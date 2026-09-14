using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSplineReversalTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        foreach (int degree in new[] { 1, 2, 3 })
        foreach (int form in new[] { 0, 1, 2 })
        foreach (int domain in new[] { 0, 1, 2 })
        {
            DxfVersion v = version; bool b = binary; int d = degree, f = form, k = domain;
            Run($"spline/reverse/{v}/{b}/degree{d}/form{f}/domain{k}", () => SplineReversal(v, b, d, f, k));
        }
        Run("spline/reverse/invalid-knots-do-not-mutate", SplineReverseInvalid);
        Run("spline/reverse/large-domain-finite", SplineReverseLarge);
        Run("spline/reverse/nested-insert", SplineReverseNested);
    }

    private static Spline NewReversalSpline(int degree, int form, int domain)
    {
        var controls = Enumerable.Range(0, 7).Select(i => new Vector3(i * 1.5, (i * i + 1) % 7, i % 3)).ToArray();
        if (form == 1) controls[^1] = controls[0];
        double[] weights = { 1, 0.5, 3, 1.25, 0.75, 2, 1 };
        int count = controls.Length + degree + 1 + (form == 2 ? degree : 0);
        var knots = new double[count];
        if (form == 2)
        {
            // Nonuniform, deliberately asymmetric padding and active spans.
            for (int i = 1; i < count; i++) knots[i] = knots[i - 1] + (i % 3 + 1) * 0.125;
        }
        else
        {
            for (int i = degree + 1; i < controls.Length; i++) knots[i] = (i - degree) * (i - degree) * 0.0625;
            for (int i = controls.Length; i < count; i++) knots[i] = 2;
        }
        double offset = domain == 1 ? -7.0 : domain == 2 ? 17.0 : 0.0;
        double scale = domain == 2 ? 4.0 : 1.0;
        return new Spline(controls, weights, knots.Select(k => k * scale + offset), (short)degree, form == 2)
        {
            StartTangent = new Vector3(2, 3, 4), EndTangent = new Vector3(-5, 6, 7),
            KnotTolerance = 1e-8, CtrlPointTolerance = 2e-8, FitTolerance = 3e-9,
            IsVisible = false
        };
    }

    // Independent rational de Boor evaluation on the explicit wire control list.
    // Do not use netDxf's evaluator as the oracle for its own Reverse method.
    private static Vector3 ReversalPoint(Spline spline, double parameter)
    {
        int p = spline.Degree;
        Vector3[] cp = spline.IsClosedPeriodic
            ? spline.ControlPoints.Skip(spline.ControlPoints.Length - p).Concat(spline.ControlPoints).ToArray()
            : spline.ControlPoints;
        double[] w = spline.IsClosedPeriodic
            ? spline.Weights.Skip(spline.Weights.Length - p).Concat(spline.Weights).ToArray()
            : spline.Weights;
        double[] u = spline.Knots;
        int span = p;
        while (span < cp.Length - 1 && parameter >= u[span + 1]) span++;
        var points = new Vector3[p + 1]; var weights = new double[p + 1];
        for (int j = 0; j <= p; j++) { int i = span - p + j; points[j] = cp[i] * w[i]; weights[j] = w[i]; }
        for (int r = 1; r <= p; r++)
            for (int j = p; j >= r; j--)
            {
                int i = span - p + j;
                double alpha = (parameter - u[i]) / (u[i + p - r + 1] - u[i]);
                points[j] = (1 - alpha) * points[j - 1] + alpha * points[j];
                weights[j] = (1 - alpha) * weights[j - 1] + alpha * weights[j];
            }
        return points[p] / weights[p];
    }

    private static void CheckReversalGeometry(Spline before, Spline after)
    {
        double a = before.Knots[before.Degree], b = before.Knots[before.Knots.Length - before.Degree - 1];
        Equal(a, after.Knots[after.Degree], "Reversal shifted active domain start");
        Equal(b, after.Knots[after.Knots.Length - after.Degree - 1], "Reversal shifted active domain end");
        for (int i = 0; i <= 32; i++)
        {
            double t = a + (b - a) * (i / 32.0);
            Vector3 expected = ReversalPoint(before, a + b - t), actual = ReversalPoint(after, t);
            Near(expected.X, actual.X, "Reverse locus X"); Near(expected.Y, actual.Y, "Reverse locus Y"); Near(expected.Z, actual.Z, "Reverse locus Z");
        }
    }

    private static void SplineReversal(DxfVersion version, bool binary, int degree, int form, int domain)
    {
        Spline before = NewReversalSpline(degree, form, domain), reversed = (Spline)before.Clone();
        double[] exposedKnots = reversed.Knots;
        reversed.Reverse();
        Check(ReferenceEquals(exposedKnots, reversed.Knots), "Reversal detached caller's knot-array reference.");
        CheckReversalGeometry(before, reversed);
        Equal(-before.EndTangent, reversed.StartTangent, "Start tangent not swapped/negated");
        Equal(-before.StartTangent, reversed.EndTangent, "End tangent not swapped/negated");
        Equal(before.IsClosedPeriodic, reversed.IsClosedPeriodic, "Periodic state changed");
        Equal(before.IsVisible, reversed.IsVisible, "Visibility changed");
        var twice = (Spline)reversed.Clone(); twice.Reverse();
        Check(before.ControlPoints.SequenceEqual(twice.ControlPoints), "Twice-reversed controls changed");
        Check(before.Weights.SequenceEqual(twice.Weights), "Twice-reversed weights changed");
        Check(before.Knots.SequenceEqual(twice.Knots), "Twice-reversed dyadic knots changed");
        var doc = new DxfDocument(version); doc.Entities.Add(before); doc.Entities.Add(reversed);
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var stream = new MemoryStream(); Check(doc.Save(stream, cycle == 0 ? binary : !binary), "Reversed spline save failed");
            if (cycle == 0 && domain == 1 && degree == 3)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-reverse-{version}-{binary}-{form}.dxf"), stream.ToArray());
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Reversed spline reload failed");
            var splines = doc.Entities.Splines.ToArray(); Equal(2, splines.Length, "Reversal entity count");
            CheckReversalGeometry(splines[0], splines[1]);
        }
    }

    private static void SplineReverseInvalid()
    {
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -100.0 })
        {
            Spline spline = NewReversalSpline(3, 0, 0); spline.Knots[5] = invalid;
            Vector3[] controls = (Vector3[])spline.ControlPoints.Clone(); double[] weights = (double[])spline.Weights.Clone();
            Vector3? start = spline.StartTangent, end = spline.EndTangent;
            Throws<InvalidOperationException>(spline.Reverse);
            Check(controls.SequenceEqual(spline.ControlPoints) && weights.SequenceEqual(spline.Weights), "Failed reversal partially mutated geometry");
            Equal(start, spline.StartTangent, "Failed reversal changed start tangent"); Equal(end, spline.EndTangent, "Failed reversal changed end tangent");
        }
    }

    private static void SplineReverseLarge()
    {
        var spline = NewReversalSpline(2, 0, 0);
        for (int i = 0; i < spline.Knots.Length; i++) spline.Knots[i] = 1.0e308 + spline.Knots[i] * 1.0e307;
        double a = spline.Knots[2], b = spline.Knots[^3];
        spline.Reverse();
        Check(spline.Knots.All(double.IsFinite), "Intermediate a+b overflowed despite finite reflected knots");
        Equal(a, spline.Knots[2], "Large domain start"); Equal(b, spline.Knots[^3], "Large domain end");
    }

    private static void SplineReverseNested()
    {
        Spline source = NewReversalSpline(3, 2, 1); var block = new Block("ReverseSpline"); block.Entities.Add(source);
        var insert = new Insert(block); var copy = (Insert)insert.Clone();
        var reversed = copy.Block.Entities.OfType<Spline>().Single(); reversed.Reverse(); CheckReversalGeometry(source, reversed);
        Spline exploded = copy.Explode().OfType<Spline>().Single(); CheckReversalGeometry(source, exploded);
    }
}
