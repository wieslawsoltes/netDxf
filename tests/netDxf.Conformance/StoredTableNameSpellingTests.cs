using netDxf;
using netDxf.IO;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredTableNameSpellingTests()
    {
        foreach (bool binary in new[] { false, true })
            foreach (bool style in new[] { false, true })
                Run($"stored-table/name-spelling/{style}/{binary}", () => StoredTableNameSpelling(style, binary));
    }
    private static void StoredTableNameSpelling(bool style, bool binary)
    {
        var payload = ReviewTablePayload();
        string wire = style ? "s\\U+0074yle_reference" : "displ\\U+0061y";
        short code = style ? (short)7 : (short)2;
        int index;
        if (style) { index = payload.FindIndex(t => t.Code == 171); payload.Insert(index, new DxfTag(code, wire)); }
        else { index = payload.FindIndex(t => t.Code == 2); payload[index] = new DxfTag(code, wire); }
        payload.AddRange(new[] { new DxfTag(100, "PrivateNames"), new DxfTag(code, wire) });
        var doc = ReviewTableLoad(payload, binary, configure: d => d.TextStyles.Add(new TextStyle("STYLE_REFERENCE", "txt.shx")));
        var table = ReviewTableTable(doc);
        DxfObject resource = style ? doc.TextStyles["STYLE_REFERENCE"] : doc.Blocks["DISPLAY"].Record;
        Check(table.References.Contains(resource), "case-insensitive source spelling resolves exact resource");
        for (int phase = 0; phase < 2; phase++)
        {
            if (phase == 1)
            {
                if (style) doc.TextStyles["STYLE_REFERENCE"].Name = "RENAMED_RESOURCE";
                else doc.Blocks["DISPLAY"].Name = "RENAMED_RESOURCE";
                Check(style ? !doc.TextStyles.Remove(doc.TextStyles["RENAMED_RESOURCE"]) : !doc.Blocks.Remove(doc.Blocks["RENAMED_RESOURCE"]), "renamed exact resource remains protected");
            }
            bool transport = phase == 0 ? binary : !binary;
            using var output = new MemoryStream(); Check(doc.Save(output, transport), "name spelling output");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-name-spelling-{style}-{binary}-{phase}.dxf"), output.ToArray()); output.Position = 0;
            var record = DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Single(r => r.Name == "ACAD_TABLE");
            var expected = payload.ToList(); if (phase == 1) expected[index] = new DxfTag(code, "RENAMED_RESOURCE");
            var actual = record.Tags.SkipWhile(t => t.Code != 100 || !Equals(t.Value, "AcDbBlockReference"));
            Check(OwnershipTagValues(expected).SequenceEqual(OwnershipTagValues(actual)), "unchanged wire spelling or bound-only rename changed the private packet");
            output.Position = 0; var round = DxfDocument.Load(output)!;
            DxfObject roundResource = style ? round.TextStyles[phase == 0 ? "STYLE_REFERENCE" : "RENAMED_RESOURCE"] : round.Blocks[phase == 0 ? "DISPLAY" : "RENAMED_RESOURCE"].Record;
            Check(ReviewTableTable(round).References.Contains(roundResource), "resource dependency survives transport change");
        }
    }
}
