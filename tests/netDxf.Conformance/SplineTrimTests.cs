// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Blocks;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline TrimSpline(Spline s, double a, double b) => (Spline)Invoke(s, "Trim", a, b)!;

    private static (double a, double b) TrimBounds(Spline s, int kind)
    {
        double a = s.Knots[s.Degree], b = s.Knots[s.ControlPoints.Length];
        double At(double t) => (1 - t) * a + t * b;
        return kind switch { 0 => (a, b), 1 => (a, At(.5)), 2 => (At(.5), b),
            3 => (At(.125), At(.875)), _ => (At(.375), At(.625)) };
    }

    private static void CheckTrim(Spline source, Spline result, double a, double b)
    {
        Equal(source.Degree, result.Degree, "Trim degree");
        Equal(a, result.Knots[result.Degree], "Trim start");
        Equal(b, result.Knots[result.ControlPoints.Length], "Trim end");
        Check(result.Knots.Take(result.Degree + 1).All(v => v == a)
            && result.Knots.TakeLast(result.Degree + 1).All(v => v == b), "Trim clamping");
        Check(result.CreationMethod == SplineCreationMethod.ControlPoints && result.FitPoints.Count == 0
            && result.StartTangent == null && result.EndTangent == null && !result.IsClosedPeriodic, "Trim representation");
        double scale = source.ControlPoints.SelectMany(p => new[] { Math.Abs(p.X), Math.Abs(p.Y), Math.Abs(p.Z) }).Max();
        double tolerance = Math.Max(double.Epsilon, scale * 8e-14);
        var retained = DegreeBezierSpans(source).Where(k => Math.Max(a, source.Knots[k]) < Math.Min(b, source.Knots[k + 1])).ToArray();
        var actual = DegreeBezierSpans(result); Equal(retained.Length, actual.Length, "Trim span count");
        for (int i = 0; i < retained.Length; i++)
        {
            int sk = retained[i], rk = actual[i]; double lo = Math.Max(a, source.Knots[sk]), hi = Math.Min(b, source.Knots[sk + 1]);
            Equal(lo, result.Knots[rk], "Trim span left"); Equal(hi, result.Knots[rk + 1], "Trim span right");
            int repeats = i == 0 || sk - retained[i - 1] == source.Degree + 1 ? source.Degree + 1 : source.Degree;
            Equal(repeats, result.Knots.Count(v => v == lo), "Trim knot multiplicity");
            for (int j = 0; j <= 8; j++)
            {
                double u = j == 0 ? lo : j == 8 ? hi : (1 - j / 8.0) * lo + (j / 8.0) * hi;
                Vector3 x = DegreeBezierSpanValue(source, sk, u), y = DegreeBezierSpanValue(result, rk, u);
                Check(double.IsFinite(y.X) && Math.Abs(x.X - y.X) <= tolerance, "Trim X locus");
                Check(double.IsFinite(y.Y) && Math.Abs(x.Y - y.Y) <= tolerance, "Trim Y locus");
                Check(double.IsFinite(y.Z) && Math.Abs(x.Z - y.Z) <= tolerance, "Trim Z locus");
            }
        }
    }

    private static void TrimModel(int degree, int kind, int range)
    {
        var source = BasisSubject(degree, kind); var (a, b) = TrimBounds(source, range);
        var p = source.ControlPoints; var w = source.Weights; var k = source.Knots;
        byte[] before = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
        var result = TrimSpline(source, a, b); CheckTrim(source, result, a, b);
        Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))) && ReferenceEquals(p, source.ControlPoints)
            && ReferenceEquals(w, source.Weights) && ReferenceEquals(k, source.Knots), "Trim changed source");
        Check(!ReferenceEquals(p, result.ControlPoints) && !ReferenceEquals(w, result.Weights) && !ReferenceEquals(k, result.Knots), "Trim source alias");
    }

    private static void TrimMetadata(int ownership)
    {
        var source = BasisSubject(3, 8); ProjectionAppearance(source); source.Normal = new Vector3(2, -3, 6);
        source.KnotTolerance = 2e-9; source.CtrlPointTolerance = 3e-9; source.FitTolerance = 4e-9;
        if (ownership == 1) new Block("TRIM_DETACHED").Entities.Add(source);
        if (ownership == 2) new DxfDocument().Entities.Add(source);
        var owner = source.Owner; var handle = source.Handle; byte[] proxy = source.ProxyGraphics!.ToArray();
        var x = TrimSpline(source, .125, .875); var y = TrimSpline(source, .125, .875);
        ProjectionAppearanceCheck(source, x); Equal(source.KnotTolerance, x.KnotTolerance, "Trim knot tolerance");
        Equal(source.CtrlPointTolerance, x.CtrlPointTolerance, "Trim control tolerance");
        Equal(source.FitTolerance, x.FitTolerance, "Trim fit tolerance");
        Equal(source.KnotParameterization, x.KnotParameterization, "Trim parameterization");
        Check(ReferenceEquals(owner, source.Owner) && handle == source.Handle && proxy.SequenceEqual(source.ProxyGraphics!), "Trim changed ownership or proxy");
        Check(!ReferenceEquals(x.Layer, y.Layer) && !ReferenceEquals(x.XData["CURVE_PROJECTION"], y.XData["CURVE_PROJECTION"]), "Trim metadata alias");
        Vector3 first = y.ControlPoints[0]; x.ControlPoints[0] = new Vector3(99, 98, 97);
        Equal(first, y.ControlPoints[0], "Trim sibling geometry alias");
    }

    private static void TrimWire(int degree, int kind, int range, DxfVersion version, bool binary)
    {
        var source = BasisSubject(degree, kind); ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        var (a, b) = TrimBounds(source, range); var result = TrimSpline(source, a, b);
        var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(result);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Trim wire save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-trim-wire-{degree}-{kind}-{range}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var curves = loaded.Entities.Splines.ToArray();
        Equal(2, curves.Length, "Trim wire inventory"); CheckTrim(curves[0], curves[1], a, b);
        Check(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(result)).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(curves[1]))), "Trim wire coefficients drifted");
        Equal(0, loaded.Objects.Validate().Count, "Trim wire graph");
    }

    private static void RegisterSplineTrimTests()
    {
        for (int d = 1; d <= 10; d++) for (int k = 0; k <= 10; k++) for (int r = 0; r < 5; r++)
        { int degree = d, kind = k, range = r; Run($"spline-trim/model/{d}/{k}/{r}", () => TrimModel(degree, kind, range)); }
        for (int d = 1; d <= 10; d++) { int degree = d; Run($"spline-trim/adjacent/{d}", () => TrimModel(degree, 11, 0)); }
        for (int o = 0; o < 3; o++) { int owner = o; Run($"spline-trim/metadata/{o}", () => TrimMetadata(owner)); }
        for (int f = 0; f < 18; f++)
        { int fault = f; Run($"spline-trim/reject/{f}", () => { Check(typeof(Spline).GetMethod("Trim") != null, "Trim API missing"); DegreeBezierReject(DegreeBezierBadSource(fault), s => TrimSpline(s, .125, .875)); }); }
        var bad = new[] { (double.NaN, 1.0), (0.0, double.NaN), (double.NegativeInfinity, 1.0), (0.0, double.PositiveInfinity),
            (-.1, .9), (.1, 1.1), (.5, .5), (.9, .1), (1.0, 2.0) };
        for (int i = 0; i < bad.Length; i++) { var pair = bad[i]; Run($"spline-trim/range/{i}", () =>
            { Check(typeof(Spline).GetMethod("Trim") != null, "Trim API missing"); DegreeBezierReject(BasisSubject(3, 0), s => TrimSpline(s, pair.Item1, pair.Item2)); }); }
        Run("spline-trim/late-underflow", () =>
        {
            Check(typeof(Spline).GetMethod("Trim") != null, "Trim API missing");
            var source = new Spline(new[] { Vector3.Zero, new Vector3(double.Epsilon, 0, 0) }, null, (short)1, false);
            DegreeBezierReject(source, s => TrimSpline(s, .125, .25));
        });
        Run("spline-trim/work-budget", () =>
        {
            Check(typeof(Spline).GetField("MaximumTrimBlendOperations") != null, "Trim bound missing");
            var source = DegreeBezierBudgetSubject(10, 10000);
            Throws<NotSupportedException>(() => TrimSpline(source, 0, 9990));
            // A small retained interval is allowed even if full extraction would exceed its work budget.
            CheckTrim(source, TrimSpline(source, 0, .125), 0, .125);
        });
        foreach (int degree in new[] { 2, 3 }) foreach (int kind in new[] { 0, 8, 9 }) foreach (int range in new[] { 1, 2, 3 })
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"spline-trim/wire/{degree}/{kind}/{range}/{version}/{binary}", () => TrimWire(degree, kind, range, version, binary));
        Run("spline-trim/numerical-corpus", () =>
        {
            var rows = new List<object>();
            for (int d = 1; d <= 10; d++) foreach (int k in new[] { 0, 2, 5, 8, 9, 10 }) foreach (int r in new[] { 1, 2, 3 })
            { var source = BasisSubject(d, k); var (a, b) = TrimBounds(source, r);
              rows.Add(new { degree = d, kind = k, range = r, start = a, end = b, source = KnotPacket(source), result = KnotPacket(TrimSpline(source, a, b)) }); }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-trim-numerics.json"), JsonSerializer.Serialize(rows));
        });
    }
}
