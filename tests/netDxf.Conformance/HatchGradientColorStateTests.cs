using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static int GradientRgb(AciColor color) => (color.R << 16) | (color.G << 8) | color.B;

    private static void RegisterHatchGradientColorStateTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (HatchGradientPatternType type in Enum.GetValues<HatchGradientPatternType>())
                    foreach (bool single in new[] { false, true })
                        foreach (double tint in new[] { 0.0, 0.35, 1.0 })
                        {
                            HatchGradientPatternType t = type; bool s = single; double f = tint;
                            Run($"hatch/gradient-state/wire/{v}/{b}/{t}/{s}/{f}", () => HatchGradientColorStateRoundTrip(v, b, t, s, f));
                        }
                foreach (bool single in new[] { false, true })
                    foreach (double tint in new[] { -0.125, 1.125 })
                    {
                        bool s = single; double f = tint;
                        Run($"hatch/gradient-state/invalid-wire/{v}/{b}/{s}/{f}", () => HatchGradientColorStateInvalid(v, b, s, f));
                    }
            }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"hatch/gradient-state/legacy/{b}", () => HatchGradientColorStateLegacy(b));
        }
        foreach (bool single in new[] { false, true })
        {
            bool s = single;
            foreach (double tint in new[] { 0.0, 0.25, 0.75, 1.0 })
            {
                double f = tint;
                Run($"hatch/gradient-state/api-edit/{s}/{f}", () => HatchGradientColorStateEdit(s, f));
            }
            foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -double.Epsilon, Math.BitIncrement(1.0) })
            {
                double f = invalid;
                Run($"hatch/gradient-state/api-reject/{s}/{f:R}", () => HatchGradientColorStateReject(s, f));
            }
        }
        Run("hatch/gradient-state/api-null-color-atomic", HatchGradientColorNullAtomic);
        Run("hatch/gradient-state/api-clone-metadata", HatchGradientColorCloneMetadata);
    }

    private static List<DxfTag> HatchGradientColorStateTags(DxfVersion version, HatchGradientPatternType type, bool single, double tint)
    {
        var tags = HatchGradientAngleTags(version, type, 37.0);
        tags[tags.FindIndex(t => t.Code == 452)] = new(452, single ? 1 : 0);
        tags[tags.FindIndex(t => t.Code == 462)] = new(462, tint);
        tags[tags.FindIndex(t => t.Code == 461)] = new(461, 0.375);
        // Both RGB stops are authored independently of dialog mode and tint.
        // Neither stop is the result of deriving Color2 from Color1's HSL.
        return tags;
    }

    private static void AssertGradientColorState(HatchGradientPattern pattern, HatchGradientPatternType type, bool single, double tint)
    {
        Equal(type, pattern.GradientType, "Gradient kind");
        Equal(single, pattern.SingleColor, "Authored dialog mode");
        Equal(BitConverter.DoubleToInt64Bits(tint), BitConverter.DoubleToInt64Bits(pattern.Tint), "Exact authored tint");
        Equal(0x123456, GradientRgb(pattern.Color1), "Authored first RGB stop");
        Equal(0xABCDEF, GradientRgb(pattern.Color2), "Authored second RGB stop must not be recomputed");
        Near(37, pattern.Angle, "Retained angle");
        Equal(0.375, pattern.Shift, "Retained continuous shift");
    }

    private static void HatchGradientColorStateRoundTrip(DxfVersion version, bool binary, HatchGradientPatternType type, bool single, double tint)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientColorStateTags(version, type, single, tint), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Gradient color-state fixture rejected.");
        Hatch original = doc.Entities.Hatches.Single();
        var source = (HatchGradientPattern)original.Pattern;
        AssertGradientColorState(source, type, single, tint);
        var direct = (HatchGradientPattern)source.Clone();
        AssertGradientColorState(direct, type, single, tint);
        Check(!ReferenceEquals(source.Color1, direct.Color1) && !ReferenceEquals(source.Color2, direct.Color2), "Gradient clone aliases color instances.");
        direct.Color2 = AciColor.Green;
        Check(!direct.SingleColor, "Explicit second-color edit must select two-color mode.");
        AssertGradientColorState(source, type, single, tint);
        var block = new Block("GradientColorState"); block.Entities.Add((Hatch)original.Clone());
        var insert = (Insert)new Insert(block, new Vector3(10, 20, 0)).Clone();
        doc.Entities.Add(insert.Explode().OfType<Hatch>().Single());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            Check(doc.Save(output, cycle % 2 == 0 ? !binary : binary), "Gradient state save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray();
            Equal(2, records.Length, "Original/exploded clone count");
            foreach (var record in records)
            {
                Equal(single ? 1 : 0, (int)record.Tags.Single(t => t.Code == 452).Value, "Output dialog mode");
                Equal(BitConverter.DoubleToInt64Bits(tint), BitConverter.DoubleToInt64Bits((double)record.Tags.Single(t => t.Code == 462).Value), "Output tint bits");
                var rgb = record.Tags.Where(t => t.Code == 421).Select(t => (int)t.Value & 0xFFFFFF).ToArray();
                Equal(2, rgb.Length, "Output RGB stop count");
                Equal(0x123456, rgb[0], "Output first RGB"); Equal(0xABCDEF, rgb[1], "Output second RGB");
            }
            if (cycle == 1 && type == HatchGradientPatternType.Linear && tint == 0.35)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-gradient-state-{version}-{binary}-{single}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Gradient state reload failed.");
            foreach (Hatch hatch in doc.Entities.Hatches)
            {
                AssertGradientColorState((HatchGradientPattern)hatch.Pattern, type, single, tint);
                Equal(2.5, hatch.Elevation, "Elevation");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData");
                Equal(1, hatch.SeedPoints.Count, "Seed count");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE");
        }
        Check(input.CanRead, "Color-state reader closed caller stream.");
    }

    private static void HatchGradientColorStateInvalid(DxfVersion version, bool binary, bool single, double tint)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientColorStateTags(version, HatchGradientPatternType.Linear, single, tint), binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH gradient tint", StringComparison.Ordinal) && error.Message.Contains("462", StringComparison.Ordinal), "Missing gradient-tint context.");
            Check(input.CanRead, "Invalid tint closed caller stream."); return;
        }
        throw new InvalidOperationException("Invalid gradient tint accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Invalid gradient tint accepted.");
        Check(input.CanRead, "Invalid tint closed caller stream.");
#endif
    }

    private static void HatchGradientColorStateLegacy(bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientColorStateTags(DxfVersion.AutoCad2000, HatchGradientPatternType.Linear, true, 0.35), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Existing permissive legacy import changed.");
        AssertGradientColorState((HatchGradientPattern)doc.Entities.Hatches.Single().Pattern, HatchGradientPatternType.Linear, true, 0.35);
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Legacy export changed.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        Check(raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").SelectMany(r => r.Tags)
            .All(t => t.Code is not 450 and not 452 and not 462 and not 421), "AC1015 unexpectedly emitted gradient payload.");
    }

    private static void HatchGradientColorStateEdit(bool single, double tint)
    {
        var pattern = new HatchGradientPattern(AciColor.FromTrueColor(0x123456), 0.4, HatchGradientPatternType.Linear);
        pattern.SingleColor = single;
        int previousRgb = GradientRgb(pattern.Color2);
        pattern.Tint = tint;
        Equal(single, pattern.SingleColor, "Tint setter must not clear single-color mode");
        Equal(tint, pattern.Tint, "Tint edit");
        AciColor.ToHsl(pattern.Color1, out double h, out double s, out double _);
        Equal(single ? GradientRgb(AciColor.FromHsl(h, s, tint)) : previousRgb,
            GradientRgb(pattern.Color2), "Tint derives second color only in single-color mode");
        var copy = (HatchGradientPattern)pattern.Clone();
        Equal(single, copy.SingleColor, "Clone mode"); Equal(tint, copy.Tint, "Clone tint");
        Equal(GradientRgb(pattern.Color2), GradientRgb(copy.Color2), "Clone current RGB");
        pattern.SingleColor = true;
        Check(pattern.SingleColor, "Enabling mode failed.");
        Equal(GradientRgb(AciColor.FromHsl(h, s, tint)), GradientRgb(pattern.Color2), "Explicit mode switch derives tint");
    }

    private static void HatchGradientColorStateReject(bool single, double tint)
    {
        var pattern = new HatchGradientPattern(AciColor.Red, 0.4, HatchGradientPatternType.Linear);
        pattern.SingleColor = single;
        var before = pattern.Color2;
        Throws<ArgumentOutOfRangeException>(() => pattern.Tint = tint);
        Equal(0.4, pattern.Tint, "Rejected tint mutated state"); Equal(single, pattern.SingleColor, "Rejected tint mutated mode");
        Check(ReferenceEquals(before, pattern.Color2), "Rejected tint replaced color.");
        Throws<ArgumentOutOfRangeException>(() => new HatchGradientPattern(AciColor.Red, tint, HatchGradientPatternType.Linear));
    }

    private static void HatchGradientColorNullAtomic()
    {
        var pattern = new HatchGradientPattern(AciColor.Red, 0.4, HatchGradientPatternType.Linear);
        AciColor before = pattern.Color2;
        Throws<ArgumentNullException>(() => pattern.Color2 = null!);
        Check(pattern.SingleColor && ReferenceEquals(before, pattern.Color2), "Rejected null Color2 cleared single-color state.");
        AciColor first = pattern.Color1;
        Throws<ArgumentNullException>(() => pattern.Color1 = null!);
        Check(ReferenceEquals(first, pattern.Color1) && pattern.SingleColor, "Rejected null Color1 changed state.");
    }

    private static void HatchGradientColorCloneMetadata()
    {
        var pattern = new HatchGradientPattern(AciColor.Red, 0.25, HatchGradientPatternType.Spherical, "Authored description")
        { Origin = new Vector2(4, 5), Angle = 71, Shift = 0.625, IsDouble = true, Scale = 2.0 };
        // Line definitions are dormant on gradient output, but are caller-owned model data.
        pattern.LineDefinitions.Add(new HatchPatternLineDefinition { Angle = 23, Delta = new Vector2(1, 2) });
        var copy = (HatchGradientPattern)pattern.Clone();
        Equal(pattern.Description, copy.Description, "Description clone");
        Equal(1, copy.LineDefinitions.Count, "Dormant line definition clone");
        Check(!ReferenceEquals(pattern.LineDefinitions[0], copy.LineDefinitions[0]), "Line definitions aliased.");
        copy.LineDefinitions[0].Angle = 90;
        Equal(23.0, pattern.LineDefinitions[0].Angle, "Cloned line edit affected source.");
        Equal(pattern.Origin, copy.Origin, "Origin clone"); Equal(pattern.Angle, copy.Angle, "Angle clone");
        Equal(pattern.Shift, copy.Shift, "Shift clone"); Equal(pattern.Scale, copy.Scale, "Scale clone");
        Check(copy.SingleColor && copy.IsDouble, "Clone flags changed.");
    }
}
