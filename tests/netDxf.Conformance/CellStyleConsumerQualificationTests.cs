using netDxf;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCellStyleConsumerQualificationTests()
    {
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
        {
            Run($"cell-style-consumers/all-slots/{file}/{binary}", () => ConsumerAllSlots(file, binary));
            foreach (string fault in new[] { "formatted-extension", "merge-count", "merge-order", "merge-bounds", "linked-format-extension", "backing-style-mismatch" })
                Run($"cell-style-consumers/qualification/{file}/{binary}/{fault}", () => ConsumerQualification(file, binary, fault));
        }
    }

    private static void ConsumerAllSlots(string file, bool binary)
    {
        // Exercise nonzero IDs in every scope, including native column/cell slots
        // whose producer normally stores zero. This is an explicit synthetic input.
        var doc = ConsumerLoad(file, binary, raw =>
        {
            var contents = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "TABLECONTENT").ToArray();
            foreach (var content in contents)
            {
                var tags = content.Tags.ToList();
                for (int i = 0; i < tags.Count - 1; i++)
                {
                    if (tags[i].Code != 1 || tags[i + 1].Code != 90) continue;
                    int id = (string)tags[i].Value == "TABLECOLUMN_BEGIN" ? 1 :
                        (string)tags[i].Value == "TABLEROW_BEGIN" ? 2 :
                        (string)tags[i].Value == "TABLECELL_BEGIN" ? 3 : 0;
                    if (id != 0) tags[i + 1] = new DxfTag(90, id);
                }
                raw = raw.WithRecord(content, tags);
            }
            return raw;
        });
        var map = CellStyleMapObject(doc);
        var contents = doc.Objects.Items.OfType<DxfStoredTableContent>().ToArray();
        var slots = contents.SelectMany(c => c.GetCellStyleReferences()).ToArray();
        foreach (var slot in slots)
            Equal(slot.Kind == DxfTableContentStyleReferenceKind.Column ? 1 : slot.Kind == DxfTableContentStyleReferenceKind.Row ? 2 : 3,
                slot.StoredId, "synthetic ID scope");
        var oldValues = contents.Select(c => c.StoredValues).ToArray();
        TableContentSave(doc, binary, $"cell-style-consumers-all-before-{file}-{binary}.dxf");
        var (definitions, mapping) = ConsumerRequest(map, "cycle");
        Equal(slots.Length, map.ReplaceStructureAndRemapConsumers(definitions, mapping), "every scope must be remapped exactly once");
        for (int c = 0; c < contents.Length; c++)
        {
            var current = contents[c];
            foreach (var slot in current.GetCellStyleReferences())
                Equal(slot.Kind == DxfTableContentStyleReferenceKind.Column ? 2 : slot.Kind == DxfTableContentStyleReferenceKind.Row ? 3 : 1,
                    slot.StoredId, "simultaneous scoped ID cycle");
            Check(oldValues[c].Count == current.StoredValues.Count, "scalar inventory changed");
            if (oldValues[c].Count > 0)
            {
                var prior = oldValues[c][0];
                var stale = prior.WithValue(prior.Value, prior.FormattedText);
                Throws<ArgumentException>(() => current.ReplaceContent(current.Name, current.Description, current.TableStyle, new[] { stale }));
            }
        }
        TableContentSave(doc, binary, $"cell-style-consumers-all-after-{file}-{binary}.dxf");
        var reload = TableContentLoad(TableContentSave(doc, !binary));
        Equal(slots.Length, reload.Objects.Items.OfType<DxfStoredTableContent>().Sum(c => c.GetCellStyleReferences().Count), "cross-transport inventory");
    }

    private static void ConsumerQualification(string file, bool binary, string fault)
    {
        var doc = ConsumerLoad(file, binary, raw =>
        {
            var content = raw.Sections.SelectMany(s => s.Records).Last(r => r.Name == "TABLECONTENT");
            var tags = content.Tags.ToList();
            int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbFormattedTableData"));
            int terminal = tags.FindIndex(start + 1, t => t.Code == 100);
            int end = tags.FindIndex(start + 1, t => t.Code == 309 && Equals(t.Value, "TABLEFORMAT_END"));
            if (fault == "formatted-extension")
                tags.InsertRange(terminal, new[] { new DxfTag(1, "PRIVATE_BEGIN"), new DxfTag(90, 3), new DxfTag(309, "PRIVATE_END") });
            if (fault == "merge-count") tags[end + 1] = new DxfTag(90, (int)tags[end + 1].Value + 1);
            if (fault == "merge-order") tags[end + 2] = new DxfTag(92, 0);
            if (fault == "merge-bounds") tags[end + 4] = new DxfTag(93, int.MaxValue);
            if (fault == "linked-format-extension")
            {
                int format = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "TABLEFORMAT_BEGIN"));
                tags.Insert(format + 3, new DxfTag(98, 3));
            }
            if (fault == "backing-style-mismatch") tags[terminal + 1] = new DxfTag(340, "0");
            return raw.WithRecord(content, tags);
        });
        var map = CellStyleMapObject(doc);
        var (definitions, mapping) = ConsumerRequest(map, "rename");
        AssertConsumerRejected(doc, () => map.ReplaceStructureAndRemapConsumers(definitions, mapping));
        // Rejection must not poison subsequent scoped edits of still-retained data.
        map.ReplaceEntryNames(map.Entries.Select(e => e.Name + " guarded"));
    }
}
