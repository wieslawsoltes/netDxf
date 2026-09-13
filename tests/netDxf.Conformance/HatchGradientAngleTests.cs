using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] GradientWireNames =
    {
        "LINEAR", "CYLINDER", "INVCYLINDER", "SPHERICAL", "INVSPHERICAL",
        "HEMISPHERICAL", "INVHEMISPHERICAL", "CURVED", "INVCURVED"
    };

    private static void RegisterHatchGradientAngleTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            foreach (bool binary in new[] { false, true })
                foreach (HatchGradientPatternType type in Enum.GetValues<HatchGradientPatternType>())
                    foreach (double angle in new[] { 0.0, 37.0, -45.0, 450.0 })
                    {
                        DxfVersion v = version; bool b = binary; HatchGradientPatternType t = type; double a = angle;
                        Run($"hatch/gradient-angle/{v}/{b}/{t}/{a}", () => HatchGradientAngle(v, b, t, a));
                    }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"hatch/gradient-angle/legacy-loss/{b}", () => HatchGradientAngleLegacy(b));
        }
    }

    private static List<DxfTag> HatchGradientAngleTags(DxfVersion version, HatchGradientPatternType type, double angle)
    {
        var tags = HatchPatternValidationTags(version);
        int start = tags.FindIndex(t => t.Code == 78), end = tags.FindIndex(t => t.Code == 98);
        tags.RemoveRange(start, end - start);
        tags.RemoveAll(t => t.Code is 52 or 41 or 77);
        tags[tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "U"))] = new(2, "SOLID");
        tags[tags.FindIndex(t => t.Code == 70)] = new(70, (short)1);
        bool single = angle is 37.0 or 450.0;
        int at = tags.FindIndex(t => t.Code == 1001);
        tags.InsertRange(at, new DxfTag[]
        {
            new(450, 1), new(451, 0), new(460, angle * Math.PI / 180.0), new(461, 0.0),
            new(452, single ? 1 : 0), new(462, 0.35), new(453, 2),
            new(463, 0.0), new(63, (short)1), new(421, 0x123456),
            new(463, 1.0), new(63, (short)5), new(421, 0xABCDEF), new(470, GradientWireNames[(int)type])
        });
        // Group 52 is inapplicable to a solid/gradient fill. Neither early nor
        // late redundant pattern metadata is allowed to replace group 460.
        if (angle == 37.0) tags.Insert(tags.FindIndex(t => t.Code == 450), new(52, 123.0));
        if (angle == -45.0) tags.Insert(tags.FindIndex(t => t.Code == 1001), new(52, 213.0));
        return tags;
    }

    private static void HatchGradientAngle(DxfVersion version, bool binary, HatchGradientPatternType type, double angle)
    {
        double expected = (angle % 360.0 + 360.0) % 360.0;
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientAngleTags(version, type, angle), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Gradient angle fixture rejected.");
        Hatch original = doc.Entities.Hatches.Single();
        var initial = original.Pattern as HatchGradientPattern ?? throw new InvalidOperationException("Gradient subtype lost.");
        Near(expected, initial.Angle, "Loaded gradient angle");
        Equal(angle is 37.0 or 450.0, initial.SingleColor, "Initial single-color mode");
        var direct = (HatchGradientPattern)initial.Clone();
        Near(expected, direct.Angle, "Pattern clone angle");
        direct.Angle = 19.0;
        Near(expected, initial.Angle, "Clone edit aliased source angle");
        var block = new Block("GradientAngle"); block.Entities.Add((Hatch)original.Clone());
        var insert = new Insert(block, new Vector3(10, 20, 0));
        var copy = (Insert)insert.Clone();
        Near(expected, copy.Block.Entities.OfType<Hatch>().Single().Pattern.Angle, "Nested clone angle");
        doc.Entities.Add((Hatch)original.Clone());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream(); bool format = cycle % 2 == 0 ? !binary : binary;
            Check(doc.Save(output, format), "Gradient save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH"))
            {
                Equal(1, (int)record.Tags.Single(t => t.Code == 450).Value, "Gradient marker");
                Near(expected * Math.PI / 180.0, (double)record.Tags.Single(t => t.Code == 460).Value, "Gradient wire radians");
                Check(record.Tags.All(t => t.Code != 52), "Gradient writer emitted pattern-only angle.");
            }
            if (cycle == 1 && type == HatchGradientPatternType.Linear && angle == 37.0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-gradient-angle-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Gradient reload failed.");
            Equal(2, doc.Entities.Hatches.Count(), "Original/clone count");
            foreach (Hatch hatch in doc.Entities.Hatches)
            {
                var gradient = hatch.Pattern as HatchGradientPattern ?? throw new InvalidOperationException("Reload lost gradient subtype.");
                Near(expected, gradient.Angle, "Repeated gradient angle");
                Equal(type, gradient.GradientType, "Gradient kind");
                Equal(2.5, hatch.Elevation, "Gradient elevation");
                Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "Gradient seed point");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following gradient XData");
            }
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following LINE");
            Check(output.CanRead && input.CanRead, "Gradient parser closed caller stream.");
        }
    }

    private static void HatchGradientAngleLegacy(bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchGradientAngleTags(DxfVersion.AutoCad2000,
            HatchGradientPatternType.Linear, 37), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Existing permissive legacy reader changed.");
        Near(37, doc.Entities.Hatches.Single().Pattern.Angle, "Retained out-of-profile gradient angle");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Existing legacy save policy changed.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        Check(raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH")
            .SelectMany(r => r.Tags).All(t => t.Code is not 450 and not 460), "Legacy save unexpectedly admitted gradient payload.");
    }
}
