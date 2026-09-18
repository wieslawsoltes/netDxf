// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly double[] AxisReviewEpsilons = { 1e-15, 1e-12, .1, 1.0, 100.0 };

    private static Vector3[] AxisReviewInputs()
    {
        var values = new List<Vector3>();
        foreach (int exponent in new[] { -1074, -1022, -1000, -600, -500, -50, 0, 50, 500, 600, 1000, 1021 })
        {
            double s = Math.ScaleB(1.0, exponent);
            values.Add(new(s, -2 * s, 3 * s));
            values.Add(new(0, 0, s));
            values.Add(new(0, 0, -s));
        }
        values.AddRange(new[] {
            new Vector3(1e-13, 2e-13, 1), new Vector3(-1e-13, -2e-13, 1),
            new Vector3(1e-13, 0, -1), new Vector3(0, 1e-13, 1),
            new Vector3(0, 1, 0), new Vector3(1, 0, 0),
            new Vector3(double.MaxValue, double.MaxValue, double.MaxValue),
            new Vector3(double.Epsilon, 0, double.MaxValue),
            new Vector3(.015624, .015624, Math.Sqrt(1 - 2 * .015624 * .015624)),
            new Vector3(.015626, .015624, Math.Sqrt(1 - .015626 * .015626 - .015624 * .015624)),
            new Vector3(.015624, .015626, Math.Sqrt(1 - .015624 * .015624 - .015626 * .015626))
        });
        return values.ToArray();
    }

    private static double[] AxisReviewValues(Matrix3 m)
    {
        var result = new double[9];
        for (int row = 0; row < 3; row++) for (int column = 0; column < 3; column++) result[row * 3 + column] = m[row, column];
        return result;
    }

    private static void AxisReviewComponent(double expected, double actual)
    {
        Check(double.IsFinite(actual), "OCS component is nonfinite");
        // A relative bound deliberately has no unit-sized absolute floor: a real
        // small tilt may not disappear behind the global geometry epsilon.
        double tolerance = Math.Max(double.Epsilon * 16, Math.Abs(expected) * 4e-15);
        Check(Math.Abs(expected - actual) <= tolerance, $"OCS component changed: {expected:R} != {actual:R}");
    }

    private static void AxisReviewFrame(Vector3 source)
    {
        Vector3 before = source;
        Matrix3 basis = MathHelper.ArbitraryAxis(source);
        var z = new Vector3(basis.M13, basis.M23, basis.M33);
        var x = new Vector3(basis.M11, basis.M21, basis.M31);
        var y = new Vector3(basis.M12, basis.M22, basis.M32);
        double scale = Math.Max(Math.Abs(source.X), Math.Max(Math.Abs(source.Y), Math.Abs(source.Z)));
        var expected = new Vector3(source.X / scale, source.Y / scale, source.Z / scale);
        double length = Math.Sqrt(expected.X * expected.X + expected.Y * expected.Y + expected.Z * expected.Z);
        expected = new Vector3(expected.X / length, expected.Y / length, expected.Z / length);
        AxisReviewComponent(expected.X, z.X); AxisReviewComponent(expected.Y, z.Y); AxisReviewComponent(expected.Z, z.Z);
        Check(AxisReviewValues(basis).All(double.IsFinite), "OCS matrix is nonfinite");
        foreach (var axis in new[] { x, y, z }) Check(Math.Abs(Vector3.DotProduct(axis, axis) - 1) <= 4e-15, "Non-unit OCS basis");
        Check(Math.Abs(Vector3.DotProduct(x, y)) <= 4e-15 && Math.Abs(Vector3.DotProduct(x, z)) <= 4e-15 && Math.Abs(Vector3.DotProduct(y, z)) <= 4e-15, "Nonorthogonal OCS basis");
        Vector3 cross = Vector3.CrossProduct(x, y);
        Check((cross - z).Modulus() <= 4e-15, "OCS basis is not right-handed");
        // Check the actual reference-axis choice, not just any orthogonal basis.
        Vector3 reference = Math.Abs(expected.X) < 1.0 / 64 && Math.Abs(expected.Y) < 1.0 / 64 ? Vector3.UnitY : Vector3.UnitZ;
        var wantedX = Vector3.CrossProduct(reference, expected);
        double xLength = Math.Sqrt(Vector3.DotProduct(wantedX, wantedX));
        AxisReviewComponent(wantedX.X / xLength, x.X); AxisReviewComponent(wantedX.Y / xLength, x.Y); AxisReviewComponent(wantedX.Z / xLength, x.Z);
        Check(DirectionBits(before).SequenceEqual(DirectionBits(source)), "Input normal was mutated");

        var point = new Vector3(2, -3, 5);
        Vector3 world = MathHelper.Transform(point, source, CoordinateSystem.Object, CoordinateSystem.World);
        Vector3 back = MathHelper.Transform(world, source, CoordinateSystem.World, CoordinateSystem.Object);
        Check((back - point).Modulus() <= 1e-13, "OCS/WCS roundtrip failed");
        Check(DirectionBits(world).SequenceEqual(DirectionBits(MathHelper.Transform(new Vector2(2, -3), source, 5))), "2D transform disagrees");
        var list = MathHelper.Transform(new[] { point }, source, CoordinateSystem.Object, CoordinateSystem.World);
        Check(DirectionBits(world).SequenceEqual(DirectionBits(list.Single())), "3D enumerable transform disagrees");
        var list2 = MathHelper.Transform(new[] { new Vector2(2, -3) }, source, 5);
        Check(DirectionBits(world).SequenceEqual(DirectionBits(list2.Single())), "2D enumerable transform disagrees");
        Vector2 local = MathHelper.Transform(world, source, out double elevation);
        Check(Math.Abs(local.X - 2) <= 1e-13 && Math.Abs(local.Y + 3) <= 1e-13 && Math.Abs(elevation - 5) <= 1e-13, "Elevation transform failed");
    }

    private static Vector3 AxisReviewWireNormal(int index) => index switch
    {
        0 => new(1e-13, 2e-13, 1), 1 => new(-1e-13, -2e-13, 1),
        2 => new(1e-13, 0, -1), 3 => new(0, 1e-13, 1),
        4 => new(0, 1, 0), 5 => new(2, 3, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    private static void AxisReviewWire(DxfVersion version, bool binary, bool nested, int index)
    {
        Vector3 normal = AxisReviewWireNormal(index);
        var circle = new Circle(new Vector3(1e12, -2e12, 3e12), 7) { Normal = normal };
        var text = new Text("OCS tilt", new Vector3(-4e12, 5e12, -6e12), 2) { Normal = normal, Rotation = 37 };
        var arc = new Arc(new Vector3(7e12, -8e12, 9e12), 5, 20, 210) { Normal = normal };
        var doc = new DxfDocument(version);
        if (nested)
        {
            var block = new Block("AXIS_REVIEW"); block.Entities.Add(circle); block.Entities.Add(text); block.Entities.Add(arc);
            doc.Entities.Add(new Insert(block));
        }
        else doc.Entities.Add(new EntityObject[] { circle, text, arc });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "OCS wire save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"ocs-axis-{version}-{binary}-{nested}-{index}.dxf"), output.ToArray());
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("OCS wire load failed");
        IEnumerable<EntityObject> entities = nested ? loaded.Blocks["AXIS_REVIEW"].Entities : loaded.Entities.All;
        Check((entities.OfType<Circle>().Single().Center - circle.Center).Modulus() <= .02, "Circle WCS center changed");
        Check((entities.OfType<Text>().Single().Position - text.Position).Modulus() <= .02, "Text WCS position changed");
        Check((entities.OfType<Arc>().Single().Center - arc.Center).Modulus() <= .02, "Arc WCS center changed");
    }

    private static void AxisReviewNumerics()
    {
        var rows = new List<object>();
        foreach (Vector3 source in AxisReviewInputs())
            rows.Add(new { input = DirectionBits(source), matrix = AxisReviewValues(MathHelper.ArbitraryAxis(source))
                .Select(v => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("X16")).ToArray() });
        File.WriteAllText(Path.Combine(ArtifactDirectory, "ocs-axis-numerics.json"), JsonSerializer.Serialize(rows));
    }

    private static void RegisterArbitraryAxisReviewTests()
    {
        var inputs = AxisReviewInputs();
        for (int n = 0; n < inputs.Length; n++) foreach (double epsilon in AxisReviewEpsilons)
        {
            int index = n;
            Run($"ocs-axis/model/{index}/{epsilon:R}", () =>
            {
                double previous = MathHelper.Epsilon;
                try { MathHelper.Epsilon = epsilon; AxisReviewFrame(inputs[index]); }
                finally { MathHelper.Epsilon = previous; }
            });
        }
        for (int sample = 0; sample < 5; sample++) for (int op = 0; op < 3; op++)
        {
            int s = sample, operation = op;
            Run($"ocs-axis/cached/{s}/{operation}", () => AxisReviewFrame(LegacyCachedDirection(s, operation)));
        }
        var bad = new List<Vector3>();
        for (int mask = 0; mask < 8; mask++) bad.Add(new((mask & 1) == 0 ? 0.0 : -0.0, (mask & 2) == 0 ? 0.0 : -0.0, (mask & 4) == 0 ? 0.0 : -0.0));
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            for (int axis = 0; axis < 3; axis++) { var v = new Vector3(1, 2, 3); v[axis] = value; bad.Add(v); }
        for (int n = 0; n < bad.Count; n++)
        {
            int index = n;
            Run($"ocs-axis/reject/{index}", () =>
            {
                ArgumentException? failure = null;
                try { MathHelper.ArbitraryAxis(bad[index]); } catch (ArgumentException ex) { failure = ex; }
                Check(failure != null, "Invalid OCS normal was accepted"); Equal("zAxis", failure!.ParamName, "OCS failure parameter");
            });
        }
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true }) foreach (bool nested in new[] { false, true })
            for (int n = 0; n < 6; n++) { int index = n; Run($"ocs-axis/wire/{version}/{binary}/{nested}/{index}", () => AxisReviewWire(version, binary, nested, index)); }
        Run("ocs-axis/numerics", AxisReviewNumerics);
    }
}
