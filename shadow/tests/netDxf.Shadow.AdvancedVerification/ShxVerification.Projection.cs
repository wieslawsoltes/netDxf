using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Studio;
using SkiaSharp;

internal static partial class ShxVerification
{
    private static ShxFont TestFont(int x = 5) => ShxFontReader.ReadAscii($"*0,4,Test font\n10,2,0,0\n*65,0,A\n9,0,10,{x},0,0,-10,0,0,2,8,3,0,0");
    private static (DxfDocument, ShxFontCatalog, CadSceneBuilder) TextDrawing(string text = "AA")
    {
        var document = new DxfDocument(); var catalog = new ShxFontCatalog(); catalog.Register("test.shx", TestFont());
        document.Entities.Add(new Text(text, Vector2.Zero, 10, new TextStyle("Stroke", "test.shx")));
        return (document, catalog, new CadSceneBuilder([new ShxEntityProjector(catalog)]));
    }
    private static void RegisterProjection(List<(string Name, Action Run)> tests)
    {
        tests.Add(("shx/catalog snapshots, alias resolution and bounded replacement", () =>
        {
            var catalog = new ShxFontCatalog(1); var font = TestFont(); var old = catalog.Register("test.shx", font);
            Require(ReferenceEquals(font, old.Resolve(@"C:\cad\test.shx")));
            Require(ReferenceEquals(old, catalog.Register("TEST.SHX", font)));
            var next = catalog.Register("test.shx", TestFont(7)); Require(next.Revision == old.Revision + 1);
            Require(ReferenceEquals(old.Resolve("test.shx"), font));
            Throws<InvalidOperationException>(() => catalog.Register("other.shx", font));
            catalog.Remove("test.shx"); Require(catalog.Current.Fonts.IsEmpty && old.Fonts.Count == 1);
        }));
        tests.Add(("shx/text uses exact strokes and stays outside semantic topology", () =>
        {
            var (document, _, builder) = TextDrawing(); var snapshot = new ShadowSession(document).Current;
            var scene = new RenderCompiler(builder).Compile(snapshot); var item = scene.Items.Single();
            Require(item.Text is null && item.Strokes.Length == 1 && item.Strokes[0].Contours.Length == 2);
            Near(item.Bounds.MinX, 0); Near(item.Bounds.MaxX, 13); Near(item.Bounds.MinY, 0); Near(item.Bounds.MaxY, 10);
            Require(RenderCompiler.HitTest(scene, new(0, 5), .01) is not null);
            Require(RenderCompiler.HitTest(scene, new(3, 4), .01) is null);
            Require(new TopologyBuilder().Build(builder.Build(snapshot)).Edges.IsEmpty);
            Require(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        }));
        tests.Add(("shx/named shape geometry honors transforms and participates in topology", () =>
        {
            var document = new DxfDocument(); var catalog = new ShxFontCatalog(); catalog.Register("test.shx", TestFont());
            var block = new Block("Shapes"); block.Entities.Add(new Shape("A", new ShapeStyle("Shapes", "test.shx"), Vector3.Zero, 2, 0));
            document.Entities.Add(new Insert(block, new Vector2(20, 30)) { Scale = new(2, 3, 1) });
            var builder = new CadSceneBuilder([new ShxEntityProjector(catalog)]); var snapshot = new ShadowSession(document).Current;
            var scene = new RenderCompiler(builder).Compile(snapshot); var item = scene.Items.Single();
            Near(item.Bounds.MinX, 20); Near(item.Bounds.MaxX, 40); Near(item.Bounds.MinY, 30); Near(item.Bounds.MaxY, 90);
            Require(item.Strokes.IsEmpty && !item.Contours.IsEmpty);
            Require(new TopologyBuilder().Build(builder.Build(snapshot)).Edges.Length == 3);
        }));
        tests.Add(("shx/rich-text colors and decorations render identically as geometry", () =>
        {
            var (document, catalog, builder) = TextDrawing("A"); document.Entities.Remove(document.Entities.Texts.Single());
            document.Entities.Add(new MText(@"\c16711680;\LAA\l\P\c255;AA", Vector2.Zero, 10, 100, new TextStyle("Stroke", "test.shx")));
            var scene = new RenderCompiler(builder).Compile(new ShadowSession(document).Current);
            var item = scene.Items.Single(); Require(item.Strokes.Length >= 2 && !item.Regions.IsEmpty);
            Require(item.Strokes.Any(s => s.Argb == 0xffff0000) && item.Strokes.Any(s => s.Argb == 0xff0000ff));
            var frame = new RenderFrame(scene, netDxf.Shadow.Rendering.Viewport.Fit(scene.Bounds, 500, 300), Background: 0xff000000);
            using var skia = new SkiaRenderBackend(); using var bitmap = SKBitmap.Decode(skia.Render(frame).Bytes);
            Require(bitmap.Pixels.Any(c => c.Red > 128 && c.Blue < 10)); Require(bitmap.Pixels.Any(c => c.Blue > 128 && c.Red < 10));
            using var svg = new SvgRenderBackend(); var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(frame).Bytes));
            Require(!xml.Descendants().Any(e => e.Name.LocalName == "text"));
            Require(xml.Descendants().Any(e => e.Attribute("stroke")?.Value == "#ff0000"));
            Near((RenderPaint.Resolve(0x80ff0000, 0x8000ffff) >> 24), 64);
        }));
        tests.Add(("shx/font revision updates live geometry but frozen resources remain unchanged", () =>
        {
            var (document, catalog, builder) = TextDrawing("A"); var snapshot = new ShadowSession(document).Current;
            var live = new RenderCompiler(builder);
            var pinned = new RenderCompiler(new CadSceneBuilder([new ShxEntityProjector(catalog.Current)]));
            var first = live.Compile(snapshot); var retained = pinned.Compile(snapshot);
            catalog.Register("test.shx", TestFont(9)); var second = live.Compile(snapshot);
            Near(first.Bounds.MaxX, 5); Near(second.Bounds.MaxX, 9);
            Require(ReferenceEquals(retained, pinned.Compile(snapshot))); Require(!ReferenceEquals(first, second));
        }));
        tests.Add(("shx/missing glyphs and absent resources do not perform implicit I/O", () =>
        {
            var (document, _, builder) = TextDrawing("AZ");
            var scene = new RenderCompiler(builder).Compile(new ShadowSession(document).Current);
            Require(scene.Diagnostics.Any(d => d.Message.Contains("Missing SHX glyph", StringComparison.Ordinal)));
            document.Entities.Texts.Single().Style = new TextStyle("No access", "not-a-host-resource.shx");
            var missing = new RenderCompiler(builder).Compile(new ShadowSession(document).Current);
            Require(missing.Diagnostics.Any(d => d.Message.Contains("substitutes a system font", StringComparison.Ordinal)));
        }));
        tests.Add(("shx/text and rules are transported into paper-space clipping", () =>
        {
            var (document, _, builder) = TextDrawing("AA"); var layout = new Layout("Sheet"); document.Layouts.Add(layout);
            layout.AssociatedBlock.Entities.Add(new netDxf.Entities.Viewport { Center = new(50, 40, 0), Width = 20, Height = 20, ViewHeight = 20 });
            var scene = new RenderCompiler(builder).Compile(new ShadowSession(document).Current, new("Sheet"));
            var projected = scene.Items.Single(i => i.Projection is not null);
            Require(!projected.Strokes.IsEmpty && !projected.Clips.IsEmpty);
            Near(projected.Strokes[0].Contours[0].Points[0].X, 50); Near(projected.Strokes[0].Contours[0].Points[0].Y, 40);
            Require(RenderCompiler.HitTest(scene, new(50, 45), .01)?.Projection is not null);
            Require(RenderCompiler.HitTest(scene, new(63, 45), .01) is null);
        }));
        tests.Add(("shx/studio import, pinned fonts and save-reload preserve source snapshots", () =>
        {
            using var studio = new Studio(); var initial = State(studio); var revision = initial.GetProperty("revision").GetInt64();
            studio.SetLayout(new(revision, "SHX strokes"));
            var png = studio.Render(1000, 650, null, null, null, "skia", null, default);
            var svg = studio.Render(1000, 650, null, null, null, "svg", null, default); studio.Retain(revision);
            var before = State(studio).GetProperty("fontRevision").GetInt64();
            Throws<FormatException>(() => studio.LoadFont(revision, "bad.shx", Encoding.ASCII.GetBytes("AutoCAD-86 shapes 1.0\r\n")));
            Require(State(studio).GetProperty("fontRevision").GetInt64() == before);
            studio.LoadFont(revision, "shadow-demo.shp", Encoding.ASCII.GetBytes("*0,4,replacement\n10,2,0,0\n*65,0,A\n9,0,5,3,0,0,-5,0,0,2,8,3,0,0"));
            Require(State(studio).GetProperty("revision").GetInt64() == revision);
            Require(State(studio).GetProperty("fontRevision").GetInt64() > before);
            Require(studio.Render(1000, 650, null, null, null, "skia", revision, default).Bytes.SequenceEqual(png.Bytes));
            var bytes = studio.Save(); studio.Load(new MemoryStream(bytes));
            studio.SetLayout(new(State(studio).GetProperty("revision").GetInt64(), "SHX strokes"));
            Require(studio.Render(1000, 650, null, null, null, "skia", null, default).Bytes.Length > 1000);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/shx-gallery.png", png.Bytes); File.WriteAllBytes("artifacts/shx-gallery.svg", svg.Bytes);
        }));
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
