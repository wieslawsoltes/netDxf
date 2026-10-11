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
    private static void RegisterVisibility(List<(string Name, Action Run)> tests)
    {
        tests.Add(("viewport/front-at-eye ignores authored FRONTZ unless bit sixteen is set", () =>
        {
            var (document, view) = Drawing(); view.ViewDirection = new(0, 0, 10);
            view.Status |= ViewportStatusFlags.FrontClipping; view.FrontClipPlane = 3;
            document.Entities.Add(new Line(new Vector3(0, 0, 0), new Vector3(10, 0, 20)));
            var session = new ShadowSession(document); var compiler = new RenderCompiler();
            var eye = Projected(compiler.Compile(session.Current, new("Sheet"))).Single();
            Near(eye.Contours[0].Points[^1].X, 5);
            session.MutateSource(_ => view.Status |= ViewportStatusFlags.FrontClipNotAtEye);
            var authored = Projected(compiler.Compile(session.Current, new("Sheet"))).Single();
            Near(authored.Contours[0].Points[^1].X, 1.5);
            session.MutateSource(_ => view.FrontClipPlane = double.NaN);
            Require(compiler.Compile(session.Current, new("Sheet")).Diagnostics.Any(d => d.Code == "viewport.unsupported"));
        }));
        tests.Add(("viewport/off and unsupported perspective views do not fabricate model output", () =>
        {
            var (document, view) = Drawing(); document.Entities.Add(new Line(Vector2.Zero, new Vector2(5, 0)));
            view.Status |= ViewportStatusFlags.ViewportOff;
            var session = new ShadowSession(document); var compiler = new RenderCompiler();
            Require(!Projected(compiler.Compile(session.Current, new("Sheet"))).Any());
            session.MutateSource(_ => { view.Status &= ~ViewportStatusFlags.ViewportOff; view.Status |= ViewportStatusFlags.PerspectiveMode; });
            var scene = compiler.Compile(session.Current, new("Sheet"));
            Require(!Projected(scene).Any()); Require(scene.Diagnostics.Any(d => d.Code == "viewport.unsupported"));
        }));
        tests.Add(("viewport/paper copies and viewport frames do not multiply semantic topology", () =>
        {
            var (document, _) = Drawing(); document.Entities.Add(new Line(Vector2.Zero, new Vector2(5, 0)));
            var session = new ShadowSession(document);
            Require(Projected(new RenderCompiler().Compile(session.Current, new("Sheet"))).Any());
            Require(new TopologyBuilder().Build(session.Current).Edges.Length == 1);
            Require(new TopologyBuilder().Build(session.Current, "Sheet").Edges.Length == 0);
        }));
    }
}
