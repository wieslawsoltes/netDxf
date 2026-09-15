using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredTableFidelityTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (string scope in new[] { "header", "cell", "private-subclass", "private-application", "private-value", "mixed-subclasses" })
                Run($"stored-table/fidelity/style-name/{binary}/{scope}", () => StoredTableNamedStyle(binary, scope));
            foreach (DxfVersion version in new[] { DxfVersion.AutoCad2004, DxfVersion.AutoCad2007, DxfVersion.AutoCad2018 })
                foreach (bool field in new[] { false, true })
                    Run($"stored-table/fidelity/field-precedence/{binary}/{version}/{field}", () => StoredTableField(binary, version, field));
            foreach (bool valueScope in new[] { false, true })
                Run($"stored-table/fidelity/private-field/{binary}/{valueScope}", () => StoredTablePrivateField(binary, valueScope));
            foreach (string context in new[] { "legacy", "legacy-unicode", "legacy-private", "legacy-out-of-order", "legacy-short-chunk", "legacy-long-tail", "block-cell", "modern" })
                Run($"stored-table/fidelity/text-chunks/{binary}/{context}", () => StoredTableLegacyChunks(binary, context));
        }
    }
    private static void StoredTableNamedStyle(bool binary, string scope)
    {
        var payload = ReviewTablePayload(); string wireName = "S\\U+0054YLE_REFERENCE";
        int cell = payload.FindIndex(t => t.Code == 171), value = payload.FindIndex(t => t.Code == 301);
        var styleTag = new DxfTag(7, wireName);
        if (scope == "header") payload.Insert(cell, styleTag);
        else if (scope == "cell") payload.Insert(value, styleTag);
        else if (scope == "mixed-subclasses") { payload.Insert(value, styleTag); payload.AddRange(new[] { new DxfTag(100, "PrivateTable"), new DxfTag(7, wireName) }); }
        else if (scope == "private-subclass") payload.AddRange(new[] { new DxfTag(100, "PrivateTable"), styleTag });
        else if (scope == "private-application") payload.InsertRange(value, new[] { new DxfTag(102, "{PRIVATE_TABLE"), styleTag, new DxfTag(102, "}") });
        else payload.Insert(value + 1, styleTag);
        var doc = ReviewTableLoad(payload, binary, configure: d => d.TextStyles.Add(new TextStyle("STYLE_REFERENCE", "txt.shx")));
        var table = ReviewTableTable(doc); var style = doc.TextStyles["STYLE_REFERENCE"];
        bool publicField = scope is "header" or "cell" or "mixed-subclasses";
        Equal(publicField, table.References.Contains(style), "only documented STYLE name fields bind resources");
        Equal(publicField, doc.TextStyles.GetReferences(style).Any(r => ReferenceEquals(r.Reference, table)), "STYLE reference query");
        using var before = new MemoryStream(); Check(doc.Save(before, binary), "unchanged named style output"); before.Position = 0;
        var beforeRaw = DxfRawDocument.Load(before).Sections.SelectMany(s => s.Records).Single(r => r.Name == "ACAD_TABLE");
        Check(beforeRaw.Tags.Where(t => t.Code == 7).All(t => (string)t.Value == wireName), "unchanged STYLE wire spelling retained");
        style.Name = "RENAMED_Ω_STYLE";
        if (publicField) Check(!doc.TextStyles.Remove(style), "bound STYLE removal rejected after rename");
        else Check(doc.TextStyles.Remove(style), "private group 7 does not bind a STYLE");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "named style follow-up output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-style-{binary}-{scope}.dxf"), output.ToArray()); output.Position = 0;
        var saved = DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Single(r => r.Name == "ACAD_TABLE");
        string expected = publicField ? "RENAMED_Ω_STYLE" : wireName;
        var names = saved.Tags.Where(t => t.Code == 7).Select(t => (string)t.Value).ToArray();
        Check(names.SequenceEqual(scope == "mixed-subclasses" ? new[] { expected, wireName } : new[] { expected }), "bound STYLE name follows exact resource and private spelling stays intact");
    }
    private static void StoredTableField(bool binary, DxfVersion version, bool field)
    {
        var payload = ReviewTablePayload(); int value = payload.FindIndex(t => t.Code == 301);
        if (version == DxfVersion.AutoCad2004) payload.RemoveRange(value, payload.Count - value);
        if (version == DxfVersion.AutoCad2004) payload.Add(new DxfTag(1, "stored text is not evaluated"));
        payload.Insert(payload.FindIndex(t => t.Code == 171) + 1, new DxfTag(344, field ? "ABCDEF" : "0"));
        var doc = ReviewTableLoad(payload, binary, version: version); var table = ReviewTableTable(doc); var cell = table.Grid![0, 0];
        Equal(field, cell.HasFieldReference, "FIELD marker presence"); Equal(!field, cell.HasLiteralValue, "FIELD reference takes precedence over stored text");
        if (field) Check(cell.LiteralValue == null, "FIELD did not produce an evaluated literal");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "FIELD carrier output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-field-{version}-{binary}-{field}.dxf"), output.ToArray()); output.Position = 0;
        var reloaded = DxfDocument.Load(output)!; Equal(field, ReviewTableTable(reloaded).Grid![0, 0].HasFieldReference, "FIELD precedence roundtrip");
    }
    private static void StoredTablePrivateField(bool binary, bool valueScope)
    {
        var payload = ReviewTablePayload(); int value = payload.FindIndex(t => t.Code == 301);
        if (valueScope) payload.Insert(value + 1, new DxfTag(344, "ABCDEF"));
        else payload.InsertRange(value, new DxfTag[] { new(102, "{PRIVATE_TABLE"), new(344, "ABCDEF"), new(102, "}") });
        var doc = ReviewTableLoad(payload, binary); var table = ReviewTableTable(doc); var cell = table.Grid![0, 0];
        Check(!cell.HasFieldReference && cell.HasLiteralValue, "private handle cannot suppress public literal");
        Equal("value", (string)cell.LiteralValue, "public literal beside private handle");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "private FIELD carrier output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-field-private-{binary}-{valueScope}.dxf"), output.ToArray()); output.Position = 0;
        var reloaded = ReviewTableTable(DxfDocument.Load(output)!);
        Check(OwnershipTagValues(table.Payload).SequenceEqual(OwnershipTagValues(reloaded.Payload)), "private FIELD packet preserved");
        Check(!reloaded.Grid![0, 0].HasFieldReference && reloaded.Grid[0, 0].HasLiteralValue, "private FIELD scope preserved");
    }
    private static void StoredTableLegacyChunks(bool binary, string context)
    {
        var payload = ReviewTablePayload(); int value = payload.FindIndex(t => t.Code == 301);
        payload.RemoveRange(value, payload.Count - value);
        var chunks = new List<DxfTag> { new(2, new string('x', 250)), new(2, new string('y', 250)), new(1, "tail") };
        if (context == "legacy-unicode") chunks = new() { new(2, new string('x', 248) + "\\U"), new(1, "+03A9tail") };
        if (context == "legacy-private") payload.AddRange(new DxfTag[] { new(102, "{PRIVATE_TABLE"), new(2, "unrelated private text"), new(102, "}") });
        if (context == "legacy-out-of-order") chunks.Reverse();
        if (context == "legacy-short-chunk") chunks[0] = new DxfTag(2, "xxx");
        if (context == "legacy-long-tail") chunks[chunks.Count - 1] = new DxfTag(1, new string('z', 251));
        if (context == "block-cell") payload[payload.FindIndex(t => t.Code == 171)] = new DxfTag(171, (short)2);
        payload.AddRange(chunks);
        DxfVersion version = context == "modern" ? DxfVersion.AutoCad2018 : DxfVersion.AutoCad2004;
        var doc = ReviewTableLoad(payload, binary, version: version); var table = ReviewTableTable(doc); var cell = table.Grid![0, 0];
        bool literal = context is "legacy" or "legacy-unicode" or "legacy-private";
        Equal(literal, cell.HasLiteralValue, "group 2 concatenation remains in its documented context");
        if (literal)
        {
            string expected = context == "legacy-unicode" ? new string('x', 248) + "Ωtail" : new string('x', 250) + new string('y', 250) + "tail";
            Equal(expected, (string)cell.LiteralValue, "legacy chunks decode after concatenation");
        }
        Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(table.Payload)), "legacy raw chunks retained");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "legacy chunk output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-chunks-{binary}-{context}.dxf"), output.ToArray()); output.Position = 0;
        var reloaded = DxfDocument.Load(output)!; Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(ReviewTableTable(reloaded).Payload)), "legacy raw chunks preserved on both transports");
    }
}
