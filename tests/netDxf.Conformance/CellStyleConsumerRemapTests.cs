using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] ConsumerModes = { "rename", "cycle", "remove", "clear", "identity", "append" };
    private static void RegisterCellStyleConsumerRemapTests()
    {
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
        {
            foreach (string mode in ConsumerModes)
                Run($"cell-style-consumers/native/{file}/{binary}/{mode}", () => CellStyleConsumerNative(file, binary, mode));
            foreach (string failure in new[] { "missing-fallback", "invalid-old-id", "duplicate-source-id", "duplicate-destination-id", "missing-source", "missing-target", "negative-target", "zero-source", "duplicate-mapping", "empty-target-without-fallback" })
                Run($"cell-style-consumers/ids/{file}/{binary}/{failure}", () => CellStyleConsumerIds(file, binary, failure));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (string failure in new[] { "definitions-dispose", "mapping-dispose", "definitions-throw", "mapping-throw", "map-reentry", "names-reentry", "entries-reentry", "structure-reentry", "content-outer", "profile", "null-entries", "null-mapping" })
                Run($"cell-style-consumers/atomic/{binary}/{failure}", () => CellStyleConsumerAtomic(binary, failure));
            foreach (string failure in new[] { "duplicate-slot", "missing-slot", "nested-slot", "negative-id", "geometry-extension", "opaque-content", "private-table", "late-consumer", "wrong-map-owner", "unqualified-map" })
                Run($"cell-style-consumers/schema/{binary}/{failure}", () => CellStyleConsumerSchema(binary, failure));
            foreach (string literal in new[] { "TABLECELL_BEGIN", "TABLECOLUMN_BEGIN", "TABLEROW_BEGIN", "TABLECELL_END", "DATAMAP_BEGIN" })
                Run($"cell-style-consumers/literal/{binary}/{literal}", () => CellStyleConsumerLiteral(binary, literal));
        }
    }

    private static DxfDocument ConsumerLoad(string file, bool binary, Func<DxfRawDocument, DxfRawDocument>? mutate = null)
    {
        var raw = TableContentRaw(TableContentSourceBytes(file));
        if (mutate != null) raw = mutate(raw);
        return TableContentLoad(TableContentRawBytes(raw, binary));
    }

    private static (DxfCellStyleMapEntryDefinition[], Dictionary<int, int>) ConsumerRequest(DxfStoredCellStyleMap map, string mode)
    {
        var old = map.Entries.Select(DxfCellStyleMapEntryDefinition.FromEntry).ToArray();
        var mapping = new Dictionary<int, int>();
        if (mode == "rename") foreach (var entry in old) mapping.Add(entry.Id, entry.Id + 10);
        if (mode == "cycle") { mapping.Add(1, 2); mapping.Add(2, 3); mapping.Add(3, 1); }
        if (mode == "remove") mapping.Add(3, 2);
        if (mode == "clear") foreach (var entry in old) mapping.Add(entry.Id, 0);
        var selected = mode == "clear" ? Array.Empty<DxfCellStyleMapEntryDefinition>() : mode == "remove" ? old.Where(e => e.Id != 3).ToArray() : old;
        var definitions = selected.Select(entry => new DxfCellStyleMapEntryDefinition(
            mapping.TryGetValue(entry.Id, out int target) ? target : entry.Id, entry.StoredType, entry.Name, entry.Format)).ToArray();
        if (mode == "rename") Array.Reverse(definitions);
        if (mode == "append") definitions = definitions.Concat(new[] { new DxfCellStyleMapEntryDefinition(10, old[0].StoredType, "Unused", old[0].Format) }).ToArray();
        return (definitions, mapping);
    }

    private static void CellStyleConsumerNative(string file, bool binary, string mode)
    {
        var doc = ConsumerLoad(file, binary); var map = CellStyleMapObject(doc);
        var style = TableStyleObject(doc);
        var contents = doc.Objects.Items.OfType<DxfStoredTableContent>().Where(c => ReferenceEquals(c.TableStyle, style)).ToArray();
        var before = contents.Select(c => c.Payload).ToArray();
        var references = contents.Select(c => c.References.ToArray()).ToArray();
        var slots = contents.Select(c => c.GetCellStyleReferences()).ToArray();
        foreach (var content in contents)
        {
            var links = content.GetCellStyleReferences();
            Equal(content.ColumnCount!.Value, links.Count(l => l.Kind == DxfTableContentStyleReferenceKind.Column), "column reference inventory");
            Equal(content.RowCount!.Value, links.Count(l => l.Kind == DxfTableContentStyleReferenceKind.Row), "row reference inventory");
            Equal(content.RowCount!.Value * content.ColumnCount!.Value, links.Count(l => l.Kind == DxfTableContentStyleReferenceKind.Cell), "cell reference inventory");
            Throws<NotSupportedException>(() => ((IList<DxfTableContentStyleReference>)links).Clear());
        }
        var oldMap = map.Payload; var mapBefore = OwnershipTagValues(oldMap).ToArray(); var oldEntries = map.Entries;
        TableContentSave(doc, binary, $"cell-style-consumers-before-{file}-{binary}-{mode}.dxf");
        long seed = OwnershipSeed(doc); int members = doc.Objects.Items.Count;
        var (definitions, mapping) = ConsumerRequest(map, mode);
        int expected = slots.SelectMany(s => s).Count(s => mapping.TryGetValue(s.StoredId, out int id) && id != s.StoredId);
        Equal(expected, map.ReplaceStructureAndRemapConsumers(definitions, mapping), "actual reference edit count");
        Equal(seed, OwnershipSeed(doc), "coordinated update allocated handles");
        Equal(members, doc.Objects.Items.Count, "coordinated update changed object membership");
        Check(mapBefore.SequenceEqual(OwnershipTagValues(oldMap)), "old map snapshot changed");
        Check(ReferenceEquals(style.StoredCellStyleMap, map), "parent map identity changed");
        var destinationIds = map.Entries.Select(e => e.Id).ToHashSet();
        for (int c = 0; c < contents.Length; c++)
        {
            var content = contents[c]; var after = content.GetCellStyleReferences();
            Equal(slots[c].Count, after.Count, "reference inventory changed");
            Check(references[c].SequenceEqual(content.References), "non-ID dependencies changed");
            var expectedTags = before[c].ToList();
            for (int i = 0; i < slots[c].Count; i++)
            {
                var prior = slots[c][i];
                int wanted = mapping.TryGetValue(prior.StoredId, out int replacement) ? replacement : prior.StoredId;
                Equal(wanted, after[i].StoredId, "simultaneous reference mapping");
                Check(wanted == 0 || destinationIds.Contains(wanted), "dangling positive consumer ID");
                expectedTags[prior.PayloadIndex] = new DxfTag(90, wanted);
                Equal(prior.StoredId, before[c][prior.PayloadIndex].Value, "prior reference snapshot mutated");
            }
            Check(OwnershipTagValues(expectedTags).SequenceEqual(OwnershipTagValues(content.Payload)), "non-reference content field changed");
        }
        if (mode == "identity")
        {
            Check(ReferenceEquals(oldMap, map.Payload) && ReferenceEquals(oldEntries, map.Entries), "identity map request changed snapshots");
            for (int i = 0; i < contents.Length; i++) Check(ReferenceEquals(before[i], contents[i].Payload), "identity consumer request changed snapshots");
        }
        TableContentSave(doc, binary, $"cell-style-consumers-after-{file}-{binary}-{mode}.dxf");
        var reload = TableContentLoad(TableContentSave(doc, !binary)); var nextMap = CellStyleMapObject(reload);
        var nextIds = nextMap.Entries.Select(e => e.Id).ToHashSet();
        foreach (var content in reload.Objects.Items.OfType<DxfStoredTableContent>())
            Check(content.GetCellStyleReferences().All(r => r.StoredId == 0 || nextIds.Contains(r.StoredId)), "dangling ID after opposite-transport reload");
        Equal(0, reload.Objects.Validate().Count, "coordinated graph validation");
        var stable = map.Payload;
        var currentDefinitions = map.Entries.Select(DxfCellStyleMapEntryDefinition.FromEntry).ToArray();
        Equal(0, map.ReplaceStructureAndRemapConsumers(currentDefinitions, Array.Empty<KeyValuePair<int, int>>()), "repeat identity changed IDs");
        Check(ReferenceEquals(stable, map.Payload), "repeat identity changed map packet");
    }

    private static void AssertConsumerRejected(DxfDocument doc, Action action)
    {
        var map = CellStyleMapObject(doc); var payload = map.Payload; var entries = map.Entries; var refs = map.References.ToArray();
        var contents = doc.Objects.Items.OfType<DxfStoredTableContent>().ToArray();
        var before = contents.Select(c => c.Payload).ToArray();
        var values = contents.Select(c => c.StoredValues).ToArray(); long seed = OwnershipSeed(doc); int members = doc.Objects.Items.Count;
        Exception? failure = null; try { action(); } catch (Exception error) { failure = error; }
        Check(failure is ArgumentException or NotSupportedException or InvalidOperationException, "invalid coordinated operation accepted or unexpected exception: " + failure);
        Check(ReferenceEquals(payload, map.Payload) && ReferenceEquals(entries, map.Entries) && refs.SequenceEqual(map.References), "failed operation published map state");
        for (int i = 0; i < contents.Length; i++)
            Check(ReferenceEquals(before[i], contents[i].Payload) && ReferenceEquals(values[i], contents[i].StoredValues), "failed operation published consumer state");
        Equal(seed, OwnershipSeed(doc), "failed operation allocated handles");
        Equal(members, doc.Objects.Items.Count, "failed operation changed membership");
    }

    private static void CellStyleConsumerIds(string file, bool binary, string failure)
    {
        var doc = ConsumerLoad(file, binary); var map = CellStyleMapObject(doc);
        var (definitions, mapping) = ConsumerRequest(map, "rename");
        if (failure == "invalid-old-id")
        {
            doc = ConsumerLoad(file, binary, raw =>
            {
                var content = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "TABLECONTENT"); var tags = content.Tags.ToList();
                int i = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "TABLEROW_BEGIN")); tags[i + 1] = new DxfTag(90, 999);
                return raw.WithRecord(content, tags);
            }); map = CellStyleMapObject(doc); (definitions, mapping) = ConsumerRequest(map, "rename");
        }
        if (failure == "duplicate-source-id") map.ReplaceStructure(map.Entries.Select(e => new DxfCellStyleMapEntryDefinition(1, e.StoredType, e.Name, DxfCellStyleFormatDefinition.FromFormat(e.Format))));
        if (failure == "duplicate-destination-id") definitions = definitions.Select(e => new DxfCellStyleMapEntryDefinition(11, e.StoredType, e.Name, e.Format)).ToArray();
        if (failure == "missing-fallback") { (definitions, mapping) = ConsumerRequest(map, "remove"); mapping.Clear(); }
        if (failure == "empty-target-without-fallback") { definitions = Array.Empty<DxfCellStyleMapEntryDefinition>(); mapping.Clear(); }
        if (failure == "missing-source") { mapping.Clear(); mapping.Add(777, 11); }
        if (failure == "missing-target") mapping[1] = 777;
        if (failure == "negative-target") mapping[1] = -1;
        if (failure == "zero-source") { mapping.Clear(); mapping.Add(0, 11); }
        IEnumerable<KeyValuePair<int, int>> request = failure == "duplicate-mapping" ? new[] { new KeyValuePair<int, int>(1, 11), new KeyValuePair<int, int>(1, 12) } : mapping;
        AssertConsumerRejected(doc, () => map.ReplaceStructureAndRemapConsumers(definitions, request));
        map.ReplaceEntryNames(map.Entries.Select(e => e.Name)); // The guard resets even on unsupported source data.
    }

    private static void CellStyleConsumerAtomic(bool binary, string failure)
    {
        var doc = ConsumerLoad("sample_AC1021_ascii.dxf", binary); var map = CellStyleMapObject(doc);
        var (definitions, mapping) = ConsumerRequest(map, "rename");
        IEnumerable<DxfCellStyleMapEntryDefinition> Definitions()
        {
            try
            {
                yield return definitions[0];
                if (failure == "definitions-throw") throw new InvalidOperationException("definitions");
                foreach (var entry in definitions.Skip(1)) yield return entry;
            }
            finally { if (failure == "definitions-dispose") throw new InvalidOperationException("definitions disposal"); }
        }
        IEnumerable<KeyValuePair<int, int>> Mapping()
        {
            try
            {
                if (failure == "map-reentry") Throws<InvalidOperationException>(() => map.ReplaceStructureAndRemapConsumers(definitions, mapping));
                if (failure == "names-reentry") Throws<InvalidOperationException>(() => map.ReplaceEntryNames(map.Entries.Select(e => e.Name)));
                if (failure == "entries-reentry") Throws<InvalidOperationException>(() => map.ReplaceEntries(Array.Empty<DxfStoredCellStyleMapEntryEdit>()));
                if (failure == "structure-reentry") Throws<InvalidOperationException>(() => map.ReplaceStructure(definitions));
                foreach (var pair in mapping) yield return pair;
                if (failure == "mapping-throw") throw new InvalidOperationException("mapping");
                if (failure == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            }
            finally { if (failure == "mapping-dispose") throw new InvalidOperationException("mapping disposal"); }
        }
        if (failure == "content-outer")
        {
            var content = doc.Objects.Items.OfType<DxfStoredTableContent>().Last();
            IEnumerable<DxfStoredTableContentValueEdit> Edits()
            {
                Throws<InvalidOperationException>(() => map.ReplaceStructureAndRemapConsumers(definitions, mapping));
                yield break;
            }
            AssertConsumerRejected(doc, () => content.ReplaceContent(content.Name, content.Description, content.TableStyle, Edits()));
        }
        else AssertConsumerRejected(doc, () => map.ReplaceStructureAndRemapConsumers(failure == "null-entries" ? null! : Definitions(), failure == "null-mapping" ? null! : Mapping()));
        doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2007;
        Check(map.ReplaceStructureAndRemapConsumers(definitions, mapping) > 0, "guard did not reset after coordinated failure");
    }

    private static void CellStyleConsumerSchema(bool binary, string failure)
    {
        var doc = failure == "wrong-map-owner" ? CellStyleMapLoad(CellFormatRaw(DxfVersion.AutoCad2007), binary) : ConsumerLoad("sample_AC1021_ascii.dxf", binary, raw =>
        {
            if (failure == "unqualified-map")
            {
                var map = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "CELLSTYLEMAP"); var tags = map.Tags.ToList();
                int f = tags.FindIndex(t => t.Code == 1 && Equals(t.Value, "CONTENTFORMAT_BEGIN")); tags.Insert(f + 1, new DxfTag(420, 0x123456));
                return raw.WithRecord(map, tags);
            }
            if (failure == "private-table")
            {
                var table = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "ACAD_TABLE");
                return raw.WithRecord(table, table.Tags.Concat(new[] { new DxfTag(100, "PrivateTableData"), new DxfTag(90, 1) }));
            }
            var contents = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "TABLECONTENT").ToArray();
            var chosen = failure is "late-consumer" or "opaque-content" or "geometry-extension" ? contents.Last() : contents.First();
            var packet = chosen.Tags.ToList();
            int index = packet.FindIndex(t => t.Code == 1 && Equals(t.Value, "TABLECOLUMN_BEGIN"));
            if (failure is "duplicate-slot" or "late-consumer") packet.Insert(index + 1, new DxfTag(90, 1));
            if (failure == "missing-slot") packet.RemoveRange(index, 4);
            if (failure == "negative-id") packet[index + 1] = new DxfTag(90, -1);
            if (failure == "nested-slot")
            {
                index = packet.FindIndex(t => t.Code == 309 && Equals(t.Value, "TABLEFORMAT_END"));
                packet.InsertRange(index, new[] { new DxfTag(1, "TABLECELL_BEGIN"), new DxfTag(90, 1), new DxfTag(91, 0), new DxfTag(309, "TABLECELL_END") });
            }
            if (failure == "geometry-extension")
            {
                index = packet.FindIndex(t => t.Code == 309 && Equals(t.Value, "TABLECELL_END")); packet.Insert(index, new DxfTag(98, 5));
            }
            if (failure == "opaque-content") packet.AddRange(new[] { new DxfTag(100, "PrivateContentData"), new DxfTag(90, 1) });
            return raw.WithRecord(chosen, packet);
        });
        var actualMap = CellStyleMapObject(doc);
        var definitions = failure == "unqualified-map" ? Array.Empty<DxfCellStyleMapEntryDefinition>() : actualMap.Entries.Select(DxfCellStyleMapEntryDefinition.FromEntry).ToArray();
        AssertConsumerRejected(doc, () => actualMap.ReplaceStructureAndRemapConsumers(definitions, Array.Empty<KeyValuePair<int, int>>()));
        if (failure == "late-consumer")
        {
            var ordered = doc.Objects.Items.OfType<DxfStoredTableContent>().ToArray();
            Check(ordered.Length >= 2 && ordered[0].GetCellStyleReferences().Count > 0, "first consumer must stage successfully before later rejection");
            Throws<NotSupportedException>(() => ordered.Last().GetCellStyleReferences());
            var (renamed, mapping) = ConsumerRequest(actualMap, "rename");
            AssertConsumerRejected(doc, () => actualMap.ReplaceStructureAndRemapConsumers(renamed, mapping));
        }
    }

    private static void CellStyleConsumerLiteral(bool binary, string literal)
    {
        var doc = ConsumerLoad("acad_table_simple.dxf", binary);
        var content = doc.Objects.Items.OfType<DxfStoredTableContent>().First();
        var value = content.StoredValues.First(v => v.Kind == DxfStoredTableContentValueKind.String);
        content.ReplaceContent(content.Name, content.Description, content.TableStyle, new[] { value.WithValue(literal, literal) });
        var map = CellStyleMapObject(doc); var (definitions, mapping) = ConsumerRequest(map, "rename");
        int count = content.GetCellStyleReferences().Count;
        Check(map.ReplaceStructureAndRemapConsumers(definitions, mapping) > 0, "literal text incorrectly blocked real references");
        Equal(count, content.GetCellStyleReferences().Count, "literal text generated phantom style-ID references");
        Check(content.StoredValues.Any(v => Equals(v.Value, literal) && v.FormattedText == literal), "literal marker text changed during remap");
        var reload = TableContentLoad(TableContentSave(doc, binary));
        Check(reload.Objects.Items.OfType<DxfStoredTableContent>().SelectMany(c => c.StoredValues).Any(v => Equals(v.Value, literal)), "literal marker text reload");
    }
}
