// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Entities;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly double[] EllipseSampleScales = { 1e-310, 1e-200, 1.0, 1e200, double.MaxValue };
    private static readonly (double Start, double End)[] EllipseSampleAngles = { (0, 0), (0, 90), (90, 270), (180, 270), (270, 90), (25, 215) };

    private static Ellipse SamplingEllipse(int scale, int sweep, int rotation)
    {
        var angles = EllipseSampleAngles[sweep];
        return new Ellipse(Vector3.Zero, EllipseSampleScales[scale], EllipseSampleScales[scale] * .25)
        { StartAngle = angles.Start, EndAngle = angles.End, Rotation = rotation * 45 };
    }

    private static void CheckEllipseSample(Ellipse ellipse)
    {
        var points = ellipse.PolygonalVertexes(17);
        Equal(17, points.Count, "ellipse sample count");
        double radians = ellipse.Rotation * MathHelper.DegToRad, c = Math.Cos(radians), s = Math.Sin(radians);
        double a = ellipse.MajorAxis * .5, b = ellipse.MinorAxis * .5;
        double relative = Math.Max(2e-13, 16 * double.Epsilon / b);
        foreach (Vector2 point in points)
        {
            Check(double.IsFinite(point.X) && double.IsFinite(point.Y), "ellipse emitted nonfinite sample");
            double x = point.X / a, y = point.Y / a;
            double u = x * c + y * s, v = (y * c - x * s) * (a / b);
            Check(Math.Abs(u * u + v * v - 1) <= relative, "sample left ellipse locus");
        }
        if (ellipse.StartAngle == 180 && ellipse.Rotation == 0)
        { Equal(-a, points[0].X, "negative major endpoint"); Equal(0.0, points[0].Y, "major endpoint Y"); }
        if (!ellipse.IsFullEllipse && ellipse.EndAngle == 90 && ellipse.Rotation == 0)
        { Equal(0.0, points[^1].X, "minor endpoint X"); Equal(b, points[^1].Y, "positive minor endpoint"); }
        var again = ellipse.PolygonalVertexes(17);
        Check(points.SequenceEqual(again), "sampling mutated source or changed deterministic output");
    }

    private static void RegisterEllipseSamplingTests()
    {
        for (int scale = 0; scale < EllipseSampleScales.Length; scale++)
            for (int sweep = 0; sweep < EllipseSampleAngles.Length; sweep++)
                for (int rotation = 0; rotation < 4; rotation++)
                {
                    int a = scale, b = sweep, c = rotation;
                    Run($"ellipse-sampling/locus/{a}/{b}/{c}", () => CheckEllipseSample(SamplingEllipse(a, b, c)));
                }
        foreach (int count in new[] { int.MinValue, -1, 0, 1, 1000001, int.MaxValue })
            Run($"ellipse-sampling/budget/{count}", () =>
            {
                // Do not issue a dangerous request to a preceding unbounded library.
                Check(typeof(Ellipse).GetField("MaximumSampledVertices") != null, "missing sampling allocation limit");
                Throws<ArgumentOutOfRangeException>(() => new Ellipse().PolygonalVertexes(count));
            });
        foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            foreach (string property in new[] { "rotation", "startAngle", "endAngle", "majorAxis", "minorAxis" })
                Run($"ellipse-sampling/invalid/{property}/{BitConverter.DoubleToInt64Bits(invalid):X16}", () =>
                {
                    var e = new Ellipse();
                    typeof(Ellipse).GetField(property, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(e, invalid);
                    Throws<ArgumentException>(() => e.PolygonalVertexes(3));
                });
        Run("ellipse-sampling/unrepresentable-semi-axis", () =>
            Throws<NotSupportedException>(() => new Ellipse(Vector3.Zero, double.Epsilon, double.Epsilon).PolygonalVertexes(3)));
        Run("ellipse-sampling/ratio-underflow-quadrants", () =>
        {
            var e = new Ellipse(Vector3.Zero, 1e308, 1e-300) { StartAngle = 180, EndAngle = 270 };
            var p = e.PolygonalVertexes(2);
            Equal(-5e307, p[0].X, "thin major endpoint"); Equal(0.0, p[0].Y, "thin major Y");
            Equal(0.0, p[1].X, "thin minor X"); Equal(-5e-301, p[1].Y, "thin minor endpoint");
        });
        Run("ellipse-sampling/ratio-underflow-nonquadrant", () =>
            Throws<NotSupportedException>(() => new Ellipse(Vector3.Zero, 1e308, 1e-300) { StartAngle = 25, EndAngle = 90 }.PolygonalVertexes(3)));
        Run("ellipse-sampling/numerical-evidence", () =>
        {
            var rows = new List<object>();
            for (int a = 0; a < EllipseSampleScales.Length; a++)
                for (int b = 0; b < EllipseSampleAngles.Length; b++)
                    for (int c = 0; c < 4; c++)
                    {
                        var e = SamplingEllipse(a, b, c); CheckEllipseSample(e);
                        rows.Add(new { scale = a, sweep = b, rotation = c,
                            points = e.PolygonalVertexes(17).Select(p => new[] { p.X, p.Y }).ToArray() });
                    }
            File.WriteAllText(Path.Combine(ArtifactDirectory, "ellipse-sampling-numerics.json"), JsonSerializer.Serialize(rows));
        });
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            for (int sweep = 0; sweep < EllipseSampleAngles.Length; sweep++)
            {
                int kind = sweep;
                Run($"ellipse-sampling/wire/{version}/{binary}/{kind}", () =>
                {
                    var source = SamplingEllipse(2, kind, 1);
                    var poly = source.ToPolyline2D(17); var doc = new DxfDocument(version); doc.Entities.Add(poly);
                    using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "ellipse sampling save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ellipse-sampling-{version}-{binary}-{kind}.dxf"), stream.ToArray());
                    stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
                    Equal(17, loaded.Entities.Polylines2D.Single().Vertexes.Count, "ellipse sampled wire count");
                });
            }
    }
}
