using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using Anthropic.Services;

namespace netDxf.Shadow.Agents;

/// <summary>
/// Fetch transport for the official SDK's public client interfaces. The official message
/// services, request models, SSE parser, error types and IChatClient adapter still implement
/// the API contract. Only transport dispatch lives here: the SDK's default desktop transport
/// eagerly configures HttpClientHandler.AutomaticDecompression, which WebAssembly does not support.
/// </summary>
internal sealed class AnthropicBrowserClient : IAnthropicClient, IAnthropicClientWithRawResponse
{
    internal const string RetryAfterKey = "netDxf.Shadow.RetryAfter";
    private const int MaxErrorBytes = 64 * 1024;
    private ClientOptions _options;
    private readonly TransportLifetime _lifetime;
    private IMessageService? _messages;
    private IMessageServiceWithRawResponse? _rawMessages;

    public AnthropicBrowserClient(string apiKey, string endpoint, HttpClient httpClient)
    {
        // ClientOptions is a public record struct. Construct its ordinary default value and
        // assign the public settings the SDK's message services consume. No private state,
        // reflection, uninitialized-object API or modified provider assembly is involved.
        // Its desktop constructor and private telemetry state are never used by this transport.
        _options = default;
        _options.HttpClient = httpClient;
        _options.Handlers = [];
        _options.BaseUrl = endpoint;
        _options.ApiKey = apiKey;
        _options.AuthToken = null;
        _options.WebhookKey = null;
        _options.MaxRetries = 0;
        _options.OpenTelemetry = false;
        _lifetime = new(httpClient);
    }

    private AnthropicBrowserClient(ClientOptions options, TransportLifetime lifetime)
    {
        if (!ReferenceEquals(options.HttpClient, lifetime.HttpClient) || options.Handlers.Count != 0)
            throw new ArgumentException("Inject a configured HttpClient when creating the browser provider instead of changing its transport with WithOptions.");
        if (options.MaxRetries is not 0 || options.Credentials is not null)
            throw new ArgumentException("Browser agent inference uses explicit API credentials and harness-managed retries.");
        _options = options;
        _lifetime = lifetime;
    }

    public HttpClient HttpClient { get => _options.HttpClient; init => _options.HttpClient = value; }
    public IReadOnlyList<DelegatingHandler> Handlers { get => _options.Handlers; init => _options.Handlers = value; }
    public string BaseUrl { get => _options.BaseUrl; init => _options.BaseUrl = value; }
    public bool ResponseValidation { get => _options.ResponseValidation; init => _options.ResponseValidation = value; }
    public int? MaxRetries { get => _options.MaxRetries; init => _options.MaxRetries = value; }
    public TimeSpan? Timeout { get => _options.Timeout; init => _options.Timeout = value; }
    public OpenTelemetryOptions? OpenTelemetry { get => _options.OpenTelemetry; init => _options.OpenTelemetry = value; }
    public string? ApiKey { get => _options.ApiKey; init => _options.ApiKey = value; }
    public string? AuthToken { get => _options.AuthToken; init => _options.AuthToken = value; }
    public string? WebhookKey { get => _options.WebhookKey; init => _options.WebhookKey = value; }
    public IAnthropicClientWithRawResponse WithRawResponse => this;
    public IAnthropicClient WithOptions(Func<ClientOptions, ClientOptions> modifier) =>
        new AnthropicBrowserClient(modifier(_options), _lifetime);
    IAnthropicClientWithRawResponse IAnthropicClientWithRawResponse.WithOptions(Func<ClientOptions, ClientOptions> modifier) =>
        new AnthropicBrowserClient(modifier(_options), _lifetime);

    public IMessageService Messages => _messages ??= new MessageService(this);
    IMessageServiceWithRawResponse IAnthropicClientWithRawResponse.Messages => _rawMessages ??= new MessageServiceWithRawResponse(this);

    // This private adapter exposes Messages inference and token counting to the harness.
    // Other Anthropic resources have their own SDK clients and are not agent tools.
    public IModelService Models => throw UnsupportedResource();
    public IFileService Files => throw UnsupportedResource();
    public ISkillService Skills => throw UnsupportedResource();
    public IOrganizationService Organization => throw UnsupportedResource();
    public IBetaService Beta => throw UnsupportedResource();
    IModelServiceWithRawResponse IAnthropicClientWithRawResponse.Models => throw UnsupportedResource();
    IFileServiceWithRawResponse IAnthropicClientWithRawResponse.Files => throw UnsupportedResource();
    ISkillServiceWithRawResponse IAnthropicClientWithRawResponse.Skills => throw UnsupportedResource();
    IOrganizationServiceWithRawResponse IAnthropicClientWithRawResponse.Organization => throw UnsupportedResource();
    IBetaServiceWithRawResponse IAnthropicClientWithRawResponse.Beta => throw UnsupportedResource();

    private static NotSupportedException UnsupportedResource() =>
        new("The browser agent transport exposes Anthropic Messages inference and token counting.");

    public async Task<HttpResponse> Execute<T>(HttpRequest<T> request, CancellationToken cancellationToken = default)
        where T : ParamsBase
    {
        ObjectDisposedException.ThrowIf(_lifetime.IsDisposed, this);
        var body = request.Params switch
        {
            MessageCreateParams messages => messages.RawBodyData,
            MessageCountTokensParams tokens => tokens.RawBodyData,
            _ => throw UnsupportedResource(),
        };
        using var message = new HttpRequestMessage(request.Method, request.Params.Url(_options))
        {
            // RawBodyData already contains the official SDK's serialized JsonElement values,
            // including tool schemas, signatures and continuation. Never transform that body.
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body)),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        foreach (var header in request.Params.RawHeaderData)
        {
            if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) continue;
            var value = header.Value.ValueKind == JsonValueKind.Array
                ? string.Join(",", header.Value.EnumerateArray().Select(item => item.ToString()))
                : header.Value.ToString();
            message.Headers.TryAddWithoutValidation(header.Key, value);
        }
        message.Headers.Remove("X-Api-Key");
        message.Headers.Remove("Authorization");
        message.Headers.TryAddWithoutValidation("x-api-key", ApiKey);
        if (!message.Headers.Contains("anthropic-version")) message.Headers.Add("anthropic-version", "2023-06-01");
        message.Headers.Remove("anthropic-dangerous-direct-browser-access");
        message.Headers.Add("anthropic-dangerous-direct-browser-access", "true");
        message.Headers.TryAddWithoutValidation("x-stainless-lang", "csharp");
        message.Headers.TryAddWithoutValidation("x-stainless-runtime", "browser");
        message.Headers.TryAddWithoutValidation("x-stainless-retry-count", "0");

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var timeout = Timeout ?? ClientOptions.DefaultTimeout;
        if (timeout != System.Threading.Timeout.InfiniteTimeSpan) lifetime.CancelAfter(timeout);
        HttpResponseMessage? response = null;
        try
        {
            response = await HttpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, lifetime.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var error = AnthropicExceptionFactory.CreateApiException(response.StatusCode,
                    await ReadBoundedErrorAsync(response.Content, lifetime.Token).ConfigureAwait(false));
                if (response.Headers.TryGetValues("Retry-After", out var values))
                    error.Data[RetryAfterKey] = values.FirstOrDefault();
                throw error;
            }
            // The timer and cancellation registrations stay alive while the official SDK reads
            // the stream, and are released with its response. A headers-only lifetime would
            // silently stop enforcing the SDK timeout after the first response byte.
            response.Content = new LifetimeContent(response.Content, lifetime);
            return new HttpResponse { RawMessage = response, CancellationToken = lifetime.Token };
        }
        catch (HttpRequestException error)
        {
            response?.Dispose();
            lifetime.Dispose();
            throw new AnthropicIOException("I/O exception in the browser provider transport.", error);
        }
        catch
        {
            response?.Dispose();
            lifetime.Dispose();
            throw;
        }
    }

    private static async Task<string> ReadBoundedErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaxErrorBytes + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            length += read;
        }
        return Encoding.UTF8.GetString(buffer, 0, Math.Min(length, MaxErrorBytes))
            + (length > MaxErrorBytes ? "\n[Provider error body truncated.]" : "");
    }

    public void Dispose() => _lifetime.Dispose();

    private sealed class TransportLifetime(HttpClient httpClient) : IDisposable
    {
        private readonly CancellationTokenSource _disposed = new();
        private int _isDisposed;
        public HttpClient HttpClient { get; } = httpClient;
        public CancellationToken Token => _disposed.Token;
        public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0) return;
            _disposed.Cancel();
            HttpClient.Dispose();
            _disposed.Dispose();
        }
    }

    private sealed class LifetimeContent : HttpContent
    {
        private readonly HttpContent _inner;
        private readonly CancellationTokenSource _lifetime;
        public LifetimeContent(HttpContent inner, CancellationTokenSource lifetime)
        {
            _inner = inner;
            _lifetime = lifetime;
            foreach (var header in inner.Headers) Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _inner.CopyToAsync(stream, _lifetime.Token);
        protected override Task<Stream> CreateContentReadStreamAsync() => _inner.ReadAsStreamAsync(_lifetime.Token);
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
            _inner.ReadAsStreamAsync(cancellationToken);
        protected override bool TryComputeLength(out long length)
        {
            length = _inner.Headers.ContentLength ?? 0;
            return _inner.Headers.ContentLength.HasValue;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { _inner.Dispose(); _lifetime.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
