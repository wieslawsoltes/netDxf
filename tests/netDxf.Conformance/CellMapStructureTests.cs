using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] MapStructureShapes = { "empty", "empty-format", "no-margins", "zero-grids", "full", "mixed" };
    private static void RegisterCellMapStructureTests()
    {
        Run("map-structure/definitions", CellMapDefinitions);
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
        foreach (bool binary in new[] { false, true })
        {
            foreach (string shape in MapStructureShapes)
                Run($"map-structure/create/{version}/{binary}/{shape}", () => CellMapCreate(version, binary, shape));
            foreach (string fault in new[] { "duplicate", "reserved", "foreign-owner", "detached-owner", "bad-class", "null-entry", "null-sequence", "enumeration", "disposal", "reentry", "foreign-style", "foreign-line", "detached-style", "invalid-graph", "escaped-limit", "empty-name", "exhausted" })
                Run($"map-structure/create-reject/{version}/{binary}/{fault}", () => CellMapCreationRejected(version, binary, fault));
            foreach (string fault in new[] { "null-sequence", "null-entry", "enumeration", "disposal", "structure-reentry", "names-reentry", "entries-reentry", "profile", "foreign-resource", "unqualified" })
                Run($"map-structure/replace-reject/{version}/{binary}/{fault}", () => CellMapReplacementRejected(version, binary, fault));
        }
        foreach (string file in TableContentFiles)
        foreach (bool binary in new[] { false, true })
        {
            foreach (var target in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
                Run($"map-structure/transfer/{file}/{target}/{binary}", () => CellMapTransfer(file, target, binary));
            foreach (string mode in new[] { "reorder", "expand", "frames", "clear" })
                Run($"map-structure/native/{file}/{binary}/{mode}", () => CellMapNativeStructure(file, binary, mode));
        }
        foreach (bool binary in new[] { false, true })
        {
            Run($"map-structure/lifecycle/{binary}", () => CellMapStructureLifecycle(binary));
            Run($"map-structure/older-profile/{binary}", () =>
            {
                var doc = new DxfDocument(DxfVersion.AutoCad2000); var root = doc.Objects.Root; long seed = OwnershipSeed(doc);
                Throws<NotSupportedException>(() => doc.Objects.CreateCellStyleMap(root, "MAP", Array.Empty<DxfCellStyleMapEntryDefinition>()));
                Equal(seed, OwnershipSeed(doc), "unsupported profile allocated a map");
            });
        }
    }

    private static DxfCellStyleFormatDefinition AuthoredFormat(TextStyle? style, Linetype? line, bool margins = true, int grids = 6)
        => new(5, 1, CellFormatValues(0), CellContentValues(0), style!, margins ? (short)1 : (short)0,
            margins ? CellMargins(0) : null!, Enumerable.Range(0, grids).Select(i => new DxfCellGridFormatDefinition(1 << i, CellGridValues(0, i), line!)));

    private static DxfCellStyleMapEntryDefinition[] AuthoredEntries(TextStyle style, Linetype line, string shape)
    {
        if (shape == "empty") return Array.Empty<DxfCellStyleMapEntryDefinition>();
        var format = shape == "empty-format" ? new DxfCellStyleFormatDefinition(5)
            : AuthoredFormat(style, line, shape != "no-margins", shape == "zero-grids" ? 0 : 6);
        var first = new DxfCellStyleMapEntryDefinition(7, 3, "Authored Ω \\U+0041 😀", format);
        return shape == "mixed" ? new[] { first, new DxfCellStyleMapEntryDefinition(-1, -2, "Empty", new DxfCellStyleFormatDefinition(9)), first }
            : new[] { first };
    }

    private static void CellMapCreate(DxfVersion version, bool binary, string shape)
    {
        var doc = new DxfDocument(version);
        var style = doc.TextStyles.Add(new TextStyle("DEST_STYLE", "txt.shx"));
        var line = doc.Linetypes.Add(new Linetype("DEST_LINE"));
        var root = doc.Objects.Root; long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count;
        var definitions = AuthoredEntries(style, line, shape);
        var map = doc.Objects.CreateCellStyleMap(root, "AUTHORED_MAP", definitions);
        Equal(seed + 1, OwnershipSeed(doc), "creation must allocate exactly one handle");
        Equal(count + 1, doc.Objects.Items.Count, "one map registration");
        Check(ReferenceEquals(root["AUTHORED_MAP"], map) && ReferenceEquals(root, map.Owner), "new map ownership");
        Check(map.PersistentReactors.SequenceEqual(new[] { root }), "canonical owner reactor");
        Equal(definitions.Length, map.Entries.Count, "authored entry count");
        var packet = map.Payload; var entries = map.Entries;
        map.ReplaceStructure(definitions);
        Check(ReferenceEquals(packet, map.Payload) && ReferenceEquals(entries, map.Entries), "exact authored structure no-op changed snapshots");
        var bytes = TableContentSave(doc, binary, $"map-structure-create-{version}-{binary}-{shape}.dxf");
        var loaded = TableContentLoad(bytes); var reloaded = CellStyleMapObject(loaded);
        Equal(0, loaded.Objects.Validate().Count, "authored map graph invalid");
        Check(OwnershipTagValues(map.Payload).SequenceEqual(OwnershipTagValues(reloaded.Payload)), "authored payload roundtrip");
        Equal(definitions.Length, reloaded.Entries.Count, "reload entry count");
        if (map.References.Count != 0) Check(!doc.TextStyles.Remove(style), "authored STYLE dependency not guarded");
        if (map.References.OfType<Linetype>().Any()) Check(!doc.Linetypes.Remove(line), "authored LTYPE dependency not guarded");
    }

    private static void CellMapTransfer(string file, DxfVersion target, bool binary)
    {
        var source = TableContentLoad(TableContentSourceBytes(file)); var original = CellStyleMapObject(source);
        var before = OwnershipTagValues(original.Payload).ToArray(); var refs = original.References.ToArray(); long seed = OwnershipSeed(source);
        var destination = new DxfDocument(target);
        var style = destination.TextStyles.Add(new TextStyle("DEST_STYLE", "txt.shx"));
        var line = destination.Linetypes.Add(new Linetype("DEST_LINE"));
        var definitions = original.Entries.Select(entry =>
        {
            var definition = DxfCellStyleMapEntryDefinition.FromEntry(entry); int calls = 0;
            var remapped = definition.Format.RemapResources(resource => { calls++; return resource is TextStyle ? style : line; });
            Check(calls <= 2, "remapping repeated resources called user code repeatedly");
            return new DxfCellStyleMapEntryDefinition(definition.Id, definition.StoredType, definition.Name, remapped);
        }).ToArray();
        var map = destination.Objects.CreateCellStyleMap(destination.Objects.Root, "AUTHORED_MAP", definitions);
        Check(map.References.All(r => ReferenceEquals(r, style) || ReferenceEquals(r, line)), "foreign reference leaked into destination");
        TableContentSave(destination, binary, $"map-structure-transfer-{file}-{target}-{binary}.dxf");
        var loaded = CellStyleMapObject(TableContentLoad(TableContentSave(destination, !binary)));
        Equal(target, loaded.SourceVersion, "new map must use target serialization profile");
        Check(map.Entries.Select(e => e.Name).SequenceEqual(loaded.Entries.Select(e => e.Name)), "transferred decoded names");
        Check(before.SequenceEqual(OwnershipTagValues(original.Payload)) && refs.SequenceEqual(original.References), "export/transfer mutated source");
        Equal(seed, OwnershipSeed(source), "transfer allocated in source");
    }

    private static DxfCellStyleMapEntryDefinition[] Restructured(DxfStoredCellStyleMap map, string mode)
    {
        var definitions = map.Entries.Select(DxfCellStyleMapEntryDefinition.FromEntry).ToArray();
        if (mode == "clear") return Array.Empty<DxfCellStyleMapEntryDefinition>();
        if (mode == "reorder") return definitions.Reverse().ToArray();
        if (mode == "frames") return new[] { new DxfCellStyleMapEntryDefinition(definitions[0].Id, definitions[0].StoredType, definitions[0].Name, new DxfCellStyleFormatDefinition(5)), definitions[1], definitions[2] };
        return new[] { definitions[0], definitions[1], definitions[2], new DxfCellStyleMapEntryDefinition(42, 9, "Authored Ω \\U+0041 😀", definitions[0].Format) };
    }

    private static void CellMapNativeStructure(string file, bool binary, string mode)
    {
        var doc = TableContentLoad(TableContentSourceBytes(file)); var map = CellStyleMapObject(doc);
        var oldEntries = map.Entries; var oldPayload = map.Payload; var oldRefs = map.References; var refValues = oldRefs.ToArray();
        var wire = OwnershipTagValues(oldPayload).ToArray(); var owner = map.Owner; var reactors = map.PersistentReactors.ToArray();
        TableContentSave(doc, binary, $"map-structure-native-before-{file}-{binary}-{mode}.dxf"); long seed = OwnershipSeed(doc);
        var request = Restructured(map, mode); map.ReplaceStructure(request);
        Equal(request.Length, map.Entries.Count, "new entry count");
        Equal(seed, OwnershipSeed(doc), "structural replacement allocated handles");
        Check(wire.SequenceEqual(OwnershipTagValues(oldPayload)) && refValues.SequenceEqual(oldRefs), "structural change mutated old snapshots");
        Check(ReferenceEquals(owner, map.Owner) && reactors.SequenceEqual(map.PersistentReactors), "structural change altered common ownership");
        Throws<ArgumentException>(() => map.ReplaceEntries(new[] { oldEntries[0].WithName("stale") }));
        TableContentSave(doc, binary, $"map-structure-native-after-{file}-{binary}-{mode}.dxf");
        var loaded = CellStyleMapObject(TableContentLoad(TableContentSave(doc, !binary)));
        Check(OwnershipTagValues(map.Payload).SequenceEqual(OwnershipTagValues(loaded.Payload)), "new structure transport roundtrip");
        if (mode == "clear") Equal(0, map.References.Count, "cleared entries kept format dependencies");
    }

    private static void CellMapCreationRejected(DxfVersion version, bool binary, string fault)
    {
        var doc = new DxfDocument(version); var root = doc.Objects.Root; var owner = root;
        var style = doc.TextStyles.Add(new TextStyle("DEST_STYLE", "txt.shx")); var line = doc.Linetypes.Add(new Linetype("DEST_LINE"));
        if (fault == "foreign-style") style = new DxfDocument(version).TextStyles.Add(new TextStyle(style.Name, "txt.shx"));
        if (fault == "foreign-line") line = new DxfDocument(version).Linetypes.Add(new Linetype(line.Name));
        if (fault == "detached-style") style = new TextStyle("DETACHED", "txt.shx");
        if (fault == "foreign-owner") owner = new DxfDocument(version).Objects.Root;
        if (fault == "detached-owner") owner = new DxfDictionary();
        if (fault == "bad-class") doc.Classes.Add(new DxfClass("CELLSTYLEMAP", "Private", "Private"));
        if (fault == "duplicate") root.Add("AUTHORED_MAP", new DxfXRecord());
        if (fault == "invalid-graph") root.PersistentReactors.Add(new Line(Vector3.Zero, Vector3.UnitX));
        if (fault == "exhausted") typeof(DxfDocument).GetProperty("NumHandles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(doc, long.MaxValue);
        long seed = OwnershipSeed(doc); int count = doc.Objects.Items.Count; int names = root.Count;
        var definitions = AuthoredEntries(style, line, "full");
        if (fault == "escaped-limit") definitions[0] = new DxfCellStyleMapEntryDefinition(1, 1, new string('\\', 149797), definitions[0].Format);
        IEnumerable<DxfCellStyleMapEntryDefinition> Input()
        {
            try
            {
                if (fault == "reentry") Throws<InvalidOperationException>(() => doc.Objects.CreateCellStyleMap(root, "INNER", Array.Empty<DxfCellStyleMapEntryDefinition>()));
                yield return fault == "null-entry" ? null! : definitions[0];
                if (fault == "enumeration") throw new InvalidOperationException("enumeration");
            }
            finally { if (fault == "disposal") throw new InvalidOperationException("disposal"); }
        }
        Exception? error = null;
        try { doc.Objects.CreateCellStyleMap(owner, fault == "empty-name" ? "" : fault == "reserved" ? "ACAD_GROUP" : "AUTHORED_MAP", fault == "null-sequence" ? null! : Input()); }
        catch (Exception e) { error = e; }
        Check(error is ArgumentException or InvalidOperationException, "invalid creation accepted or unexpected failure");
        Equal(seed, OwnershipSeed(doc), "rejected creation allocated a handle"); Equal(count, doc.Objects.Items.Count, "rejected creation registered object"); Equal(names, root.Count, "rejected creation changed dictionary");
        if (fault == "invalid-graph") root.PersistentReactors.Clear();
        if (fault == "bad-class") doc.Classes.Remove("CELLSTYLEMAP");
        if (fault == "exhausted") return;
        var recovered = doc.Objects.CreateCellStyleMap(root, "RECOVERED", Array.Empty<DxfCellStyleMapEntryDefinition>());
        Equal(0, recovered.Entries.Count, "creation guard did not reset");
        TableContentSave(doc, binary);
    }

    private static void CellMapReplacementRejected(DxfVersion version, bool binary, string fault)
    {
        var raw = CellFormatRaw(version, (_, packet) => { if (fault == "unqualified") packet.Insert(18, new DxfTag(420, 0x123456)); });
        var doc = CellStyleMapLoad(raw, binary); var map = CellStyleMapObject(doc);
        var style = fault == "foreign-resource" ? new DxfDocument(version).TextStyles["Standard"] : doc.TextStyles["SOURCE_MAP_STYLE"];
        var definition = new DxfCellStyleMapEntryDefinition(1, 1, "new", AuthoredFormat(style, doc.Linetypes["SOURCE_MAP_LINE"]));
        var payload = map.Payload; var entries = map.Entries; var refs = map.References.ToArray(); long seed = OwnershipSeed(doc);
        IEnumerable<DxfCellStyleMapEntryDefinition> Input()
        {
            try
            {
                if (fault == "structure-reentry") Throws<InvalidOperationException>(() => map.ReplaceStructure(Array.Empty<DxfCellStyleMapEntryDefinition>()));
                if (fault == "names-reentry") Throws<InvalidOperationException>(() => map.ReplaceEntryNames(map.Entries.Select(e => e.Name)));
                if (fault == "entries-reentry") Throws<InvalidOperationException>(() => map.ReplaceEntries(Array.Empty<DxfStoredCellStyleMapEntryEdit>()));
                yield return fault == "null-entry" ? null! : definition;
                if (fault == "enumeration") throw new InvalidOperationException("enumeration");
                if (fault == "profile") doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
            }
            finally { if (fault == "disposal") throw new InvalidOperationException("disposal"); }
        }
        Exception? error = null; try { map.ReplaceStructure(fault == "null-sequence" ? null! : Input()); } catch (Exception e) { error = e; }
        Check(error is ArgumentException or InvalidOperationException or NotSupportedException, "invalid structural edit accepted");
        Check(ReferenceEquals(payload, map.Payload) && ReferenceEquals(entries, map.Entries) && refs.SequenceEqual(map.References), "rejection changed structural snapshots");
        Equal(seed, OwnershipSeed(doc), "rejection allocated a handle");
        doc.DrawingVariables.AcadVer = version;
        map.ReplaceEntryNames(map.Entries.Select(e => e.Name + " recovered"));
    }

    private static void CellMapStructureLifecycle(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var root = doc.Objects.Root;
        var style = doc.TextStyles.Add(new TextStyle("DEST_STYLE", "txt.shx")); var line = doc.Linetypes.Add(new Linetype("DEST_LINE"));
        var map = doc.Objects.CreateCellStyleMap(root, "AUTHORED_MAP", AuthoredEntries(style, line, "full"));
        var refs = map.References; Equal(7, refs.Count, "created repeated references");
        map.ReplaceStructure(Array.Empty<DxfCellStyleMapEntryDefinition>());
        Equal(7, refs.Count, "earlier membership changed");
        Check(doc.TextStyles.Remove(style) && doc.Linetypes.Remove(line), "removed structure did not release resources");
        map.ReplaceStructure(new[] { new DxfCellStyleMapEntryDefinition(1, 0, "new", new DxfCellStyleFormatDefinition(5)) });
        map.ReplaceEntryNames(new[] { "renamed" }); map.ReplaceEntries(new[] { map.Entries[0].WithName("edited") });
        var reload = CellStyleMapObject(TableContentLoad(TableContentSave(doc, binary))); Equal("edited", reload.Entries[0].Name, "old editing APIs after structural changes");
        Throws<NotSupportedException>(() => doc.Objects.EraseOwnedTree(map));
    }

    private static void CellMapDefinitions()
    {
        var empty = new DxfCellStyleFormatDefinition(5);
        Throws<ArgumentNullException>(() => new DxfCellStyleMapEntryDefinition(1, 1, "x", null!));
        Throws<ArgumentNullException>(() => DxfCellStyleMapEntryDefinition.FromEntry(null!));
        Throws<ArgumentNullException>(() => DxfCellStyleFormatDefinition.FromFormat(null!));
        Throws<ArgumentNullException>(() => empty.RemapResources(null!));
        Throws<ArgumentOutOfRangeException>(() => new DxfCellGridFormatDefinition(0, CellGridValues(0, 0), null!));
        Throws<ArgumentNullException>(() => new DxfCellGridFormatDefinition(1, null!, null!));
        Throws<ArgumentException>(() => new DxfCellStyleFormatDefinition(5, 0, CellFormatValues(0), null!, null!, 0, null!, Array.Empty<DxfCellGridFormatDefinition>()));
        Throws<ArgumentException>(() => new DxfCellStyleFormatDefinition(5, 1, null!, null!, null!, 0, null!, Array.Empty<DxfCellGridFormatDefinition>()));
        Throws<ArgumentException>(() => new DxfCellStyleFormatDefinition(5, 1, CellFormatValues(0), CellContentValues(0), null!, 0, CellMargins(0), Array.Empty<DxfCellGridFormatDefinition>()));
        var style = new TextStyle("source", "txt.shx"); var line = new Linetype("line"); var complete = AuthoredFormat(style, line);
        Throws<ArgumentException>(() => complete.RemapResources(_ => null!));
        Throws<ArgumentException>(() => complete.RemapResources(_ => line));
        Throws<InvalidOperationException>(() => complete.RemapResources(_ => throw new InvalidOperationException("callback")));
        var grid = new DxfCellGridFormatDefinition(1, CellGridValues(0, 0), line); var caller = new[] { grid };
        var definition = new DxfCellStyleFormatDefinition(5, 1, CellFormatValues(0), CellContentValues(0), style, 0, null!, caller); caller[0] = null!;
        Check(ReferenceEquals(grid, definition.Borders[0]), "definition aliases caller array");
        Throws<NotSupportedException>(() => ((IList<DxfCellGridFormatDefinition>)definition.Borders).Clear());
        int consumed = 0; bool disposed = false;
        IEnumerable<DxfCellGridFormatDefinition> Infinite() { try { while (true) { consumed++; yield return grid; } } finally { disposed = true; } }
        Throws<ArgumentException>(() => new DxfCellStyleFormatDefinition(5, 1, CellFormatValues(0), CellContentValues(0), style, 0, null!, Infinite()));
        Check(consumed == 7 && disposed, "unbounded grid definition enumeration");
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var root = doc.Objects.Root; long seed = OwnershipSeed(doc);
        var entry = new DxfCellStyleMapEntryDefinition(1, 1, "empty", empty); consumed = 0; disposed = false;
        IEnumerable<DxfCellStyleMapEntryDefinition> Endless() { try { while (true) { consumed++; yield return entry; } } finally { disposed = true; } }
        Throws<ArgumentException>(() => doc.Objects.CreateCellStyleMap(root, "OVERFLOW", Endless()));
        Check(consumed == DxfStoredCellStyleMap.MaximumAuthoredEntries + 1 && disposed, "unbounded entry enumeration");
        Equal(seed, OwnershipSeed(doc), "limit rejection allocated handles");
    }
}
