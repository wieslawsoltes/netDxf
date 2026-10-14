using System.Text;
using System.Collections.Immutable;
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

internal static class VerticalShxVerification
{
    // Horizontal advance (6,0); conditional corrections yield vertical top-center origin and advance (0,-12).
    private const string FontSource = "*0,4,Vertical\n10,2,2,0\n*65,0,A\n2,14,8,-3,-10,1,9,0,10,6,0,0,-10,0,0,2,14,8,-3,-2,0";
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("vertical-shx/" + name, run));
        Test("vertical glyph origins and two glyph advances are not a rotated string", () =>
        {
            var (session, _, projector) = Drawing("AA");
            var semantic = new CadSceneBuilder([projector]).Build(session.Current);
            var curves = semantic.Occurrences.Single().Geometry.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            Check(curves.Length == 6); Near(curves[0].Start.X, -3); Near(curves[0].Start.Y, -10);
            Near(curves[3].Start.X, -3); Near(curves[3].Start.Y, -22);
            Check(new TopologyBuilder().Build(semantic).Edges.IsEmpty);
        });
        Test("horizontal and vertical glyph cache entries remain independent", () =>
        {
            var (session, text, projector) = Drawing("A"); var builder = new CadSceneBuilder([projector]);
            var vertical = builder.Build(session.Current); session.MutateSource(_ => text.IsVertical = false);
            var horizontal = builder.Build(session.Current);
            Near(Start(vertical).X, -3); Near(Start(horizontal).X, 0);
            session.MutateSource(_ => text.IsVertical = true); Near(Start(builder.Build(session.Current)).X, -3);
        });
        Test("color boundaries preserve cumulative vertical advance", () =>
        {
            var (session, _, projector) = Drawing("{\\c255;A}A", true);
            var strokes = new CadSceneBuilder([projector]).Build(session.Current).Occurrences.Single().Geometry.Strokes;
            Check(strokes.Length == 2 && strokes[0].Argb != strokes[1].Argb);
            Near(strokes[1].Contours[0].Curves[0].Start.Y - strokes[0].Contours[0].Curves[0].Start.Y, -12);
        });
        Test("paragraphs produce leftwards columns", () =>
        {
            var (session, _, projector) = Drawing("A\\PA", true);
            var strokes = new CadSceneBuilder([projector]).Build(session.Current).Occurrences.Single().Geometry.Strokes;
            Check(strokes.Length == 2); var a = strokes[0].Contours[0].Curves[0].Start; var b = strokes[1].Contours[0].Curves[0].Start;
            Check(b.X < a.X); Near(a.Y, b.Y);
        });
        Test("width and oblique modify glyph axes without scaling flow twice", () =>
        {
            var (session, _, projector) = Drawing("{\\W2;AA}", true);
            var geometry = new CadSceneBuilder([projector]).Build(session.Current).Occurrences.Single().Geometry;
            var curves = geometry.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            Near(curves[3].Start.Y - curves[0].Start.Y, -12); Near(curves[1].End.X - curves[1].Start.X, 12);
        });
        Test("rules and fractions use common paragraph composition", () =>
        {
            var (session, _, projector) = Drawing("\\LA\\l \\SAA/A;", true);
            var geometry = new CadSceneBuilder([projector]).Build(session.Current).Occurrences.Single().Geometry;
            Check(geometry.Regions.Length >= 2 && geometry.Strokes.Length >= 3);
        });
        Test("vertical linetype text uses the same registered resource", () =>
        {
            var catalog = Catalog(); var projector = new ShxEntityProjector(catalog);
            Check(projector.TryProject(new CadLinetypeDecoration(CadLinetypeDecorationKind.Text, "AA", "vertical.shx", IsVertical: true), out var geometry, out _));
            Check(geometry.Strokes.Single().Contours.Length == 2);
        });
        Test("live replacement does not mutate pinned vertical geometry", () =>
        {
            var (session, _, _) = Drawing("AA"); var catalog = Catalog();
            var live = new RenderCompiler(new([new ShxEntityProjector(catalog)]));
            var pinned = new RenderCompiler(new([new ShxEntityProjector(catalog.Current)]));
            var old = pinned.Compile(session.Current); catalog.Register("vertical.shx", ShxFontReader.ReadAscii(FontSource.Replace("-3,-2", "-3,-5")));
            Check(live.Compile(session.Current).Bounds.MinY < old.Bounds.MinY); Check(ReferenceEquals(old, pinned.Compile(session.Current)));
        });
        Test("missing vertical glyph produces explicit geometry and diagnostic", () =>
        {
            var (session, _, projector) = Drawing("Z"); var scene = new CadSceneBuilder([projector]).Build(session.Current);
            Check(scene.Occurrences.Length == 1 && scene.Diagnostics.Any(d => d.Message.Contains("Missing vertical", StringComparison.Ordinal)));
        });
        Test("unsupported vertical metrics retain an explicit refusal", () =>
        {
            var (session, _, _) = Drawing("A"); var catalog = new ShxFontCatalog(); catalog.Register("vertical.shx", ShxFontReader.ReadAscii(FontSource.Replace("10,2,2,0", "10,2,0,0")));
            var scene = new CadSceneBuilder([new ShxEntityProjector(catalog)]).Build(session.Current);
            Check(scene.Occurrences.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        });
        Test("vertical curve budget rejects without partial source output", () =>
        {
            var (session, _, _) = Drawing("AA"); var scene = new CadSceneBuilder([new ShxEntityProjector(Catalog(), new(MaximumCurves: 3))]).Build(session.Current);
            Check(scene.Occurrences.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        });
        Test("SVG and Skia share vertical stroke geometry", () =>
        {
            var (session, _, projector) = Drawing("AA"); var scene = new RenderCompiler(new([projector])).Compile(session.Current);
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend();
            var frame = new RenderFrame(scene, netDxf.Shadow.Rendering.Viewport.Fit(scene.Bounds, 320, 640));
            Check(svg.Render(frame).Bytes.Length > 100 && skia.Render(frame).Bytes.Length > 100);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/vertical-shx.svg", svg.Render(frame).Bytes); File.WriteAllBytes("artifacts/vertical-shx.png", skia.Render(frame).Bytes);
        });
    }
    private static (ShadowSession, TextStyle Style, ShxEntityProjector) Drawing(string content, bool multiline = false)
    {
        var doc = new DxfDocument(); var style = new TextStyle("VERTICAL", "vertical.shx") { IsVertical = true };
        doc.Entities.Add(multiline ? new MText(content, Vector2.Zero, 10, 0, style) : new Text(content, Vector2.Zero, 10, style));
        return (new(doc), style, new ShxEntityProjector(Catalog()));
    }
    private static ShxFontCatalog Catalog() { var catalog = new ShxFontCatalog(); catalog.Register("vertical.shx", ShxFontReader.ReadAscii(FontSource)); return catalog; }
    private static Vec3 Start(CadScene scene) => scene.Occurrences.Single().Geometry.Strokes[0].Contours[0].Curves[0].Start;
    private static void Near(double a, double b) => Check(double.IsFinite(a) && Math.Abs(a - b) < 1e-8);
    private static void Check(bool condition) { if (!condition) throw new Exception("Vertical SHX assertion failed."); }
}
