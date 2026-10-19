using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Agents.Codex;

if (args.FirstOrDefault() == "app-server")
{
    // This executable is a local fixture process, never the installed Codex binary.
    await Console.Error.WriteAsync(new string('x', 128 * 1024));
    string? line;
    while ((line = await Console.In.ReadLineAsync()) is not null)
    {
        using var message = JsonDocument.Parse(line);
        if (message.RootElement.TryGetProperty("id", out var id))
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(new { id, result = new { pid = Environment.ProcessId,
                ambientSecret = Environment.GetEnvironmentVariable("SHADOW_UNRELATED_SECRET_FIXTURE") is not null,
                explicitValue = Environment.GetEnvironmentVariable("SHADOW_EXPLICIT_FIXTURE") } }));
    }
    return;
}

var count = 0;
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException("FAIL: " + name); count++; }
async Task Throws<T>(Func<Task> action, string name) where T : Exception
{
    try { await action().WaitAsync(TimeSpan.FromSeconds(5)); }
    catch (T) { count++; return; }
    throw new InvalidOperationException("FAIL: " + name);
}
JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

await using (var fixture = await Fixture.OpenAsync())
{
    var client = fixture.Client;
    await Throws<InvalidOperationException>(() => client.ReadAccountAsync(), "inference/auth require handshake");
    var initialize = client.InitializeAsync("shadow_fixture", "Shadow fixture", "1.0");
    var start = await fixture.Read();
    Check(start.GetProperty("method").GetString() == "initialize" && !start.TryGetProperty("jsonrpc", out _), "official headerless JSON-RPC initialize");
    Check(start.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString() == "shadow_fixture", "stable client attribution");
    Check(!start.GetProperty("params").GetProperty("capabilities").GetProperty("experimentalApi").GetBoolean(), "experimental features require explicit opt-in");
    await fixture.Reply(start, new { userAgent = "fixture" }); await initialize;
    Check((await fixture.Read()).GetProperty("method").GetString() == "initialized" && client.IsInitialized, "initialized notification follows successful response");
    await Throws<InvalidOperationException>(() => client.InitializeAsync("x", "x", "1"), "connection initializes only once");
    var account = client.ReadAccountAsync(); var models = client.ListModelsAsync("next-page");
    var accountRequest = await fixture.Read(); var modelRequest = await fixture.Read();
    Check(!accountRequest.GetProperty("params").GetProperty("refreshToken").GetBoolean(), "account status does not request token refresh");
    Check(modelRequest.GetProperty("params").GetProperty("cursor").GetString() == "next-page", "model catalog preserves pagination");
    await fixture.Reply(modelRequest, new { data = new[] { new { id = "explicit-model" } } }); await fixture.Reply(accountRequest, new { account = new { type = "chatgpt" } });
    Check((await models).GetProperty("data")[0].GetProperty("id").GetString() == "explicit-model" && (await account).GetProperty("account").GetProperty("type").GetString() == "chatgpt", "out-of-order responses correlate by ID");
    foreach (var deviceCode in new[] { false, true })
    {
        var login = client.BeginChatGptLoginAsync(deviceCode); var request = await fixture.Read();
        Check(request.GetProperty("params").GetProperty("type").GetString() == (deviceCode ? "chatgptDeviceCode" : "chatgpt"), "managed browser/device login stays in Codex");
        await fixture.Reply(request, new { loginId = "login-fixture", authUrl = "https://auth.openai.com/fixture" });
        Check((await login).GetProperty("loginId").GetString() == "login-fixture", "login metadata returned for user ceremony");
    }
    var seen = new List<string>(); var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Notification += notification => { lock (seen) seen.Add(notification.Method); if (notification.Method == "turn/completed") done.TrySetResult(); };
    var thread = client.StartThreadAsync("explicit-model", Path.GetTempPath()); var requestThread = await fixture.Read();
    Check(requestThread.GetProperty("params").GetProperty("model").GetString() == "explicit-model" && requestThread.GetProperty("params").GetProperty("sandbox").GetString() == "readOnly", "thread preserves explicit model and read-only default");
    await fixture.Reply(requestThread, new { thread = new { id = "thread-fixture" } }); await thread;
    var turn = client.StartTurnAsync("thread-fixture", "Inspect the drawing"); var requestTurn = await fixture.Read();
    Check(requestTurn.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString() == "Inspect the drawing", "turn prompt uses official text input shape");
    await fixture.Send(new { method = "item/agentMessage/delta", @params = new { threadId = "thread-fixture", delta = "Hello" } });
    await fixture.Reply(requestTurn, new { turn = new { id = "turn-fixture", status = "inProgress" } }); await turn;
    var approval = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously); var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.RequestHandler = async (request, token) =>
    {
        Check(request.Method == "item/commandExecution/requestApproval" && request.Id.GetString() == "approval-fixture", "server approval preserves method and string ID");
        reached.TrySetResult(); return await approval.Task.WaitAsync(token);
    };
    await fixture.Send(new { id = "approval-fixture", method = "item/commandExecution/requestApproval", @params = new { threadId = "thread-fixture", turnId = "turn-fixture", command = "fixture-command" } });
    await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var interrupt = client.InterruptTurnAsync("thread-fixture", "turn-fixture"); var requestInterrupt = await fixture.Read();
    Check(requestInterrupt.GetProperty("method").GetString() == "turn/interrupt", "pending user approval does not block cancellation requests");
    await fixture.Reply(requestInterrupt, new { }); await interrupt;
    approval.SetResult(Json(new { decision = "decline" })); var decision = await fixture.Read();
    Check(decision.GetProperty("id").GetString() == "approval-fixture" && decision.GetProperty("result").GetProperty("decision").GetString() == "decline", "explicit host decision returned without automatic approval");
    client.RequestHandler = null;
    await fixture.Send(new { id = 45, method = "unknown/permission", @params = new { } });
    Check((await fixture.Read()).GetProperty("error").GetProperty("code").GetInt32() == -32601, "unhandled server operations fail closed");
    await fixture.Send(new { method = "turn/completed", @params = new { turn = new { id = "turn-fixture", status = "interrupted" } } }); await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(seen.SequenceEqual(new[] { "item/agentMessage/delta", "turn/completed" }), "streamed notifications preserve order and terminal outcome");
    using var cancellation = new CancellationTokenSource(); var cancelled = client.ReadAccountAsync(cancellation.Token); var cancelledRequest = await fixture.Read();
    cancellation.Cancel(); await Throws<OperationCanceledException>(async () => await cancelled, "caller can stop waiting for a request");
    await fixture.Reply(cancelledRequest, new { ignored = true });
    var failure = client.ReadAccountAsync(); var failedRequest = await fixture.Read();
    await fixture.Send(new { id = failedRequest.GetProperty("id"), error = new { code = -32001, message = "fixture error", data = new { retryable = true } } });
    await Throws<CodexRpcException>(async () => await failure, "typed remote failure never retries the request");
    var steering = client.SteerTurnAsync("thread-fixture", "turn-fixture", "Use the selected layer");
    var steerRequest = await fixture.Read();
    Check(steerRequest.GetProperty("method").GetString() == "turn/steer"
        && steerRequest.GetProperty("params").GetProperty("expectedTurnId").GetString() == "turn-fixture"
        && steerRequest.GetProperty("params").GetProperty("input")[0].GetProperty("text").GetString() == "Use the selected layer",
        "steering targets the observed active turn with canonical text input");
    await fixture.Reply(steerRequest, new { turnId = "turn-fixture" });
    Check((await steering).GetProperty("turnId").GetString() == "turn-fixture", "steering preserves accepted turn identity");
    var staleSteering = client.SteerTurnAsync("thread-fixture", "stale-turn", "Do not start a new turn");
    var staleSteerRequest = await fixture.Read();
    await fixture.Send(new { id = staleSteerRequest.GetProperty("id"), error = new { code = -32602, message = "Expected turn does not match active turn" } });
    await Throws<CodexRpcException>(() => staleSteering, "a rejected steering request is never retried as a new turn");
    await Throws<ArgumentException>(() => client.SteerTurnAsync("thread-fixture", "", "text"), "steering requires an explicit expected turn");
    var compaction = client.CompactThreadAsync("thread-fixture"); var compactRequest = await fixture.Read();
    Check(compactRequest.GetProperty("method").GetString() == "thread/compact/start"
        && compactRequest.GetProperty("params").EnumerateObject().Count() == 1, "compaction sends only the target thread ID");
    await fixture.Reply(compactRequest, new { }); await compaction;
    var compactDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    client.Notification += notification => { if (notification.Method == "item/completed" && notification.Parameters.GetProperty("item").GetProperty("type").GetString() == "contextCompaction") compactDone.TrySetResult(); };
    Check(!compactDone.Task.IsCompleted, "compaction acknowledgement does not invent a completion event");
    await fixture.Send(new { method = "item/completed", @params = new { threadId = "thread-fixture", item = new { id = "compact-1", type = "contextCompaction" } } });
    await compactDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(client.IsInitialized, "compaction activity remains an ordinary streamed notification");
}

await using (var malformed = await Fixture.OpenAsync(1024))
{
    var initialize = malformed.Client.InitializeAsync("x", "x", "1"); await malformed.Read();
    await malformed.WriteRaw(new string('x', 1025));
    await Throws<IOException>(async () => await initialize, "oversized incoming frame closes pending handshake");
    await malformed.Client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(malformed.Client.ConnectionError is IOException, "closed connection exposes protocol failure");
}
await using (var duplicate = await Fixture.OpenAsync())
{
    var invocations = 0;
    duplicate.Client.RequestHandler = (_, _) => { invocations++; return ValueTask.FromResult(Json(new { decision = "decline" })); };
    await duplicate.Send(new { id = "duplicate", method = "item/fileChange/requestApproval", @params = new { } }); await duplicate.Read();
    await duplicate.WriteRaw("{\"id\":\"dupli\\u0063ate\",\"method\":\"item/fileChange/requestApproval\",\"params\":{}}\n");
    await duplicate.Client.Completion.WaitAsync(TimeSpan.FromSeconds(5));
    Check(invocations == 1 && duplicate.Client.ConnectionError is IOException, "JSON-escaped duplicate server request never repeats a side effect");
}
await using (var resolved = await Fixture.OpenAsync())
{
    var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    resolved.Client.RequestHandler = async (_, token) =>
    {
        prompted.SetResult();
        try { await Task.Delay(Timeout.Infinite, token); return Json(new { decision = "accept" }); }
        finally { cleared.TrySetResult(); }
    };
    await resolved.Send(new { id = 12, method = "item/fileChange/requestApproval", @params = new { } });
    await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await resolved.Send(new { method = "serverRequest/resolved", @params = new { requestId = 12, threadId = "fixture" } });
    await cleared.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(!resolved.Client.Completion.IsCompleted, "server-resolved approval cancels its UI handler without terminating connection");
}

await using (var synchronous = await Fixture.OpenAsync())
{
    var prompted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var notified = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var release = new ManualResetEventSlim();
    synchronous.Client.RequestHandler = (_, token) =>
    {
        using var registration = token.Register(release.Set);
        prompted.TrySetResult();
        release.Wait(TimeSpan.FromSeconds(5));
        return ValueTask.FromResult(Json(new { decision = "accept" }));
    };
    synchronous.Client.Notification += _ => throw new InvalidOperationException("Synthetic broken UI observer.");
    synchronous.Client.Notification += _ => notified.TrySetResult();
    try
    {
        await synchronous.Send(new { id = "synchronous", method = "item/fileChange/requestApproval", @params = new { } });
        await prompted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await synchronous.WriteRaw("{\"method\":\"serverRequest/resolved\",\"params\":{\"requestId\":\"synchro\\u006eous\"}}\n");
        await notified.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Check(release.IsSet, "reader cancels a server request while its callback blocks synchronously");
        Check(!synchronous.Client.Completion.IsCompleted && synchronous.Client.ConnectionError is null,
            "broken notification observer does not disconnect or suppress later observers");
    }
    finally { release.Set(); }
}

var executable = Path.Combine(AppContext.BaseDirectory, "netDxf.Shadow.Codex.Verification" + (OperatingSystem.IsWindows() ? ".exe" : ""));
Environment.SetEnvironmentVariable("SHADOW_UNRELATED_SECRET_FIXTURE", "synthetic-fixture-only");
CodexAppServerClient child;
try
{
    child = await CodexAppServerClient.StartAsync(new() { Executable = executable, WorkingDirectory = AppContext.BaseDirectory,
        Environment = new Dictionary<string, string> { ["SHADOW_EXPLICIT_FIXTURE"] = "explicit-fixture" } });
}
finally { Environment.SetEnvironmentVariable("SHADOW_UNRELATED_SECRET_FIXTURE", null); }
var initializedChild = await child.InitializeAsync("shadow_fixture", "Fixture", "1");
var pid = initializedChild.GetProperty("pid").GetInt32();
Check(pid != Environment.ProcessId, "process transport starts only the fixture executable and drains stderr");
Check(!initializedChild.GetProperty("ambientSecret").GetBoolean() && initializedChild.GetProperty("explicitValue").GetString() == "explicit-fixture",
    "process excludes ambient secrets and honors explicit host-only environment");
await child.DisposeAsync();
try { using var exited = Process.GetProcessById(pid); await exited.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
catch (ArgumentException) { }
Check(child.Completion.IsCompleted, "process disposal terminates owned child and reader");
Console.WriteLine($"PASS: {count} Codex protocol, auth, streaming, approval, cancellation, bounds and process-lifetime fixture checks. No installed Codex or credentials used.");

sealed class Fixture(TcpClient local, TcpClient remote, int maximum) : IAsyncDisposable
{
    public CodexAppServerClient Client { get; } = new(local.GetStream(), local.GetStream(), maximum, TimeSpan.FromSeconds(5));
    private readonly StreamReader reader = new(remote.GetStream(), new UTF8Encoding(false), leaveOpen: true);
    private readonly StreamWriter writer = new(remote.GetStream(), new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
    public static async Task<Fixture> OpenAsync(int maximum = 4 * 1024 * 1024)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var local = new TcpClient(); var accepted = listener.AcceptTcpClientAsync();
            await local.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            return new(local, await accepted, maximum);
        }
        finally { listener.Stop(); }
    }
    public async Task<JsonElement> Read()
    {
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)) ?? throw new EndOfStreamException();
        using var value = JsonDocument.Parse(line); return value.RootElement.Clone();
    }
    public Task Send(object value) => writer.WriteLineAsync(JsonSerializer.Serialize(value));
    public Task Reply(JsonElement request, object value) => Send(new { id = request.GetProperty("id"), result = value });
    public Task WriteRaw(string value) => writer.WriteAsync(value);
    public async ValueTask DisposeAsync() { await Client.DisposeAsync(); await writer.DisposeAsync(); reader.Dispose(); local.Dispose(); remote.Dispose(); }
}
