using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

internal static class LinetypePlannerVerification
{
    private static readonly CadLinetypeDecoration Text = new(CadLinetypeDecorationKind.Text, "A", "");
    private static CadLinetype Pattern(CadLinetypeDecoration? decoration = null, double scale = 1)
        => new([new(2), new(-1, decoration ?? Text), new(-1)], scale, Vec3.UnitZ);
    private static ImmutableArray<RenderContour> Line(double length = 10) => [new([Vec3.Zero, new(length, 0)], false)];

    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-plan/" + name, run));
        Test("anchors follow the preceding dash and gap", () =>
        {
            var plan = LinetypePlanner.Plan(Line(), Pattern());
            Check(plan.Select(p => p.Distance).SequenceEqual(new[] { 3.0, 7.0 }));
            Check(plan.All(p => p.ElementIndex == 1 && p.ContourIndex == 0));
            Vector(plan[0].Frame.Point(Vec3.Zero), new(3, 0)); Vector(plan[1].Frame.X, Vec3.UnitX);
        });
        Test("entity scale is independent of glyph scale and offset", () =>
        {
            var plan = LinetypePlanner.Plan(Line(20), Pattern(Text with { Scale = 3, Offset = new(0.5, 1) }, 2));
            Check(plan.Length == 2); Near(plan[0].Distance, 6);
            Vector(plan[0].Frame.Point(Vec3.Zero), new(7, 2)); Near(plan[0].Frame.X.Length, 6);
        });
        Test("fixed text style height multiplies segment scale", () =>
        {
            var plan = LinetypePlanner.Plan(Line(), Pattern(Text with { StyleHeight = 4, Scale = 0.5 }));
            Near(plan[0].Frame.X.Length, 2); Near(plan[0].Frame.Y.Length, 2);
        });
        Test("phase survives duplicate points and corners", () =>
        {
            var plan = LinetypePlanner.Plan([new([Vec3.Zero, Vec3.Zero, new(3, 0), new(3, 4)], false)], Pattern());
            Check(plan.Length == 2); Vector(plan[0].Frame.Point(Vec3.Zero), new(3, 0));
            Vector(plan[0].Frame.X, Vec3.UnitX); Vector(plan[1].Frame.X, Vec3.UnitY);
            Vector(plan[1].Frame.Point(Vec3.Zero), new(3, 4));
        });
        Test("closed seam emits once with implicit closing edge", () =>
        {
            var plan = LinetypePlanner.Plan([new([Vec3.Zero, new(10, 0), new(10, 10), new(0, 10)], true)],
                new([new(10, Text)], 1, Vec3.UnitZ));
            Check(plan.Select(p => p.Distance).SequenceEqual(new[] { 0.0, 10.0, 20.0, 30.0 }));
        });
        Test("explicit and implicit closure agree", () =>
        {
            var implicitPath = new RenderContour([Vec3.Zero, new(10, 0), new(10, 10), new(0, 10)], true);
            var explicitPath = implicitPath with { Points = implicitPath.Points.Add(Vec3.Zero) };
            var pattern = new CadLinetype([new(5, Text)], 1, Vec3.UnitZ);
            Check(LinetypePlanner.Plan([implicitPath], pattern).SequenceEqual(LinetypePlanner.Plan([explicitPath], pattern)));
        });
        foreach (var mode in Enum.GetValues<CadLinetypeRotation>())
            Test($"reversed path/{mode}", () =>
            {
                var plan = LinetypePlanner.Plan([new([new(10, 0), Vec3.Zero], false)], Pattern(Text with { RotationType = mode, Offset = new(0, 2) }));
                Vector(plan[0].Frame.X, mode == CadLinetypeRotation.Relative ? new(-1, 0) : Vec3.UnitX);
                Vector(plan[0].Frame.Point(Vec3.Zero), new(7, -2));
            });
        Test("authored rotation changes axes not tangent-relative offsets", () =>
        {
            var placement = LinetypePlanner.Plan(Line(), Pattern(Text with { Rotation = 90, Offset = new(2, 1) }))[0];
            Vector(placement.Frame.X, Vec3.UnitY); Vector(placement.Frame.Y, new(-1, 0));
            Vector(placement.Frame.Point(Vec3.Zero), new(5, 1));
        });
        Test("absolute rotation ignores path direction", () =>
        {
            var plan = LinetypePlanner.Plan([new([Vec3.Zero, new(0, 10)], false)], Pattern(Text with { RotationType = CadLinetypeRotation.Absolute }));
            Vector(plan[0].Frame.X, Vec3.UnitX); Vector(plan[0].Frame.Point(Vec3.Zero), new(0, 3));
        });
        Test("insert transforms the entity plane normal", () =>
        {
            var frame = new Affine3(Vec3.UnitX, Vec3.UnitZ, new(0, -1, 0), Vec3.Zero);
            var placement = LinetypePlanner.Plan(Line(), Pattern(), frame)[0];
            Vector(placement.Frame.X, Vec3.UnitX); Vector(placement.Frame.Y, Vec3.UnitZ);
        });
        Test("independent contours restart phase", () =>
        {
            var plan = LinetypePlanner.Plan([Line(5)[0], new([new(20, 0), new(25, 0)], false)], Pattern());
            Check(plan.Length == 2 && plan[1].ContourIndex == 1); Near(plan[1].Distance, 3);
        });
        Test("zero length geometry does not invent a tangent", () =>
        {
            Check(LinetypePlanner.Plan([new([Vec3.Zero, Vec3.Zero], false)], Pattern()).IsEmpty);
        });
        Test("zero length decorations coexist at a shared anchor", () =>
        {
            var plan = LinetypePlanner.Plan(Line(5), new([new(2, Text), new(0, Text with { Content = "B" }), new(-2)], 1, Vec3.UnitZ));
            Check(plan.Length == 2 && plan[0].Distance == 2 && plan[1].Distance == 2 && plan[0].ElementIndex != plan[1].ElementIndex);
        });
        Test("empty contours still honor cancellation", () =>
        {
            Throws<OperationCanceledException>(() => LinetypePlanner.Plan([], Pattern(), cancellationToken: new(true)));
        });
        Test("all zero period is rejected", () =>
        {
            Throws<ArgumentException>(() => LinetypePlanner.Plan(Line(), new([new(0, Text)])));
        });
        Test("NaN lengths are rejected", () =>
        {
            Throws<ArgumentException>(() => LinetypePlanner.Plan(Line(), new([new(double.NaN, Text)])));
        });
        Test("microscopic periods are rejected before expansion", () =>
        {
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan(Line(), new([new(1e-12, Text)]), options: new(MaximumPlacements: 8)));
        });
        Test("point budget is enforced before distance allocation", () =>
        {
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan([new([Vec3.Zero, Vec3.UnitX, Vec3.UnitX * 2], false)], Pattern(), options: new(MaximumPoints: 2)));
        });
        Test("character budget includes decoration payload", () =>
        {
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan(Line(), Pattern(Text with { Content = "AB" }), options: new(MaximumTextLength: 1)));
        });
        Test("non-planar paths fail explicitly", () =>
        {
            Throws<NotSupportedException>(() => LinetypePlanner.Plan([new([Vec3.Zero, new(10, 0, 10)], false)], Pattern()));
        });
        Test("non-finite coordinates fail explicitly", () =>
        {
            Throws<ArgumentException>(() => LinetypePlanner.Plan([new([Vec3.Zero, new(double.PositiveInfinity, 0)], false)], Pattern()));
        });
        Test("numeric period overflow is rejected", () =>
        {
            Throws<ArgumentException>(() => LinetypePlanner.Plan(Line(), new([new(double.MaxValue), new(double.MaxValue, Text)])));
        });
        Test("singular source frame is rejected", () =>
        {
            Throws<ArgumentException>(() => LinetypePlanner.Plan(Line(), Pattern(), new Affine3(Vec3.Zero, Vec3.Zero, Vec3.Zero, Vec3.Zero)));
        });
        var malformed = new (string Name, CadLinetypeDecoration Value)[]
        {
            ("zero glyph scale", Text with { Scale = 0 }), ("negative width", Text with { WidthFactor = -1 }),
            ("negative style height", Text with { StyleHeight = -1 }), ("NaN rotation", Text with { Rotation = double.NaN }),
            ("singular oblique", Text with { ObliqueAngle = 90 }), ("non-planar offset", Text with { Offset = Vec3.UnitZ }),
            ("unknown rotation mode", Text with { RotationType = (CadLinetypeRotation)99 }), ("shape word overflow", Text with { ShapeNumber = 65536 })
        };
        foreach (var invalid in malformed)
            Test("reject malformed decoration/" + invalid.Name, () =>
            {
                Throws<ArgumentException>(() => LinetypePlanner.Plan(Line(), Pattern(invalid.Value)));
            });
    }

    private static void Near(double actual, double expected)
    { Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-9, $"Expected {expected}, got {actual}."); }
    private static void Vector(Vec3 actual, Vec3 expected) => Near(Vec3.Distance(actual, expected), 0);
    private static void Check(bool condition, string message = "Linetype planner assertion failed.")
    { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
