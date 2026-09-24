// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline InsertSplineKnot(Spline spline, double parameter, int times)
        => (Spline)Invoke(spline, "InsertKnot", parameter, times)!;

    private static double KnotCoordinate(double value, int kind)
    {
        if (kind == 1) return value * 4 + 2;
        if (kind == 2) return (2 * value - 1) * double.MaxValue;
        if (kind == 3) return value * Math.ScaleB(1, -1020);
        return value;
    }

    private static Spline KnotSubject(int degree, int kind)
    {
        int count = degree + 4;
        var controls = new Vector3[count]; var weights = new double[count];
        for (int i = 0; i < count; i++)
        {
            controls[i] = new Vector3(i - 2, (i * 7 % 11) - 4, (i % 3) - 1);
            weights[i] = 1 + (i % 4) * .25;
            if (kind == 4) weights[i] = double.MaxValue * (.5 + (i % 4) * .125);
            if (kind == 5) weights[i] = double.Epsilon * 16;
            if (kind == 6) controls[i] *= Math.ScaleB(1, 990);
            if (kind == 7) controls[i] *= Math.ScaleB(1, -990);
        }
        var knots = new double[count + degree + 1];
        for (int i = 0; i < knots.Length; i++)
            knots[i] = KnotCoordinate(Math.Clamp((i - degree) / 4.0, 0, 1), kind);
        if (kind == 8)
            for (int i = 0; i < knots.Length; i++) knots[i] = (i - degree) / 4.0;
        return new Spline(controls, weights, knots, (short)degree, false);
    }

    private static object KnotPacket(Spline spline) => new
    {
        degree = (int)spline.Degree,
        points = spline.ControlPoints.Select(p => new[] { p.X, p.Y, p.Z }).ToArray(),
        weights = spline.Weights, knots = spline.Knots
    };

    private static void CheckKnotShape(Spline source, Spline result)
    {
        Equal(source.Degree, result.Degree, "refinement degree");
        Equal(source.IsClosed, result.IsClosed, "refinement closure");
        Check(!result.IsClosedPeriodic, "nonperiodic refinement");
        var before = source.PolygonalVertexes(33); var after = result.PolygonalVertexes(33);
        double scale = source.ControlPoints.SelectMany(p => new[] { Math.Abs(p.X), Math.Abs(p.Y), Math.Abs(p.Z) }).Max();
        double tolerance = Math.Max(double.Epsilon, scale * 4e-14);
        for (int i = 0; i < before.Count; i++)
        {
            Check(double.IsFinite(after[i].X) && Math.Abs(before[i].X - after[i].X) <= tolerance, "refinement changed X");
            Check(double.IsFinite(after[i].Y) && Math.Abs(before[i].Y - after[i].Y) <= tolerance, "refinement changed Y");
            Check(double.IsFinite(after[i].Z) && Math.Abs(before[i].Z - after[i].Z) <= tolerance, "refinement changed Z");
        }
    }

    private static void KnotModel(int degree, int kind, bool repeated, int times)
    {
        var source = KnotSubject(degree, kind);
        var oldPoints = source.ControlPoints; var oldWeights = source.Weights; var oldKnots = source.Knots;
        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
        double parameter = KnotCoordinate(repeated ? .5 : .125, kind);
        var result = InsertSplineKnot(source, parameter, times);
        CheckKnotShape(source, result);
        Equal(source.ControlPoints.Length + times, result.ControlPoints.Length, "new control count");
        Equal(source.Knots.Length + times, result.Knots.Length, "new knot count");
        Equal((repeated ? 1 : 0) + times, result.Knots.Count(k => k == parameter), "additional knot multiplicity");
        Check(snapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))), "source arrays changed");
        Check(ReferenceEquals(oldPoints, source.ControlPoints) && ReferenceEquals(oldKnots, source.Knots)
            && ReferenceEquals(oldWeights, source.Weights), "source containers changed");
        Check(!ReferenceEquals(oldPoints, result.ControlPoints) && !ReferenceEquals(oldKnots, result.Knots)
            && !ReferenceEquals(oldWeights, result.Weights), "result aliases source arrays");
        Check(DirectionBits(source.ControlPoints[0]).SequenceEqual(DirectionBits(result.ControlPoints[0]))
            && DirectionBits(source.ControlPoints[^1]).SequenceEqual(DirectionBits(result.ControlPoints[^1])), "unaffected endpoint bits changed");
    }

    private static void KnotMetadata(int owner, bool fitted)
    {
        Spline source = fitted ? new Spline(new[] { new Vector3(1, 2, 3), new Vector3(3, 5, 7), new Vector3(7, 1, 2) }) : KnotSubject(3, 0);
        source.StartTangent = new Vector3(2, -4, 8); source.EndTangent = new Vector3(-3, 6, 9);
        source.KnotTolerance = 1e-9; source.CtrlPointTolerance = 2e-8; source.FitTolerance = 3e-9;
        ProjectionAppearance(source); NormalFixtureEditAndRestore(source, new Vector3(2, -3, 6));
        var doc = new DxfDocument();
        if (owner == 1) new Block("KNOT_DETACHED").Entities.Add(source);
        if (owner == 2) doc.Entities.Add(source);
        var oldOwner = source.Owner; var handle = source.Handle; var proxy = source.ProxyGraphics!.ToArray();
        double parameter = (source.Knots[source.Degree] + source.Knots[source.ControlPoints.Length]) * .125;
        var result = InsertSplineKnot(source, parameter, 2); var sibling = InsertSplineKnot(source, parameter, 2);
        ProjectionAppearanceCheck(source, result); CheckKnotShape(source, result);
        Equal(source.CreationMethod, result.CreationMethod, "fit creation method");
        Equal(source.StartTangent, result.StartTangent, "start tangent"); Equal(source.EndTangent, result.EndTangent, "end tangent");
        Equal(source.KnotTolerance, result.KnotTolerance, "knot tolerance");
        Equal(source.CtrlPointTolerance, result.CtrlPointTolerance, "control tolerance");
        Equal(source.FitTolerance, result.FitTolerance, "fit tolerance");
        Check(source.FitPoints.SequenceEqual(result.FitPoints) && !ReferenceEquals(source.FitPoints, result.FitPoints), "fit points not isolated");
        Check(!ReferenceEquals(result.Layer, sibling.Layer) && !ReferenceEquals(result.XData["CURVE_PROJECTION"], sibling.XData["CURVE_PROJECTION"]), "sibling metadata alias");
        Check(proxy.SequenceEqual(source.ProxyGraphics!) && ReferenceEquals(oldOwner, source.Owner) && handle == source.Handle, "owned source changed");
        result.ControlPoints[0] = new Vector3(91, 92, 93);
        Check(sibling.ControlPoints[0] == source.ControlPoints[0], "sibling geometry alias");
    }

    private static void KnotWire(int degree, bool repeated, int times, DxfVersion version, bool binary)
    {
        var source = KnotSubject(degree, 0); ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        double parameter = repeated ? .5 : .125;
        var result = InsertSplineKnot(source, parameter, times);
        var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(result);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "refined spline save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-knot-wire-{degree}-{repeated}-{times}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var curves = loaded.Entities.Splines.ToArray();
        Equal(2, curves.Length, "spline wire inventory");
        CheckKnotShape(curves[0], curves[1]);
        Check(result.Knots.SequenceEqual(curves[1].Knots) && result.Weights.SequenceEqual(curves[1].Weights), "wire knots/weights changed");
        Check(result.ControlPoints.SelectMany(DirectionBits).SequenceEqual(curves[1].ControlPoints.SelectMany(DirectionBits)), "wire controls changed");
        Equal(0, loaded.Objects.Validate().Count, "refinement wire graph");
    }

    private sealed class KnotDerived : Spline
    {
        public KnotDerived() : base(new[] { Vector3.Zero, Vector3.UnitX }, null, (short)1, false) { }
    }

    private static void RegisterSplineKnotInsertionTests()
    {
        for (int d = 1; d <= 10; d++) for (int k = 0; k < 9; k++)
        {
            int degree = d, kind = k;
            Run($"spline-knot/model/{d}/{k}/single", () => KnotModel(degree, kind, false, 1));
            if (d > 1)
            {
                Run($"spline-knot/model/{d}/{k}/multiple", () => KnotModel(degree, kind, false, degree));
                Run($"spline-knot/model/{d}/{k}/existing", () => KnotModel(degree, kind, true, degree - 1));
            }
        }
        for (int o = 0; o < 3; o++) foreach (bool fit in new[] { false, true })
        { int owner = o; Run($"spline-knot/metadata/{o}/{fit}", () => KnotMetadata(owner, fit)); }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0, 0.0, 1.0, 2.0 })
            Run($"spline-knot/parameter/{BitConverter.DoubleToInt64Bits(bad):X16}", () => Throws<ArgumentOutOfRangeException>(() => InsertSplineKnot(KnotSubject(3, 0), bad, 1)));
        foreach (int times in new[] { int.MinValue, -1, 0, 4, int.MaxValue })
            Run($"spline-knot/times/{times}", () => Throws<ArgumentOutOfRangeException>(() => InsertSplineKnot(KnotSubject(3, 0), .125, times)));
        Run("spline-knot/excess-multiplicity", () => Throws<ArgumentOutOfRangeException>(() => InsertSplineKnot(KnotSubject(3, 0), .5, 3)));
        for (int i = 0; i < 12; i++)
        {
            int fault = i;
            Run($"spline-knot/reject/{i}", () =>
            {
                Spline source = fault == 11 ? new KnotDerived() : KnotSubject(3, 0);
                source.ProxyGraphics = new byte[] { 3, 7, 19 };
                if (fault == 0) source.Weights[2] = 0;
                if (fault == 1) source.Weights[2] = -1;
                if (fault == 2) source.Weights[2] = double.NaN;
                if (fault == 3) source.ControlPoints[2] = new Vector3(double.PositiveInfinity, 0, 0);
                if (fault == 4) source.Knots[5] = -5;
                if (fault == 5) source.Knots[5] = double.NaN;
                if (fault == 6) source.KnotTolerance = double.NaN;
                if (fault == 7) source.StartTangent = new Vector3(double.NaN, 0, 0);
                if (fault == 8) source.PersistentReactors.Add(new Line());
                if (fault == 9) source.IsClosedPeriodic = true;
                if (fault == 10) typeof(EntityObject).GetField("normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, Vector3.Zero);
                var points = source.ControlPoints.SelectMany(DirectionBits).ToArray(); var knots = source.Knots.Select(BitConverter.DoubleToInt64Bits).ToArray();
                bool rejected = false;
                try { InsertSplineKnot(source, .125, 1); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { rejected = true; }
                Check(rejected, "invalid refinement admitted");
                Check(points.SequenceEqual(source.ControlPoints.SelectMany(DirectionBits)) && knots.SequenceEqual(source.Knots.Select(BitConverter.DoubleToInt64Bits)), "rejection mutated source");
                Check(source.ProxyGraphics!.SequenceEqual(new byte[] { 3, 7, 19 }), "rejection cleared source proxy");
            });
        }
        Run("spline-knot/budget", () =>
        {
            Check(typeof(Spline).GetField("MaximumRefinedControlPoints") != null, "refinement allocation bound missing");
            var source = KnotSubject(3, 0);
            typeof(Spline).GetField("controlPoints", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, new Vector3[1000000]);
            Throws<NotSupportedException>(() => InsertSplineKnot(source, .125, 1));
        });
        foreach (int degree in new[] { 2, 3 }) foreach (bool repeated in new[] { false, true })
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            {
                int times = repeated ? degree - 1 : degree;
                Run($"spline-knot/wire/{degree}/{repeated}/{version}/{binary}", () => KnotWire(degree, repeated, times, version, binary));
            }
        Run("spline-knot/numerical-corpus", () =>
        {
            var rows = new List<object>();
            for (int degree = 1; degree <= 10; degree++) for (int kind = 0; kind < 9; kind++)
            {
                var source = KnotSubject(degree, kind); double parameter = KnotCoordinate(.125, kind);
                var result = InsertSplineKnot(source, parameter, degree);
                rows.Add(new { degree, kind, parameter, times = degree, source = KnotPacket(source), result = KnotPacket(result) });
            }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-knot-numerics.json"), JsonSerializer.Serialize(rows));
        });
    }
}
