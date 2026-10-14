using System.Collections.Immutable;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;

internal static class CadWorkspaceVerification
{
    private static void Require(bool condition, string message = "CAD workspace assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double a, double b) { Require(Math.Abs(a - b) < 1e-7, $"Expected {b}, received {a}"); }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new InvalidOperationException("Expected rejection"); }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("cad/selection crossing uses geometry not broad phase bounds", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector3.Zero, new Vector3(100, 100, 0)));
            var source = new ShadowSession(doc); var scene = new RenderCompiler().Compile(source.Current);
            Require(DraftingQueries.Select(scene, new(0, 90, 10, 100), DraftingSelectionMode.Crossing).IsEmpty);
            Require(DraftingQueries.Select(scene, new(45, 45, 55, 55), DraftingSelectionMode.Crossing).Length == 1);
            Require(DraftingQueries.Select(scene, new(45, 45, 55, 55), DraftingSelectionMode.Window).IsEmpty);
            Require(DraftingQueries.Select(scene, new(-1, -1, 101, 101), DraftingSelectionMode.Window).Length == 1);
        }));
        tests.Add(("cad/insert selection expands the instance and does not select its definition", () =>
        {
            var doc = new DxfDocument(); var block = new Block("PART"); block.Entities.Add(new Line(Vector3.Zero, new Vector3(20, 0, 0)));
            block.Entities.Add(new Circle(new Vector3(10, 10, 0), 3)); var insert = new Insert(block, new Vector3(50, 0, 0)); doc.Entities.Add(insert);
            var source = new ShadowSession(doc); var scene = new RenderCompiler().Compile(source.Current);
            var keys = DraftingQueries.Pick(scene, new(55, 0), 1);
            Require(keys.Length >= 2); var ids = scene.Items.Where(i => keys.Contains(i.Key)).Select(DraftingQueries.Root).Distinct().ToArray();
            Require(ids.Length == 1 && source.Current.Read(ids[0]).Type == "Insert");
        }));
        tests.Add(("cad/analytic snaps distinguish endpoints midpoints centers and intersections", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(new Vector3(-20, 0, 0), new Vector3(20, 0, 0)));
            doc.Entities.Add(new Line(new Vector3(0, -20, 0), new Vector3(0, 20, 0))); doc.Entities.Add(new Circle(new Vector3(50, 0, 0), 10));
            var source = new ShadowSession(doc); var scene = new RenderCompiler().Compile(source.Current);
            var end = DraftingQueries.Snap(source.Current, scene, new(20.1, .1), 1, ObjectSnapMode.Endpoint); Require(end is { Kind: "Endpoint", Approximate: false }); Near(end!.Position.X, 20);
            var middle = DraftingQueries.Snap(source.Current, scene, new(.2, .1), 1, ObjectSnapMode.Midpoint); Require(middle?.Kind == "Midpoint");
            var intersection = DraftingQueries.Snap(source.Current, scene, new(.1, .1), 1, ObjectSnapMode.Intersection); Require(intersection?.Kind == "Intersection");
            var center = DraftingQueries.Snap(source.Current, scene, new(50.2, .1), 1, ObjectSnapMode.Center); Require(center?.Kind == "Center");
            Require(DraftingQueries.Snap(source.Current, scene, new(60, 0), 1, ObjectSnapMode.Endpoint) is null, "Closed circles must not acquire tessellation endpoints");
            var quadrant = DraftingQueries.Snap(source.Current, scene, new(60, .1), 1, ObjectSnapMode.Quadrant); Require(quadrant?.Kind == "Quadrant");
            var perpendicular = DraftingQueries.Snap(source.Current, scene, new(5, .1), 1, ObjectSnapMode.Perpendicular, new(5, 8)); Require(perpendicular?.Kind == "Perpendicular");
        }));
        tests.Add(("cad/grips edit source geometry with canonical undo and token validation", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Circle(Vector3.Zero, 10)); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            var id = source.Current.DrawingEntities().Single().Id!.Value; var grips = DraftingGrips.Read(source.Current, [id]); Require(grips.Length == 5);
            var radius = grips.First(g => g.Kind == "Radius"); DraftingGrips.Move(source.Current, id, radius.Id, new(20, 0)).Execute(editor);
            Near(doc.Entities.Circles.Single().Radius, 20); editor.Undo(); Near(doc.Entities.Circles.Single().Radius, 10);
            Reject(() => DraftingGrips.Move(source.Current, id, "arbitrary-property", new(1, 2)));
            doc.Layers["0"].IsLocked = true; source.Refresh(); Require(DraftingGrips.Read(source.Current, [id]).IsEmpty);
            Reject(() => DraftingGrips.Move(source.Current, id, radius.Id, new(20, 0)));
        }));
        tests.Add(("cad/layers create true colors assign and undo source table membership", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0))); var source = new ShadowSession(doc); var editor = new EditorSession(source);
            DraftingLayers.Plan(source.Current, new(null, "PIPE", "#2389DB", Lineweight: "W25")).Execute(editor);
            var layer = DraftingLayers.Read(source.Current).Single(l => l.Name == "PIPE"); Require(layer.Color == "#2389DB");
            Require(doc.Layers["PIPE"].Color.UseTrueColor); Near(doc.Layers["PIPE"].Color.G, 137);
            var entity = source.Current.DrawingEntities().Single().Id!.Value;
            DraftingLayers.Assign(source.Current, [entity], layer.Id).Execute(editor); Require(doc.Entities.Lines.Single().Layer.Name == "PIPE");
            editor.Undo(); Require(doc.Entities.Lines.Single().Layer.Name == "0"); editor.Undo(); Require(!doc.Layers.Contains("PIPE"));
            editor.Redo(); Require(doc.Layers.Contains("PIPE")); Reject(() => DraftingLayers.Plan(source.Current, new(null, "pipe")));
        }));
        tests.Add(("cad/studio guarded commands preserve source selection order and history", () =>
        {
            using var studio = new Studio(false); var before = Json(studio.CadState()).GetProperty("state").GetProperty("revision").GetInt64();
            studio.CadCommand(new(before, new("LINE", [new(0, 0), new(20, 0)])));
            var data = Json(studio.CadState()); Require(data.GetProperty("selectedEntities").GetArrayLength() == 1);
            var revision = data.GetProperty("state").GetProperty("revision").GetInt64();
            studio.CadCommand(new(revision, new("COPY", [new(0, 0), new(0, 10)])));
            var next = Json(studio.CadState()); Require(next.GetProperty("itemCount").GetInt32() == 2);
            Reject(() => studio.CadCommand(new(before, new("ERASE"))));
            studio.History("undo", next.GetProperty("state").GetProperty("revision").GetInt64());
            Require(Json(studio.CadState()).GetProperty("itemCount").GetInt32() == 1);
        }));
        tests.Add(("cad/document hub isolates source state and bounds lifetimes", () =>
        {
            using var legacy = new Studio(); using var hub = new StudioDocumentHub(legacy);
            var first = Json(hub.Create(new("First.dxf"))).GetProperty("id").GetString()!;
            var second = Json(hub.Create(new("Second.dxf"))).GetProperty("id").GetString()!;
            hub.Use(first, s => s.CadCommand(new(s.CadRevision, new("CIRCLE", [new(0, 0)], Value: 4))));
            Require(hub.Use(first, s => Json(s.CadState()).GetProperty("itemCount").GetInt32()) == 1);
            Require(hub.Use(second, s => Json(s.CadState()).GetProperty("itemCount").GetInt32()) == 0);
            for (var i = 0; i < 5; i++) hub.Create(new("Drawing.dxf")); Reject(() => hub.Create(new("Overflow.dxf")));
            Reject(() => hub.Use("closed-id", s => s.State())); Reject(() => hub.Create(new("../bad.dxf")));
        }));
        tests.Add(("cad/svg background alpha is retained for a composited drafting grid", () =>
        {
            var source = new ShadowSession(new DxfDocument()); var scene = new RenderCompiler().Compile(source.Current);
            using var svg = new SvgRenderBackend(); var image = svg.Render(new(scene, new(new(0, 0), 1, 100, 100), Background: 0x00212830));
            var xml = XDocument.Parse(System.Text.Encoding.UTF8.GetString(image.Bytes)); var rectangle = xml.Root!.Elements().First();
            Require(rectangle.Attribute("fill-opacity")?.Value == "0");
        }));
    }
}
