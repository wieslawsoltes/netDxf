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
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using DxfViewport = netDxf.Entities.Viewport;
using Screen = netDxf.Shadow.Rendering.Viewport;

internal static partial class PerspectiveVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        RegisterIntegration(tests);
        void Test(string name, Action action) => tests.Add(("perspective/" + name, action));
        Test("pinhole projection changes size with depth", () =>
        {
            var camera = new PerspectiveCamera(Affine3.Identity, Vec3.Zero, 10);
            Check(camera.TryProject(new(2, 3, 0), out var a)); Near(a.X, 2);
            Check(camera.TryProject(new(2, 3, 5), out var b)); Near(b.X, 4); Near(b.Y, 6);
            Check(camera.TryProject(new(2, 3, -10), out var c)); Near(c.X, 1);
            Check(!camera.TryProject(new(0, 0, 10), out _) && !camera.TryProject(new(0, 0, 11), out _));
        });
        Test("ray and unprojection roundtrip random affine camera frames", () =>
        {
            var random = new Random(947);
            var matrix = Affine3.Move(new(15, -7)) * Affine3.Scale(new(2, 2, 1)) * Affine3.RotateZ(.6);
            var camera = new PerspectiveCamera(matrix, new(15, -7), 100);
            for (var i = 0; i < 500; i++)
            {
                var original = new Vec3(random.NextDouble() * 50, random.NextDouble() * 50, random.NextDouble() * 60 - 30);
                Check(camera.TryProject(original, out var projected));
                Near(Vec3.Distance(camera.Unproject(projected, original.Z), original), 0);
                Check(camera.Ray(projected).TryIntersectPlane(new(0, 0, original.Z), Vec3.UnitZ, out var hit));
                Near(Vec3.Distance(hit, original), 0);
            }
        });
        Test("ray rejects parallel and behind-eye planes", () =>
        {
            var ray = new PerspectiveCamera(Affine3.Identity, Vec3.Zero, 10).Ray(Vec3.Zero);
            Check(!ray.TryIntersectPlane(Vec3.UnitX, Vec3.UnitX, out _));
            Check(!ray.TryIntersectPlane(new(0, 0, 20), Vec3.UnitZ, out _));
        });
        foreach (var fraction in new[] { 0, -1, 1, double.NaN, double.PositiveInfinity })
            Test("invalid near fraction " + fraction, () => Throws<ArgumentException>(() => new PerspectiveCamera(Affine3.Identity, Vec3.Zero, 10, fraction)));
        Test("singular camera refused", () => Throws<ArgumentException>(() => new PerspectiveCamera(default, Vec3.Zero, 10)));
        Test("opt-in preserves legacy perspective refusal", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(Vector3.Zero, new Vector3(2, 0, 0)));
            var scene = new RenderCompiler().Compile(new ShadowSession(document).Current, new("Perspective"));
            Check(!Items(scene).Any() && scene.Diagnostics.Any(d => d.Code == "viewport.unsupported"));
        });
        Test("source lines preserve depth-dependent size and source identity", () =>
        {
            var (document, _) = Drawing();
            var a = new Line(Vector3.Zero, new Vector3(2, 0, 0)); var b = new Line(new Vector3(0, 1, 5), new Vector3(2, 1, 5));
            document.Entities.Add(a); document.Entities.Add(b);
            var session = new ShadowSession(document); var scene = new RenderCompiler().Compile(session.Current, Options());
            Near(Items(scene).Single(i => i.Entity == Id(a)).Bounds.Width, 2);
            Near(Items(scene).Single(i => i.Entity == Id(b)).Bounds.Width, 4);
            Check(Items(scene).All(i => i.Projection!.Perspective is not null));
            Check(new TopologyBuilder().Build(session.Current).Edges.Length == 2);
        });
        Test("frustum side clipping happens before division", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(new Vector3(-1000, 0, 5), new Vector3(1000, 0, 5)));
            var item = Items(new RenderCompiler().Compile(new ShadowSession(document).Current, Options())).Single();
            Near(item.Bounds.MinX, -10); Near(item.Bounds.MaxX, 10);
            Check(item.Contours.SelectMany(c => c.Points).All(p => p.IsFinite));
        });
        Test("near-plane crossing never divides an eye-plane vertex", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(new Vector3(0, 0, 0), new Vector3(0, 0, 20)));
            var item = Items(new RenderCompiler().Compile(new ShadowSession(document).Current, Options())).Single();
            Near(item.Contours.Single().Points[^1].Z, 10 * (1 - 1.0 / 64));
        });
        Test("geometry fully behind camera is absent", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(new Vector3(0, 0, 11), new Vector3(0, 0, 20)));
            Check(!Items(new RenderCompiler().Compile(new ShadowSession(document).Current, Options())).Any());
        });
        Test("authored front and back clip planes remain authoritative", () =>
        {
            var (document, view) = Drawing(); view.Status |= ViewportStatusFlags.FrontClipping | ViewportStatusFlags.FrontClipNotAtEye | ViewportStatusFlags.BackClipping;
            view.BackClipPlane = -2; view.FrontClipPlane = 2;
            document.Entities.Add(new Line(new Vector3(0, 0, -5), new Vector3(0, 0, 5)));
            var item = Items(new RenderCompiler().Compile(new ShadowSession(document).Current, Options())).Single();
            Near(item.Contours.Single().Points[0].Z, -2); Near(item.Contours.Single().Points[^1].Z, 2);
        });
        Test("target translation view center and camera twist share ray mapping", () =>
        {
            var (document, view) = Drawing(); view.Center = new(20, 30, 0); view.ViewTarget = new(100, 50, 0); view.ViewCenter = new(1, 2); view.TwistAngle = 90;
            document.Entities.Add(new Line(new Vector3(100, 50, 0), new Vector3(102, 50, 0)));
            var item = Items(new RenderCompiler().Compile(new ShadowSession(document).Current, Options())).Single();
            Near(item.Contours[0].Points[0].X, 19); Near(item.Contours[0].Points[0].Y, 28);
            Near(item.Bounds.Height, 2);
        });
        Test("lens policy uses explicit film height and changes field of view", () =>
        {
            var (document, view) = Drawing(); view.LensLength = 24;
            var line = new Line(Vector3.Zero, new Vector3(1, 0, 0)); document.Entities.Add(line); var session = new ShadowSession(document);
            var compiler = new RenderCompiler(); var options = Options(new(FieldOfView: PerspectiveFieldOfView.LensLength));
            Near(Items(compiler.Compile(session.Current, options)).Single().Bounds.Width, 2);
            session.MutateSource(_ => view.LensLength = 48);
            Near(Items(compiler.Compile(session.Current, options)).Single().Bounds.Width, 4);
        });
        Test("camera edits invalidate only current views and retain old projection", () =>
        {
            var (document, view) = Drawing(); document.Entities.Add(new Line(new Vector3(0, 0, 5), new Vector3(1, 0, 5)));
            var session = new ShadowSession(document); var compiler = new RenderCompiler(); var old = compiler.Compile(session.Current, Options()); var retained = session.Current;
            session.MutateSource(_ => view.ViewDirection = new(0, 0, 20)); var next = compiler.Compile(session.Current, Options());
            Near(Items(old).Single().Bounds.Width, 2); Near(Items(next).Single().Bounds.Width, 4.0 / 3);
            Check(ReferenceEquals(old, compiler.Compile(retained, Options())));
        });
        Test("point and work budgets remain active on repeated compilations", () =>
        {
            var (document, _) = Drawing(); for (var i = 0; i < 4; i++) document.Entities.Add(new Line(new Vector3(0, i, 0), new Vector3(1, i, 0)));
            var session = new ShadowSession(document); var compiler = new RenderCompiler();
            Throws<InvalidOperationException>(() => compiler.Compile(session.Current, Options(new(MaximumPoints: 4))));
            Throws<InvalidOperationException>(() => compiler.Compile(session.Current, Options(new(MaximumPoints: 4))));
            Throws<InvalidOperationException>(() => compiler.Compile(session.Current, Options(new(MaximumWork: 4))));
        });
        Test("cancellation is not partial success", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Circle(Vector2.Zero, 4));
            Throws<OperationCanceledException>(() => new RenderCompiler().Compile(new ShadowSession(document).Current, Options(), new(true)));
        });
        Test("generic affine edit refuses a projective selection before writes", () =>
        {
            var (document, _) = Drawing(); var line = new Line(Vector3.Zero, new Vector3(2, 0, 0)); document.Entities.Add(line);
            var session = new ShadowSession(document); var editor = new EditorSession(session); var scene = new RenderCompiler().Compile(session.Current, Options());
            editor.Select([Items(scene).Single().Key]); var before = session.Current;
            Throws<NotSupportedException>(() => editor.Translate(scene, Vec3.UnitX));
            Throws<NotSupportedException>(() => editor.Transform(scene, Affine3.Move(Vec3.UnitX)));
            Check(ReferenceEquals(before, session.Current) && editor.State.UndoCount == 0);
        });
        Test("ray-plane dragging moves the source once and supports undo redo", () =>
        {
            var (document, _) = Drawing(); var line = new Line(new Vector3(0, 0, 5), new Vector3(2, 0, 5)); document.Entities.Add(line);
            var session = new ShadowSession(document); var editor = new EditorSession(session); var scene = new RenderCompiler().Compile(session.Current, Options());
            editor.Select([Items(scene).Single().Key]); editor.TranslatePerspective(scene, Vec3.Zero, new(2, 0), new(0, 0, 5), Vec3.UnitZ);
            Near(line.StartPoint.X, 1); Near(line.EndPoint.X, 3); Check(editor.State.UndoCount == 1);
            editor.Undo(); Near(line.StartPoint.X, 0); editor.Redo(); Near(line.StartPoint.X, 1);
        });
        Test("invalid work-plane drag rejects without mutation", () =>
        {
            var (document, _) = Drawing(); var line = new Line(Vector3.Zero, new Vector3(1, 0, 0)); document.Entities.Add(line);
            var session = new ShadowSession(document); var editor = new EditorSession(session); var scene = new RenderCompiler().Compile(session.Current, Options());
            editor.Select([Items(scene).Single().Key]); var before = session.Current;
            Throws<NotSupportedException>(() => editor.TranslatePerspective(scene, Vec3.Zero, Vec3.UnitX, new(0, 0, 20), Vec3.UnitZ));
            Check(ReferenceEquals(before, session.Current));
        });
        Test("native parallel text stays shaped and scales exactly", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Text("CAD", new Vector3(0, 0, 5), 1));
            using var typography = new SkiaTextLayoutEngine(); var scene = new RenderCompiler(textLayout: typography).Compile(new ShadowSession(document).Current, Options());
            var item = Items(scene).Single(); Check(item.Text is not null); Near(item.Text!.XAxis.Length, 2); Check(item.TextClip is not null);
        });
        Test("tilted native text becomes true glyph paths on both backends", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Text("CAD", new Vector3(0, 0, 2), 1) { Normal = new(0, 1, 1) });
            using var typography = new SkiaTextLayoutEngine(); var scene = new RenderCompiler(textLayout: typography).Compile(new ShadowSession(document).Current, Options());
            var item = Items(scene).Single(); Check(item.Text is null && !item.Regions.IsEmpty);
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend(textLayout: typography);
            var frame = new RenderFrame(scene, new Screen(Vec3.Zero, 25, 600, 600));
            var vector = svg.Render(frame); var png = skia.Render(frame);
            Check(XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes)).Descendants().Any(e => e.Name.LocalName == "path"));
            using var bitmap = SKBitmap.Decode(png.Bytes); Check(bitmap is not null);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/perspective-typography.png", png.Bytes);
            File.WriteAllBytes("artifacts/perspective-typography.svg", vector.Bytes);
        });
        Test("tilted text without outlines is diagnosed not affinely faked", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Text("CAD", Vector3.Zero, 1) { Normal = new(0, 1, 1) });
            var scene = new RenderCompiler().Compile(new ShadowSession(document).Current, Options());
            Check(!Items(scene).Any() && scene.Diagnostics.Any(d => d.Code == "viewport.projective-paint"));
        });
        Test("native outline quotas and cancellation are explicit", () =>
        {
            using var engine = new SkiaTextLayoutEngine(); var text = new CadText("AAAA", Vec3.Zero, Vec3.UnitX, Vec3.UnitY, 1, "sans-serif", "BaselineLeft", false, 0);
            var layout = engine.Layout(text);
            Throws<InvalidOperationException>(() => engine.Outline(text, layout, 1));
            Throws<OperationCanceledException>(() => engine.Outline(text, layout, 1000, new(true)));
        });
        Test("source DXF save reload preserves camera and projected geometry", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(new Vector3(0, 0, 5), new Vector3(2, 0, 5)));
            using var memory = new MemoryStream(); Check(document.Save(memory)); memory.Position = 0;
            var loaded = DxfDocument.Load(memory)!; var scene = new RenderCompiler().Compile(new ShadowSession(loaded).Current, Options());
            Near(Items(scene).Single().Bounds.Width, 4);
        });
        Test("wireframe gallery retains clipping bounds on every projected item", Gallery);
    }
    private static void Gallery()
    {
        var (document, view) = Drawing(); view.ViewDirection = new(12, -18, 14); view.ViewHeight = 18;
        Vec3[] cube = [new(-3, -3, -3), new(3, -3, -3), new(3, 3, -3), new(-3, 3, -3),
            new(-3, -3, 3), new(3, -3, 3), new(3, 3, 3), new(-3, 3, 3)];
        foreach (var (a,b) in new (int,int)[] { (0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7) })
            document.Entities.Add(new Line(new Vector3(cube[a].X,cube[a].Y,cube[a].Z),new Vector3(cube[b].X,cube[b].Y,cube[b].Z)));
        using var engine = new SkiaTextLayoutEngine(); var scene = new RenderCompiler(textLayout: engine).Compile(new ShadowSession(document).Current, Options());
        Check(Items(scene).Count() == 12 && Items(scene).All(i => i.Bounds.MinX >= -10-1e-8 && i.Bounds.MaxX <= 10+1e-8));
        using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend(textLayout:engine); var frame = new RenderFrame(scene,new Screen(Vec3.Zero,35,800,800));
        Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/perspective-wireframe.png",skia.Render(frame).Bytes);
        File.WriteAllBytes("artifacts/perspective-wireframe.svg",svg.Render(frame).Bytes);
    }
    private static RenderCompileOptions Options(PerspectiveOptions? policy=null) => new("Perspective") { PaperSpace = new() { Perspective = policy ?? new() } };
    private static IEnumerable<RenderItem> Items(RenderScene scene) => scene.Items.Where(i => i.Projection?.Perspective is not null);
    private static (DxfDocument Document, DxfViewport View) Drawing()
    {
        var document = new DxfDocument(); var layout = new Layout("Perspective"); document.Layouts.Add(layout);
        var view = new DxfViewport { Width=20, Height=20, ViewHeight=20, ViewDirection=new(0,0,10), Center=Vector3.Zero };
        view.Status |= ViewportStatusFlags.PerspectiveMode; layout.AssociatedBlock.Entities.Add(view); return (document,view);
    }
    private static ObjectId Id(DxfObject o) => new("H:"+o.Handle.ToUpperInvariant());
    private static void Check(bool condition) { if(!condition) throw new InvalidOperationException("Perspective assertion failed."); }
    private static void Near(double a,double b) { if(!double.IsFinite(a)||Math.Abs(a-b)>1e-7)throw new Exception($"Expected {b}, got {a}."); }
    private static void Throws<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}.");}
}
