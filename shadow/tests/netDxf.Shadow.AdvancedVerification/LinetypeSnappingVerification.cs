using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class LinetypeSnappingVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-snapping/" + name, run));
        Test("presentation metadata marks only generated symbols", () =>
        {
            var scene = Scene();
            Check(scene.Items.Length == 3 && !scene.Items[0].IsPresentationOnly);
            Check(scene.Items.Skip(1).All(item => item.IsPresentationOnly));
        });
        Test("default snapping excludes glyph vertices but keeps source picking", () =>
        {
            var scene = Scene();
            Check(Snapping.Find(scene, new(3, 1), 0.01, SnapMode.Vertex) is null);
            var hit = RenderCompiler.HitTest(scene, new(3, 1), 0.01);
            Check(hit is { IsPresentationOnly: true } && hit.Entity == scene.Items[0].Entity);
        });
        Test("presentation snapping requires explicit opt in", () =>
        {
            var scene = Scene();
            var snap = Snapping.Find(scene, new(3, 1), 0.01, includePresentation: true, mode: SnapMode.Vertex);
            Check(snap is not null && snap.Occurrence == scene.Items[1].Key);
        });
        Test("semantic nearest snapping does not inherit printed dash gaps", () =>
        {
            var scene = Scene();
            var snap = Snapping.Find(scene, new(3, 0), 0.01, SnapMode.Nearest);
            Check(snap is not null && snap.Occurrence == scene.Items[0].Key);
        });
        Test("original compiler and snapping binary signatures remain available", () =>
        {
            Check(typeof(RenderCompiler).GetConstructor([typeof(CadSceneBuilder), typeof(ITextLayoutEngine)]) is not null);
            Check(typeof(RenderCompiler).GetMethods().Any(m => m.Name == "Compile" && m.IsStatic && m.GetParameters().Length == 4));
            Check(typeof(Snapping).GetMethods().Any(m => m.Name == "Find" && m.GetParameters().Length == 6));
        });
    }
    private static RenderScene Scene()
    {
        var pattern = new CadLinetype([new(2), new(-1, new(CadLinetypeDecorationKind.Shape, "MARK", "host")), new(-1)], 1, Vec3.UnitZ);
        var occurrence = new CadOccurrence("line", new ObjectId("test:line"), [], CadStyle.Default with { Dashes = [2, -1, -1], Linetype = pattern },
            new([new([new LineCurve(Vec3.Zero, new(10, 0))])]));
        return RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model", [occurrence], []), linetypeResources: new Symbols());
    }
    private sealed class Symbols : ILinetypeDecorationProvider
    {
        public long Revision => 0;
        public bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic)
        { geometry = new([new([new LineCurve(Vec3.Zero, Vec3.UnitY)])]); diagnostic = null; return true; }
    }
    private static void Check(bool value)
    { if (!value) throw new Exception("Linetype snapping assertion failed."); }
}
