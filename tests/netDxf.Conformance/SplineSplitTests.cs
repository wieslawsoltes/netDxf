// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline[] SplitSpline(Spline spline, double parameter)
        => (Spline[])Invoke(spline, "SplitAt", parameter)!;

    private static Spline SplitBrokenSubject(int degree)
    {
        int count = 2 * (degree + 1);
        var points = Enumerable.Range(0, count).Select(i => new Vector3(i - 2, i % 3, i * i % 7)).ToArray();
        var weights = Enumerable.Range(0, count).Select(i => 1.0 + (i % 4) * .25).ToArray();
        var knots = Enumerable.Repeat(0.0, degree + 1).Concat(Enumerable.Repeat(.5, degree + 1))
            .Concat(Enumerable.Repeat(1.0, degree + 1)).ToArray();
        return new Spline(points, weights, knots, (short)degree, false);
    }

    private static Vector3 SplitReferencePoint(Spline source, double parameter)
    {
        var method = typeof(Spline).GetMethod("NonPeriodicPoint", BindingFlags.Static | BindingFlags.NonPublic)!;
        return (Vector3)method.Invoke(null, new object[] { source.ControlPoints, source.Weights, source.Knots,
            (int)source.Degree, parameter, false })!;
    }

    private static void CheckSplit(Spline source, Spline[] parts, double parameter, bool broken)
    {
        Equal(2, parts.Length, "split result count");
        Equal(source.Knots[source.Degree], parts[0].Knots[parts[0].Degree], "left domain start");
        Equal(parameter, parts[0].Knots[parts[0].ControlPoints.Length], "left domain end");
        Equal(parameter, parts[1].Knots[parts[1].Degree], "right domain start");
        Equal(source.Knots[source.ControlPoints.Length], parts[1].Knots[parts[1].ControlPoints.Length], "right domain end");
        if (!broken)
        {
            Check(DirectionBits(parts[0].ControlPoints[^1]).SequenceEqual(DirectionBits(parts[1].ControlPoints[0])), "continuous split endpoints differ");
            Equal(parts[0].Weights[^1], parts[1].Weights[0], "continuous split weights differ");
        }
        else Check(parts[0].ControlPoints[^1] != parts[1].ControlPoints[0], "discontinuity was joined");
        double scale = source.ControlPoints.SelectMany(p => new[] { Math.Abs(p.X), Math.Abs(p.Y), Math.Abs(p.Z) }).Max();
        double tolerance = Math.Max(double.Epsilon, scale * 5e-14);
        for (int side = 0; side < 2; side++)
        {
            Spline part = parts[side]; Equal(source.Degree, part.Degree, "split degree");
            Check(part.CreationMethod == SplineCreationMethod.ControlPoints && part.FitPoints.Count == 0 && !part.IsClosedPeriodic, "split definition");
            double lo = part.Knots[part.Degree], hi = part.Knots[part.ControlPoints.Length];
            var samples = part.PolygonalVertexes(17); int intervals = part.IsClosed ? 17 : 16;
            for (int i = 0; i < samples.Count; i++)
            {
                if (broken && side == 0 && i == intervals) continue; // left-hand break limit is checked by the independent oracle
                double t = (double)i / intervals;
                double u = i == 0 ? lo : i == intervals ? hi : (1 - t) * lo + t * hi;
                Vector3 expected = SplitReferencePoint(source, u), actual = samples[i];
                Check(double.IsFinite(actual.X) && Math.Abs(actual.X - expected.X) <= tolerance, "split X geometry");
                Check(double.IsFinite(actual.Y) && Math.Abs(actual.Y - expected.Y) <= tolerance, "split Y geometry");
                Check(double.IsFinite(actual.Z) && Math.Abs(actual.Z - expected.Z) <= tolerance, "split Z geometry");
            }
        }
    }

    private static void SplitModel(int degree, int kind, double fraction)
    {
        var source = KnotSubject(degree, kind); double parameter = KnotCoordinate(fraction, kind);
        byte[] snapshot = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
        var parts = SplitSpline(source, parameter); CheckSplit(source, parts, parameter, false);
        Check(snapshot.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))), "split changed source");
        Check(!ReferenceEquals(parts[0].ControlPoints, parts[1].ControlPoints)
            && !ReferenceEquals(parts[0].Knots, source.Knots) && !ReferenceEquals(parts[1].Weights, source.Weights), "split arrays alias");
    }

    private static void SplitWire(int degree, bool broken, double parameter, DxfVersion version, bool binary)
    {
        var source = broken ? SplitBrokenSubject(degree) : KnotSubject(degree, 0); ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        var parts = SplitSpline(source, parameter);
        var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(parts);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "split wire save");
        string token = parameter == .125 ? "new" : "existing";
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-split-wire-{degree}-{broken}-{token}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var splines = loaded.Entities.Splines.ToArray();
        Equal(3, splines.Length, "split wire inventory"); CheckSplit(splines[0], splines.Skip(1).ToArray(), parameter, broken);
        Equal(0, loaded.Objects.Validate().Count, "split graph");
    }

    private static void RegisterSplineSplitTests()
    {
        for (int d = 1; d <= 10; d++) for (int k = 0; k < 8; k++) foreach (double fraction in new[] { .125, .5, .875 })
        {
            int degree = d, kind = k;
            Run($"spline-split/model/{d}/{k}/{fraction}", () => SplitModel(degree, kind, fraction));
        }
        for (int d = 1; d <= 10; d++)
        {
            int degree = d;
            Run($"spline-split/discontinuous/{d}", () =>
            {
                var source = SplitBrokenSubject(degree); var parts = SplitSpline(source, .5);
                CheckSplit(source, parts, .5, true);
                Check(parts[0].ControlPoints.SequenceEqual(source.ControlPoints.Take(degree + 1)), "broken left controls");
                Check(parts[1].ControlPoints.SequenceEqual(source.ControlPoints.Skip(degree + 1)), "broken right controls");
            });
        }
        for (int i = 0; i < 6; i++)
        {
            int fault = i;
            Run($"spline-split/reject/{i}", () =>
            {
                var source = fault == 0 ? KnotSubject(3, 8) : fault == 1
                    ? new Spline(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }) : KnotSubject(3, 0);
                if (fault == 2) source.StartTangent = Vector3.UnitX;
                if (fault == 3) source.EndTangent = Vector3.UnitY;
                if (fault == 4) source.PersistentReactors.Add(new Line());
                if (fault == 5) source.IsClosedPeriodic = true;
                byte[] before = JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source));
                Throws<NotSupportedException>(() => SplitSpline(source, .5));
                Check(before.SequenceEqual(JsonSerializer.SerializeToUtf8Bytes(KnotPacket(source))), "split rejection mutated source");
            });
        }
        foreach (double parameter in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -1.0, 0.0, 1.0, 2.0 })
            Run($"spline-split/parameter/{BitConverter.DoubleToInt64Bits(parameter):X16}", () => Throws<ArgumentOutOfRangeException>(() => SplitSpline(KnotSubject(3, 0), parameter)));
        Run("spline-split/metadata-owned", () =>
        {
            var source = KnotSubject(3, 0); ProjectionAppearance(source); var doc = new DxfDocument(); doc.Entities.Add(source);
            var owner = source.Owner; var handle = source.Handle; var proxy = source.ProxyGraphics!.ToArray();
            var parts = SplitSpline(source, .125);
            foreach (Spline part in parts) ProjectionAppearanceCheck(source, part);
            Check(!ReferenceEquals(parts[0].Layer, parts[1].Layer) && !ReferenceEquals(parts[0].XData["CURVE_PROJECTION"], parts[1].XData["CURVE_PROJECTION"]), "split sibling metadata alias");
            Check(ReferenceEquals(owner, source.Owner) && handle == source.Handle && proxy.SequenceEqual(source.ProxyGraphics!), "split owned source changed");
        });
        foreach (int degree in new[] { 2, 3 }) foreach (bool broken in new[] { false, true })
            foreach (double parameter in broken ? new[] { .5 } : new[] { .125, .5 })
                foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                    Run($"spline-split/wire/{degree}/{broken}/{parameter}/{version}/{binary}", () => SplitWire(degree, broken, parameter, version, binary));
        Run("spline-split/numerical-corpus", () =>
        {
            var rows = new List<object>();
            for (int degree = 1; degree <= 10; degree++) for (int kind = 0; kind < 8; kind++)
                foreach (double fraction in new[] { .125, .5, .875 })
                {
                    var source = KnotSubject(degree, kind); double parameter = KnotCoordinate(fraction, kind);
                    var parts = SplitSpline(source, parameter);
                    rows.Add(new { degree, kind, fraction, parameter, source = KnotPacket(source), parts = parts.Select(KnotPacket).ToArray() });
                }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "spline-split-numerics.json"), JsonSerializer.Serialize(rows));
        });
    }
}
