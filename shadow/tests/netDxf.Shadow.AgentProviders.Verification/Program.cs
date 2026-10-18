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
await VerifyFailures();
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

async Task VerifyProvider(string provider)
{
    var requests = new List<(Uri Uri, JsonObject Body, string? Authorization)>();
    using var handler = new TestHandler(async (request, ct) =>
    {
        if (request.Method == HttpMethod.Get) return new(HttpStatusCode.OK);
        requests.Add((request.RequestUri!, JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject(), request.Headers.Authorization?.ToString()));
        var payload = Streams.For(provider, requests.Count == 1);
        if (provider == "chatgpt") payload = payload.Replace("\"name\":\"document_get\"", "\"name\":\"document_get\",\"namespace\":\"shadow\"", StringComparison.Ordinal);
        return new(HttpStatusCode.OK) { Content = new StringContent(payload, Encoding.UTF8, "text/event-stream") };
    });
    using var http = new HttpClient(handler);
    var configuration = new ShadowAgentProviderOptions { Provider = provider, Model = "verification-model", ApiKey = "test-secret", Endpoint = "https://provider.example/v1" };
    using var client = ShadowAgentProviders.Create(configuration, http);
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
    if (provider is "openai" or "chatgpt")
    {
        Check(requests[0].Authorization == "Bearer test-secret", provider + " uses bearer API credential");
        Check(requests[0].Uri.AbsolutePath == "/v1/responses", provider + " uses Responses SDK endpoint");
        Check(requests[0].Body["store"]?.GetValue<bool>() == false, provider + " disables server-side history storage");
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
    client.Dispose();
    using var alive = await http.GetAsync("https://provider.example/alive");
    Check(alive.IsSuccessStatusCode, provider + " leaves injected HttpClient usable");
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
        if (!tool) events.Add("""{"type":"response.output_text.delta","sequence_number":2,"item_id":"msg_1","output_index":0,"content_index":0,"delta":"done"}""");
        events.Add("""{"type":"response.output_item.done","sequence_number":3,"output_index":0,"item":""" + item + "}");
        events.Add("""{"type":"response.completed","sequence_number":4,"response":{"id":"resp_1","created_at":1,"model":"verification-model","status":"completed","store":false,"output":[""" + item + """],"usage":{"input_tokens":11,"output_tokens":4,"total_tokens":15}}}""");
        return string.Concat(events.Select(value => "data: " + value + "\n\n"));
    }
    private static string Anthropic(bool tool)
    {
        string[] events =
        [
            """{"type":"message_start","message":{"id":"msg_1","type":"message","role":"assistant","model":"verification-model","content":[],"stop_reason":null,"stop_sequence":null,"usage":{"input_tokens":11,"output_tokens":1}}}""",
            tool ? """{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"call_1","name":"document_get","input":{}}}"""
                : """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            tool ? """{"type":"content_block_delta","index":0,"delta":{"type":"input_json_delta","partial_json":"{}"}}"""
                : """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"done"}}""",
            """{"type":"content_block_stop","index":0}""",
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

