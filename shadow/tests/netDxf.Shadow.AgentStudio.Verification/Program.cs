using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

var checks = 0;
void Assert(bool value, string description)
{
    if (!value) throw new InvalidOperationException("Verification failed: " + description);
    checks++;
}
void Expect<TException>(Action operation, string description) where TException : Exception
{
    try { operation(); }
    catch (TException) { checks++; return; }
    throw new InvalidOperationException("Expected " + typeof(TException).Name + ": " + description);
}
async Task ExpectAsync<TException>(Func<Task> operation, string description) where TException : Exception
{
    try { await operation(); }
    catch (TException) { checks++; return; }
    throw new InvalidOperationException("Expected " + typeof(TException).Name + ": " + description);
}
JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, McpJson.Options);

const string apiKey = "fixture-provider-credential-not-a-real-key";
const string companionToken = "fixture-companion-credential-not-a-real-token";
var companionStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var companionCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var companionExecutions = 0;
var builder = WebApplication.CreateSlimBuilder();
builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var host = builder.Build();
// Provider calls in this executable are local fixtures, never requests to a billed service.
host.MapPost("/v1/responses", () => Results.Json(new { error = new { code = "invalid_api_key", message = "Fixture rejected inference.", type = "invalid_request_error" } }, statusCode: 401));
host.MapGet("/workspace/tools", (HttpContext context) =>
{
    if (context.Request.Headers.Authorization != "Bearer " + companionToken) return Results.Unauthorized();
    return Results.Json(new[] { new McpToolDefinition("companion_probe", "Local cancellation fixture", McpJson.Parse("""{"type":"object","properties":{"wait":{"type":"boolean"}},"additionalProperties":false}""")) }, McpJson.Options);
});
host.MapPost("/workspace/call", async (HttpContext context) =>
{
    if (context.Request.Headers.Authorization != "Bearer " + companionToken) { context.Response.StatusCode = 401; return; }
    Interlocked.Increment(ref companionExecutions);
    using var input = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
    if (input.RootElement.GetProperty("arguments").TryGetProperty("wait", out var wait) && wait.GetBoolean())
    {
        companionStarted.TrySetResult();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
        catch (OperationCanceledException) { companionCanceled.TrySetResult(); }
        return;
    }
    await context.Response.WriteAsJsonAsync(new McpToolResult("Native fixture rejected the command.", true), McpJson.Options);
});
await host.StartAsync();
var endpoint = host.Urls.Single();
var openDocuments = new HashSet<string>(StringComparer.Ordinal) { "demo", "second" };
var mutations = 0;
McpToolRegistry Tools(string document)
{
    if (!openDocuments.Contains(document)) throw new ObjectDisposedException("The drawing is closed.");
    var registry = new McpToolRegistry();
    registry.Register(new("drawing_state", "Read the bound document", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}"""), true), (_, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(!openDocuments.Contains(document), "The drawing is closed.");
        return ValueTask.FromResult(McpToolResult.FromObject(new { document }));
    });
    registry.Register(new("drawing_change", "Commit one fixture change", McpJson.Parse("""{"type":"object","properties":{},"additionalProperties":false}""")), (_, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(!openDocuments.Contains(document), "The drawing is closed.");
        mutations++; return ValueTask.FromResult(new McpToolResult("Committed."));
    });
    return registry;
}

await using (var controller = new StudioAgentController(Tools, (_, _) => throw new InvalidOperationException("Refresh fixture is unavailable.")))
{
    var config = new StudioAgentConfiguration { ApiKey = apiKey, Endpoint = endpoint + "/v1", PermissionMode = "fullAccess" };
    controller.Configure(config);
    Expect<ArgumentNullException>(() => controller.Start(null!), "a null prompt is rejected synchronously");
    Expect<ArgumentException>(() => controller.Start(new string('x', 256001)), "an oversized prompt is rejected synchronously");
    Assert(!controller.Session.IsRunning, "invalid starts do not leave a phantom running operation");
    Expect<ArgumentException>(() => controller.Configure(config with { ApiKey = null! }), "null configuration strings fail before replacing a session");
    var pin = controller.Pin("Keep " + apiKey + " private.");
    Assert(!pin.Text.Contains(apiKey, StringComparison.Ordinal) && !controller.Export().Contains(apiKey, StringComparison.Ordinal),
        "provider credentials are redacted before pins and exported checkpoints");
    controller.SetDocument("second");
    var second = await controller.Call("drawing_state", McpJson.EmptyObject);
    Assert(JsonDocument.Parse(second.Text).RootElement.GetProperty("document").GetString() == "second", "switching drawing binds a fresh registry");
    controller.Configure(config);
    Assert(controller.DocumentId == "demo", "configuring a previously used drawing does not incorrectly short-circuit after a document switch");
    var mutation = await controller.Call("drawing_change", McpJson.EmptyObject);
    Assert(!mutation.IsError && mutations == 1, "an observational UI refresh failure cannot turn a committed mutation into a failed receipt");
    openDocuments.Remove("demo");
    Assert((await controller.Call("drawing_change", McpJson.EmptyObject)).IsError && mutations == 1,
        "a registry bound to a closed drawing cannot execute source operations");
    var disconnected = Json(await controller.Disconnect());
    Assert(!disconnected.GetProperty("configured").GetBoolean(), "disconnect clears the provider even after its drawing closes");
    Assert(controller.Catalog().Any(t => t.Name == "workspace_read") && !controller.Catalog().Any(t => t.Name == "drawing_change"),
        "an unavailable drawing leaves only disconnected local workspace and plan capabilities");
    Expect<InvalidOperationException>(() => controller.Start("must not infer"), "a disconnected controller cannot start inference");
    controller.SetDocument("second");
    Assert(controller.Catalog().Any(t => t.Name == "drawing_state"), "a newly selected live drawing restores source capabilities");
}
openDocuments.Add("demo");

// A real manual compaction fault is stored in the controller's work task. It must not
// block later credential cleanup or make DisposeAsync permanently skip its cleanup path.
foreach (var disposeAfterFault in new[] { false, true })
{
    var controller = new StudioAgentController(Tools);
    controller.Configure(new() { ApiKey = apiKey, Endpoint = endpoint + "/v1", PermissionMode = "fullAccess" });
    var checkpoint = JsonNode.Parse(controller.Export())!.AsObject();
    checkpoint["messages"] = new JsonArray(Enumerable.Range(0, 8).Select(i => (JsonNode)new JsonObject
    {
        ["role"] = i % 2 == 0 ? "user" : "assistant",
        ["contents"] = new JsonArray(new JsonObject { ["kind"] = "text", ["text"] = "completed round " + i })
    }).ToArray());
    controller.Import(checkpoint.ToJsonString());
    for (var index = 0; index < 512; index++) controller.Write("capacity/" + index + ".txt", "occupied", 0);
    await ExpectAsync<InvalidOperationException>(() => controller.Compact(), "manual compaction fails if its exact archive cannot fit in the workspace");
    Assert(!controller.Session.IsRunning, "failed manual compaction releases the current operation");
    if (disposeAfterFault)
    {
        await controller.DisposeAsync();
        Expect<ObjectDisposedException>(() => controller.Configure(new()), "disposal completes after observing a prior faulted work task");
        await controller.DisposeAsync();
        Assert(true, "disposal is idempotent after a prior operation fault");
    }
    else
    {
        var disconnected = Json(await controller.Disconnect());
        Assert(!disconnected.GetProperty("configured").GetBoolean(), "disconnect succeeds after a faulted manual compaction task");
        Expect<InvalidOperationException>(() => controller.Start("must not infer"), "disconnect after a fault removes inference authorization");
        await controller.DisposeAsync();
    }
}

await using (var controller = new StudioAgentController(Tools))
{
    controller.Configure(new() { PermissionMode = "fullAccess" });
    var connected = Json(await controller.CompanionConnect(endpoint, companionToken));
    Assert(connected.GetProperty("connected").GetBoolean() && controller.Catalog().Any(t => t.Name == "companion_probe"),
        "an explicitly authenticated loopback companion installs its native catalog");
    var pending = controller.Call("companion_probe", McpJson.Element(new { wait = true }));
    await companionStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
    Expect<InvalidOperationException>(() => controller.CompanionDisconnect(), "native tools cannot be disposed while a call is in flight");
    var disconnected = Json(await controller.Disconnect());
    await companionCanceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    var interrupted = await pending;
    Assert(interrupted.IsError && companionExecutions == 1, "disconnect cancels the in-flight native request once without replay");
    Assert(!disconnected.GetProperty("configured").GetBoolean() && !controller.Catalog().Any(t => t.Name.StartsWith("companion_", StringComparison.Ordinal)),
        "disconnect releases native companion capabilities after canceled call cleanup");
    controller.Configure(new() { PermissionMode = "fullAccess" });
    await controller.CompanionConnect(endpoint, companionToken);
    Assert((await controller.Call("companion_probe", McpJson.EmptyObject)).IsError, "native command errors remain model-visible on the controller boundary");
    controller.CompanionDisconnect();
    Assert(!controller.Catalog().Any(t => t.Name == "companion_probe"), "ordinary native errors do not prevent explicit companion cleanup");
}

var blockedPaths = new[]
{
    "/api/agent/export", "/api/Agent/configure", "/API/AGENT/approve", "/api/%61gent/cancel", "/api/%2561gent/reset",
    "/api/%2e%2e/agent/configure", "/api/a/../agent/configure", "/api/a/%252e%252e/agent/configure",
    "/api/cad%5c..%5cagent/configure", "/api/health#agent", "/api/health%23agent", "//other.example/api/state",
    "http://other.example/api/state", "/api/\nagent/poll"
};
foreach (var path in blockedPaths) Assert(!StudioAgentRouteTools.IsStudioPath(path), "route boundary rejects encoded, case-varied or traversing agent paths");
foreach (var path in new[] { "/api/state", "/api/cad/demo/state", "/api/object/H%3A40", "/api/cad/demo/font?name=font%20name.shx" })
    Assert(StudioAgentRouteTools.IsStudioPath(path), "valid API paths and query parameters remain available");
var routed = new McpToolRegistry(); var requests = 0;
routed.Register(new("studio_request", "Guarded routing fixture", McpJson.Parse("""{"type":"object","properties":{"method":{"type":"string"},"path":{"type":"string"}},"required":["method","path"],"additionalProperties":false}""")), (args, _) =>
{
    if (!StudioAgentRouteTools.IsStudioPath(args.GetProperty("path").GetString())) return ValueTask.FromResult(McpToolResult.Error("path", "Use a discovered Studio route."));
    requests++; return ValueTask.FromResult(McpToolResult.FromObject(new { method = args.GetProperty("method").GetString() }));
});
StudioAgentRouteTools.RegisterRead(routed);
Assert(routed.Tools.Single(t => t.Name == "studio_read").ReadOnly, "the dedicated Studio GET alias is advertised as a read operation");
Assert((await routed.ExecuteAsync("studio_read", McpJson.Element(new { path = "/api/%61gent/export" }))).IsError && requests == 0,
    "the read alias retains the same canonical agent-path rejection");
var allowedRead = await routed.ExecuteAsync("studio_read", McpJson.Element(new { path = "/api/state" }));
Assert(!allowedRead.IsError && JsonDocument.Parse(allowedRead.Text).RootElement.GetProperty("method").GetString() == "GET", "the read alias sends only a GET request");
Assert((await routed.ExecuteAsync("studio_read", McpJson.Element(new { path = "/api/state", method = "POST" }))).IsError && requests == 1,
    "callers cannot override the read alias with a mutating method");
await host.StopAsync();
Console.WriteLine($"Studio agent verification passed: {checks} checks covering lifecycle faults, stale drawing bindings, credential cleanup, native cancellation and canonical route permissions.");
