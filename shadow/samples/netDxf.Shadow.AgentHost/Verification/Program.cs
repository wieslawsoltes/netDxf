using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using netDxf.Shadow.AgentHost;
using netDxf.Shadow.Mcp;

if (args is ["--command-child", var markerDirectory])
{
    await File.WriteAllTextAsync(Path.Combine(markerDirectory, "started"), Environment.ProcessId.ToString());
    await Task.Delay(TimeSpan.FromSeconds(30));
    await File.WriteAllTextAsync(Path.Combine(markerDirectory, "completed"), "completed");
    return;
}

const string token = "verification-bridge-token-32-bytes-long";
const string origin = "https://studio.example";
var count = 0;
var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var directory = Path.Combine(Path.GetTempPath(), "netdxf-agenthost-verification-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var options = new AgentHostOptions(port, [origin], token, Path.Combine(directory, "host.json"));
await using var app = AgentHostApplication.Build(options);
await app.StartAsync();
try
{
    using var http = new HttpClient { BaseAddress = new($"http://127.0.0.1:{port}") };
    Check((await http.GetAsync("/health")).StatusCode == HttpStatusCode.Unauthorized, "health requires bridge token");
    Check((await http.GetAsync("/mcp")).StatusCode == HttpStatusCode.Unauthorized, "MCP requires bridge token");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    Check((await http.GetAsync("/health")).StatusCode == HttpStatusCode.OK, "native authenticated requests work");
    Check((await http.GetFromJsonAsync<McpToolDefinition[]>("/workspace/tools", McpJson.Options)) is { Length: 0 }, "native workspace catalog empty without startup grant");
    Check((await http.PostAsync("/workspace/call", JsonContent.Create(new { name = "companion_workspace_list" }))).StatusCode == HttpStatusCode.NotFound, "native mutations unavailable without startup grant");
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/health"))
    {
        request.Headers.Add("Origin", "https://untrusted.example");
        Check((await http.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "untrusted origin rejected with valid token");
    }
    using (var request = new HttpRequestMessage(HttpMethod.Get, "/health"))
    {
        request.Headers.Host = "rebound.example:" + port;
        Check((await http.SendAsync(request)).StatusCode == HttpStatusCode.Forbidden, "DNS rebinding Host rejected");
    }
    using (var request = new HttpRequestMessage(HttpMethod.Options, "/v1/responses"))
    {
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type,x-ms-useragent");
        request.Headers.Add("Access-Control-Request-Private-Network", "true");
        using var response = await http.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.NoContent, "allowed browser CORS preflight works");
        Check(response.Headers.GetValues("Access-Control-Allow-Origin").Single() == origin, "CORS returns exact allowed origin");
        Check(response.Headers.GetValues("Access-Control-Allow-Private-Network").Single() == "true", "allowed browser can request local network access");
    }
    using (var response = await http.GetAsync("/auth/status"))
    {
        using var status = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Check(!status.RootElement.GetProperty("authenticated").GetBoolean(), "starts without OAuth credentials");
    }
    Check((await http.GetAsync("/auth/callback?state=invalid")).StatusCode == HttpStatusCode.BadRequest, "unsolicited OAuth callback rejected");
    using (var response = await http.PostAsync("/v1/responses", new StringContent("{\"model\":\"test\",\"input\":[]}", Encoding.UTF8, "application/json")))
        Check(response.StatusCode == HttpStatusCode.Unauthorized, "inference requires an authenticated ChatGPT session");

    using (var rejected = new ClientWebSocket())
    {
        rejected.Options.SetRequestHeader("Origin", "https://untrusted.example");
        await Throws<WebSocketException>(() => rejected.ConnectAsync(new($"ws://127.0.0.1:{port}/studio"), CancellationToken.None), "websocket rejects untrusted origin");
    }
    using (var rejected = await Connect())
    {
        await Send(rejected, Registration("wrong-token"));
        Check(await IsPolicyClosed(rejected), "websocket registration requires bridge token");
    }
    using var browser = await Connect();
    await Send(browser, Registration(token));
    using (var registered = await Receive(browser)) Check(registered.RootElement.GetProperty("type").GetString() == "registered", "studio catalog registered");
    using (var other = await Connect())
    {
        await Send(other, Registration(token));
        Check(await IsPolicyClosed(other), "second studio cannot replace active document");
    }

    await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(new()
    {
        Endpoint = new($"http://127.0.0.1:{port}/mcp"), AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
    }));
    var tools = await mcp.ListToolsAsync();
    Check(tools.Select(tool => tool.Name).Order().SequenceEqual(new[] { "studio_edit", "studio_state" }), "official MCP SDK discovers active studio tools");
    var call = mcp.CallToolAsync("studio_state");
    using (var request = await Receive(browser))
    {
        var root = request.RootElement;
        Check(root.GetProperty("type").GetString() == "call" && root.GetProperty("name").GetString() == "studio_state", "MCP call reaches exact registered browser tool");
        await Send(browser, new { type = "result", id = root.GetProperty("id").GetString(), result = new { text = "{\"revision\":7}", isError = false } });
    }
    var result = await call;
    Check(result.Content.OfType<TextContentBlock>().Single().Text == "{\"revision\":7}" && result.IsError != true, "correlated result returns through official MCP transport");

    using (var cancel = new CancellationTokenSource())
    {
        var canceledCall = mcp.CallToolAsync("studio_state", cancellationToken: cancel.Token);
        using var request = await Receive(browser);
        var callId = request.RootElement.GetProperty("id").GetString();
        cancel.Cancel();
        await Throws<OperationCanceledException>(async () => { await canceledCall; }, "MCP caller cancellation is honored");
        using var notification = await Receive(browser);
        Check(notification.RootElement.GetProperty("type").GetString() == "cancel" && notification.RootElement.GetProperty("id").GetString() == callId, "cancellation reaches corresponding browser operation");
    }

    var interrupted = mcp.CallToolAsync("studio_edit");
    using (var request = await Receive(browser)) Check(request.RootElement.GetProperty("name").GetString() == "studio_edit", "mutation dispatched once");
    browser.Abort();
    var interruptedResult = await interrupted.AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    Check(interruptedResult.IsError == true && interruptedResult.Content.OfType<TextContentBlock>().Single().Text.Contains("No call was replayed", StringComparison.Ordinal), "disconnect reports uncertain mutation outcome without replay");
    using var replacement = await Connect();
    await Send(replacement, Registration(token, "other_document_state"));
    using (var registered = await Receive(replacement)) Check(registered.RootElement.GetProperty("type").GetString() == "registered", "explicit reconnect accepts replacement catalog");
    Check((await mcp.ListToolsAsync()).Select(tool => tool.Name).SequenceEqual(new[] { "other_document_state" }), "catalog changes only after explicit reconnect");
    Check((await mcp.CallToolAsync("studio_edit")).IsError == true, "stale tool cannot dispatch to different document");
    replacement.Abort();
    var saved = await File.ReadAllTextAsync(options.StateFile);
    Check(saved.Contains("urn:uuid:", StringComparison.Ordinal) && !saved.Contains(token, StringComparison.Ordinal), "only nonsecret host registration persists");
    await VerifyNativeWorkspace();
    Console.WriteLine($"PASS: {count} loopback host, auth, origin, WebSocket relay, native workspace and official MCP transport checks.");
}
finally
{
    await app.StopAsync();
    Directory.Delete(directory, true);
}

async Task VerifyNativeWorkspace()
{
    var root = Path.Combine(directory, "workspace");
    Directory.CreateDirectory(root);
    await File.WriteAllTextAsync(Path.Combine(root, "example.cs"), "class Before { }");
    var next = new TcpListener(IPAddress.Loopback, 0);
    next.Start(); var nativePort = ((IPEndPoint)next.LocalEndpoint).Port; next.Stop();
    var nativeOptions = new AgentHostOptions(nativePort, [origin], token, Path.Combine(directory, "native-host.json"), root, allowProcesses: true);
    var fileOnly = new CompanionWorkspace(new(nativePort, [origin], token, nativeOptions.StateFile, root));
    Check(fileOnly.Tools.All(tool => tool.Name.StartsWith("companion_workspace_", StringComparison.Ordinal))
        && fileOnly.Tools.All(tool => tool.Name != "companion_workspace_execute"), "file grant cannot silently acquire process tool");
    Check((await fileOnly.ExecuteAsync("companion_workspace_execute", McpJson.EmptyObject)).IsError, "ungranted process calls rejected");
    await Throws<ArgumentException>(() => Task.Run(() => new AgentHostOptions(nativePort, [origin], token, nativeOptions.StateFile, allowProcesses: true)), "process grant requires explicit workspace");
    await using var nativeApp = AgentHostApplication.Build(nativeOptions);
    await nativeApp.StartAsync();
    try
    {
        using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{nativePort}") };
        Check((await client.GetAsync("/workspace/tools")).StatusCode == HttpStatusCode.Unauthorized, "native file catalog requires bridge token");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var catalog = await client.GetFromJsonAsync<McpToolDefinition[]>("/workspace/tools", McpJson.Options) ?? [];
        Check(catalog.Length == 7 && catalog.Any(tool => tool.Name == "companion_workspace_execute" && !tool.ReadOnly), "native grant exposes prefixed file and mutating process tools");
        var read = await Call("companion_workspace_read", new { path = "example.cs" });
        using var file = JsonDocument.Parse(read.Text);
        var version = file.RootElement.GetProperty("version").GetInt64();
        Check(!read.IsError && file.RootElement.GetProperty("content").GetString() == "class Before { }", "browser can read explicitly granted native file");
        var write = await Call("companion_workspace_write", new { path = "example.cs", content = "class After { }", expectedVersion = version });
        Check(!write.IsError && await File.ReadAllTextAsync(Path.Combine(root, "example.cs")) == "class After { }", "native write persists with compare-and-swap version");
        Check((await Call("companion_workspace_write", new { path = "example.cs", content = "unexpected", expectedVersion = version })).IsError, "stale native edit is rejected without replay");
        Check((await Call("companion_workspace_read", new { path = "../host.json" })).IsError, "file tools reject paths outside granted directory");
        Check((await Call("workspace_read", new { path = "example.cs" })).IsError, "native endpoint does not alias browser workspace names");
        using (var denied = new HttpRequestMessage(HttpMethod.Post, "/workspace/call")
        { Content = JsonContent.Create(new { name = "companion_workspace_delete", arguments = new { path = "example.cs", expectedVersion = version } }) })
        {
            denied.Headers.Add("Origin", "https://untrusted.example");
            Check((await client.SendAsync(denied)).StatusCode == HttpStatusCode.Forbidden && File.Exists(Path.Combine(root, "example.cs")), "native mutations honor origin boundary before dispatch");
        }
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        var command = await Call("companion_workspace_execute", new { executable = dotnet, arguments = new[] { "--version" }, timeoutSeconds = 30 });
        using var commandResult = JsonDocument.Parse(command.Text);
        Check(!command.IsError && commandResult.RootElement.GetProperty("exitCode").GetInt32() == 0
            && commandResult.RootElement.GetProperty("standardOutput").GetString()!.Trim().Length > 0, "explicit process grant runs a real tool and captures output");

        var markers = Path.Combine(root, "markers"); Directory.CreateDirectory(markers);
        using var canceled = new CancellationTokenSource();
        var execution = client.PostAsJsonAsync("/workspace/call", new
        {
            name = "companion_workspace_execute", arguments = new
            {
                executable = dotnet,
                arguments = new[] { typeof(Program).Assembly.Location, "--command-child", markers },
                timeoutSeconds = 30,
            },
        }, canceled.Token);
        using var startDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(Path.Combine(markers, "started"))) await Task.Delay(25, startDeadline.Token);
        var processId = int.Parse(await File.ReadAllTextAsync(Path.Combine(markers, "started")));
        canceled.Cancel();
        await Throws<OperationCanceledException>(async () => { using var response = await execution; }, "browser cancellation cancels native HTTP command");
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (ArgumentException) { /* The child already exited before inspection. */ }
        Check(!File.Exists(Path.Combine(markers, "completed")), "canceled native command is terminated without finishing or replaying");

        async Task<McpToolResult> Call(string name, object arguments)
        {
            using var response = await client.PostAsJsonAsync("/workspace/call", new { name, arguments });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<McpToolResult>(McpJson.Options) ?? throw new InvalidOperationException("Missing native tool result.");
        }
    }
    finally { await nativeApp.StopAsync(); }
}

void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException("FAIL: " + name);
    count++;
}
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action(); }
    catch (T) { count++; return; }
    throw new InvalidOperationException("FAIL: " + name);
}
async Task<ClientWebSocket> Connect()
{
    var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Origin", origin);
    await socket.ConnectAsync(new($"ws://127.0.0.1:{port}/studio"), CancellationToken.None); return socket;
}
object Registration(string key, string? name = null) => new
{
    type = "register", token = key,
    tools = (name is null ? new[] { "studio_state", "studio_edit" } : new[] { name }).Select(tool => new
    {
        name = tool, description = "Verification tool", readOnly = tool != "studio_edit",
        inputSchema = new { type = "object", properties = new { }, additionalProperties = false },
    }).ToArray(),
};
static Task Send(ClientWebSocket socket, object value) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, CancellationToken.None);
static async Task<JsonDocument> Receive(ClientWebSocket socket)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var result = new MemoryStream(); var buffer = new byte[16_384]; WebSocketReceiveResult read;
    do
    {
        read = await socket.ReceiveAsync(buffer, timeout.Token);
        if (read.MessageType != WebSocketMessageType.Text) throw new InvalidOperationException("Expected WebSocket JSON text.");
        result.Write(buffer, 0, read.Count);
    } while (!read.EndOfMessage);
    return JsonDocument.Parse(result.ToArray());
}
static async Task<bool> IsPolicyClosed(ClientWebSocket socket)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var received = await socket.ReceiveAsync(new byte[4096], timeout.Token);
    return received.MessageType == WebSocketMessageType.Close && received.CloseStatus == WebSocketCloseStatus.PolicyViolation;
}
