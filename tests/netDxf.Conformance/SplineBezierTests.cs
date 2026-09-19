// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline[] BezierParts(Spline source) => (Spline[])Invoke(source, "ToBezierSegments")!;

    private static Spline BasisSubject(int degree, int kind)
    {
        if (kind < 9) return KnotSubject(degree, kind);
        if (kind == 9) return SplitBrokenSubject(degree);
        double[] knots = kind == 10
            ? Enumerable.Repeat(0.0, degree + 1).Concat(Enumerable.Repeat(.25, Math.Max(1, degree - 1)))
                .Concat(Enumerable.Repeat(.5, degree)).Concat(Enumerable.Repeat(.75, degree + 1))
                .Concat(Enumerable.Repeat(1.0, degree + 1)).ToArray()
            : Enumerable.Repeat(1.0, degree + 1).Concat(Enumerable.Repeat(Math.BitIncrement(1.0), degree + 1)).ToArray();
        int count = knots.Length - degree - 1;
        return new Spline(Enumerable.Range(0, count).Select(i => new Vector3(i - 2, i * 7 % 11 - 4, i % 3 - 1)),
            Enumerable.Range(0, count).Select(i => 1.0 + (i % 4) * .25), knots, (short)degree, false);
    }

    private static void CheckBezierParts(Spline source, Spline[] parts)
    {
        var bounds = new List<(double a, double b)>();
        for (int i = source.Degree; i < source.ControlPoints.Length; i++)
            if (source.Knots[i] < source.Knots[i + 1]) bounds.Add((source.Knots[i], source.Knots[i + 1]));
        Equal(bounds.Count, parts.Length, "Bezier span count");
        double scale = source.ControlPoints.SelectMany(p => new[] { Math.Abs(p.X), Math.Abs(p.Y), Math.Abs(p.Z) }).Max();
        double tolerance = Math.Max(double.Epsilon, scale * 8e-14);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i]; var (a, b) = bounds[i];
            Equal(source.Degree, part.Degree, "Bezier degree");
            Equal(source.Degree + 1, part.ControlPoints.Length, "Bezier control count");
            Check(part.Knots.SequenceEqual(Enumerable.Repeat(a, source.Degree + 1).Concat(Enumerable.Repeat(b, source.Degree + 1))), "Bezier clamped interval");
            Check(!part.IsClosedPeriodic && part.CreationMethod == SplineCreationMethod.ControlPoints && part.FitPoints.Count == 0, "Bezier definition");
            Check(part.Weights.All(w => double.IsFinite(w) && w > 0), "Bezier positive finite weights");
            for (int j = 0; j <= 16; j++)
            {
                double t = j / 16.0, u = j == 0 ? a : j == 16 ? b : (1 - t) * a + t * b;
                if (u == b && i != parts.Length - 1 && source.Knots.Count(k => k == b) == source.Degree + 1) continue;
                var expected = SplitReferencePoint(source, u); var actual = SplitReferencePoint(part, u);
                Check(double.IsFinite(actual.X) && Math.Abs(actual.X - expected.X) <= tolerance, "Bezier X locus");
                Check(double.IsFinite(actual.Y) && Math.Abs(actual.Y - expected.Y) <= tolerance, "Bezier Y locus");
                Check(double.IsFinite(actual.Z) && Math.Abs(actual.Z - expected.Z) <= tolerance, "Bezier Z locus");
            }
            Check(!ReferenceEquals(part.ControlPoints, source.ControlPoints) && !ReferenceEquals(part.Weights, source.Weights), "Bezier source alias");
            if (i > 0) Check(!ReferenceEquals(part.Knots, parts[i - 1].Knots) && !ReferenceEquals(part.ControlPoints, parts[i - 1].ControlPoints), "Bezier sibling alias");
        }
    }

    private static void BezierModel(int degree, int kind)
    {
        var source = BasisSubject(degree, kind); var oldPoints = source.ControlPoints; var oldKnots = source.Knots; var oldWeights = source.Weights;
        var before = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
        var parts = BezierParts(source); CheckBezierParts(source, parts);
        Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))), "Bezier source changed");
        Check(ReferenceEquals(oldPoints, source.ControlPoints) && ReferenceEquals(oldKnots, source.Knots) && ReferenceEquals(oldWeights, source.Weights), "Bezier source containers changed");
    }

    private static void BasisMetadata(bool elevate, int ownership)
    {
        var source = BasisSubject(3, 0); ProjectionAppearance(source); source.Normal = new Vector3(2, -3, 6);
        source.KnotTolerance = 2e-9; source.CtrlPointTolerance = 3e-9; source.FitTolerance = 4e-9;
        if (ownership == 1) new Block("BEZIER_DETACHED").Entities.Add(source);
        if (ownership == 2) new DxfDocument().Entities.Add(source);
        var oldOwner = source.Owner; var handle = source.Handle; byte[] proxy = source.ProxyGraphics!.ToArray();
        Spline[] parts = elevate ? new[] { (Spline)Invoke(source, "ElevateDegree", 5)!, (Spline)Invoke(source, "ElevateDegree", 5)! } : BezierParts(source);
        foreach (var part in parts)
        {
            ProjectionAppearanceCheck(source, part);
            Equal(source.KnotTolerance, part.KnotTolerance, "basis knot tolerance");
            Equal(source.CtrlPointTolerance, part.CtrlPointTolerance, "basis control tolerance");
            Equal(source.FitTolerance, part.FitTolerance, "basis fit tolerance");
            Equal(source.KnotParameterization, part.KnotParameterization, "basis parameterization");
        }
        Check(!ReferenceEquals(parts[0].XData["CURVE_PROJECTION"], parts[1].XData["CURVE_PROJECTION"])
            && !ReferenceEquals(parts[0].Layer, parts[1].Layer), "basis sibling metadata alias");
        Check(ReferenceEquals(oldOwner, source.Owner) && handle == source.Handle && proxy.SequenceEqual(source.ProxyGraphics!), "basis source identity or proxy changed");
        var before = parts[1].ControlPoints[0]; parts[0].ControlPoints[0] = new Vector3(99, 98, 97);
        Equal(before, parts[1].ControlPoints[0], "basis sibling control alias");
    }

    private static void BezierReject(int fault)
    {
        Check(typeof(Spline).GetMethod("ToBezierSegments") != null, "Bezier API missing");
        var source = BasisSubject(3, 0);
        switch (fault)
        {
            case 0: source.ControlPoints[2] = new Vector3(double.NaN, 0, 0); break;
            case 1: source.Weights[2] = 0; break;
            case 2: source.Weights[2] = -1; break;
            case 3: source.Weights[2] = double.PositiveInfinity; break;
            case 4: source.Knots[4] = -.5; break;
            case 5: source.Knots[4] = double.NaN; break;
            case 6: Array.Fill(source.Knots, 0.0); break;
            case 7: source.StartTangent = Vector3.UnitX; break;
            case 8: source.EndTangent = Vector3.UnitY; break;
            case 9: source = new Spline(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }); break;
            case 10: source = new Spline(source.ControlPoints, source.Weights, (short)3, true); break;
            case 11: source = new KnotDerived(); break;
            case 12: source.PersistentReactors.Add(new Line()); break;
            case 13: source = new Spline(new[] { new Vector3(double.Epsilon, 0, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero }, new[] { 1.0, 1.0, 1.0, 1.0 }, new[] { -2.0, -1.0, 0.0, 1.0, 2.0, 3.0, 4.0 }, (short)2, false); break;
        }
        source.ProxyGraphics = new byte[] { 7, 3, 1 };
        var points = source.ControlPoints.ToArray(); var weights = source.Weights.ToArray(); var knots = source.Knots.ToArray();
        Exception? error = null;
        try { BezierParts(source); } catch (Exception ex) { error = ex; }
        Check(error is ArgumentException || error is InvalidOperationException || error is NotSupportedException, "Expected Bezier admission rejection");
        Check(points.SelectMany(DirectionBits).SequenceEqual(source.ControlPoints.SelectMany(DirectionBits))
            && weights.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(source.Weights.Select(BitConverter.DoubleToInt64Bits))
            && knots.Select(BitConverter.DoubleToInt64Bits).SequenceEqual(source.Knots.Select(BitConverter.DoubleToInt64Bits))
            && source.ProxyGraphics.SequenceEqual(new byte[] { 7, 3, 1 }), "Bezier rejection changed source");
    }

    private static void BasisWire(bool elevate, int degree, int kind, DxfVersion version, bool binary)
    {
        var source = BasisSubject(degree, kind); ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        Spline[] result = elevate ? new[] { (Spline)Invoke(source, "ElevateDegree", degree + 2)! } : BezierParts(source);
        var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(result);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "basis wire save");
        string name = elevate ? "degree" : "bezier";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-{name}-wire-{degree}-{kind}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var all = loaded.Entities.Splines.ToArray();
        Equal(result.Length + 1, all.Length, "basis wire entity inventory");
        for (int i = 0; i < result.Length; i++)
            Check(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(result[i])).SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(all[i + 1]))), "basis wire coefficient retention");
        Equal(0, loaded.Objects.Validate().Count, "basis graph");
    }

    private static void RegisterSplineBezierTests()
    {
        for (int d = 1; d <= 10; d++) for (int k = 0; k < 12; k++)
        { int degree = d, kind = k; Run($"spline-bezier/model/{d}/{k}", () => BezierModel(degree, kind)); }
        for (int i = 0; i < 14; i++) { int fault = i; Run($"spline-bezier/reject/{i}", () => BezierReject(fault)); }
        for (int i = 0; i < 3; i++) { int owner = i; Run($"spline-bezier/metadata/{i}", () => BasisMetadata(false, owner)); }
        foreach (bool algebra in new[] { false, true }) Run($"spline-bezier/budget/{algebra}", () =>
        {
            Check(typeof(Spline).GetMethod("ToBezierSegments") != null, "Bezier API missing");
            int count = algebra ? 35000 : 600000; short degree = (short)(algebra ? 10 : 1);
            var source = new Spline(Enumerable.Repeat(Vector3.UnitX, count), null, degree, false);
            Throws<NotSupportedException>(() => BezierParts(source));
        });
        foreach (int degree in new[] { 2, 3 }) foreach (int kind in new[] { 0, 8, 10 })
            foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"spline-bezier/wire/{degree}/{kind}/{version}/{binary}", () => BasisWire(false, degree, kind, version, binary));
        Run("spline-bezier/numerical-corpus", () =>
        {
            var rows = new List<object>();
            for (int degree = 1; degree <= 10; degree++) for (int kind = 0; kind < 12; kind++)
            {
                var source = BasisSubject(degree, kind); var parts = BezierParts(source);
                rows.Add(new { degree, kind, source = KnotPacket(source), parts = parts.Select(KnotPacket).ToArray() });
            }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-bezier-numerics.json"), JsonSerializer.Serialize(rows));
        });
    }
}
