using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using DxfViewport = netDxf.Entities.Viewport;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class LinetypeScaleVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-scale/" + name, run));
        foreach (var paperScale in new short[] { 0, 1 })
        foreach (var magnification in new[] { 0.25, 0.5, 1.0, 2.0, 4.0 })
            Test($"shared stroke and text scale/{paperScale}/{magnification}", () =>
            {
                var (session, line, views) = Drawing(paperScale, magnification);
                var retained = session.Current;
                var scene = new RenderCompiler().Compile(retained, new("Sheet"));
                Check(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
                var group = Items(scene, views[1], line);
                var expected = paperScale == 1 ? 1 : magnification;
                var symbols = group.Where(i => i.IsPresentationOnly).ToArray();
                Check(symbols.Length >= 2);
                foreach (var symbol in symbols) Near(symbol.Text!.XAxis.Length, expected);
                Near(symbols[1].Text!.Origin.X - symbols[0].Text!.Origin.X, 4 * expected);
                var dash = group.Single(i => !i.IsPresentationOnly).Contours[0];
                Near(Vec3.Distance(dash.Points[0], dash.Points[^1]), 2 * expected);
                Check(ReferenceEquals(retained, session.Current));
                Check(new TopologyBuilder().Build(retained).Edges.Length == 1);
                Check(new TopologyBuilder().Build(retained, "Sheet").Edges.IsEmpty);
            });
        foreach (var factor in new[] { 0.5, 1.0, 2.0 })
            Test("consumer model scale does not mutate source/" + factor, () =>
            {
                var (session, line, _) = Drawing(1, 0.5);
                var compiler = new RenderCompiler(); var snapshot = session.Current;
                var original = compiler.Compile(snapshot);
                var adjusted = compiler.Compile(snapshot, new() { LinetypeScale = factor });
                Near(adjusted.Items[0].Style.Dashes[0], 2 * factor);
                Near(adjusted.Items.First(i => i.IsPresentationOnly).Text!.XAxis.Length, factor);
                Near(line.LinetypeScale, 1); Check(ReferenceEquals(snapshot, session.Current));
                Check(ReferenceEquals(original, compiler.Compile(snapshot)));
                Near(new CadSceneBuilder().Build(snapshot, new() { LinetypeScale = factor }).Occurrences[0].Style.Linetype!.Scale, factor);
            });
        foreach (var factor in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            Test("reject invalid consumer multiplier/" + factor, () =>
            {
                var (session, _, _) = Drawing(1, 0.5);
                Throws<ArgumentOutOfRangeException>(() => new RenderCompiler().Compile(session.Current, new() { LinetypeScale = factor }));
                Throws<ArgumentOutOfRangeException>(() => new CadSceneBuilder().Build(session.Current, new() { LinetypeScale = factor }));
            });
        Test("entity global style and consumer scales compose once", () =>
        {
            var (session, line, views) = Drawing(1, 0.5);
            session.MutateSource(d => { d.DrawingVariables.LtScale = 3; line.LinetypeScale = 0.5; });
            var scene = new RenderCompiler().Compile(session.Current, new("Sheet") { LinetypeScale = 2 });
            foreach (var view in views)
            {
                var group = Items(scene, view, line);
                var text = group.First(i => i.IsPresentationOnly).Text!;
                Near(text.XAxis.Length, 3);
                var dash = group.Single(i => !i.IsPresentationOnly).Contours[0];
                Near(Vec3.Distance(dash.Points[0], dash.Points[^1]), 6);
            }
        });
        Test("PSLTSCALE edits invalidate live output but not retained views", () =>
        {
            var (session, line, views) = Drawing(0, 0.5); var compiler = new RenderCompiler();
            var retained = session.Current; var before = compiler.Compile(retained, new("Sheet"));
            session.MutateSource(d => d.DrawingVariables.PsLtScale = 1);
            var after = compiler.Compile(session.Current, new("Sheet"));
            Near(Items(before, views[1], line).First(i => i.IsPresentationOnly).Text!.XAxis.Length, 0.5);
            Near(Items(after, views[1], line).First(i => i.IsPresentationOnly).Text!.XAxis.Length, 1);
            Check(ReferenceEquals(before, compiler.Compile(retained, new("Sheet"))));
        });
        Test("changing magnification preserves paper-unit symbol dimensions", () =>
        {
            var (session, line, views) = Drawing(1, 0.5); var compiler = new RenderCompiler();
            var before = compiler.Compile(session.Current, new("Sheet"));
            session.MutateSource(_ => views[1].ViewHeight = 400);
            var after = compiler.Compile(session.Current, new("Sheet"));
            Near(Items(before, views[1], line).First(i => i.IsPresentationOnly).Text!.XAxis.Length, 1);
            Near(Items(after, views[1], line).First(i => i.IsPresentationOnly).Text!.XAxis.Length, 1);
            Check(Items(before, views[1], line).Count != Items(after, views[1], line).Count);
        });
        Test("equal viewport scales share one model expansion", () =>
        {
            var (session, _, views) = Drawing(1, 0.5);
            session.MutateSource(d => d.Layouts["Sheet"].AssociatedBlock.Entities.Add(new DxfViewport
            { Center = new(1000, 0, 0), Width = 400, Height = 100, ViewHeight = views[1].ViewHeight, ViewTarget = new(40, 0, 0) }));
            var provider = new MarkerProvider();
            new RenderCompiler(linetypeResources: provider).Compile(session.Current, new("Sheet"));
            Check(provider.Calls == 2, $"Expected two distinct-scale expansions, got {provider.Calls}.");
        });
        Test("distinct-scale budget is checked before provider execution", () =>
        {
            var (session, _, _) = Drawing(1, 0.5); var provider = new MarkerProvider();
            var compiler = new RenderCompiler(linetypeResources: provider);
            var options = new RenderCompileOptions("Sheet") { PaperSpace = new() { MaximumPatternScales = 1 } };
            Throws<InvalidOperationException>(() => compiler.Compile(session.Current, options));
            Check(provider.Calls == 0);
            session.MutateSource(d => d.DrawingVariables.PsLtScale = 0);
            compiler.Compile(session.Current, options); Check(provider.Calls == 1);
        });
        Test("invalid distinct-scale budget and original binary signatures", () =>
        {
            var (session, _, _) = Drawing(1, 0.5);
            Throws<ArgumentOutOfRangeException>(() => new RenderCompiler().Compile(session.Current,
                new("Sheet") { PaperSpace = new() { MaximumPatternScales = 0 } }));
            Check(typeof(PaperSpaceOptions).GetConstructor([typeof(int), typeof(int), typeof(int), typeof(int)]) is not null);
            Check(typeof(SceneOptions).GetConstructor([typeof(string), typeof(int), typeof(int), typeof(bool)]) is not null);
        });
        Test("clipping cannot restart the compensated pattern phase", () =>
        {
            var (session, line, views) = Drawing(1, 0.5);
            session.MutateSource(_ => views[1].Width = 10);
            var scene = new RenderCompiler().Compile(session.Current, new("Sheet"));
            var symbols = Items(scene, views[1], line).Where(i => i.IsPresentationOnly).ToArray();
            Check(symbols.Length >= 2);
            foreach (var symbol in symbols)
            {
                var model = symbol.Projection!.PaperToModel.Point(symbol.Text!.Origin);
                var periodIndex = (model.X - 6) / 8;
                Near(periodIndex, Math.Round(periodIndex));
            }
        });
        Test("projected markers stay source-editable and excluded from default snapping", () =>
        {
            var (session, line, views) = Drawing(1, 0.5);
            var compiler = new RenderCompiler(linetypeResources: new MarkerProvider());
            var scene = compiler.Compile(session.Current, new("Sheet"));
            var symbol = Items(scene, views[1], line).First(i => i.IsPresentationOnly);
            var endpoint = symbol.Contours.Single().Points[^1];
            Check(RenderCompiler.HitTest(scene, endpoint, 0.001)?.Entity == Id(line));
            Check(Snapping.Find(scene, endpoint, 0.001, SnapMode.Vertex) is null);
            Check(Snapping.Find(scene, endpoint, 0.001, true, SnapMode.Vertex)?.Occurrence == symbol.Key);
            var editor = new EditorSession(session); editor.Select([symbol.Key]); editor.Translate(scene, new(1, 0));
            Near(line.StartPoint.X, 2); editor.Undo(); Near(line.StartPoint.X, 0);
        });
        Test("SHX resources and font revisions use the same compensated placement", () =>
        {
            var (session, line, views) = Drawing(1, 0.5);
            session.MutateSource(_ => line.Linetype = new Linetype("SYMBOLS", [new LinetypeSimpleSegment(2),
                new LinetypeShapeSegment("M", new ShapeStyle("MARKS", "marks.shx"), -1) { ShapeNumber = 17 }, new LinetypeSimpleSegment(-1)]));
            var catalog = new ShxFontCatalog(); catalog.Register("marks.shx", ShxFontReader.ReadAscii("*17,4,M\n8,0,2,0"));
            var compiler = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog)]));
            var before = compiler.Compile(session.Current, new("Sheet"));
            foreach (var view in views) Near(Items(before, view, line).First(i => i.IsPresentationOnly).Bounds.Height, 2);
            catalog.Register("marks.shx", ShxFontReader.ReadAscii("*17,4,M\n8,0,4,0"));
            var after = compiler.Compile(session.Current, new("Sheet"));
            foreach (var view in views) Near(Items(after, view, line).First(i => i.IsPresentationOnly).Bounds.Height, 4);
            Near(Items(before, views[1], line).First(i => i.IsPresentationOnly).Bounds.Height, 2);
        });
        Test("restart boundaries survive viewport compensation", () =>
        {
            var (session, original, views) = Drawing(1, 0.5);
            var polyline = new Polyline2D(new Polyline2DVertex[] { new(0, 0), new(10, 0), new(10, 10) }) { Linetype = original.Linetype };
            session.MutateSource(d => { d.Entities.Remove(original); d.Entities.Add(polyline); });
            var scene = new RenderCompiler().Compile(session.Current, new("Sheet"));
            var symbols = Items(scene, views[1], polyline).Where(i => i.IsPresentationOnly).ToArray();
            Check(symbols.Length == 2);
            var point = symbols[1].Projection!.PaperToModel.Point(symbols[1].Text!.Origin);
            Near(point.X, 10); Near(point.Y, 6);
        });
        Test("SVG and native pixels share compensated symbol dimensions", () =>
        {
            var (session, line, views) = Drawing(1, 0.5);
            var scene = new RenderCompiler(linetypeResources: new MarkerProvider()).Compile(session.Current, new("Sheet"));
            var frame = new RenderFrame(scene, new Camera(new(250, 0), 1, 1100, 240));
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend();
            var vector = svg.Render(frame); var raster = skia.Render(frame);
            Check(XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes)).Root is not null);
            using var image = global::SkiaSharp.SKBitmap.Decode(raster.Bytes);
            foreach (var view in views)
            {
                var symbol = Items(scene, view, line).First(i => i.IsPresentationOnly);
                var position = frame.Viewport.ToScreen(symbol.Bounds.Center);
                Check(image.GetPixel((int)position.X, (int)position.Y) != new global::SkiaSharp.SKColor(frame.Background));
            }
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/linetype-viewport-scales.svg", vector.Bytes);
            File.WriteAllBytes("artifacts/linetype-viewport-scales.png", raster.Bytes);
        });
    }

    private static (ShadowSession, Line, DxfViewport[]) Drawing(short paperScale, double magnification)
    {
        var document = new DxfDocument(); document.DrawingVariables.PsLtScale = paperScale;
        var type = new Linetype("LABELS", [new LinetypeSimpleSegment(2),
            new LinetypeTextSegment("A", new TextStyle("TEXT", "Arial.ttf"), -1, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1),
            new LinetypeSimpleSegment(-1)]);
        var line = new Line(Vector2.Zero, new Vector2(80, 0)) { Linetype = type }; document.Entities.Add(line);
        var sheet = new Layout("Sheet"); document.Layouts.Add(sheet);
        DxfViewport View(double x, double scale) => new() { Center = new(x, 0, 0), Width = 400, Height = 100,
            ViewHeight = 100 / scale, ViewTarget = new(40, 0, 0) };
        var views = new[] { View(0, 1), View(500, magnification) };
        foreach (var view in views) sheet.AssociatedBlock.Entities.Add(view);
        return (new ShadowSession(document), line, views);
    }
    private static List<RenderItem> Items(RenderScene scene, DxfViewport view, EntityObject source) => scene.Items
        .Where(i => i.Entity == Id(source) && i.Projection?.ViewportEntity == Id(view)).ToList();
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private sealed class MarkerProvider : ILinetypeDecorationProvider
    {
        public long Revision => 0;
        public int Calls { get; private set; }
        public bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic)
        { Calls++; geometry = new([new([new LineCurve(Vec3.Zero, new(0, 4))])]); diagnostic = null; return true; }
    }
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message = "Linetype scale assertion failed.")
    { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
