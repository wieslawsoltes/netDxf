#pragma warning disable OPENAI001, MEAI001 // Pinned official Responses API and its Microsoft adapter.
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Anthropic;
using Anthropic.Exceptions;
using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using OpenAI.Responses;

namespace netDxf.Shadow.Agents;

/// <summary>Explicit, session-scoped credentials for an official provider SDK.</summary>
public sealed record ShadowAgentProviderOptions
{
    public string Provider { get; init; } = "openai";
    public string Model { get; init; } = "";
    [JsonIgnore]
    public string ApiKey { get; init; } = "";
    public string? Endpoint { get; init; }

    public override string ToString() => $"{Provider}/{Model}; Endpoint={Endpoint ?? "default"}; ApiKey=[redacted]";
}

/// <summary>Creates independent streaming clients backed by the providers' official .NET SDKs.</summary>
public static partial class ShadowAgentProviders
{
    internal const string ReasoningProviderKey = "shadow:reasoning-provider";

    internal static string? GetReasoningProvider(AIContent content) =>
        content.AdditionalProperties?.TryGetValue(ReasoningProviderKey, out var origin) == true ? origin switch
        {
            string value => value,
            JsonElement { ValueKind: JsonValueKind.String } value => value.GetString(),
            _ => null,
        } : null;

    /// <summary>
    /// Creates a client. Dispose the returned client after its session ends; an injected
    /// <paramref name="httpClient"/> remains owned by its caller. SDK retries are disabled so the
    /// harness can retry inference with one bounded policy and never replay tool mutations.
    /// </summary>
    public static IChatClient Create(ShadowAgentProviderOptions options, HttpClient? httpClient = null) =>
        CreateCore(options, httpClient, OperatingSystem.IsBrowser());

    /// <summary>
    /// Creates the browser-compatible official SDK adapters explicitly, including when testing
    /// on a native runtime. <see cref="Create"/> selects these adapters automatically on WebAssembly.
    /// Browser requests use Fetch response streaming and explicit API credentials without cookies.
    /// The configured provider endpoint must allow the application's origin through CORS.
    /// </summary>
    public static IChatClient CreateBrowser(ShadowAgentProviderOptions options, HttpClient? httpClient = null) =>
        CreateCore(options, httpClient, browser: true);

    private static IChatClient CreateCore(ShadowAgentProviderOptions options, HttpClient? httpClient, bool browser)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ApiKey);
        var provider = options.Provider.Trim().ToLowerInvariant();
        var endpoint = GetEndpoint(options.Endpoint);
        var model = string.IsNullOrWhiteSpace(options.Model) ? provider switch
        {
            "openai" or "chatgpt" => "gpt-6.1-sol",
            "anthropic" => "claude-opus-5-5",
            "google" or "gemini" => "gemini-flash-latest",
            _ => throw new ArgumentException($"Unknown agent provider '{options.Provider}'.", nameof(options)),
        } : options.Model.Trim();

        HttpMessageHandler handler = httpClient is null
            ? new HttpClientHandler()
            : new BorrowedHttpClientHandler(httpClient);
        if (browser) handler = new ProviderBrowserHandler(handler);
        var transport = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        try
        {
            IChatClient inner;
            IDisposable? sdkOwner = null;
            switch (provider)
            {
                case "openai":
                case "chatgpt":
                    var responseOptions = new ResponsesClientOptions
                    {
                        Transport = new HttpClientPipelineTransport(transport),
                        RetryPolicy = new ClientRetryPolicy(0),
                    };
                    if (endpoint is not null) responseOptions.Endpoint = endpoint;
                    if (provider == "chatgpt")
                        responseOptions.AddPolicy(new ChatGptResponsesPolicy(), PipelinePosition.BeforeTransport);
                    inner = new ResponsesClient(new ApiKeyCredential(options.ApiKey), responseOptions).AsIChatClient(model);
                    break;
                case "anthropic":
                    IAnthropicClient anthropic = browser
                        ? new AnthropicBrowserClient(options.ApiKey, endpoint?.AbsoluteUri.TrimEnd('/') ?? "https://api.anthropic.com", transport)
                        : new AnthropicClient(new Anthropic.Core.ClientOptions
                    {
                        ApiKey = options.ApiKey,
                        AuthToken = null,
                        HttpClient = transport,
                        BaseUrl = endpoint?.AbsoluteUri.TrimEnd('/') ?? "https://api.anthropic.com",
                        MaxRetries = 0,
                    });
                    inner = anthropic.AsIChatClient(model);
                    sdkOwner = anthropic;
                    break;
                case "google":
                case "gemini":
                    var google = new Google.GenAI.Client(
                        vertexAI: false,
                        apiKey: options.ApiKey,
                        httpOptions: new HttpOptions
                        {
                            BaseUrl = endpoint?.AbsoluteUri.TrimEnd('/'),
                            RetryOptions = new HttpRetryOptions { Attempts = 1 },
                        },
                        clientOptions: new ClientOptions { HttpClientFactory = () => transport });
                    inner = google.AsIChatClient(model);
                    sdkOwner = google;
                    break;
                default:
                    throw new ArgumentException($"Unknown agent provider '{options.Provider}'.", nameof(options));
            }
            return new ProviderChatClient(inner, transport, sdkOwner, provider);
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }

    /// <summary>Classifies retryable inference failures without treating authorization errors as transient.</summary>
    public static bool TryGetTransientFailure(Exception error, out TimeSpan? retryAfter)
    {
        ArgumentNullException.ThrowIfNull(error);
        retryAfter = null;
        switch (error)
        {
            case OperationCanceledException:
            case ArgumentException:
                return false;
            case ClientResultException openAI:
                if (openAI.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true && value is not null)
                    retryAfter = ParseRetryAfter(value);
                return IsTransientStatus(openAI.Status);
            case AnthropicApiException anthropic:
                if (anthropic.Data[AnthropicBrowserClient.RetryAfterKey] is string retryHeader)
                    retryAfter = ParseRetryAfter(retryHeader);
                return IsTransientStatus((int)anthropic.StatusCode);
            case AnthropicSseException streaming:
                return streaming.ErrorType is Anthropic.Models.ErrorType.RateLimitError or Anthropic.Models.ErrorType.TimeoutError
                    or Anthropic.Models.ErrorType.OverloadedError or Anthropic.Models.ErrorType.ApiError;
            case Google.GenAI.ApiException google:
                return IsTransientStatus(google.StatusCode);
            case AnthropicIOException anthropic:
                return TryGetTransientFailure(anthropic.InnerException, out retryAfter);
            case ShadowAgentProviderException stream:
                retryAfter = stream.RetryAfter;
                return stream.IsTransient;
            case HttpRequestException { StatusCode: { } status }:
                return IsTransientStatus((int)status);
            case HttpRequestException network:
                return network.HttpRequestError is HttpRequestError.Unknown or HttpRequestError.NameResolutionError
                    or HttpRequestError.ConnectionError or HttpRequestError.ResponseEnded;
            case IOException:
                return true;
            default:
                return false;
        }
    }

    private static bool IsTransientStatus(int status) => status is 408 or 429 or 500 or 502 or 503 or 504;

    private static TimeSpan? ParseRetryAfter(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && double.IsFinite(seconds) && seconds >= 0)
            return TimeSpan.FromSeconds(Math.Min(seconds, TimeSpan.MaxValue.TotalSeconds / 2));
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date)
            ? TimeSpan.FromTicks(Math.Max(0, (date - DateTimeOffset.UtcNow).Ticks)) : null;
    }

    private static Uri? GetEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint)
            || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment)
            || (endpoint.Scheme != Uri.UriSchemeHttps && !(endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)))
            throw new ArgumentException("Provider endpoints require HTTPS, or HTTP on loopback, without credentials, query or fragment.", nameof(value));
        return endpoint;
    }

    private sealed class ProviderBrowserHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Same public HttpRequestMessage option contract as Blazor's
            // SetBrowserResponseStreamingEnabled / SetBrowserRequestOption extensions.
            // Keeping the package independent of ASP.NET also supports non-Blazor WASM hosts.
            request.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);
            request.Options.Set(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["credentials"] = "omit", ["mode"] = "cors", ["cache"] = "no-store",
                });
            return base.SendAsync(request, cancellationToken);
        }
    }

    // An SDK can dispose its private transport without disposing a caller's shared HttpClient.
    private sealed class BorrowedHttpClientHandler(HttpClient client) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var forwarded = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content,
                Version = request.Version,
                VersionPolicy = request.VersionPolicy,
            };
            foreach (var header in request.Headers) forwarded.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var option in request.Options) forwarded.Options.Set(new HttpRequestOptionsKey<object?>(option.Key), option.Value);
            return await client.SendAsync(forwarded, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class ProviderChatClient(IChatClient inner, HttpClient transport, IDisposable? sdkOwner, string provider)
        : DelegatingChatClient(inner)
    {
        private int _disposed;
        private readonly string reasoningProvider = provider == "gemini" ? "google" : provider;

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken).ConfigureAwait(false);

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var configured = options?.Clone() ?? new ChatOptions();
            var originalFactory = configured.RawRepresentationFactory;
            if (provider is "openai" or "chatgpt")
            {
                configured.ConversationId = null;
                configured.RawRepresentationFactory = client =>
                {
                    var raw = originalFactory?.Invoke(client) as CreateResponseOptions ?? new CreateResponseOptions();
                    raw.StoredOutputEnabled = false;
                    raw.PreviousResponseId = null;
                    return raw;
                };
            }
            else if (provider is "google" or "gemini")
            {
                configured.RawRepresentationFactory = client =>
                {
                    var raw = originalFactory?.Invoke(client) as GenerateContentConfig ?? new GenerateContentConfig();
                    // The harness owns bounded continuation and cancellation; never create invisible billed retries.
                    raw.AutomaticContinuation = false;
                    return raw;
                };
            }

            var completed = false;
            await foreach (var update in InnerClient.GetStreamingResponseAsync(CompatibleHistory(messages), configured, cancellationToken).ConfigureAwait(false))
            {
                if (update.RawRepresentation is StreamingResponseFailedUpdate failed)
                    throw new ShadowAgentProviderException(failed.Response.Error?.Code.ToString() ?? "response_failed",
                        failed.Response.Error?.Message ?? "The provider failed the response.");
                if (update.RawRepresentation is StreamingResponseIncompleteUpdate)
                    throw new ShadowAgentProviderException("response_incomplete", "The provider returned an incomplete response.");
                foreach (var error in update.Contents.OfType<ErrorContent>())
                    throw new ShadowAgentProviderException(error.ErrorCode ?? "provider_error", error.Message ?? "The provider reported a streaming error.");
                // Gemini can omit call IDs. Give each completed call a stable session ID before
                // the harness checkpoints it or correlates its result. The following protected
                // reasoning content retains the SDK's original thought signature on replay.
                for (var i = 0; i < update.Contents.Count; i++)
                {
                    if (update.Contents[i] is TextReasoningContent reasoning)
                        (reasoning.AdditionalProperties ??= new())[ReasoningProviderKey] = reasoningProvider;
                    if (update.Contents[i] is FunctionCallContent call && string.IsNullOrWhiteSpace(call.CallId))
                        update.Contents[i] = new FunctionCallContent("call_" + Guid.NewGuid().ToString("N"), call.Name, call.Arguments)
                        { AdditionalProperties = call.AdditionalProperties };
                }
                // Anthropic reports its stop reason in message_delta before message_stop.
                // Losing the connection between those events must not commit a partial turn.
                completed |= provider == "anthropic"
                    ? update.RawRepresentation is Anthropic.Models.Messages.RawMessageStreamEvent
                        { Value: Anthropic.Models.Messages.RawMessageStopEvent }
                    : update.FinishReason is not null;
                yield return update;
            }
            if (!completed)
                throw new ShadowAgentProviderException("stream_interrupted", "The provider stream ended without a terminal completion event.", isTransient: true);
        }

        private IEnumerable<ChatMessage> CompatibleHistory(IEnumerable<ChatMessage> messages)
        {
            foreach (var message in messages)
            {
                // ProtectedData is not a portable wire format: the adapters interpret it as
                // OpenAI encrypted reasoning, an Anthropic signature, or base64 Gemini bytes.
                // Retain it only for its original provider. Old untagged reasoning cannot be
                // safely attributed; omit it from inference without changing the checkpoint.
                // Visible assistant text and complete function call/result pairs stay intact.
                bool Compatible(AIContent content) => content is not TextReasoningContent reasoning ||
                    string.Equals(GetReasoningProvider(reasoning), reasoningProvider, StringComparison.Ordinal);
                if (message.Contents.All(Compatible)) { yield return message; continue; }
                var retained = message.Contents.Where(Compatible).ToList();
                if (retained.Count == 0) continue;
                var copy = message.Clone();
                copy.Contents = retained;
                // A raw SDK message may still contain the reasoning that was just removed.
                copy.RawRepresentation = null;
                yield return copy;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                base.Dispose(disposing);
                sdkOwner?.Dispose();
                transport.Dispose();
            }
        }
    }
}

/// <summary>An explicit streaming failure, distinct from a successfully completed assistant turn.</summary>
public sealed class ShadowAgentProviderException : Exception
{
    public ShadowAgentProviderException(string code, string message, bool isTransient = false, TimeSpan? retryAfter = null) : base(message)
    {
        Code = code;
        IsTransient = isTransient || code is "rate_limit_exceeded" or "rate_limit_error" or "server_error" or "overloaded_error";
        RetryAfter = retryAfter;
    }

    public string Code { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }
}
