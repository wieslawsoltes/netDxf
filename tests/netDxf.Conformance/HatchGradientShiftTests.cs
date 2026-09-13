using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchGradientShiftTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (HatchGradientPatternType type in Enum.GetValues<HatchGradientPatternType>())
                    foreach (double shift in new[] { 0.0, 0.125, 0.5, 0.875, 1.0 })
                    {
                        HatchGradientPatternType t = type; double s = shift;
                        Run($"hatch/gradient-shift/roundtrip/{v}/{b}/{t}/{s}", () => HatchGradientShiftRoundTrip(v, b, t, s));
                    }
                foreach (double shift in new[] { double.Epsilon, Math.BitDecrement(1.0) })
                {
                    double s = shift;
                    Run($"hatch/gradient-shift/precision/{v}/{b}/{s:R}", () => HatchGradientShiftRoundTrip(v, b, HatchGradientPatternType.Linear, s));
                }
                foreach (double shift in new[] { -0.125, 1.125, -double.Epsilon, Math.BitIncrement(1.0) })
                {
                    double s = shift;
                    Run($"hatch/gradient-shift/invalid/{v}/{b}/{s:R}", () => HatchGradientShiftInvalid(v, b, s));
                }
            }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"hatch/gradient-shift/legacy-loss/{b}", () => HatchGradientShiftLegacy(b));
        }
    }

    private static List<DxfTag> HatchGradientShiftTags(DxfVersion version, HatchGradientPatternType type, double shift)
    {
        var tags = HatchGradientAngleTags(version, type, 0.0);
        tags[tags.FindIndex(t => t.Code == 461)] = new(461, shift);
        return tags;
    }

    private static void HatchGradientShiftRoundTrip(DxfVersion version, bool binary, HatchGradientPatternType type, double shift)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(version, type, shift), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Gradient shift input rejected.");
        var original = doc.Entities.Hatches.Single();
        var gradient = (HatchGradientPattern)original.Pattern;
        Equal(shift == 0.0, gradient.Centered, "Centered projects exact zero, not an integer cast");
        var block = new Block("ShiftBlock"); block.Entities.Add((Hatch)original.Clone());
        var insert = (Insert)new Insert(block, new Vector3(10, 20, 0)).Clone();
        doc.Entities.Add(insert.Explode().OfType<Hatch>().Single());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            bool format = cycle % 2 == 0 ? !binary : binary;
            Check(doc.Save(output, format), "Gradient shift save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray();
            Equal(2, records.Length, "Original plus cloned/exploded HATCH count");
            foreach (var record in records)
            {
                double value = (double)record.Tags.Single(t => t.Code == 461).Value;
                Equal(BitConverter.DoubleToInt64Bits(shift), BitConverter.DoubleToInt64Bits(value), "Exact gradient shift bits");
                Equal(1, (int)record.Tags.Single(t => t.Code == 450).Value, "Gradient marker");
                Equal(0, (int)record.Tags.Single(t => t.Code == 452).Value, "Two-color mode");
                Near(0.0, (double)record.Tags.Single(t => t.Code == 460).Value, "Unchanged angle");
            }
            if (cycle == 1 && type == HatchGradientPatternType.Linear && shift == 0.5)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-gradient-shift-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Gradient shift reload failed.");
            foreach (Hatch hatch in doc.Entities.Hatches)
            {
                var current = (HatchGradientPattern)hatch.Pattern;
                Equal(type, current.GradientType, "Gradient type unchanged");
                Equal(shift == 0.0, current.Centered, "Repeated centered projection");
                Equal(2.5, hatch.Elevation, "Elevation unchanged");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData unchanged");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE unchanged");
        }
        Check(input.CanRead, "Gradient reader closed caller input.");
    }

    private static void HatchGradientShiftInvalid(DxfVersion version, bool binary, double shift)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(version, HatchGradientPatternType.Linear, shift), binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH gradient shift", StringComparison.Ordinal) &&
                error.Message.Contains("461", StringComparison.Ordinal) && error.Message.Contains("position", StringComparison.Ordinal),
                "Missing gradient-shift field context.");
            Check(input.CanRead, "Invalid shift closed caller input.");
            return;
        }
        throw new InvalidOperationException("Invalid gradient shift accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Invalid gradient shift accepted.");
        Check(input.CanRead, "Invalid shift closed caller input.");
#endif
    }

    private static void HatchGradientShiftLegacy(bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientShiftTags(DxfVersion.AutoCad2000, HatchGradientPatternType.Linear, 0.5), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Existing permissive legacy import changed.");
        Check(!((HatchGradientPattern)doc.Entities.Hatches.Single().Pattern).Centered, "Legacy import quantized fractional shift.");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Existing legacy export changed.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        Check(raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH")
            .SelectMany(r => r.Tags).All(t => t.Code is not 450 and not 461), "AC1015 unexpectedly emitted gradients.");
    }
}
