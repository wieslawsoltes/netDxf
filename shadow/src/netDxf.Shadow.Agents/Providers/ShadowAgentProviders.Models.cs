using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace netDxf.Shadow.Agents;

public static partial class ShadowAgentProviders
{
    /// <summary>
    /// Lists models using explicit credentials without changing a session or performing inference.
    /// Default transports refuse redirects, cookies and HTTP caches. Injected clients remain caller-owned
    /// and must themselves disable redirects and credential-bearing default headers.
    /// </summary>
    public static Task<AgentModelDiscoveryResult> DiscoverModelsAsync(AgentModelDiscoveryRequest request,
        HttpClient? httpClient = null, AgentModelDiscoveryLimits? limits = null, CancellationToken cancellationToken = default) =>
        DiscoverModelsCoreAsync(request, httpClient, limits ?? new(), OperatingSystem.IsBrowser(), cancellationToken);

    /// <summary>Explicit browser variant for hosts and credential-free transport verification.</summary>
    public static Task<AgentModelDiscoveryResult> DiscoverBrowserModelsAsync(AgentModelDiscoveryRequest request,
        HttpClient? httpClient = null, AgentModelDiscoveryLimits? limits = null, CancellationToken cancellationToken = default) =>
        DiscoverModelsCoreAsync(request, httpClient, limits ?? new(), true, cancellationToken);

    private static async Task<AgentModelDiscoveryResult> DiscoverModelsCoreAsync(AgentModelDiscoveryRequest options,
        HttpClient? httpClient, AgentModelDiscoveryLimits limits, bool browser, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var provider = options.Provider?.Trim().ToLowerInvariant() ?? "";
        if (provider == "gemini") provider = "google";
        if (provider is not ("openai" or "anthropic" or "google" or "chatgpt" or "codex"))
            throw new ArgumentException("Unsupported model discovery provider.");
        if (limits.MaximumPages is < 1 or > 20 || limits.MaximumModels is < 1 or > 5000
            || limits.MaximumResponseBytes is < 256 or > 4_194_304
            || limits.Timeout < TimeSpan.FromMilliseconds(1) || limits.Timeout > TimeSpan.FromMinutes(1))
            throw new ArgumentException("Model discovery limits are outside the supported bounds.");
        if (provider == "codex") return new(provider, "unavailable", [],
            "Use the connected Codex app-server model list in Local agents, or enter a model manually.", false);
        if (provider == "chatgpt" && string.IsNullOrWhiteSpace(options.Endpoint)) return new(provider, "unavailable", [],
            "ChatGPT model discovery requires an explicitly connected local companion. Enter a model manually otherwise.", false);
        var endpoint = GetEndpoint(options.Endpoint);
        if (provider == "chatgpt" && endpoint?.IsLoopback != true)
            throw new ArgumentException("ChatGPT model discovery requires the local companion endpoint.");
        if (string.IsNullOrWhiteSpace(options.ApiKey) || options.ApiKey.Length > 8192 || options.ApiKey.Any(char.IsControl))
            throw new ArgumentException("Model discovery requires a valid explicit credential.");
        var baseUri = endpoint?.AbsoluteUri.TrimEnd('/') ?? provider switch
        {
            "openai" => "https://api.openai.com/v1",
            "anthropic" => "https://api.anthropic.com",
            "google" => "https://generativelanguage.googleapis.com",
            _ => throw new ArgumentException("Missing model discovery endpoint."),
        };
        if (provider == "chatgpt" && !baseUri.EndsWith("/v1", StringComparison.Ordinal)) baseUri += "/v1";
        var path = provider switch { "anthropic" => "/v1/models?limit=1000", "google" => "/v1beta/models?pageSize=1000", _ => "/models" };
        // Exact public OpenAI endpoint only: compatible servers may assign different semantics to identical IDs.
        var officialOpenAi = provider == "openai" && baseUri == "https://api.openai.com/v1";
        HttpMessageHandler handler = httpClient is null
            ? browser ? new HttpClientHandler() : new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }
            : new BorrowedHttpClientHandler(httpClient);
        using var transport = new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.Timeout);
        try
        {
            var models = new List<AgentDiscoveredModel>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            string? cursor = null;
            var truncated = false;
            for (var page = 0; page < limits.MaximumPages; page++)
            {
                var url = baseUri + path + (cursor is null ? "" : (provider == "anthropic" ? "&after_id=" : "&pageToken=") + Uri.EscapeDataString(cursor));
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
                if (provider == "anthropic")
                {
                    request.Headers.Add("x-api-key", options.ApiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    if (browser) request.Headers.Add("anthropic-dangerous-direct-browser-access", "true");
                }
                else if (provider == "google") request.Headers.Add("x-goog-api-key", options.ApiKey);
                else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                if (browser)
                {
                    request.Options.Set(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), true);
                    request.Options.Set(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"),
                        new Dictionary<string, object> { ["credentials"] = "omit", ["mode"] = "cors", ["cache"] = "no-store", ["redirect"] = "error" });
                }
                using var response = await transport.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
                if ((int)response.StatusCode is >= 300 and < 400 || response.RequestMessage?.RequestUri is { } actual && actual != request.RequestUri)
                    throw ModelDiscoveryError("Model discovery refused a redirected response.");
                if (!response.IsSuccessStatusCode)
                    throw ModelDiscoveryError(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                        ? "Model discovery authorization failed. Check the configured credential and provider access."
                        : "The provider could not list models. Check its endpoint and try again.");
                using var document = await ReadModelPageAsync(response, limits.MaximumResponseBytes, deadline.Token).ConfigureAwait(false);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty(provider == "google" ? "models" : "data", out var data) || data.ValueKind != JsonValueKind.Array)
                    throw ModelDiscoveryError("The provider returned an invalid model catalogue.");
                foreach (var item in data.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) throw ModelDiscoveryError("The provider returned an invalid model catalogue.");
                    var id = ModelText(item, provider == "google" ? "name" : "id", 256, options.ApiKey);
                    if (string.IsNullOrWhiteSpace(id)) throw ModelDiscoveryError("The provider returned an invalid model identifier.");
                    if (provider == "google" && id.StartsWith("models/", StringComparison.Ordinal)) id = id[7..];
                    if (string.IsNullOrWhiteSpace(id)) throw ModelDiscoveryError("The provider returned an invalid model identifier.");
                    if (provider == "google" && item.TryGetProperty("supportedGenerationMethods", out var methods)
                        && methods.ValueKind == JsonValueKind.Array && !methods.EnumerateArray().Any(m => m.ValueKind == JsonValueKind.String && m.GetString() == "generateContent")) continue;
                    if (!identifiers.Add(id)) continue;
                    if (models.Count >= limits.MaximumModels) { truncated = true; break; }
                    var display = ModelText(item, provider == "google" ? "displayName" : "display_name", 256, options.ApiKey) ?? id;
                    int? context = null, input = null, output = null;
                    var source = "unavailable";
                    string? metadataUrl = null;
                    if (provider is "anthropic" or "google")
                    {
                        input = ModelTokenLimit(item, provider == "google" ? "inputTokenLimit" : "max_input_tokens");
                        output = ModelTokenLimit(item, provider == "google" ? "outputTokenLimit" : "max_tokens");
                        if (input is not null || output is not null)
                        {
                            source = "provider-api";
                            metadataUrl = provider == "google" ? "https://ai.google.dev/api/models" : "https://platform.claude.com/docs/en/api/models/list";
                        }
                    }
                    else if (officialOpenAi && TryOpenAiModelCapacity(id, out var capacity, out var maximumOutput, out var urlSource))
                    { context = capacity; output = maximumOutput; source = "official-exact-id-catalog"; metadataUrl = urlSource; }
                    models.Add(new(id, display, context, input, output, source, metadataUrl));
                }
                if (truncated) break;
                cursor = provider switch
                {
                    "google" => ModelText(root, "nextPageToken", 2048, options.ApiKey),
                    "anthropic" when root.TryGetProperty("has_more", out var more) && more.ValueKind == JsonValueKind.True =>
                        ModelText(root, "last_id", 2048, options.ApiKey) ?? throw ModelDiscoveryError("The provider returned an invalid model page cursor."),
                    _ => null,
                };
                if (string.IsNullOrEmpty(cursor)) break;
                if (!cursors.Add(cursor)) throw ModelDiscoveryError("The provider repeated a model page cursor.");
                if (page == limits.MaximumPages - 1 || models.Count >= limits.MaximumModels) { truncated = true; break; }
            }
            return new(provider, "available", models.OrderBy(m => m.Id, StringComparer.Ordinal).ToArray(),
                "Listed models are not a guarantee of tool support or inference access. Unknown capacities require a manual budget; input and output limits do not imply a shared context capacity."
                + (truncated ? " The catalogue was truncated by discovery limits." : ""), truncated);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw ModelDiscoveryError("Model discovery timed out."); }
        catch (OperationCanceledException) { throw new OperationCanceledException("Model discovery was cancelled.", cancellationToken); }
        catch (ShadowAgentProviderException) { throw; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException)
        { throw ModelDiscoveryError("Model discovery failed. Check the provider endpoint, network access and browser CORS policy."); }
    }

    private static ShadowAgentProviderException ModelDiscoveryError(string message) => new("model_discovery", message);

    private static async Task<JsonDocument> ReadModelPageAsync(HttpResponseMessage response, int maximumBytes, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength > maximumBytes) throw ModelDiscoveryError("The provider model catalogue exceeded the response limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var bytes = new byte[Math.Min(maximumBytes + 1, 16384)];
        while (true)
        {
            var count = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
            if (count == 0) break;
            if (buffer.Length + count > maximumBytes) throw ModelDiscoveryError("The provider model catalogue exceeded the response limit.");
            buffer.Write(bytes, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static string? ModelText(JsonElement value, string name, int maximumLength, string secret)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind == JsonValueKind.Null) return null;
        if (property.ValueKind != JsonValueKind.String) throw ModelDiscoveryError("The provider returned invalid model metadata.");
        var text = property.GetString()!;
        if (text.Length > maximumLength || text.Any(char.IsControl) || text.Contains(secret, StringComparison.Ordinal))
            throw ModelDiscoveryError("The provider returned invalid model metadata.");
        return text;
    }

    private static int? ModelTokenLimit(JsonElement item, string field) =>
        item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var tokens) && tokens > 0 ? tokens : null;

    // Verified against these exact official model pages on 2026-10-19. Do not match prefixes,
    // snapshots, aliases, fine-tunes or custom endpoints: their capacities may differ.
    private static bool TryOpenAiModelCapacity(string id, out int context, out int output, out string? url)
    {
        context = 0; output = 0; url = null;
        if (id is not ("gpt-6.1-sol" or "gpt-6-luna" or "gpt-6-astra")) return false;
        context = 1_050_000; output = 128_000;
        url = "https://developers.openai.com/api/docs/models/" + id;
        return true;
    }
}
