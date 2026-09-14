using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchGradientAciTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (HatchGradientPatternType type in Enum.GetValues<HatchGradientPatternType>())
                    foreach (bool single in new[] { false, true })
                        for (int presence = 0; presence < 4; presence++)
                        {
                            DxfVersion v = version; bool b = binary, s = single; HatchGradientPatternType t = type; int p = presence;
                            Run($"hatch/gradient-aci/wire/{v}/{b}/{t}/{s}/{p}", () => HatchGradientAciRoundTrip(v, b, t, s,
                                (p & 1) != 0 ? (short)17 : null, (p & 2) != 0 ? (short)231 : null, p));
                        }
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            foreach (bool binary in new[] { false, true })
                foreach (short index in new short[] { short.MinValue, -1, 0, 1, 255, 256, short.MaxValue })
                {
                    DxfVersion v = version; bool b = binary; short i = index;
                    Run($"hatch/gradient-aci/int16/{v}/{b}/{i}", () => HatchGradientAciRoundTrip(v, b,
                        HatchGradientPatternType.Linear, false, i, i, -1));
                }
    }

    private static List<DxfTag> HatchGradientAciTags(DxfVersion version, HatchGradientPatternType type,
        bool single, short? index1, short? index2, bool reverseComponents)
    {
        var tags = HatchGradientColorStateTags(version, type, single, 0.35);
        tags.RemoveAll(t => t.Code == 63);
        if (index1.HasValue)
            tags.Insert(tags.FindIndex(t => t.Code == 463) + (reverseComponents ? 2 : 1), new(63, index1.Value));
        if (index2.HasValue)
            tags.Insert(tags.FindLastIndex(t => t.Code == 463) + (reverseComponents ? 2 : 1), new(63, index2.Value));
        return tags;
    }

    private static void HatchGradientAciRoundTrip(DxfVersion version, bool binary, HatchGradientPatternType type,
        bool single, short? index1, short? index2, int fixturePresence)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientAciTags(version, type, single, index1, index2, binary), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Gradient ACI input rejected.");
        Hatch original = doc.Entities.Hatches.Single();
        AssertGradientColorState((HatchGradientPattern)original.Pattern, type, single, 0.35);
        var block = new Block("GradientAci"); block.Entities.Add((Hatch)original.Clone());
        var insert = (Insert)new Insert(block, Vector3.Zero).Clone();
        doc.Entities.Add(insert.Explode().OfType<Hatch>().Single());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle % 2 == 0 ? !binary : binary), "Gradient ACI save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            var hatches = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray();
            Equal(2, hatches.Length, "ACI original and nested/exploded clone count");
            if (version == DxfVersion.AutoCad2000)
            {
                Check(hatches.SelectMany(h => h.Tags).All(t => t.Code is not 450 and not 463 and not 63), "AC1015 lossy downgrade changed.");
                break;
            }
            foreach (var hatch in hatches) AssertGradientAciTags(hatch.Tags, index1, index2);
            if (cycle == 1 && type == HatchGradientPatternType.Linear && single && fixturePresence >= 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-gradient-aci-{version}-{binary}-{fixturePresence}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Gradient ACI reload failed.");
            foreach (Hatch hatch in doc.Entities.Hatches)
            {
                AssertGradientColorState((HatchGradientPattern)hatch.Pattern, type, single, 0.35);
                Equal(2.5, hatch.Elevation, "ACI elevation");
                Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "ACI seed");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "ACI XData");
                Equal(1, hatch.BoundaryPaths.Count, "ACI boundary count");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "ACI following LINE");
        }
        Check(input.CanRead, "ACI reader closed caller-owned stream.");
    }

    private static void AssertGradientAciTags(IEnumerable<DxfTag> tags, short? index1, short? index2)
    {
        var indices = new List<short?>(); var rgb = new List<int>(); int stop = -1;
        foreach (DxfTag tag in tags)
        {
            if (tag.Code == 463) { indices.Add(null); stop++; }
            else if (tag.Code == 63)
            {
                Check(stop >= 0 && !indices[stop].HasValue, "ACI stop is missing or duplicated.");
                indices[stop] = (short)tag.Value;
            }
            else if (tag.Code == 421) rgb.Add((int)tag.Value & 0xFFFFFF);
        }
        Check(indices.SequenceEqual(new short?[] { index1, index2 }),
            $"Authored optional ACI metadata changed: expected [{index1?.ToString() ?? "absent"},{index2?.ToString() ?? "absent"}], " +
            $"found [{string.Join(",", indices.Select(i => i?.ToString() ?? "absent"))}].");
        Check(rgb.SequenceEqual(new[] { 0x123456, 0xABCDEF }), "ACI assignment replaced the authoritative RGB stops.");
    }
}
