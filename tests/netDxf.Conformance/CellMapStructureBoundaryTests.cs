using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterCellMapStructureBoundaryTests()
    {
        foreach (bool binary in new[] { false, true })
        foreach (string kind in new[] { "parent-cache", "names-reentry", "entries-reentry", "null-owner", "target-removed", "slot-taken", "target-added", "tag-limit", "metadata", "wrong-kind-export", "null-resources", "mask-order", "class-count", "constructor-disposal", "entry-disposal" })
            Run($"map-structure/boundary/{binary}/{kind}", () => CellMapStructureBoundary(binary, kind));
    }

    private static void CellMapStructureBoundary(bool binary, string kind)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var root = doc.Objects.Root;
        var style = doc.TextStyles.Add(new TextStyle("DEST_STYLE", "txt.shx")); var line = doc.Linetypes.Add(new Linetype("DEST_LINE"));
        var definitions = AuthoredEntries(style, line, "full"); long seed = OwnershipSeed(doc);
        if (kind == "null-owner")
        { Throws<ArgumentNullException>(() => doc.Objects.CreateCellStyleMap(null!, "MAP", definitions)); Equal(seed, OwnershipSeed(doc), "null owner allocated"); return; }
        if (kind == "parent-cache")
        {
            doc = TableStyleLoad(TableStylePacket(), binary); var tableStyle = TableStyleObject(doc); var owner = new DxfDictionary();
            doc.Objects.SetExtensionDictionary(tableStyle, owner);
            var map = doc.Objects.CreateCellStyleMap(owner, "acad_roundtrip_2008_tablestyle_cellstylemap", Array.Empty<DxfCellStyleMapEntryDefinition>());
            Check(ReferenceEquals(map, tableStyle.StoredCellStyleMap), "authored native slot did not update parent cache");
            var loaded = TableContentLoad(TableContentSave(doc, binary));
            Check(ReferenceEquals(TableStyleObject(loaded).StoredCellStyleMap, CellStyleMapObject(loaded)), "parent map reference reload"); return;
        }
        if (kind == "constructor-disposal")
        {
            IEnumerable<DxfCellGridFormatDefinition> Grids()
            { try { yield return definitions[0].Format.Borders[0]; } finally { throw new InvalidOperationException("dispose"); } }
            Throws<InvalidOperationException>(() => new DxfCellStyleFormatDefinition(5, 1, CellFormatValues(0), CellContentValues(0), style, 0, null!, Grids()));
            Equal(seed, OwnershipSeed(doc), "definition constructor mutated source"); return;
        }
        if (kind is "target-removed" or "slot-taken" or "target-added" or "entry-disposal")
        {
            if (kind == "target-added") { Check(doc.TextStyles.Remove(style), "detach target"); style = new TextStyle("ADDED", "txt.shx"); definitions = AuthoredEntries(style, line, "full"); }
            IEnumerable<DxfCellStyleMapEntryDefinition> Input()
            {
                try
                {
                    yield return definitions[0];
                    if (kind == "target-removed") Check(doc.TextStyles.Remove(style), "callback remove");
                    if (kind == "slot-taken") root.Add("MAP", new DxfXRecord());
                    if (kind == "target-added") doc.TextStyles.Add(style);
                }
                finally { if (kind == "entry-disposal") throw new InvalidOperationException("dispose"); }
            }
            if (kind == "target-added")
            { var authored = doc.Objects.CreateCellStyleMap(root, "MAP", Input()); Check(ReferenceEquals(style, authored.Entries[0].Format.Content.TextStyle), "post-callback target registration lost"); }
            else
            {
                Exception? error = null; try { doc.Objects.CreateCellStyleMap(root, "MAP", Input()); } catch (Exception e) { error = e; }
                Check(error is ArgumentException or InvalidOperationException, "callback-invalid creation succeeded");
                Check(!doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Any(), "callback failure published a map");
                if (kind != "slot-taken") Equal(seed, OwnershipSeed(doc), "rejected creation allocated beyond callback");
            }
            return;
        }
        if (kind == "tag-limit")
        {
            Throws<ArgumentException>(() => doc.Objects.CreateCellStyleMap(root, "MAP", Enumerable.Repeat(definitions[0], 10000)));
            Equal(seed, OwnershipSeed(doc), "tag budget failure allocated handles"); return;
        }
        if (kind == "wrong-kind-export")
        {
            doc = CellStyleMapLoad(CellFormatRaw(DxfVersion.AutoCad2018, (source, tags) => tags[18] = new DxfTag(340, source.Linetypes["SOURCE_MAP_LINE"].Handle)), binary);
            Throws<NotSupportedException>(() => DxfCellStyleMapEntryDefinition.FromEntry(CellStyleMapObject(doc).Entries[0])); return;
        }
        if (kind == "null-resources") definitions = new[] { new DxfCellStyleMapEntryDefinition(1, 0, "null refs", AuthoredFormat(null, null)) };
        if (kind == "mask-order") definitions = new[] { new DxfCellStyleMapEntryDefinition(1, 0, "masks", new DxfCellStyleFormatDefinition(5, 1,
            CellFormatValues(0), CellContentValues(0), style, 0, null!, new[] { 8, 1, 8 }.Select(mask => new DxfCellGridFormatDefinition(mask, CellGridValues(0, 0), line)))) };
        var current = doc.Objects.CreateCellStyleMap(root, "MAP", definitions);
        var old = current.Payload; var oldEntries = current.Entries;
        if (kind is "names-reentry" or "entries-reentry")
        {
            IEnumerable<string> Names() { Throws<InvalidOperationException>(() => current.ReplaceStructure(definitions)); foreach (var e in current.Entries) yield return e.Name + " invalid"; }
            IEnumerable<DxfStoredCellStyleMapEntryEdit> Edits() { Throws<InvalidOperationException>(() => current.ReplaceStructure(definitions)); yield return current.Entries[0].WithName("invalid"); }
            if (kind == "names-reentry") Throws<InvalidOperationException>(() => current.ReplaceEntryNames(Names()));
            else Throws<InvalidOperationException>(() => current.ReplaceEntries(Edits()));
            Check(ReferenceEquals(old, current.Payload) && ReferenceEquals(oldEntries, current.Entries), "cross-API reentry published changes");
        }
        if (kind == "metadata")
        {
            var metadata = new DxfDictionary(); metadata.Add("extra", new DxfXRecord()); doc.Objects.SetExtensionDictionary(current, metadata);
            current.XData.Add(new XData(new ApplicationRegistry("MAP_METADATA")) { XDataRecord = { new XDataRecord(XDataCode.String, "retained") } });
            var reactors = current.PersistentReactors.ToArray(); current.ReplaceStructure(Array.Empty<DxfCellStyleMapEntryDefinition>());
            Check(ReferenceEquals(metadata, current.ExtensionDictionary) && reactors.SequenceEqual(current.PersistentReactors), "common metadata replaced");
        }
        if (kind == "class-count") doc.Objects.CreateCellStyleMap(root, "SECOND", definitions);
        var again = TableContentLoad(TableContentSave(doc, binary));
        var loadedMap = (DxfStoredCellStyleMap)again.Objects.Root["MAP"];
        if (kind == "null-resources")
        { Equal(0, loadedMap.References.Count, "null references rebound"); Check(DxfCellStyleMapEntryDefinition.FromEntry(loadedMap.Entries[0]).Format.TextStyle == null, "null reference export"); }
        if (kind == "mask-order") Check(loadedMap.Entries[0].Format.Borders.Select(b => b.StoredIndexMask).SequenceEqual(new[] { 8, 1, 8 }), "mask order normalized");
        if (kind == "class-count") Equal((int?)2, again.Classes["CELLSTYLEMAP"].InstanceCount, "authored class count");
        if (kind == "metadata") Equal("retained", loadedMap.XData["MAP_METADATA"].XDataRecord[0].Value, "metadata roundtrip");
    }
}
