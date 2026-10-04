// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterR12SelectionIntegrityTests()
    {
        RegisterR12CodePageTests();
        RegisterR12CodePageAliasTests();
        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                Run($"r12-plan-integrity/controls/{version}/{binary}", () => RsiControls(version, binary));
                Run($"r12-plan-integrity/escaped-budget/{version}/{binary}", () => RsiEscapedBudget(version, binary));
                Run($"r12-plan-integrity/handles/{version}/{binary}", () => RsiHandles(version, binary));
            }
            foreach (double? last in new double?[] { null, 0.0, 3.125 })
                Run($"r12-plan-integrity/default-style/{version}/{last?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "absent"}",
                    () => RsiDefaultStyle(version, last));
        }
    }

    // Includes every C0 value, literal escape-looking content, a caret pair and DEL.
    private static string RsiText => new string(Enumerable.Range(0, 32).Select(i => (char)i).ToArray())
        + @"|\U+0041|\U+000A|\u+00e9|^J|^ |\\|%%d|café|" + '\x7f';

    private const string RsiDescription = @"Pattern café \U+0041 ^J";

    private static EntityObject[] RsiSeeds()
    {
        var style = new TextStyle("Standard", "txt.shx");
        var pattern = new Linetype("LITERAL") { Description = RsiDescription };
        var leaf = new Block("RAW_LEAF");
        var definition = new AttributeDefinition("VALUE", 2, style)
        { Value = "default:" + RsiText, Prompt = "prompt:" + RsiText, Position = new Vector3(1, 2, 3) };
        leaf.AttributeDefinitions.Add(definition);
        leaf.Entities.Add(new Text("leaf:" + RsiText, new Vector3(4, 5, 6), 2, style));
        var nested = new Insert(leaf, new Vector3(7, 8, 9));
        nested.Attributes.Single().Value = "instance:" + RsiText;
        nested.Attributes.Single().Position = new Vector3(16, 17, 18);
        var outer = new Block("RAW_OUTER"); outer.Entities.Add(nested);
        outer.Entities.Add(new Text("outer:" + RsiText, new Vector3(10, 11, 12), 2, style));
        return new EntityObject[] { new Text("root:" + RsiText, new Vector3(13, 14, 15), 2, style) { Linetype = pattern }, new Insert(outer) };
    }

    private static void RsiContents(DxfDocument document)
    {
        Equal(RsiDescription, document.Linetypes["LITERAL"].Description, "Literal resource description");
        Equal("root:" + RsiText, document.Entities.All.OfType<Text>().Single().Value, "Root TEXT logical content");
        Equal("leaf:" + RsiText, document.Blocks["RAW_LEAF"].Entities.OfType<Text>().Single().Value, "Leaf TEXT logical content");
        Equal("outer:" + RsiText, document.Blocks["RAW_OUTER"].Entities.OfType<Text>().Single().Value, "Outer TEXT logical content");
        var definition = document.Blocks["RAW_LEAF"].AttributeDefinitions["VALUE"];
        var attribute = document.Blocks["RAW_OUTER"].Entities.OfType<Insert>().Single().Attributes.Single();
        Equal("default:" + RsiText, definition.Value, "ATTDEF logical content");
        Equal("prompt:" + RsiText, definition.Prompt, "ATTDEF logical prompt");
        Equal("instance:" + RsiText, attribute.Value, "ATTRIB logical content");
        Check(ReferenceEquals(attribute.Definition, definition), "Decoded definition identity");
    }

    private static void RsiControls(DxfVersion version, bool binary)
    {
        var source = RsiSeeds(); var plan = DxfR12SelectionPlan.Prepare(source);
        var before = plan.NormalizedSelection.Tags.ToArray();
        var editable = plan.CreateDocument(version); RsiContents(editable);
        using var output = new MemoryStream(); plan.Save(output, version, binary);
        byte[] bytes = output.ToArray();
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-plan-integrity-{version}-{(binary ? "binary" : "text")}.dxf"), bytes);
        using var input = new MemoryStream(bytes); var decoded = DxfDocument.Load(input)!;
        RsiContents(decoded);
        // Physical content strings, unlike logical values, must not carry transport delimiters.
        using var rawInput = new MemoryStream(bytes); var raw = DxfRawDocument.Load(rawInput);
        var strings = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name is "TEXT" or "ATTDEF" or "ATTRIB")
            .SelectMany(r => r.Tags.Where(t => t.Code == 1 || (r.Name == "ATTDEF" && t.Code == 3))).Select(t => (string)t.Value).ToArray();
        Equal(6, strings.Length, "Physical content inventory");
        Check(strings.All(s => !s.Any(c => c < ' ')), "Control character escaped the transient output projection");
        Check(strings.All(s => s.Contains(@"\U+005CU+0041") && s.Contains(@"\U+005EJ")), "Literal escape-looking content was not protected");
        Check(before.SequenceEqual(plan.NormalizedSelection.Tags), "Saving modified the normalized selection");
        RsiContents(editable); // Save must not encode an earlier caller-visible document in place.
        Check(source.All(e => e.Handle == null && e.Owner == null), "Output adopted source roots");
        using var resave = new MemoryStream();
        DxfR12SelectionPlan.Prepare(decoded.Entities.All).Save(resave, version, !binary);
        resave.Position = 0; RsiContents(DxfDocument.Load(resave)!);
    }

    private static void RsiEscapedBudget(DxfVersion version, bool binary)
    {
        string value = new string('\n', 200);
        var plan = DxfR12SelectionPlan.Prepare(new[] { new Text(value, Vector3.Zero, 1) });
        var before = plan.NormalizedSelection.Tags.ToArray();
        using var output = new MemoryStream(); output.Write(new byte[] { 3, 6, 9 }); output.Position = 1;
        // Logical 200 chars and R12 400 chars fit. Modern 1,400-char escaped value must not.
        Throws<InvalidDataException>(() => plan.Save(output, version, binary, new DxfRawOptions(1000000, 100000, 512)));
        Check(output.Position == 1 && output.ToArray().SequenceEqual(new byte[] { 3, 6, 9 }), "Expanded string overflow touched output");
        Check(output.CanWrite && before.SequenceEqual(plan.NormalizedSelection.Tags), "Budget refusal mutated plan or closed output");
        using var valid = new MemoryStream(); plan.Save(valid, version, binary, new DxfRawOptions(1000000, 100000, 1400));
        valid.Position = 0; Equal(value, DxfDocument.Load(valid)!.Entities.All.OfType<Text>().Single().Value, "Exact escaped-string budget");
    }

    private static void RsiHandles(DxfVersion version, bool binary)
    {
        // More than 0x100 resources makes a retained raw root identity collide with imported blocks.
        Block current = new Block("IDENTITY_0"); current.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
        for (int i = 1; i < 96; i++)
        { var parent = new Block("IDENTITY_" + i); parent.Entities.Add(new Insert(current)); current = parent; }
        var plan = DxfR12SelectionPlan.Prepare(new EntityObject[] { new Insert(current), new Insert(current), new Point(new Vector3(9, 8, 7)) });
        var document = plan.CreateDocument(version);
        var roots = document.Entities.All.OfType<Insert>().ToArray();
        Check(ReferenceEquals(roots[0].Block, roots[1].Block), "Shared deep graph was copied twice");
        foreach (EntityObject root in document.Entities.All)
            Check(ReferenceEquals(document.GetObjectByHandle(root.Handle), root), "Root handle resolves to another object");
        using var output = new MemoryStream(); plan.Save(output, version, binary); output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        var handles = raw.Sections.SelectMany(s => s.Records).SelectMany(r => r.Tags.Where(t => t.Code == 5 || t.Code == 105))
            .Select(t => (string)t.Value).ToArray();
        Equal(handles.Length, handles.Distinct(StringComparer.OrdinalIgnoreCase).Count(), "Imported object handle collision");
        output.Position = 0; var loaded = DxfDocument.Load(output)!;
        var block = loaded.Entities.All.OfType<Insert>().First().Block;
        for (int i = 95; i > 0; i--)
        { Equal("IDENTITY_" + i, block.Name, "Deep resaved graph order"); block = block.Entities.OfType<Insert>().Single().Block; }
        Equal("IDENTITY_0", block.Name, "Resaved graph leaf");
    }

    private static void RsiDefaultStyle(DxfVersion version, double? last)
    {
        var style = new TextStyle("Standard", "romans.shx")
        { Flags = TextStyleFlags.Vertical | TextStyleFlags.Referenced, TextGenerationFlags = 6, LastHeight = last, WidthFactor = .7, Height = 2 };
        var plan = DxfR12SelectionPlan.Prepare(new[] { new Text("Default style", Vector3.Zero, 2, style) });
        var document = plan.CreateDocument(version);
        var actual = document.TextStyles["Standard"];
        Equal(style.Flags, actual.Flags, "Default STYLE flags were overwritten");
        Equal(last, actual.LastHeight, "Optional default STYLE last height was overwritten");
        Equal((short)6, actual.TextGenerationFlags, "Default STYLE generation flags");
        using var output = new MemoryStream(); plan.Save(output, version); output.Position = 0;
        actual = DxfDocument.Load(output)!.TextStyles["Standard"];
        Equal(style.Flags, actual.Flags, "Resaved default STYLE flags"); Equal(last, actual.LastHeight, "Resaved default STYLE last height");
    }
}
