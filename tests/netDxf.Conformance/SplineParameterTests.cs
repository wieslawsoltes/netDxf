// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using DxfPoint = netDxf.Entities.Point;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static object ParameterCall(Spline spline, string name, double u, int order, int side)
    {
        MethodInfo method = typeof(Spline).GetMethod(name) ?? throw new MissingMethodException(name);
        object limit = Enum.ToObject(method.GetParameters()[^1].ParameterType, side);
        object[] args = name == "PointAt" ? new object[] { u, limit } : new object[] { u, order, limit };
        try { return method.Invoke(spline, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static Vector3[] ParameterJet(Spline spline, double u, int order = 1, int side = 0)
        => (Vector3[])ParameterCall(spline, "EvaluateDerivatives", u, order, side);
    private static string ParameterBits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16");
    private static string[] ParameterVector(Vector3 point) => new[] { point.X, point.Y, point.Z }.Select(ParameterBits).ToArray();
    private static string ParameterState(Spline s) => JsonSerializer.Serialize(new
    {
        controls = s.ControlPoints.Select(ParameterVector), weights = s.Weights.Select(ParameterBits),
        knots = s.Knots.Select(ParameterBits), fits = s.FitPoints.Select(ParameterVector),
        normal = ParameterVector(s.Normal), s.Handle, s.ProxyGraphics, s.Layer.Name, s.IsVisible
    });

    private static Spline ParameterCurve(int degree, int form, int variant)
    {
        int n = form == 3 ? 2 * (degree + 1) : degree + 3;
        double scale = Math.ScaleB(1.0, (variant - 1) * 300);
        double knotScale = Math.ScaleB(1.0, (variant - 1) * 20);
        double weightScale = Math.ScaleB(1.0, (variant - 1) * 700);
        Vector3[] points = Enumerable.Range(0, n).Select(i =>
            new Vector3((i * 7 % 11 - 5) * scale, ((i * i + 3) % 13 - 6) * scale, (i % 3 - 1) * scale)).ToArray();
        double[] weights = Enumerable.Range(0, n).Select(i => (1 + i % 4) * weightScale).ToArray();
        double[] knots;
        if (form == 0)
            knots = Enumerable.Repeat(0.0, degree + 1).Concat(new[] { .25, .75 })
                .Concat(Enumerable.Repeat(1.0, degree + 1)).ToArray();
        else if (form == 1)
            knots = Enumerable.Range(0, n + degree + 1).Select(i => (double)(i - degree)).ToArray();
        else if (form == 2)
            knots = Enumerable.Range(0, n + 2 * degree + 1).Select(i => (i - degree) / 4.0).ToArray();
        else
            knots = Enumerable.Repeat(0.0, degree + 1).Concat(Enumerable.Repeat(.5, degree + 1))
                .Concat(Enumerable.Repeat(1.0, degree + 1)).ToArray();
        for (int i = 0; i < knots.Length; i++) knots[i] *= knotScale;
        return new Spline(points, weights, knots, (short)degree, form == 2);
    }

    private static void RegisterSplineParameterTests()
    {
        var rows = new List<object>();
        int[] degrees = { 1, 2, 3, 5, 10 };
        double[] fractions = { 0, .125, .5, .875, 1 };
        int[] orders = { 0, 1, 3, 10, 3 };
        foreach (int degree in degrees) for (int form = 0; form < 4; form++)
            for (int variant = 0; variant < 3; variant++) for (int sample = 0; sample < fractions.Length; sample++)
            {
                int f = form, v = variant, j = sample;
                string id = $"{degree}-{f}-{v}-{j}";
                Run("spline-parameter/corpus/" + id, () =>
                {
                    var s = ParameterCurve(degree, f, v); string before = ParameterState(s);
                    Vector3[] controls = s.ControlPoints; double[] knots = s.Knots, weights = s.Weights;
                    int end = controls.Length + (s.IsClosedPeriodic ? degree : 0);
                    double u = (1 - fractions[j]) * knots[degree] + fractions[j] * knots[end];
                    Vector3[] jet = ParameterJet(s, u, orders[j]);
                    Equal(orders[j] + 1, jet.Length, "Derivative count");
                    Check(jet.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)), "Finite results");
                    Vector3 point = (Vector3)ParameterCall(s, "PointAt", u, 0, 0);
                    Check(ParameterVector(point).SequenceEqual(ParameterVector(jet[0])), "Point convenience method differs");
                    Equal(before, ParameterState(s), "Evaluation mutated source");
                    Check(ReferenceEquals(controls, s.ControlPoints) && ReferenceEquals(knots, s.Knots) &&
                        ReferenceEquals(weights, s.Weights), "Evaluation replaced source arrays");
                    rows.Add(new
                    {
                        id, degree, form = f, variant = v, sample = j, order = orders[j],
                        parameter = ParameterBits(u), controls = controls.Select(ParameterVector),
                        weights = weights.Select(ParameterBits), knots = knots.Select(ParameterBits),
                        derivatives = jet.Select(ParameterVector)
                    });
                });
            }
        Run("spline-parameter/corpus-output", () =>
        {
            Equal(300, rows.Count, "Numerical corpus inventory");
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-parameter-numerics.json"), JsonSerializer.Serialize(rows));
        });

        for (int k = 0; k <= 10; k++)
        {
            int order = k;
            foreach (double u in new[] { 0.0, .5, 1.0 })
                Run($"spline-parameter/rational-derivative/{order}/{u:R}", () =>
                {
                    var s = new Spline(new[] { Vector3.Zero, new Vector3(1, 2, -1) },
                        new[] { 1.0, 2.0 }, (short)1);
                    Vector3[] jet = ParameterJet(s, u, order);
                    double factorial = 1;
                    for (int d = 0; d <= order; d++)
                    {
                        if (d > 1) factorial *= d;
                        double x = d == 0 ? 2 * u / (1 + u) : (d % 2 == 1 ? 2 : -2) * factorial / Math.Pow(1 + u, d + 1);
                        Near(x, jet[d].X, "Rational X derivative");
                        Near(2 * x, jet[d].Y, "Rational Y derivative");
                        Near(-x, jet[d].Z, "Rational Z derivative");
                    }
                });
        }
        foreach (double weight in new[] { 0.0, -1.0, -2.0 })
            Run($"spline-parameter/signed/{weight:R}", () =>
            {
                var s = new Spline(new[] { Vector3.Zero, Vector3.UnitX }, new[] { 1.0, weight }, (short)1);
                Vector3[] jet = ParameterJet(s, .25, 10);
                double denom = .75 + .25 * weight;
                Near(.25 * weight / denom, jet[0].X, "Signed point");
                double factorial = 1;
                for (int k = 1; k <= 10; k++)
                {
                    factorial *= k;
                    double expected = (k % 2 == 1 ? 1 : -1) * factorial * weight *
                        Math.Pow(weight - 1, k - 1) / Math.Pow(denom, k + 1);
                    Near(expected, jet[k].X, "Signed derivative");
                }
            });
        Run("spline-parameter/sampled-pole", () =>
        {
            var s = new Spline(new[] { Vector3.UnitX, Vector3.UnitX }, new[] { 1.0, -1.0 }, (short)1);
            Throws<InvalidOperationException>(() => ParameterJet(s, .5, 0));
        });
        Run("spline-parameter/one-sided-break", () =>
        {
            var s = new Spline(new[] { Vector3.Zero, Vector3.UnitX, new Vector3(10, 0, 0), new Vector3(12, 0, 0) },
                new[] { 1.0, 1.0, 1.0, 1.0 }, new[] { 0.0, 0, .5, .5, 1, 1 }, (short)1, false);
            Near(1, ParameterJet(s, .5, 1, 1)[0].X, "Left point");
            Near(2, ParameterJet(s, .5, 1, 1)[1].X, "Left derivative");
            Near(10, ParameterJet(s, .5, 1, 2)[0].X, "Right point");
            Near(4, ParameterJet(s, .5, 1, 2)[1].X, "Right derivative");
            Near(10, ParameterJet(s, .5, 1)[0].X, "Automatic chooses right");
        });
        foreach (int degree in degrees)
            Run($"spline-parameter/periodic-seam/{degree}", () =>
            {
                var s = ParameterCurve(degree, 2, 1);
                Vector3[] first = ParameterJet(s, s.Knots[degree], 3), last = ParameterJet(s, s.Knots[s.ControlPoints.Length + degree], 3);
                // At low degrees, high derivatives may differ at the seam.
                for (int k = 0; k < Math.Min(degree, 4); k++)
                    Check(ParameterVector(first[k]).SequenceEqual(ParameterVector(last[k])), "Periodic seam mismatch");
            });
        foreach (double scale in new[] { double.Epsilon, 1e-200, 1.0, 1e200, double.MaxValue })
            Run($"spline-parameter/constant/{scale:R}", () =>
            {
                var s = new Spline(Enumerable.Repeat(new Vector3(scale, -scale, 0), 4), new[] { 1e-300, 2.0, 1e200, 1.0 }, (short)3);
                Vector3[] jet = ParameterJet(s, .375, 10);
                Equal(scale, jet[0].X, "Exact constant X"); Equal(-scale, jet[0].Y, "Exact constant Y");
                Check(jet.Skip(1).All(p => p.X == 0 && p.Y == 0 && p.Z == 0), "Constant derivatives");
            });
        Run("spline-parameter/extreme-domain", () =>
        {
            var s = new Spline(new[] { new Vector3(-1e308, 0, 0), new Vector3(1e308, 0, 0) },
                new[] { 1.0, 1.0 }, new[] { -1e308, -1e308, 1e308, 1e308 }, (short)1, false);
            Vector3[] jet = ParameterJet(s, 0, 10);
            Equal(0.0, jet[0].X, "Exact cancellation"); Equal(1.0, jet[1].X, "Derivative cancellation");
            Check(jet.Skip(2).All(p => p.X == 0), "Linear higher derivatives");
        });

        foreach (double u in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0, 4.0 })
            Run($"spline-parameter/reject-parameter/{ParameterBits(u)}",
                () => Throws<ArgumentOutOfRangeException>(() => ParameterJet(ParameterCurve(2, 0, 1), u)));
        foreach (int order in new[] { -1, 11, int.MaxValue })
            Run($"spline-parameter/reject-order/{order}",
                () => Throws<ArgumentOutOfRangeException>(() => ParameterJet(ParameterCurve(2, 0, 1), .5, order)));
        foreach (int side in new[] { -1, 3, int.MaxValue })
            Run($"spline-parameter/reject-side/{side}",
                () => Throws<ArgumentOutOfRangeException>(() => ParameterJet(ParameterCurve(2, 0, 1), .5, 1, side)));
        Run("spline-parameter/exterior-limits", () =>
        {
            var s = ParameterCurve(2, 0, 1);
            Throws<ArgumentOutOfRangeException>(() => ParameterJet(s, 0, 1, 1));
            Throws<ArgumentOutOfRangeException>(() => ParameterJet(s, 1, 1, 2));
        });
        foreach (string field in new[] { "controlPoints", "weights", "knots" })
            Run("spline-parameter/missing/" + field, () =>
            {
                var s = ParameterCurve(2, 0, 1);
                typeof(Spline).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, null);
                Throws<InvalidOperationException>(() => ParameterJet(s, .5));
            });
        for (int fault = 0; fault < 7; fault++)
        {
            int f = fault;
            Run($"spline-parameter/malformed/{f}", () =>
            {
                var s = ParameterCurve(2, 0, 1);
                if (f == 0) s.ControlPoints[0] = new(double.NaN, 0, 0);
                else if (f == 1) s.Weights[0] = double.PositiveInfinity;
                else if (f == 2) s.Knots[0] = double.NaN;
                else if (f == 3) s.Knots[3] = -1;
                else if (f == 4) s.Knots[3] = 0;
                else if (f == 5) Array.Fill(s.Knots, 0);
                else typeof(Spline).GetField("degree", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(s, (short)0);
                string before = ParameterState(s);
                Throws<InvalidOperationException>(() => ParameterJet(s, .5));
                Equal(before, ParameterState(s), "Rejected evaluation mutated source");
            });
        }
        Run("spline-parameter/derivative-overflow", () =>
        {
            var s = new Spline(new[] { Vector3.Zero, new Vector3(double.MaxValue, 0, 0) },
                new[] { 1.0, 1.0 }, new[] { 0.0, 0, double.Epsilon, double.Epsilon }, (short)1, false);
            Near(0, ParameterJet(s, 0, 0)[0].X, "Order zero does not compute overflowing derivatives");
            string before = ParameterState(s);
            Throws<ArgumentException>(() => ParameterJet(s, 0, 1));
            Equal(before, ParameterState(s), "Overflow mutated source");
        });
        Run("spline-parameter/read-only-owned", () =>
        {
            var s = ParameterCurve(3, 0, 1); var d = new DxfDocument(); d.Entities.Add(s); s.ProxyGraphics = new byte[] { 1, 2, 3 };
            string before = ParameterState(s); object owner = s.Owner;
            Vector3[] a = ParameterJet(s, .5, 3); a[0] = Vector3.Zero;
            Vector3[] b = ParameterJet(s, .5, 3);
            Check(!ReferenceEquals(a, b), "Result arrays are aliased");
            Equal(before, ParameterState(s), "Owned source mutated"); Check(ReferenceEquals(owner, s.Owner), "Owner changed");
        });
        foreach (double epsilon in new[] { 1e-12, 1.0, 100.0 })
            Run($"spline-parameter/epsilon/{epsilon:R}", () =>
            {
                var s = ParameterCurve(3, 0, 1); Vector3[] expected = ParameterJet(s, .5, 3);
                double old = MathHelper.Epsilon;
                try
                {
                    MathHelper.Epsilon = epsilon;
                    Vector3[] actual = ParameterJet(s, .5, 3);
                    for (int k = 0; k < actual.Length; k++)
                        Check(ParameterVector(expected[k]).SequenceEqual(ParameterVector(actual[k])), "Global epsilon changed evaluation");
                }
                finally { MathHelper.Epsilon = old; }
            });
        Run("spline-parameter/unclamped-active-end", () =>
        {
            var s = new Spline(Enumerable.Range(0, 5).Select(i => new Vector3(i, 0, 0)), Enumerable.Repeat(1.0, 5),
                new[] { -2.0, -1, 0, 1, 2, 3, 4, 5 }, (short)2, false);
            var jet = ParameterJet(s, 3, 3);
            Equal(3.5, jet[0].X, "Unclamped endpoint is not the last control");
            Equal(1.0, jet[1].X, "Unclamped derivative"); Equal(0.0, jet[2].X, "Unclamped second derivative");
        });
        Run("spline-parameter/fit-definition-read-only", () =>
        {
            var s = new Spline(new[] { Vector3.Zero, new Vector3(2, 3, 1), new Vector3(5, 0, 2) });
            var reference = new Spline(s.ControlPoints, s.Weights, s.Knots, s.Degree, false);
            s.StartTangent = new Vector3(1e200, -1e200, 0);
            s.EndTangent = new Vector3(0, 1e200, 1e200);
            string before = ParameterState(s);
            double u = (s.Knots[s.Degree] + s.Knots[s.ControlPoints.Length]) / 2;
            var expected = ParameterJet(reference, u, 3); var actual = ParameterJet(s, u, 3);
            for (int k = 0; k < actual.Length; k++)
                Check(ParameterVector(expected[k]).SequenceEqual(ParameterVector(actual[k])), "Fitting metadata changed stored control evaluation");
            Equal(before, ParameterState(s), "Fit-created source mutated");
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            for (int form = 0; form < 4; form++)
            {
                int f = form;
                Run($"spline-parameter/wire/{version}/{binary}/{f}", () =>
                {
                    var s = ParameterCurve(3, f, 1); var d = new DxfDocument(version); d.Entities.Add(s);
                    int end = s.ControlPoints.Length + (s.IsClosedPeriodic ? 3 : 0);
                    double a = s.Knots[3], b = s.Knots[end];
                    foreach (double fraction in fractions)
                        d.Entities.Add(new DxfPoint(ParameterJet(s, (1 - fraction) * a + fraction * b, 0)[0]));
                    using var output = new MemoryStream(); Check(d.Save(output, binary), "Parameter drawing save failed");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-parameter-{version}-{binary}-{f}.dxf"), output.ToArray());
                    output.Position = 0; var loaded = DxfDocument.Load(output)!; var restored = loaded.Entities.Splines.Single();
                    foreach (double fraction in fractions)
                    {
                        double u = (1 - fraction) * a + fraction * b;
                        var expected = ParameterJet(s, u, 3); var actual = ParameterJet(restored, u, 3);
                        for (int k = 0; k < 4; k++)
                            Check(ParameterVector(expected[k]).SequenceEqual(ParameterVector(actual[k])), "Stored derivative changed");
                    }
                });
            }
    }
}
