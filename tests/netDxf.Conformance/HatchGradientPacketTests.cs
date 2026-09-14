using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] GradientScalarCodes = { 450, 451, 452, 453, 460, 461, 462, 470 };
    private static bool IsGradientTag(DxfTag tag) => GradientScalarCodes.Contains(tag.Code) || tag.Code is 463 or 63 or 421;

    private static void RegisterHatchGradientPacketTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (HatchGradientPatternType type in Enum.GetValues<HatchGradientPatternType>())
                    for (int variant = 0; variant < 8; variant++)
                    {
                        HatchGradientPatternType t = type; int n = variant;
                        Run($"hatch/gradient-packet/valid/{v}/{b}/{t}/{n}", () => HatchGradientPacketValid(v, b, t, n));
                    }
                for (int fault = 0; fault < 34; fault++)
                {
                    int f = fault;
                    Run($"hatch/gradient-packet/invalid/{v}/{b}/{f}", () => HatchGradientPacketInvalid(v, b, f));
                }
                Run($"hatch/gradient-packet/solid/{v}/{b}", () => HatchGradientPacketSolid(v, b));
            }
    }

    private static List<DxfTag> GradientPacketTags(DxfVersion version, HatchGradientPatternType type, int variant)
    {
        var tags = HatchGradientColorStateTags(version, type, true, 0.35);
        var fields = tags.Where(t => GradientScalarCodes.Contains(t.Code)).ToList();
        tags.RemoveAll(IsGradientTag);
        var stops = new List<DxfTag> { new(463, 0.0), new(63, (short)1), new(421, 0x123456),
            new(463, 1.0), new(63, (short)5), new(421, 0xABCDEF) };
        switch (variant)
        {
            case 1: stops.RemoveAll(t => t.Code == 63); break;
            case 2: stops.RemoveAt(4); break;
            case 3: stops.RemoveAt(1); break;
            case 4: (stops[1], stops[2]) = (stops[2], stops[1]); (stops[4], stops[5]) = (stops[5], stops[4]); break;
            case 5: fields.Reverse(); break;
            case 6: stops.AddRange(fields.AsEnumerable().Reverse()); fields.Clear(); break;
            case 7: stops.InsertRange(1, fields); fields.Clear(); break;
        }
        if (variant < 5)
        {
            DxfTag name = fields.Single(t => t.Code == 470);
            fields.Remove(name);
            fields.AddRange(stops);
            fields.Add(name);
        }
        else fields.AddRange(stops);
        // Interleaved comments and unrelated HATCH fields must not become a
        // positional color component or break the aggregate scalar dispatch.
        if (variant != 0)
        {
            fields = fields.SelectMany(t => new[] { t, new DxfTag(999, "gradient comment") }).ToList();
            fields.Insert(3, new(52, 213.0));
        }
        tags.InsertRange(tags.FindIndex(t => t.Code == 1001), fields);
        return tags;
    }

    private static void HatchGradientPacketValid(DxfVersion version, bool binary, HatchGradientPatternType type, int variant)
    {
        using var input = new MemoryStream(RawFixtureBytes(GradientPacketTags(version, type, variant).Where(t => !binary || t.Code != 999), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid gradient packet rejected.");
        AssertGradientColorState((HatchGradientPattern)doc.Entities.Hatches.Single().Pattern, type, true, 0.35);
        doc.Entities.Add((Hatch)doc.Entities.Hatches.Single().Clone());
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle == 0 ? !binary : binary), "Gradient packet save failed.");
            if (version >= DxfVersion.AutoCad2004 && cycle == 1 && type == HatchGradientPatternType.Linear && variant == 1)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-gradient-packet-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Gradient packet reload failed.");
            foreach (Hatch hatch in doc.Entities.Hatches)
            {
                if (version >= DxfVersion.AutoCad2004)
                    AssertGradientColorState((HatchGradientPattern)hatch.Pattern, type, true, 0.35);
                else
                    Check(hatch.Pattern is not HatchGradientPattern, "Existing AC1015 solid downgrade changed.");
                Equal(2.5, hatch.Elevation, "Gradient packet elevation");
                Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "Gradient packet seeds");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Gradient packet XData");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Gradient packet consumed following LINE");
            Check(input.CanRead && output.CanRead, "Gradient packet closed caller streams.");
        }
    }

    private static void HatchGradientPacketInvalid(DxfVersion version, bool binary, int fault)
    {
        var tags = GradientPacketTags(version, HatchGradientPatternType.Linear, 0);
        int at = tags.FindIndex(t => t.Code == 450);
        if (fault < 8) tags.RemoveAll(t => t.Code == GradientScalarCodes[fault]);
        else if (fault < 16) tags.Insert(at, tags.Single(t => t.Code == GradientScalarCodes[fault - 8]));
        else
        {
            void Set(short code, object value) => tags[tags.FindIndex(at, t => t.Code == code)] = new(code, value);
            switch (fault)
            {
                case 16: Set(450, 2); break;
                case 17: Set(451, 1); break;
                case 18: Set(452, -1); break;
                case 19: Set(452, 2); break;
                case 20: Set(453, -1); break;
                case 21: Set(453, 1); break;
                case 22: Set(453, int.MaxValue); break;
                case 23: Set(463, 0.25); break;
                case 24: tags[tags.FindLastIndex(t => t.Code == 463)] = new(463, 0.0); break;
                case 25: tags.RemoveAt(tags.FindIndex(t => t.Code == 421)); break;
                case 26: tags.RemoveAt(tags.FindLastIndex(t => t.Code == 421)); break;
                case 27: tags.Insert(tags.FindIndex(t => t.Code == 421), new(421, 0x654321)); break;
                case 28: tags.Insert(tags.FindIndex(t => t.Code == 63), new(63, (short)2)); break;
                case 29: tags.Insert(at, new(421, 0x654321)); break;
                case 30: tags.Insert(at, new(63, (short)2)); break;
                case 31: tags.Insert(tags.FindIndex(t => t.Code == 1001), new(463, 1.0)); break;
                case 32: Set(470, "NOT_A_KNOWN_GRADIENT"); break;
                case 33: tags.RemoveAll(t => t.Code is 463 or 63 or 421); break;
            }
        }
        using var input = new MemoryStream(RawFixtureBytes(tags.Where(t => !binary || t.Code != 999), binary));
        long allocated = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        bool rejected = false;
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH gradient", StringComparison.Ordinal) &&
                error.Message.Contains("group code", StringComparison.Ordinal) && error.Message.Contains("position", StringComparison.Ordinal),
                "Gradient packet diagnostic lacks context.");
            rejected = true;
        }
        Check(rejected, "Malformed gradient packet accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Malformed gradient packet accepted.");
#endif
        Check(input.CanRead, "Malformed gradient closed caller stream.");
        Check(GC.GetAllocatedBytesForCurrentThread() - allocated < 4 * 1024 * 1024, "Gradient count caused unbounded allocation.");
    }

    private static void HatchGradientPacketSolid(DxfVersion version, bool binary)
    {
        var tags = GradientPacketTags(version, HatchGradientPatternType.Linear, 5);
        tags.RemoveAll(t => t.Code is 463 or 63 or 421);
        foreach (short code in new short[] { 450, 453 }) tags[tags.FindIndex(t => t.Code == code)] = new(code, 0);
        // The reference explicitly says the remaining values are ignored for
        // solid kind 0; presence and primitive framing still matter.
        tags[tags.FindIndex(t => t.Code == 461)] = new(461, -5.0);
        tags[tags.FindIndex(t => t.Code == 462)] = new(462, 7.0);
        tags[tags.FindIndex(t => t.Code == 470)] = new(470, "IGNORED_SOLID_NAME");
        using var input = new MemoryStream(RawFixtureBytes(tags.Where(t => !binary || t.Code != 999), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid solid marker packet rejected.");
        Check(doc.Entities.Hatches.Single().Pattern is not HatchGradientPattern, "Solid packet created gradient model.");
        Equal(1, doc.Entities.Lines.Count(), "Solid packet consumed following entity.");
    }
}
