using System.Collections;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchSeedPointApiTests()
    {
        Run("hatch/seeds/api-validation", HatchSeedApiValidation);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"hatch/seeds/api-edit-clone/{v}/{b}", () => HatchSeedApiEdit(v, b));
                Run($"hatch/seeds/absent/{v}/{b}", () => HatchSeedAbsent(v, b));
                Run($"hatch/seeds/physical-eof/{v}/{b}", () => HatchSeedPhysicalEof(v, b));
            }
        foreach (Vector3 normal in new[] { Vector3.UnitZ, -Vector3.UnitZ, new Vector3(1, 2, 3) })
            foreach (int transform in Enumerable.Range(0, 4))
            {
                Vector3 n = normal; int t = transform;
                Run($"hatch/seeds/ocs-transform/{n}/{t}", () => HatchSeedTransform(n, t));
            }
        Run("hatch/seeds/insert-clone-explode", HatchSeedInsert);
    }

    private static Hatch NewSeedHatch()
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchSeedTags(DxfVersion.AutoCad2018, 3, 0, false), false));
        return (DxfDocument.Load(input) ?? throw new InvalidOperationException("Seed API fixture failed.")).Entities.Hatches.Single();
    }

    private static void HatchSeedApiValidation()
    {
        var hatch = new Hatch(HatchPattern.Line, false);
        EqualHatchSeeds(new[] { Vector2.Zero }, hatch.SeedPoints);
        hatch.SeedPoints.Clear(); hatch.SeedPoints.Add(new(1, 2));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            foreach (Vector2 point in new[] { new Vector2(bad, 0), new Vector2(0, bad) })
            {
                Throws<ArgumentOutOfRangeException>(() => hatch.SeedPoints.Add(point));
                Throws<ArgumentOutOfRangeException>(() => hatch.SeedPoints.Insert(0, point));
                Throws<ArgumentOutOfRangeException>(() => hatch.SeedPoints[0] = point);
                Throws<ArgumentOutOfRangeException>(() => ((IList)hatch.SeedPoints).Add(point));
                EqualHatchSeeds(new[] { new Vector2(1, 2) }, hatch.SeedPoints);
            }
        hatch.SeedPoints.Add(new(3, 4)); hatch.SeedPoints.Insert(1, new(5, 6));
        hatch.SeedPoints[0] = new(7, 8); hatch.SeedPoints.RemoveAt(2);
        EqualHatchSeeds(new[] { new Vector2(7, 8), new Vector2(5, 6) }, hatch.SeedPoints);
        var clone = (Hatch)hatch.Clone();
        Check(!ReferenceEquals(hatch.SeedPoints, clone.SeedPoints), "Seed clone aliases collection storage.");
        clone.SeedPoints.Clear(); Equal(2, hatch.SeedPoints.Count, "Clearing clone changed source");
        Check(clone.Owner == null && clone.Handle == null, "Clone copied database identity.");
    }

    private static void HatchSeedApiEdit(DxfVersion version, bool binary)
    {
        foreach (HatchPattern pattern in new HatchPattern[] { HatchPattern.Line, HatchPattern.Solid, new HatchGradientPattern() })
        {
            Hatch original = NewSeedHatch();
            EqualHatchSeeds(SeedValues, original.SeedPoints);
            original.Pattern = pattern; original.Pattern.Origin = new Vector2(21, 34);
            original.SeedPoints.Clear(); original.SeedPoints.Add(new(4.25, -8.5));
            original.SeedPoints.Add(new(BitConverter.Int64BitsToDouble(long.MinValue), 0));
            var clone = (Hatch)original.Clone();
            clone.SeedPoints[0] = new(9.75, -11.125);
            EqualHatchSeeds(new[] { new Vector2(4.25, -8.5), new Vector2(-0.0, 0) }, original.SeedPoints);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Vector2[] expected = clone.SeedPoints.ToArray();
                var doc = new DxfDocument(version); doc.Entities.Add(clone);
                using var output = new MemoryStream(); Check(doc.Save(output, cycle == 1 ? !binary : binary), "Seed API save failed.");
                EqualHatchSeeds(expected, EmittedHatchSeeds(output.ToArray()));
                EqualHatchSeeds(expected, clone.SeedPoints);
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Seed API reload failed.");
                clone = (Hatch)doc.Entities.Hatches.Single().Clone();
                EqualHatchSeeds(expected, clone.SeedPoints);
                Equal(new Vector2(21, 34), clone.Pattern.Origin, "Seeds overwrote pattern origin");
                Equal(2.5, clone.Elevation, "Seeds overwrote elevation");
                if (cycle == 1) clone.SeedPoints.Clear();
            }
        }
    }

    private static void HatchSeedAbsent(DxfVersion version, bool binary)
    {
        var tags = HatchSeedTags(version, 0, 0, false); tags.RemoveAll(t => t.Code == 98);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Absent seed-list input failed.");
        Equal(0, doc.Entities.Hatches.Single().SeedPoints.Count, "Absent input invented a seed");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Absent seed-list save failed.");
        Equal(0, EmittedHatchSeeds(output.ToArray()).Length, "Absent seed list did not normalize to zero count");
    }

    private static void HatchSeedPhysicalEof(DxfVersion version, bool binary)
    {
        var tags = HatchSeedTags(version, 1, 0, false);
        int start = tags.FindIndex(t => t.Code == 98);
        for (int offset = 1; offset <= 3; offset++)
        {
            using var input = new MemoryStream(RawFixtureBytes(tags.Take(start + offset), binary));
#if DEBUG
            Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
            Check(DxfDocument.Load(input) == null, "Truncated seed data was accepted.");
#endif
            Check(input.CanRead, "Truncated seeds closed caller stream.");
        }
    }

    private static Vector3 WorldSeed(Hatch hatch, Vector2 seed) =>
        MathHelper.Transform(new Vector3(seed.X, seed.Y, hatch.Elevation), hatch.Normal, CoordinateSystem.Object, CoordinateSystem.World);

    private static void CompareWorldSeeds(Vector3[] expected, Hatch actual)
    {
        Equal(expected.Length, actual.SeedPoints.Count, "Transformed seed count");
        for (int i = 0; i < expected.Length; i++)
        {
            Vector3 value = WorldSeed(actual, actual.SeedPoints[i]);
            Near(expected[i].X, value.X, "World seed X"); Near(expected[i].Y, value.Y, "World seed Y"); Near(expected[i].Z, value.Z, "World seed Z");
        }
    }

    private static void HatchSeedTransform(Vector3 normal, int transform)
    {
        Hatch hatch = NewSeedHatch(); hatch.Normal = normal;
        Matrix3 matrix = transform switch
        {
            0 => Matrix3.Identity,
            1 => Matrix3.RotationZ(Math.PI / 2),
            2 => Matrix3.RotationX(0.7) * Matrix3.RotationY(-0.3) * Matrix3.Scale(2),
            _ => Matrix3.Reflection(Vector3.UnitX)
        };
        Vector3 translation = new(10, -20, 30);
        Vector3[] expected = hatch.SeedPoints.Select(seed => matrix * WorldSeed(hatch, seed) + translation).ToArray();
        hatch.TransformBy(matrix, translation); CompareWorldSeeds(expected, hatch);
    }

    private static void HatchSeedInsert()
    {
        Hatch hatch = NewSeedHatch();
        var block = new Block("SeedBlock"); block.Entities.Add((Hatch)hatch.Clone());
        var insert = new Insert(block, new Vector3(10, 20, 30)) { Scale = new Vector3(2, 3, 1), Rotation = 30 };
        var clone = (Insert)insert.Clone();
        Hatch copied = clone.Block.Entities.OfType<Hatch>().Single();
        EqualHatchSeeds(hatch.SeedPoints, copied.SeedPoints); copied.SeedPoints.Clear();
        EqualHatchSeeds(SeedValues, hatch.SeedPoints); EqualHatchSeeds(SeedValues, block.Entities.OfType<Hatch>().Single().SeedPoints);
        Matrix3 matrix = Matrix3.RotationZ(Math.PI / 6) * Matrix3.Scale(2, 3, 1);
        Vector3[] expected = hatch.SeedPoints.Select(seed => matrix * WorldSeed(hatch, seed) + insert.Position).ToArray();
        Hatch exploded = insert.Explode().OfType<Hatch>().Single(); CompareWorldSeeds(expected, exploded);
        exploded.SeedPoints.Clear(); EqualHatchSeeds(SeedValues, block.Entities.OfType<Hatch>().Single().SeedPoints);
    }
}
