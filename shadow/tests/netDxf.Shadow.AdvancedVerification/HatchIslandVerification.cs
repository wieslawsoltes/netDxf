using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Studio;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class HatchIslandVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        Add("containment forest retains analytic boundaries", () =>
        {
            var input = Nested(); var tree = HatchIslandResolver.Analyze(input);
            Require(tree.Loops.Select(l => l.Depth).SequenceEqual(new[] { 0, 1, 2, 3 }));
            Require(tree.Loops.Select(l => l.ParentIndex).SequenceEqual(new[] { -1, 0, 1, 2 }));
            Require(tree.Loops.Select(l => l.SourceIndex).SequenceEqual(new[] { 0, 1, 2, 3 }));
            Require(tree.SampledPointCount == 16);
            Require(tree.Select(CadHatchIslandStyle.Normal) == input);
            Require(tree.Select(CadHatchIslandStyle.Outer).Length == 2);
            Require(ReferenceEquals(tree.Select(CadHatchIslandStyle.Ignore)[0], input[0]));
        });
        foreach (var order in new[] { new[] { 3, 1, 0, 2 }, new[] { 2, 0, 3, 1 }, new[] { 3, 2, 1, 0 } })
            Add("unordered forest " + string.Join('-', order), () =>
            {
                var original = Nested(); var input = order.Select(i => original[i]).ToImmutableArray();
                var tree = HatchIslandResolver.Analyze(input);
                for (var i = 0; i < order.Length; i++)
                {
                    Require(tree.Loops[i].Depth == order[i]);
                    Require(tree.Loops[i].ParentIndex == (order[i] == 0 ? -1 : Array.IndexOf(order, order[i] - 1)));
                }
                Require(tree.Select(CadHatchIslandStyle.Outer).SequenceEqual(input.Where((_, i) => order[i] <= 1)));
            });
        foreach (var mask in new[] { 0, 1, 5, 10, 15 })
            Add("winding-independent mask " + mask, () =>
            {
                var input = Nested().Select((c, i) => (mask & (1 << i)) != 0 ? Reverse(c) : c).ToImmutableArray();
                Require(HatchIslandResolver.Analyze(input).Loops.Select(l => l.Depth).SequenceEqual(new[] { 0, 1, 2, 3 }));
            });
        Add("disjoint roots preserve their immediate holes", () =>
        {
            ImmutableArray<CadContour> input = [Rectangle(20, 0, 30, 10), Rectangle(22, 2, 28, 8), .. Nested()];
            var tree = HatchIslandResolver.Analyze(input);
            Require(tree.Loops.Select(l => l.Depth).SequenceEqual(new[] { 0, 1, 0, 1, 2, 3 }));
            Require(tree.Select(CadHatchIslandStyle.Outer).Length == 4);
            Require(tree.Select(CadHatchIslandStyle.Ignore).Length == 2);
        });
        Add("concave containment uses a boundary witness rather than centroid", () =>
        {
            // Two nested U shapes have their average vertex outside their filled interiors.
            var outer = Polygon(new(0, 0), new(10, 0), new(10, 10), new(7, 10), new(7, 3), new(3, 3), new(3, 10), new(0, 10));
            var child = Polygon(new(.5, .5), new(9.5, .5), new(9.5, 9), new(8, 9), new(8, 2), new(2, 2), new(2, 9), new(.5, 9));
            var notch = Rectangle(4, 4, 6, 6);
            var tree = HatchIslandResolver.Analyze([child, notch, outer]);
            Require(tree.Loops[0].ParentIndex == 2 && tree.Loops[1].ParentIndex == -1);
        });
        Add("analytic circles survive classification unsampled", () =>
        {
            var a = Circle(10); var b = Circle(6); var c = Circle(2);
            var tree = HatchIslandResolver.Analyze([c, a, b]);
            Require(tree.Loops.Select(l => l.Depth).SequenceEqual(new[] { 2, 0, 1 }));
            var output = tree.Select(CadHatchIslandStyle.Outer);
            Require(output.Length == 2 && ReferenceEquals(output[0], a) && ReferenceEquals(output[1], b));
            Require(output.All(loop => loop.Curves[0] is EllipseCurve));
        });
        Add("closed linear NURBS boundary participates in nesting", () =>
        {
            var curve = new NurbsCurve([new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)],
                [], [0, 0, 1, 2, 3, 4, 4], 1, true);
            var outer = new CadContour([curve], true);
            var tree = HatchIslandResolver.Analyze([Rectangle(2, 2, 8, 8), outer]);
            Require(tree.Loops[0].Depth == 1 && ReferenceEquals(tree.Select(CadHatchIslandStyle.Ignore)[0], outer));
        });
        Add("neighbouring curve endpoints use only the contact tolerance", () =>
        {
            var first = new LineCurve(new(0, 0), new(10, 0));
            var second = new LineCurve(new(10, 5e-10), new(10, 10));
            var loop = new CadContour([first, second, new LineCurve(new(10, 10), new(0, 10)), new LineCurve(new(0, 10), new(0, 0))], true);
            var tree = HatchIslandResolver.Analyze([loop, Rectangle(2, 2, 8, 8)]);
            Require(tree.Loops[1].Depth == 1 && ReferenceEquals(tree.Select(CadHatchIslandStyle.Ignore)[0], loop));
            Require(second.A.Y == 5e-10, "Classification must not mutate original curve data.");
            Throws<ArgumentException>(() => HatchIslandResolver.Analyze([loop], new(ContactTolerance: 1e-11)));
        });
        Add("large translated coordinates retain containment", () =>
        {
            var move = Affine3.Move(new(1e12, -1e12));
            var input = Transform(Nested(), move);
            Require(HatchIslandResolver.Analyze(input).Loops.Select(l => l.Depth).SequenceEqual(new[] { 0, 1, 2, 3 }));
        });
        Add("affine mirror and shear retain topology", () =>
        {
            var transform = new Affine3(new(-2, 1), new(.5, 3), Vec3.UnitZ, new(25, 40));
            Require(HatchIslandResolver.Analyze(Transform(Nested(), transform)).Loops.Select(l => l.Depth).SequenceEqual(new[] { 0, 1, 2, 3 }));
        });
        Add("normal style avoids simple-loop restrictions and retains array", () =>
        {
            ImmutableArray<CadContour> input = [Rectangle(0, 0, 4, 4), Rectangle(2, 2, 6, 6)];
            Require(HatchIslandResolver.Select(input, CadHatchIslandStyle.Normal) == input);
        });
        Add("empty forest", () => Require(HatchIslandResolver.Analyze([]).Select(CadHatchIslandStyle.Ignore).IsEmpty));
        Add("invalid style", () => Throws<ArgumentOutOfRangeException>(() => HatchIslandResolver.Select([], (CadHatchIslandStyle)999)));
        Add("invalid topology selection style", () => Throws<ArgumentOutOfRangeException>(() => HatchIslandResolver.Analyze([]).Select((CadHatchIslandStyle)(-1))));
        Add("uninitialized boundaries", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze(default)));
        Add("nonfinite tolerance", () => Throws<ArgumentOutOfRangeException>(() => HatchIslandResolver.Analyze([], new(BoundaryTolerance: double.NaN))));
        Add("nonpositive contact tolerance", () => Throws<ArgumentOutOfRangeException>(() => HatchIslandResolver.Analyze([], new(ContactTolerance: 0))));
        Add("contour budget", () => Throws<InvalidOperationException>(() => HatchIslandResolver.Analyze(Nested(), new(MaximumContours: 3))));
        Add("point budget", () => Throws<InvalidOperationException>(() => HatchIslandResolver.Analyze(Nested(), new(MaximumBoundaryPoints: 8))));
        Add("segment comparison budget", () => Throws<InvalidOperationException>(() => HatchIslandResolver.Analyze(Nested(), new(MaximumSegmentComparisons: 1))));
        Add("containment visit budget", () => Throws<InvalidOperationException>(() => HatchIslandResolver.Analyze(Nested(), new(MaximumContainmentEdgeVisits: 1))));
        Add("cancellation", () => Throws<OperationCanceledException>(() => HatchIslandResolver.Analyze(Nested(), cancellationToken: new(true))));
        Add("normal cancellation", () => Throws<OperationCanceledException>(() => HatchIslandResolver.Select([], CadHatchIslandStyle.Normal, cancellationToken: new(true))));
        Add("open contour", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4) with { Closed = false }])));
        Add("disconnected curve chain", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([new([new LineCurve(new(0, 0), new(1, 0)), new LineCurve(new(2, 2), new(0, 0))], true)])));
        Add("implicit missing closing edge refused", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([new([new LineCurve(new(0, 0), new(1, 0)), new LineCurve(new(1, 0), new(1, 1))], true)])));
        Add("nonfinite boundary", () => Throws<ArithmeticException>(() => HatchIslandResolver.Analyze([Polygon(new(0, 0), new(double.NaN, 0), new(1, 1))])));
        Add("zero-area contour", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Polygon(new(0, 0), new(1, 0), new(2, 0))])));
        Add("self crossing contour", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Polygon(new(0, 0), new(5, 4), new(0, 4), new(4, 0))])));
        Add("adjacent collinear reversal", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Polygon(new(0, 0), new(4, 0), new(2, 0), new(4, 4), new(0, 4))])));
        Add("collinear forward continuation allowed", () => Require(HatchIslandResolver.Analyze([Polygon(new(0, 0), new(2, 0), new(4, 0), new(4, 4), new(0, 4))]).Loops.Length == 1));
        Add("crossing loops", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Rectangle(2, 2, 6, 6)])));
        Add("coincident loops", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Reverse(Rectangle(0, 0, 4, 4))])));
        Add("shared edge", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Rectangle(4, 0, 8, 4)])));
        Add("vertex contact", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Rectangle(4, 4, 8, 8)])));
        Add("unresolved narrow separation", () => Throws<ArgumentException>(() => HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Rectangle(4 + 1e-10, 0, 8, 4)])));
        Add("resolved narrow separation", () => Require(HatchIslandResolver.Analyze([Rectangle(0, 0, 4, 4), Rectangle(4 + 1e-6, 0, 8, 4)]).Loops.All(l => l.Depth == 0)));
        Add("ten thousand disjoint loops avoid quadratic predicates", () =>
        {
            var boundaries = Enumerable.Range(0, 10_000).Select(i => Rectangle((i % 100) * 3, (i / 100) * 3, (i % 100) * 3 + 1, (i / 100) * 3 + 1)).ToImmutableArray();
            var tree = HatchIslandResolver.Analyze(boundaries);
            Require(tree.Loops.All(l => l.Depth == 0) && tree.SampledPointCount == 40_000);
            Require(tree.SegmentComparisons == 40_000 && tree.ContainmentEdgeVisits == 0,
                $"Unexpected broad phase: {tree.SegmentComparisons} segment predicates, {tree.ContainmentEdgeVisits} containment visits.");
        });
        Add("deep nesting is iterative", () =>
        {
            var boundaries = Enumerable.Range(0, 400).Select(i => Rectangle(i, i, 1000 - i, 1000 - i)).Reverse().ToImmutableArray();
            var tree = HatchIslandResolver.Analyze(boundaries);
            Require(tree.Loops[0].Depth == 399 && tree.Select(CadHatchIslandStyle.Outer).Length == 2);
        });
        foreach (var style in Enum.GetValues<HatchStyle>())
        foreach (var paint in new[] { "solid", "pattern", "gradient" })
        {
            Add($"source/{style}/{paint} geometry and picking", () =>
            {
                var hatch = Hatch(style, paint); var (source, geometry, _) = Capture(hatch);
                Require(geometry.Contours.Length == 4);
                if (paint != "pattern") Require(geometry.Regions.Single().Contours.Length == SelectedCount(style));
                var scene = new RenderCompiler().Compile(source.Current);
                foreach (var (point, depth) in Witnesses())
                    Require((RenderCompiler.HitTest(scene, point, .02) is not null) == Painted(style, depth), $"Wrong {style}/{paint} hit at {point}.");
                Require(RenderCompiler.HitTest(scene, new(11, 5.5), .02) is null);
                var graph = new TopologyBuilder().Build(source.Current);
                Require(graph.Edges.Length == 16 && graph.ConnectedComponents().Length == 4 && graph.CycleRank == 4);
            });
            Add($"native/{style}/{paint} shares fill decision", () =>
            {
                var (source, _, _) = Capture(Hatch(style, paint));
                var frame = Frame(new RenderCompiler().Compile(source.Current));
                using var backend = new SkiaRenderBackend(); using var bitmap = SKBitmap.Decode(backend.Render(frame).Bytes);
                foreach (var (point, depth) in Witnesses())
                {
                    var screen = frame.Viewport.ToScreen(point); var ink = false;
                    for (var dy = -1; dy <= 1; dy++) for (var dx = -1; dx <= 1; dx++)
                    {
                        var c = bitmap.GetPixel((int)Math.Round(screen.X) + dx, (int)Math.Round(screen.Y) + dy);
                        ink |= Math.Max(c.Red, Math.Max(c.Green, c.Blue)) > 35;
                    }
                    Require(ink == Painted(style, depth), $"Wrong {style}/{paint} raster at {point}.");
                }
            });
        }
        foreach (var style in Enum.GetValues<HatchStyle>())
        {
            Add($"SVG/{style} selected subpaths", () =>
            {
                var (source, _, _) = Capture(Hatch(style, "solid"));
                using var backend = new SvgRenderBackend();
                var xml = XDocument.Parse(Encoding.UTF8.GetString(backend.Render(Frame(new RenderCompiler().Compile(source.Current))).Bytes));
                var paths = xml.Descendants().Where(e => e.Name.LocalName == "path" && e.Attribute("fill-rule")?.Value == "evenodd").ToArray();
                Require(paths.Length == 1);
                Require(paths[0].Attribute("d")!.Value.Count(c => c == 'M') == SelectedCount(style));
            });
            Add($"DXF/{style} roundtrip", () =>
            {
                var document = new DxfDocument(); document.Entities.Add(Hatch(style, "solid"));
                using var stream = new MemoryStream(); Require(document.Save(stream)); stream.Position = 0;
                var reloaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("DXF load failed.");
                Require(reloaded.Entities.Hatches.Single().Pattern.Style == style);
                var scene = new RenderCompiler().Compile(new ShadowSession(reloaded).Current);
                foreach (var (point, depth) in Witnesses()) Require((RenderCompiler.HitTest(scene, point, .02) is not null) == Painted(style, depth));
            });
        }
        Add("source style edits invalidate only dependent geometry and preserve undo", () =>
        {
            var document = new DxfDocument(); var hatch = Hatch(HatchStyle.Normal, "solid");
            var line = new Line(new Vector2(20, 0), new Vector2(25, 0));
            document.Entities.Add(hatch); document.Entities.Add(line);
            var source = new ShadowSession(document); var retained = source.Current; var editor = new EditorSession(source);
            var pattern = retained.Read(Id(hatch))["Pattern"].Id!.Value;
            var compiler = new RenderCompiler(); var before = compiler.Compile(retained); var stats = compiler.CacheStatistics;
            editor.SetProperty(pattern, "Style", new ShadowValue.Atom(typeof(HatchStyle).FullName!, "Outer"));
            var after = compiler.Compile(source.Current);
            Require(compiler.CacheStatistics.GeometryCompilations - stats.GeometryCompilations == 1);
            Require(ReferenceEquals(before.Items.Single(i => i.Entity == Id(line)), after.Items.Single(i => i.Entity == Id(line))));
            Require(before.Items.Single(i => i.Entity == Id(hatch)).Regions[0].Contours.Length == 4);
            Require(after.Items.Single(i => i.Entity == Id(hatch)).Regions[0].Contours.Length == 2);
            Require(retained.Read(pattern)["Style"].Text == "Normal" && hatch.Pattern.Style == HatchStyle.Outer);
            editor.Undo(); Require(compiler.Compile(source.Current).Items.Single(i => i.Entity == Id(hatch)).Regions[0].Contours.Length == 4);
            editor.Redo(); Require(compiler.Compile(source.Current).Items.Single(i => i.Entity == Id(hatch)).Regions[0].Contours.Length == 2);
        });
        Add("source refresh detects an externally changed style", () =>
        {
            var hatch = Hatch(HatchStyle.Normal, "solid"); var (source, _, _) = Capture(hatch);
            var compiler = new RenderCompiler(); var before = compiler.Compile(source.Current);
            source.MutateSource(_ => hatch.Pattern.Style = HatchStyle.Ignore);
            var after = compiler.Compile(source.Current);
            Require(before.Items[0].Regions[0].Contours.Length == 4 && after.Items[0].Regions[0].Contours.Length == 1);
        });
        Add("unknown source style is diagnosed without changing the live enum", () =>
        {
            var hatch = Hatch((HatchStyle)123, "solid");
            var (_, geometry, diagnostic) = Capture(hatch);
            Require(hatch.Pattern.Style == (HatchStyle)123 && geometry.Contours.Length == 4);
            Require(geometry.Regions.IsEmpty && diagnostic?.Contains("Unknown hatch island style", StringComparison.Ordinal) == true);
        });
        Add("style clone is detached", () =>
        {
            var original = Hatch(HatchStyle.Ignore, "pattern"); var clone = (Hatch)original.Clone();
            Require(clone.Pattern.Style == HatchStyle.Ignore && !ReferenceEquals(original.Pattern, clone.Pattern));
            clone.Pattern.Style = HatchStyle.Outer; Require(original.Pattern.Style == HatchStyle.Ignore);
        });
        Add("source ambiguity retains semantic contours without fake paint", () =>
        {
            var pattern = HatchPattern.Solid; pattern.Style = HatchStyle.Outer;
            var (source, geometry, diagnostic) = Capture(new Hatch(pattern, [Boundary(0, 0, 4, 4), Boundary(2, 2, 6, 6)], false));
            Require(geometry.Contours.Length == 2 && geometry.DisplayContours.IsEmpty && geometry.Regions.IsEmpty);
            Require(diagnostic?.Contains("island presentation was not generated", StringComparison.Ordinal) == true);
            var scene = new RenderCompiler().Compile(source.Current);
            Require(RenderCompiler.HitTest(scene, new(1, 1), .02) is null);
            Require(scene.Diagnostics.Any(d => d.Message.Contains("ambiguous", StringComparison.Ordinal)));
        });
        Add("OCS elevation and mirror preserve selected presentation", () =>
        {
            var hatch = Hatch(HatchStyle.Outer, "solid"); hatch.Normal = new Vector3(0, 0, -1); hatch.Elevation = 7;
            var (_, geometry, _) = Capture(hatch);
            Require(geometry.Contours.Length == 4 && geometry.Regions[0].Contours.Length == 2);
            var transform = Affine3.ObjectCoordinates(new(0, 0, -1)) * Affine3.Move(new(0, 0, 7));
            var scene = RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model", [new("hatch", new("hatch:1"), [], CadStyle.Default, geometry)], []));
            foreach (var (point, depth) in Witnesses()) Require((RenderCompiler.HitTest(scene, transform.Point(point), .02) is not null) == Painted(HatchStyle.Outer, depth));
            Require(geometry.Contours.SelectMany(c => c.Curves).All(c => Math.Abs(c.Start.Z + 7) < 1e-8));
        });

        Add("Studio gallery renders retains and reloads", () =>
        {
            using var studio = new Studio();
            JsonElement State() => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var initial = State(); var revision = initial.GetProperty("revision").GetInt64();
            Require(initial.GetProperty("layouts").EnumerateArray().Any(l => l.GetString() == "Hatch islands"));
            studio.SetLayout(new(revision, "Hatch islands"));
            var state = State(); var count = state.GetProperty("occurrences").GetInt32();
            Require(count >= 20);
            Require(state.GetProperty("diagnostics").EnumerateArray().All(d => d.GetProperty("severity").GetString() != "Error"));
            var png = studio.Render(1300, 1050, null, null, null, "skia", null, default);
            var svg = studio.Render(1300, 1050, null, null, null, "svg", null, default);
            Require(png.Bytes.Length > 1000); _ = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
            studio.Retain(revision); studio.Add(new(revision, "line", 280, 160));
            Require(State().GetProperty("occurrences").GetInt32() == count + 1);
            studio.History("undo", State().GetProperty("revision").GetInt64());
            Require(State().GetProperty("occurrences").GetInt32() == count);
            Require(studio.Render(1300, 1050, null, null, null, "skia", revision, default).Bytes.SequenceEqual(png.Bytes));
            var dxf = studio.Save(); studio.Load(new MemoryStream(dxf));
            studio.SetLayout(new(State().GetProperty("revision").GetInt64(), "Hatch islands"));
            Require(State().GetProperty("occurrences").GetInt32() == count);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/hatch-islands-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/hatch-islands-gallery.svg", svg.Bytes);
            File.WriteAllBytes("artifacts/hatch-islands-gallery.dxf", dxf);
        });

        void Add(string name, Action action) => tests.Add(("hatch-islands/" + name, action));
    }

    private static ImmutableArray<CadContour> Nested() => [Rectangle(0, 0, 10, 10), Rectangle(2, 2, 8, 8), Rectangle(3, 3, 7, 7), Rectangle(4, 4, 6, 6)];
    private static CadContour Rectangle(double x0, double y0, double x1, double y1) => Polygon(new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1));
    private static CadContour Polygon(params Vec3[] points) => new(points.Select((p, i) => (Curve3)new LineCurve(p, points[(i + 1) % points.Length])).ToImmutableArray(), true);
    private static CadContour Circle(double radius) => new([new EllipseCurve(Vec3.Zero, Vec3.UnitX * radius, Vec3.UnitY * radius, 0, Math.Tau)], true);
    private static CadContour Reverse(CadContour contour) => contour with { Curves = contour.Curves.Reverse().Select(c => (Curve3)new ReversedCurve(c)).ToImmutableArray() };
    private static ImmutableArray<CadContour> Transform(ImmutableArray<CadContour> contours, Affine3 transform) => contours.Select(c => c with { Curves = c.Curves.Select(curve => curve.Transform(transform)).ToImmutableArray() }).ToImmutableArray();
    private static IEnumerable<(Vec3 Point, int Depth)> Witnesses() => new[] { (new Vec3(1, 5.5), 0), (new Vec3(2.5, 5.5), 1), (new Vec3(3.5, 5.5), 2), (new Vec3(5, 5.5), 3), (new Vec3(9, 5.5), 0) };
    private static bool Painted(HatchStyle style, int depth) => style == HatchStyle.Ignore || style == HatchStyle.Outer && depth == 0 || style == HatchStyle.Normal && depth % 2 == 0;
    private static int SelectedCount(HatchStyle style) => style == HatchStyle.Normal ? 4 : style == HatchStyle.Outer ? 2 : 1;
    private static Hatch Hatch(HatchStyle style, string paint)
    {
        HatchPattern pattern = paint switch
        {
            "solid" => HatchPattern.Solid,
            "gradient" => new HatchGradientPattern(new AciColor((byte)255, (byte)40, (byte)40), new AciColor((byte)40, (byte)40, (byte)255), HatchGradientPatternType.Linear),
            _ => new HatchPattern("SHADOW_ISLAND_TEST")
        };
        pattern.Style = style;
        if (paint == "pattern") pattern.LineDefinitions.Add(new HatchPatternLineDefinition { Origin = new(0, .5), Delta = new(0, 1) });
        return new Hatch(pattern, [Boundary(0, 0, 10, 10), Boundary(2, 2, 8, 8), Boundary(3, 3, 7, 7), Boundary(4, 4, 6, 6)], false);
    }
    private static HatchBoundaryPath Boundary(double x0, double y0, double x1, double y1) => new(new HatchBoundaryPath.Edge[]
    {
        new HatchBoundaryPath.Line { Start = new(x0, y0), End = new(x1, y0) },
        new HatchBoundaryPath.Line { Start = new(x1, y0), End = new(x1, y1) },
        new HatchBoundaryPath.Line { Start = new(x1, y1), End = new(x0, y1) },
        new HatchBoundaryPath.Line { Start = new(x0, y1), End = new(x0, y0) }
    });
    private static (ShadowSession Source, CadGeometry Geometry, string? Diagnostic) Capture(EntityObject entity)
    {
        var document = new DxfDocument(); document.Entities.Add(entity); var source = new ShadowSession(document);
        Require(new StandardEntityProjector().TryProject(source.Current.Read(Id(entity)), out var geometry, out var diagnostic));
        return (source, geometry, diagnostic);
    }
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static RenderFrame Frame(RenderScene scene) => new(scene, new Camera(new(5, 5), 10, 100, 100)) { Background = 0xff000000 };
    private static void Require(bool condition, string message = "Hatch island contract failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
