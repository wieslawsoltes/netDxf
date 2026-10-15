using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
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

internal static partial class BigFontVerification
{
    private const string Primary = "*0,4,Primary\n10,2,2,0\n*65,0,A\n8,5,10,2,8,3,-10,14,8,-8,-12,0\n*32,0,space\n2,8,5,0,14,8,-5,-6,0";
    private static ShxBigFontMap Map(int target = 32832) => new([new(0x3a9, target)]);
    private static ShxFontCatalog Catalog()
    {
        var catalog = new ShxFontCatalog(); catalog.Register("primary.shx", ShxFontReader.ReadAscii(Primary));
        catalog.RegisterBigFont("big.shx", ShxFontReader.ReadAscii(Regular), Map()); return catalog;
    }
    private static CadGeometry Project(ShxEntityProjector projector, string content, bool vertical = false)
    {
        Check(projector.TryProject(new CadLinetypeDecoration(CadLinetypeDecorationKind.Text, content, "primary.shx", IsVertical: vertical, BigFont: "big.shx"),
            out var geometry, out var diagnostic));
        Check(diagnostic is null, diagnostic ?? ""); return geometry;
    }
    private static RenderScene Render(CadGeometry geometry) => RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model",
        [new("pair", new ObjectId("test:pair"), [], CadStyle.Default, geometry)], []));
    private static (ShadowSession Session, Text Text) Source(string content = "AΩA", bool vertical = false)
    {
        var document = new DxfDocument();
        var style = new TextStyle("PAIRED", "primary.shx") { BigFont = "big.shx", IsVertical = vertical };
        var text = new Text(content, Vector2.Zero, 10, style); document.Entities.Add(text);
        return (new ShadowSession(document), text);
    }
    private static void RegisterLayout(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("bigfont-layout/" + name, action));
        Test("Unicode map is detached and permits supplementary scalars", () =>
        {
            var pairs = new List<KeyValuePair<int, int>> { new(0x1f600, 32832) }; var map = new ShxBigFontMap(pairs); pairs.Clear();
            Check(map.Characters[0x1f600] == 32832);
            var catalog = Catalog(); catalog.RegisterBigFont("big.shx", catalog.Current.Resolve("big.shx")!, map);
            Check(Project(new ShxEntityProjector(catalog), "\U0001F600").Strokes.Sum(s => s.Contours.Length) == 1);
        });
        foreach (var pair in new[] { new KeyValuePair<int, int>(0xd800, 1), new(0x110000, 1), new(-1, 1), new(65, 0), new(65, 65536) })
            Test("invalid map/" + tests.Count, () => Throws<ArgumentException>(() => new ShxBigFontMap([pair])));
        Test("mapping duplicates and quotas fail before publication", () =>
        {
            Throws<ArgumentException>(() => new ShxBigFontMap([new(65, 1), new(65, 2)]));
            Throws<InvalidOperationException>(() => new ShxBigFontMap([new(65, 1), new(66, 2)], 1));
            var catalog = Catalog(); var before = catalog.Current;
            Throws<ArgumentException>(() => catalog.RegisterBigFont("wrong", ShxFontReader.ReadAscii(Primary), Map()));
            Check(ReferenceEquals(before, catalog.Current));
        });
        Test("host encoding construction does not infer an encoding from the filename", () =>
        {
            var font = ShxFontReader.ReadAscii(Regular);
            var map = ShxBigFontMap.FromEncoding(font, Encoding.BigEndianUnicode);
            Check(map.Characters[0x8040] == 32832 && !map.Characters.ContainsKey(0x3a9));
            Check(ShxBigFontMap.FromEncoding(font, Encoding.ASCII).Characters.IsEmpty);
            Throws<OperationCanceledException>(() => ShxBigFontMap.FromEncoding(font, Encoding.BigEndianUnicode, new(true)));
        });
        Test("map-only replacement advances the same resource revision as fonts", () =>
        {
            var catalog = Catalog(); var before = catalog.Current; var font = before.Resolve("big.shx")!;
            var after = catalog.RegisterBigFont("big.shx", font, Map(32833));
            Check(after.Revision == before.Revision + 1 && before.ResolveBigFont("big.shx")!.Map.Characters[0x3a9] == 32832);
            Check(after.ResolveBigFont("big.shx")!.Map.Characters[0x3a9] == 32833);
            Check(ReferenceEquals(after, catalog.RegisterBigFont("big.shx", font, after.BigFontMaps["big.shx"])));
        });
        Test("font and map aliases cannot be mixed across directories", () =>
        {
            var catalog = Catalog(); catalog.Register("/unmapped/big.shx", ShxFontReader.ReadAscii(Regular));
            Check(catalog.Current.ResolveBigFont("/unmapped/big.shx") is null);
            Check(catalog.Current.ResolveBigFont("C:\\mapped\\big.shx") is not null);
        });
        Test("removing or replacing a companion with an ordinary font clears its mapping", () =>
        {
            var catalog = Catalog(); var old = catalog.Current; catalog.Remove("big.shx");
            Check(catalog.Current.ResolveBigFont("big.shx") is null && old.ResolveBigFont("big.shx") is not null);
            catalog.RegisterBigFont("big.shx", old.Resolve("big.shx")!, Map()); catalog.Register("big.shx", ShxFontReader.ReadAscii(Primary));
            Check(!catalog.Current.BigFontMaps.ContainsKey("big.shx"));
        });
        Test("primary and companion metrics normalize before layout", () =>
        {
            var geometry = Project(new ShxEntityProjector(Catalog()), "AΩA");
            var bounds = Render(geometry).Bounds;
            Near(bounds.Width, 2.3); Near(bounds.Height, 1);
            Check(geometry.Contours.IsEmpty && geometry.Strokes.Sum(s => s.Contours.Sum(c => c.Curves.Length)) == 3);
        });
        Test("explicit mappings take priority over matching primary Unicode glyphs", () =>
        {
            var catalog = Catalog(); catalog.RegisterBigFont("big.shx", catalog.Current.Resolve("big.shx")!, new([new(65, 32832)]));
            Near(Render(Project(new ShxEntityProjector(catalog), "A")).Bounds.Width, 0);
        });
        Test("unmapped primary glyphs keep their original strokes", () =>
        {
            var geometry = Project(new ShxEntityProjector(Catalog()), "A");
            Near(Render(geometry).Bounds.Width, .5); Near(Render(geometry).Bounds.Height, 1);
        });
        Test("missing companion or mapping is never guessed", () =>
        {
            var catalog = Catalog(); catalog.Remove("big.shx");
            Throws<NotSupportedException>(() => Project(new ShxEntityProjector(catalog), "Ω"));
            catalog.Register("big.shx", ShxFontReader.ReadAscii(Regular));
            Throws<NotSupportedException>(() => Project(new ShxEntityProjector(catalog), "Ω"));
        });
        Test("mapped but absent glyph produces a diagnosed box", () =>
        {
            var catalog = Catalog(); catalog.RegisterBigFont("big.shx", catalog.Current.Resolve("big.shx")!, Map(32834));
            var projector = new ShxEntityProjector(catalog);
            Check(projector.TryProject(new CadLinetypeDecoration(CadLinetypeDecorationKind.Text, "Ω", "primary.shx", BigFont: "big.shx"), out var geometry, out var issue));
            Check(issue?.Contains("Mapped BIGFONT", StringComparison.Ordinal) == true && !geometry.Strokes.IsEmpty);
        });
        Test("vertical companion execution is not rotated horizontal geometry", () =>
        {
            var projector = new ShxEntityProjector(Catalog());
            var geometry = Project(projector, "ΩΩ", true);
            var curves = geometry.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            Check(curves.Length == 2); Near(curves[1].Start.X - curves[0].Start.X, 0); Near(curves[1].Start.Y - curves[0].Start.Y, -1);
            var horizontal = Render(Project(projector, "ΩΩ"));
            Near(horizontal.Bounds.Width, 1); Near(Render(geometry).Bounds.Height, 2);
        });
        Test("horizontal-only companion rejects vertical layout", () =>
        {
            var catalog = Catalog(); catalog.RegisterBigFont("big.shx", ShxFontReader.ReadAscii(Regular.Replace("20,0,2,0", "20,0,0,0", StringComparison.Ordinal)), Map());
            Throws<NotSupportedException>(() => Project(new ShxEntityProjector(catalog), "Ω", true));
        });
        Test("source TEXT uses decoded Unicode with independent companion styles", () =>
        {
            var (session, _) = Source(); var compiler = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())]));
            var result = compiler.Compile(session.Current); Near(result.Bounds.Width, 23); Near(result.Bounds.Height, 10);
            Check(result.Items.Single().Text is null && result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error));
            Check(new TopologyBuilder().Build(session.Current).Edges.IsEmpty);
        });
        Test("MTEXT scopes and paragraph composition retain mapped strokes", () =>
        {
            var doc = new DxfDocument(); var style = new TextStyle("PAIR", "primary.shx") { BigFont = "big.shx" };
            doc.Entities.Add(new MText("{\\c255;Ω}A\\PΩ", Vector2.Zero, 10, 30, style));
            var session = new ShadowSession(doc); var compiler = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())]));
            var result = compiler.Compile(session.Current);
            Check(result.Items.Single().Strokes.Any(s => s.Argb.HasValue));
            Check(result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error));
        });
        Test("retained resources pin Unicode interpretation as well as glyph bytes", () =>
        {
            var (session, _) = Source("Ω"); var catalog = Catalog();
            var live = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog)]));
            var pinned = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog.Current)]));
            var old = live.Compile(session.Current); var fixedScene = pinned.Compile(session.Current);
            catalog.RegisterBigFont("big.shx", catalog.Current.Resolve("big.shx")!, Map(32833));
            var next = live.Compile(session.Current);
            Near(old.Bounds.Width, 0); Near(next.Bounds.Width, 5); Check(ReferenceEquals(fixedScene, pinned.Compile(session.Current)));
        });
        Test("source edits preserve old immutable text scenes", () =>
        {
            var (session, text) = Source("Ω"); var compiler = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())]));
            var retained = session.Current; var before = compiler.Compile(retained);
            session.MutateSource(_ => text.Value = "ΩΩ"); var next = compiler.Compile(session.Current);
            Near(before.Bounds.Width, 0); Near(next.Bounds.Width, 10); Check(ReferenceEquals(before, compiler.Compile(retained)));
        });
        Test("complex linetype labels use the same companion mapping", () =>
        {
            var doc = new DxfDocument(); var style = new TextStyle("PAIR", "primary.shx") { BigFont = "big.shx" };
            var pattern = new Linetype("PAIR_LINE", new LinetypeSegment[] { new LinetypeSimpleSegment(2),
                new LinetypeTextSegment("Ω", style, -1, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1), new LinetypeSimpleSegment(-1) });
            var line = new Line(Vector2.Zero, new Vector2(10, 0)) { Linetype = pattern }; doc.Entities.Add(line);
            var session = new ShadowSession(doc); var result = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())])).Compile(session.Current);
            Check(result.Items.Count(i => i.IsPresentationOnly) == 2 && result.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error));
            Check(new TopologyBuilder().Build(session.Current).Edges.Length == 1);
        });
        Test("DXF save and reload retain companion reference and Unicode content", () =>
        {
            var (session, text) = Source();
            using var stream = new MemoryStream(); session.MutateSource(d => Check(d.Save(stream)));
            stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
            Check(loaded.Entities.Texts.Single().Style.BigFont == "big.shx" && loaded.Entities.Texts.Single().Value == text.Value);
            var result = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(Catalog())])).Compile(new ShadowSession(loaded).Current);
            Near(result.Bounds.Width, 23);
        });
        Test("paired geometry uses shared SVG and native output", () =>
        {
            var result = Render(Project(new ShxEntityProjector(Catalog()), "AΩA"));
            var frame = new RenderFrame(result, new Camera(new(1.15, .5), 100, 340, 180));
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend();
            var vector = svg.Render(frame); var png = skia.Render(frame);
            Check(XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes)).Descendants().Any(e => e.Name.LocalName == "path"));
            using var bitmap = SKBitmap.Decode(png.Bytes); Check(bitmap is not null && bitmap.Width == 340);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/bigfont-paired.png", png.Bytes); File.WriteAllBytes("artifacts/bigfont-paired.svg", vector.Bytes);
        });
        Test("paired layout retains character and curve budgets", () =>
        {
            Throws<InvalidOperationException>(() => Project(new ShxEntityProjector(Catalog(), new(MaximumCharacters: 1)), "ΩΩ"));
            Throws<InvalidOperationException>(() => Project(new ShxEntityProjector(Catalog(), new(MaximumCurves: 1)), "ΩΩ"));
        });
    }
}
