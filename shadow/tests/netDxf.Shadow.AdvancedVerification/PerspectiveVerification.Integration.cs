using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;

internal static partial class PerspectiveVerification
{
    private static void RegisterIntegration(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("perspective/integration/" + name, action));
        Test("unrepresentably small near distance is rejected", () =>
            Throws<ArgumentException>(() => new PerspectiveCamera(Affine3.Identity, Vec3.Zero, 10, 1e-30)));
        Test("zero ray and overflowing plane normalization are rejected", () =>
        {
            Throws<ArgumentException>(() => new PickingRay(Vec3.Zero, Vec3.Zero).TryIntersectPlane(Vec3.Zero, Vec3.UnitZ, out _));
            Throws<ArgumentException>(() => new PickingRay(Vec3.Zero, Vec3.UnitZ).TryIntersectPlane(Vec3.Zero, new(double.MaxValue, double.MaxValue), out _));
        });
        Test("nonrectangular mask controls projective picking", () =>
        {
            var (document, view) = Drawing(); view.ClippingBoundary = new Circle(Vector2.Zero, 4);
            view.ViewHeight = 8; // Assigning a native clip updates Width/Height; retain unit target-plane scale.
            var line = new Line(new Vector3(-5, 3, 0), new Vector3(5, 3, 0)); document.Entities.Add(line);
            var scene = new RenderCompiler().Compile(new ShadowSession(document).Current, Options());
            var item = Items(scene).Single();
            Check(RenderHitTesting.Contains(item, new(0, 3), .01));
            Check(!RenderHitTesting.Contains(item, new(3.8, 3), .01));
        });
        Test("layer overrides precede perspective style resolution", () =>
        {
            var (document, view) = Drawing(); var layer = new Layer("PROJECTED") { Color = new AciColor(0, 0, 255) };
            var line = new Line(Vector3.Zero, new Vector3(2, 0, 0)) { Layer = layer }; document.Entities.Add(line);
            var session = new ShadowSession(document); var profile = new ViewportLayerProfile([new(Id(layer), new(0xff0000))]);
            var options = Options() with { ViewportOverrides = new(session.Current.DocumentId, [new(Id(view), profile)]) };
            var item = Items(new RenderCompiler().Compile(session.Current, options)).Single();
            Check(item.Style.Argb == 0xffff0000 && layer.Color.B == 255);
        });
        Test("PSLTSCALE glyphs use the reference plane without model annotation compounding", () =>
        {
            var (document, view) = Drawing(); view.ViewHeight = 40;
            var type = new Linetype("PROJECTED_TEXT", new LinetypeSegment[] { new LinetypeSimpleSegment(2),
                new LinetypeTextSegment("A", new TextStyle("LABEL", "Arial.ttf"), -1), new LinetypeSimpleSegment(-1) });
            document.Entities.Add(new Line(new Vector3(-10, 0, 0), new Vector3(10, 0, 0)) { Linetype = type });
            var session = new ShadowSession(document);
            var options = Options() with { ModelAnnotation = new(new AnnotationScale(1, 100), ModelAnnotationMode.Enabled) };
            var scene = new RenderCompiler().Compile(session.Current, options);
            var symbols = Items(scene).Where(i => i.IsPresentationOnly).ToArray();
            Check(symbols.Length > 0 && symbols.All(i => i.Text is not null));
            Near(symbols[0].Text!.XAxis.Length, 1);
        });
        Test("filled polygons are clipped to the finite frustum", () =>
        {
            Vec3[] p = [new(-100, -100, 5), new(100, -100, 5), new(100, 100, 5), new(-100, 100, 5)];
            var geometry = new CadGeometry([new(p.Select((v, i) => (Curve3)new LineCurve(v, p[(i + 1) % 4])).ToImmutableArray(), true)], true);
            var item = Fixture(geometry).Single();
            Check(!item.Regions.IsEmpty); Near(item.Bounds.MinX, -10); Near(item.Bounds.MaxY, 10);
            Check(RenderHitTesting.Contains(item, Vec3.Zero, .01));
        });
        Test("camera-parallel raster keeps exact corners and image mask", () =>
        {
            var image = new CadImage("host-only.png", [new(0, 0, 5), new(2, 0, 5), new(2, 2, 5), new(0, 2, 5)]);
            var item = Fixture(new([], Image: image)).Single();
            Check(item.Image is not null && item.ImageClip is not null);
            Near(item.Image!.Corners[2].X, 4); Near(item.Bounds.Height, 4);
        });
        Test("retained raster payload participates in output point budget", () =>
        {
            var image = new CadImage("host-only.png", [new(0, 0, 5), new(2, 0, 5), new(2, 2, 5), new(0, 2, 5)]);
            var (document, _) = Drawing(); document.Entities.Add(new Line(Vector3.Zero, new Vector3(1, 0, 0)));
            var compiler = new RenderCompiler(new CadSceneBuilder([new FixedGeometry(new([], Image: image))]));
            var snapshot = new ShadowSession(document).Current;
            Throws<InvalidOperationException>(() => compiler.Compile(snapshot, Options(new(MaximumPoints: 4))));
            Throws<InvalidOperationException>(() => compiler.Compile(snapshot, Options(new(MaximumPoints: 4))));
        });
        Test("tilted raster is diagnosed rather than projected affinely", () =>
        {
            var image = new CadImage("host-only.png", [new(0, 0, 0), new(2, 0, 0), new(2, 2, 1), new(0, 2, 1)]);
            var scene = FixtureScene(new([], Image: image));
            Check(!Items(scene).Any() && scene.Diagnostics.Any(d => d.Code == "viewport.projective-paint"));
        });
        Test("Studio view control is source neutral and an exact no-op", () =>
        {
            using var studio = new Studio(); var before = State(studio); var revision = before.GetProperty("revision").GetInt64();
            studio.SetPerspectiveView(new(revision, "TargetPlaneHeight", 5)); var after = State(studio);
            Check(after.GetProperty("revision").GetInt64() == revision && after.GetProperty("undoCount").GetInt32() == 0);
            Check(after.GetProperty("viewVersion").GetInt64() > before.GetProperty("viewVersion").GetInt64());
            studio.SetPerspectiveView(new(revision, "TargetPlaneHeight", 5));
            Check(State(studio).GetProperty("changeVersion").GetInt64() == after.GetProperty("changeVersion").GetInt64());
        });
        Test("Studio perspective context is inherited but isolated by branch", () =>
        {
            using var studio = new Studio(); var revision = State(studio).GetProperty("revision").GetInt64();
            studio.SetPerspectiveView(new(revision, "LensLength", 5)); studio.Fork(new(revision, "Projective experiment"));
            Check(State(studio).GetProperty("perspectiveMode").GetString() == "LensLength");
            studio.SetPerspectiveView(new(revision, "Disabled", 0)); studio.SwitchWorkspace(new(revision, "live"));
            Check(State(studio).GetProperty("perspectiveMode").GetString() == "LensLength");
            Near(State(studio).GetProperty("perspectiveWorkPlaneZ").GetDouble(), 5);
        });
        Test("Studio stale invalid and disabled projective requests do not mutate state", () =>
        {
            using var studio = new Studio(); var before = State(studio); var revision = before.GetProperty("revision").GetInt64();
            Throws<ShadowConflictException>(() => studio.SetPerspectiveView(new(revision + 1, "LensLength")));
            Throws<ArgumentException>(() => studio.SetPerspectiveView(new(revision, "unknown")));
            Throws<ArgumentException>(() => studio.SetPerspectiveView(new(revision, "LensLength", double.NaN)));
            Throws<InvalidOperationException>(() => studio.TranslatePerspective(new(revision, 0, 0, 1, 1)));
            Check(State(studio).GetProperty("changeVersion").GetInt64() == before.GetProperty("changeVersion").GetInt64());
        });
        Test("Studio projected selection drags on its configured source plane", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(new Vector3(0, 0, 5), new Vector3(2, 0, 5)));
            using var stream = new MemoryStream(); Check(document.Save(stream)); stream.Position = 0;
            using var studio = new Studio(); studio.Load(stream); var revision = State(studio).GetProperty("revision").GetInt64();
            studio.SetLayout(new(revision, "Perspective")); studio.SetPerspectiveView(new(revision, "TargetPlaneHeight", 5));
            studio.Pick(new(revision, 1, 0, .01));
            Check(State(studio).GetProperty("perspectiveSelection").GetBoolean());
            studio.TranslatePerspective(new(revision, 1, 0, 3, 0)); var after = State(studio);
            Check(after.GetProperty("undoCount").GetInt32() == 1);
            studio.History("undo", after.GetProperty("revision").GetInt64());
            Check(State(studio).GetProperty("redoCount").GetInt32() == 1);
        });
    }
    private static IEnumerable<RenderItem> Fixture(CadGeometry geometry) => Items(FixtureScene(geometry));
    private static RenderScene FixtureScene(CadGeometry geometry)
    {
        var (document, _) = Drawing(); document.Entities.Add(new Line(Vector3.Zero, new Vector3(1, 0, 0)));
        var builder = new CadSceneBuilder([new FixedGeometry(geometry)]);
        return new RenderCompiler(builder).Compile(new ShadowSession(document).Current, Options());
    }
    private sealed class FixedGeometry(CadGeometry value) : ICacheableEntityProjector
    {
        public long Revision => 0;
        public bool TryProject(ShadowReader source, out CadGeometry geometry, out string? diagnostic)
        { geometry = value; diagnostic = null; return source.Type == "Line"; }
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
