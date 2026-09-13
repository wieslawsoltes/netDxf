using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchDoublePatternTests()
    {
        Run("hatch/double/model-defaults-and-clones", HatchDoubleDefaults);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"hatch/double/edit-and-insert/{v}/{b}", () => HatchDoubleEdit(v, b));
                Run($"hatch/double/inapplicable-fills/{v}/{b}", () => HatchDoubleSolid(v, b));
                Run($"hatch/double/late-flag/{v}/{b}", () => HatchDoubleLate(v, b));
            }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (HatchType type in Enum.GetValues<HatchType>())
                    foreach (short? flag in new short?[] { null, 0, 1, -1, 2 })
                    {
                        DxfVersion v = version; bool b = binary; HatchType t = type; short? f = flag;
                        Run($"hatch/double/wire/{v}/{b}/{t}/{f}", () => HatchDoubleWire(v, b, t, f));
                    }
    }

    private static List<DxfTag> HatchDoubleTags(DxfVersion version, HatchType type, short? flag)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, "HATCH"), new(5, "200"),
            new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbHatch"),
            new(10, 0.0), new(20, 0.0), new(30, 2.5), new(210, 0.0), new(220, 0.0), new(230, 1.0),
            new(2, "U"), new(70, (short)0), new(71, (short)0), new(91, 1),
            new(92, 2), new(72, (short)0), new(73, (short)1), new(93, 4),
            new(10, 0.0), new(20, 0.0), new(10, 10.0), new(20, 0.0),
            new(10, 10.0), new(20, 10.0), new(10, 0.0), new(20, 10.0), new(97, 0),
            new(75, (short)0), new(76, (short)type), new(52, 0.0), new(41, 1.0)
        };
        if (flag.HasValue) tags.Add(new(77, flag.Value));
        tags.AddRange(new DxfTag[]
        {
            new(78, (short)1), new(53, 0.0), new(43, 0.0), new(44, 0.0), new(45, 0.0), new(46, 0.125), new(79, (short)0),
            new(98, 1), new(10, 2.0), new(20, 3.0),
            new(1001, "DOUBLE_TEST"), new(1000, "after pattern"), new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void HatchDoubleWire(DxfVersion version, bool binary, HatchType type, short? flag)
    {
        var tags = HatchDoubleTags(version, type, flag);
        if (!binary)
        {
            int at = tags.FindIndex(t => t.Code == 78);
            tags.Insert(at, new(999, "77 ENDSEC"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        if (flag < 0 || flag > 1)
        {
#if DEBUG
            try { DxfDocument.Load(input); throw new InvalidOperationException("Invalid double-pattern flag accepted."); }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains("77", StringComparison.Ordinal), "Flag diagnostic lost group 77.");
            }
#else
            Check(DxfDocument.Load(input) == null, "Invalid double-pattern flag accepted.");
#endif
            Check(input.CanRead, "Rejected HATCH closed input.");
            return;
        }
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid patterned HATCH failed to load.");
        Hatch original = doc.Entities.Hatches.Single();
        Equal(type, original.Pattern.Type, "Double flag changed pattern type");
        Equal(1, original.Pattern.LineDefinitions.Count, "Double flag changed authored line count");
        Equal(0.125, original.Pattern.LineDefinitions[0].Delta.Y, "Double flag changed spacing");
        Equal(2.5, original.Elevation, "Double flag changed elevation");
        Equal("after pattern", (string)original.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Double flag disrupted XData");
        doc.Entities.Add((Hatch)original.Clone());
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Double pattern save failed.");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        var hatches = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray();
        Equal(2, hatches.Length, "Double hatch clone count");
        foreach (var hatch in hatches)
            Equal(flag ?? (short)0, (short)hatch.Tags.Single(t => t.Code == 77).Value, "Double flag lost through load/clone/save");
        Check(input.CanRead && output.CanWrite, "HATCH closed caller streams.");
        if (type == HatchType.UserDefined && flag == 1)
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-double-{version}-{!binary}.dxf"), output.ToArray());
    }
}

internal static partial class Program
{
    private static void HatchDoubleDefaults()
    {
        foreach (HatchPattern pattern in new HatchPattern[] { new("U"), HatchPattern.Line, HatchPattern.Net, HatchPattern.Solid, new HatchGradientPattern() })
        {
            Check(!pattern.IsDouble, "A new pattern invented a double flag.");
            pattern.IsDouble = true;
            var copy = (HatchPattern)pattern.Clone();
            Check(copy.IsDouble, "Pattern clone lost the double flag.");
            Equal(pattern.GetType(), copy.GetType(), "Pattern clone changed subtype");
            copy.IsDouble = false;
            Check(pattern.IsDouble, "Clone edit changed source double flag.");
        }
    }

    private static void HatchDoubleEdit(DxfVersion version, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchDoubleTags(version, HatchType.UserDefined, 0), binary));
        DxfDocument source = DxfDocument.Load(input) ?? throw new InvalidOperationException("Editable hatch fixture failed.");
        Hatch original = source.Entities.Hatches.Single(); original.Pattern.IsDouble = true;
        var block = new Block("DoubleHatch"); block.Entities.Add((Hatch)original.Clone());
        var insert = new Insert(block, new Vector3(10, 20, 0));
        var clonedInsert = (Insert)insert.Clone();
        Hatch child = clonedInsert.Block.Entities.OfType<Hatch>().Single();
        Check(child.Pattern.IsDouble && !ReferenceEquals(child.Pattern, original.Pattern), "INSERT clone lost or aliased pattern state.");
        child.Pattern.IsDouble = false;
        Check(original.Pattern.IsDouble && block.Entities.OfType<Hatch>().Single().Pattern.IsDouble, "Nested clone edit changed source.");
        Hatch exploded = insert.Explode().OfType<Hatch>().Single();
        Check(exploded.Pattern.IsDouble, "INSERT explosion lost double flag.");
        exploded.TransformBy(Matrix3.RotationZ(Math.PI / 2), new Vector3(30, 40, 0));
        Check(exploded.Pattern.IsDouble, "Transform lost double flag.");
        Equal(1, exploded.Pattern.LineDefinitions.Count, "Double flag duplicated the actual line-definition list");
        Equal(1, original.Pattern.LineDefinitions.Count, "Double flag mutated source line definitions");
        var doc = new DxfDocument(version); doc.Entities.Add(exploded);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            bool expected = cycle != 1; doc.Entities.Hatches.Single().Pattern.IsDouble = expected;
            using var output = new MemoryStream(); Check(doc.Save(output, cycle == 1 ? !binary : binary), "Double flag edit failed to save.");
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Double flag edit failed to reload.");
            Equal(expected, doc.Entities.Hatches.Single().Pattern.IsDouble, "Edited flag changed");
            Equal(1, doc.Entities.Hatches.Single().Pattern.LineDefinitions.Count, "Repeated output changed line count");
        }
    }

    private static void HatchDoubleSolid(DxfVersion version, bool binary)
    {
        foreach (HatchPattern pattern in new HatchPattern[] { HatchPattern.Solid, new HatchGradientPattern() })
        {
            using var input = new MemoryStream(RawFixtureBytes(HatchDoubleTags(version, HatchType.UserDefined, 0), binary));
            DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Solid control fixture failed.");
            pattern.IsDouble = true; doc.Entities.Hatches.Single().Pattern = pattern;
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Inapplicable-fill control failed to save.");
            output.Position = 0; DxfRawDocument raw = DxfRawDocument.Load(output);
            var hatch = raw.Sections.SelectMany(section => section.Records).Single(record => record.Name == "HATCH");
            Check(!hatch.Tags.Any(tag => tag.Code == 77), "Solid/gradient fill emitted inapplicable double flag.");
            Check(pattern.IsDouble, "Writer mutated dormant model flag.");
        }
    }

    private static void HatchDoubleLate(DxfVersion version, bool binary)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, null);
        tags.Insert(tags.FindIndex(tag => tag.Code == 98), new(77, (short)1));
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Late double-flag fixture failed.");
        Check(doc.Entities.Hatches.Single().Pattern.IsDouble, "Flag after pattern definitions was ignored.");
        Equal(1, doc.Entities.Hatches.Single().Pattern.LineDefinitions.Count, "Late flag affected pattern definitions");
    }
}
