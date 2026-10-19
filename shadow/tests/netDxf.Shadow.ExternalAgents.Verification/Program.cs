using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Net.WebSockets;
using netDxf.Shadow.Mcp;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using netDxf.Shadow.AgentHost;

if (args.FirstOrDefault() is "app-server" or "--acp-fixture")
{ await FakeAgent.RunAsync(args[0] == "--acp-fixture", args.Contains("--stall-initialize", StringComparer.Ordinal)); return; }

const string token = "synthetic-external-agent-bridge-token-32-bytes";
var count = 0;
var directory = Path.Combine(Path.GetTempPath(), "netdxf-external-fixture-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
var executable = Path.Combine(AppContext.BaseDirectory, "netDxf.Shadow.ExternalAgents.Verification" + (OperatingSystem.IsWindows() ? ".exe" : ""));
var options = new AgentHostOptions(port, ["https://studio.example"], token, Path.Combine(directory, "host.json"), workspace: directory,
    codexExecutable: executable, acpExecutable: executable, acpArguments: ["--acp-fixture"]);
await using var app = AgentHostApplication.Build(options, services => services.AddSingleton(new ExternalAgentHostSettings {
    ApprovalTimeout = TimeSpan.FromSeconds(2), RequestTimeout = TimeSpan.FromSeconds(8), MaximumEventBytes = 2048, MaximumBufferedEventBytes = 8192 }));
using var http = new HttpClient { BaseAddress = new($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(12) };
await app.StartAsync();
try
{
    Check((await http.GetAsync("/agents/profiles")).StatusCode == HttpStatusCode.Unauthorized, "agent routes require companion authorization");
    Check((await http.PostAsJsonAsync("/agents/start", new { profile = "codex" })).StatusCode == HttpStatusCode.Unauthorized, "process start requires companion authorization");
    http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    using (var forbidden = new HttpRequestMessage(HttpMethod.Get, "/agents/events"))
    { forbidden.Headers.Add("Origin", "https://untrusted.example"); Check((await http.SendAsync(forbidden)).StatusCode == HttpStatusCode.Forbidden, "agent events reject an untrusted browser origin"); }
    var profiles = await Get("/agents/profiles"); Check(profiles.GetProperty("profiles").EnumerateArray().All(p => p.GetProperty("enabled").GetBoolean()), "only startup-configured profiles are offered");
    Check((await http.GetAsync("/agents/events?after=-1")).StatusCode == HttpStatusCode.BadRequest, "invalid event cursor is a bounded client error");
    var started = await Post("/agents/start", new { profile = "codex", executable = "/untrusted/command", cwd = "/", arguments = new[] { "--unsafe" } });
    var processId = started.GetProperty("initialization").GetProperty("fixtureProcessId").GetInt32();
    Check(Canonical(started.GetProperty("initialization").GetProperty("cwd").GetString()!) == Canonical(directory), "Codex child runs in startup-granted workspace");
    var startup = started.GetProperty("initialization").GetProperty("startupArguments").GetRawText();
    Check(startup.Contains("stdio://", StringComparison.Ordinal) && startup.Contains("bearer_token_env_var", StringComparison.Ordinal) && !startup.Contains(token, StringComparison.Ordinal) && !startup.Contains("--unsafe", StringComparison.Ordinal), "Codex startup uses official transport/config with credential only in child environment");
    Check((await Get("/agents/events")).GetProperty("connected").GetBoolean(), "successful initialization is connected");
    await Reject("/agents/start", new { profile = "arbitrary-executable" }, "unknown profile cannot launch a browser-selected executable");
    Check((await Get("/agents/events")).GetProperty("connected").GetBoolean(), "invalid start does not destroy the current process");
    var account = await Call("account/read", new { refreshToken = true, arbitrary = "dropped" });
    Check(account.GetProperty("echo").EnumerateObject().Count() == 1 && !account.GetProperty("echo").GetProperty("refreshToken").GetBoolean(), "account read cannot request external token refresh");
    await RejectCall("account/login/start", new { type = "apiKey", apiKey = "synthetic-never-sent" }, "login accepts managed browser/device flow only");
    var login = await Call("account/login/start", new { type = "chatgptDeviceCode", apiKey = "synthetic-never-sent" });
    Check(login.GetProperty("echo").EnumerateObject().Count() == 1, "login forwards only managed login type");
    await RejectCall("command/exec", new { command = new[] { "not-executed" } }, "raw command execution method is not browser-exposed");
    await RejectCall("thread/start", new { }, "thread creation requires explicit model selection");
    var thread = await Call("thread/start", new { model = "selected-model", cwd = "/", sandbox = "danger-full-access", approvalPolicy = "never", config = new { bypass = true }, mcpServers = new[] { "untrusted" } });
    var echo = thread.GetProperty("echo");
    Check(echo.GetProperty("model").GetString() == "selected-model" && echo.GetProperty("cwd").GetString() == directory && echo.GetProperty("sandbox").GetString() == "workspaceWrite" && echo.GetProperty("approvalPolicy").GetString() == "unlessTrusted" && echo.EnumerateObject().Count() == 4, "thread policy/cwd fixed by host and model preserved exactly");
    var resume = await Call("thread/resume", new { threadId = "thread-fixture", model = "selected-model", excludeTurns = false });
    Check(resume.GetProperty("echo").GetProperty("excludeTurns").GetBoolean(), "resume uses paginated history");
    var read = await Call("thread/read", new { threadId = "thread-fixture", includeTurns = true });
    Check(!read.GetProperty("echo").GetProperty("includeTurns").GetBoolean(), "thread read avoids unbounded full history");
    await RejectCall("model/list", new { limit = 101 }, "model discovery page budget enforced");
    var turns = await Call("thread/turns/list", new { threadId = "thread-fixture", cursor = "opaque", limit = 7, itemsView = "full", sortDirection = "asc", arbitrary = "dropped" });
    Check(turns.GetProperty("echo").GetProperty("limit").GetInt32() == 7 && turns.GetProperty("echo").EnumerateObject().Count() == 5, "history pagination preserves official bounded fields");
    var items = await Call("thread/items/list", new { threadId = "thread-fixture", turnId = "turn-fixture", cursor = new { type = "item", itemId = "item-fixture", privatePath = "/" } });
    Check(items.GetProperty("echo").GetProperty("cursor").EnumerateObject().Count() == 2, "item anchor cursor strips unknown fields");
    await RejectCall("thread/items/list", new { threadId = "thread-fixture", cursor = new { type = "item", itemId = "item-fixture" } }, "item cursor requires its turn scope");
    await RejectCall("turn/start", new { threadId = "thread-fixture", input = new[] { new { type = "localImage", path = "/private/file" } } }, "browser cannot make agent open arbitrary local input files");
    await RejectCall("turn/start", new { threadId = "thread-fixture", input = Text(new string('x', 65537)) }, "oversized text is rejected before child dispatch");
    using (var duplicate = await http.PostAsync("/agents/call", new StringContent("{\"method\":\"account/read\",\"parameters\":{\"refreshToken\":true,\"refreshToken\":false}}", Encoding.UTF8, "application/json")))
        Check(duplicate.StatusCode == HttpStatusCode.BadRequest, "duplicate parameter keys are rejected");
    var textResult = await Call("turn/start", new { threadId = "thread-fixture", input = new[] { new { type = "text", text = "normal", unsafePath = "/" } }, cwd = "/", sandbox = "danger-full-access" });
    Check(textResult.GetProperty("echo").EnumerateObject().Count() == 2 && textResult.GetProperty("echo").GetProperty("input")[0].EnumerateObject().Count() == 2, "turn policy and content extensions cannot widen host grant");

    var drawingReference = new DrawingObjectReference(Guid.NewGuid(), "live", 0, 7, "fixture-object", "A1", "netDxf.Entities.Line", "Model", "fixture-occurrence", []);
    var referencedCall = new { method = "turn/start", parameters = new { threadId = "thread-fixture", input = Text("Referenced drawing task") }, references = new[] { drawingReference } };
    await Reject("/agents/call", referencedCall, "external dispatch requires authoritative connected drawing validation");
    using (var browser = new ClientWebSocket())
    {
        browser.Options.SetRequestHeader("Origin", "https://studio.example");
        await browser.ConnectAsync(new($"ws://127.0.0.1:{port}/studio"), CancellationToken.None);
        await Send(browser, new { type = "register", token, tools = new[] { new { name = DrawingReferences.ValidationTool, description = "Fixture validation", readOnly = true,
            inputSchema = new { type = "object", properties = new { references = new { type = "array", items = new { type = "object" } }, refresh = new { type = "boolean" } }, additionalProperties = false } } } });
        _ = await Receive(browser);
        var pendingReference = Post("/agents/call", referencedCall);
        var validationCall = await Receive(browser);
        Check(validationCall.GetProperty("name").GetString() == DrawingReferences.ValidationTool, "external prompt checks the actual connected drawing tool");
        var validation = new DrawingReferenceValidation(new(drawingReference.DocumentId, "live", 0, 7), true,
            [new(drawingReference, "current", drawingReference, "Current")]);
        await Send(browser, new { type = "result", id = validationCall.GetProperty("id").GetString(), result = new { text = JsonSerializer.Serialize(validation, McpJson.Options), isError = false } });
        var referenceResult = await pendingReference;
        Check(referenceResult.GetProperty("echo").GetProperty("input")[0].GetProperty("text").GetString()!.Contains("<shadow-drawing-references-v1>", StringComparison.Ordinal), "external protocol receives server-generated reference declaration after validation");
        var staleCall = http.PostAsJsonAsync("/agents/call", referencedCall); validationCall = await Receive(browser);
        validation = validation with { CanSend = false, Items = [new(drawingReference, "deleted", null, "Deleted")] };
        await Send(browser, new { type = "result", id = validationCall.GetProperty("id").GetString(), result = new { text = JsonSerializer.Serialize(validation, McpJson.Options), isError = false } });
        using var staleResponse = await staleCall;
        Check(staleResponse.StatusCode == HttpStatusCode.BadRequest, "external server rejects deleted references before forwarding a turn");
        browser.Abort();
    }

    var approvalTurn = Call("turn/start", new { threadId = "thread-fixture", input = Text("approval") });
    var approval = await Pending(); var requestId = approval.GetProperty("requestId").GetString()!;
    Check(!approvalTurn.IsCompleted, "turn remains pending while agent waits for approval");
    await Call("model/list", new { });
    Check(!approvalTurn.IsCompleted, "reader and HTTP dispatch remain live during server callback");
    await Reject("/agents/reply", new { requestId, result = new { decision = "acceptForSession" } }, "unoffered persistent approval rejected");
    await Post("/agents/reply", new { requestId, result = new { decision = "decline", permissions = new { sandbox = "full" } } });
    var approved = await approvalTurn;
    Check(approved.GetProperty("approval").GetProperty("result").EnumerateObject().Count() == 1 && approved.GetProperty("approval").GetProperty("result").GetProperty("decision").GetString() == "decline", "approval forwards only chosen per-action decision");
    await Reject("/agents/reply", new { requestId, result = new { decision = "accept" } }, "resolved approval cannot be replayed");

    var questionTurn = Call("turn/start", new { threadId = "thread-fixture", input = Text("question") });
    requestId = (await Pending()).GetProperty("requestId").GetString()!;
    await Reject("/agents/reply", new { requestId, result = new { answers = new { q1 = new { answers = new[] { "invented" } } } } }, "question answer must match offered choices");
    await Reject("/agents/reply", new { requestId, result = new { answers = new { q1 = new { answers = new[] { "One" } }, q2 = new { answers = new[] { "extra" } } } } }, "question answer cannot add unrelated IDs");
    await Reject("/agents/reply", new { requestId, result = new { answers = new { q1 = new { answers = new[] { "One", "Two" } } } } }, "question requires one answer per requested ID");
    await Post("/agents/reply", new { requestId, result = new { answers = new { q1 = new { answers = new[] { "One" }, extra = true } }, extra = true } });
    var answered = await questionTurn;
    Check(answered.GetProperty("approval").GetProperty("result").EnumerateObject().Count() == 1 && answered.GetProperty("approval").GetProperty("result").GetProperty("answers").GetProperty("q1").EnumerateObject().Count() == 1, "question result is canonical and extension-free");

    var expiredTurn = Call("turn/start", new { threadId = "thread-fixture", input = Text("expire") });
    requestId = (await Pending()).GetProperty("requestId").GetString()!;
    var expired = await expiredTurn;
    Check(expired.GetProperty("approval").TryGetProperty("error", out _), "expired Codex callback fails closed without an implicit decision");
    await WaitPoll(p => p.GetProperty("pending").GetArrayLength() == 0);
    await Reject("/agents/reply", new { requestId, result = new { decision = "accept" } }, "expired approval cannot be accepted later");
    var interruptedTurn = Call("turn/start", new { threadId = "thread-fixture", input = Text("wait") });
    requestId = (await Pending()).GetProperty("requestId").GetString()!;
    await Call("turn/interrupt", new { threadId = "thread-fixture", turnId = "turn-fixture" }); await interruptedTurn;
    await WaitPoll(p => p.GetProperty("pending").GetArrayLength() == 0);
    await Reject("/agents/reply", new { requestId, result = new { decision = "accept" } }, "server-resolved callback disappears after interrupt");
    await Call("turn/start", new { threadId = "thread-fixture", input = Text("flood") });
    var flood = await Get("/agents/events"); Check(flood.GetProperty("truncated").GetBoolean() && flood.GetRawText().Length < 12000, "event replay buffer is bounded by bytes with truncation marker");
    await Call("turn/start", new { threadId = "thread-fixture", input = Text("large-event") });
    var large = await Get("/agents/events"); Check(large.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("method").GetString() == "fixture/large" && e.GetProperty("data").GetProperty("truncated").GetBoolean()), "oversized single event has explicit truncation metadata");
    var secretError = await http.PostAsJsonAsync("/agents/call", new { method = "turn/start", parameters = new { threadId = "thread-fixture", input = Text("secret") } });
    Check(secretError.StatusCode == HttpStatusCode.BadRequest && !(await secretError.Content.ReadAsStringAsync()).Contains(token, StringComparison.Ordinal), "child error cannot echo companion credential");
    var redactedEvents = await Get("/agents/events");
    Check(!redactedEvents.GetRawText().Contains(token, StringComparison.Ordinal), "scalar event and method metadata credential echoes are redacted");
    Check(redactedEvents.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("method").GetString() == "fixture/[companion credential]"),
        "credential redaction precedes buffering external protocol method names");
    await http.PostAsJsonAsync("/agents/call", new { method = "turn/start", parameters = new { threadId = "thread-fixture", input = Text("exit") } });
    var dead = await WaitPoll(p => p.GetProperty("status").GetString() == "disconnected");
    Check(!dead.GetProperty("connected").GetBoolean() && dead.GetProperty("protocol").GetString() == "codex", "unexpected child exit reports disconnected without stale ready state");
    await RejectCall("model/list", new { }, "calls fail promptly after child exit");
    await Post("/agents/stop", new { }); Check(Exited(processId), "stop reaps exited child process");

    started = await Post("/agents/start", new { profile = "acp" }); processId = started.GetProperty("initialization").GetProperty("fixtureProcessId").GetInt32();
    var auth = await Call("authenticate", new { methodId = "managed", apiKey = "synthetic-never-sent" });
    Check(auth.GetProperty("echo").EnumerateObject().Count() == 1, "ACP authentication forwards advertised method ID only");
    var session = await Call("session/new", new { cwd = "/", additionalDirectories = new[] { "/" }, mcpServers = new[] { new { command = "/untrusted/command" } } });
    echo = session.GetProperty("echo");
    Check(echo.GetProperty("cwd").GetString() == directory && echo.EnumerateObject().Count() == 2 && echo.GetProperty("mcpServers")[0].GetProperty("url").GetString() == $"http://127.0.0.1:{port}/mcp", "ACP setup uses only granted cwd and companion MCP transport");
    Check(!echo.GetRawText().Contains(token, StringComparison.Ordinal), "ACP echoed MCP auth header redacted");
    var loaded = await Call("session/resume", new { sessionId = "session-fixture", cwd = "/", additionalDirectories = new[] { "/" } });
    Check(loaded.GetProperty("echo").EnumerateObject().Count() == 3 && loaded.GetProperty("echo").GetProperty("cwd").GetString() == directory, "ACP resume follows identical workspace boundary");
    var listed = await Call("session/list", new { cwd = "/", cursor = "next", arbitrary = true });
    Check(listed.GetProperty("echo").GetProperty("cwd").GetString() == directory && listed.GetProperty("echo").EnumerateObject().Count() == 2, "ACP history list stays in granted workspace");
    await RejectCall("terminal/create", new { sessionId = "session-fixture", command = "not-executed" }, "ACP terminal request is not browser-exposed");
    var acpTurn = Call("session/prompt", new { sessionId = "session-fixture", prompt = Text("approval") });
    requestId = (await Pending()).GetProperty("requestId").GetString()!;
    await Call("session/list", new { }); Check(!acpTurn.IsCompleted, "ACP pending callback does not block protocol reader");
    await Reject("/agents/reply", new { requestId, result = new { optionId = "unoffered" } }, "ACP permission requires offered option ID");
    await Post("/agents/reply", new { requestId, result = new { optionId = "reject-once", arbitrary = true } });
    var acpApproved = await acpTurn;
    Check(acpApproved.GetProperty("approval").GetProperty("result").GetProperty("outcome").GetProperty("optionId").GetString() == "reject-once", "ACP selected option maps to official outcome response");
    var acpExpired = Call("session/prompt", new { sessionId = "session-fixture", prompt = Text("expire") }); await Pending();
    Check((await acpExpired).GetProperty("approval").GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString() == "cancelled", "expired ACP permission returns cancelled outcome");
    var acpCancelled = Call("session/prompt", new { sessionId = "session-fixture", prompt = Text("wait") }); await Pending();
    await Call("session/cancel", new { sessionId = "session-fixture" }); Check((await acpCancelled).GetProperty("stopReason").GetString() == "cancelled", "ACP cancellation completes pending prompt without reader deadlock");
    await WaitPoll(p => p.GetProperty("pending").GetArrayLength() == 0);
    var stopping = CallStatus("session/prompt", new { sessionId = "session-fixture", prompt = Text("wait") }); await Pending();
    await Post("/agents/stop", new { }); Check((await stopping) is HttpStatusCode.Conflict or HttpStatusCode.BadRequest, "stop cancels outstanding HTTP request and approval");
    Check(Exited(processId), "stop terminates only owned fake ACP child");
    var stopped = await Get("/agents/events"); Check(!stopped.GetProperty("connected").GetBoolean() && stopped.GetProperty("pending").GetArrayLength() == 0, "stopped host has no stale pending approvals");
    var starts = new[] { Post("/agents/start", new { profile = "codex" }), Post("/agents/start", new { profile = "acp" }) };
    await Task.WhenAll(starts);
    Check((await Get("/agents/events")).GetProperty("connected").GetBoolean(), "simultaneous profile starts serialize process ownership");
    await Task.WhenAll(Post("/agents/stop", new { }), Post("/agents/stop", new { }));
    Check(!(await Get("/agents/events")).GetProperty("connected").GetBoolean(), "concurrent stop is idempotent");
    await CheckHandshakeCancellation();
    Console.WriteLine($"PASS {count} credential-free external agent HTTP checks");
}
finally { await app.StopAsync(); Directory.Delete(directory, true); }

void Check(bool pass, string name) { if (!pass) throw new InvalidOperationException(name); count++; }
static object[] Text(string value) => [new { type = "text", text = value }];
static bool Exited(int pid) { try { using var process = Process.GetProcessById(pid); return process.HasExited; } catch (ArgumentException) { return true; } }
async Task<JsonElement> Get(string path) { using var response = await http.GetAsync(path); response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
async Task<JsonElement> Post(string path, object value)
{
    using var response = await http.PostAsJsonAsync(path, value); var json = await response.Content.ReadFromJsonAsync<JsonElement>();
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"{path}: {response.StatusCode} {json}"); return json;
}
Task<JsonElement> Call(string method, object parameters) => Post("/agents/call", new { method, parameters });
async Task<HttpStatusCode> CallStatus(string method, object parameters) { using var response = await http.PostAsJsonAsync("/agents/call", new { method, parameters }); return response.StatusCode; }
async Task Reject(string path, object value, string name) { using var response = await http.PostAsJsonAsync(path, value); Check(response.StatusCode == HttpStatusCode.BadRequest, name); }
Task RejectCall(string method, object parameters, string name) => Reject("/agents/call", new { method, parameters }, name);
async Task<JsonElement> WaitPoll(Func<JsonElement, bool> predicate)
{
    var timer = Stopwatch.StartNew();
    while (timer.Elapsed < TimeSpan.FromSeconds(6)) { var poll = await Get("/agents/events"); if (predicate(poll)) return poll; await Task.Delay(15); }
    throw new TimeoutException("Expected fixture state was not observed.");
}
async Task<JsonElement> Pending() => (await WaitPoll(p => p.GetProperty("pending").GetArrayLength() != 0)).GetProperty("pending")[0].Clone();

static string Canonical(string path)
{
    var full = Path.GetFullPath(path); var result = Path.GetPathRoot(full)!;
    foreach (var component in full[result.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
    { var item = new DirectoryInfo(Path.Combine(result, component)); result = item.ResolveLinkTarget(true)?.FullName ?? item.FullName; }
    return result;
}

async Task CheckHandshakeCancellation()
{
    var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var otherPort = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    var otherOptions = new AgentHostOptions(otherPort, [], token, Path.Combine(directory, "stall-host.json"), workspace: directory,
        acpExecutable: executable, acpArguments: ["--acp-fixture", "--stall-initialize"]);
    await using var other = AgentHostApplication.Build(otherOptions);
    using var client = new HttpClient { BaseAddress = new($"http://127.0.0.1:{otherPort}"), Timeout = TimeSpan.FromSeconds(6) };
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    await other.StartAsync();
    try
    {
        var starting = client.PostAsJsonAsync("/agents/start", new { profile = "acp" });
        var timer = Stopwatch.StartNew(); var pid = 0;
        while (timer.Elapsed < TimeSpan.FromSeconds(4))
        {
            var poll = await client.GetFromJsonAsync<JsonElement>("/agents/events");
            var initialized = poll.GetProperty("events").EnumerateArray().FirstOrDefault(e => e.GetProperty("method").GetString() == "fixture/initializing");
            if (initialized.ValueKind != JsonValueKind.Undefined)
            { pid = initialized.GetProperty("data").GetProperty("fixtureProcessId").GetInt32(); Check(poll.GetProperty("status").GetString() == "starting", "handshake exposes starting state without readiness"); break; }
            await Task.Delay(15);
        }
        Check(pid != 0 && !starting.IsCompleted, "fake handshake remains pending before stop");
        timer.Restart(); using var stopped = await client.PostAsJsonAsync("/agents/stop", new { });
        using var cancelled = await starting;
        Check(stopped.IsSuccessStatusCode && cancelled.StatusCode == HttpStatusCode.Conflict && timer.Elapsed < TimeSpan.FromSeconds(3), "stop cancels blocked handshake without waiting for request timeout");
        Check(Exited(pid), "cancelled handshake does not orphan its child");
    }
    finally { await other.StopAsync(); }
}

static Task Send(ClientWebSocket socket, object value) => socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, CancellationToken.None);
static async Task<JsonElement> Receive(ClientWebSocket socket)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var stream = new MemoryStream(); var buffer = new byte[16384]; WebSocketReceiveResult read;
    do { read = await socket.ReceiveAsync(buffer, timeout.Token); if (read.MessageType != WebSocketMessageType.Text) throw new IOException("Expected fixture frame."); stream.Write(buffer, 0, read.Count); } while (!read.EndOfMessage);
    using var document = JsonDocument.Parse(stream.ToArray()); return document.RootElement.Clone();
}
