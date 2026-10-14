using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class LinetypeRenderingVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-render/" + name, run));
        Test("text metadata produces separate source-identified render items", () =>
        {
            var (session, _, _, _) = TextDocument();
            var semantic = new CadSceneBuilder().Build(session.Current);
            var occurrence = semantic.Occurrences.Single();
            Check(occurrence.Style.Linetype?.Elements[1].Decoration?.Content == "A");
            var render = new RenderCompiler().Compile(session.Current);
            Check(render.Items.Length == 3 && render.Items.Count(i => i.Text is not null) == 2);
            Check(render.Items.All(i => i.Entity == occurrence.Entity));
            Check(render.Items.Select(i => i.Key).Distinct().Count() == 3);
            Check(render.Items.Where(i => i.Text is not null).All(i => i.Style.Dashes.IsEmpty && i.Style.Linetype is null));
            var graph = new TopologyBuilder().Build(session.Current);
            Check(graph.Nodes.Length == 2 && graph.Edges.Length == 1, "Linetype text became graph geometry.");
            Check(semantic.Occurrences.Length == 1 && semantic.Occurrences[0].Geometry.Text is null);
        });
        Test("text-only edits invalidate occurrences with unchanged dash lengths", () =>
        {
            var (session, segment, _, _) = TextDocument();
            var compiler = new RenderCompiler(); var retained = session.Current;
            var old = compiler.Compile(retained);
            session.MutateSource(_ => segment.Text = "B");
            var next = compiler.Compile(session.Current);
            Check(old.Items.Where(i => i.Text is not null).All(i => i.Text!.Content == "A"));
            Check(next.Items.Where(i => i.Text is not null).All(i => i.Text!.Content == "B"));
            Check(!ReferenceEquals(old.Items[0], next.Items[0]));
            Check(ReferenceEquals(compiler.Compile(retained), old));
        });
        Test("referenced style changes invalidate placement and glyph axes", () =>
        {
            var (session, segment, _, _) = TextDocument();
            var compiler = new RenderCompiler(); var old = compiler.Compile(session.Current);
            session.MutateSource(_ => { segment.Style.Height = 2; segment.Style.WidthFactor = 3; });
            var next = compiler.Compile(session.Current);
            Near(next.Items.First(i => i.Text is not null).Text!.XAxis.Length,
                old.Items.First(i => i.Text is not null).Text!.XAxis.Length * 6);
        });
        Test("unaffected plain occurrences retain render cache identity", () =>
        {
            var (session, segment, _, _) = TextDocument();
            var plain = new Line(new Vector3(0, 10, 0), new Vector3(10, 10, 0));
            session.MutateSource(d => d.Entities.Add(plain));
            var compiler = new RenderCompiler(); var old = compiler.Compile(session.Current);
            session.MutateSource(_ => segment.Text = "B");
            var next = compiler.Compile(session.Current);
            var id = Id(plain);
            Check(ReferenceEquals(old.Items.Single(i => i.Entity == id), next.Items.Single(i => i.Entity == id)));
        });
        Test("ByLayer resolves detached complex metadata and global scale", () =>
        {
            var (session, _, line, type) = TextDocument();
            session.MutateSource(d => { line.Layer = new Layer("PIPE") { Linetype = type }; line.Linetype = Linetype.ByLayer; line.LinetypeScale = 3; d.DrawingVariables.LtScale = 2; });
            var occurrence = new CadSceneBuilder().Build(session.Current).Occurrences.Single();
            Near(occurrence.Style.Linetype!.Scale, 6);
            Check(occurrence.Style.Dashes.SequenceEqual(new[] { 12.0, -6.0, -6.0 }));
        });
        Test("ByBlock inherits pattern with child entity scaling", () =>
        {
            var (_, segment, _, type) = TextDocument();
            var child = new Line(Vector3.Zero, new Vector3(100, 0, 0)) { Linetype = Linetype.ByBlock, LinetypeScale = 3 };
            var block = new Block("LT_BLOCK"); block.Entities.Add(child);
            var insert = new Insert(block, new Vector3(10, 20, 0)) { Linetype = (Linetype)type.Clone(), LinetypeScale = 2 };
            var document = new DxfDocument(); document.Entities.Add(insert);
            var session = new ShadowSession(document);
            var occurrence = new CadSceneBuilder().Build(session.Current).Occurrences.Single();
            Near(occurrence.Style.Linetype!.Scale, 6); Check(occurrence.InsertPath.Length == 1);
            var render = new RenderCompiler().Compile(session.Current);
            Check(render.Items.Any(i => i.Text is not null));
            Check(render.Items.All(i => i.InsertPath.Length == 1 && i.Entity == occurrence.Entity));
        });
        Test("missing SHX leaves signed stroke spacing and one resource diagnostic", () =>
        {
            var session = ShapeDocument(); var render = new RenderCompiler().Compile(session.Current);
            Check(render.Items.Length == 1);
            Check(render.Diagnostics.Count(d => d.Code == "linetype.resource") == 1);
            var contours = StrokePattern.GetContours(render.Items[0]);
            Check(contours.Length == 3); Near(contours[0].Points[^1].X, 2);
            Check(new TopologyBuilder().Build(session.Current).Edges.Length == 1);
        });
        Test("SHX numeric identifier overrides an unavailable display name", () =>
        {
            var catalog = Catalog(); var projector = new ShxEntityProjector(catalog);
            var compiler = new RenderCompiler(new CadSceneBuilder([projector]));
            var session = ShapeDocument(); var render = compiler.Compile(session.Current);
            Check(render.Items.Length == 3 && !render.Diagnostics.Any(d => d.Code == "linetype.resource"));
            Check(new TopologyBuilder().Build(session.Current).Edges.Length == 1);
            var hit = RenderCompiler.HitTest(render, new(3, 1), 0.01);
            Check(hit is not null && hit.Entity == render.Items[0].Entity && hit.Key.Contains("/linetype[", StringComparison.Ordinal));
        });
        Test("explicit shape name lookup is supported without a numeric identifier", () =>
        {
            var provider = new ShxEntityProjector(Catalog());
            Check(provider.TryProject(new CadLinetypeDecoration(CadLinetypeDecorationKind.Shape, "MARK", "marks.shx"), out var geometry, out _));
            Check(geometry.Contours.Length == 1); Near(geometry.Contours[0].Curves[0].End.Y, 2);
        });
        Test("signed DXF shape words resolve as unsigned SHX indices", () =>
        {
            var catalog = new ShxFontCatalog();
            catalog.Register("high.shx", ShxFontReader.ReadAscii("*65535,4,HIGH\n8,1,0,0"));
            var provider = new ShxEntityProjector(catalog);
            Check(provider.TryProject(new CadLinetypeDecoration(CadLinetypeDecorationKind.Shape, "missing", "high.shx", -1), out var geometry, out _));
            Near(geometry.Contours[0].Curves[0].End.X, 1);
        });
        Test("live and pinned SHX resources have independent retained output", () =>
        {
            var catalog = Catalog(); var session = ShapeDocument();
            var live = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog)]));
            var pinned = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog.Current)]));
            var before = live.Compile(session.Current); var frozen = pinned.Compile(session.Current);
            catalog.Register("marks.shx", ShxFontReader.ReadAscii("*17,4,MARK\n8,0,4,0"));
            var after = live.Compile(session.Current);
            Near(before.Bounds.MaxY, 2); Near(after.Bounds.MaxY, 4);
            Check(ReferenceEquals(frozen, pinned.Compile(session.Current)));
            Near(frozen.Bounds.MaxY, 2);
        });
        Test("SHX text uses registered glyph strokes rather than system substitution", () =>
        {
            var (session, segment, _, _) = TextDocument();
            session.MutateSource(_ => segment.Style = new TextStyle("SHX_LT", "letters.shx"));
            var catalog = new ShxFontCatalog();
            catalog.Register("letters.shx", ShxFontReader.ReadAscii("*0,4,Letters\n10,2,0,0\n*65,4,A\n8,5,10,0"));
            var render = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog)])).Compile(session.Current);
            Check(render.Items.Length == 3 && render.Items.All(i => i.Text is null));
            Check(render.Items.Skip(1).All(i => !i.Strokes.IsEmpty || !i.Contours.IsEmpty));
            Check(!render.Diagnostics.Any(d => d.Code == "linetype.resource"));
        });
        Test("provider revisions invalidate snapshot rendering without source edits", () =>
        {
            var (session, _, _, _) = TextDocument(); var provider = new TestProvider();
            var compiler = new RenderCompiler(linetypeResources: provider);
            var before = compiler.Compile(session.Current); provider.Revision = 1;
            var after = compiler.Compile(session.Current);
            Near(before.Bounds.MaxY, 1); Near(after.Bounds.MaxY, 2);
            Check(!ReferenceEquals(before, after));
        });
        Test("provider mutation during compilation rejects publication", () =>
        {
            var (session, _, _, _) = TextDocument(); var provider = new TestProvider { ChangeDuringProjection = true };
            Throws<InvalidOperationException>(() => new RenderCompiler(linetypeResources: provider).Compile(session.Current));
        });
        Test("placement limit retains base geometry with an explicit diagnostic", () =>
        {
            var (session, _, _, _) = TextDocument();
            var render = new RenderCompiler(linetypeOptions: new(MaximumPlacements: 1)).Compile(session.Current);
            Check(render.Items.Length == 1 && render.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("scene placement budget rejects complete decoration groups", () =>
        {
            var (session, _, line, type) = TextDocument();
            session.MutateSource(d => d.Entities.Add(new Line(new Vector3(0, 10, 0), new Vector3(10, 10, 0)) { Linetype = type }));
            var render = new RenderCompiler(linetypeOptions: new(MaximumScenePlacements: 3)).Compile(session.Current);
            Check(render.Items.Length == 4 && render.Items.Count(i => i.Text is not null) == 2);
            Check(render.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("static scene compilation preserves original semantic occurrence count", () =>
        {
            var (session, _, _, _) = TextDocument(); var semantic = new CadSceneBuilder().Build(session.Current);
            var render = RenderCompiler.Compile(semantic);
            Check(render.Items.Length == 3 && semantic.Occurrences.Length == 1);
        });
        Test("SVG and Skia draw the same SHX placement plan", () =>
        {
            var session = ShapeDocument();
            var render = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())])).Compile(session.Current);
            var frame = new RenderFrame(render, new Camera(new(5, 1), 30, 360, 180));
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend();
            var vector = svg.Render(frame); var raster = skia.Render(frame);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes));
            Check(xml.Root is not null && xml.Descendants().Any(e => e.Name.LocalName == "path"));
            using var bitmap = SKBitmap.Decode(raster.Bytes);
            Check(bitmap is not null && bitmap.Width == 360 && bitmap.Height == 180);
            var point = frame.Viewport.ToScreen(new(3, 1));
            Check(bitmap!.GetPixel((int)point.X, (int)point.Y) != new SKColor(frame.Background));
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/complex-linetypes.svg", vector.Bytes);
            File.WriteAllBytes("artifacts/complex-linetypes.png", raster.Bytes);
        });
        Test("linetype text remains escaped in SVG", () =>
        {
            var (session, segment, _, _) = TextDocument();
            session.MutateSource(_ => segment.Text = "<script>&A");
            var render = new RenderCompiler().Compile(session.Current);
            using var svg = new SvgRenderBackend();
            var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(new(render, Camera.Fit(render.Bounds, 600, 200))).Bytes));
            Check(!xml.Descendants().Any(e => e.Name.LocalName == "script"));
        });
    }

    private static (ShadowSession Session, LinetypeTextSegment Segment, Line Line, Linetype Type) TextDocument()
    {
        var style = new TextStyle("LT_TEXT", "Arial.ttf");
        var segment = new LinetypeTextSegment("A", style, -1, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1);
        var type = new Linetype("PIPE_TEXT", new LinetypeSegment[] { new LinetypeSimpleSegment(2), segment, new LinetypeSimpleSegment(-1) });
        var line = new Line(Vector3.Zero, new Vector3(10, 0, 0)) { Linetype = type };
        var doc = new DxfDocument(); doc.Entities.Add(line);
        return (new ShadowSession(doc), segment, line, type);
    }
    private static ShadowSession ShapeDocument()
    {
        var shape = new LinetypeShapeSegment("not-the-resource-name", new ShapeStyle("MARKS", "marks.shx"), -1,
            Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1) { ShapeNumber = 17 };
        var type = new Linetype("PIPE_SHAPE", new LinetypeSegment[] { new LinetypeSimpleSegment(2), shape, new LinetypeSimpleSegment(-1) });
        var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0)) { Linetype = type });
        return new ShadowSession(doc);
    }
    private static ShxFontCatalog Catalog()
    {
        var catalog = new ShxFontCatalog();
        catalog.Register("marks.shx", ShxFontReader.ReadAscii("*17,4,MARK\n8,0,2,0")); return catalog;
    }
    private sealed class TestProvider : ILinetypeDecorationProvider
    {
        public long Revision { get; set; }
        public bool ChangeDuringProjection { get; init; }
        public bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic)
        {
            if (ChangeDuringProjection) Revision++;
            geometry = new([new([new LineCurve(Vec3.Zero, new(0, Revision + 1))])]); diagnostic = null; return true;
        }
    }
    private static ObjectId Id(EntityObject entity) => new("H:" + entity.Handle.ToUpperInvariant());
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message = "Linetype rendering assertion failed.")
    { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run) where T : Exception
    { try { run(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
