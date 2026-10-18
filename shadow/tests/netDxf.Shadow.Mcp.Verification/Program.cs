using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.NetDxf;

var checks = 0;
void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}
JsonElement Parse(string text) => McpJson.Parse(text);

var document = new DxfDocument();
var line = new Line(new Vector3(1, 2, 0), new Vector3(10, 20, 0));
var circle = new Circle(new Vector3(5, 5, 0), 3);
document.Entities.Add(line); document.Entities.Add(circle);
var longText = new Text(new string('x', 5000) + "tail", Vector3.Zero, 1);
document.Entities.Add(longText);
var source = new ShadowSession(document);
var editor = new EditorSession(source);
var workspace = "live"; long viewVersion = 0; var notifications = 0;
var gate = new object();
var registry = ShadowMcpTools.Create(new(() => editor, () => source)
{
    Identity = () => new(editor.Snapshot.DocumentId, workspace, viewVersion),
    RequireWorkspaceIdentity = true,
    Synchronize = callback => { lock (gate) return callback(); },
    Changed = () => notifications++
});
var lineId = editor.Snapshot.EntityIds.Single(id => editor.Snapshot.Objects[id].Type == "Line").Value;
var circleId = editor.Snapshot.EntityIds.Single(id => editor.Snapshot.Objects[id].Type == "Circle").Value;

async Task<McpToolResult> Call(string name, object? arguments = null, bool mutation = false)
{
    var json = McpJson.Element(arguments ?? new { });
    if (mutation)
    {
        var dictionary = json.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value);
        dictionary["revision"] = editor.Snapshot.Revision;
        dictionary["documentId"] = editor.Snapshot.DocumentId;
        dictionary["workspaceId"] = workspace;
        dictionary["viewVersion"] = viewVersion;
        json = McpJson.Element(dictionary);
    }
    return await registry.ExecuteAsync(name, json);
}
async Task<JsonElement> Success(string name, object? args = null, bool mutation = false)
{
    var result = await Call(name, args, mutation);
    Assert(!result.IsError, name + " failed: " + result.Text);
    return Parse(result.Text);
}

Assert(registry.Tools.Count == 19, "Complete base MCP catalog is missing tools.");
var state = await Success("shadow_state");
Assert(state.GetProperty("documentId").GetGuid() == source.Current.DocumentId && state.GetProperty("data").GetProperty("objects").GetInt32() == source.Current.Objects.Count, "State omitted source identity/count.");
Assert(state.GetProperty("data").GetProperty("mutationIdentityRequired").GetBoolean(), "Workspace identity requirement is not discoverable.");
var page = await Success("shadow_objects", new { limit = 3 });
Assert(page.GetProperty("data").GetProperty("items").GetArrayLength() == 3 && page.GetProperty("data").GetProperty("nextOffset").GetInt32() == 3, "Object pagination failed.");
var last = await Success("shadow_objects", new { limit = 3, offset = source.Current.Objects.Count + 10 });
Assert(last.GetProperty("data").GetProperty("items").GetArrayLength() == 0 && last.GetProperty("data").GetProperty("nextOffset").ValueKind == JsonValueKind.Null, "Pagination beyond the end failed.");
Assert((await Call("shadow_objects", new { limit = 0 })).IsError, "Invalid paging was not centrally rejected.");
Assert((await Call("shadow_state", new { unexpected = true })).IsError, "Unknown arguments were not rejected.");
Assert((await registry.ExecuteAsync("shadow_state", Parse("{\"revision\":0,\"revision\":0}"))).IsError, "Duplicate arguments were accepted.");
Assert((await registry.ExecuteAsync("shadow_state", Parse("[]"))).IsError, "Non-object arguments were accepted.");
Assert((await Call("missing_tool")).IsError, "Unknown tools must return model-visible errors.");
var read = await Success("shadow_read", new { id = lineId, path = "/StartPoint/X" });
Assert(read.GetProperty("data").GetProperty("value").GetProperty("value").GetString() == "1", "Nested coordinate read failed.");
var schema = await Success("shadow_schema", new { type = "netDxf.Entities.Spline" });
Assert(schema.GetProperty("data").GetProperty("properties").GetArrayLength() > 10 && schema.GetProperty("data").GetProperty("constructors").GetArrayLength() > 0, "Absent source type introspection is incomplete.");
var types = await Success("dxf_types", new { search = "Vertex", limit = 100 });
Assert(types.GetProperty("data").GetProperty("items").EnumerateArray().Any(t => t.GetProperty("name").GetString() == "netDxf.Entities.Polyline2DVertex"), "Low-level netDxf vertex type is absent from the catalog.");
var hatchSchema = await Success("shadow_schema", new { type = "netDxf.Entities.HatchBoundaryPath+Arc" });
Assert(hatchSchema.GetProperty("data").GetProperty("properties").EnumerateArray().Any(p => p.GetProperty("name").GetString() == "Radius" && p.GetProperty("writable").GetBoolean()), "Low-level hatch public fields were absent from schema metadata.");
var rawSchema = await Success("shadow_schema", new { type = "netDxf.IO.DxfRawLwPolylineVertex" });
Assert(!rawSchema.GetProperty("data").GetProperty("sourceConstructible").GetBoolean(), "Raw source schema incorrectly advertises native graph construction support.");
var unitSchema = await Success("shadow_schema", new { type = "netDxf.Units.DrawingUnits" });
Assert(unitSchema.GetProperty("data").GetProperty("enumValues").GetArrayLength() > 10, "Unit enums are missing from complete public model introspection.");
var schemaPage = await Success("shadow_schema", new { type = "netDxf.Entities.Spline", offset = 2, limit = 3 });
Assert(schemaPage.GetProperty("data").GetProperty("properties").GetArrayLength() == 3 && schemaPage.GetProperty("data").GetProperty("nextOffset").GetInt32() == 5, "Type schema metadata ignored pagination.");
var sourceText = await Success("dxf_source_inspect", new { handle = longText.Handle, path = "/Value", textOffset = 5000, textLimit = 10 });
Assert(sourceText.GetProperty("data").GetProperty("value").GetProperty("value").GetString() == "tail"
    && sourceText.GetProperty("data").GetProperty("value").GetProperty("nextTextOffset").ValueKind == JsonValueKind.Null, "Raw source inspection cannot page beyond its first text preview.");
var checkpoint = await Success("shadow_checkpoint", new { name = "Before edits" });
var checkpointId = checkpoint.GetProperty("data").GetProperty("checkpoint").GetString()!;

var initialRevision = source.Current.Revision;
var edited = await Success("shadow_edit", new
{
    description = "Move start point",
    edits = new object[] { new { id = lineId, path = "/StartPoint/X", value = 4 }, new { id = lineId, path = "/StartPoint/Y", value = 6 } }
}, true);
Assert(line.StartPoint == new Vector3(4, 6, 0), "Property edits did not reach native netDxf source.");
Assert(source.Current.Revision == initialRevision + 1 && editor.State.UndoCount == 1 && notifications == 1, "An atomic property batch must publish one revision/history entry/notification.");
Assert(edited.GetProperty("data").GetProperty("edits").GetInt32() == 1, "Nested writes were not coalesced into one top-level property edit.");
var diff = await Success("shadow_diff", new { checkpoint = checkpointId, includeValues = true });
Assert(diff.GetProperty("data").GetProperty("changed").GetInt32() >= 1 && diff.GetProperty("data").GetProperty("changes").GetProperty("items").EnumerateArray().Any(c => c.GetProperty("id").GetString() == lineId), "Checkpoint diff did not retain the original immutable graph.");
var beforeInvalid = source.Current;
var invalid = await Call("shadow_edit", new { edits = new object[] { new { id = lineId, path = "/EndPoint/X", value = 999 }, new { id = circleId, path = "/Radius", value = -1 } } }, true);
Assert(invalid.IsError && ReferenceEquals(source.Current, beforeInvalid) && line.EndPoint.X == 10 && circle.Radius == 3 && editor.State.UndoCount == 1, "Invalid native edit changed source, snapshot or history.");
var missingIdentity = await Call("shadow_edit", new { revision = source.Current.Revision, edits = new[] { new { id = circleId, path = "/Radius", value = 9 } } });
Assert(missingIdentity.IsError, "Studio identity fields are not required for mutation.");
var wrongIdentity = await Call("shadow_undo", new { revision = source.Current.Revision, documentId = source.Current.DocumentId, workspaceId = "other", viewVersion });
Assert(wrongIdentity.IsError && ReferenceEquals(source.Current, beforeInvalid), "Workspace mismatch mutated the document.");
var stale = await Call("shadow_undo", new { revision = initialRevision, documentId = source.Current.DocumentId, workspaceId = workspace, viewVersion });
Assert(stale.IsError && Parse(stale.Text).GetProperty("error").GetProperty("code").GetString() == "revision_conflict", "Stale revision was not identified.");
await Success("shadow_undo", mutation: true);
Assert(line.StartPoint == new Vector3(1, 2, 0) && editor.State.RedoCount == 1, "Undo did not restore native coordinate.");
await Success("shadow_redo", mutation: true);
Assert(line.StartPoint == new Vector3(4, 6, 0), "Redo did not restore native coordinate.");
var previewSource = source.Current; var previewNotifications = notifications;
var preview = await Success("shadow_edit", new { preview = true, edits = new[] { new { id = circleId, path = "/Radius", value = 8 } } }, true);
Assert(ReferenceEquals(source.Current, previewSource) && notifications == previewNotifications && preview.GetProperty("data").GetProperty("preview").GetBoolean(), "Preview mutated the source or sent an invalidation.");

var created = await Success("shadow_graph_edit", new
{
    description = "Create native entity and layer",
    operations = new object[]
    {
        new { op = "create", type = "netDxf.Tables.Layer", alias = "@layer", properties = new { Name = "Agent layer" } },
        new { op = "addEntity", type = "netDxf.Entities.Line", alias = "@line", properties = new { StartPoint = new { X = 20, Y = 30, Z = 0 }, EndPoint = new { X = 50, Y = 60, Z = 0 }, Layer = new Dictionary<string, string> { ["$ref"] = "@layer" } } }
    }
}, true);
var createdId = created.GetProperty("data").GetProperty("aliases").GetProperty("@line").GetString()!;
Assert(document.Entities.Lines.Count() == 2 && document.Layers.Contains("Agent layer") && source.Current.Objects.ContainsKey(new(createdId)), "Structural graph creation did not attach native objects with stable identities.");
var nativeAdded = document.Entities.Lines.Single(l => l.StartPoint.X == 20);
Assert(nativeAdded.Layer.Name == "Agent layer", "Structural alias reference did not bind the new layer.");
await Success("shadow_undo", mutation: true);
Assert(document.Entities.Lines.Count() == 1, "Structural undo failed to detach added native entity.");
await Success("shadow_redo", mutation: true);
Assert(document.Entities.Lines.Count() == 2 && source.Current.Objects.ContainsKey(new(createdId)), "Structural redo lost native identity.");
await Success("shadow_graph_edit", new { operations = new[] { new { op = "removeEntities", ids = new[] { createdId } } } }, true);
Assert(document.Entities.Lines.Count() == 1 && !source.Current.Objects.ContainsKey(new(createdId)), "Entity deletion failed to remove native membership and shadow node.");
await Success("shadow_undo", mutation: true);
Assert(source.Current.Objects.ContainsKey(new(createdId)), "Entity deletion undo lost the object identity.");
var beforeDangling = source.Current;
var dangling = await Call("shadow_graph_edit", new { operations = new[] { new { op = "set", id = lineId, path = "/Layer", value = new Dictionary<string, string> { ["$ref"] = "H:DOESNOTEXIST" } } } }, true);
Assert(dangling.IsError && ReferenceEquals(source.Current, beforeDangling), "Dangling reference reached mutable source.");
var references = await Success("shadow_references", new { id = lineId });
Assert(references.GetProperty("data").GetProperty("total").GetInt32() > 0, "Reference paths were not exposed.");
var traversal = await Success("shadow_traverse", new { ids = new[] { lineId }, depth = 8, limit = 4 });
Assert(traversal.GetProperty("data").GetProperty("nodes").GetArrayLength() <= 4 && traversal.GetProperty("data").GetProperty("truncated").GetBoolean(), "Graph traversal ignored node budget.");
var nativeRead = await Success("dxf_source_inspect", new { handle = line.Handle, path = "/StartPoint/X" });
Assert(nativeRead.GetProperty("data").GetProperty("value").GetProperty("value").GetString() == "4", "Live source public-property inspection failed.");

var liveEditor = editor;
editor = new EditorSession(new ShadowMemoryStore(source.Current)); workspace = "branch-a"; viewVersion++;
await Success("shadow_edit", new { edits = new[] { new { id = circleId, path = "/Radius", value = 12 } } }, true);
Assert(circle.Radius == 3 && editor.Snapshot.Read(new(circleId))["Radius"].Number() == 12, "Active branch edit incorrectly mutated live source.");
var sourceRead = await Success("dxf_source_read", new { id = circleId, path = "/Radius" });
Assert(sourceRead.GetProperty("data").GetProperty("value").GetProperty("value").GetProperty("value").GetString() == "3", "Source read leaked the active branch value.");
editor = liveEditor; workspace = "live"; viewVersion++;
var switched = await Success("shadow_state");
Assert(switched.GetProperty("workspaceId").GetString() == "live" && switched.GetProperty("viewVersion").GetInt64() == viewVersion, "Dynamic registry retained a stale editor/workspace binding.");
using (var cancelled = new CancellationTokenSource())
{
    cancelled.Cancel();
    try { await registry.ExecuteAsync("shadow_state", McpJson.EmptyObject, cancelled.Token); throw new Exception("Cancellation was swallowed."); }
    catch (OperationCanceledException) { checks++; }
}

var bounded = new McpToolRegistry(maximumResultCharacters: 1024);
bounded.Register(new("large", "large result", Parse("{\"type\":\"object\"}")), (a, ct) => ValueTask.FromResult(new McpToolResult(new string('x', 10000))));
var limited = await bounded.ExecuteAsync("large", McpJson.EmptyObject);
Assert(!limited.IsError && Parse(limited.Text).GetProperty("truncated").GetBoolean(), "Large successful results must be bounded without misreporting a completed mutation as failed.");

var slowCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
registry.Register(new("verification_wait", "Cancellation integration probe", Parse("{\"type\":\"object\",\"properties\":{\"milliseconds\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":10000}},\"required\":[\"milliseconds\"],\"additionalProperties\":false}"), true), async (a, ct) =>
{
    try { await Task.Delay(a.GetProperty("milliseconds").GetInt32(), ct); return McpToolResult.FromObject(new { completed = true }); }
    catch (OperationCanceledException) { slowCancelled.TrySetResult(); throw; }
});
await using (var protocol = new McpJsonRpcSession(registry, TimeSpan.FromSeconds(20)))
{
    var malformed = Parse((await protocol.ProcessAsync("{broken"))!);
    Assert(malformed.GetProperty("error").GetProperty("code").GetInt32() == -32700, "Malformed JSON must produce a parse error without killing the session.");
    var invalidRpc = Parse((await protocol.ProcessAsync("[]"))!);
    Assert(invalidRpc.GetProperty("error").GetProperty("code").GetInt32() == -32600, "Invalid JSON-RPC request was accepted.");
    var initialized = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-11-25\",\"capabilities\":{},\"clientInfo\":{\"name\":\"verification\",\"version\":\"1.0\"}}}"))!);
    Assert(initialized.GetProperty("result").GetProperty("protocolVersion").GetString() == "2025-11-25", "Official SDK initialization did not negotiate the requested protocol.");
    Assert(await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") is null, "Initialized notification must not produce a response.");
    var list = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}"))!);
    Assert(list.GetProperty("result").GetProperty("tools").GetArrayLength() == registry.Tools.Count, "SDK tools/list omitted registered tools.");
    var call = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"tools/call\",\"params\":{\"name\":\"shadow_state\",\"arguments\":{}}}"))!);
    Assert(!call.GetProperty("result").GetProperty("isError").GetBoolean(), "SDK tool call failed.");
    var errorCall = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"tools/call\",\"params\":{\"name\":\"shadow_objects\",\"arguments\":{\"limit\":0}}}"))!);
    Assert(errorCall.GetProperty("result").GetProperty("isError").GetBoolean(), "SDK tool errors must remain model-visible result errors.");
    var resources = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"resources/list\"}"))!);
    Assert(resources.GetProperty("result").GetProperty("resources").GetArrayLength() >= 4, "Standard resource listing is incomplete.");
    var resource = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":6,\"method\":\"resources/read\",\"params\":{\"uri\":\"shadow://workspace/state\"}}"))!);
    Assert(resource.GetProperty("result").GetProperty("contents")[0].GetProperty("text").GetString()!.Contains("documentId", StringComparison.Ordinal), "Standard resource reading failed.");
    var prompts = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":7,\"method\":\"prompts/list\"}"))!);
    Assert(prompts.GetProperty("result").GetProperty("prompts").GetArrayLength() == 2, "Standard prompts listing failed.");
    var prompt = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":8,\"method\":\"prompts/get\",\"params\":{\"name\":\"edit_drawing\",\"arguments\":{\"task\":\"Move the line\"}}}"))!);
    Assert(prompt.GetProperty("result").GetProperty("messages")[0].GetProperty("content").GetProperty("text").GetString()!.Contains("Move the line", StringComparison.Ordinal), "Prompt arguments were not preserved.");
    var fast = protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":\"fast\",\"method\":\"tools/call\",\"params\":{\"name\":\"verification_wait\",\"arguments\":{\"milliseconds\":1}}}").AsTask();
    var slow = protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":\"slow\",\"method\":\"tools/call\",\"params\":{\"name\":\"verification_wait\",\"arguments\":{\"milliseconds\":20}}}").AsTask();
    await Task.WhenAll(fast, slow);
    Assert(Parse(fast.Result!).GetProperty("id").GetString() == "fast" && Parse(slow.Result!).GetProperty("id").GetString() == "slow", "Concurrent JSON-RPC request ids were miscorrelated.");
    using var cancellation = new CancellationTokenSource(100);
    try
    {
        await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":\"cancel\",\"method\":\"tools/call\",\"params\":{\"name\":\"verification_wait\",\"arguments\":{\"milliseconds\":10000}}}", cancellation.Token);
        throw new InvalidOperationException("JSON-RPC caller cancellation was swallowed.");
    }
    catch (OperationCanceledException) { checks++; }
    await slowCancelled.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Assert(slowCancelled.Task.IsCompletedSuccessfully, "Browser cancellation did not reach the official server handler.");
    var alive = Parse((await protocol.ProcessAsync("{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ping\"}"))!);
    Assert(alive.TryGetProperty("result", out _), "Session was not reusable after tool cancellation.");
}

Console.WriteLine($"MCP verification passed: {checks} checks covering source/branch transactions, typed paths, schema discovery, bounded output, official MCP resources/prompts, concurrency and cancellation.");
