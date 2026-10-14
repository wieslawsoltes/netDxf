using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using DxfViewport = netDxf.Entities.Viewport;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class ViewportOverrideVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("viewport-overrides/" + name, action));
        Test("ByLayer resolves color opacity and lineweight before projection", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            var scene = new RenderCompiler().Compile(session.Current, Options(session, d, new(0xff0000, 0.8, 50)));
            var item = LineItem(scene, d.Line);
            Check(item.Style.Argb == 0x80ff0000); Near(item.Style.Lineweight, 0.8);
        });
        Test("explicit entity properties remain authoritative", () =>
        {
            var d = Drawing(); d.Line.Color = new AciColor(0, 255, 0); d.Line.Lineweight = Lineweight.W50; d.Line.Transparency = new Transparency(25);
            var session = new ShadowSession(d.Document);
            var item = LineItem(new RenderCompiler().Compile(session.Current, Options(session, d, new(0xff0000, 0.8, 50))), d.Line);
            Check(item.Style.Argb == 0xbf00ff00); Near(item.Style.Lineweight, 0.5);
        });
        Test("ByBlock and layer zero inherit the overridden insertion layer", () =>
        {
            var d = Drawing(); d.Document.Entities.Remove(d.Line);
            d.Line.Layer = Layer.Default; d.Line.Color = AciColor.ByBlock; d.Line.Lineweight = Lineweight.ByBlock; d.Line.Transparency = Transparency.ByBlock;
            var block = new Block("OVERRIDE_BLOCK"); block.Entities.Add(d.Line);
            var insert = new Insert(block) { Layer = d.Layer, Color = AciColor.ByLayer, Lineweight = Lineweight.ByLayer, Transparency = Transparency.ByLayer };
            d.Document.Entities.Add(insert); var session = new ShadowSession(d.Document);
            var item = LineItem(new RenderCompiler().Compile(session.Current, Options(session, d, new(0x123456, 0.8, 50))), d.Line);
            Check(item.Style.Argb == 0x80123456 && item.InsertPath.Length == 1); Near(item.Style.Lineweight, 0.8);
        });
        Test("two viewports keep independent layer appearance", () =>
        {
            var d = Drawing(); var other = SecondView(d); var session = new ShadowSession(d.Document);
            var red = Profile(d.Layer, new(0xff0000)); var green = Profile(d.Layer, new(0x00ff00));
            var catalog = new ViewportOverrideCatalog(session.Current.DocumentId, [new(Id(d.View), red), new(Id(other), green)]);
            var result = new RenderCompiler().Compile(session.Current, new("Sheet") { ViewportOverrides = catalog });
            Check(result.Items.Single(i => i.Entity == Id(d.Line) && i.Projection?.ViewportEntity == Id(d.View)).Style.Argb == 0xffff0000);
            Check(result.Items.Single(i => i.Entity == Id(d.Line) && i.Projection?.ViewportEntity == Id(other)).Style.Argb == 0xff00ff00);
        });
        Test("equivalent profiles share the distinct compilation quota", () =>
        {
            var d = Drawing(); var other = SecondView(d); var session = new ShadowSession(d.Document);
            var a = Profile(d.Layer, new(0xff0000)); var b = Profile(d.Layer, new(0xff0000));
            Check(a.Equals(b) && a.GetHashCode() == b.GetHashCode() && !ReferenceEquals(a, b));
            var catalog = new ViewportOverrideCatalog(session.Current.DocumentId, [new(Id(d.View), a), new(Id(other), b)]);
            var result = new RenderCompiler().Compile(session.Current, new("Sheet")
            { ViewportOverrides = catalog, PaperSpace = new() { MaximumPatternScales = 1 } });
            Check(result.Items.Count(i => i.Entity == Id(d.Line) && i.Projection is not null) == 2);
        });
        Test("complex linetype override generates labels before clipping", () =>
        {
            var d = Drawing(); var pattern = ComplexPattern(); d.Document.Linetypes.Add(pattern);
            var session = new ShadowSession(d.Document);
            var result = new RenderCompiler().Compile(session.Current, Options(session, d, new(Linetype: Id(pattern))));
            Check(result.Items.Any(i => i.Entity == Id(d.Line) && i.IsPresentationOnly && i.Text?.Content == "A"));
            Check(result.Items.Where(i => i.Entity == Id(d.Line)).All(i => i.Projection is not null));
            Check(d.Layer.Linetype.Name == "Continuous");
        });
        Test("explicit entity linetypes ignore the layer pattern override", () =>
        {
            var d = Drawing(); var pattern = ComplexPattern(); d.Document.Linetypes.Add(pattern); d.Line.Linetype = Linetype.Continuous;
            var session = new ShadowSession(d.Document);
            var result = new RenderCompiler().Compile(session.Current, Options(session, d, new(Linetype: Id(pattern))));
            Check(!result.Items.Any(i => i.IsPresentationOnly));
        });
        Test("viewport freeze suppresses the layer", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            var result = new RenderCompiler().Compile(session.Current, Options(session, d, new(Frozen: true)));
            Check(!result.Items.Any(i => i.Entity == Id(d.Line)));
        });
        Test("viewport-local thaw removes a native frozen-layer entry", () =>
        {
            var d = Drawing(); d.View.FrozenLayers.Add(d.Layer); var session = new ShadowSession(d.Document); var compiler = new RenderCompiler();
            Check(!compiler.Compile(session.Current, new("Sheet")).Items.Any(i => i.Entity == Id(d.Line)));
            Check(compiler.Compile(session.Current, Options(session, d, new(Frozen: false))).Items.Any(i => i.Entity == Id(d.Line)));
            Check(d.View.FrozenLayers.Contains(d.Layer));
        });
        foreach (var globallyFrozen in new[] { true, false })
            Test("viewport thaw cannot override global hidden state/" + globallyFrozen, () =>
            {
                var d = Drawing(); d.Layer.IsFrozen = globallyFrozen; d.Layer.IsVisible = globallyFrozen;
                var session = new ShadowSession(d.Document);
                Check(!new RenderCompiler().Compile(session.Current, Options(session, d, new(Frozen: false))).Items.Any(i => i.Entity == Id(d.Line)));
            });
        Test("nonplotting override does not change ordinary screen display", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            Check(new RenderCompiler().Compile(session.Current, Options(session, d, new(Plot: false))).Items.Any(i => i.Entity == Id(d.Line)));
        });
        Test("plot context excludes nonplotting layers", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            Check(!new RenderCompiler().Compile(session.Current, Options(session, d, new(Plot: false), true)).Items.Any(i => i.Entity == Id(d.Line)));
        });
        Test("plot context respects source flags and explicit plot override", () =>
        {
            var d = Drawing(); d.Layer.Plot = false; var session = new ShadowSession(d.Document); var compiler = new RenderCompiler();
            Check(!compiler.Compile(session.Current, Options(session, d, new(), true)).Items.Any(i => i.Entity == Id(d.Line)));
            Check(compiler.Compile(session.Current, Options(session, d, new(Plot: true), true)).Items.Any(i => i.Entity == Id(d.Line)));
        });
        Test("source receipt geometry graph and history remain unchanged", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var editor = new EditorSession(session); var retained = session.Current;
            var graph = new TopologyBuilder().Build(retained); var options = Options(session, d, new(0xff0000, 1, 70));
            var scene = new RenderCompiler().Compile(retained, options);
            Check(ReferenceEquals(retained, session.Current) && scene.Revision == retained.Revision && editor.State.UndoCount == 0);
            Check(d.Layer.Color.B == 255 && d.Layer.Color.R == 0);
            Check(new TopologyBuilder().Build(retained).Edges.Length == graph.Edges.Length);
        });
        Test("model tab ignores paper-only overrides", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            var result = new RenderCompiler().Compile(session.Current, Options(session, d, new(0xff0000)) with { Layout = "Model" });
            Check(result.Items.Single(i => i.Entity == Id(d.Line)).Style.Argb == 0xff0000ff);
        });
        Test("compiled paper frames are cached by immutable context", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var compiler = new RenderCompiler(); var options = Options(session, d, new(0xff0000));
            Check(ReferenceEquals(compiler.Compile(session.Current, options), compiler.Compile(session.Current, options)));
        });
        Test("source edits invalidate inherited fields without changing retained frames", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var compiler = new RenderCompiler(); var options = Options(session, d, new(TransparencyPercent: 50));
            var snapshot = session.Current; var before = compiler.Compile(snapshot, options);
            session.MutateSource(_ => d.Layer.Color = new AciColor(0, 255, 0));
            var after = compiler.Compile(session.Current, options);
            Check(LineItem(before, d.Line).Style.Argb == 0x800000ff && LineItem(after, d.Line).Style.Argb == 0x8000ff00);
            Check(ReferenceEquals(before, compiler.Compile(snapshot, options)));
        });
        Test("overridden linetype dependencies observe label edits", () =>
        {
            var d = Drawing(); var pattern = ComplexPattern(); d.Document.Linetypes.Add(pattern); var session = new ShadowSession(d.Document);
            var options = Options(session, d, new(Linetype: Id(pattern))); var compiler = new RenderCompiler(); var before = compiler.Compile(session.Current, options);
            session.MutateSource(_ => ((LinetypeTextSegment)pattern.Segments[1]).Text = "B");
            var after = compiler.Compile(session.Current, options);
            Check(before.Items.Any(i => i.Text?.Content == "A") && after.Items.Any(i => i.Text?.Content == "B"));
        });
        Test("source-identified paper editing still produces one undo step", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var editor = new EditorSession(session);
            var scene = new RenderCompiler().Compile(session.Current, Options(session, d, new(0xff0000)));
            editor.Select([LineItem(scene, d.Line).Key]); editor.Translate(scene, new(2, 3));
            Near(d.Line.StartPoint.X, -6); Near(d.Line.StartPoint.Y, 3); Check(editor.State.UndoCount == 1);
            editor.Undo(); Near(d.Line.StartPoint.X, -8); Near(d.Line.StartPoint.Y, 0);
        });
        Test("foreign document catalogs are rejected", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            Throws<ArgumentException>(() => new RenderCompiler().Compile(session.Current, new("Sheet")
            { ViewportOverrides = new ViewportOverrideCatalog(Guid.NewGuid(), []) }));
        });
        Test("unknown viewport layer and linetype targets are rejected", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var compiler = new RenderCompiler(); var id = session.Current.DocumentId;
            Throws<ArgumentException>(() => compiler.Compile(session.Current, new("Sheet")
            { ViewportOverrides = new(id, [new(Id(d.Line), Profile(d.Layer, new()))]) }));
            Throws<ArgumentException>(() => compiler.Compile(session.Current, new("Sheet")
            { ViewportOverrides = new(id, [new(Id(d.View), new([new(Id(d.Line), new())]))]) }));
            Throws<ArgumentException>(() => compiler.Compile(session.Current, Options(session, d, new(Linetype: Id(d.Line)))));
        });
        Test("removed viewport targets invalidate a retained catalog", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var options = Options(session, d, new());
            session.MutateSource(_ => d.Document.Layouts["Sheet"].AssociatedBlock.Entities.Remove(d.View));
            Throws<ArgumentException>(() => new RenderCompiler().Compile(session.Current, options));
        });
        Test("variant budget is checked before model glyph-provider execution", () =>
        {
            var d = Drawing(); var second = SecondView(d); d.Layer.Linetype = ComplexPattern(); var session = new ShadowSession(d.Document); var provider = new CountingProvider();
            var catalog = new ViewportOverrideCatalog(session.Current.DocumentId,
                [new(Id(d.View), Profile(d.Layer, new(0xff0000))), new(Id(second), Profile(d.Layer, new(0x00ff00)))]);
            Throws<InvalidOperationException>(() => new RenderCompiler(linetypeResources: provider).Compile(session.Current,
                new("Sheet") { ViewportOverrides = catalog, PaperSpace = new() { MaximumPatternScales = 1 } }));
            Check(provider.Calls == 0);
        });
        Test("input collections are detached and duplicate identities are refused", () =>
        {
            var values = new List<KeyValuePair<ObjectId, ViewportLayerOverride>> { new(new("L"), new(1)) };
            var profile = new ViewportLayerProfile(values); values.Clear(); Check(profile.Layers.Count == 1);
            Throws<ArgumentException>(() => new ViewportLayerProfile([new(new("L"), new()), new(new("L"), new())]));
            Throws<InvalidOperationException>(() => new ViewportLayerProfile([new(new("L1"), new()), new(new("L2"), new())], 1));
            Throws<ArgumentException>(() => new ViewportOverrideCatalog(Guid.NewGuid(), [new(new("V"), profile), new(new("V"), profile)]));
            Throws<InvalidOperationException>(() => new ViewportOverrideCatalog(Guid.NewGuid(),
                [new(new("V1"), profile), new(new("V2"), profile)], limits: new(MaximumProfiles: 1)));
        });
        foreach (var value in new[] { new ViewportLayerOverride(Rgb: 0x1000000), new(LineweightMillimeters: -1), new(LineweightMillimeters: double.NaN),
            new(TransparencyPercent: 91), new(TransparencyPercent: double.PositiveInfinity), new(Linetype: new ObjectId("")) })
            Test("invalid value/" + tests.Count, () => Throws<ArgumentException>(() => new ViewportLayerProfile([new(new("L"), value)])));
        Test("cached paths still honor cancellation", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document); var compiler = new RenderCompiler(); var options = Options(session, d, new());
            _ = compiler.Compile(session.Current, options);
            Throws<OperationCanceledException>(() => compiler.Compile(session.Current, options, new(true)));
        });
        Test("Skia and SVG consume the same overridden paint", () =>
        {
            var d = Drawing(); var session = new ShadowSession(d.Document);
            var scene = new RenderCompiler().Compile(session.Current, Options(session, d, new(0xff0000, 0.8)));
            var frame = new RenderFrame(scene, new Camera(Vec3.Zero, 8, 320, 320));
            using var skia = new SkiaRenderBackend(); using var svg = new SvgRenderBackend();
            var png = skia.Render(frame); var vector = svg.Render(frame);
            using var bitmap = SKBitmap.Decode(png.Bytes); var pixel = bitmap.GetPixel(128, 160);
            Check(pixel.Red > pixel.Green + 20 && pixel.Red > pixel.Blue + 20);
            Check(XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes)).Descendants().Any(e => e.Name.LocalName == "path"));
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/viewport-layer-overrides.png", png.Bytes);
            File.WriteAllBytes("artifacts/viewport-layer-overrides.svg", vector.Bytes);
        });
    }

    private sealed record DrawingData(DxfDocument Document, DxfViewport View, Layer Layer, Line Line);
    private static DrawingData Drawing()
    {
        var document = new DxfDocument(); var layout = new Layout("Sheet"); document.Layouts.Add(layout);
        var view = new DxfViewport { Center = Vector3.Zero, Width = 40, Height = 40, ViewHeight = 40 };
        layout.AssociatedBlock.Entities.Add(view);
        var layer = new Layer("PIPE") { Color = new AciColor(0, 0, 255) };
        var line = new Line(new Vector2(-8, 0), new Vector2(8, 0))
        { Layer = layer, Color = AciColor.ByLayer, Lineweight = Lineweight.ByLayer, Transparency = Transparency.ByLayer, Linetype = Linetype.ByLayer };
        document.Entities.Add(line); return new(document, view, layer, line);
    }
    private static DxfViewport SecondView(DrawingData d)
    {
        var view = new DxfViewport { Center = new Vector3(50, 0, 0), Width = 40, Height = 40, ViewHeight = 40 };
        d.Document.Layouts["Sheet"].AssociatedBlock.Entities.Add(view); return view;
    }
    private static Linetype ComplexPattern() => new("OVERRIDE_TEXT", new LinetypeSegment[]
    {
        new LinetypeSimpleSegment(2), new LinetypeTextSegment("A", new TextStyle("OVERRIDE_STYLE", "Arial.ttf"), -1,
            new Vector2(0, 2), LinetypeSegmentRotationType.Relative, 0, 1), new LinetypeSimpleSegment(-1)
    });
    private static RenderCompileOptions Options(ShadowSession session, DrawingData d, ViewportLayerOverride value, bool plot = false)
        => new("Sheet") { ViewportOverrides = new(session.Current.DocumentId, [new(Id(d.View), Profile(d.Layer, value))], plot) };
    private static ViewportLayerProfile Profile(Layer layer, ViewportLayerOverride value) => new([new(Id(layer), value)]);
    private static RenderItem LineItem(RenderScene scene, Line line) => scene.Items.First(i => i.Entity == Id(line) && i.Projection is not null && !i.IsPresentationOnly);
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message = "Viewport override assertion failed.") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
    private sealed class CountingProvider : ILinetypeDecorationProvider
    {
        public long Revision => 0;
        public int Calls { get; private set; }
        public bool TryProject(CadLinetypeDecoration value, out CadGeometry geometry, out string? diagnostic)
        { Calls++; geometry = CadGeometry.Empty; diagnostic = null; return true; }
    }
}
