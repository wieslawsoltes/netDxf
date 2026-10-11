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
    private static void RegisterProjection(List<(string Name, Action Run)> tests)
    {
        tests.Add(("viewport/scaled affine inversion and singular rejection", () =>
        {
            var matrix = Affine3.Move(new(100, -70, 9)) * Affine3.RotateZ(0.8) * Affine3.Scale(new(-2, 3, 0.25));
            Require(matrix.TryInverse(out var inverse));
            Near(Vec3.Distance(inverse.Point(matrix.Point(new(2, 3, 4))), new(2, 3, 4)), 0);
            Require(!Affine3.Scale(new(1, 0, 1)).TryInverse(out _));
            Require(Affine3.Scale(new(1e100, 1e100, 1e100)).TryInverse(out var large));
            Near(large.X.X / 1e-100, 1);
        }));
        tests.Add(("viewport/model-to-paper target, view center, twist and scale", () =>
        {
            var (document, view) = Drawing();
            var line = new Line(new Vector3(100, 200, 0), new Vector3(110, 200, 0)); document.Entities.Add(line);
            view.Center = new(30, 40, 0); view.ViewTarget = new(100, 200, 0); view.ViewCenter = new(2, 3);
            view.Width = 100; view.Height = 80; view.ViewHeight = 40; view.TwistAngle = 90;
            var item = Projected(new RenderCompiler().Compile(new ShadowSession(document).Current, new("Sheet"))).Single();
            Near(item.Contours[0].Points[0].X, 26); Near(item.Contours[0].Points[0].Y, 34);
            Near(item.Contours[0].Points[^1].X, 26); Near(item.Contours[0].Points[^1].Y, 14);
            var p = item.Projection!;
            Near(Vec3.Distance(p.PaperToModel.Point(p.ModelToPaper.Point(new(107, 212, -3))), new(107, 212, -3)), 0);
        }));
        tests.Add(("viewport/rectangular clipping agrees with Skia, SVG and picking", () =>
        {
            var (document, view) = Drawing(); view.Width = 10; view.Height = 6; view.ViewHeight = 6;
            document.Entities.Add(new Line(new Vector2(-100, 0), new Vector2(100, 0)) { Color = new AciColor((byte)255, (byte)255, (byte)255) });
            var scene = new RenderCompiler().Compile(new ShadowSession(document).Current, new("Sheet"));
            Require(RenderCompiler.HitTest(scene, new(0, 0), 0.01)?.Projection is not null);
            Require(RenderCompiler.HitTest(scene, new(7, 0), 0.01) is null);
            var frame = new RenderFrame(scene, new Camera(Vec3.Zero, 10, 400, 200), Background: 0xff000000);
            using var skia = new SkiaRenderBackend(); using var pixels = SKBitmap.Decode(skia.Render(frame).Bytes);
            Require(pixels.GetPixel(200, 100).Red > 100); Require(pixels.GetPixel(320, 100).Red == 0);
            using var svg = new SvgRenderBackend(); var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(frame).Bytes));
            XNamespace ns = "http://www.w3.org/2000/svg";
            Require(xml.Descendants(ns + "clipPath").Count() == 1);
            Require(xml.Descendants(ns + "g").Any(g => g.Attribute("clip-path") is not null));
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/paper-viewport.png", skia.Render(frame).Bytes);
            File.WriteAllBytes("artifacts/paper-viewport.svg", svg.Render(frame).Bytes);
        }));
        tests.Add(("viewport/nonrectangular mask excludes invisible model geometry", () =>
        {
            var (document, view) = Drawing(); view.ClippingBoundary = new Circle(Vector2.Zero, 5); view.ViewHeight = 10;
            document.Entities.Add(new Line(new Vector2(-20, 4), new Vector2(20, 4)));
            var scene = new RenderCompiler().Compile(new ShadowSession(document).Current, new("Sheet", 0.001));
            Require(RenderCompiler.HitTest(scene, new(0, 4), 0.01)?.Projection is not null);
            Require(RenderCompiler.HitTest(scene, new(4, 4), 0.01) is null);
        }));
        tests.Add(("viewport/frozen insert layers suppress descendants with explicit child layers", () =>
        {
            var (document, view) = Drawing();
            var layer = new Layer("Hidden in this view"); var childLayer = new Layer("Child");
            var block = new Block("Part"); block.Entities.Add(new Line(Vector2.Zero, new Vector2(5, 0)) { Layer = childLayer });
            document.Entities.Add(new Insert(block, Vector2.Zero) { Layer = layer });
            document.Entities.Add(new Line(new Vector2(0, 3), new Vector2(5, 3)));
            view.FrozenLayers.Add(layer);
            var session = new ShadowSession(document); var compiler = new RenderCompiler();
            Require(Projected(compiler.Compile(session.Current, new("Sheet"))).Count() == 1);
            session.MutateSource(_ => view.FrozenLayers.Clear());
            Require(Projected(compiler.Compile(session.Current, new("Sheet"))).Count() == 2);
        }));
        tests.Add(("viewport/oblique camera and depth-clipped line segments", () =>
        {
            var (document, view) = Drawing(); view.ViewDirection = new(0, 1, 0);
            view.Status |= ViewportStatusFlags.FrontClipping | ViewportStatusFlags.BackClipping;
            view.BackClipPlane = -2; view.FrontClipPlane = 2;
            document.Entities.Add(new Line(new Vector3(0, -10, 0), new Vector3(0, 10, 10)));
            var item = Projected(new RenderCompiler().Compile(new ShadowSession(document).Current, new("Sheet"))).Single();
            Near(item.Contours[0].Points[0].Y, 4); Near(item.Contours[0].Points[^1].Y, 6);
            Near(item.Contours[0].Points[0].Z, -2); Near(item.Contours[0].Points[^1].Z, 2);
        }));
    }
}
