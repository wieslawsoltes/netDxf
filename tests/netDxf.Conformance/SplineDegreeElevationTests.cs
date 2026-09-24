// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline DegreeBezierSubject(int degree, int kind) => kind == 9 ? SplitBrokenSubject(degree) : KnotSubject(degree, kind);
    private static int[] DegreeBezierSpans(Spline source) => Enumerable.Range(source.Degree, source.ControlPoints.Length - source.Degree)
        .Where(i => source.Knots[i] < source.Knots[i + 1]).ToArray();

    private static Vector3 DegreeBezierSpanValue(Spline source, int span, double u)
    {
        var type = typeof(Spline).Assembly.GetType("netDxf.Entities.PeriodicSplineExactEvaluation")!;
        var method = type.GetMethod("Evaluate", BindingFlags.NonPublic | BindingFlags.Static)!;
        return (Vector3)method.Invoke(null, new object[] { source.ControlPoints, source.Weights, source.Knots,
            (int)source.Degree, u, span - source.Degree, false })!;
    }

    private static Spline DegreeBezierBadSource(int fault)
    {
        var source = fault == 0 ? new KnotDerived() : fault == 1
            ? new Spline(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }) : DegreeBezierSubject(3, 0);
        if (fault == 2) source.IsClosedPeriodic = true;
        if (fault == 3) source.StartTangent = Vector3.UnitX;
        if (fault == 4) source.EndTangent = Vector3.UnitY;
        if (fault == 5) source.Weights[2] = 0;
        if (fault == 6) source.Weights[2] = -1;
        if (fault == 7) source.Weights[2] = double.NaN;
        if (fault == 8) source.ControlPoints[1] = new Vector3(double.PositiveInfinity, 0, 0);
        if (fault == 9) source.Knots[4] = -1;
        if (fault == 10) source.Knots[4] = double.NaN;
        if (fault == 11) source.KnotTolerance = double.NaN;
        if (fault == 12) source.PersistentReactors.Add(new Line());
        if (fault == 13) typeof(EntityObject).GetField("normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, Vector3.Zero);
        if (fault == 14) typeof(Spline).GetField("weights", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, new double[0]);
        if (fault == 15) typeof(Spline).GetField("knots", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, new double[0]);
        if (fault == 16) for (int i = 0; i < source.Knots.Length; i++) source.Knots[i] = 0;
        if (fault == 17) { var data = new XData(new netDxf.Tables.ApplicationRegistry("BEZIER_HANDLE")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "AB")); source.XData.Add(data); }
        source.ProxyGraphics = new byte[] { 3, 7, 19 };
        return source;
    }

    private static void DegreeBezierReject(Spline source, Action<Spline> operation)
    {
        var p = source.ControlPoints; var w = source.Weights; var k = source.Knots;
        string[] bits = p.SelectMany(DirectionBits).ToArray(); long[] wb = w.Select(BitConverter.DoubleToInt64Bits).ToArray(), kb = k.Select(BitConverter.DoubleToInt64Bits).ToArray();
        byte[]? proxy = source.ProxyGraphics?.ToArray(); bool rejected = false;
        try { operation(source); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid Bézier edit was accepted");
        Check(ReferenceEquals(p, source.ControlPoints) && ReferenceEquals(w, source.Weights) && ReferenceEquals(k, source.Knots), "Rejected edit replaced source arrays");
        Check(bits.SequenceEqual(p.SelectMany(DirectionBits)) && wb.SequenceEqual(w.Select(BitConverter.DoubleToInt64Bits))
            && kb.SequenceEqual(k.Select(BitConverter.DoubleToInt64Bits)), "Rejected edit changed source values");
        Check(proxy == null ? source.ProxyGraphics == null : proxy.SequenceEqual(source.ProxyGraphics!), "Rejected edit changed source proxy");
    }

    private static Spline DegreeBezierBudgetSubject(int degree, int count)
    {
        var points = Enumerable.Range(0, count).Select(i => new Vector3(i, 1, 2)).ToArray();
        var knots = Enumerable.Range(0, count + degree + 1).Select(i => (double)Math.Clamp(i - degree, 0, count - degree)).ToArray();
        return new Spline(points, null, knots, (short)degree, false);
    }


    private static Spline ElevateSpline(Spline source, int times) => (Spline)Invoke(source, "ElevateDegree", times)!;

    private static void CheckElevationShape(Spline source, Spline result, int times)
    {
        int p = source.Degree, q = p + times; Equal(q, (int)result.Degree, "Elevated degree");
        int[] before = DegreeBezierSpans(source), after = DegreeBezierSpans(result); Equal(before.Length, after.Length, "Elevated span count");
        Equal(source.Knots[p], result.Knots[q], "Elevation active start");
        Equal(source.Knots[source.ControlPoints.Length], result.Knots[result.ControlPoints.Length], "Elevation active end");
        double scale = source.ControlPoints.SelectMany(pt => new[] { Math.Abs(pt.X), Math.Abs(pt.Y), Math.Abs(pt.Z) }).Max();
        double tolerance = Math.Max(double.Epsilon, scale * 4e-14);
        for (int s = 0; s < before.Length; s++)
        {
            int a = before[s], b = after[s]; double lo = source.Knots[a], hi = source.Knots[a + 1];
            Equal(lo, result.Knots[b], "Elevated span start"); Equal(hi, result.Knots[b + 1], "Elevated span end");
            int expected = s == 0 || source.Knots.Count(u => u == lo) == p + 1 ? q + 1 : q;
            Equal(expected, result.Knots.Count(u => u == lo), "Elevated knot multiplicity");
            for (int i = 0; i <= 8; i++)
            {
                double u = i == 0 ? lo : i == 8 ? hi : (1 - i / 8.0) * lo + (i / 8.0) * hi;
                Vector3 x = DegreeBezierSpanValue(source, a, u), y = DegreeBezierSpanValue(result, b, u);
                Check(double.IsFinite(y.X) && Math.Abs(x.X - y.X) <= tolerance, "Degree elevation X shape");
                Check(double.IsFinite(y.Y) && Math.Abs(x.Y - y.Y) <= tolerance, "Degree elevation Y shape");
                Check(double.IsFinite(y.Z) && Math.Abs(x.Z - y.Z) <= tolerance, "Degree elevation Z shape");
            }
        }
        Check(result.FitPoints.Count == 0 && result.StartTangent == null && result.EndTangent == null && !result.IsClosedPeriodic, "Elevated definition");
    }

    private static void DegreeElevationModel(int degree, int kind, int times)
    {
        var source = DegreeBezierSubject(degree, kind);
        var p = source.ControlPoints; var w = source.Weights; var k = source.Knots;
        byte[] before = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
        var result = ElevateSpline(source, times); CheckElevationShape(source, result, times);
        Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))) && ReferenceEquals(p, source.ControlPoints)
            && ReferenceEquals(w, source.Weights) && ReferenceEquals(k, source.Knots), "Elevation mutated source");
        Check(!ReferenceEquals(p, result.ControlPoints) && !ReferenceEquals(w, result.Weights) && !ReferenceEquals(k, result.Knots), "Elevation aliases source");
        if (kind != 8)
        {
            Equal(source.ControlPoints[0], result.ControlPoints[0], "Elevation changed clamped first endpoint");
            Equal(source.ControlPoints[^1], result.ControlPoints[^1], "Elevation changed clamped last endpoint");
            Equal(source.Weights[0], result.Weights[0], "Elevation changed first weight");
            Equal(source.Weights[^1], result.Weights[^1], "Elevation changed last weight");
        }
    }

    private static void DegreeElevationMetadata(int owner)
    {
        var source = DegreeBezierSubject(3, 0); ProjectionAppearance(source); NormalFixtureEditAndRestore(source, new Vector3(2, -3, 6));
        source.KnotTolerance = 1e-8; source.CtrlPointTolerance = 3e-8; source.FitTolerance = 2e-9;
        source.KnotParameterization = SplineKnotParameterization.FitUniform;
        if (owner == 1) new Block("ELEVATION_DETACHED").Entities.Add(source);
        if (owner == 2) new DxfDocument().Entities.Add(source);
        var oldOwner = source.Owner; var handle = source.Handle; byte[] proxy = source.ProxyGraphics!.ToArray();
        var result = ElevateSpline(source, 2); var sibling = ElevateSpline(source, 2);
        ProjectionAppearanceCheck(source, result);
        Equal(source.KnotTolerance, result.KnotTolerance, "Elevated knot tolerance");
        Equal(source.CtrlPointTolerance, result.CtrlPointTolerance, "Elevated control tolerance");
        Equal(source.FitTolerance, result.FitTolerance, "Elevated fit tolerance");
        Equal(source.KnotParameterization, result.KnotParameterization, "Elevated preference");
        Check(!ReferenceEquals(result.Layer, sibling.Layer) && !ReferenceEquals(result.XData["CURVE_PROJECTION"], sibling.XData["CURVE_PROJECTION"]), "Elevation metadata alias");
        Check(proxy.SequenceEqual(source.ProxyGraphics!) && ReferenceEquals(oldOwner, source.Owner) && handle == source.Handle, "Elevation source ownership/proxy changed");
        result.ControlPoints[0] = new Vector3(99, 98, 97);
        Check(sibling.ControlPoints[0] == source.ControlPoints[0], "Elevation sibling geometry alias");
    }

    private static void DegreeElevationWire(int degree, int kind, int times, DxfVersion version, bool binary)
    {
        var source = DegreeBezierSubject(degree, kind); ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        var result = ElevateSpline(source, times); var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(result);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Elevated spline save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-degree-wire-{degree}-{kind}-{times}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var curves = loaded.Entities.Splines.ToArray();
        Equal(2, curves.Length, "Elevated wire inventory"); CheckElevationShape(curves[0], curves[1], times);
        Check(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(result)).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(curves[1]))), "Elevated wire coefficient drift");
        Equal(0, loaded.Objects.Validate().Count, "Elevated wire graph");
    }

    private static void RegisterSplineDegreeElevationTests()
    {
        for (int d = 1; d < 10; d++) for (int k = 0; k <= 9; k++)
            foreach (int t in new[] { 1, 10 - d }.Distinct())
            { int degree = d, kind = k, times = t; Run($"spline-degree/model/{d}/{k}/{t}", () => DegreeElevationModel(degree, kind, times)); }
        for (int o = 0; o < 3; o++) { int owner = o; Run($"spline-degree/metadata/{o}", () => DegreeElevationMetadata(owner)); }
        foreach (int bad in new[] { int.MinValue, -1, 0, 8, int.MaxValue })
            Run($"spline-degree/times/{bad}", () => { Check(typeof(Spline).GetMethod("ElevateDegree") != null, "API missing"); Throws<ArgumentOutOfRangeException>(() => ElevateSpline(DegreeBezierSubject(3, 0), bad)); });
        Run("spline-degree/maximum-degree", () => { Check(typeof(Spline).GetMethod("ElevateDegree") != null, "API missing"); Throws<ArgumentOutOfRangeException>(() => ElevateSpline(DegreeBezierSubject(10, 0), 1)); });
        for (int f = 0; f < 18; f++)
        { int fault = f; Run($"spline-degree/reject/{f}", () => { Check(typeof(Spline).GetMethod("ElevateDegree") != null, "API missing"); DegreeBezierReject(DegreeBezierBadSource(fault), s => ElevateSpline(s, 1)); }); }
        Run("spline-degree/late-underflow", () =>
        {
            var source = new Spline(new[] { Vector3.Zero, new Vector3(double.Epsilon, 0, 0), Vector3.Zero, Vector3.UnitY },
                null, new[] { 0.0, 0.0, 0.0, .5, 1.0, 1.0, 1.0 }, (short)2, false);
            Check(typeof(Spline).GetMethod("ElevateDegree") != null, "API missing"); DegreeBezierReject(source, s => ElevateSpline(s, 1));
        });
        foreach (bool arithmetic in new[] { false, true }) Run($"spline-degree/budget/{arithmetic}", () =>
        {
            Check(typeof(Spline).GetMethod("ElevateDegree") != null, "API missing");
            var source = DegreeBezierBudgetSubject(arithmetic ? 9 : 1, arithmetic ? 11200 : 340002);
            Throws<NotSupportedException>(() => ElevateSpline(source, 1));
        });
        Run("spline-degree/adjacent-parameters", () =>
        {
            var source = new Spline(new[] { Vector3.Zero, Vector3.UnitX }, null,
                new[] { 1.0, 1.0, Math.BitIncrement(1.0), Math.BitIncrement(1.0) }, (short)1, false);
            var result = ElevateSpline(source, 1); Equal(new Vector3(.5, 0, 0), result.ControlPoints[1], "Adjacent interval midpoint");
        });
        foreach (int kind in new[] { 0, 1, 2 }) Run($"spline-degree/conic/{kind}", () =>
        {
            Spline source = kind == 0 ? new Circle(new Vector3(2, 3, 4), 5).ToSpline() : kind == 1
                ? new Arc(new Vector3(2, 3, 4), 5, 20, 280).ToSpline() : new Ellipse(new Vector3(2, 3, 4), 10, 4).ToSpline();
            var result = ElevateSpline(source, 3); CheckElevationShape(source, result, 3);
            Equal(source.IsClosed, result.IsClosed, "Conic closure after elevation");
        });
        foreach (int degree in new[] { 2, 3 }) foreach (int kind in new[] { 0, 8, 9 })
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"spline-degree/wire/{degree}/{kind}/{version}/{binary}", () => DegreeElevationWire(degree, kind, 2, version, binary));
        Run("spline-degree/numerical-corpus", () =>
        {
            var rows = new List<object>();
            for (int d = 1; d < 10; d++) for (int k = 0; k <= 9; k++) foreach (int t in new[] { 1, 10 - d }.Distinct())
            { var source = DegreeBezierSubject(d, k); rows.Add(new { degree = d, kind = k, times = t, source = KnotPacket(source), result = KnotPacket(ElevateSpline(source, t)) }); }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-degree-numerics.json"), JsonSerializer.Serialize(rows));
        });
    }
}
