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

internal static partial class ViewportVerification
{
    private static void RegisterEditing(List<(string Name, Action Run)> tests)
    {
        tests.Add(("viewport/projected edit transforms paper deltas and preserves undo", () =>
        {
            var (document, view) = Drawing(); view.Height = 40; view.ViewHeight = 20;
            var line = new Line(Vector2.Zero, new Vector2(5, 0)); document.Entities.Add(line);
            var session = new ShadowSession(document); var editor = new EditorSession(session); var compiler = new RenderCompiler();
            var retained = session.Current; var scene = compiler.Compile(retained, new("Sheet"));
            editor.Select([Projected(scene).Single().Key]); editor.Translate(scene, new(6, 4));
            Near(line.StartPoint.X, 3); Near(line.StartPoint.Y, 2);
            editor.Undo(); Near(line.StartPoint.X, 0); editor.Redo(); Near(line.StartPoint.X, 3);
            Near(retained.Read(Id(line))["StartPoint"].Vector().X, 0);
        }));
        tests.Add(("viewport/incompatible multi-view edits fail before source mutation", () =>
        {
            var (document, first) = Drawing();
            var second = new DxfViewport { Center = new(40, 0, 0), Width = 20, Height = 40, ViewHeight = 20 };
            document.Layouts["Sheet"].AssociatedBlock.Entities.Add(second);
            var line = new Line(Vector2.Zero, new Vector2(5, 0)); document.Entities.Add(line);
            var session = new ShadowSession(document); var editor = new EditorSession(session);
            var scene = new RenderCompiler().Compile(session.Current, new("Sheet"));
            editor.Select(Projected(scene).Select(i => i.Key)); var before = session.Current;
            Throws<ShadowConflictException>(() => editor.Translate(scene, new(4, 0)));
            Require(ReferenceEquals(before, session.Current)); Near(line.StartPoint.X, 0);
        }));
        tests.Add(("viewport/block definition editing uses the inverse occurrence transform", () =>
        {
            var document = new DxfDocument(); var block = new Block("Scaled");
            var line = new Line(Vector2.Zero, new Vector2(5, 0)); block.Entities.Add(line);
            document.Entities.Add(new Insert(block, new Vector2(20, 30)) { Rotation = 90, Scale = new(2, 2, 1) });
            var session = new ShadowSession(document); var editor = new EditorSession(session);
            var scene = new RenderCompiler().Compile(session.Current); editor.Select([scene.Items.Single().Key]);
            editor.Translate(scene, new(0, 4), true); Near(line.StartPoint.X, 2); Near(line.StartPoint.Y, 0);
            editor.Undo(); Near(line.StartPoint.X, 0);
        }));
        tests.Add(("viewport/global and paper-space linetype scales preserve authored patterns", () =>
        {
            var (document, view) = Drawing(); view.Width = 100; view.Height = 40; view.ViewHeight = 20;
            var line = new Line(Vector2.Zero, new Vector2(10, 0))
            { Linetype = new Linetype("Dash", [new LinetypeSimpleSegment(1), new LinetypeSimpleSegment(-1)]) };
            document.Entities.Add(line); document.DrawingVariables.LtScale = 2;
            var session = new ShadowSession(document); var compiler = new RenderCompiler();
            var one = Projected(compiler.Compile(session.Current, new("Sheet"))).Single();
            Near(Vec3.Distance(one.Contours[0].Points[0], one.Contours[0].Points[^1]), 2);
            session.MutateSource(_ => document.DrawingVariables.PsLtScale = 0);
            var two = Projected(compiler.Compile(session.Current, new("Sheet"))).Single();
            Near(Vec3.Distance(two.Contours[0].Points[0], two.Contours[0].Points[^1]), 4);
            session.MutateSource(_ => document.DrawingVariables.LtScale = 3);
            Near(compiler.Compile(session.Current).Items.Single().Style.Dashes[0], 3);
        }));
        tests.Add(("viewport/retained views, incremental reuse, budgets and cancellation", () =>
        {
            var (document, view) = Drawing(); var line = new Line(Vector2.Zero, new Vector2(5, 0)); document.Entities.Add(line);
            var other = new Line(new Vector2(500, 500), new Vector2(501, 500)); document.Entities.Add(other);
            var session = new ShadowSession(document); var compiler = new RenderCompiler(); var old = session.Current;
            var first = Projected(compiler.Compile(old, new("Sheet"))).Single();
            var editor = new EditorSession(session); editor.SetProperty(Id(other), "EndPoint", ShadowValue.From(new Vec3(502, 500)));
            var second = Projected(compiler.Compile(session.Current, new("Sheet"))).Single();
            Require(ReferenceEquals(first, second)); Require(ReferenceEquals(first, Projected(compiler.Compile(old, new("Sheet"))).Single()));
            Throws<OperationCanceledException>(() => compiler.Compile(session.Current, new("Sheet"), new CancellationToken(true)));
            session.MutateSource(_ => document.Layouts["Sheet"].AssociatedBlock.Entities.Add(new DxfViewport()));
            Throws<InvalidOperationException>(() => compiler.Compile(session.Current, new("Sheet") { PaperSpace = new(MaximumViewports: 1) }));
        }));
    }
}
