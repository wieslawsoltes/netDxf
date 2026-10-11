using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Studio;

var tests = new List<(string Name, Action Run)>();
void Test(string name, Action run) => tests.Add((name, run));
void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
void Near(double actual, double expected, double epsilon = 1e-8) => Require(Math.Abs(actual - expected) <= epsilon, $"Expected {expected:R}, got {actual:R}");
void Reject(Action action) { try { action(); } catch (Exception) { return; } throw new InvalidOperationException("Expected operation to be rejected."); }
ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
(DxfDocument Doc, ShadowSession Session, Line Line, Circle Circle) Fixture()
{
    var document = new DxfDocument();
    var line = new Line(new Vector2(0, 0), new Vector2(10, 0));
    var circle = new Circle(new Vector2(20, 0), 5);
    document.Entities.Add(line); document.Entities.Add(circle);
    return (document, new ShadowSession(document), line, circle);
}
CadScene Segments(params (Vec3 A, Vec3 B)[] lines) => new(Guid.NewGuid(), 0, "Model",
    lines.Select((p, i) => new CadOccurrence("segment/" + i, new ObjectId("test:" + i), [], CadStyle.Default,
        new CadGeometry([new CadContour([new LineCurve(p.A, p.B)])]))).ToImmutableArray(), []);

Test("capture/public-model and no-op structural sharing", () =>
{
    var (_, session, line, circle) = Fixture();
    var snapshot = session.Current;
    Require(snapshot.DrawingEntities().Count() == 2);
    Require(snapshot.Read(Id(line)).Type == "Line");
    Near(snapshot.Read(Id(circle))["Radius"].Number(), 5);
    Require(snapshot.Objects.Count > 2);
    foreach (var node in snapshot.Objects.Values)
        foreach (var reference in node.Properties.Values.SelectMany(v => v.References())) Require(snapshot.Objects.ContainsKey(reference), "Dangling captured reference");
    foreach (var issue in snapshot.Diagnostics) Console.WriteLine($"  CAPTURE {issue.Code}: {issue.Message}");
    Require(ReferenceEquals(snapshot, session.Refresh()), "No-op refresh must retain the same snapshot instance");
});
Test("capture/detachment and explicit reverse synchronization", () =>
{
    var (_, session, line, _) = Fixture(); var old = session.Current;
    line.StartPoint = new Vector3(7, 8, 9);
    Near(old.Read(Id(line))["StartPoint"]["X"].Number(), 0);
    var next = session.Refresh(); Near(next.Read(Id(line))["StartPoint"]["X"].Number(), 7);
    Near(old.Read(Id(line))["StartPoint"]["X"].Number(), 0);
    Require(next.Revision > old.Revision);
});
Test("synchronization/canonical edit and unchanged node identity", () =>
{
    var (_, session, line, circle) = Fixture(); var before = session.Current;
    var change = new ShadowPropertyEdit(Id(line), "StartPoint", before.Read(Id(line))["StartPoint"].Value, ShadowValue.From(new Vec3(2, 3, 4)));
    var after = session.Apply(ShadowEditBatch.Create(before, change));
    Near(line.StartPoint.X, 2); Near(after.Read(Id(line))["StartPoint"]["Z"].Number(), 4);
    Require(ReferenceEquals(before.Objects[Id(circle)], after.Objects[Id(circle)]));
    Require(after.Diff(before).Changed.Contains(Id(line)));
    Reject(() => session.Apply(ShadowEditBatch.Create(before, change)));
});
Test("synchronization/invalid source setter is atomic", () =>
{
    var (_, session, line, circle) = Fixture(); var before = session.Current;
    var batch = ShadowEditBatch.Create(before,
        new(Id(line), "StartPoint", before.Read(Id(line))["StartPoint"].Value, ShadowValue.From(new Vec3(9, 9))),
        new(Id(circle), "Radius", before.Read(Id(circle))["Radius"].Value, ShadowValue.From(-2.0)));
    Reject(() => session.Apply(batch)); Near(line.StartPoint.X, 0); Near(circle.Radius, 5);
    Require(ReferenceEquals(before, session.Current));
});
Test("synchronization/external touched property conflict", () =>
{
    var (_, session, line, _) = Fixture(); var before = session.Current;
    line.StartPoint = new Vector3(1, 0, 0);
    Reject(() => session.Apply(ShadowEditBatch.Create(before, new(Id(line), "StartPoint", before.Read(Id(line))["StartPoint"].Value, ShadowValue.From(new Vec3(2, 0))))));
    Near(line.StartPoint.X, 1); session.Refresh();
});
Test("capture/structural source add and remove", () =>
{
    var (_, session, _, _) = Fixture(); var before = session.Current;
    var added = new Line(new Vector2(3, 4), new Vector2(5, 6));
    session.MutateSource(d => d.Entities.Add(added)); Require(session.Current.DrawingEntities().Count() == 3);
    var id = Id(added); Require(session.Current.EntityIds.Contains(id));
    session.MutateSource(d => d.Entities.Remove(added)); Require(session.Current.DrawingEntities().Count() == 2);
    Require(!session.Current.Objects.ContainsKey(id)); Require(before.DrawingEntities().Count() == 2);
});
Test("geometry/bulge and affine ellipse", () =>
{
    var curve = StandardEntityProjector.Bulge(new Vec3(0, 0), new Vec3(10, 0), 1);
    Near(Vec3.Distance(curve.Start, new Vec3(0, 0)), 0);
    Near(Vec3.Distance(curve.End, new Vec3(10, 0)), 0);
    Near(Math.Abs(curve.At(0.5).Y), 5);
    var matrix = Affine3.Move(new Vec3(100, -20)) * Affine3.RotateZ(0.3) * Affine3.Scale(new Vec3(2, -3, 1));
    var transformed = curve.Transform(matrix);
    for (var i = 0; i <= 10; i++) Near(Vec3.Distance(transformed.At(i / 10.0), matrix.Point(curve.At(i / 10.0))), 0, 1e-6);
});
Test("geometry/rational de Boor quarter circle", () =>
{
    var curve = new NurbsCurve([new Vec3(1, 0), new Vec3(1, 1), new Vec3(0, 1)], [1, Math.Sqrt(0.5), 1], [0, 0, 0, 1, 1, 1], 2);
    Near(curve.At(0.5).X, Math.Sqrt(0.5)); Near(curve.At(0.5).Y, Math.Sqrt(0.5));
    Near(Vec3.Distance(curve.End, new Vec3(0, 1)), 0);
    var points = CurveSampler.Sample(curve, new SamplingOptions(0.001)); Require(points.Length > 10);
    Reject(() => new NurbsCurve([Vec3.Zero, Vec3.UnitX], [1, -1], [0, 0, 1, 1], 1));
});
Test("geometry/shared blocks and occurrence transforms", () =>
{
    var document = new DxfDocument(); var block = new Block("shared");
    var line = new Line(new Vector2(0, 0), new Vector2(10, 0)); block.Entities.Add(line);
    var first = new Insert(block, new Vector2(20, 30)); var second = new Insert(block, new Vector2(-20, -30)) { Rotation = 90 };
    document.Entities.Add(first); document.Entities.Add(second);
    var session = new ShadowSession(document); var scene = new CadSceneBuilder().Build(session.Current);
    Require(scene.Occurrences.Length == 2); Require(scene.Occurrences[0].Entity == scene.Occurrences[1].Entity);
    Require(scene.Occurrences[0].Key != scene.Occurrences[1].Key);
    var firstCurve = scene.Occurrences.Single(o => o.InsertPath.Contains(Id(first))).Geometry.Contours[0].Curves[0];
    Near(firstCurve.Start.X, 20); Near(firstCurve.End.X, 30);
    var secondCurve = scene.Occurrences.Single(o => o.InsertPath.Contains(Id(second))).Geometry.Contours[0].Curves[0];
    Near(secondCurve.End.X, -20); Near(secondCurve.End.Y, -20);
});
Test("spatial/BVH agrees with brute force", () =>
{
    var random = new Random(1776);
    var boxes = Enumerable.Range(0, 500).Select(_ => { var x = random.NextDouble() * 100; var y = random.NextDouble() * 100; return new Bounds2(x, y, x + random.NextDouble() * 5, y + random.NextDouble() * 5); }).ToArray();
    var index = new SpatialIndex(boxes);
    for (var i = 0; i < 100; i++)
    {
        var x = random.NextDouble() * 100; var y = random.NextDouble() * 100; var query = new Bounds2(x, y, x + 8, y + 8);
        Require(index.Query(query).Order().SequenceEqual(Enumerable.Range(0, boxes.Length).Where(n => boxes[n].Intersects(query))));
    }
});
Test("graph/crossing split, bridges and shortest path", () =>
{
    var graph = new TopologyBuilder().Build(Segments((new Vec3(-10, 0), new Vec3(10, 0)), (new Vec3(0, -10), new Vec3(0, 10))));
    Require(graph.Nodes.Length == 5, "Crossing must create one junction"); Require(graph.Edges.Length == 4);
    Require(graph.Junctions.Single().Edges.Length == 4); Require(graph.Bridges().Length == 4); Require(graph.CycleRank == 0);
    var left = graph.Nodes.Single(n => n.Position.X == -10).Id; var top = graph.Nodes.Single(n => n.Position.Y == 10).Id;
    Near(graph.ShortestPath(left, top)!.Length, 20);
});
Test("graph/elevation prevents false connection", () =>
{
    var graph = new TopologyBuilder().Build(Segments((new Vec3(-10, 0), new Vec3(10, 0)), (new Vec3(0, -10, 2), new Vec3(0, 10, 2))));
    Require(graph.ConnectedComponents().Length == 2); Require(graph.Edges.Length == 2);
});
Test("graph/cycle rank and parallel edge bridges", () =>
{
    var graph = new TopologyBuilder().Build(Segments((Vec3.Zero, new Vec3(10, 0)), (new Vec3(10, 0), new Vec3(10, 10)), (new Vec3(10, 10), new Vec3(0, 10)), (new Vec3(0, 10), Vec3.Zero)));
    Require(graph.CycleRank == 1); Require(graph.Bridges().IsEmpty);
    var parallel = new TopologyBuilder().Build(Segments((Vec3.Zero, new Vec3(10, 0)), (Vec3.Zero, new Vec3(10, 0))));
    Require(parallel.CycleRank == 1); Require(parallel.Bridges().IsEmpty);
});
Test("editor/canonical history and stale-history protection", () =>
{
    var (_, session, line, _) = Fixture(); var editor = new EditorSession(session); var original = session.Current;
    editor.SetProperty(Id(line), "StartPoint", ShadowValue.From(new Vec3(3, 4))); Near(line.StartPoint.X, 3);
    editor.Undo(); Near(line.StartPoint.X, 0); editor.Redo(); Near(line.StartPoint.X, 3);
    Near(original.Read(Id(line))["StartPoint"]["X"].Number(), 0);
    session.MutateSource(_ => line.StartPoint = new Vector3(8, 0, 0)); Reject(() => editor.Undo()); editor.ResetHistory();
    editor.SetProperty(Id(line), "StartPoint", ShadowValue.From(new Vec3(9, 0))); Near(line.StartPoint.X, 9);
});
Test("editor/memory branch and snapping", () =>
{
    var (_, source, line, _) = Fixture(); var memory = new ShadowMemoryStore(source.Current); var editor = new EditorSession(memory);
    editor.SetProperty(Id(line), "StartPoint", ShadowValue.From(new Vec3(5, 0))); Near(line.StartPoint.X, 0);
    var scene = new RenderCompiler().Compile(source.Current);
    var hit = RenderCompiler.HitTest(scene, new Vec3(5, 0), 0.1); Require(hit?.Entity == Id(line));
    var snap = Snapping.Find(scene, new Vec3(0.04, 0.03), 0.1, SnapMode.Vertex); Require(snap is not null); Near(snap!.Position.X, 0);
});
Test("render/runtime backend swap, PNG and safe SVG", () =>
{
    var (document, session, _, _) = Fixture();
    session.MutateSource(d => d.Entities.Add(new Text("<script>& Unicode Ω", new Vector2(0, 10), 3)));
    var compiler = new RenderCompiler(); var scene = compiler.Compile(session.Current);
    Require(ReferenceEquals(scene, compiler.Compile(session.Current)));
    using var host = new RenderHost(new Dictionary<string, Func<IRenderBackend>> { ["skia"] = () => new SkiaRenderBackend(), ["svg"] = () => new SvgRenderBackend() }, "skia");
    var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 640, 360));
    var png = host.Render(frame); Require(png.ContentType == "image/png"); Require(png.Bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
    host.Swap("svg"); var svg = host.Render(frame); var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
    Require(!xml.Descendants().Any(e => e.Name.LocalName == "script")); Require(xml.Descendants().Any(e => e.Name.LocalName == "text"));
    Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/sample.png", png.Bytes); File.WriteAllBytes("artifacts/sample.svg", svg.Bytes);
});
Test("sample/state JSON and integrated edit/render/graph/save", () =>
{
    using var studio = new Studio(); var json = JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    var revision = json.GetProperty("revision").GetInt64(); Require(json.GetProperty("occurrences").GetInt32() > 10);
    _ = JsonSerializer.Serialize(studio.Graph(CancellationToken.None));
    var image = studio.Render(800, 500, null, null, null, "skia", null, CancellationToken.None); Require(image.Bytes.Length > 1000);
    studio.Retain(revision); studio.SourceDemo(revision);
    var after = JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web)); Require(after.GetProperty("revision").GetInt64() > revision);
    var saved = studio.Save(); Require(saved.Length > 1000); studio.Load(new MemoryStream(saved));
    _ = JsonSerializer.Serialize(studio.State());
});
Test("performance/10k lines and constant-time snapshot reads", () =>
{
    var document = new DxfDocument();
    for (var i = 0; i < 10000; i++) document.Entities.Add(new Line(new Vector2(i % 100, i / 100), new Vector2(i % 100 + 0.75, i / 100 + 0.75)));
    var timer = Stopwatch.StartNew(); var allocated = GC.GetAllocatedBytesForCurrentThread();
    var session = new ShadowSession(document); var captureMs = timer.Elapsed.TotalMilliseconds; var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
    var snapshot = session.Current; timer.Restart();
    for (var i = 0; i < 1000000; i++) Require(ReferenceEquals(snapshot, session.Current));
    var readsMs = timer.Elapsed.TotalMilliseconds;
    timer.Restart(); Require(ReferenceEquals(snapshot, session.Refresh())); var refreshMs = timer.Elapsed.TotalMilliseconds;
    Console.WriteLine(JsonSerializer.Serialize(new { entities = 10000, objects = snapshot.Objects.Count, captureMs, allocatedBytes = bytes, millionSnapshotReadsMs = readsMs, noOpRefreshMs = refreshMs }));
});

var failed = 0;
foreach (var (name, run) in tests)
{
    var timer = Stopwatch.StartNew();
    try { run(); Console.WriteLine($"PASS {name} ({timer.Elapsed.TotalMilliseconds:F1} ms)"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}\n{error}"); }
}
Console.WriteLine($"Verification: {tests.Count - failed}/{tests.Count} passed.");
return failed == 0 ? 0 : 1;
