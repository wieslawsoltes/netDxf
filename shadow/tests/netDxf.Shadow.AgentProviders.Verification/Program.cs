#pragma warning disable OPENAI001, MEAI001
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Agents.Auth;

var checks = 0;
foreach (var provider in new[] { "openai", "anthropic", "google", "chatgpt" }) await VerifyProvider(provider);
foreach (var provider in new[] { "openai", "anthropic", "google", "chatgpt" }) await VerifyProvider(provider, browser: true);
await VerifyReasoningProvenance();
await VerifyFailures();
await VerifyBrowserAnthropic();
checks += await ProviderTerminationVerification.RunAsync();
await VerifyAuthentication();
Console.WriteLine($"PASS: {checks} provider, streaming, tool replay, PKCE, JWT and token-lifecycle checks.");

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    checks++;
}
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { checks++; return; }
    throw new InvalidOperationException("FAIL: " + name);
}

async Task VerifyProvider(string provider, bool browser = false)
{
    var requests = new List<(Uri Uri, JsonObject Body, string? Authorization, Dictionary<string, string> Headers, bool Streaming, string? Credentials)>();
    using var handler = new TestHandler(async (request, ct) =>
    {
        if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK);
        request.Options.TryGetValue(new HttpRequestOptionsKey<bool>("WebAssemblyEnableStreamingResponse"), out var streaming);
        request.Options.TryGetValue(new HttpRequestOptionsKey<IDictionary<string, object>>("WebAssemblyFetchOptions"), out var fetchOptions);
        requests.Add((request.RequestUri!, JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject(), request.Headers.Authorization?.ToString(),
            request.Headers.ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.OrdinalIgnoreCase),
            streaming, fetchOptions is not null && fetchOptions.TryGetValue("credentials", out var credentials) ? credentials as string : null));
        var payload = Streams.For(provider, requests.Count == 1);
        if (provider == "chatgpt") payload = payload.Replace("\"name\":\"document_get\"", "\"name\":\"document_get\",\"namespace\":\"shadow\"", StringComparison.Ordinal);
        return new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
    });
    using var http = new HttpClient(handler);
    var configuration = new ShadowAgentProviderOptions { Provider = provider, Model = "verification-model", ApiKey = "test-secret", Endpoint = "https://provider.example/v1" };
    using var client = browser ? ShadowAgentProviders.CreateBrowser(configuration, http) : ShadowAgentProviders.Create(configuration, http);
    var options = new ChatOptions
    {
        ModelId = "verification-model", MaxOutputTokens = 256, Temperature = 0.5f,
        Tools = [AIFunctionFactory.Create(() => "drawing", name: "document_get", description: "Inspect drawing")],
    };
    var messages = new List<ChatMessage> { new(ChatRole.System, "Help edit a DXF document."), new(ChatRole.User, "Inspect my drawing.") };
    var response = await client.GetResponseAsync(messages, options);
    var call = response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Single();
    Check(call.Name == "document_get" && !string.IsNullOrWhiteSpace(call.CallId), provider + " decodes streamed tool call (" + call.Name + ", id=" + call.CallId + ")");
    Check(requests[0].Body.ToJsonString().Contains("document_get", StringComparison.Ordinal), provider + " serializes tool declaration");
    Check(response.Usage?.InputTokenCount == 11, provider + " reports input usage");
    foreach (var message in response.Messages)
        messages.Add(new ChatMessage(message.Role, message.Contents.Select(CloneContent).ToList()));
    messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, "{\"entities\":3}")]));
    var final = await client.GetResponseAsync(messages, options);
    Check(final.Text == "done", provider + " completes after tool result");
    Check(requests.Count == 2, provider + " makes exactly one request per inference");
    Check(requests[1].Body.ToJsonString().Contains("entities", StringComparison.Ordinal), provider + " replays tool output");
    Check(!configuration.ToString().Contains("test-secret", StringComparison.Ordinal), provider + " redacts options");
    Check(!JsonSerializer.Serialize(configuration).Contains("test-secret", StringComparison.Ordinal), provider + " excludes API key from settings JSON");
    if (browser)
    {
        Check(requests.All(request => request.Streaming), provider + " enables browser response streaming");
        Check(requests.All(request => request.Credentials == "omit"), provider + " browser requests omit cookies");
    }
    if (provider is "openai" or "chatgpt")
    {
        Check(requests[0].Authorization == "Bearer test-secret", provider + " uses bearer API credential");
        Check(requests[0].Uri.AbsolutePath == "/v1/responses", provider + " uses Responses SDK endpoint");
        Check(requests[0].Body["store"]?.GetValue<bool>() == false, provider + " disables server-side history storage");
        Check(requests[1].Body.ToJsonString().Contains("encrypted_reasoning_fixture", StringComparison.Ordinal), provider + " retains encrypted reasoning through portable replay");
    }
    if (provider == "chatgpt")
    {
        var body = requests[0].Body;
        Check(body["stream"]?.GetValue<bool>() == true, "ChatGPT always streams");
        Check(!body.ContainsKey("max_output_tokens") && !body.ContainsKey("temperature") && !body.ContainsKey("previous_response_id"), "ChatGPT removes unsupported inference fields");
        Check(body["input"]!.AsArray().OfType<JsonObject>().All(item => item["role"]?.GetValue<string>() != "system"), "ChatGPT converts system messages");
        Check(body["tools"]![0]!["type"]?.GetValue<string>() == "namespace", "ChatGPT namespaces function tools");
        Check(requests[1].Body["input"]!.AsArray().OfType<JsonObject>().Single(item => item["type"]?.GetValue<string>() == "function_call")
            ["namespace"]?.GetValue<string>() == "shadow", "ChatGPT restores tool-call namespace after portable checkpoint");
        var before = requests[1].Body.ToJsonString();
        Check(ChatGptResponsesContract.Normalize(requests[1].Body).ToJsonString() == before, "Browser and companion normalization is idempotent");
    }
    if (provider == "google")
    {
        Check(messages.SelectMany(m => m.Contents).OfType<TextReasoningContent>().Any(r => r.ProtectedData == "c2lnbmF0dXJl"), "Gemini preserves thought signature in portable content");
        Check(requests[1].Body.ToJsonString().Contains("c2lnbmF0dXJl", StringComparison.Ordinal), "Gemini restores thought signature after checkpoint");
    }
    if (provider == "anthropic")
    {
        Check(requests.All(request => request.Headers["x-api-key"] == "test-secret"), "Anthropic uses the explicit API credential");
        Check(requests.All(request => request.Headers["anthropic-version"] == "2023-06-01"), "Anthropic sends the supported API version");
        Check(messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>().Any(reasoning => reasoning.ProtectedData == "signature_fixture"),
            "Anthropic preserves streamed thinking signatures in portable content");
        Check(requests[1].Body.ToJsonString().Contains("signature_fixture", StringComparison.Ordinal), "Anthropic restores thinking signature after checkpoint");
        if (browser)
        {
            Check(requests.All(request => request.Headers["anthropic-dangerous-direct-browser-access"] == "true"), "Anthropic explicitly enables direct browser API access");
            Check(requests.All(request => !request.Headers.ContainsKey("User-Agent")), "Anthropic browser transport avoids the browser-controlled User-Agent header");
        }
    }
    client.Dispose();
    using var alive = await http.GetAsync("https://provider.example/alive");
    Check(alive.IsSuccessStatusCode, provider + " leaves injected HttpClient usable");
}

async Task VerifyReasoningProvenance()
{
    foreach (var source in new[] { "openai", "anthropic", "google", "chatgpt" })
    {
        using var sourceHttp = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Streams.For(source, true), Encoding.UTF8, "text/event-stream") })));
        using var sourceClient = ShadowAgentProviders.CreateBrowser(new() { Provider = source, ApiKey = "key", Model = "model" }, sourceHttp);
        var response = await sourceClient.GetResponseAsync([new(ChatRole.User, "Inspect drawing.")]);
        var history = new List<ChatMessage> { new(ChatRole.User, "visible_user_request") };
        history.AddRange(response.Messages.Select(message => new ChatMessage(message.Role, message.Contents.Select(CloneContent).ToList())));
        var reasoning = history.SelectMany(message => message.Contents).OfType<TextReasoningContent>().ToArray();
        Check(reasoning.Length > 0 && reasoning.All(content => content.AdditionalProperties?[ShadowAgentProviders.ReasoningProviderKey] as string == source),
            source + " stamps reasoning provenance through streaming aggregation");
        var signatures = reasoning.Select(content => content.ProtectedData).Where(value => !string.IsNullOrEmpty(value)).Distinct().ToArray();
        Check(signatures.Length > 0, source + " reasoning fixture supplies protected continuation data");
        // AdditionalProperties values become JsonElement when the companion's NDJSON bridge
        // serializes generic content. This must preserve the same provenance as native strings.
        foreach (var content in reasoning) content.AdditionalProperties![ShadowAgentProviders.ReasoningProviderKey] = JsonSerializer.SerializeToElement(source);
        foreach (var call in history.SelectMany(message => message.Contents).OfType<FunctionCallContent>().ToArray())
            history.Add(new(ChatRole.Tool, [new FunctionResultContent(call.CallId, "visible_tool_result")]));
        history.Add(new(ChatRole.Assistant, "visible_assistant_reply"));
        foreach (var target in new[] { "openai", "anthropic", "google", "chatgpt", "gemini" })
        {
            string? body = null;
            using var targetHttp = new HttpClient(new TestHandler(async (request, token) =>
            {
                body = await request.Content!.ReadAsStringAsync(token);
                return new(HttpStatusCode.OK) { Content = new StringContent(Streams.For(target == "gemini" ? "google" : target, false), Encoding.UTF8, "text/event-stream") };
            }));
            using var targetClient = ShadowAgentProviders.CreateBrowser(new() { Provider = target, ApiKey = "key", Model = "model" }, targetHttp);
            await targetClient.GetResponseAsync(history);
            var compatible = source == (target == "gemini" ? "google" : target);
            Check(body is not null && signatures.All(signature => body.Contains(signature!, StringComparison.Ordinal) == compatible),
                source + " → " + target + " preserves only compatible provider signatures");
            Check(body!.Contains("visible_user_request", StringComparison.Ordinal) && body.Contains("visible_assistant_reply", StringComparison.Ordinal)
                && body.Contains("document_get", StringComparison.Ordinal) && body.Contains("visible_tool_result", StringComparison.Ordinal),
                source + " → " + target + " retains visible conversation and complete tool pairs");
        }
        Check(reasoning.All(content => signatures.Contains(content.ProtectedData) || string.IsNullOrEmpty(content.ProtectedData)),
            source + " provider switching leaves stored reasoning unchanged");
    }
    foreach (var target in new[] { "openai", "anthropic", "google", "chatgpt" })
    {
        string? body = null;
        using var http = new HttpClient(new TestHandler(async (request, token) =>
        {
            body = await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent(Streams.For(target, false), Encoding.UTF8, "text/event-stream") };
        }));
        using var client = ShadowAgentProviders.CreateBrowser(new() { Provider = target, ApiKey = "key", Model = "model" }, http);
        await client.GetResponseAsync([
            new(ChatRole.User, "visible_legacy_user"),
            new(ChatRole.Assistant, [new TextReasoningContent("legacy_reasoning_only") { ProtectedData = "unknown_not_base64_signature" }]),
            new(ChatRole.Assistant, [new TextReasoningContent("legacy_reasoning_mixed") { ProtectedData = "unknown_signature" }, new TextContent("visible_legacy_reply")]),
        ]);
        Check(body is not null && !body.Contains("unknown_", StringComparison.Ordinal) && !body.Contains("legacy_reasoning", StringComparison.Ordinal)
            && body.Contains("visible_legacy_user", StringComparison.Ordinal) && body.Contains("visible_legacy_reply", StringComparison.Ordinal),
            target + " safely omits untagged legacy reasoning and empty reasoning-only messages");
    }
}

async Task VerifyBrowserAnthropic()
{
    var configuration = new ShadowAgentProviderOptions { Provider = "anthropic", ApiKey = "key", Model = "model" };
    var terminal = "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n";
    foreach (var browser in new[] { false, true })
    {
        using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(Streams.For("anthropic", true).Replace(terminal, "", StringComparison.Ordinal), Encoding.UTF8, "text/event-stream") })));
        using var client = browser ? ShadowAgentProviders.CreateBrowser(configuration, http) : ShadowAgentProviders.Create(configuration, http);
        await Throws<ShadowAgentProviderException>(() => client.GetResponseAsync([new(ChatRole.User, "test")]),
            "Anthropic rejects a truncated stream after stop_reason and tool data before message_stop");
    }
    using (var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent("event: error\ndata: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Retry later.\"}}\n\n",
            Encoding.UTF8, "text/event-stream"),
    }))))
    using (var client = ShadowAgentProviders.CreateBrowser(configuration, http))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "test")]); throw new InvalidOperationException("Expected SSE error"); }
        catch (Anthropic.Exceptions.AnthropicSseException error)
        { Check(ShadowAgentProviders.TryGetTransientFailure(error, out _), "Anthropic browser uses official retryable SSE error parsing"); }
    }
    var requests = 0;
    using (var http = new HttpClient(new TestHandler((_, _) =>
    {
        requests++;
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("{\"error\":{\"type\":\"rate_limit_error\",\"message\":\"Rate limited.\"}}", Encoding.UTF8, "application/json") };
        response.Headers.Add("Retry-After", "3");
        return Task.FromResult(response);
    })))
    using (var client = ShadowAgentProviders.CreateBrowser(configuration, http))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "test")]); throw new InvalidOperationException("Expected rate limit"); }
        catch (Anthropic.Exceptions.AnthropicApiException error)
        {
            Check(ShadowAgentProviders.TryGetTransientFailure(error, out var delay) && delay == TimeSpan.FromSeconds(3),
                "Anthropic browser preserves Retry-After for harness retry policy");
            Check(requests == 1, "Anthropic browser dispatch never retries beneath the harness");
        }
    }
    using (var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
    { Content = new StringContent(new string('x', 128 * 1024)) }))))
    using (var client = ShadowAgentProviders.CreateBrowser(configuration, http))
    {
        try { await client.GetResponseAsync([new(ChatRole.User, "test")]); throw new InvalidOperationException("Expected oversized provider error"); }
        catch (Anthropic.Exceptions.AnthropicApiException error)
        { Check(error.ResponseBody.Length < 66 * 1024 && error.ResponseBody.EndsWith("[Provider error body truncated.]", StringComparison.Ordinal), "Anthropic browser bounds HTTP error bodies"); }
    }
    var waiting = new WaitingStream();
    using (var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StreamContent(waiting) }))))
    using (var client = ShadowAgentProviders.CreateBrowser(configuration, http))
    {
        var sdk = client.GetService<Anthropic.IAnthropicClient>()!;
        var timed = sdk.WithOptions(options => options with { Timeout = TimeSpan.FromMilliseconds(100) });
        async Task ReadTimedStreamAsync()
        {
            await foreach (var _ in timed.Messages.CreateStreaming(new()
            {
                MaxTokens = 256, Model = "model", Messages = [new() { Role = Anthropic.Models.Messages.Role.User, Content = "test" }],
            }).WithCancellation(CancellationToken.None)) { }
        }
        await Throws<OperationCanceledException>(() => ReadTimedStreamAsync().WaitAsync(TimeSpan.FromSeconds(5)),
            "Anthropic browser SDK timeout remains active while reading the response body");
        Check(waiting.IsDisposed, "Anthropic browser cancellation disposes its response stream");
    }
    var cancelledStream = new WaitingStream();
    using (var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
    { Content = new StreamContent(cancelledStream) }))))
    using (var client = ShadowAgentProviders.CreateBrowser(configuration, http))
    using (var cancellation = new CancellationTokenSource())
    {
        var pending = client.GetResponseAsync([new(ChatRole.User, "test")], cancellationToken: cancellation.Token);
        await cancelledStream.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Throws<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)),
            "Anthropic browser user cancellation interrupts a stalled response body");
        Check(cancelledStream.IsDisposed, "Anthropic browser user cancellation releases response resources");
    }
}

AIContent CloneContent(AIContent content) => content switch
{
    TextContent text => new TextContent(text.Text),
    FunctionCallContent call => new FunctionCallContent(call.CallId, call.Name, call.Arguments),
    TextReasoningContent reasoning => new TextReasoningContent(reasoning.Text)
    {
        ProtectedData = reasoning.ProtectedData,
        AdditionalProperties = reasoning.AdditionalProperties is { } properties ? new(properties) : null,
    },
    _ => throw new InvalidOperationException("Unexpected checkpoint content: " + content.GetType().Name),
};

async Task VerifyFailures()
{
    await Throws<ArgumentException>(() => Task.Run(() => ChatGptResponsesContract.Normalize(JsonNode.Parse(
        """{"input":[],"tools":[{"type":"namespace","name":"shadow","tools":[{"type":"mcp","server_url":"https://remote.example"}]}]}""")!.AsObject())),
        "ChatGPT rejects unsupported hosted tools inside namespaces");
    foreach (var payload in new[]
    {
        """data: {"type":"response.created","sequence_number":0,"response":{"id":"r","created_at":1,"model":"m","status":"in_progress","output":[]}}""" + "\n\n",
        """data: {"type":"response.failed","sequence_number":0,"response":{"id":"r","created_at":1,"model":"m","status":"failed","output":[],"error":{"code":"subscription_sharing_usage_limit_exceeded","message":"Plan limit reached."}}}""" + "\n\n",
    })
    {
        using var http = new HttpClient(new TestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") })));
        using var client = ShadowAgentProviders.Create(new() { Provider = "chatgpt", ApiKey = "key", Model = "model" }, http);
        await Throws<ShadowAgentProviderException>(() => client.GetResponseAsync([new(ChatRole.User, "test")]), "Partial and failed streams never become successful turns");
    }
    Check(ShadowAgentProviders.TryGetTransientFailure(new HttpRequestException("rate", null, HttpStatusCode.TooManyRequests), out _), "Retries rate limiting");
    Check(!ShadowAgentProviders.TryGetTransientFailure(new HttpRequestException("auth", null, HttpStatusCode.Unauthorized), out _), "Does not retry invalid credentials");
    Check(!ShadowAgentProviders.TryGetTransientFailure(new OperationCanceledException(), out _), "Cancellation is final");
    Check(!ShadowAgentProviders.TryGetTransientFailure(new ShadowAgentProviderException("subscription_sharing_usage_limit_exceeded", "limit"), out _), "Subscription limits require user recovery");
    var calls = 0;
    using var rateHttp = new HttpClient(new TestHandler((_, _) =>
    {
        calls++;
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        { Content = new StringContent("""{"error":{"message":"Rate limited","type":"rate_limit_error","code":"rate_limit_exceeded"}}""", Encoding.UTF8, "application/json") };
        response.Headers.TryAddWithoutValidation("Retry-After", "7"); return Task.FromResult(response);
    }));
    using var limited = ShadowAgentProviders.Create(new() { ApiKey = "key", Model = "model" }, rateHttp);
    try { await limited.GetResponseAsync([new(ChatRole.User, "test")]); throw new InvalidOperationException("Expected rate limit"); }
    catch (System.ClientModel.ClientResultException error)
    {
        Check(ShadowAgentProviders.TryGetTransientFailure(error, out var retry) && retry == TimeSpan.FromSeconds(7), "Reads official SDK Retry-After");
        Check(calls == 1, "SDK never retries underneath harness");
    }
}

async Task VerifyAuthentication()
{
    using var server = new AuthServer();
    using var http = new HttpClient(server);
    using var auth = new ChatGptSignInClient(http);
    var settings = new ChatGptSignInOptions { HostId = "urn:uuid:verification", CallbackUri = new("http://127.0.0.1:1455/auth/callback") };
    var attempt = await auth.BeginAsync(settings);
    var authorize = AuthServer.Query(attempt.AuthorizationUri);
    server.Nonce = authorize["nonce"]; server.Challenge = authorize["code_challenge"];
    Check(authorize["client_id"] == "dynamic_agent_client" && authorize["code_challenge_method"] == "S256", "Uses documented dynamic registration and PKCE");
    Check(authorize["ext_agent_host_id"] == settings.HostId && authorize["agent_name_hint"] == settings.AppName, "Binds authorization to stable host");
    await Throws<ChatGptAuthenticationException>(() => auth.CompleteAsync(attempt, new(attempt.CallbackUri + "?code=test&state=invalid&client_id=oaiapp_verify")), "Rejects invalid OAuth state");
    Check(attempt.RegisteredClientId is null, "Invalid callback cannot replace a registered client");
    var callback = new Uri(attempt.CallbackUri + "?code=test&state=" + authorize["state"] + "&client_id=oaiapp_verify");
    var credential = await auth.CompleteAsync(attempt, callback);
    Check(credential.Subject == "verified-user" && credential.HasPlanUsage && credential.AccessToken == "access-1", "Validates identity and scopes before enabling inference");
    Check(server.PkceVerified && server.LastClientId == "oaiapp_verify", "Exchanges code using issued client and matching verifier");
    Check(!credential.ToString().Contains("access-1", StringComparison.Ordinal) && !JsonSerializer.Serialize(credential).Contains("access-1", StringComparison.Ordinal), "Credential diagnostics and JSON redact tokens");
    await Throws<ChatGptAuthenticationException>(() => auth.CompleteAsync(attempt, callback), "Rejects callback replay");
    var updated = await Task.WhenAll(auth.RefreshAsync(credential), auth.RefreshAsync(credential));
    Check(updated.All(value => value.AccessToken == "access-2" && value.RefreshToken == "refresh-2") && server.RefreshCount == 1, "Serializes refresh rotation and shares updated credentials");
    Check(await auth.RevokeAsync(updated[0]) && server.RevokedToken == "refresh-2", "Revokes latest rotating refresh token");
    foreach (var fault in new[] { "nonce", "signature", "audience", "issuer", "scope", "expiry", "invalid_grant" })
    {
        var pending = await auth.BeginAsync(settings);
        var query = AuthServer.Query(pending.AuthorizationUri);
        server.Nonce = query["nonce"]; server.Challenge = query["code_challenge"]; server.Fault = fault;
        await Throws<ChatGptAuthenticationException>(() => auth.CompleteAsync(pending,
            new(pending.CallbackUri + "?code=test&state=" + query["state"] + "&client_id=oaiapp_verify")), "Rejects invalid authentication " + fault);
        if (fault == "invalid_grant") Check(pending.RegisteredClientId == "oaiapp_verify", "Retains issued nonsecret registration when code exchange fails");
    }
    await Throws<ArgumentException>(() => auth.BeginAsync(settings with { CallbackUri = new("https://example.github.io/callback") }), "Rejects unsupported static-site OAuth callback");
    await Throws<ArgumentException>(() => auth.BeginAsync(settings with { CallbackUri = new("http://localhost:1455/auth/callback") }), "Requires documented 127.0.0.1 callback host");
}

static class Streams
{
    public static string For(string provider, bool tool) => provider switch { "anthropic" => Anthropic(tool), "google" => Google(tool), _ => OpenAI(tool) };
    private static string OpenAI(bool tool)
    {
        var item = tool
            ? """{"type":"function_call","id":"fc_1","call_id":"call_1","name":"document_get","arguments":"{}","status":"completed"}"""
            : """{"type":"message","id":"msg_1","role":"assistant","status":"completed","content":[{"type":"output_text","text":"done","annotations":[]}]}""";
        var events = new List<string>
        {
            """{"type":"response.created","sequence_number":0,"response":{"id":"resp_1","created_at":1,"model":"verification-model","status":"in_progress","output":[]}}""",
            """{"type":"response.output_item.added","sequence_number":1,"output_index":0,"item":""" + item + "}",
        };
        const string reasoning = """{"type":"reasoning","id":"rs_1","summary":[],"encrypted_content":"encrypted_reasoning_fixture"}""";
        if (tool)
        {
            events.Add("""{"type":"response.output_item.added","sequence_number":2,"output_index":1,"item":""" + reasoning + "}");
            events.Add("""{"type":"response.output_item.done","sequence_number":3,"output_index":1,"item":""" + reasoning + "}");
        }
        if (!tool) events.Add("""{"type":"response.output_text.delta","sequence_number":2,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"done"}""");
        events.Add("""{"type":"response.output_item.done","sequence_number":4,"output_index":0,"item":""" + item + "}");
        events.Add("""{"type":"response.completed","sequence_number":5,"response":{"id":"resp_1","created_at":1,"model":"verification-model","status":"completed","store":false,"output":[""" + item + (tool ? "," + reasoning : "") + """],"usage":{"input_tokens":11,"output_tokens":4,"total_tokens":15}}}""");
        return string.Concat(events.Select(value => "data: " + value + "\n\n"));
    }
    private static string Anthropic(bool tool)
    {
        string[] events =
        [
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"verification-model","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":11,"output_tokens":1}}}""",
            ..(tool ? new[]
            {
                """{"type":"content_block_start","index":0,"content_block":{"type":"thinking","thinking":"","signature":""}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"Inspecting the document."}}""",
                """{"type":"content_block_delta","index":0,"delta":{"type":"signature_delta","signature":"signature_fixture"}}""",
                """{"type":"content_block_stop","index":0}""",
            } : []),
            tool ? """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"call_1","name":"document_get","input":{}}}"""
                : """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            tool ? """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{}"}}"""
                : """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}""",
            tool ? """{"type":"content_block_stop","index":1}""" : """{"type":"content_block_stop","index":0}""",
            """{"type":"message_delta","delta":{"stop_reason":""" + JsonSerializer.Serialize(tool ? "tool_use" : "end_turn") + ""","stop_sequence":null},"usage":{"output_tokens":4}}""",
            """{"type":"message_stop"}""",
        ];
        return string.Concat(events.Select(value => "event: " + JsonNode.Parse(value)!["type"]!.GetValue<string>() + "\ndata: " + value + "\n\n"));
    }
    private static string Google(bool tool) => """data: {"candidates":[{"index":0,"content":{"role":"model","parts":["""
        + (tool ? """{"functionCall":{"name":"document_get","args":{}},"thoughtSignature":"c2lnbmF0dXJl"}""" : """{"text":"done"}""")
        + """]},"finishReason":"STOP"}],"modelVersion":"verification-model","responseId":"g1","usageMetadata":{"promptTokenCount":11,"candidatesTokenCount":4,"totalTokenCount":15}}""" + "\n\n";
}

sealed class TestHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
}

sealed class WaitingStream : Stream
{
    public bool IsDisposed { get; private set; }
    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        ReadStarted.TrySetResult();
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        await ReadAsync(buffer.AsMemory(offset, count), cancellationToken);
    protected override void Dispose(bool disposing) { IsDisposed = true; base.Dispose(disposing); }
}

sealed class AuthServer : HttpMessageHandler
{
    private readonly RSA _rsa = RSA.Create(2048);
    public string Nonce { get; set; } = "";
    public string Challenge { get; set; } = "";
    public string? Fault { get; set; }
    public bool PkceVerified { get; private set; }
    public string? LastClientId { get; private set; }
    public int RefreshCount { get; private set; }
    public string? RevokedToken { get; private set; }
    public static Dictionary<string, string> Query(Uri uri) => uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(value => value.Split('=', 2)).ToDictionary(value => Uri.UnescapeDataString(value[0]), value => Uri.UnescapeDataString(value.ElementAtOrDefault(1)?.Replace('+', ' ') ?? ""));
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri!.AbsolutePath;
        object result;
        if (path == "/.well-known/openid-configuration") result = new
        {
            issuer = "https://auth.openai.com", authorization_endpoint = "https://auth.openai.com/api/accounts/authorize",
            token_endpoint = "https://auth.openai.com/api/accounts/oauth/token", jwks_uri = "https://auth.openai.com/.well-known/jwks.json",
            revocation_endpoint = "https://auth.openai.com/api/accounts/oauth/revoke",
        };
        else if (path == "/.well-known/jwks.json")
        {
            var key = _rsa.ExportParameters(false);
            result = new { keys = new[] { new { kid = "test-key", kty = "RSA", alg = "RS256", use = "sig", n = B64(key.Modulus!), e = B64(key.Exponent!) } } };
        }
        else
        {
            var data = (await request.Content!.ReadAsStringAsync(cancellationToken)).Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(part => Uri.UnescapeDataString(part[0]), part => Uri.UnescapeDataString(part[1].Replace('+', ' ')));
            LastClientId = data["client_id"];
            if (path.EndsWith("/revoke", StringComparison.Ordinal))
            {
                RevokedToken = data["token"];
                return new(HttpStatusCode.OK) { Content = new StringContent("") };
            }
            var refresh = data["grant_type"] == "refresh_token";
            if (Fault == "invalid_grant") return new(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json") };
            if (refresh) RefreshCount++;
            else PkceVerified = B64(SHA256.HashData(Encoding.ASCII.GetBytes(data["code_verifier"]))) == Challenge;
            var header = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", kid = "test-key" }));
            var payload = B64(JsonSerializer.SerializeToUtf8Bytes(new
            {
                iss = Fault == "issuer" ? "https://evil.example" : "https://auth.openai.com",
                sub = "verified-user", email = "verified@example.com", aud = Fault == "audience" ? "another-client" : "oaiapp_verify",
                exp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (Fault == "expiry" ? -600 : 3600), nonce = Fault == "nonce" ? "wrong" : Nonce,
            }));
            var signature = _rsa.SignData(Encoding.ASCII.GetBytes(header + "." + payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            if (Fault == "signature") signature[0] ^= 1;
            result = new
            {
                token_type = "Bearer", access_token = refresh ? "access-2" : "access-1", refresh_token = refresh ? "refresh-2" : "refresh-1",
                id_token = header + "." + payload + "." + B64(signature), expires_in = 3600,
                scope = Fault == "scope" ? "openid profile email" : "openid profile email offline_access resource.invoke chatgpt.tokens.use.direct",
            };
        }
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") };
    }
    private static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    protected override void Dispose(bool disposing) { if (disposing) _rsa.Dispose(); base.Dispose(disposing); }
}
