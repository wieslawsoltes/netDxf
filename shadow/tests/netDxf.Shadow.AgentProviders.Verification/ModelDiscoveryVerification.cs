using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Agents;

internal static class ModelDiscoveryVerification
{
    public static async Task<int> RunAsync()
    {
        var checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("FAIL: model discovery " + message);
            checks++;
        }
        const string key = "model-discovery-fixture-secret";
        AgentModelDiscoveryRequest Options(string provider = "openai", string? endpoint = null) => new() { Provider = provider, ApiKey = key, Endpoint = endpoint };
        async Task Fails(Func<Task> action, string message, string? expected = null)
        {
            try { await action(); throw new InvalidOperationException("Expected discovery failure: " + message); }
            catch (Exception error) when (error is ShadowAgentProviderException or ArgumentException or OperationCanceledException)
            {
                Check(!error.ToString().Contains(key, StringComparison.Ordinal), message + " does not leak credentials");
                Check(expected is null || error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), message);
            }
        }
        foreach (var browser in new[] { false, true })
        {
            var sent = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                sent++;
                if (sent > 1) return Task.FromResult(Json("{\"data\":[]} "));
                Check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/models", "OpenAI documented route");
                Check(request.Method == HttpMethod.Get && request.Content is null, "discovery performs read-only GET");
                Check(request.Headers.Authorization?.Parameter == key, "OpenAI explicit authorization");
                Check(request.Headers.CacheControl?.NoStore == true, "cache prohibited");
                if (browser)
                {
                    Check(request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var fetch)
                        && (string)fetch["credentials"] == "omit" && (string)fetch["cache"] == "no-store" && (string)fetch["redirect"] == "error", "browser refuses cookies/cache/redirects");
                }
                return Task.FromResult(Json("""{"data":[{"id":"fixture-manual"},{"id":"gpt-6-luna"},{"id":"gpt-6-luna-2026-10-19"},{"id":"gpt-6-luna"}]}"""));
            }));
            var result = browser ? await ShadowAgentProviders.DiscoverBrowserModelsAsync(Options(), http) : await ShadowAgentProviders.DiscoverModelsAsync(Options(), http);
            Check(result.Status == "available" && result.Models.Count == 3 && sent == 1, "deduplicated single-page OpenAI catalogue");
            var known = result.Models.Single(m => m.Id == "gpt-6-luna");
            Check(known.ContextWindowTokens == 1_050_000 && known.OutputTokenLimit == 128_000 && known.InputTokenLimit is null, "exact documented capacity semantics");
            Check(known.MetadataSource == "official-exact-id-catalog" && known.MetadataUrl!.EndsWith("/gpt-6-luna", StringComparison.Ordinal), "exact model provenance");
            Check(result.Models.Where(m => m.Id != "gpt-6-luna").All(m => m.ContextWindowTokens is null && m.MetadataSource == "unavailable"), "unknown and snapshot IDs are not guessed");
            Check(!JsonSerializer.Serialize(result).Contains(key, StringComparison.Ordinal) && !Options().ToString().Contains(key, StringComparison.Ordinal), "results and request display are credential-free");
            // Discovery must leave an injected client usable after disposing its own transport.
            using var extra = await http.GetAsync("https://api.openai.com/v1/models");
            Check(extra.IsSuccessStatusCode, "injected transport remains caller-owned");
        }
        using (var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"data":[{"id":"gpt-6-luna"}]}""")))))
        {
            var custom = await ShadowAgentProviders.DiscoverModelsAsync(Options(endpoint: "https://custom.example/v1"), http);
            Check(custom.Models[0].ContextWindowTokens is null, "custom compatible endpoints do not inherit official model capacities");
            var chat = await ShadowAgentProviders.DiscoverModelsAsync(Options("chatgpt", "http://127.0.0.1:5091/v1"), http);
            Check(chat.Models[0].ContextWindowTokens is null, "ChatGPT companion does not inherit API context limits");
            var unavailable = await ShadowAgentProviders.DiscoverModelsAsync(new() { Provider = "chatgpt" }, http);
            Check(unavailable.Status == "unavailable" && unavailable.Models.Count == 0, "unconnected ChatGPT is honestly unavailable");
            Check((await ShadowAgentProviders.DiscoverModelsAsync(new() { Provider = "codex" }, http)).Status == "unavailable", "Codex directs callers to official app-server discovery");
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options("chatgpt", "https://other.example/v1"), http), "ChatGPT rejects a remote credential target");
        }
        foreach (var endpoint in new[] { "http://127.0.0.1:5091", "http://127.0.0.1:5091/v1", "http://127.0.0.1:5091/" })
        {
            using var http = new HttpClient(new Handler((request, _) =>
            {
                Check(request.RequestUri!.AbsoluteUri == "http://127.0.0.1:5091/v1/models", "ChatGPT companion root and versioned endpoint normalization");
                return Task.FromResult(Json("{\"data\":[]} "));
            }));
            await ShadowAgentProviders.DiscoverModelsAsync(Options("chatgpt", endpoint), http);
        }
        foreach (var browser in new[] { false, true })
        {
            var pages = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                pages++;
                Check(request.Headers.GetValues("x-api-key").Single() == key && request.Headers.GetValues("anthropic-version").Single() == "2023-06-01", "Anthropic documented headers");
                Check(request.Headers.Contains("anthropic-dangerous-direct-browser-access") == browser, "Anthropic browser opt-in is explicit");
                Check(request.RequestUri!.AbsolutePath == "/v1/models" && request.RequestUri.Query.Contains("limit=1000", StringComparison.Ordinal), "Anthropic route and page bound");
                if (pages == 2) Check(request.RequestUri.Query.Contains("after_id=claude-first", StringComparison.Ordinal), "Anthropic pagination cursor");
                return Task.FromResult(Json(pages == 1
                    ? """{"data":[{"id":"claude-first","display_name":"First","max_input_tokens":200000,"max_tokens":8192}],"has_more":true,"last_id":"claude-first"}"""
                    : """{"data":[{"id":"claude-second","max_input_tokens":0,"max_tokens":-1}],"has_more":false}"""));
            }));
            var result = browser ? await ShadowAgentProviders.DiscoverBrowserModelsAsync(Options("anthropic"), http) : await ShadowAgentProviders.DiscoverModelsAsync(Options("anthropic"), http);
            Check(pages == 2 && result.Models.Count == 2 && !result.Truncated, "Anthropic bounded pagination");
            Check(result.Models[0].DisplayName == "First" && result.Models[0].ContextWindowTokens is null && result.Models[0].InputTokenLimit == 200000 && result.Models[0].OutputTokenLimit == 8192, "Anthropic input/output semantics");
            Check(result.Models[0].MetadataSource == "provider-api" && result.Models[1].InputTokenLimit is null && result.Models[1].OutputTokenLimit is null, "invalid capacities remain unknown");
        }
        {
            var pages = 0;
            using var http = new HttpClient(new Handler((request, _) =>
            {
                pages++;
                Check(request.Headers.GetValues("x-goog-api-key").Single() == key && !request.RequestUri!.Query.Contains(key, StringComparison.Ordinal), "Google credential stays in header");
                Check(request.RequestUri!.AbsolutePath == "/v1beta/models", "Google documented route");
                if (pages == 2) Check(request.RequestUri.Query.Contains("pageToken=next%2F%3F%26", StringComparison.Ordinal), "opaque Google cursor is safely encoded");
                return Task.FromResult(Json(pages == 1
                    ? """{"models":[{"name":"models/fixture-gemini","displayName":"Gemini fixture","inputTokenLimit":131072,"outputTokenLimit":8192,"supportedGenerationMethods":["generateContent"]},{"name":"models/embed-only","supportedGenerationMethods":["embedContent"]}],"nextPageToken":"next/?&"}"""
                    : """{"models":[{"name":"models/unknown"}]}"""));
            }));
            var result = await ShadowAgentProviders.DiscoverBrowserModelsAsync(Options("gemini"), http);
            Check(result.Provider == "google" && result.Models.Count == 2, "Google alias normalized and explicit non-generation models filtered");
            Check(result.Models[0].Id == "fixture-gemini" && result.Models[0].InputTokenLimit == 131072 && result.Models[0].OutputTokenLimit == 8192 && result.Models[0].ContextWindowTokens is null, "Google retains separate input/output capacity");
        }
        foreach (var response in new[]
        {
            """{"data":[{"id":"x"}],"has_more":true,"last_id":"x"}""",
            """{"data":[{"id":"x"}],"has_more":true}""",
            "{bad-json",
            """{"data":[{"id":42}]}""",
            """{"unexpected":[]}""",
            "{\"data\":[{\"id\":\"" + key + "\"}]}"
        })
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(response))));
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options("anthropic"), http), "malformed or cyclic provider response rejected");
        }
        using (var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json("""{"models":[{"name":"models/first"},{"name":"models/second"}],"nextPageToken":"next"}""")))))
        {
            var models = await ShadowAgentProviders.DiscoverModelsAsync(Options("google"), http, new() { MaximumModels = 1 });
            Check(models.Truncated && models.Models.Count == 1, "model count is bounded with truncation notice");
            var pages = await ShadowAgentProviders.DiscoverModelsAsync(Options("google"), http, new() { MaximumPages = 1 });
            Check(pages.Truncated && pages.Models.Count == 2, "page count is bounded with truncation notice");
        }
        using (var http = new HttpClient(new Handler((_, _) => Task.FromResult(Json(new string('x', 1024))))))
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), http, new() { MaximumResponseBytes = 256 }), "oversize response rejected", "limit");
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.Redirect, HttpStatusCode.InternalServerError })
        {
            using var http = new HttpClient(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(key) })));
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), http), "provider error body never exposed");
        }
        using (var http = new HttpClient(new Handler((_, _) => throw new HttpRequestException(key))))
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), http), "transport exception sanitized");
        using (var http = new HttpClient(new Handler(async (_, cancellation) => { await Task.Delay(10000, cancellation); return Json("{}"); })))
        {
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), http, new() { Timeout = TimeSpan.FromMilliseconds(5) }), "whole-operation deadline", "timed out");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), http, cancellationToken: cancelled.Token), "caller cancellation preserved", "cancelled");
        }
        foreach (var endpoint in new[] { "http://example.com/v1", "https://user:password@example.com/v1", "https://example.com/v1?key=" + key, "https://example.com/v1#fragment" })
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(endpoint: endpoint)), "unsafe endpoint rejected before network");
        await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(), limits: new() { MaximumPages = 100 }), "invalid resource limits rejected");
        await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(new() { Provider = "openai", ApiKey = key + "\r\n" }), "header injection rejected");
        // Exercise the owned native handler: a redirect must never contact its destination.
        using (var source = new TcpListener(IPAddress.Loopback, 0))
        using (var target = new TcpListener(IPAddress.Loopback, 0))
        {
            source.Start(); target.Start();
            var address = $"http://127.0.0.1:{((IPEndPoint)source.LocalEndpoint).Port}/v1";
            var redirect = $"http://127.0.0.1:{((IPEndPoint)target.LocalEndpoint).Port}/stolen";
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var serve = Task.Run(async () =>
            {
                using var socket = await source.AcceptTcpClientAsync(deadline.Token);
                await using var stream = socket.GetStream();
                using var reader = new StreamReader(stream, leaveOpen: true);
                while (await reader.ReadLineAsync(deadline.Token) is { Length: > 0 }) { }
                await stream.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 302 Found\r\nLocation: {redirect}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), deadline.Token);
            });
            await Fails(() => ShadowAgentProviders.DiscoverModelsAsync(Options(endpoint: address), cancellationToken: deadline.Token), "owned native transport refuses redirects", "redirect");
            await serve;
            Check(!target.Pending(), "redirect destination receives no credential-bearing request");
        }
        Console.WriteLine($"PASS: {checks} credential-free model discovery checks.");
        return checks;
    }

    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
