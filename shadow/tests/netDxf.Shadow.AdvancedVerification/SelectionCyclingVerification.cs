using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Studio;

internal static class SelectionCyclingVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected overlap query rejection.");
    }
    private static Studio Open(DxfDocument drawing)
    {
        using var bytes = new MemoryStream(); Check(drawing.Save(bytes), "Fixture save failed."); bytes.Position = 0;
        var studio = new Studio(false); studio.Load(bytes); return studio;
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions());
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("selection-cycling/read-only overlaps preserve hit order and ignore elevation, hidden layers and bounds-only misses", () =>
        {
            var drawing = new DxfDocument();
            var first = new Line(new Vector3(0, 0, 0), new(10, 0, 0));
            var second = new Line(new Vector3(0, 0, 10), new(10, 0, 10));
            var hidden = new Line(Vector2.Zero, new Vector2(10, 0)) { Layer = new Layer("Hidden") { IsVisible = false } };
            drawing.Entities.Add([first, second, hidden, new Circle(new Vector2(5, 0), 10)]);
            using var studio = Open(drawing); var before = State(studio).GetRawText();
            var result = studio.CadSelectionCandidates(new(studio.CadRevision, "Model", new(5, 0, 999), .01));
            Check(result.Candidates.Length == 2 && !result.Truncated, "Exact hit test included invisible or bounds-only geometry.");
            Check(result.Candidates[0].Handle == second.Handle && result.Candidates[1].Handle == first.Handle, "Painter order changed.");
            Check(result.Candidates.All(c => c.Entity == c.LeafEntity && c.Preview.Length > 0 && !c.PreviewTruncated), "Ordinary source preview lost identity.");
            Check(before == State(studio).GetRawText(), "Read-only query changed selection, source, or history.");
        }));
        tests.Add(("selection-cycling/INSERT leaves collapse to selectable roots with accurate occurrence and source linkage", () =>
        {
            var drawing = new DxfDocument(); var block = new Block("Overlaps");
            block.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
            block.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
            var one = new Insert(block); var two = new Insert(block); drawing.Entities.Add([one, two]);
            using var studio = Open(drawing);
            var result = studio.CadSelectionCandidates(new(studio.CadRevision, "Model", new(5, 0), .01));
            Check(result.Candidates.Length == 2 && result.Candidates.All(c => c.Entity != c.LeafEntity && c.Type == "Insert"), "INSERT identity was replaced by a leaf.");
            Check(result.Candidates.Select(c => c.Occurrence).Distinct().Count() == 2, "Repeated instances lost occurrence identity.");
            studio.CadSelect(new(studio.CadRevision, "entity", Entity: result.Candidates[1].Entity));
            var selected = State(studio).GetProperty("selectedEntities");
            Check(selected.GetArrayLength() == 1 && selected[0].GetString() == result.Candidates[1].Entity, "Chooser did not select its exact root.");
            studio.CadSelect(new(studio.CadRevision, "entity", Extend: true, Entity: result.Candidates[0].Entity));
            Check(State(studio).GetProperty("selectedEntities").GetArrayLength() == 2, "Additive root selection failed.");
            studio.CadSelect(new(studio.CadRevision, "entity", Remove: true, Entity: result.Candidates[1].Entity));
            Check(State(studio).GetProperty("selectedEntities")[0].GetString() == result.Candidates[0].Entity, "Removal selected the wrong occurrence root.");
        }));
        tests.Add(("selection-cycling/result and preview budgets are explicit, finite and bounded", () =>
        {
            var drawing = new DxfDocument();
            for (var i = 0; i < 80; i++) drawing.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
            using var studio = Open(drawing);
            var result = studio.CadSelectionCandidates(new(studio.CadRevision, "Model", new(5, 0), .01, 4));
            Check(result.Candidates.Length == 4 && result.Truncated, "Limited page claimed complete coverage.");
            Check(JsonSerializer.SerializeToUtf8Bytes(result, StudioJson.CreateOptions()).Length <= 128 * 1024, "Response exceeded its byte cap.");
            var geometry = new DxfDocument();
            geometry.Entities.Add(new Polyline2D(Enumerable.Range(0, 600).Select(i => new Polyline2DVertex(i, i % 2)).ToList()));
            using var detailed = Open(geometry);
            var preview = detailed.CadSelectionCandidates(new(detailed.CadRevision, "Model", new(.5, .5), .01)).Candidates.Single();
            Check(preview.PreviewTruncated && preview.Preview.Sum(p => p.Points.Length) <= 256, "Complex preview was silently decimated or unbounded.");
        }));
        tests.Add(("selection-cycling/stale layouts and revisions, malformed input and cancellation fail before changes", () =>
        {
            using var studio = new Studio(false);
            studio.CadCommand(new(studio.CadRevision, new("LINE", [Vec3.Zero, Vec3.UnitX])));
            var request = new CadSelectionCandidatesRequest(studio.CadRevision, "Model", Vec3.Zero, .01);
            Reject(() => studio.CadSelectionCandidates(request with { Layout = "Other" }));
            Reject(() => studio.CadSelectionCandidates(request with { Revision = request.Revision - 1 }));
            Reject(() => studio.CadSelectionCandidates(request with { Point = new(double.NaN, 0) }));
            Reject(() => studio.CadSelectionCandidates(request with { Tolerance = double.PositiveInfinity }));
            Reject(() => studio.CadSelectionCandidates(request with { Limit = 0 }));
            Reject(() => studio.CadSelectionCandidates(request with { Limit = 65 }));
            try { studio.CadSelectionCandidates(request, new(true)); throw new InvalidOperationException("Cancellation ignored."); }
            catch (OperationCanceledException) { }
            Check(studio.CadSelectionCandidates(request).Candidates.Length == 1, "Rejected query corrupted subsequent reads.");
        }));
    }
}
