using System.Collections.Immutable;
using System.Diagnostics;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class IncrementalVerification
{
    private static void Require(bool condition, string message = "Assertion failed") { if (!condition) throw new InvalidOperationException(message); }
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("incremental/geometry and render item reuse, style-only edits", () =>
        {
            var doc = new DxfDocument(); var layer = new Layer("Pipes") { Color = AciColor.Red };
            var line = new Line(Vector2.Zero, new Vector2(10, 0)); var circle = new Circle(new Vector2(30, 0), 5) { Layer = layer };
            doc.Entities.Add(line); doc.Entities.Add(circle);
            var source = new ShadowSession(doc); var builder = new CadSceneBuilder(); var compiler = new RenderCompiler(builder); var editor = new EditorSession(source);
            var first = compiler.Compile(source.Current); var circleItem = first.Items.Single(i => i.Entity == Id(circle));
            var initialStats = compiler.CacheStatistics;
            editor.SetProperty(Id(line), "EndPoint", ShadowValue.From(new Vec3(12, 0)));
            var second = compiler.Compile(source.Current);
            Require(ReferenceEquals(circleItem, second.Items.Single(i => i.Entity == Id(circle))));
            Require(compiler.CacheStatistics.GeometryCompilations == initialStats.GeometryCompilations + 1);
            Require(second.Index.RefitCount == 1);
            source.MutateSource(_ => layer.Color = AciColor.Blue);
            var third = compiler.Compile(source.Current); var changed = third.Items.Single(i => i.Entity == Id(circle));
            Require(changed.Style.Argb != circleItem.Style.Argb);
            Require(changed.Contours == circleItem.Contours, "Style changes must reuse tessellation");
            Require(ReferenceEquals(third.Index, second.Index), "Style changes must reuse BVH");
            Require(first.Items.Single(i => i.Entity == Id(line)).Contours[0].Points[^1].X == 10);
        }));
        tests.Add(("incremental/nested vertex dependencies invalidate every block occurrence", () =>
        {
            var doc = new DxfDocument(); var block = new Block("Shared");
            var polyline = new Polyline2D([Vector2.Zero, new Vector2(10, 0)]); block.Entities.Add(polyline);
            doc.Entities.Add(new Insert(block, new Vector2(0, 0))); doc.Entities.Add(new Insert(block, new Vector2(100, 0)));
            var source = new ShadowSession(doc); var builder = new CadSceneBuilder(); var compiler = new RenderCompiler(builder);
            var first = compiler.Compile(source.Current);
            var vertex = source.Current.Read(Id(polyline))["Vertexes"].Items.Last().Id!.Value;
            new EditorSession(source).SetProperty(vertex, "Position", ShadowValue.From(new Vec3(20, 0), true));
            var second = compiler.Compile(source.Current);
            Require(second.Items.Length == 2);
            foreach (var item in second.Items)
            {
                var old = first.Items.Single(i => i.Key == item.Key);
                Require(!ReferenceEquals(old, item));
                Require(item.Contours[0].Points[^1].X == old.Contours[0].Points[^1].X + 10);
            }
            Require(builder.CacheStatistics.Projections == 2, "Shared definition should project only once per content revision");
        }));
        tests.Add(("incremental/custom projector revision and untracked extension opt-out", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
            var source = new ShadowSession(doc); var projector = new ConfigurableProjector(); var compiler = new RenderCompiler(new([projector]));
            var first = compiler.Compile(source.Current); projector.Revision++;
            var second = compiler.Compile(source.Current);
            Require(!ReferenceEquals(first, second)); Require(second.Items[0].Contours[0].Points[^1].X == 2);
            var untracked = new UntrackedProjector(); compiler = new(new([untracked]));
            _ = compiler.Compile(source.Current); _ = compiler.Compile(source.Current); Require(untracked.Calls == 2);
        }));
        tests.Add(("incremental/persistent BVH refit agrees with brute force and retains old index", () =>
        {
            var random = new Random(47); var boxes = Enumerable.Range(0, 300).Select(i => new Bounds2(i, i % 10, i + 1, i % 10 + 1)).ToArray();
            var index = new SpatialIndex(boxes); var original = index; var originalBoxes = boxes.ToArray();
            for (var iteration = 0; iteration < 140; iteration++)
            {
                var id = random.Next(boxes.Length); var x = random.NextDouble() * 300; var y = random.NextDouble() * 10;
                boxes[id] = new(x, y, x + 3, y + 2); index = index.Refit([new(id, boxes[id])]);
                for (var j = 0; j < 10; j++)
                {
                    x = random.NextDouble() * 300; var query = new Bounds2(x, 0, x + 12, 20);
                    Require(index.Query(query).Order().SequenceEqual(Enumerable.Range(0, boxes.Length).Where(i => boxes[i].Intersects(query))));
                    Require(original.Query(query).Order().SequenceEqual(Enumerable.Range(0, boxes.Length).Where(i => originalBoxes[i].Intersects(query))));
                }
            }
            boxes[5] = Bounds2.Empty; index = index.Refit([new(5, Bounds2.Empty)]);
            Require(!index.Query(new(-100, -100, 1000, 1000)).Contains(5));
            Require(ReferenceEquals(index, index.Refit([])));
        }));
        tests.Add(("incremental/10k drawing single-edit work and dirty capture allocation", () =>
        {
            var doc = new DxfDocument();
            for (var i = 0; i < 10000; i++) doc.Entities.Add(new Line(new Vector2(i, 0), new Vector2(i + 0.5, 0)));
            var source = new ShadowSession(doc); var builder = new CadSceneBuilder(); var compiler = new RenderCompiler(builder);
            var first = compiler.Compile(source.Current); var id = first.Items[1234].Entity; var editor = new EditorSession(source);
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var timer = Stopwatch.StartNew();
            editor.SetProperty(id, "EndPoint", ShadowValue.From(new Vec3(1234.7, 0)));
            var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated; var editMs = timer.Elapsed.TotalMilliseconds;
            var before = compiler.CacheStatistics; timer.Restart(); var second = compiler.Compile(source.Current); var renderMs = timer.Elapsed.TotalMilliseconds;
            Require(bytes < 500_000, "A scalar edit must not copy the full source registry");
            Require(compiler.CacheStatistics.GeometryCompilations == before.GeometryCompilations + 1);
            Require(first.Items.Zip(second.Items).Count(p => ReferenceEquals(p.First, p.Second)) == 9999);
            Require(builder.LastBuildStatistics == new SceneBuildStatistics(1, 9999));
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { entities = 10000, scalarEditBytes = bytes, scalarEditMs = editMs, incrementalCompileMs = renderMs, geometryRebuilds = 1, renderItemsReused = 9999 }));
        }));
    }
    private sealed class ConfigurableProjector : ICacheableEntityProjector
    {
        public long Revision { get; set; }
        public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
        { geometry = new([new([new LineCurve(Vec3.Zero, new Vec3(Revision + 1, 0))])]); diagnostic = null; return true; }
    }
    private sealed class UntrackedProjector : IEntityProjector
    {
        public int Calls;
        public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
        { Calls++; geometry = CadGeometry.Empty; diagnostic = null; return true; }
    }
}
