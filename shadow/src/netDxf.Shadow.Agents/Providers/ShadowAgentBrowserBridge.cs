using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace netDxf.Shadow.Agents;

public static partial class ShadowAgentProviders
{
    /// <summary>Uses the native companion's official SDK client while retaining the harness and tool execution in the browser.</summary>
    /// <remarks>The bridge token authenticates the companion; the provider API key is sent to that explicitly selected host for this inference request only.</remarks>
    public static IChatClient CreateBrowserBridge(ShadowAgentProviderOptions options, string endpoint, string token, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (Encoding.UTF8.GetByteCount(token) < 32) throw new ArgumentException("Supply the companion's bridge token.", nameof(token));
        var address = GetEndpoint(endpoint) ?? throw new ArgumentException("Supply a companion endpoint.", nameof(endpoint));
        _ = GetEndpoint(options.Endpoint); // Preserve explicitly configured HTTPS/loopback SDK endpoints.
        if (options.Provider.Trim().ToLowerInvariant() is not ("openai" or "anthropic" or "google" or "gemini"))
            throw new ArgumentException("The API-key companion bridge supports OpenAI, Anthropic and Google; use the separate ChatGPT sign-in bridge for subscriptions.", nameof(options));
        return new CompanionChatClient(options with { PromptCacheKey = options.PromptCacheKey ?? "shadow-" + Guid.NewGuid().ToString("N") },
            new Uri(address.AbsoluteUri.TrimEnd('/') + "/providers/stream"), token, httpClient);
    }

    private sealed class CompanionChatClient(ShadowAgentProviderOptions configuration, Uri endpoint, string bridgeToken, HttpClient? supplied) : IChatClient
    {
        private readonly HttpClient _http = supplied ?? new() { Timeout = Timeout.InfiniteTimeSpan };
        private int _disposed;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var requestBody = ShadowAgentBridgeRequest.Create(configuration, messages, options);
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(requestBody, options: ShadowAgentBridgeRequest.JsonOptions),
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bridgeToken);
            request.Headers.Accept.Add(new("application/x-ndjson"));
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The companion rejected provider inference (HTTP {(int)response.StatusCode}). Check its bridge token and allowed origin.", null, response.StatusCode);
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192);
            await foreach (var line in ReadLinesAsync(reader, cancellationToken).ConfigureAwait(false))
            {
                var frame = JsonSerializer.Deserialize<ShadowAgentBridgeFrame>(line, ShadowAgentBridgeRequest.JsonOptions)
                    ?? throw new ShadowAgentProviderException("bridge_protocol", "The companion returned an empty inference frame.");
                switch (frame.Type)
                {
                    case "update" when frame.Update is { } update: yield return update; break;
                    case "completed": yield break;
                    case "error": throw new ShadowAgentProviderException(frame.Code ?? "bridge_provider_error",
                        frame.Message ?? "The companion's provider request failed.", frame.IsTransient, frame.RetryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600)) : null);
                    default: throw new ShadowAgentProviderException("bridge_protocol", "The companion returned an unsupported inference frame.");
                }
            }
            throw new ShadowAgentProviderException("stream_interrupted", "The companion stream ended without a terminal completion event.", isTransient: true);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => serviceKey is null && (serviceType == typeof(IChatClient) || serviceType == GetType()) ? this : null;
        public void Dispose() { if (Interlocked.Exchange(ref _disposed, 1) == 0 && supplied is null) _http.Dispose(); }

        private static async IAsyncEnumerable<string> ReadLinesAsync(StreamReader reader, [EnumeratorCancellation] CancellationToken token)
        {
            var buffer = new char[8192]; var line = new StringBuilder(); int length;
            while ((length = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) != 0)
                for (var i = 0; i < length; i++)
                {
                    if (buffer[i] == '\n')
                    {
                        if (line.Length > 0) { yield return line.ToString(); line.Clear(); }
                    }
                    else
                    {
                        if (line.Length >= 4 * 1024 * 1024) throw new ShadowAgentProviderException("bridge_frame_too_large", "The companion exceeded the inference frame limit.");
                        if (buffer[i] != '\r') line.Append(buffer[i]);
                    }
                }
            if (line.Length > 0) throw new ShadowAgentProviderException("stream_interrupted", "The companion ended in the middle of an inference frame.", isTransient: true);
        }
    }
}

/// <summary>Typed, stateless bridge request. Its API key is a transport credential, never a saved session setting.</summary>
public sealed class ShadowAgentBridgeRequest
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    public string Provider { get; init; } = "anthropic";
    public string Model { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public string? Endpoint { get; init; }
    public bool OptimizePromptCaching { get; init; } = true;
    public string? PromptCacheKey { get; init; }
    public IReadOnlyList<ChatMessage> Messages { get; init; } = [];
    public ChatOptions Options { get; init; } = new();
    public IReadOnlyList<ShadowAgentBridgeTool> Tools { get; init; } = [];
    public override string ToString() => $"ShadowAgentBridgeRequest {{ Provider={Provider}, Model={Model}, ApiKey=[redacted] }}";

    public static ShadowAgentBridgeRequest Create(ShadowAgentProviderOptions configuration, IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var copied = options?.Clone() ?? new();
        if (copied.RawRepresentationFactory is not null || copied.AllowBackgroundResponses == true || copied.ContinuationToken is not null)
            throw new ArgumentException("The stateless companion bridge supports portable ChatOptions; native provider factories and background continuation require a native IChatClient.");
        var tools = copied.Tools?.Select(tool => tool is AIFunctionDeclaration function
            ? new ShadowAgentBridgeTool(function.Name, function.Description, function.JsonSchema.Clone())
            : throw new ArgumentException("The companion bridge advertises local function declarations; tool execution stays in the browser.")).ToArray() ?? [];
        copied.Tools = null;
        var request = new ShadowAgentBridgeRequest
        {
            Provider = configuration.Provider.Trim().ToLowerInvariant(), Model = copied.ModelId ?? configuration.Model,
            ApiKey = configuration.ApiKey, Endpoint = configuration.Endpoint, Messages = messages.ToArray(), Options = copied, Tools = tools,
            OptimizePromptCaching = configuration.OptimizePromptCaching, PromptCacheKey = configuration.PromptCacheKey,
        };
        request.Validate();
        return request;
    }

    public void Validate()
    {
        if (Provider is not ("openai" or "anthropic" or "google" or "gemini") || ApiKey is not { Length: > 0 and <= 8192 }
            || Model is null || Model.Length > 256 || Options is null || Messages is null || Messages.Count is < 1 or > 20000
            || Tools is null || Tools.Count > 512)
            throw new ArgumentException("Invalid provider, credential, model, message or tool budget in the companion request.");
        if (PromptCacheKey is { } key && (key.Length is < 1 or > 64 || key.Any(char.IsControl)))
            throw new ArgumentException("A prompt cache key requires 1–64 non-control characters.");
        if (Options.RawRepresentationFactory is not null || Options.AllowBackgroundResponses == true || Options.ContinuationToken is not null)
            throw new ArgumentException("The companion accepts stateless portable inference options.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in Tools)
            if (tool.Name is not { Length: > 0 and <= 128 } || tool.Name.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-' and not '.')
                || !names.Add(tool.Name) || tool.Description?.Length > 4096 || tool.InputSchema.ValueKind != JsonValueKind.Object
                || !tool.InputSchema.TryGetProperty("type", out var type) || type.GetString() != "object")
                throw new ArgumentException("Invalid local function declaration in the companion request.");
    }

    public ChatOptions CreateOptions()
    {
        Validate();
        var options = Options.Clone();
        options.ModelId = string.IsNullOrWhiteSpace(Model) ? options.ModelId : Model;
        options.Tools = Tools.Select(tool => (AITool)new BridgeFunction(tool)).ToList();
        return options;
    }

    private sealed class BridgeFunction(ShadowAgentBridgeTool tool) : AIFunctionDeclaration
    {
        public override IReadOnlyDictionary<string, object?> AdditionalProperties { get; } = new Dictionary<string, object?> { ["strict"] = false };
        public override string Name => tool.Name;
        public override string Description => tool.Description ?? "";
        public override JsonElement JsonSchema => tool.InputSchema;
    }
}

public sealed record ShadowAgentBridgeTool(string Name, string? Description, JsonElement InputSchema);
public sealed record ShadowAgentBridgeFrame(string Type, ChatResponseUpdate? Update = null, string? Code = null,
    string? Message = null, bool IsTransient = false, double? RetryAfterSeconds = null);
