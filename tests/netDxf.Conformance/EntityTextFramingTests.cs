// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] TextWireKinds = { "text", "mtext", "definition-value", "definition-prompt", "attribute", "dimension" };
    private static (DxfDocument Document, Action<string> Set, Func<string> Get) TextWireDocument(DxfVersion version, string kind, bool nested)
    {
        var document = new DxfDocument(version) { BuildDimensionBlocks = true };
        Block block = nested ? new Block("TEXT_OUTER") : document.Blocks[Block.DefaultModelSpaceName];
        Action<string> set; Func<string> get;
        if (kind == "text")
        {
            var text = new Text("valid", Vector3.Zero, 2); block.Entities.Add(text);
            set = value => text.Value = value; get = () => text.Value;
        }
        else if (kind == "mtext")
        {
            var text = new MText("valid", Vector3.Zero, 2); block.Entities.Add(text);
            set = value => text.Value = value; get = () => text.Value;
        }
        else if (kind == "dimension")
        {
            var dimension = new LinearDimension(Vector2.Zero, new Vector2(10, 0), 3, 0) { UserText = "valid" };
            block.Entities.Add(dimension); set = value => dimension.UserText = value; get = () => dimension.UserText;
        }
        else
        {
            var definition = new AttributeDefinition("TAG") { Value = "valid", Prompt = "valid" };
            var child = new Block("TEXT_CHILD"); child.AttributeDefinitions.Add(definition);
            var insert = new Insert(child); block.Entities.Add(insert);
            if (kind == "definition-value") { set = value => definition.Value = value; get = () => definition.Value; }
            else if (kind == "definition-prompt") { set = value => definition.Prompt = value; get = () => definition.Prompt; }
            else { var attribute = insert.Attributes.Single(); set = value => attribute.Value = value; get = () => attribute.Value; }
        }
        if (nested) document.Entities.Add(new Insert(block));
        return (document, set, get);
    }

    private static void RegisterEntityTextFramingTests()
    {
        string[] invalidUnicode = { "a\ud800b", "a\udc00b", "\ud800", "\udc00", "\ud800\ud800", "\udc00\ud800", "valid\U0001f680\ud800" };
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            foreach (string kind in TextWireKinds) foreach (bool nested in new[] { false, true })
            {
                for (int i = 0; i < invalidUnicode.Length; i++)
                {
                    int sample = i;
                    Run($"entity-text-framing/unicode/{version}/{binary}/{kind}/{nested}/{i}",
                        () => TextWireRejected(version, binary, kind, nested, invalidUnicode[sample]));
                }
                Run($"entity-text-framing/null-byte/{version}/{binary}/{kind}/{nested}",
                    () => TextWireRejected(version, binary, kind, nested, "before\0after"));
                foreach (string control in new[] { "\r", "\n", "\r\n" })
                {
                    string value = "before" + control + "after";
                    if (!binary) Run($"entity-text-framing/line-break/{version}/{kind}/{nested}/{control.Length}/{(int)control[0]}",
                        () => TextWireRejected(version, false, kind, nested, value));
                }
                Run($"entity-text-framing/valid/{version}/{binary}/{kind}/{nested}",
                    () => TextWireValid(version, binary, kind, nested, 0));
                if (binary) for (int i = 1; i <= 3; i++)
                {
                    int sample = i;
                    Run($"entity-text-framing/binary-line-break/{version}/{kind}/{nested}/{sample}",
                        () => TextWireValid(version, true, kind, nested, sample));
                }
            }
        foreach (bool binary in new[] { false, true }) Run($"entity-text-framing/linked-column/{binary}", () => TextWireLinkedColumn(binary));
    }

    private static void TextWireRejected(DxfVersion version, bool binary, string kind, bool nested, string value)
    {
        var test = TextWireDocument(version, kind, nested); test.Set(value);
        var objects = test.Document.Objects.Items.ToArray();
        var blocks = test.Document.Blocks.ToArray(); var layouts = test.Document.Layouts.ToArray();
        var registries = test.Document.ApplicationRegistries.Items.ToArray();
        using var output = new MemoryStream();
        // Guard pre-existing destination data as well as an empty output prefix.
        byte[] sentinel = { 0x10, 0x20, 0x30 }; output.Write(sentinel); output.Position = 1;
#if DEBUG
        Throws<InvalidDataException>(() => test.Document.Save(output, binary));
#else
        Check(!test.Document.Save(output, binary), "Malformed text was saved");
#endif
        Check(output.ToArray().SequenceEqual(sentinel) && output.Position == 1, "Rejected text wrote destination bytes");
        Equal(value, test.Get(), "Rejected text mutated source content");
        Check(objects.SequenceEqual(test.Document.Objects.Items), "Rejection changed object identities");
        Check(blocks.SequenceEqual(test.Document.Blocks) && layouts.SequenceEqual(test.Document.Layouts), "Rejection changed blocks/layouts");
        Check(registries.SequenceEqual(test.Document.ApplicationRegistries.Items), "Rejection registered applications");
        test.Set("repaired"); using var valid = new MemoryStream();
        Check(test.Document.Save(valid, binary), "Repair could not be saved");
    }

    private static void TextWireValid(DxfVersion version, bool binary, string kind, bool nested, int sample)
    {
        var test = TextWireDocument(version, kind, nested);
        string value = sample == 0 ? "\U0001f680 Zażółć 東京 e\u0301 \\P \\U+0041" :
            "before" + new[] { "", "\r", "\n", "\r\n" }[sample] + "after";
        test.Set(value); using var output = new MemoryStream();
        Check(test.Document.Save(output, binary), "Valid Unicode or literal escape rejected");
        Equal(value, test.Get(), "Save changed source content");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"entity-text-framing-{version}-{binary}-{kind}-{nested}-{sample}.dxf"), output.ToArray());
    }

    private static void TextWireLinkedColumn(bool binary)
    {
        var root = new MText("firstsecondthird") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
        var columns = root.ConvertToLinkedColumns(new[] { "first", "second", "third" });
        var document = new DxfDocument(DxfVersion.AutoCad2013); document.Entities.Add(columns);
        columns[2].Value = "bad\0tail";
        using var output = new MemoryStream();
#if DEBUG
        Throws<InvalidDataException>(() => document.Save(output, binary));
#else
        Check(!document.Save(output, binary), "Malformed linked-column text saved");
#endif
        Equal(0L, output.Length, "Malformed linked-column output was partial");
    }
}
