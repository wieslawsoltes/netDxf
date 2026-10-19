using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Studio;
using netDxf.Shadow.Workflows;

internal static class WorkflowVerification
{
    private static readonly JsonSerializerOptions Json = StudioJson.CreateOptions();
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Add(string name, Action test) => tests.Add(("Workflow: " + name, test));
        Add("records actual commands undo redo and native roundtrip with geometry and SVG assertions", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Geometry regression"));
            Command(studio, new("LINE", [new(0, 0), new(10, 0)])); Command(studio, new("MOVE", [new(0, 0), new(4, 7)]));
            Command(studio, new("CIRCLE", [new(30, 10)], Value: 4)); studio.History("undo", Revision(studio)); studio.History("redo", Revision(studio));
            studio.CadWorkflowRoundTrip(new(Revision(studio))); studio.CadWorkflowStop(new(Revision(studio))); var scenario = studio.CadWorkflowExport();
            Require(scenario.Steps.Length == 6, J(studio.CadWorkflowStatus()).GetRawText());
            Require(scenario.Steps.All(s => s.Expected.TryGetProperty("geometryHash", out _) && s.Expected.TryGetProperty("renderGeometryHash", out _)));
            var roundTrip = WorkflowJson.Read(WorkflowJson.Write(scenario)); var before = J(studio.State()).GetRawText();
            var report = studio.CadWorkflowReplay(new(Revision(studio), roundTrip)); Require(report.Passed && report.Complete, J(report).GetRawText());
            Require(J(studio.State()).GetRawText() == before, "Isolated replay mutated the visible drawing.");
            using var file = new MemoryStream(studio.Save()); var loaded = DxfDocument.Load(file)!; Require(loaded.Entities.Lines.Single().StartPoint == new Vector3(4, 7, 0) && loaded.Entities.Circles.Single().Radius == 4);
        });
        Add("resource references survive rename delete and layer styles replay", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Resources"));
            studio.CadLayer(new(Revision(studio), new(Id: null, Name: "Recorded", ColorIndex: 3, Transparency: 25)));
            var resources = J(studio.CadResources()); var id = new ObjectId(resources.GetProperty("layers").EnumerateArray().First(l => l.GetProperty("name").GetString() == "Recorded").GetProperty("id").GetProperty("value").GetString()!);
            studio.CadLayer(new(Revision(studio), new(Id: id, Name: "Renamed", Description: "Replayed definition")));
            studio.CadResource(new(Revision(studio), new("layer", "current", id)));
            studio.CadResource(new(Revision(studio), new("linetype", "save", Linetype: new("Recorded dash", Segments: [new("simple", 2), new("simple", -1)]))));
            studio.CadResource(new(Revision(studio), new("textstyle", "save", TextStyle: new("Notes", "txt.shx", WidthFactor: .8))));
            Command(studio, new("LINE", [new(0, 0), new(12, 8)], Layer: "Renamed"));
            var scenario = studio.CadWorkflowExport(); Require(scenario.Steps.Length == 6, J(studio.CadWorkflowStatus()).GetRawText());
            var result = studio.CadWorkflowReplay(new(Revision(studio), scenario)); Require(result.Passed, J(result).GetRawText());
        });
        Add("block conversion insert and definition branch roundtrip replay", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Block editing"));
            Command(studio, new("LINE", [new(0, 0), new(8, 0)])); studio.CadCreateBlock(new(Revision(studio), "Recorded symbol", new(0, 0)));
            var block = J(studio.CadState()).GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "Recorded symbol").GetProperty("id").GetString()!;
            Command(studio, new("INSERT", [new(20, 15)], Reference: new(block), Scale: new(2, 3, 1), Rotation: 30));
            studio.CadBeginBlock(new(Revision(studio), block)); Command(studio, new("CIRCLE", [new(0, 0)], Value: 2));
            studio.CadFinishBlock(new(Revision(studio), State(studio).GetProperty("sourceRevision").GetInt64(), true));
            studio.CadWorkflowRoundTrip(new(Revision(studio))); var scenario = studio.CadWorkflowExport();
            Require(scenario.Steps.Length == 7, J(studio.CadWorkflowStatus()).GetRawText()); var result = studio.CadWorkflowReplay(new(Revision(studio), scenario)); Require(result.Passed, J(result).GetRawText());
        });
        Add("branches and explicit merge replay without old opaque tokens", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Branch merge")); Command(studio, new("LINE", [new(0, 0), new(10, 0)]));
            studio.Fork(new(Revision(studio), "Candidate")); Command(studio, new("CIRCLE", [new(30, 20)], Value: 3)); var branch = State(studio); var branchId = branch.GetProperty("workspaceId").GetString()!; var incoming = Revision(studio);
            studio.SwitchWorkspace(new(incoming, "live")); var state = State(studio); var plan = J(studio.PreviewMerge(new(state.GetProperty("documentId").GetGuid(), branchId, Revision(studio), incoming)));
            studio.ApplyMerge(new(state.GetProperty("documentId").GetGuid(), plan.GetProperty("planId").GetString()!, Revision(studio), incoming));
            var scenario = studio.CadWorkflowExport(); Require(scenario.Steps.Length == 5, J(studio.CadWorkflowStatus()).GetRawText()); var result = studio.CadWorkflowReplay(new(Revision(studio), scenario)); Require(result.Passed, J(result).GetRawText());
        });
        Add("semantic drift reports the exact step and never retries", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Detect regression")); Command(studio, new("CIRCLE", [new(0, 0)], Value: 2)); var scenario = studio.CadWorkflowExport();
            var changed = scenario.Steps[0] with { Arguments = JsonSerializer.SerializeToElement(new DraftingCommand("CIRCLE", [new(0, 0)], Entities: [], Value: 9), Json) };
            var result = studio.CadWorkflowReplay(new(Revision(studio), scenario with { Steps = [changed] })); Require(!result.Passed && result.Steps.Length == 1 && result.Steps[0].Differences.Any(d => d.Path.Contains("geometryHash")), J(result).GetRawText());
        });
        Add("checksum unknown operations budget and unsupported edits fail explicitly", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Boundaries")); Command(studio, new("LINE", [new(0, 0), new(1, 0)])); var scenario = studio.CadWorkflowExport();
            var bad = studio.CadWorkflowReplay(new(Revision(studio), scenario with { Baseline = scenario.Baseline with { Sha256 = "bad" } })); Require(!bad.Passed && bad.Steps.IsEmpty);
            bad = studio.CadWorkflowReplay(new(Revision(studio), scenario with { Steps = [scenario.Steps[0] with { Operation = "arbitrary-code" }] })); Require(!bad.Passed && bad.Steps.Length == 1);
            Throws<ArgumentException>(() => WorkflowJson.Read(new byte[2048], new(MaximumJsonBytes: 1024)));
            studio.SourceDemo(Revision(studio)); Require(!J(studio.CadWorkflowStatus()).GetProperty("recording").GetBoolean() && J(studio.CadWorkflowStatus()).GetProperty("error").GetString()!.Contains("not supported"));
        });
        Add("entity batch selection validates IDs and is one revision-preserving selection", () =>
        {
            using var studio = new Studio(false); Command(studio, new("LINE", [new(0, 0), new(1, 0)])); Command(studio, new("CIRCLE", [new(4, 0)], Value: 2));
            var ids = J(studio.CadState()).GetProperty("items").EnumerateArray().Select(i => new ObjectId(i.GetProperty("id").GetString()!)).ToImmutableArray(); var revision = Revision(studio);
            studio.CadSelect(new(revision, "entities", Entities: ids)); Require(J(studio.CadState()).GetProperty("selectedEntities").GetArrayLength() == 2 && Revision(studio) == revision);
            Throws<ArgumentException>(() => studio.CadSelect(new(revision, "entities", Entities: [new("absent")])));
        });
        Add("invalid recording names cannot normalize source or replace a recording", () =>
        {
            using var studio = new Studio(false); Command(studio, new("DIMALIGNED", [new(0, 0), new(40, 0), new(0, 10)]));
            var before = State(studio).GetRawText(); Throws<ArgumentException>(() => studio.CadWorkflowStart(new(Revision(studio), " ")));
            Throws<ArgumentException>(() => studio.CadWorkflowStart(new(Revision(studio), new string('x', 201))));
            Require(State(studio).GetRawText() == before && !J(studio.CadWorkflowStatus()).GetProperty("hasRecording").GetBoolean());
            studio.CadWorkflowStart(new(Revision(studio), "Keep recording")); Command(studio, new("CIRCLE", [new(100, 0)], Value: 3));
            Throws<InvalidOperationException>(() => studio.CadWorkflowStart(new(Revision(studio), "Replacement")));
            Require(studio.CadWorkflowExport().Name == "Keep recording" && studio.CadWorkflowExport().Steps.Length == 1);
        });
        Add("rejected edits are not recorded and crossing the baseline stops recording", () =>
        {
            using var studio = new Studio(false); Command(studio, new("LINE", [new(0, 0), new(10, 0)])); studio.CadWorkflowStart(new(Revision(studio), "Baseline history"));
            Throws<ArgumentException>(() => Command(studio, new("CIRCLE", [new(10, 20)], Value: -3)));
            Require(J(studio.CadWorkflowStatus()).GetProperty("recording").GetBoolean() && studio.CadWorkflowExport().Steps.IsEmpty);
            studio.History("undo", Revision(studio)); var status = J(studio.CadWorkflowStatus());
            Require(!status.GetProperty("recording").GetBoolean() && status.GetProperty("error").GetString()!.Contains("baseline") && studio.CadWorkflowExport().Steps.IsEmpty);
            Require(State(studio).GetProperty("entities").GetInt32() == 0);
        });
        Add("atomic previews record typed operations and replay as one undo", () =>
        {
            using var studio = new Studio(false); studio.CadWorkflowStart(new(0, "Preview transaction"));
            var token = J(studio.CadEditPreview(new(Revision(studio), "Move new line", [new("command", new("LINE", [new(0, 0), new(10, 0)])), new("command", new("MOVE", [new(0, 0), new(5, 8)]))]))).GetProperty("previewId").GetString()!;
            Require(studio.CadWorkflowExport().Steps.IsEmpty); studio.CadEditPreviewApply(new(Revision(studio), token));
            Require(studio.CadWorkflowExport().Steps.Single().Operation == "edit-preview");
            studio.History("undo", Revision(studio)); studio.History("redo", Revision(studio));
            var result = studio.CadWorkflowReplay(new(Revision(studio), studio.CadWorkflowExport())); Require(result.Passed, J(result).GetRawText());
        });
        Add("recorder budgets preserve prior steps and comparison has bounded numeric tolerance", () =>
        {
            var recorder = new WorkflowRecorder("Bounded", new("fixture", "{}", "none"), new(MaximumSteps: 1));
            recorder.Record("command", J(new { }), J(new { x = 1 })); Throws<InvalidOperationException>(() => recorder.Record("command", J(new { }), J(new { x = 2 })));
            Require(recorder.Count == 1 && recorder.Snapshot().Steps[0].Expected.GetProperty("x").GetInt32() == 1);
            Require(WorkflowRunner.Compare(J(new { x = 1.0, y = 2.0 }), J(new { y = 2.0, x = 1.0 + 1e-10 })).IsEmpty);
            var differences = WorkflowRunner.Compare(J(new { x = 1, y = 2 }), J(new { x = 2, y = 3 }), new(MaximumDifferences: 1));
            Require(differences.Length == 1 && differences[0].Path == "$.x");
        });
        Add("pending property previews own their JSON until approval and workflow replay", () =>
        {
            using var studio = new Studio(false); Command(studio, new("CIRCLE", [new(0, 0)], Value: 2));
            var id = new ObjectId(J(studio.CadState()).GetProperty("selectedEntities")[0].GetString()!);
            studio.CadWorkflowStart(new(Revision(studio), "Deferred property approval"));
            string preview;
            using (var value = JsonDocument.Parse("7"))
                preview = J(studio.CadEditPreview(new(Revision(studio), "Change radius", [new("property",
                    Property: new(id, "Radius", value.RootElement))]))).GetProperty("previewId").GetString()!;
            studio.CadEditPreviewApply(new(Revision(studio), preview));
            Require(J(studio.CadWorkflowStatus()).GetProperty("recording").GetBoolean());
            var scenario = studio.CadWorkflowExport(); Require(scenario.Steps.Length == 1);
            var result = studio.CadWorkflowReplay(new(Revision(studio), scenario)); Require(result.Passed, J(result).GetRawText());
        });
        Add("semantic comparison preserves large numeric differences and decoded string equality", () =>
        {
            Require(WorkflowRunner.Compare(J(9_007_199_254_740_992L), J(9_007_199_254_740_993L)).Length == 1,
                "Adjacent integers above double precision must not compare equal.");
            Require(WorkflowRunner.Compare(J(decimal.MaxValue), J(decimal.MaxValue - 1)).Length == 1);
            Require(WorkflowRunner.Compare(J(decimal.MinValue), J(decimal.MaxValue)).Length == 1,
                "The decimal difference may overflow without failing comparison.");
            Require(WorkflowRunner.Compare(J(9_007_199_254_740_992L), J(9_007_199_254_740_993L), new(NumberTolerance: 1)).IsEmpty);
            Require(WorkflowRunner.Compare(J(1e-40), J(2e-40), new(NumberTolerance: 0)).Length == 1);
            Require(WorkflowRunner.Compare(J(0), J(1e-28), new(NumberTolerance: 7e-29)).Length == 1);
            Require(WorkflowRunner.Compare(J(0), J(2e-28), new(NumberTolerance: 1.5e-28)).Length == 1);
            Require(WorkflowRunner.Compare(J(9_007_199_254_740_992L), J(9_007_199_254_740_993L), new(NumberTolerance: 1e-40)).Length == 1);
            using var escaped = JsonDocument.Parse("\"\\u0061\"");
            Require(WorkflowRunner.Compare(J("a"), escaped.RootElement).IsEmpty,
                "Equivalent JSON string escapes must compare by their decoded values.");
        });
        Add("cancellation during baseline initialization cannot report an empty replay as passed", () =>
        {
            using var cancellation = new CancellationTokenSource();
            var driver = new CancellingBaselineDriver(cancellation);
            var scenario = new WorkflowScenario(1, "Cancelled baseline", new("fixture", "{}", "none"), []);
            Throws<OperationCanceledException>(() => WorkflowRunner.RunAsync(scenario, driver,
                cancellationToken: cancellation.Token).GetAwaiter().GetResult());
        });
    }
    private static void Command(Studio studio, DraftingCommand command) => studio.CadCommand(new(Revision(studio), command));
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static JsonElement State(Studio studio) => J(studio.State());
    private static long Revision(Studio studio) => State(studio).GetProperty("revision").GetInt64();
    private static void Require(bool condition, string? message = null) { if (!condition) throw new Exception(message ?? "Workflow assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private sealed class CancellingBaselineDriver(CancellationTokenSource cancellation) : IWorkflowDriver
    {
        public ValueTask InitializeAsync(WorkflowBaseline baseline, CancellationToken token = default)
        { cancellation.Cancel(); return ValueTask.CompletedTask; }
        public ValueTask<JsonElement> ExecuteAsync(string operation, JsonElement arguments, CancellationToken token = default)
            => throw new InvalidOperationException("No steps should run after cancellation.");
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
