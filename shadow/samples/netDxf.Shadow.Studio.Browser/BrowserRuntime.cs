using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.JSInterop;
using netDxf.Shadow.Rendering;
using global::SkiaSharp;

namespace netDxf.Shadow.Studio.Browser;

/// <summary>Only the transport crosses JS interop. Drawing objects never leave .NET.</summary>
public static partial class BrowserRuntime
{
    private static BrowserApplication? application;
    internal static void Initialize(BrowserApplication value, IJSRuntime javascript) { application = value; value.InitializeAgents(javascript); }
    private static BrowserApplication App => application ?? throw new InvalidOperationException("The browser engine is not initialized.");
    [JSInvokable] public static BrowserReply Dispatch(BrowserRequest request, byte[]? upload) => App.Dispatch(request, upload);
    [JSInvokable] public static string Subscribe(string? lastEventId) => App.Subscribe(lastEventId);
    [JSInvokable] public static Task<BrowserNotice?> ReadEvent(string id) => App.ReadEvent(id);
    [JSInvokable] public static void Unsubscribe(string id) => App.Unsubscribe(id);
    [JSInvokable] public static void Shutdown() { application?.Dispose(); application = null; }
}

public sealed record BrowserRequest(string Method, string Path, Dictionary<string, string> Headers, JsonElement? Body);
public sealed record BrowserReply(int Status, string ContentType, string? Json, byte[]? Bytes)
{
    internal static readonly JsonSerializerOptions Serializer = StudioJson.CreateOptions();
    private static readonly BrowserReply NullValue = new(200, "application/json", "null", null);
    // The byte[] interop channel copies UTF-8 directly. Embedding a large JSON string
    // inside the interop envelope would escape it again, then re-encode it in Response.
    public static BrowserReply Value(object? value) => value is null ? NullValue :
        new(200, "application/json", null, JsonSerializer.SerializeToUtf8Bytes(value, Serializer));
    public static BrowserReply Binary(byte[] value, string type) => new(200, type, null, value);
    public static BrowserReply Error(int status, string message) =>
        new(status, "application/json", null, JsonSerializer.SerializeToUtf8Bytes(new { error = message }, Serializer));
}
public sealed record BrowserNotice(string EventId, string Data);

internal sealed partial class BrowserApplication : IDisposable
{
    private readonly Studio studio;
    private readonly StudioDocumentHub documents;
    private readonly Dictionary<string, StudioSubscription> subscriptions = new(StringComparer.Ordinal);
    private readonly List<Route> routes = [];
    private bool disposed;
    private sealed record Route(string Method, string Pattern, string[] Segments, Func<Request, BrowserReply> Execute);

    private BrowserApplication(byte[][] fonts)
    {
        Studio Create(bool demo)
        {
            var result = new Studio(demo);
            result.ConfigureBrowserFonts(style =>
            {
                var index = (style.Bold ? 1 : 0) | (style.Italic ? 2 : 0);
                using var data = SKData.CreateCopy(fonts[index]);
                return SKTypeface.FromData(data) ?? throw new InvalidOperationException("The bundled browser typeface could not be loaded.");
            });
            return result;
        }
        studio = Create(true);
        documents = new(studio, Create);
        MapRoutes();
    }
    public static async Task<BrowserApplication> CreateAsync(HttpClient client)
    {
        string[] names = ["Regular", "Bold", "Italic", "BoldItalic"];
        var fonts = await Task.WhenAll(names.Select(name => client.GetByteArrayAsync($"fonts/NotoSans-{name}.ttf")));
        if (fonts.Any(bytes => bytes.Length < 1000 || bytes.Length > 4 * 1024 * 1024))
            throw new InvalidOperationException("Browser font assets are missing or invalid. Publish the complete wwwroot directory.");
        return new(fonts);
    }
    public BrowserReply Dispatch(BrowserRequest request, byte[]? upload)
    {
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ArgumentNullException.ThrowIfNull(request);
            var context = new Request(request, upload);
            if (context.Method != "GET" && context.Header("X-Shadow-Studio") != "1")
                return BrowserReply.Error(403, "Missing Studio request marker.");
            var segments = context.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var pathExists = false;
            foreach (var route in routes)
            {
                if (route.Segments.Length != segments.Length) continue;
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                var matches = true;
                for (var i = 0; i < segments.Length; i++)
                {
                    var part = route.Segments[i];
                    if (part.StartsWith('{') && part.EndsWith('}')) values[part[1..^1]] = Uri.UnescapeDataString(segments[i]);
                    else if (!string.Equals(part, segments[i], StringComparison.Ordinal)) { matches = false; break; }
                }
                if (!matches) continue;
                pathExists = true;
                if (route.Method != context.Method) continue;
                context.Parameters = values;
                return route.Execute(context);
            }
            return BrowserReply.Error(pathExists ? 405 : 404, "Unknown Studio operation.");
        }
        catch (ShadowConflictException error) { return BrowserReply.Error(409, error.Message); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException or FormatException or JsonException)
        { return BrowserReply.Error(400, error.Message); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return BrowserReply.Error(500, $"{error.GetType().Name}: {error.Message}");
        }
    }
    public string Subscribe(string? lastEventId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var id = Guid.NewGuid().ToString("N");
        subscriptions.Add(id, studio.Subscribe(lastEventId));
        return id;
    }
    public async Task<BrowserNotice?> ReadEvent(string id)
    {
        if (!subscriptions.TryGetValue(id, out var subscription)) return null;
        try
        {
            var notice = await subscription.Reader.ReadAsync();
            return new(notice.EventId, JsonSerializer.Serialize(notice, BrowserReply.Serializer));
        }
        catch (ChannelClosedException) { return null; }
    }
    public void Unsubscribe(string id)
    {
        if (subscriptions.Remove(id, out var subscription)) subscription.Dispose();
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var subscription in subscriptions.Values) subscription.Dispose();
        subscriptions.Clear(); agents?.Cancel();
        if (agents is not null) _ = agents.DisposeAsync();
        documents.Dispose(); studio.Dispose();
    }
    private void Map(string method, string pattern, Func<Request, BrowserReply> action) =>
        routes.Add(new(method, pattern, pattern.Split('/', StringSplitOptions.RemoveEmptyEntries), action));
    private void Legacy(string method, string pattern, Func<Studio, Request, object?> action) =>
        Map(method, pattern, request => studio.ExecuteRequest(request.Identity(), () => BrowserReply.Value(action(studio, request))));
    private void Cad(string method, string pattern, Func<Studio, Request, object?> action) =>
        Map(method, pattern, request => documents.Use(request.Parameter("id"), s => s.ExecuteRequest(request.Identity(), () => BrowserReply.Value(action(s, request)))));

    private sealed class Request
    {
        private readonly BrowserRequest input;
        private readonly byte[]? upload;
        private readonly Dictionary<string, string> query = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Parameters { get; set; } = new(StringComparer.Ordinal);
        public string Method { get; }
        public string Path { get; }
        public Request(BrowserRequest input, byte[]? upload)
        {
            this.input = input; this.upload = upload;
            if (input.Path is null || input.Path.Length > 16384 || !input.Path.StartsWith("/api/", StringComparison.Ordinal))
                throw new ArgumentException("Invalid Studio request path.");
            Method = input.Method.ToUpperInvariant();
            var parts = input.Path.Split('?', 2); Path = parts[0];
            if (parts.Length == 2)
                foreach (var pair in parts[1].Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var fields = pair.Split('=', 2);
                    static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
                    if (!query.TryAdd(Decode(fields[0]), fields.Length == 2 ? Decode(fields[1]) : ""))
                        throw new ArgumentException("Duplicate query parameters are not allowed.");
                }
        }
        public string Header(string name) => input.Headers?.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value ?? "";
        public string Parameter(string name) => Parameters[name];
        public string? Optional(string name) => query.TryGetValue(name, out var value) ? value : null;
        public string Text(string name) => Optional(name) ?? throw new ArgumentException($"Missing {name}.");
        public long Long(string name) => long.TryParse(Text(name), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : throw new ArgumentException($"Invalid {name}.");
        public long? OptionalLong(string name) => Optional(name) is null ? null : Long(name);
        public int Int(string name) => checked((int)Long(name));
        public double Double(string name) => double.TryParse(Text(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : throw new ArgumentException($"Invalid {name}.");
        public double? OptionalDouble(string name) => Optional(name) is null ? null : Double(name);
        public bool Boolean(string name, bool fallback = false) => Optional(name) is null ? fallback : bool.TryParse(Text(name), out var value) ? value : throw new ArgumentException($"Invalid {name}.");
        public T Body<T>() => input.Body is { } body ? body.Deserialize<T>(BrowserReply.Serializer) ?? throw new ArgumentException("Missing request body.") : throw new ArgumentException("Missing request body.");
        public byte[] Bytes(int maximum)
        {
            var bytes = upload ?? throw new ArgumentException("Missing upload bytes.");
            if (bytes.Length > maximum) throw new ArgumentException("Upload size limit exceeded.");
            return bytes;
        }
        public StudioRequestIdentity Identity()
        {
            if (!Guid.TryParseExact(Header("X-Shadow-Document"), "D", out var document)
                || !long.TryParse(Header("X-Shadow-View"), NumberStyles.None, CultureInfo.InvariantCulture, out var view)
                || Header("X-Shadow-Workspace") is not { Length: > 0 } workspace || workspace.Contains(','))
                throw new ShadowConflictException("Fetch the current state and supply its document, workspace and view identity.");
            return new(document, workspace, view);
        }
    }
}
