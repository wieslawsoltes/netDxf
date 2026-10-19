using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Health;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Modules;
using netDxf.Shadow.Studio;

internal static class HealthPreviewVerification
{
    private static readonly JsonSerializerOptions Json = StudioJson.CreateOptions();
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Add(string name, Action test) => tests.Add(("Health/preview: " + name, test));
        Add("duplicates and tiny gaps are deterministic source-only suggestions", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(new Vector3(0, 0, 0), new(10, 0, 0))); doc.Entities.Add(new Line(new Vector3(10, 0, 0), new(0, 0, 0)));
            doc.Entities.Add(new Line(new Vector3(10.005, 0, 0), new(20, 0, 0))); doc.Entities.Add(new Line(new Vector3(0, 0, 10), new(10, 0, 10)));
            var source = new ShadowSession(doc); var before = source.Current; var report = DrawingHealth.Analyze(before);
            Require(report.Complete && report.Issues.Count(i => i.Code == "geometry.duplicate") == 1 && report.Issues.Any(i => i.Code == "geometry.tiny-gap"));
            Require(J(report.Issues).GetRawText() == J(DrawingHealth.Analyze(before).Issues).GetRawText() && ReferenceEquals(source.Current, before));
        });
        Add("invalid geometry capture faults and dangling references have precise paths", () =>
        {
            var doc = new DxfDocument(); var circle = new Circle(Vector3.Zero, 3); doc.Entities.Add(circle); var source = new ShadowSession(doc); var s = source.Current; var id = new ObjectId("H:" + circle.Handle);
            var node = s.Objects[id] with { Properties = s.Objects[id].Properties.SetItem("Radius", ShadowValue.From(-1.0)).SetItem("Thickness", ShadowValue.From(double.NaN)).SetItem("Missing", new ShadowValue.Reference(new("absent"))).SetItem("Broken", new ShadowValue.Fault("fixture fault")) };
            var report = DrawingHealth.Analyze(s with { Objects = s.Objects.SetItem(id, node) });
            foreach (var code in new[] { "geometry.invalid", "geometry.nonfinite", "graph.reference", "capture.fault" }) Require(report.Issues.Any(i => i.Code == code));
            Require(report.Issues.Single(i => i.Code == "geometry.nonfinite").Path == "Thickness" && circle.Radius == 3);
        });
        Add("unused definitions and font inventory are explicit", () =>
        {
            var doc = new DxfDocument(); doc.Blocks.Add(new Block("Unused fixture")); doc.Linetypes.Add(new Linetype("Unused dash", [new LinetypeSimpleSegment(2)])); doc.TextStyles.Add(new TextStyle("Unused font", "absent.shx"));
            var s = new ShadowSession(doc).Current; var report = DrawingHealth.Analyze(s, resources: new(ImmutableHashSet.Create<string>(StringComparer.OrdinalIgnoreCase)));
            foreach (var code in new[] { "resource.unused-block", "resource.unused-linetype", "resource.unused-font-style", "resource.missing-shx" }) Require(report.Issues.Any(i => i.Code == code));
            Require(!DrawingHealth.Analyze(s).Issues.Any(i => i.Code == "resource.missing-shx"));
        });
        Add("budgets and cancellation never report completed checks", () =>
        {
            var s = new ShadowSession(new DxfDocument()).Current;
            Require(!DrawingHealth.Analyze(s, new(MaximumWork: 1)).Complete && !DrawingHealth.Analyze(s, new(MaximumObjects: 1)).Complete);
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); Throws<OperationCanceledException>(() => DrawingHealth.Analyze(s, cancellationToken: cancel.Token));
            Throws<ArgumentOutOfRangeException>(() => DrawingHealth.Analyze(s, new(GapTolerance: 0)));
            var minimal = DrawingHealth.Analyze(s, new(FindDuplicates: false, FindTinyGaps: false, FindUnusedResources: false));
            Require(!minimal.Coverage.Any(c => c.Contains("duplicates") || c.Contains("endpoint gaps") || c.Contains("Unused") || c.Contains("SHX")));
        });
        Add("recursive block references are diagnosed without native expansion", () =>
        {
            var doc = new DxfDocument(); var leaf = new Block("Leaf"); leaf.Entities.Add(new Circle(Vector3.Zero, 2));
            var parent = new Block("Parent"); var nested = new Insert(leaf); parent.Entities.Add(nested); doc.Entities.Add(new Insert(parent));
            var source = new ShadowSession(doc); var snapshot = source.Current; var nestedId = new ObjectId("H:" + nested.Handle); var parentId = new ObjectId("H:" + parent.Handle);
            var broken = snapshot.Objects[nestedId] with { Properties = snapshot.Objects[nestedId].Properties.SetItem("Block", new ShadowValue.Reference(parentId)) };
            var report = DrawingHealth.Analyze(snapshot with { Objects = snapshot.Objects.SetItem(nestedId, broken) });
            Require(report.Complete && report.Issues.Any(i => i.Code == "block.recursion" && i.Object == parentId));
            Require(ReferenceEquals(nested.Block, leaf) && ReferenceEquals(source.Current, snapshot));
        });
        Add("editor preview renders graph-valid native failure honestly without undo", () =>
        {
            var doc = new DxfDocument(); var circle = new Circle(Vector3.Zero, 3); doc.Entities.Add(circle); var source = new ShadowSession(doc); var editor = new EditorSession(source); var before = source.Current;
            var graph = new ShadowGraphEdit(before); graph.Set(new("H:" + circle.Handle), "Radius", ShadowValue.From(-2.0)); var preview = editor.Preview("Invalid radius", graph.Build());
            Require(preview.Validation.Contains("has not run") && ReferenceEquals(before, source.Current)); Throws<Exception>(() => editor.ApplyPreview(preview));
            Require(circle.Radius == 3 && ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0);
        });
        Add("studio multi-operation preview apply one undo render and DXF roundtrip", () =>
        {
            using var studio = new Studio(false); var receipt = J(studio.CadEditPreview(new(0, "Line and circle", [
                new("command", new("LINE", [new(0, 0), new(10, 0)])), new("command", new("CIRCLE", [new(25, 5)], Value: 3))])));
            var token = receipt.GetProperty("previewId").GetString()!; Require(receipt.GetProperty("current").GetBoolean()); Require(State(studio).GetProperty("entities").GetInt32() == 0);
            var before = studio.CadEditPreviewRender(token, false); var after = studio.CadEditPreviewRender(token, true);
            Require(before.Bytes.Length > 50 && after.Bytes.Length > before.Bytes.Length && !before.Bytes.SequenceEqual(after.Bytes));
            studio.CadEditPreviewApply(new(0, token)); Require(State(studio).GetProperty("undoCount").GetInt32() == 1);
            using var file = new MemoryStream(studio.Save()); var dxf = DxfDocument.Load(file)!; Require(dxf.Entities.Lines.Single().EndPoint.X == 10 && dxf.Entities.Circles.Single().Radius == 3);
            studio.History("undo", Revision(studio)); Require(State(studio).GetProperty("entities").GetInt32() == 0);
            studio.History("redo", Revision(studio)); Require(State(studio).GetProperty("entities").GetInt32() == 2);
        });
        Add("sequential create and move uses created selection", () =>
        {
            using var studio = new Studio(false); var p = J(studio.CadEditPreview(new(0, "Create and move", [new("command", new("LINE", [new(0, 0), new(10, 0)])), new("command", new("MOVE", [new(0, 0), new(5, 7)]))])));
            studio.CadEditPreviewApply(new(0, p.GetProperty("previewId").GetString()!)); using var file = new MemoryStream(studio.Save()); var line = DxfDocument.Load(file)!.Entities.Lines.Single(); Require(line.StartPoint == new Vector3(5, 7, 0) && line.EndPoint == new Vector3(15, 7, 0));
        });
        Add("stale exact target survives neither edit and undo nor workspace switch", () =>
        {
            using var studio = new Studio(false); var p = Proposal(studio); studio.Add(new(0, "line", 0, 0)); studio.History("undo", Revision(studio));
            Throws<ShadowConflictException>(() => studio.CadEditPreviewApply(new(Revision(studio), p))); var next = Proposal(studio); studio.Fork(new(Revision(studio), "Preview epoch")); studio.SwitchWorkspace(new(Revision(studio), "live"));
            Throws<ShadowConflictException>(() => studio.CadEditPreviewApply(new(Revision(studio), next)));
        });
        Add("selection changes invalidate preview and health selection stays narrow", () =>
        {
            using var studio = new Studio(false); studio.CadCommand(new(0, new("LINE", [new(0, 0), new(10, 0)]))); studio.CadCommand(new(Revision(studio), new("LINE", [new(0, 0), new(10, 0)]))); studio.CadCommand(new(Revision(studio), new("CIRCLE", [new(50, 50)], Value: 2)));
            var p = Proposal(studio); studio.CadSelect(new(Revision(studio), "clear")); Throws<ShadowConflictException>(() => studio.CadEditPreviewApply(new(Revision(studio), p)));
            var health = J(studio.CadHealth(new(Revision(studio)))); var issue = health.GetProperty("report").GetProperty("issues").EnumerateArray().First(i => i.GetProperty("code").GetString() == "geometry.duplicate");
            var result = J(studio.CadHealthSelect(new(Revision(studio), health.GetProperty("reportId").GetString()!, issue.GetProperty("id").GetString()!)));
            Require(result.GetProperty("selectedCount").GetInt32() == 2);
        });
        Add("rejected planning leaves source unchanged and invalid operation is explicit", () =>
        {
            using var studio = new Studio(false); Throws<ArgumentException>(() => studio.CadEditPreview(new(0, "bad", [new("command", new("LINE", [new(0, 0), new(5, 0)])), new("arbitrary-code")])));
            Require(Revision(studio) == 0 && State(studio).GetProperty("entities").GetInt32() == 0 && State(studio).GetProperty("undoCount").GetInt32() == 0);
        });
        Add("reordering the same selected objects expires a preview without changing source", () =>
        {
            using var studio = new Studio(false);
            studio.CadCommand(new(0, new("LINE", [new(0, 0), new(10, 0)])));
            studio.CadCommand(new(Revision(studio), new("LINE", [new(0, 10), new(10, 10)])));
            var ids = J(studio.CadState()).GetProperty("items").EnumerateArray()
                .Select(item => new ObjectId(item.GetProperty("id").GetString()!)).Distinct().ToImmutableArray();
            studio.CadSelect(new(Revision(studio), "entities", Entities: ids));
            var token = J(studio.CadEditPreview(new(Revision(studio), "Move selected", [new("command", new("MOVE", [new(0, 0), new(5, 0)]))])))
                .GetProperty("previewId").GetString()!;
            var revision = Revision(studio); var undo = State(studio).GetProperty("undoCount").GetInt32();
            studio.CadSelect(new(revision, "entities", Entities: ids.Reverse().ToImmutableArray()));
            Require(!J(studio.CadEditPreviews())[0].GetProperty("current").GetBoolean());
            Throws<ShadowConflictException>(() => studio.CadEditPreviewApply(new(revision, token)));
            Require(Revision(studio) == revision && State(studio).GetProperty("undoCount").GetInt32() == undo);
        });
        Add("module disable and reenable permanently expires earlier previews", () =>
        {
            using var studio = new Studio(false); studio.CadSetModule(new(0, "pid", true));
            var command = new CadModuleCommandRequest("route", [new(0, 0), new(20, 0)], Properties: new Dictionary<string, string>
                { ["tag"] = "P-101", ["nominalSize"] = "50", ["service"] = "Water", ["spec"] = "CS", ["flow"] = "forward" }.ToImmutableDictionary());
            var request = new CadEditPreviewRequest(0, "Process pipe", [new("module", Module: "pid", ModuleCommand: command)]);
            var token = J(studio.CadEditPreview(request)).GetProperty("previewId").GetString()!;
            Require(State(studio).GetProperty("entities").GetInt32() == 0);
            studio.CadSetModule(new(0, "pid", false)); studio.CadSetModule(new(0, "pid", true));
            Throws<ShadowConflictException>(() => studio.CadEditPreviewApply(new(0, token)));
            token = J(studio.CadEditPreview(request)).GetProperty("previewId").GetString()!; studio.CadEditPreviewApply(new(0, token));
            Require(State(studio).GetProperty("undoCount").GetInt32() == 1 && J(studio.CadAnalyzeModule(new(Revision(studio), "pid"))).GetProperty("records").GetArrayLength() == 1);
        });
        Add("MCP preview requires exact identity and marks apply as a write", () =>
        {
            using var studio = new Studio(false); var tools = studio.CreateMcpRegistry(); var state = State(studio);
            var identity = new StudioRequestIdentity(state.GetProperty("documentId").GetGuid(), state.GetProperty("workspaceId").GetString()!, state.GetProperty("viewVersion").GetInt64());
            Require(tools.Tools.Single(t => t.Name == "studio_edit_preview").ReadOnly && !tools.Tools.Single(t => t.Name == "studio_apply_edit_preview").ReadOnly);
            var request = new { revision = 0, description = "MCP proposal", operations = new[] { new { kind = "command", command = new { name = "CIRCLE", points = new[] { new { x = 2, y = 4 } }, value = 3 } } } };
            McpToolResult Prepare(StudioRequestIdentity receipt) => tools.ExecuteAsync("studio_edit_preview", McpJson.Element(new { identity = receipt, request })).GetAwaiter().GetResult();
            Require(Prepare(identity with { DocumentId = Guid.NewGuid() }).IsError);
            Require(tools.ExecuteAsync("studio_edit_preview", McpJson.Element(new { request })).GetAwaiter().GetResult().IsError);
            var proposal = Prepare(identity); Require(!proposal.IsError); var token = McpJson.Parse(proposal.Text).GetProperty("previewId").GetString()!;
            Require(Revision(studio) == 0 && State(studio).GetProperty("entities").GetInt32() == 0);
            var apply = new { identity, request = new { revision = 0, previewId = token } };
            Require(!tools.ExecuteAsync("studio_apply_edit_preview", McpJson.Element(apply)).GetAwaiter().GetResult().IsError);
            Require(State(studio).GetProperty("undoCount").GetInt32() == 1 && State(studio).GetProperty("entities").GetInt32() == 1);
            Require(tools.ExecuteAsync("studio_apply_edit_preview", McpJson.Element(apply)).GetAwaiter().GetResult().IsError);
            Require(State(studio).GetProperty("undoCount").GetInt32() == 1);
        });
        Add("health selection rejects reports from a prior source snapshot", () =>
        {
            using var studio = new Studio(false); studio.CadCommand(new(0, new("LINE", [new(0, 0), new(10, 0)]))); studio.CadCommand(new(Revision(studio), new("LINE", [new(0, 0), new(10, 0)])));
            var receipt = J(studio.CadHealth(new(Revision(studio)))); var issue = receipt.GetProperty("report").GetProperty("issues").EnumerateArray().First(i => i.GetProperty("code").GetString() == "geometry.duplicate");
            studio.CadCommand(new(Revision(studio), new("CIRCLE", [new(40, 20)], Value: 3)));
            Throws<ShadowConflictException>(() => studio.CadHealthSelect(new(Revision(studio), receipt.GetProperty("reportId").GetString()!, issue.GetProperty("id").GetString()!)));
        });
    }
    private static string Proposal(Studio studio) => J(studio.CadEditPreview(new(Revision(studio), "Circle", [new("command", new("CIRCLE", [new(0, 0)], Value: 2))]))).GetProperty("previewId").GetString()!;
    private static JsonElement J(object value) => JsonSerializer.SerializeToElement(value, Json);
    private static JsonElement State(Studio studio) => J(studio.State());
    private static long Revision(Studio studio) => State(studio).GetProperty("revision").GetInt64();
    private static void Require(bool condition) { if (!condition) throw new Exception("Health/preview assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
}
