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
using StudioHost = netDxf.Shadow.Studio.Studio;

namespace netDxf.Shadow.Verification;

internal static class Program
{
    private static int Main()
    {
        (string Name, Action Run)[] tests =
        [
            ("capture/public graph and no-op structural sharing", CapturePublicGraph),
            ("capture/detachment and explicit reverse synchronization", CaptureDetachment),
            ("synchronization/canonical edit and node sharing", CanonicalEdit),
            ("synchronization/invalid setter atomicity", InvalidSetter),
            ("synchronization/external touched property conflict", ExternalConflict),
            ("capture/source add and remove", StructuralCapture),
            ("geometry/bulge and affine ellipse", BulgeGeometry),
            ("geometry/rational de Boor quarter circle", NurbsGeometry),
            ("geometry/shared blocks and occurrence transforms", BlockInstances),
            ("spatial/BVH agrees with brute force", SpatialQueries),
            ("graph/crossing split, bridges and shortest path", GraphCrossing),
            ("graph/elevation prevents false connection", GraphElevation),
            ("graph/cycles and parallel edges", GraphCycles),
            ("editor/canonical history and stale-history protection", EditorHistory),
            ("editor/isolated memory branch and snapping", EditorBranch),
            ("render/backend swap, PNG and safe SVG", RenderBackends),
            ("sample/state, render, graph, synchronization and save", IntegratedStudio),
            ("performance/10k lines and snapshot acquisition", PerformanceSmoke)
        ];
        var failures = 0;
        foreach (var test in tests)
        {
            var timer = Stopwatch.StartNew();
            try { test.Run(); Console.WriteLine($"PASS {test.Name} ({timer.Elapsed.TotalMilliseconds:F1} ms)"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {test.Name}\n{error}"); }
        }
        Console.WriteLine($"Verification: {tests.Length - failures}/{tests.Length} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void Require(bool condition, string message = "Assertion failed")
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Near(double actual, double expected, double epsilon = 1e-8)
        => Require(Math.Abs(actual - expected) <= epsilon, $"Expected {expected:R}, got {actual:R}");
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new InvalidOperationException("Expected operation to be rejected.");
    }
    private static ObjectId Id(DxfObject source) => new ObjectId("H:" + source.Handle.ToUpperInvariant());
    private static (DxfDocument Document, ShadowSession Session, Line Line, Circle Circle) Fixture()
    {
        var document = new DxfDocument();
        var line = new Line(new Vector2(0, 0), new Vector2(10, 0));
        var circle = new Circle(new Vector2(20, 0), 5);
        document.Entities.Add(line);
        document.Entities.Add(circle);
        return (document, new ShadowSession(document), line, circle);
    }
    private static CadScene Segments(params (Vec3 A, Vec3 B)[] lines)
    {
        var occurrences = ImmutableArray.CreateBuilder<CadOccurrence>();
        for (var index = 0; index < lines.Length; index++)
        {
            var pair = lines[index];
            occurrences.Add(new CadOccurrence("segment/" + index, new ObjectId("test:" + index), [], CadStyle.Default,
                new CadGeometry([new CadContour([new LineCurve(pair.A, pair.B)])])));
        }
        return new CadScene(Guid.NewGuid(), 0, "Model", occurrences.ToImmutable(), []);
    }

    private static void CapturePublicGraph()
    {
        var fixture = Fixture();
        var snapshot = fixture.Session.Current;
        Require(snapshot.DrawingEntities().Count() == 2, "Expected two model-space entities.");
        Require(snapshot.Read(Id(fixture.Line)).Type == "Line");
        Near(snapshot.Read(Id(fixture.Circle))["Radius"].Number(), 5);
        Require(snapshot.Objects.Count > 2);
        foreach (var node in snapshot.Objects.Values)
            foreach (var reference in node.Properties.Values.SelectMany(value => value.References()))
                Require(snapshot.Objects.ContainsKey(reference), $"Dangling captured reference {reference}.");
        foreach (var issue in snapshot.Diagnostics) Console.WriteLine($"CAPTURE {issue.Code}: {issue.Message}");
        var refreshed = fixture.Session.Refresh();
        if (!ReferenceEquals(snapshot, refreshed))
        {
            var diff = refreshed.Diff(snapshot);
            foreach (var id in diff.Changed.Take(20))
            {
                var before = snapshot.Objects[id];
                var after = refreshed.Objects[id];
                var properties = after.Properties.Where(p => !ShadowValue.Equivalent(before[p.Key], p.Value)).Select(p => p.Key);
                Console.WriteLine($"UNSTABLE {id} {after.Type}: {string.Join(", ", properties)}");
            }
            Console.WriteLine($"REFRESH added={diff.Added.Count}, changed={diff.Changed.Count}, removed={diff.Removed.Count}");
        }
        Require(ReferenceEquals(snapshot, refreshed), "No-op refresh must retain the same snapshot instance.");
    }
    private static void CaptureDetachment()
    {
        var fixture = Fixture();
        var retained = fixture.Session.Current;
        fixture.Line.StartPoint = new Vector3(7, 8, 9);
        Near(retained.Read(Id(fixture.Line))["StartPoint"]["X"].Number(), 0);
        var next = fixture.Session.Refresh();
        Near(next.Read(Id(fixture.Line))["StartPoint"]["X"].Number(), 7);
        Near(retained.Read(Id(fixture.Line))["StartPoint"]["X"].Number(), 0);
        Require(next.Revision > retained.Revision);
    }
    private static void CanonicalEdit()
    {
        var fixture = Fixture();
        var before = fixture.Session.Current;
        var change = new ShadowPropertyEdit(Id(fixture.Line), "StartPoint", before.Read(Id(fixture.Line))["StartPoint"].Value, ShadowValue.From(new Vec3(2, 3, 4)));
        var after = fixture.Session.Apply(ShadowEditBatch.Create(before, change));
        Near(fixture.Line.StartPoint.X, 2);
        Near(after.Read(Id(fixture.Line))["StartPoint"]["Z"].Number(), 4);
        Require(ReferenceEquals(before.Objects[Id(fixture.Circle)], after.Objects[Id(fixture.Circle)]));
        Require(after.Diff(before).Changed.Contains(Id(fixture.Line)));
        Reject(() => fixture.Session.Apply(ShadowEditBatch.Create(before, change)));
    }
    private static void InvalidSetter()
    {
        var fixture = Fixture();
        var before = fixture.Session.Current;
        var batch = ShadowEditBatch.Create(before,
            new ShadowPropertyEdit(Id(fixture.Line), "StartPoint", before.Read(Id(fixture.Line))["StartPoint"].Value, ShadowValue.From(new Vec3(9, 9))),
            new ShadowPropertyEdit(Id(fixture.Circle), "Radius", before.Read(Id(fixture.Circle))["Radius"].Value, ShadowValue.From(-2.0)));
        Reject(() => fixture.Session.Apply(batch));
        Near(fixture.Line.StartPoint.X, 0);
        Near(fixture.Circle.Radius, 5);
        Require(ReferenceEquals(before, fixture.Session.Current));
    }
    private static void ExternalConflict()
    {
        var fixture = Fixture();
        var before = fixture.Session.Current;
        fixture.Line.StartPoint = new Vector3(1, 0, 0);
        var change = new ShadowPropertyEdit(Id(fixture.Line), "StartPoint", before.Read(Id(fixture.Line))["StartPoint"].Value, ShadowValue.From(new Vec3(2, 0)));
        Reject(() => fixture.Session.Apply(ShadowEditBatch.Create(before, change)));
        Near(fixture.Line.StartPoint.X, 1);
        fixture.Session.Refresh();
    }
    private static void StructuralCapture()
    {
        var fixture = Fixture();
        var before = fixture.Session.Current;
        var added = new Line(new Vector2(3, 4), new Vector2(5, 6));
        fixture.Session.MutateSource(document => document.Entities.Add(added));
        Require(fixture.Session.Current.DrawingEntities().Count() == 3);
        var id = Id(added);
        Require(fixture.Session.Current.EntityIds.Contains(id));
        fixture.Session.MutateSource(document => document.Entities.Remove(added));
        Require(fixture.Session.Current.DrawingEntities().Count() == 2);
        Require(!fixture.Session.Current.Objects.ContainsKey(id));
        Require(before.DrawingEntities().Count() == 2);
    }
    private static void BulgeGeometry()
    {
        var curve = StandardEntityProjector.Bulge(new Vec3(0, 0), new Vec3(10, 0), 1);
        Near(Vec3.Distance(curve.Start, new Vec3(0, 0)), 0);
        Near(Vec3.Distance(curve.End, new Vec3(10, 0)), 0);
        Near(Math.Abs(curve.At(0.5).Y), 5);
        var matrix = Affine3.Move(new Vec3(100, -20)) * Affine3.RotateZ(0.3) * Affine3.Scale(new Vec3(2, -3, 1));
        var transformed = curve.Transform(matrix);
        for (var index = 0; index <= 10; index++)
            Near(Vec3.Distance(transformed.At(index / 10.0), matrix.Point(curve.At(index / 10.0))), 0, 1e-6);
    }
    private static void NurbsGeometry()
    {
        var curve = new NurbsCurve([new Vec3(1, 0), new Vec3(1, 1), new Vec3(0, 1)], [1, Math.Sqrt(0.5), 1], [0, 0, 0, 1, 1, 1], 2);
        Near(curve.At(0.5).X, Math.Sqrt(0.5));
        Near(curve.At(0.5).Y, Math.Sqrt(0.5));
        Near(Vec3.Distance(curve.End, new Vec3(0, 1)), 0);
        Require(CurveSampler.Sample(curve, new SamplingOptions(0.001)).Length > 10);
        Reject(() => new NurbsCurve([Vec3.Zero, Vec3.UnitX], [1, -1], [0, 0, 1, 1], 1));
    }
    private static void BlockInstances()
    {
        var document = new DxfDocument();
        var block = new Block("shared");
        block.Entities.Add(new Line(new Vector2(0, 0), new Vector2(10, 0)));
        var first = new Insert(block, new Vector2(20, 30));
        var second = new Insert(block, new Vector2(-20, -30)) { Rotation = 90 };
        document.Entities.Add(first);
        document.Entities.Add(second);
        var session = new ShadowSession(document);
        var occurrences = new CadSceneBuilder().Build(session.Current).Occurrences;
        Require(occurrences.Length == 2);
        Require(occurrences[0].Entity == occurrences[1].Entity);
        Require(occurrences[0].Key != occurrences[1].Key);
        var firstCurve = occurrences.Single(item => item.InsertPath.Contains(Id(first))).Geometry.Contours[0].Curves[0];
        Near(firstCurve.Start.X, 20);
        Near(firstCurve.End.X, 30);
        var secondCurve = occurrences.Single(item => item.InsertPath.Contains(Id(second))).Geometry.Contours[0].Curves[0];
        Near(secondCurve.End.X, -20);
        Near(secondCurve.End.Y, -20);
    }
    private static void SpatialQueries()
    {
        var random = new Random(1776);
        var boxes = Enumerable.Range(0, 500).Select(_ =>
        {
            var x = random.NextDouble() * 100;
            var y = random.NextDouble() * 100;
            return new Bounds2(x, y, x + random.NextDouble() * 5, y + random.NextDouble() * 5);
        }).ToArray();
        var index = new SpatialIndex(boxes);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var x = random.NextDouble() * 100;
            var y = random.NextDouble() * 100;
            var query = new Bounds2(x, y, x + 8, y + 8);
            Require(index.Query(query).Order().SequenceEqual(Enumerable.Range(0, boxes.Length).Where(n => boxes[n].Intersects(query))));
        }
    }
    private static void GraphCrossing()
    {
        var graph = new TopologyBuilder().Build(Segments((new Vec3(-10, 0), new Vec3(10, 0)), (new Vec3(0, -10), new Vec3(0, 10))));
        Require(graph.Nodes.Length == 5, "A crossing must create one junction.");
        Require(graph.Edges.Length == 4);
        Require(graph.Junctions.Single().Edges.Length == 4);
        Require(graph.Bridges().Length == 4);
        Require(graph.CycleRank == 0);
        var left = graph.Nodes.Single(n => n.Position.X == -10).Id;
        var top = graph.Nodes.Single(n => n.Position.Y == 10).Id;
        Near(graph.ShortestPath(left, top)!.Length, 20);
    }
    private static void GraphElevation()
    {
        var graph = new TopologyBuilder().Build(Segments((new Vec3(-10, 0), new Vec3(10, 0)), (new Vec3(0, -10, 2), new Vec3(0, 10, 2))));
        Require(graph.ConnectedComponents().Length == 2);
        Require(graph.Edges.Length == 2);
    }
    private static void GraphCycles()
    {
        var graph = new TopologyBuilder().Build(Segments((Vec3.Zero, new Vec3(10, 0)), (new Vec3(10, 0), new Vec3(10, 10)), (new Vec3(10, 10), new Vec3(0, 10)), (new Vec3(0, 10), Vec3.Zero)));
        Require(graph.CycleRank == 1);
        Require(graph.Bridges().IsEmpty);
        var parallel = new TopologyBuilder().Build(Segments((Vec3.Zero, new Vec3(10, 0)), (Vec3.Zero, new Vec3(10, 0))));
        Require(parallel.CycleRank == 1);
        Require(parallel.Bridges().IsEmpty);
    }
    private static void EditorHistory()
    {
        var fixture = Fixture();
        var editor = new EditorSession(fixture.Session);
        var original = fixture.Session.Current;
        editor.SetProperty(Id(fixture.Line), "StartPoint", ShadowValue.From(new Vec3(3, 4)));
        Near(fixture.Line.StartPoint.X, 3);
        editor.Undo();
        Near(fixture.Line.StartPoint.X, 0);
        editor.Redo();
        Near(fixture.Line.StartPoint.X, 3);
        Near(original.Read(Id(fixture.Line))["StartPoint"]["X"].Number(), 0);
        fixture.Session.MutateSource(_ => fixture.Line.StartPoint = new Vector3(8, 0, 0));
        Reject(() => editor.Undo());
        editor.ResetHistory();
        editor.SetProperty(Id(fixture.Line), "StartPoint", ShadowValue.From(new Vec3(9, 0)));
        Near(fixture.Line.StartPoint.X, 9);
    }
    private static void EditorBranch()
    {
        var fixture = Fixture();
        var memory = new ShadowMemoryStore(fixture.Session.Current);
        var editor = new EditorSession(memory);
        editor.SetProperty(Id(fixture.Line), "StartPoint", ShadowValue.From(new Vec3(5, 0)));
        Near(fixture.Line.StartPoint.X, 0);
        var rendering = new RenderCompiler().Compile(fixture.Session.Current);
        Require(RenderCompiler.HitTest(rendering, new Vec3(5, 0), 0.1)?.Entity == Id(fixture.Line));
        var snap = Snapping.Find(rendering, new Vec3(0.04, 0.03), 0.1, SnapMode.Vertex);
        Require(snap is not null);
        Near(snap!.Position.X, 0);
    }
    private static void RenderBackends()
    {
        var fixture = Fixture();
        fixture.Session.MutateSource(document => document.Entities.Add(new Text("<script>& Unicode Ω", new Vector2(0, 10), 3)));
        var compiler = new RenderCompiler();
        var rendering = compiler.Compile(fixture.Session.Current);
        Require(ReferenceEquals(rendering, compiler.Compile(fixture.Session.Current)));
        using var host = new RenderHost(new Dictionary<string, Func<IRenderBackend>>
        {
            ["skia"] = () => new SkiaRenderBackend(),
            ["svg"] = () => new SvgRenderBackend()
        }, "skia");
        var frame = new RenderFrame(rendering, Viewport.Fit(rendering.Bounds, 640, 360));
        var png = host.Render(frame);
        Require(png.ContentType == "image/png");
        Require(png.Bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        host.Swap("svg");
        var svg = host.Render(frame);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
        Require(!xml.Descendants().Any(element => element.Name.LocalName == "script"));
        Require(xml.Descendants().Any(element => element.Name.LocalName == "text"));
        Directory.CreateDirectory("artifacts");
        File.WriteAllBytes("artifacts/sample.png", png.Bytes);
        File.WriteAllBytes("artifacts/sample.svg", svg.Bytes);
    }
    private static void IntegratedStudio()
    {
        using var studio = new StudioHost();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var state = JsonSerializer.SerializeToElement(studio.State(), options);
        var revision = state.GetProperty("revision").GetInt64();
        Require(state.GetProperty("occurrences").GetInt32() > 10);
        _ = JsonSerializer.Serialize(studio.Graph(CancellationToken.None));
        var image = studio.Render(800, 500, null, null, null, "skia", null, CancellationToken.None);
        Require(image.Bytes.Length > 1000);
        studio.Retain(revision);
        studio.SourceDemo(revision);
        var after = JsonSerializer.SerializeToElement(studio.State(), options);
        Require(after.GetProperty("revision").GetInt64() > revision);
        var saved = studio.Save();
        Require(saved.Length > 1000);
        studio.Load(new MemoryStream(saved));
        _ = JsonSerializer.Serialize(studio.State());
    }
    private static void PerformanceSmoke()
    {
        var document = new DxfDocument();
        for (var index = 0; index < 10000; index++)
            document.Entities.Add(new Line(new Vector2(index % 100, index / 100), new Vector2(index % 100 + 0.75, index / 100 + 0.75)));
        var timer = Stopwatch.StartNew();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var session = new ShadowSession(document);
        var captureMs = timer.Elapsed.TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        var snapshot = session.Current;
        timer.Restart();
        for (var index = 0; index < 1000000; index++) Require(ReferenceEquals(snapshot, session.Current));
        var readsMs = timer.Elapsed.TotalMilliseconds;
        timer.Restart();
        Require(ReferenceEquals(snapshot, session.Refresh()));
        var refreshMs = timer.Elapsed.TotalMilliseconds;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            entities = 10000, objects = snapshot.Objects.Count, captureMs, allocatedBytes = bytes,
            millionSnapshotReadsMs = readsMs, noOpRefreshMs = refreshMs
        }));
    }
}
