using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using netDxf.Shadow.Agents.Acp;

if (args.FirstOrDefault() == "--fixture") { await Fixture.Run(args.ElementAtOrDefault(1) ?? "normal"); return; }
var checks = 0;
var notifications = new ConcurrentQueue<(string Method, JsonElement Data)>();
var permissionSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var permissions = 0; var files = 0; var terminals = 0; var elicitations = 0;
var services = new AcpClientServices
{
    Notification = (method, data) => notifications.Enqueue((method, data)),
    RequestPermissionAsync = (request, ct) =>
    {
        Interlocked.Increment(ref permissions); permissionSeen.TrySetResult();
        Check(request.Options.GetArrayLength() == 2 && request.ToolCall.GetProperty("toolCallId").GetString() == "edit1", "Permission tool/options preserved.");
        return Task.FromResult<string?>("allow");
    },
    ReadTextFileAsync = (p, ct) => { Interlocked.Increment(ref files); return Task.FromResult(J(new { content = "file from editor" })); },
    WriteTextFileAsync = (p, ct) => { Interlocked.Increment(ref files); return Task.FromResult(J(new { })); },
    TerminalRequestAsync = (method, p, ct) =>
    {
        Interlocked.Increment(ref terminals);
        return Task.FromResult(method switch
        {
            "terminal/create" => J(new { terminalId = "t1" }), "terminal/output" => J(new { output = "ok", truncated = false }),
            "terminal/wait_for_exit" => J(new { exitCode = 0 }), _ => J(new { })
        });
    },
    ElicitFormAsync = (p, ct) => { Interlocked.Increment(ref elicitations); return Task.FromResult(J(new { action = "accept", content = new { name = "example" } })); },
    ElicitUrlAsync = (p, ct) => { Interlocked.Increment(ref elicitations); return Task.FromResult(J(new { action = "decline" })); }
};
await using (var client = await AcpClient.StartAsync(Launch("normal"), services))
{
    Check(client.Initialization.GetProperty("protocolVersion").GetInt32() == 1, "Negotiates stable ACP v1.");
    await Throws<AcpException>(() => client.RequestAsync("authenticate", J(new { methodId = "absent" })), "Rejects unknown auth.");
    await Throws<AcpException>(() => client.RequestAsync("authenticate", J(new { methodId = "interactive" })), "Does not send terminal auth to authenticate.");
    await client.RequestAsync("authenticate", J(new { methodId = "login" }));
    var session = await client.RequestAsync("session/new", Setup()); var id = session.GetProperty("sessionId").GetString()!;
    Check(id == "s1", "Creates a session.");
    var listed = await client.RequestAsync("session/list", J(new { cursor = "opaque" }));
    Check(listed.GetProperty("nextCursor").GetString() == "next", "Preserves opaque pagination cursor.");
    await client.RequestAsync("session/set_mode", J(new { sessionId = id, modeId = "ask" }));
    await Throws<AcpException>(() => client.RequestAsync("session/set_mode", J(new { sessionId = id, modeId = "invented" })), "Rejects unknown mode.");
    await client.RequestAsync("session/set_config_option", J(new { sessionId = id, configId = "model", value = "model-b" }));
    await client.RequestAsync("session/set_config_option", J(new { sessionId = id, configId = "safe", type = "boolean", value = true }));
    await Throws<AcpException>(() => client.RequestAsync("session/set_config_option", J(new { sessionId = id, configId = "model", value = "absent" })), "Rejects unknown config option value.");
    var result = await client.RequestAsync("session/prompt", Prompt("basic"));
    Check(result.GetProperty("stopReason").GetString() == "end_turn", "Prompt completes only at the protocol response.");
    Check(permissions == 1 && files == 2 && terminals == 5, "Services dispatched without blocking the reader.");
    Check(elicitations == 2, "Form and URL elicitation dispatched only to opted-in services.");
    Check(client.Initialization.GetProperty("clientCaps").GetProperty("elicitation").GetProperty("url").ValueKind == JsonValueKind.Object,
        "Only supplied elicitation modes are advertised.");
    Check(notifications.Any(e => e.Data.TryGetProperty("update", out var u) && u.TryGetProperty("sessionUpdate", out var k) && k.GetString() == "tool_call_update"), "Tool patches forwarded.");
    Check(notifications.Any(e => e.Data.TryGetProperty("update", out var u) && u.TryGetProperty("content", out var c) && c.TryGetProperty("text", out var t) && t.GetString() == "Hello 世界"), "UTF-8 message chunks preserved.");
    Check(notifications.Any(e => e.Method == "_fixture/metadata" && e.Data.GetProperty("custom").GetInt32() == 7), "Unknown notification metadata forwarded.");
    await Throws<AcpException>(() => client.RequestAsync("session/prompt", J(new { sessionId = id, prompt = new[] { new { type = "image", data = "AA==", mimeType = "image/png" } } })), "Prompt capability gate blocks images.");
    await Throws<AcpException>(() => client.RequestAsync("session/new", J(new { cwd = "relative", mcpServers = Array.Empty<object>() })), "Rejects relative workspace.");
    await Throws<AcpException>(() => client.RequestAsync("session/new", J(new { cwd = Path.GetTempPath(), mcpServers = new[] { new { type = "http", name = "studio", url = "http://127.0.0.1:1/mcp", headers = Array.Empty<object>() } } })), "Unsupported MCP transport rejected.");
    await client.RequestAsync("session/delete", J(new { sessionId = id }));
    await client.RequestAsync("session/load", J(new { sessionId = id, cwd = Path.GetTempPath(), mcpServers = Array.Empty<object>() }));
    await client.RequestAsync("session/close", J(new { sessionId = id }));
    await Throws<AcpException>(() => client.RequestAsync("session/prompt", Prompt("done")), "Closed sessions cannot prompt.");
    await client.RequestAsync("session/resume", J(new { sessionId = id, cwd = Path.GetTempPath() }));
    await client.RequestAsync("logout", J(new { }));
    await Throws<AcpException>(() => client.RequestAsync("session/prompt", Prompt("basic")), "Logout clears local sessions.");
}
await using (var minimal = await AcpClient.StartAsync(Launch("minimal")))
{
    await Throws<AcpException>(() => minimal.RequestAsync("session/load", J(new { sessionId = "s1", cwd = Path.GetTempPath(), mcpServers = Array.Empty<object>() })), "Load capability gated.");
    await Throws<AcpException>(() => minimal.RequestAsync("session/list", J(new { })), "List capability gated.");
    await Throws<AcpException>(() => minimal.RequestAsync("session/resume", J(new { sessionId = "s1", cwd = Path.GetTempPath() })), "Resume capability gated.");
    await Throws<AcpException>(() => minimal.RequestAsync("logout", J(new { })), "Logout capability gated.");
    await minimal.RequestAsync("session/new", Setup());
    var denied = await minimal.RequestAsync("session/prompt", Prompt("permission"));
    Check(denied.GetProperty("permissionOutcome").GetString() == "cancelled", "No permission callback fails closed.");
    Check(!minimal.Initialization.GetProperty("clientCaps").GetProperty("terminal").GetBoolean(), "No implicit terminal capability.");
}
await using (var invalid = await AcpClient.StartAsync(Launch("normal"), new() { RequestPermissionAsync = (r, ct) => Task.FromResult<string?>("invented") }))
{
    await invalid.RequestAsync("session/new", Setup());
    var result = await invalid.RequestAsync("session/prompt", Prompt("invalid-choice"));
    Check(result.GetProperty("permissionError").GetInt32() == -32602, "Unadvertised permission selection is never authorized.");
}
await Throws<AcpException>(async () => { await using var rejected = await AcpClient.StartAsync(Launch("version2")); }, "Unsupported negotiated version closes connection.");
await using (var required = await AcpClient.StartAsync(Launch("auth")))
{
    try { await required.RequestAsync("session/new", Setup()); throw new Exception("Expected auth_required."); }
    catch (AcpException e) { Check(e.Code == -32000 && e.ErrorData!.Value.GetProperty("reason").GetString() == "auth_required", "Auth-required code/data preserved."); }
    await required.RequestAsync("authenticate", J(new { methodId = "login" }));
    await required.RequestAsync("session/new", Setup());
}
var cancelPermission = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
await using (var client = await AcpClient.StartAsync(Launch("normal"), new()
{
    RequestPermissionAsync = async (request, ct) => { cancelPermission.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return "allow"; }
}))
{
    await client.RequestAsync("session/new", Setup()); using var cancel = new CancellationTokenSource();
    var pending = client.RequestAsync("session/prompt", Prompt("cancel"), cancel.Token);
    await cancelPermission.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Throws<AcpException>(() => client.RequestAsync("session/prompt", Prompt("basic")), "Concurrent same-session prompt refused.");
    await Throws<AcpException>(() => client.RequestAsync("session/load", J(new { sessionId = "s1", cwd = Path.GetTempPath(), mcpServers = Array.Empty<object>() })), "Cannot reload an active turn into a second session state.");
    cancel.Cancel(); await Throws<OperationCanceledException>(() => pending, "Caller cancellation sends session/cancel and drains permission.");
    Check(client.IsConnected, "Acknowledged cancellation keeps the connection reusable.");
    var next = await client.RequestAsync("session/prompt", Prompt("done"));
    Check(next.GetProperty("stopReason").GetString() == "end_turn", "Next prompt waits until prior turn completes.");
}
await using (var client = await AcpClient.StartAsync(Launch("normal"), options: new() { CancellationGrace = TimeSpan.FromMilliseconds(100) }))
{
    await client.RequestAsync("session/new", Setup()); using var cancel = new CancellationTokenSource(200);
    await Throws<OperationCanceledException>(() => client.RequestAsync("session/prompt", Prompt("hang"), cancel.Token), "Unacknowledged cancellation is bounded.");
    Check(!client.IsConnected, "Unacknowledged cancellation closes uncertain session transport.");
}
var elicitationSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (var releaseElicitation = new ManualResetEventSlim())
await using (var client = await AcpClient.StartAsync(Launch("normal"), new()
{
    ElicitFormAsync = (request, ct) =>
    {
        elicitationSeen.TrySetResult();
        releaseElicitation.Wait(TimeSpan.FromSeconds(5));
        return Task.FromResult(J(new { action = "accept", content = new { value = "late acceptance" } }));
    }
}))
{
    await client.RequestAsync("session/new", Setup());
    var pending = client.RequestAsync("session/prompt", Prompt("cancel-elicitation"));
    try
    {
        await elicitationSeen.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await client.CancelSessionAsync("s1");
    }
    finally { releaseElicitation.Set(); }
    var response = await pending.WaitAsync(TimeSpan.FromSeconds(5));
    Check(response.GetProperty("elicitation").GetProperty("action").GetString() == "cancel"
        && !response.GetProperty("elicitation").TryGetProperty("content", out _),
        "Session cancellation cannot return a late synchronous elicitation acceptance or its content.");
}
foreach (var failure in new[] { "malformed", "oversized", "eof" })
{
    await using var client = await AcpClient.StartAsync(Launch("normal"), options: new() { MaximumMessageBytes = 4096 });
    await client.RequestAsync("session/new", Setup());
    await Throws<Exception>(() => client.RequestAsync("session/prompt", Prompt(failure)), "Transport rejects " + failure);
    Check(!client.IsConnected, "Transport failure is visible to host.");
    Check(client.ConnectionError is not null, "Transport preserves the original failure for diagnostics.");
    if (failure == "eof") Check(client.ConnectionError is EndOfStreamException, "Unexpected process exit is identified as EOF rather than a request timeout.");
}
await using (var disconnected = new AcpClient(Stream.Null, new MemoryStream()))
{
    await disconnected.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    await Throws<EndOfStreamException>(() => disconnected.InitializeAsync(), "A peer that exits before initialization reports its disconnect, not cancellation.");
}
await using (var client = await AcpClient.StartAsync(Launch("normal"), new()
{
    ReadTextFileAsync = async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); return J(new { content = "never" }); }
}))
{
    await client.RequestAsync("session/new", Setup());
    await Throws<Exception>(() => client.RequestAsync("session/prompt", Prompt("duplicate-request-id")).WaitAsync(TimeSpan.FromSeconds(5)),
        "Escaped duplicate request IDs terminate the uncertain transport.");
    Check(!client.IsConnected, "ACP duplicate incoming IDs compare decoded strings rather than JSON spellings.");
}
var oldSecret = Environment.GetEnvironmentVariable("SHADOW_AGENT_BRIDGE_TOKEN");
await using (var client = await AcpClient.StartAsync(Launch("pending"), options: new() { MaximumPendingRequests = 4 }))
{
    using var cancel = new CancellationTokenSource();
    var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var rejectedCount = 0; var cancelledCount = 0;
    var calls = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
    {
        await start.Task;
        try { await client.RequestAsync("session/list", J(new { }), cancel.Token); }
        catch (AcpException error) when (error.Code == -32000) { if (Interlocked.Increment(ref rejectedCount) == 28) rejected.TrySetResult(); }
        catch (OperationCanceledException) { Interlocked.Increment(ref cancelledCount); }
    })).ToArray();
    start.TrySetResult();
    try { await rejected.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
    finally { cancel.Cancel(); }
    await Task.WhenAll(calls);
    Check(rejectedCount == 28 && cancelledCount == 4, "Concurrent requests respect the atomic pending-request bound.");
}
try
{
    Environment.SetEnvironmentVariable("SHADOW_AGENT_BRIDGE_TOKEN", "verification-only-do-not-inherit");
    await using var client = await AcpClient.StartAsync(Launch("environment"));
    Check(!client.Initialization.GetProperty("secretInherited").GetBoolean(), "Companion credentials not inherited by child.");
}
finally { Environment.SetEnvironmentVariable("SHADOW_AGENT_BRIDGE_TOKEN", oldSecret); }
// Reexecute the framework-dependent apphost with only an explicit runtime installation,
// as setup-dotnet does on macOS. Assert inheritance even on machines with a global runtime.
var runtimeNames = new[] { "DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64", "DOTNET_ROOT(x86)", "DOTNET_ROLL_FORWARD" };
var previousRuntime = runtimeNames.ToDictionary(name => name, Environment.GetEnvironmentVariable);
var runtimeRoot = Directory.GetParent(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory().TrimEnd(Path.DirectorySeparatorChar))!.Parent!.Parent!.FullName;
try
{
    foreach (var name in runtimeNames) Environment.SetEnvironmentVariable(name, name == "DOTNET_ROLL_FORWARD" ? "LatestPatch" : runtimeRoot);
    var apphost = Path.ChangeExtension(Assembly.GetExecutingAssembly().Location, OperatingSystem.IsWindows() ? ".exe" : null);
    await using var client = await AcpClient.StartAsync(new(apphost, Path.GetTempPath(), ["--fixture", "environment"]));
    var inherited = client.Initialization.GetProperty("runtimeEnvironment");
    foreach (var name in runtimeNames)
        Check(inherited.GetProperty(name).GetString() == Environment.GetEnvironmentVariable(name), "Inherits required runtime setting " + name);
}
finally { foreach (var (name, value) in previousRuntime) Environment.SetEnvironmentVariable(name, value); }
await Throws<ArgumentOutOfRangeException>(() => AcpClient.StartAsync(Launch("normal"), options: new() { MaximumMessageBytes = 0 }), "Rejects invalid connection limits before launching a child.");
await Throws<ArgumentException>(() => AcpClient.StartAsync(Launch("normal") with { Arguments = ["bad\0argument"] }), "Rejects invalid process arguments before launch.");
await Throws<ArgumentException>(() => AcpClient.StartAsync(Launch("normal") with { Environment = new Dictionary<string, string> { ["invalid=name"] = "value" } }), "Rejects malformed child environment before launch.");
Console.WriteLine($"{checks} ACP verification checks passed (local protocol fixtures; no provider credentials or network inference).");

void Check(bool condition, string message) { Interlocked.Increment(ref checks); if (!condition) throw new InvalidOperationException(message); }
async Task Throws<T>(Func<Task> action, string message) where T : Exception
{ try { await action(); } catch (T) { Interlocked.Increment(ref checks); return; } throw new InvalidOperationException(message); }
AcpProcessOptions Launch(string scenario) => new(Environment.ProcessPath!, Path.GetTempPath(),
    string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)
        ? new[] { Assembly.GetExecutingAssembly().Location, "--fixture", scenario } : new[] { "--fixture", scenario });
static JsonElement Setup() => J(new { cwd = Path.GetTempPath(), mcpServers = Array.Empty<object>() });
static JsonElement Prompt(string text) => J(new { sessionId = "s1", prompt = new[] { new { type = "text", text } } });
static JsonElement J<T>(T value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
