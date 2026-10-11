using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Units;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class AppearanceVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("appearance/tapered-width rendering, hit testing and semantic topology", () =>
        {
            var polyline = new Polyline2D([new Polyline2DVertex(0, 0) { StartWidth = 2, EndWidth = 4 }, new Polyline2DVertex(10, 0)]);
            var (session, geometry) = Capture(polyline);
            Require(geometry.Contours.Single().Curves.Length == 1);
            Require(geometry.Regions.Length == 1 && geometry.DisplayContours.IsEmpty);
            var scene = new RenderCompiler().Compile(session.Current);
            Near(scene.Bounds.MinY, -2); Near(scene.Bounds.MaxY, 2);
            Require(RenderCompiler.HitTest(scene, new(5, 1.4), 0.01) is not null);
            Require(RenderCompiler.HitTest(scene, new(5, 2.1), 0.01) is null);
            var graph = new TopologyBuilder().Build(session.Current);
            Require(graph.Nodes.Length == 2 && graph.Edges.Length == 1, "Painted ribbons must not become graph edges.");
            var retained = session.Current;
            session.MutateSource(_ => polyline.Vertexes[0].EndWidth = 8);
            var next = new RenderCompiler().Compile(session.Current);
            Near(next.Bounds.MaxY, 4); Near(scene.Bounds.MaxY, 2);
            Require(retained.Read(Id(polyline))["Vertexes"].Items.First()["EndWidth"].Number() == 4);
        }));
        tests.Add(("appearance/analytic tapered bulge and post-offset affine transforms", () =>
        {
            var center = StandardEntityProjector.Bulge(new(0, 0), new(10, 0), 1);
            var edge = new WidthBoundaryCurve(center, 2, 4, 1, Affine3.Identity);
            var matrix = Affine3.Move(new(100, -30)) * Affine3.RotateZ(0.3) * Affine3.Scale(new(2, -3, 1));
            var transformed = edge.Transform(matrix);
            for (var i = 0; i <= 20; i++)
            {
                var t = i / 20.0;
                Near(Vec3.Distance(edge.At(t), center.At(t)), 1 + t);
                Near(Vec3.Distance(transformed.At(t), matrix.Point(edge.At(t))), 0);
            }
        }));
        tests.Add(("appearance/hatch active-edge clipping preserves holes", () =>
        {
            var output = HatchPatternGenerator.Generate([Rectangle(0, 0, 10, 10), Rectangle(3, 3, 7, 7)],
                [new(new(0, 0.5), 0, new(0, 1), [])]);
            Require(output.Length == 14);
            Near(output.Sum(c => Vec3.Distance(c.Curves[0].Start, c.Curves[0].End)), 84);
            foreach (var curve in output.SelectMany(c => c.Curves))
            {
                var middle = curve.At(0.5);
                Require(!(middle.X > 3 && middle.X < 7 && middle.Y > 3 && middle.Y < 7));
            }
        }));
        tests.Add(("appearance/signed dashes, leading gaps, dots and negative phases", () =>
        {
            var first = DashPattern.Clip(0, 7, [-2, 1]);
            Require(first.SequenceEqual(new[] { new DashInterval(2, 3), new DashInterval(5, 6) }));
            var negative = DashPattern.Clip(-5, 5, [2, -2]);
            Require(negative.SequenceEqual(new[] { new DashInterval(-4, -2), new DashInterval(0, 2), new DashInterval(4, 5) }));
            var dots = DashPattern.Clip(0, 5, [0, -2]);
            Require(dots.SequenceEqual(new[] { new DashInterval(0, 0), new DashInterval(2, 2), new DashInterval(4, 4) }));
            Throws<ArgumentException>(() => DashPattern.Clip(0, 10, [0, 0]));
            Throws<InvalidOperationException>(() => DashPattern.Clip(0, 1e10, [1, -1], maximumSegments: 10));
        }));
        tests.Add(("appearance/strokes preserve phase through corners and close contours", () =>
        {
            var output = StrokePattern.Apply([new([new(0, 0), new(3, 0), new(3, 4)], false)], [5, -2]);
            Require(output.Length == 1 && output[0].Points.Length == 3);
            Near(output[0].Points[^1].X, 3); Near(output[0].Points[^1].Y, 2);
            var rectangle = new RenderContour([new(0, 0), new(10, 0), new(10, 10), new(0, 10)], true);
            var item = new RenderItem("closed", new("test:closed"), [], CadStyle.Default, [rectangle], false, null, null, new(0, 0, 10, 10));
            Require(RenderHitTesting.Contains(item, new(0, 5), 0.01), "The implicit closing edge must participate in hit testing.");
        }));
        tests.Add(("appearance/hatch row budgets and cancellation", () =>
        {
            Throws<InvalidOperationException>(() => HatchPatternGenerator.Generate([Rectangle(0, 0, 10, 10)],
                [new(Vec3.Zero, 0, new(0, 1e-8), [])], new(MaximumRows: 10)));
            Throws<OperationCanceledException>(() => HatchPatternGenerator.Generate([Rectangle(0, 0, 10, 10)],
                [new(Vec3.Zero, 0, new(0, 1), [])], cancellationToken: new CancellationToken(true)));
        }));
        tests.Add(("appearance/source hatch pattern projection", () =>
        {
            var pattern = new HatchPattern("SHADOW_TEST");
            var line = new HatchPatternLineDefinition { Angle = 0, Origin = new(0, 0.5), Delta = new(0, 1) };
            line.DashPattern.Add(2); line.DashPattern.Add(-2); pattern.LineDefinitions.Add(line);
            var hatch = new Hatch(pattern, [HatchRectangle(0, 0, 10, 10)], false);
            var (_, geometry) = Capture(hatch);
            Require(geometry.Contours.Length == 1 && geometry.DisplayContours.Length > 0);
            Require(geometry.Regions.IsEmpty);
            Require(geometry.DisplayContours.SelectMany(c => c.Curves).All(c => Vec3.Distance(c.Start, c.End) <= 2.000001));
        }));
        tests.Add(("appearance/rational hatch spline boundary", () =>
        {
            var spline = new HatchBoundaryPath.Spline
            {
                Degree = 2, IsRational = true, IsPeriodic = false,
                ControlPoints = [new(1, 0, 1), new(1, 1, Math.Sqrt(0.5)), new(0, 1, 1)],
                Knots = [0, 0, 0, 1, 1, 1]
            };
            var boundary = new HatchBoundaryPath(new HatchBoundaryPath.Edge[]
            {
                spline, new HatchBoundaryPath.Line { Start = new(0, 1), End = new(0, 0) },
                new HatchBoundaryPath.Line { Start = new(0, 0), End = new(1, 0) }
            });
            var (_, geometry) = Capture(new Hatch(HatchPattern.Solid, [boundary], false));
            var curve = geometry.Contours[0].Curves[0];
            Require(curve is NurbsCurve); Near(curve.At(0.5).X, Math.Sqrt(0.5)); Near(curve.At(0.5).Y, Math.Sqrt(0.5));
            Require(geometry.Regions.Length == 1);
        }));
        tests.Add(("appearance/clockwise hatch arcs follow source angle convention", () =>
        {
            var arc = new HatchBoundaryPath.Arc { Center = new(0, 0), Radius = 5, StartAngle = 0, EndAngle = 90, IsCounterclockwise = false };
            var boundary = new HatchBoundaryPath(new HatchBoundaryPath.Edge[]
            {
                arc, new HatchBoundaryPath.Line { Start = new(0, -5), End = new(0, 0) },
                new HatchBoundaryPath.Line { Start = new(0, 0), End = new(5, 0) }
            });
            var (_, geometry) = Capture(new Hatch(HatchPattern.Solid, [boundary], false));
            var curve = geometry.Contours[0].Curves[0]; Near(curve.Start.X, 5); Near(curve.End.Y, -5);
            Require(curve.At(0.5).X > 0 && curve.At(0.5).Y < 0);
        }));
        tests.Add(("appearance/source image shear and rectangular pixel clipping", () =>
        {
            var definition = new ImageDefinition("memory.png", 10, 96, 10, 96, ImageResolutionUnits.Inches);
            var image = new netDxf.Entities.Image(definition, new Vector3(3, 4, 0), 20, 10)
            {
                Uvector = new(1, 0), Vvector = new(0.6, 0.8), Clipping = true,
                ClippingBoundary = new(0, 0, 5, 10)
            };
            var (_, geometry) = Capture(image);
            var data = geometry.Image!;
            Near(data.Corners[3].X, 9); Near(data.Corners[3].Y, 12);
            Require(data.ClipPolygon.Length == 4); Near(data.ClipPolygon[1].X, 13);
            Near(data.ClipPolygon[2].X, 19); Near(data.ClipPolygon[2].Y, 12);
        }));
        tests.Add(("appearance/image clip-aware picking and inverse clips", () =>
        {
            var geometry = ImageGeometry(); var scene = Compile(geometry);
            Require(RenderCompiler.HitTest(scene, new(2, 5), 0.01) is not null);
            Require(RenderCompiler.HitTest(scene, new(8, 5), 0.01) is null);
            var inverse = Compile(geometry with { Image = geometry.Image! with { ClipOutside = true } });
            Require(RenderCompiler.HitTest(inverse, new(2, 5), 0.01) is null);
            Require(RenderCompiler.HitTest(inverse, new(8, 5), 0.01) is not null);
            var rotated = Compile(geometry.Transform(Affine3.RotateZ(Math.PI / 4)));
            Require(RenderCompiler.HitTest(rotated, new(-6.8, 0.2), 0.01) is null, "Image bounding-box corners are not image pixels.");
        }));
        tests.Add(("appearance/nonzero regions union overlapping ribbons", () =>
        {
            var loops = ImmutableArray.Create(Rectangle(0, 0, 10, 10), Rectangle(3, 3, 7, 7));
            var solid = Compile(new([]) { Regions = [new(loops, CadFillRule.NonZero)] });
            var hole = Compile(new([]) { Regions = [new(loops, CadFillRule.EvenOdd)] });
            Require(RenderCompiler.HitTest(solid, new(5, 5), 0.01) is not null);
            Require(RenderCompiler.HitTest(hole, new(5, 5), 0.01) is null);
        }));
        tests.Add(("appearance/Skia image clipping and live resolver invalidation", () =>
        {
            using var redBitmap = new SKBitmap(4, 4); redBitmap.Erase(SKColors.Red);
            using var greenBitmap = new SKBitmap(4, 4); greenBitmap.Erase(SKColors.Green);
            using var red = SKImage.FromBitmap(redBitmap); using var green = SKImage.FromBitmap(greenBitmap);
            var resolver = new ImageResolver(red);
            using var backend = new SkiaRenderBackend(resolver);
            var frame = Frame(Compile(ImageGeometry()));
            using (var first = SKBitmap.Decode(backend.Render(frame).Bytes))
            {
                Require(first.GetPixel(20, 50).Red > 240); Require(first.GetPixel(80, 50).Red < 10);
            }
            resolver.Value = green;
            using (var next = SKBitmap.Decode(backend.Render(frame).Bytes)) Require(next.GetPixel(20, 50).Green > 100);
            var inverse = ImageGeometry(); inverse = inverse with { Image = inverse.Image! with { ClipOutside = true } };
            using var inverted = SKBitmap.Decode(backend.Render(Frame(Compile(inverse))).Bytes);
            Require(inverted.GetPixel(20, 50).Green < 10 && inverted.GetPixel(80, 50).Green > 100);
        }));
        tests.Add(("appearance/all gradient display plans render on both backends", () =>
        {
            using var skia = new SkiaRenderBackend(); using var svg = new SvgRenderBackend();
            foreach (var kind in Enum.GetNames<HatchGradientPatternType>())
            {
                var geometry = new CadGeometry([])
                {
                    Regions = [new([Rectangle(0, 0, 10, 10)], Gradient: new(kind, 0xffff0000, 0xff0000ff,
                        Affine3.Scale(new(10, 10, 1)), 0.25))]
                };
                var frame = Frame(Compile(geometry));
                var png = skia.Render(frame); Require(png.Bytes.Length > 100);
                var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(frame).Bytes));
                Require(xml.Descendants().Any(e => e.Name.LocalName.EndsWith("Gradient", StringComparison.Ordinal)));
            }
        }));
        tests.Add(("appearance/source gradient keeps authored RGB stops and transform", () =>
        {
            var pattern = new HatchGradientPattern(new AciColor((byte)255, (byte)0, (byte)0),
                new AciColor((byte)0, (byte)0, (byte)255), HatchGradientPatternType.Linear) { Angle = 30, Shift = 0.3 };
            var (_, geometry) = Capture(new Hatch(pattern, [HatchRectangle(0, 0, 10, 10)], false));
            var gradient = geometry.Regions.Single().Gradient!;
            Require(gradient.Color1 == 0xffff0000 && gradient.Color2 == 0xff0000ff); Near(gradient.Shift, 0.3);
            var transform = Affine3.Move(new(7, -8)) * Affine3.Scale(new(-2, 3, 1));
            var moved = geometry.Transform(transform).Regions.Single().Gradient!;
            Near(Vec3.Distance(moved.Frame.Point(new(0.3, 0.7)), transform.Point(gradient.Frame.Point(new(0.3, 0.7)))), 0);
        }));
        tests.Add(("appearance/SVG trusted raster embedding and active-content rejection", () =>
        {
            using var bitmap = new SKBitmap(2, 2); bitmap.Erase(SKColors.Red);
            using var image = SKImage.FromBitmap(bitmap); using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            var frame = Frame(Compile(ImageGeometry()));
            using var svg = new SvgRenderBackend(new SvgResolver(new("image/png", png.ToArray())));
            var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(frame).Bytes));
            Require(xml.Descendants().Any(e => e.Name.LocalName == "clipPath"));
            var href = xml.Descendants().Single(e => e.Name.LocalName == "image").Attribute("href")!.Value;
            Require(href.StartsWith("data:image/png;base64,", StringComparison.Ordinal));
            using var unsafeSvg = new SvgRenderBackend(new SvgResolver(new("image/svg+xml", Encoding.UTF8.GetBytes("<svg><script/></svg>"))));
            Throws<ArgumentException>(() => unsafeSvg.Render(frame));
        }));
        tests.Add(("appearance/backend width fill output", () =>
        {
            var (_, geometry) = Capture(new Polyline2D([new Polyline2DVertex(0, 5) { StartWidth = 2, EndWidth = 2 }, new Polyline2DVertex(10, 5)]));
            var frame = Frame(Compile(geometry));
            using var skia = new SkiaRenderBackend(); using var bitmap = SKBitmap.Decode(skia.Render(frame).Bytes);
            Require(bitmap.GetPixel(50, 45).Red > 100 && bitmap.GetPixel(50, 20).Red < 10);
            using var svg = new SvgRenderBackend();
            var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Render(frame).Bytes));
            Require(xml.Descendants().Any(e => e.Attribute("fill-rule")?.Value == "nonzero"));
        }));
    }

    private static (ShadowSession Session, CadGeometry Geometry) Capture(EntityObject entity)
    {
        var document = new DxfDocument(); document.Entities.Add(entity); var session = new ShadowSession(document);
        Require(new StandardEntityProjector().TryProject(session.Current.Read(Id(entity)), out var geometry, out _));
        return (session, geometry);
    }
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static CadContour Rectangle(double x0, double y0, double x1, double y1) => new([
        new LineCurve(new(x0, y0), new(x1, y0)), new LineCurve(new(x1, y0), new(x1, y1)),
        new LineCurve(new(x1, y1), new(x0, y1)), new LineCurve(new(x0, y1), new(x0, y0))], true);
    private static HatchBoundaryPath HatchRectangle(double x0, double y0, double x1, double y1)
        => new(new HatchBoundaryPath.Edge[]
        {
            new HatchBoundaryPath.Line { Start = new(x0, y0), End = new(x1, y0) },
            new HatchBoundaryPath.Line { Start = new(x1, y0), End = new(x1, y1) },
            new HatchBoundaryPath.Line { Start = new(x1, y1), End = new(x0, y1) },
            new HatchBoundaryPath.Line { Start = new(x0, y1), End = new(x0, y0) }
        });
    private static CadGeometry ImageGeometry() => new([], Image: new("host-owned", [new(0, 0), new(10, 0), new(10, 10), new(0, 10)])
        { ClipPolygon = [new(0, 0), new(5, 0), new(5, 10), new(0, 10)] });
    private static RenderScene Compile(CadGeometry geometry) => RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model",
        [new("test", new("test:1"), [], CadStyle.Default, geometry)], []));
    private static RenderFrame Frame(RenderScene scene) => new(scene, new Camera(new(5, 5), 10, 100, 100)) { Background = 0xff000000 };
    private sealed class ImageResolver(SKImage image) : ISkiaImageResolver
    {
        public SKImage Value { get; set; } = image;
        public SKImage? Resolve(string reference) => Value;
    }
    private sealed class SvgResolver(SvgImageData data) : ISvgImageResolver
    {
        public SvgImageData? Resolve(string reference) => data;
    }
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected, double tolerance = 1e-7)
        => Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= tolerance, $"Expected {expected:R}, got {actual:R}");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
