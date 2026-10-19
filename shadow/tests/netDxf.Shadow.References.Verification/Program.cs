using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Agents;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;

var count = 0;
using var studio = new Studio(false);
var drawing = new DxfDocument();
var block = new Block("Reference fixture");
for (var i = 0; i < 40; i++) block.Entities.Add(new Line(new Vector2(i, 0), new Vector2(i, 5)));
drawing.Entities.Add(new Insert(block, new Vector3(0, 0, 0))); drawing.Entities.Add(new Insert(block, new Vector3(100, 0, 0)));
drawing.Entities.Add(new Circle(new Vector3(0, 20, 0), 2));
using (var stream = new MemoryStream()) { drawing.Save(stream); stream.Position = 0; studio.Load(stream, 0); }
var initial = State();
studio.CadSelect(new(studio.CadRevision, "all"));
string[] Selection() => McpJson.Element(studio.CadState()).GetProperty("selectedEntities").EnumerateArray().Select(i => i.GetString()!).ToArray();
var requestedOrder = Selection().Reverse().ToArray();
studio.CadSelect(new(studio.CadRevision, "entities", Entities: requestedOrder.Append(requestedOrder[0]).Select(id => new ObjectId(id)).ToImmutableArray()));
Check(Selection().SequenceEqual(requestedOrder), "batched source selection preserves explicit target/boundary order and removes duplicates");
studio.CadSelect(new(studio.CadRevision, "entities", Entities: [new(requestedOrder[1])], Remove: true));
Check(Selection().SequenceEqual(new[] { requestedOrder[0], requestedOrder[2] }), "batched removal preserves remaining selection order");
studio.CadSelect(new(studio.CadRevision, "entities", Entities: [new(requestedOrder[1]), new(requestedOrder[0])], Extend: true));
Check(Selection().SequenceEqual(new[] { requestedOrder[0], requestedOrder[2], requestedOrder[1] }), "batched extension appends new roots in caller order");
var preservedSelection = Selection();
Throws<ArgumentException>(() => studio.CadSelect(new(studio.CadRevision, "entities", Entities: [new(requestedOrder[0]), new("missing-source-entity")])), "invalid batched source selection rejected");
Check(Selection().SequenceEqual(preservedSelection), "invalid batched selection does not partially clear or reorder prior selection");
studio.CadSelect(new(studio.CadRevision, "clear"));
var first = initial.GetProperty("items").EnumerateArray().First(item => item.GetProperty("inserted").GetBoolean()); studio.Select(new(studio.CadRevision, first.GetProperty("key").GetString()!));
var captured = Capture(); var reference = captured.References.Single();
Check(reference.Type == "netDxf.Entities.Insert" && reference.InsertPath?.Count == 1 && reference.DefinitionObjectId is not null && reference.ObjectId != reference.DefinitionObjectId, "selected INSERT keeps instance root separate from shared definition leaf");
Check(!string.IsNullOrEmpty(reference.Handle) && reference.DocumentId == initial.GetProperty("documentId").GetGuid(), "reference has stable handle and snapshot GUID");
studio.CadSelect(new(studio.CadRevision, "entities", Entities: [new(reference.DefinitionObjectId!), new(reference.ObjectId)]));
Check(Selection().SequenceEqual(new[] { reference.ObjectId }), "batch excludes shared definition leaves without visible root occurrences");
studio.CadSelect(new(studio.CadRevision, "entity", Entity: reference.ObjectId));
Check(Capture().References.Count == 1, "expanded block selection captures one instance despite forty render occurrences");
var validation = studio.ValidateReferences(new([reference])); Check(validation.CanSend && validation.Items[0].Status == "current", "fresh authoritative reference validates");
Check(!studio.ValidateReferences(new([reference with { DocumentId = Guid.NewGuid() }], true)).CanSend, "refresh cannot remap a different drawing by handle");
Check(studio.ValidateReferences(new([reference with { WorkspaceId = "other" }], true)).Items[0].Status == "wrongWorkspace", "refresh cannot cross workspace identity");
Check(studio.ValidateReferences(new([reference with { Type = "netDxf.Entities.Circle" }], true)).Items[0].Status == "identityMismatch", "forged object type rejected");
Check(studio.ValidateReferences(new([reference with { OccurrenceKey = "missing" }], true)).Items[0].Status == "occurrenceMissing", "missing instance occurrence rejected");
Throws<ArgumentException>(() => DrawingReferences.ValidateShape([reference, reference]), "duplicate references rejected");
Throws<ArgumentException>(() => DrawingReferences.ValidateShape(Enumerable.Repeat(reference, 33).ToArray()), "reference count bounded");
Throws<ShadowConflictException>(() => studio.CaptureReferences(new(reference.DocumentId, reference.WorkspaceId, reference.ViewVersion, reference.Revision + 1)), "capture requires current revision");

var registry = studio.CreateMcpRegistry();
var model = new FixtureClient(); using var agent = new AgentSession(model, registry, new() { MaxTurns = 1, MaxRetries = 0 });
await agent.RunAsync(new AgentPrompt("Describe this selected instance", [reference]));
Check(model.Calls == 1 && model.Input.Contains(AgentPrompt.ReferenceStart, StringComparison.Ordinal) && model.Input.Contains(reference.ObjectId, StringComparison.Ordinal), "model receives structured context declaration with selected object identity");
var user = agent.Transcript().Single(m => m.Role == "user");
Check(user.DisplayText == "Describe this selected instance" && user.References?.Single().ObjectId == reference.ObjectId, "visible transcript keeps original text and typed references");
var checkpoint = agent.Export(); using var imported = new AgentSession(new FixtureClient(), registry); imported.Import(checkpoint);
Check(imported.Transcript()[0].References?.Single().DefinitionObjectId == reference.DefinitionObjectId, "checkpoint preserves instance/definition context");
var oldCheckpoint = JsonSerializer.SerializeToNode(new AgentCheckpoint(1, Guid.NewGuid().ToString("N"), "", [], [], [], []), McpJson.Options)!.AsObject(); oldCheckpoint.Remove("archivedReferences");
imported.Import(oldCheckpoint.ToJsonString()); Check(imported.Transcript().Count == 0, "legacy checkpoint remains supported");

using (var compacted = new AgentSession(new FixtureClient(), registry, new() { MaxTurns = 1, MaxRetries = 0 }))
{
    await compacted.RunAsync(new AgentPrompt("Keep this selected object", [reference]));
    for (var i = 0; i < 7; i++) await compacted.RunAsync("Fixture followup " + i);
    await compacted.CompactAsync();
    var restored = JsonSerializer.Deserialize<AgentCheckpoint>(compacted.Export(), McpJson.Options)!;
    Check(restored.ArchivedReferences?.Count == 1 && compacted.Transcript().Any(m => m.References?.Count == 1), "compaction retains reference chips outside model context and in portable history");
    using var resumed = new AgentSession(new FixtureClient(), registry); resumed.Import(compacted.Export());
    Check(resumed.Transcript().Any(m => m.References?.Single().ObjectId == reference.ObjectId), "archived reference chips survive checkpoint reload");
}

studio.Add(new(studio.CadRevision, "line", 250, 250));
var stale = studio.ValidateReferences(new([reference])); Check(!stale.CanSend && stale.Items[0].Status == "stale", "any new revision marks captured context stale");
var refreshed = studio.ValidateReferences(new([reference], true));
Check(refreshed.CanSend && refreshed.Items[0].Status == "refreshed" && refreshed.Items[0].Current!.ObjectId == reference.ObjectId && reference.Revision != refreshed.Items[0].Current!.Revision, "explicit refresh retains stable object and advances only draft receipt");
await agent.RunAsync(new AgentPrompt("Stale attempt", [reference])); Check(model.Calls == 1 && agent.Poll().Status == "error", "stale reference rejected before inference");
await using var controller = new StudioAgentController(_ => studio.CreateMcpRegistry());
await ThrowsAsync<InvalidOperationException>(() => controller.StartPromptAsync(new("Stale server dispatch", [reference])), "controller rejects stale reference at server dispatch");
Check(controller.Session.Transcript().Count == 0, "rejected dispatch preserves conversation without adding attempted prompt");
reference = refreshed.Items[0].Current!;
var branch = McpJson.Element(studio.Fork(new(studio.CadRevision, "Reference branch")));
Check(studio.ValidateReferences(new([reference])).Items[0].Status == "wrongWorkspace", "branch switch invalidates live reference even with same object IDs");
studio.SwitchWorkspace(new(studio.CadRevision, "live"));
Check(studio.ValidateReferences(new([reference])).Items[0].Status == "stale", "switch away and back invalidates view version");
reference = studio.ValidateReferences(new([reference], true)).Items[0].Current!;
studio.CadSelect(new(studio.CadRevision, "entity", Entity: reference.ObjectId));
studio.CadCommand(new(studio.CadRevision, new netDxf.Shadow.Drafting.DraftingCommand("ERASE")));
Check(studio.ValidateReferences(new([reference], true)).Items[0].Status == "deleted", "deleted instance cannot be refreshed to another insert using shared definition");
Check(agent.Transcript().First(m => m.References?.Count > 0).References![0].Revision == captured.Identity.Revision, "historical attachment remains immutable after edits and deletion");
Console.WriteLine($"PASS {count} drawing reference and prompt preservation checks");

JsonElement State() => McpJson.Element(studio.State());
DrawingReferenceSet Capture() { var s = State(); return studio.CaptureReferences(new(s.GetProperty("documentId").GetGuid(), s.GetProperty("workspaceId").GetString()!, s.GetProperty("viewVersion").GetInt64(), s.GetProperty("revision").GetInt64())); }
void Check(bool condition, string name) { if (!condition) throw new InvalidOperationException(name); count++; }
void Throws<T>(Action action, string name) where T : Exception { try { action(); } catch (T) { count++; return; } throw new InvalidOperationException(name); }
async Task ThrowsAsync<T>(Func<Task> action, string name) where T : Exception { try { await action(); } catch (T) { count++; return; } throw new InvalidOperationException(name); }
sealed class FixtureClient : IChatClient
{
    public int Calls; public string Input = "";
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls++; Input = string.Join("\n", messages.Select(m => m.Text)); await Task.Yield();
        if (options?.Tools?.OfType<AIFunctionDeclaration>().Any(t => t.Name == "agent_finish") == true)
            yield return new(ChatRole.Assistant, [new FunctionCallContent("finish-" + Guid.NewGuid().ToString("N"), "agent_finish", new Dictionary<string, object?>
                { ["outcome"] = "completed", ["summary"] = "Inspected fixture", ["verification"] = new[] { "Read selected object identity" } })]) { FinishReason = ChatFinishReason.ToolCalls };
        else yield return new(ChatRole.Assistant, "Inspected fixture") { FinishReason = ChatFinishReason.Stop };
    }
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) => GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
    public object? GetService(Type type, object? key = null) => null;
    public void Dispose() { }
}
