using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchSeedPointTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int size in new[] { 0, 1, 3 })
                    foreach (int location in new[] { 0, 1, 2 })
                    {
                        int n = size; int l = location;
                        Run($"hatch/seeds/wire/{v}/{b}/{n}/{l}", () => HatchSeedsWire(v, b, n, l));
                    }
                for (int problem = 0; problem < 8; problem++)
                {
                    int p = problem;
                    Run($"hatch/seeds/invalid/{v}/{b}/{p}", () => HatchSeedsInvalid(v, b, p));
                }
            }
    }

    private static readonly Vector2[] SeedValues =
    {
        new(2.5, 3.75), new(-1e-20, double.Epsilon), new(2.5, 3.75)
    };

    private static List<DxfTag> HatchSeedTags(DxfVersion version, int size, int location, bool comments)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 1);
        int start = tags.FindIndex(t => t.Code == 98);
        tags.RemoveRange(start, 3);
        if (location == 1) start = tags.FindIndex(t => t.Code == 75);
        if (location == 2) start = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        var seeds = new List<DxfTag> { new(98, size) };
        foreach (Vector2 seed in SeedValues.Take(size))
        {
            if (comments) seeds.Add(new(999, "10 ENDSEC"));
            seeds.Add(new(10, seed.X));
            if (comments) seeds.Add(new(999, "20 EOF"));
            seeds.Add(new(20, seed.Y));
        }
        tags.InsertRange(start, seeds);
        return tags;
    }

    private static Vector2[] EmittedHatchSeeds(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var raw = DxfRawDocument.Load(input);
        var hatch = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "HATCH");
        var tags = hatch.Tags.Where(t => t.Code != 999).ToArray();
        int i = Array.FindIndex(tags, t => t.Code == 98);
        Check(i >= 0, "Missing HATCH seed-point count.");
        int count = (int)tags[i].Value;
        var values = new List<Vector2>();
        for (int n = 0; n < count; n++)
        {
            Equal((short)10, tags[++i].Code, "Seed X group"); double x = (double)tags[i].Value;
            Equal((short)20, tags[++i].Code, "Seed Y group"); double y = (double)tags[i].Value;
            values.Add(new Vector2(x, y));
        }
        return values.ToArray();
    }

    private static void EqualHatchSeeds(IEnumerable<Vector2> expected, IEnumerable<Vector2> actual)
    {
        Vector2[] a = expected.ToArray(), b = actual.ToArray();
        Equal(a.Length, b.Length, "HATCH seed-point count");
        for (int i = 0; i < a.Length; i++)
        {
            SameDoubleBits(a[i].X, b[i].X, "Seed X bits/order");
            SameDoubleBits(a[i].Y, b[i].Y, "Seed Y bits/order");
        }
    }

    private static void HatchSeedsWire(DxfVersion version, bool binary, int size, int location)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchSeedTags(version, size, location, !binary), binary));
        DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid HATCH seed fixture failed to load.");
        Check(input.CanRead, "Seed reader closed caller stream.");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            Hatch original = doc.Entities.Hatches.Single();
            Equal(2.5, original.Elevation, "Seeds changed HATCH elevation");
            Equal("after pattern", original.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Seeds disrupted XData");
            var clone = (Hatch)original.Clone();
            var next = new DxfDocument(version); next.Entities.Add(clone);
            using var output = new MemoryStream();
            bool transport = cycle == 1 ? binary : !binary;
            Check(next.Save(output, transport), "Seed fixture failed to save.");
            EqualHatchSeeds(SeedValues.Take(size), EmittedHatchSeeds(output.ToArray()));
            if (cycle == 0 && size == 3 && location == 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-seeds-{version}-{transport}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Seed fixture failed to reload.");
        }
    }

    private static void HatchSeedsInvalid(DxfVersion version, bool binary, int problem)
    {
        var tags = HatchSeedTags(version, 1, 0, false);
        int start = tags.FindIndex(t => t.Code == 98);
        switch (problem)
        {
            case 0: tags[start] = new(98, -1); break;
            case 1: tags.RemoveRange(start + 1, 2); break;
            case 2: tags[start + 1] = new(11, 1.0); break;
            case 3: tags[start + 2] = new(21, 1.0); break;
            case 4: tags[start] = new(98, 2); break;
            case 5: tags.Insert(start + 3, new(98, 0)); break;
            case 6: tags.RemoveAt(start + 2); break;
            default: tags[start] = new(98, int.MaxValue); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Malformed HATCH seeds were accepted."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH", StringComparison.Ordinal) && error.Message.Contains("seed", StringComparison.Ordinal),
                "Seed diagnostic must identify its entity and field.");
        }
#else
        Check(DxfDocument.Load(input) == null, "Malformed HATCH seeds were accepted.");
#endif
        Check(input.CanRead, "Malformed seeds closed caller stream.");
    }
}
