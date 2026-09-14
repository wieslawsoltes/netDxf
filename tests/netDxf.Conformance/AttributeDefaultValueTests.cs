using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
using DxfAttribute = netDxf.Entities.Attribute;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterAttributeDefaultValueTests()
    {
        Run("attribute-default/api", AttributeDefaultApi);
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            Run($"attribute-default/roundtrip/{v}/{b}", () => AttributeDefaultRoundTrip(v, b));
            Run($"attribute-default/omitted-reader/{v}/{b}", () => AttributeDefaultOmitted(v, b));
        }
    }

    private static void AttributeDefaultApi()
    {
        AttributeDefinition[] definitions = { new("TAG"), new("TAG", TextStyle.Default), new("TAG", 2, TextStyle.Default) };
        foreach (var definition in definitions)
        {
            Equal(string.Empty, definition.Value, "New ATTDEF must have a valid empty value");
            Equal(string.Empty, definition.Prompt, "New ATTDEF must have a valid empty prompt");
            definition.Prompt = "kept prompt";
            Equal("kept prompt", ((AttributeDefinition)definition.Clone()).Prompt, "ATTDEF clone prompt");
            definition.Prompt = null!;
            Equal(string.Empty, definition.Prompt, "Null prompt assignment semantics");
            Equal(string.Empty, ((AttributeDefinition)definition.Clone()).Prompt, "ATTDEF clone normalized prompt");
            Equal(string.Empty, new DxfAttribute(definition).Value, "New ATTRIB must inherit a valid empty value");
            Equal(string.Empty, ((AttributeDefinition)definition.Clone()).Value, "ATTDEF clone default");
            var attribute = new DxfAttribute(definition);
            Equal(string.Empty, ((DxfAttribute)attribute.Clone()).Value, "ATTRIB clone default");
            definition.Value = "value"; attribute.Value = "override";
            definition.Value = null!; attribute.Value = null!;
            Equal(string.Empty, definition.Value, "Null ATTDEF assignment semantics");
            Equal(string.Empty, attribute.Value, "Null ATTRIB assignment semantics");
            definition.Value = "  padded  "; attribute.Value = "   ";
            Equal("  padded  ", definition.Value, "ATTDEF whitespace must not be trimmed");
            Equal("   ", attribute.Value, "ATTRIB whitespace must not be trimmed");
        }
        Throws<ArgumentNullException>(() => new AttributeDefinition(null!));
        Throws<ArgumentNullException>(() => new AttributeDefinition(string.Empty));
        Throws<ArgumentNullException>(() => new DxfAttribute((AttributeDefinition)null!));
        try { _ = new AttributeDefinition("TAG", (TextStyle)null!); throw new InvalidOperationException("Null attribute style accepted."); }
        catch (ArgumentNullException error) { Equal("style", error.ParamName, "Null style parameter name"); }
        try { _ = new AttributeDefinition("TAG", 2, null!); throw new InvalidOperationException("Null explicit-height attribute style accepted."); }
        catch (ArgumentNullException error) { Equal("style", error.ParamName, "Null explicit-height style parameter name"); }
        var automaticHeight = new TextStyle("ATTRIBUTE_AUTO", "txt.shx") { Height = 0 };
        var fixedHeight = new TextStyle("ATTRIBUTE_FIXED", "txt.shx") { Height = 2.5 };
        Equal(1.0, new AttributeDefinition("TAG", automaticHeight).Height, "Zero style height default");
        Equal(2.5, new AttributeDefinition("TAG", fixedHeight).Height, "Fixed style height inheritance");
        Equal(4.0, new AttributeDefinition("TAG", 4, fixedHeight).Height, "Explicit attribute height");
        var block = new Block("ATTRIBUTE_SYNC"); var insert = new Insert(block);
        block.AttributeDefinitions.Add(new AttributeDefinition("NEW_TAG")); insert.Sync();
        Equal(string.Empty, insert.Attributes.Single().Value, "Synced ATTRIB inherited null");
        var clone = (Insert)insert.Clone();
        Equal(string.Empty, clone.Attributes.Single().Value, "INSERT clone default ATTRIB");
        Equal(string.Empty, clone.Block.AttributeDefinitions["NEW_TAG"].Value, "Block clone default ATTDEF");
    }

    private static DxfDocument AttributeDefaultDocument(DxfVersion version, int mode)
    {
        var document = new DxfDocument(version);
        var block = new Block("ATTRIBUTE_DEFAULT");
        var definition = new AttributeDefinition("TAG");
        string? value = mode switch { 1 => null, 2 => string.Empty, 3 => "retained value", _ => "   " };
        if (mode != 0) { definition.Value = value!; definition.Prompt = value!; }
        block.AttributeDefinitions.Add(definition);
        var insert = new Insert(block);
        if (mode != 0) insert.Attributes.Single().Value = value!;
        document.Entities.Add(insert);
        document.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        return document;
    }

    private static void AttributeDefaultAssert(DxfDocument document, string definition, string attribute, string prompt)
    {
        var insert = document.Entities.Inserts.Single();
        Equal(definition, insert.Block.AttributeDefinitions["TAG"].Value, "Loaded ATTDEF value");
        Equal(attribute, insert.Attributes.Single().Value, "Loaded ATTRIB value");
        Equal(prompt, insert.Block.AttributeDefinitions["TAG"].Prompt, "Loaded ATTDEF prompt");
        Equal("TAG", insert.Attributes.Single().Tag, "ATTRIB tag changed");
        Equal(new Vector3(1, 2, 3), document.Entities.Lines.Single().StartPoint, "Following LINE changed");
    }

    private static void AttributeDefaultRoundTrip(DxfVersion version, bool binary)
    {
        for (int mode = 0; mode < 5; mode++)
        {
            var document = AttributeDefaultDocument(version, mode);
            string expected = mode < 3 ? string.Empty : mode == 3 ? "retained value" : "   ";
            AttributeDefaultAssert(document, expected, expected, expected);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                using var output = new MemoryStream(); Check(document.Save(output, cycle == 0 ? binary : !binary), "Attribute default save failed.");
                output.Position = 0; var raw = DxfRawDocument.Load(output);
                var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "ATTDEF" || r.Name == "ATTRIB").ToArray();
                Equal(2, records.Length, "Expected ATTDEF and ATTRIB records");
                foreach (var record in records)
                {
                    Equal(expected, (string)record.Tags.Single(t => t.Code == 1).Value, "Explicit attribute text field");
                    if (record.Name == "ATTDEF") Equal(expected, (string)record.Tags.Single(t => t.Code == 3).Value, "Explicit prompt text field");
                }
                if (mode == 0 && cycle == 0) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"attribute-default-{version}-{binary}.dxf"), output.ToArray());
                output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Attribute default reload failed.");
                AttributeDefaultAssert(document, expected, expected, expected);
                Check(output.CanRead, "Attribute roundtrip closed caller stream.");
            }
        }
    }

    private static void AttributeDefaultOmitted(DxfVersion version, bool binary)
    {
        var original = AttributeDefaultDocument(version, 3);
        using var source = new MemoryStream(); Check(original.Save(source, binary), "Attribute omission source save failed."); source.Position = 0;
        var raw = DxfRawDocument.Load(source);
        foreach (string omitFrom in new[] { "ATTDEF", "ATTRIB", "PROMPT" })
        {
            string active = string.Empty;
            var tags = new List<DxfTag>();
            foreach (DxfTag tag in raw.Tags)
            {
                if (tag.Code == 0) active = (string)tag.Value;
                bool omitted = omitFrom == "PROMPT" ? active == "ATTDEF" && tag.Code == 3 : active == omitFrom && tag.Code == 1;
                if (!omitted) tags.Add(tag);
            }
            using var changed = new MemoryStream(); DxfRawDocument.Create(tags, binary).Save(changed); changed.Position = 0;
            var loaded = DxfDocument.Load(changed) ?? throw new InvalidOperationException("Omitted attribute value load failed.");
            AttributeDefaultAssert(loaded, omitFrom == "ATTDEF" ? string.Empty : "retained value", omitFrom == "ATTRIB" ? string.Empty : "retained value", omitFrom == "PROMPT" ? string.Empty : "retained value");
            using var output = new MemoryStream(); Check(loaded.Save(output, !binary), "Normalized omitted attribute save failed."); output.Position = 0;
            var normalized = DxfRawDocument.Load(output);
            var record = normalized.Sections.SelectMany(s => s.Records).Single(r => r.Name == (omitFrom == "PROMPT" ? "ATTDEF" : omitFrom));
            Equal(string.Empty, (string)record.Tags.Single(t => t.Code == (omitFrom == "PROMPT" ? 3 : 1)).Value, "Omitted attribute must materialize existing empty-string default");
        }
    }
}
