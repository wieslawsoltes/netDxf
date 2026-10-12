using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;

internal static class ResourceRevisionVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("resources/projection changes cannot publish a mixed-resource scene", () =>
        {
            var doc = new DxfDocument(); doc.Entities.Add(new Line(Vector2.Zero, new Vector2(10, 0)));
            var snapshot = new ShadowSession(doc).Current;
            var builder = new CadSceneBuilder([new ChangingProjector()]);
            Throws<InvalidOperationException>(() => builder.Build(snapshot));
            Require(builder.Build(snapshot).Occurrences.Single().Geometry.Contours[0].Curves[0].End.X == 2);
            var compiler = new RenderCompiler(new CadSceneBuilder([new ChangingProjector()]));
            Throws<InvalidOperationException>(() => compiler.Compile(snapshot));
            var stable = compiler.Compile(snapshot);
            Require(stable.Bounds.MaxX == 2 && ReferenceEquals(stable, compiler.Compile(snapshot)));
        }));
        tests.Add(("shx/combined embedding metadata is preserved without licensing reinterpretation", () =>
        {
            var font = ShxFontReader.ReadAscii("*UNIFONT,0,TEST\n10,2,0,0,3,0\n*65,0,A\n010,0");
            Require(font.Embedding == (ShxEmbedding.Disallowed | ShxEmbedding.ReadOnly));
            Throws<FormatException>(() => ShxFontReader.ReadAscii("*UNIFONT,0,TEST\n10,2,0,0,4,0\n*65,0,A\n010,0"));
        }));
        tests.Add(("shx/legacy percent symbols select reserved glyphs and literal O-stroke stays literal", () =>
        {
            const string source = "*0,4,Legacy\n10,2,0,0\n*256,0,degree\n010,0\n*257,0,tolerance\n020,0\n*258,0,diameter\n030,0\n*216,0,Oslash\n040,0";
            var fonts = new ShxFontCatalog(); fonts.Register("legacy.shx", ShxFontReader.ReadAscii(source));
            var doc = new DxfDocument(); doc.Entities.Add(new Text("%%d%%p%%cØ", Vector2.Zero, 10, new TextStyle("Legacy", "legacy.shx")));
            var scene = new RenderCompiler(new([new ShxEntityProjector(fonts)])).Compile(new ShadowSession(doc).Current);
            Require(scene.Diagnostics.IsEmpty && scene.Items.Single().Bounds.MaxX == 10);
            Require(TextLayout.PlainText("%%cØ", false) == "∅Ø");
        }));
        tests.Add(("shx/nonbreaking space obeys the same glyph state contract", () =>
        {
            var fonts = new ShxFontCatalog();
            fonts.Register("invalid.shx", ShxFontReader.ReadAscii("*0,4,Invalid state\n10,2,0,0\n*32,0,space\n4,2,010,0"));
            var doc = new DxfDocument(); doc.Entities.Add(new Text("\u00a0", Vector2.Zero, 10, new TextStyle("Invalid state", "invalid.shx")));
            var scene = new RenderCompiler(new([new ShxEntityProjector(fonts)])).Compile(new ShadowSession(doc).Current);
            Require(scene.Items.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("restore their vector scale")));
        }));
    }
    private sealed class ChangingProjector : ICacheableEntityProjector
    {
        public long Revision { get; private set; }
        public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
        {
            if (Revision == 0) Revision = 1;
            geometry = new([new([new LineCurve(Vec3.Zero, new(Revision + 1, 0))])]);
            diagnostic = null; return true;
        }
    }
    private static void Require(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
