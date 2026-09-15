using System.IO.Compression;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunDataTableTests()
    {
        foreach (var version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2004))
            foreach (bool binary in new[] { false, true })
            {
                Run($"datatable/all-types/{version}/{binary}", () => DataTableAuthored(version, binary));
                Run($"datatable/clone-erase/{version}/{binary}", () => DataTableCloneErase(version, binary));
                foreach (string decoy in new[] { "none", "class", "payload", "control", "unknown-entity", "discarded-entity", "dictionary-entity", "ignored-section", "missing-table-identity", "missing-raster-identity", "table-payload-identity", "raster-payload-identity", "unrelated-managed-dictionary" })
                    Run($"datatable/source-identity/{version}/{binary}/{decoy}", () => DataTableSourceIdentity(version, binary, decoy));
                Run($"datatable/canonical-forward-identity/{version}/{binary}", () => DataTableCanonicalIdentity(version, binary));
                Run($"datatable/symbol-table-identities/{version}/{binary}", () => DataTableSymbolTableIdentity(version, binary));
                Run($"datatable/retained-resource-identities/{version}/{binary}", () => DataTableRetainedIdentities(version, binary));
                Run($"datatable/section-entity-context/{version}/{binary}", () => DataTableSectionContext(version, binary));
                foreach (string defect in new[] { "missing-version", "duplicate-version", "missing-columns", "negative-columns", "huge-columns", "negative-rows", "huge-rows", "missing-name", "missing-column-name", "wrong-type-code", "missing-value", "extra-value", "duplicate-marker", "bool-range", "point-missing-z", "vector-order", "unresolved-reference", "wrong-owner", "duplicate-owner", "foreign-owner", "nonfinite", "surrogate" })
                    Run($"datatable/malformed/{version}/{binary}/{defect}", () => DataTableMalformed(version, binary, defect));
                foreach (string variant in new[] { "version", "type", "field", "subclass", "header" })
                    Run($"datatable/opaque/{version}/{binary}/{variant}", () => DataTableOpaque(version, binary, variant));
            }
        foreach (bool binary in new[] { false, true })
        {
            Run($"datatable/native-R2004/{binary}", () => DataTableNative(binary));
            Run($"datatable/R2000/{binary}", () => DataTableOldProfile(binary));
        }
        Run("datatable/atomic-mutation", DataTableAtomicity);
        Run("datatable/external-clone-map", DataTableExternalMap);
        Run("datatable/caller-enumerator-reentrancy", DataTableReentrancy);
    }
    private static DxfDocument DataTableDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        doc.Entities.Add(new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)));
        var graph = new DxfDictionary(); var pointer = new DxfPlaceholder(); graph.Add("POINTER", pointer);
        var hard = new DxfXRecord(); hard.Data.Add(new DxfTag(1, "Hard-owned payload"));
        var soft = new DxfXRecord(); soft.Data.Add(new DxfTag(1, "Soft-owned payload"));
        var table = new DxfDataTable { Name = "Ω table \\U+0041 🧪" };
        table.SetColumns(3, new[]
        {
            new DxfDataColumn(DxfDataCellType.Integer, "", new object[] { int.MinValue, 0, int.MaxValue }),
            new DxfDataColumn(DxfDataCellType.Double, "Duplicate", new object[] { -2.5, 0.0, 1.25e30 }),
            new DxfDataColumn(DxfDataCellType.String, "Duplicate", new object[] { "", "青 🧪", @"Literal\U+0042" }),
            new DxfDataColumn(DxfDataCellType.Point, "Point", new object[] { new Vector3(1, 2, 3), new Vector3(-4, 5, -6), new Vector3(7, 8, 9) }),
            new DxfDataColumn(DxfDataCellType.ObjectId, "Id", new object[] { pointer, null!, pointer }),
            new DxfDataColumn(DxfDataCellType.HardOwner, "Hard owner", new object[] { hard, null!, null! }),
            new DxfDataColumn(DxfDataCellType.SoftOwner, "Soft owner", new object[] { null!, soft, null! }),
            new DxfDataColumn(DxfDataCellType.HardPointer, "Hard pointer", new object[] { pointer, hard, null! }),
            new DxfDataColumn(DxfDataCellType.SoftPointer, "Soft pointer", new object[] { soft, null!, pointer }),
            new DxfDataColumn(DxfDataCellType.Boolean, "Boolean", new object[] { false, true, false }),
            new DxfDataColumn(DxfDataCellType.Vector, "Vector", new object[] { new Vector3(10, 20, 30), new Vector3(-40, 50, -60), new Vector3(70, 80, 90) })
        });
        graph.Add("TABLE", table); graph.Add("ALIAS", table, false);
        graph.Add("EMPTY", new DxfDataTable());
        var zeroColumns = new DxfDataTable(); zeroColumns.SetColumns(7, Array.Empty<DxfDataColumn>()); graph.Add("ZERO_COLUMNS", zeroColumns);
        var zeroRows = new DxfDataTable(); zeroRows.SetColumns(0, new[] { new DxfDataColumn(DxfDataCellType.String, "", Array.Empty<object>()), new DxfDataColumn(DxfDataCellType.Integer, "", Array.Empty<object>()) }); graph.Add("ZERO_ROWS", zeroRows);
        doc.NamedObjects.Add("DATA_TABLES", graph);
        table.PersistentReactors.Add(graph);
        var extension = new DxfDictionary(); extension.Add("NOTE", new DxfDictionaryVariable { Value = "Metadata" }); doc.Objects.SetExtensionDictionary(table, extension);
        var data = new XData(new ApplicationRegistry("DATATABLE_APP")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "Metadata 青")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, pointer.Handle)); table.XData.Add(data);
        return doc;
    }
    private static DxfDataTable DataTableAssert(DxfDocument doc, string key = "DATA_TABLES")
    {
        var graph = (DxfDictionary)doc.NamedObjects[key]; var table = (DxfDataTable)graph["TABLE"];
        Equal("Ω table \\U+0041 🧪", table.Name, "Exact table text"); Equal((short)2, table.StoredVersion, "Stored version");
        Equal(3, table.RowCount, "Row count"); Equal(11, table.Columns.Count, "All public stored types");
        Check(ReferenceEquals(graph["TABLE"], graph["ALIAS"]), "Typed table alias identity");
        Equal(0, ((DxfDataTable)graph["EMPTY"]).Columns.Count, "Empty table");
        Equal(7, ((DxfDataTable)graph["ZERO_COLUMNS"]).RowCount, "Rows survive zero columns");
        Equal(2, ((DxfDataTable)graph["ZERO_ROWS"]).Columns.Count, "Columns survive zero rows");
        Equal(int.MinValue, (int)table.Columns[0].Values[0], "Signed integer min"); Equal(int.MaxValue, (int)table.Columns[0].Values[2], "Signed integer max");
        Equal(-2.5, (double)table.Columns[1].Values[0], "Double"); Equal("青 🧪", (string)table.Columns[2].Values[1], "Supplementary Unicode"); Equal(@"Literal\U+0042", (string)table.Columns[2].Values[2], "Literal escape");
        Equal(new Vector3(1, 2, 3), (Vector3)table.Columns[3].Values[0], "Point Z retained"); Equal(new Vector3(-40, 50, -60), (Vector3)table.Columns[10].Values[1], "Vector Z retained");
        Check(!(bool)table.Columns[9].Values[0] && (bool)table.Columns[9].Values[1], "Boolean values");
        for (int i = 0; i < 11; i++) Equal(i + 1, (int)table.Columns[i].Type, "Exact stored column type");
        for (int c = 4; c <= 8; c++) foreach (object value in table.Columns[c].Values)
            if (value is DxfObject reference) Check(ReferenceEquals(doc.GetObjectByHandle(reference.Handle), reference), "Canonical object reference");
        foreach (int c in new[] { 5, 6 }) foreach (object value in table.Columns[c].Values)
            if (value is DxfObject reference) Check(ReferenceEquals(reference.Owner, table), "Reciprocal owner cell");
        Check(ReferenceEquals(table.PersistentReactors.Single(), graph), "Persistent reactor");
        Check(ReferenceEquals(table.ExtensionDictionary!.Owner, table), "Extension owner");
        Equal(((DxfObject)table.Columns[4].Values[0]).Handle, (string)table.XData["DATATABLE_APP"].XDataRecord[1].Value, "XData reference");
        Equal(0, doc.Objects.Validate().Count, "Valid table graph");
        return table;
    }
    private static void DataTableAuthored(DxfVersion version, bool binary)
    {
        var doc = DataTableDocument(version);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            DataTableAssert(doc);
            using var stream = new MemoryStream(); Check(doc.Save(stream, cycle == 1 ? !binary : binary), "DATATABLE save");
            if (cycle == 2) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"datatable-{version}-{binary}.dxf"), stream.ToArray());
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new Exception("DATATABLE reload failed.");
        }
        DataTableAssert(doc);
    }
    private static void DataTableCloneErase(DxfVersion version, bool binary)
    {
        var source = DataTableDocument(version); var graph = (DxfDictionary)source.NamedObjects["DATA_TABLES"];
        var target = new DxfDocument(version); for (int i = 0; i < 40; i++) target.Layers.Add(new Layer("Offset" + i));
        target.Objects.Clone(graph, target.NamedObjects, "DATA_TABLES"); var copy = DataTableAssert(target); var original = DataTableAssert(source);
        Check(copy.Handle != original.Handle, "Clone handle offset");
        Check(!ReferenceEquals(copy.Columns[5].Values[0], original.Columns[5].Values[0]), "Owned cells deeply cloned");
        var blocker = new DxfDataTable(); blocker.SetColumns(1, new[] { new DxfDataColumn(DxfDataCellType.HardPointer, "", new object[] { copy }) }); target.NamedObjects.Add("BLOCKER", blocker);
        DataTableReject(() => target.Objects.EraseOwnedTree((DxfDictionary)target.NamedObjects["DATA_TABLES"]));
        blocker.SetColumns(0, Array.Empty<DxfDataColumn>()); target.Objects.EraseOwnedTree(blocker);
        using var output = new MemoryStream(); Check(target.Save(output, binary), "Clone export");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"datatable-clone-{version}-{binary}.dxf"), output.ToArray());
        output.Position = 0; target = DxfDocument.Load(output) ?? throw new Exception("Clone reload failed."); copy = DataTableAssert(target);
        var child = (DxfDatabaseObject)copy.Columns[5].Values[0]; target.Objects.EraseOwnedTree((DxfDictionary)target.NamedObjects["DATA_TABLES"]);
        Check(copy.IsErased && child.IsErased && child.Database == null, "Ownership erasure cascade");
        DataTableReject(() => target.NamedObjects.Add("RESURRECT", copy));
        DataTableAssert(source);
        using var erased = new MemoryStream(); Check(target.Save(erased, binary), "Erased export"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"datatable-erased-{version}-{binary}.dxf"), erased.ToArray());
    }
    private static void DataTableReject(Action action)
    { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Expected atomic rejection"); }
    private static void DataTableAtomicity()
    {
        var child = new DxfXRecord(); var table = new DxfDataTable();
        table.SetColumns(1, new[] { new DxfDataColumn(DxfDataCellType.HardOwner, "", new object[] { child }) });
        var old = table.Columns;
        DataTableReject(() => table.SetColumns(2, old)); Check(ReferenceEquals(old, table.Columns) && ReferenceEquals(child.Owner, table), "Bad dimensions retain original state");
        DataTableReject(() => table.SetColumns(1, new[] { old[0], old[0] })); Check(ReferenceEquals(old, table.Columns), "Duplicate ownership retains columns");
        var other = new DxfDataTable(); DataTableReject(() => other.SetColumns(1, old)); Check(child.Owner == table && other.Columns.Count == 0, "Cannot transfer owned child");
        DataTableReject(() => table.SetColumns(1, new[] { new DxfDataColumn(DxfDataCellType.HardOwner, "", new object[] { table }) }));
        foreach (object value in new object[] { double.NaN, double.PositiveInfinity }) DataTableReject(() => new DxfDataColumn(DxfDataCellType.Double, "", new[] { value }));
        foreach (string value in new[] { "\ud800", "\udc00", "a\nb", "a\rb", "a\0b" }) DataTableReject(() => new DxfDataColumn(DxfDataCellType.String, "", new object[] { value }));
        DataTableReject(() => new DxfDataColumn(DxfDataCellType.Integer, "", new object[] { 1L }));
        DataTableReject(() => new DxfDataColumn(DxfDataCellType.Point, "", new object[] { new Vector3(1, 2, double.NaN) }));
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.NamedObjects.Add("T", table); string handle = child.Handle;
        DataTableReject(() => table.SetColumns(0, Array.Empty<DxfDataColumn>())); Check(ReferenceEquals(old, table.Columns) && child.Handle == handle, "Registered ownership removal is atomic");
        table.SetColumns(1, new[] { new DxfDataColumn(DxfDataCellType.SoftOwner, "Changed", new object[] { child }) }); Equal(DxfDataCellType.SoftOwner, table.Columns[0].Type, "Owner strength and name editable");
        var foreign = new DxfDocument(); var f = new DxfPlaceholder(); foreign.NamedObjects.Add("F", f);
        DataTableReject(() => table.SetColumns(1, new[] { table.Columns[0], new DxfDataColumn(DxfDataCellType.ObjectId, "", new object[] { f }) })); Equal(1, table.Columns.Count, "Foreign pointer rejection atomic");
        table = new DxfDataTable(); table.SetColumns(1, new[] { new DxfDataColumn(DxfDataCellType.HardOwner, "", new object[] { new DxfXRecord() }) }); var released = (DxfObject)table.Columns[0].Values[0]; table.SetColumns(0, Array.Empty<DxfDataColumn>()); Check(released.Owner == null, "Detached replacement releases previous owner");
    }
    private static void DataTableExternalMap()
    {
        var source = new DxfDocument(DxfVersion.AutoCad2018); var line = new Line(Vector3.Zero, Vector3.UnitX); source.Entities.Add(line);
        var table = new DxfDataTable(); table.SetColumns(2, new[] {
            new DxfDataColumn(DxfDataCellType.ObjectId, "", new object[] { line, null! }),
            new DxfDataColumn(DxfDataCellType.HardPointer, "", new object[] { null!, line }),
            new DxfDataColumn(DxfDataCellType.SoftPointer, "", new object[] { line, line }) }); source.NamedObjects.Add("SOURCE", table);
        var target = new DxfDocument(DxfVersion.AutoCad2018); var replacement = new Line(Vector3.UnitY, Vector3.UnitZ); target.Entities.Add(replacement);
        int count = target.Objects.Items.Count; long seed = (long)typeof(DxfDocument).GetProperty("NumHandles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(target)!;
        DataTableReject(() => target.Objects.CloneObject(table, target.NamedObjects, "BAD"));
        Equal(count, target.Objects.Items.Count, "Failed external mapping leaves object inventory"); Equal(seed, (long)typeof(DxfDocument).GetProperty("NumHandles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(target)!, "Failed external mapping leaves seed"); Check(!target.NamedObjects.Contains("BAD"), "No partial clone name");
        var copy = (DxfDataTable)target.Objects.CloneObject(table, target.NamedObjects, "COPY", new Dictionary<DxfObject, DxfObject> { [line] = replacement });
        foreach (DxfObject reference in copy.Columns.SelectMany(c => c.Values).OfType<DxfObject>()) Check(ReferenceEquals(reference, replacement), "All pointer strengths map exact external identity");
        Check(copy.Columns[0].Values[1] == null && copy.Columns[1].Values[0] == null, "Null cells survive map");
        Check(!ReferenceEquals(line.Owner, table) && !ReferenceEquals(replacement.Owner, copy), "Pointer cells never acquire graphical ownership");
        target.Objects.EraseOwnedTree(copy); Check(target.Entities.Lines.Single() == replacement, "External mapped entity survives table erasure");
        var erased = new DxfPlaceholder(); target.NamedObjects.Add("ERASE", erased); target.Objects.EraseOwnedTree(erased);
        DataTableReject(() => new DxfDataColumn(DxfDataCellType.ObjectId, "", new object[] { erased }));
    }
    private static void DataTableReentrancy()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); var table = new DxfDataTable(); doc.NamedObjects.Add("T", table);
        IEnumerable<DxfDataColumn> ErasingEnumerator()
        { yield return new DxfDataColumn(DxfDataCellType.Integer, "", new object[] { 3 }); doc.Objects.EraseOwnedTree(table); }
        DataTableReject(() => table.SetColumns(1, ErasingEnumerator())); Check(table.IsErased && table.Columns.Count == 0, "Enumerator erasure rechecked before replacement");
        var owner = new DxfDataTable(); var child = new DxfXRecord();
        IEnumerable<DxfDataColumn> ForeignOwnerEnumerator()
        { yield return new DxfDataColumn(DxfDataCellType.HardOwner, "", new object[] { child }); var dictionary = new DxfDictionary(); dictionary.Add("NOW_OWNED", child); }
        DataTableReject(() => owner.SetColumns(1, ForeignOwnerEnumerator())); Check(owner.Columns.Count == 0 && child.Owner is DxfDictionary, "Enumerator owner change rechecked before adoption");
    }
    private static void DataTableSourceIdentity(DxfVersion version, bool binary, string decoy)
    {
        var raw = DataTableRaw(version, binary); string seed = (string)raw.Tags[raw.Tags.ToList().FindIndex(t => t.Code == 9 && (string)t.Value == "$HANDSEED") + 1].Value;
        if (decoy == "table-payload-identity" || decoy == "raster-payload-identity") seed = "F00";
        if (decoy == "unrelated-managed-dictionary") seed = (Convert.ToUInt64(seed, 16) + 2).ToString("X");
        var original = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11));
        string originalHandle = (string)original.Tags.Single(t => t.Code == 5).Value;
        var source = original.Tags.ToList(); int slot = source.FindIndex(t => t.Code == 331); source[slot] = new DxfTag(331, seed); raw = raw.WithRecord(original, source);
        if (decoy == "class")
        {
            var definition = raw.Sections.Single(s => s.Name == "CLASSES").Records.Single(r => r.Name == "CLASS" && r.Tags.Any(t => t.Code == 1 && (string)t.Value == "DATATABLE"));
            raw = raw.WithRecord(definition, definition.Tags.Concat(new[] { new DxfTag(5, seed) }));
        }
        else if (decoy == "payload" || decoy == "control")
        {
            var other = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 0));
            var tags = other.Tags.ToList(); int at = tags.FindIndex(t => t.Code == 100);
            if (decoy == "payload") tags.Add(new DxfTag(5, seed));
            else tags.InsertRange(at, new[] { new DxfTag(102, "{PRIVATE_ID_DECOY"), new DxfTag(5, seed), new DxfTag(102, "}") });
            raw = raw.WithRecord(other, tags);
        }
        else if (decoy == "unrelated-managed-dictionary")
        {
            var root = raw.Sections.Single(section => section.Name == "OBJECTS").Records.First(record => record.Name == "DICTIONARY");
            var tags = root.Tags.ToList(); int entry = tags.FindIndex(tag => tag.Code == 3 && (string)tag.Value == "ACAD_GROUP");
            string group = (string)tags[entry + 1].Value;
            tags[entry] = new DxfTag(3, "UNRELATED_GROUP_DICTIONARY"); tags[entry + 1] = new DxfTag(tags[entry + 1].Code, seed);
            raw = raw.WithRecord(root, tags);
            var dictionary = raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record => record.Name == "DICTIONARY" && record.Tags.Any(tag => tag.Code == 5 && (string)tag.Value == group));
            raw = raw.WithRecord(dictionary, dictionary.Tags.Select(tag => tag.Code == 5 ? new DxfTag(5, seed) : tag));
        }
        else if (decoy == "unknown-entity" || decoy == "discarded-entity" || decoy == "dictionary-entity" || decoy == "ignored-section" || decoy == "missing-table-identity" || decoy == "missing-raster-identity" || decoy == "table-payload-identity" || decoy == "raster-payload-identity")
        {
            string kind = decoy == "unknown-entity" || decoy.StartsWith("missing-", StringComparison.Ordinal) || decoy.EndsWith("-payload-identity", StringComparison.Ordinal) ? "FUTURE_ENTITY" : decoy == "discarded-entity" ? "PDFUNDERLAY" : "DICTIONARY";
            var packet = new List<DxfTag> { new DxfTag(0, kind), new DxfTag(5, seed), new DxfTag(100, "AcDbEntity"), new DxfTag(8, "0"), new DxfTag(100, kind == "PDFUNDERLAY" ? "AcDbUnderlayReference" : kind == "DICTIONARY" ? "AcDbDictionary" : "FutureEntity") };
            if (kind == "PDFUNDERLAY") packet.AddRange(new[] { new DxfTag(340, "0"), new DxfTag(10, 0.0), new DxfTag(20, 0.0), new DxfTag(30, 0.0) });
            if (decoy == "ignored-section")
            {
                packet.InsertRange(0, new[] { new DxfTag(0, "SECTION"), new DxfTag(2, "FUTURE_SECTION") });
                packet.Add(new DxfTag(0, "ENDSEC"));
                int eof = raw.Tags.Count - 1; raw = raw.WithTags(raw.Tags.Take(eof).Concat(packet).Concat(raw.Tags.Skip(eof)));
            }
            else
            {
                if (decoy == "missing-table-identity" || decoy == "table-payload-identity")
                {
                    var layerTable = raw.Sections.Single(section => section.Name == "TABLES").Records.Single(record => record.Name == "TABLE" && record.Tags.Any(tag => tag.Code == 2 && (string)tag.Value == "LAYER"));
                    var tableTags = layerTable.Tags.Where(tag => tag.Code != 5).ToList();
                    if (decoy == "table-payload-identity") tableTags.Add(new DxfTag(5, seed));
                    raw = raw.WithRecord(layerTable, tableTags);
                }
                else if (decoy == "missing-raster-identity" || decoy == "raster-payload-identity")
                {
                    int objectEnd = raw.Sections.Single(section => section.Name == "OBJECTS").EndTagIndex - 1;
                    var raster = new List<DxfTag> { new DxfTag(0, "RASTERVARIABLES"), new DxfTag(100, "AcDbRasterVariables"), new DxfTag(90, 0), new DxfTag(70, (short)1), new DxfTag(71, (short)1), new DxfTag(72, (short)0) };
                    if (decoy == "raster-payload-identity") raster.Add(new DxfTag(5, seed));
                    raw = raw.WithTags(raw.Tags.Take(objectEnd).Concat(raster).Concat(raw.Tags.Skip(objectEnd)));
                }
                int end = raw.Sections.Single(section => section.Name == "ENTITIES").EndTagIndex - 1;
                raw = raw.WithTags(raw.Tags.Take(end).Concat(packet).Concat(raw.Tags.Skip(end)));
            }
        }
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0; bool rejected = false;
        try { rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "Missing or discarded source target must not alias a synthesized runtime collection: " + seed + "/" + decoy);
    }
    private static void DataTableCanonicalIdentity(DxfVersion version, bool binary)
    {
        var raw = DataTableRaw(version, binary); var records = raw.Sections.Single(s => s.Name == "OBJECTS").Records;
        var table = records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11));
        string pointer = (string)table.Tags.First(t => t.Code == 331).Value;
        var target = records.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == pointer));
        var moved = target.Tags.Select(t => t.Code == 5 ? new DxfTag(5, "000" + pointer.ToLowerInvariant()) : t).ToArray();
        raw = raw.WithoutRecord(target); int end = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1;
        raw = raw.WithTags(raw.Tags.Take(end).Concat(moved).Concat(raw.Tags.Skip(end)));
        table = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11));
        raw = raw.WithRecord(table, table.Tags.Select(t => (t.Code == 331 || t.Code == 340 || t.Code == 330) && (string)t.Value == pointer ? new DxfTag(t.Code, "00" + pointer.ToLowerInvariant()) : t));
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Numeric canonical forward reference rejected.");
        var loaded = DataTableAssert(doc); Check(ReferenceEquals(loaded.Columns[4].Values[0], doc.GetObjectByHandle(pointer)), "Forward target identity canonicalized");
    }
    private static void DataTableSymbolTableIdentity(DxfVersion version, bool binary)
    {
        var raw = DataTableRaw(version, binary);
        var symbols = raw.Sections.Single(s => s.Name == "TABLES").Records;
        string collection = (string)symbols.Single(r => r.Name == "TABLE" && r.Tags.Any(t => t.Code == 2 && (string)t.Value == "DIMSTYLE")).Tags.Single(t => t.Code == 5).Value;
        string style = (string)symbols.First(r => r.Name == "DIMSTYLE").Tags.Single(t => t.Code == 105).Value;
        var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11));
        int index = 0; var references = new[] { collection, style, "0" };
        raw = raw.WithRecord(record, record.Tags.Select(t => t.Code == 331 ? new DxfTag(331, references[index++]) : t));
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Real symbol table source identities rejected.");
        var table = (DxfDataTable)((DxfDictionary)doc.NamedObjects["DATA_TABLES"])["TABLE"];
        Check(ReferenceEquals(table.Columns[4].Values[0], doc.DimensionStyles), "Actual TABLE collection identity admitted");
        Check(ReferenceEquals(table.Columns[4].Values[1], doc.GetObjectByHandle(style)), "Actual DIMSTYLE group105 identity admitted");
        Check(table.Columns[4].Values[2] == null, "Source handle0 remains null");
        Equal(0, doc.Objects.Validate().Count, "Source symbol references validate");
    }
    private static void DataTableRetainedIdentities(DxfVersion version, bool binary)
    {
        var seedDocument = DataTableDocument(version); using var seed = new MemoryStream(); Check(seedDocument.Save(seed, binary), "Canonical source seed"); seed.Position = 0;
        var doc = DxfDocument.Load(seed) ?? throw new Exception("Canonical source seed rejected."); var line = doc.Entities.Lines.Single();
        var group = new Group("SOURCE_IDENTITY_GROUP"); group.Entities.Add(line); doc.Groups.Add(group);
        var block = new netDxf.Blocks.Block("SOURCE_ATTRIBUTES"); var definition = new AttributeDefinition("SOURCE_TAG"); block.AttributeDefinitions.Add(definition);
        var insert = new Insert(block); doc.Entities.Add(insert);
        DxfObject[] targets = { doc.NamedObjects, line, line.Owner, line.Owner.Record, line.Layer, line.Linetype,
            doc.Layers, doc.Linetypes, doc.Blocks, doc.ApplicationRegistries, doc.ApplicationRegistries["DATATABLE_APP"],
            doc.DimensionStyles, doc.DimensionStyles.First(), doc.TextStyles, doc.TextStyles.First(),
            doc.Groups, group, doc.Layouts, doc.Layouts.First(), doc.MlineStyles, doc.MlineStyles.First(),
            block, block.Record, definition, insert };
        foreach (var target in targets) Check(target != null && ReferenceEquals(doc.GetObjectByHandle(target.Handle), target), "Positive fixture target must already be registered: " + target?.GetType().Name + "/" + target?.Handle);
        var references = new DxfDataTable(); references.SetColumns(targets.Length, new[] { new DxfDataColumn(DxfDataCellType.ObjectId, "Retained identities", targets) });
        doc.NamedObjects.Add("SOURCE_REFERENCES", references);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Retained resources seed"); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new Exception("Retained source resources rejected.");
        var actual = (DxfDataTable)loaded.NamedObjects["SOURCE_REFERENCES"];
        for (int i = 0; i < targets.Length; i++)
        {
            DxfObject target = (DxfObject)actual.Columns[0].Values[i];
            Equal(targets[i].Handle, target.Handle, "Source identity retained: " + targets[i].CodeName);
            Equal(targets[i].GetType(), target.GetType(), "Source object type retained");
            Check(ReferenceEquals(loaded.GetObjectByHandle(target.Handle), target), "Retained resource is canonical");
        }
        var loadedInsert = loaded.Entities.Inserts.Single();
        Equal(1, loadedInsert.Attributes.Count, "Nested ATTRIB retained while resolving the enclosing INSERT");
        Equal("SOURCE_TAG", loadedInsert.Attributes.Single().Tag, "Nested attribute data retained");
        Equal(0, loaded.Objects.Validate().Count, "Retained source resources validate");
    }
    private static void DataTableSectionContext(DxfVersion version, bool binary)
    {
        var raw = DataTableRaw(version, binary); var entities = raw.Sections.Single(section => section.Name == "ENTITIES");
        var line = entities.Records.Single(record => record.Name == "LINE"); string handle = (string)line.Tags.Single(tag => tag.Code == 5).Value;
        int at = line.StartTagIndex;
        // SECTION is now a typed entity: use its actual public packet while checking
        // that its name never resets the observer's surrounding ENTITIES context.
        var sectionPacket = new[] {
            new DxfTag(0, "SECTION"), new DxfTag(5, "F10"), new DxfTag(100, "AcDbEntity"), new DxfTag(8, "0"),
            new DxfTag(100, "AcDbSection"), new DxfTag(90, 1), new DxfTag(91, 0), new DxfTag(1, "Source context"),
            new DxfTag(10, 0.0), new DxfTag(20, 0.0), new DxfTag(30, 1.0), new DxfTag(40, 1.0), new DxfTag(41, 0.0),
            new DxfTag(70, (short)0), new DxfTag(92, 2),
            new DxfTag(11, 0.0), new DxfTag(21, 0.0), new DxfTag(31, 0.0),
            new DxfTag(11, 1.0), new DxfTag(21, 0.0), new DxfTag(31, 0.0), new DxfTag(93, 0), new DxfTag(360, "0") };
        raw = raw.WithTags(raw.Tags.Take(at).Concat(sectionPacket).Concat(raw.Tags.Skip(at)));
        var record = raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(item => item.Name == "DATATABLE" && item.Tags.Any(tag => tag.Code == 90 && (int)tag.Value == 11));
        bool first = true; raw = raw.WithRecord(record, record.Tags.Select(tag => tag.Code == 331 && first ? (first = false, new DxfTag(331, handle)).Item2 : tag));
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        if (version < DxfVersion.AutoCad2007)
        {
            bool rejected = false;
            try { rejected = DxfDocument.Load(input) == null; } catch (NotSupportedException) { rejected = true; }
            Check(rejected, "A typed SECTION packet requires R2007 or later");
            return;
        }
        var loaded = DxfDocument.Load(input) ?? throw new Exception("SECTION entity erased following entity source context.");
        Equal(1, loaded.Entities.All.OfType<netDxf.Entities.Section>().Count(), "The SECTION context control must be a retained typed entity");
        var table = (DxfDataTable)((DxfDictionary)loaded.NamedObjects["DATA_TABLES"])["TABLE"];
        Check(ReferenceEquals(table.Columns[4].Values[0], loaded.Entities.Lines.Single()), "Following LINE retains its same-record source identity");
        Equal(0, loaded.Objects.Validate().Count, "Typed SECTION does not alter following reference resolution");
    }
    private static DxfRawDocument DataTableRaw(DxfVersion version, bool binary)
    { using var output = new MemoryStream(); Check(DataTableDocument(version).Save(output, binary), "Malformed seed"); output.Position = 0; return DxfRawDocument.Load(output); }
    private static void DataTableMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = DataTableRaw(version, binary); var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11)); var tags = record.Tags.ToList();
        int Find(short code) => tags.FindIndex(t => t.Code == code);
        int body = Find(100); int firstType = Find(92);
        switch (defect)
        {
            case "missing-version": tags.RemoveAt(Find(70)); break;
            case "duplicate-version": tags.Insert(Find(70), new DxfTag(70, (short)2)); break;
            case "missing-columns": tags.RemoveAt(Find(90)); break;
            case "negative-columns": tags[Find(90)] = new DxfTag(90, -1); break;
            case "huge-columns": tags[Find(90)] = new DxfTag(90, int.MaxValue); break;
            case "negative-rows": tags[Find(91)] = new DxfTag(91, -1); break;
            case "huge-rows": tags[Find(91)] = new DxfTag(91, int.MaxValue); break;
            case "missing-name": tags.RemoveAt(Find(1)); break;
            case "missing-column-name": tags.RemoveAt(Find(2)); break;
            case "wrong-type-code": tags[Find(93)] = new DxfTag(40, 1.0); break;
            case "missing-value": tags.RemoveAt(Find(93)); break;
            case "extra-value": tags.Insert(Find(93), new DxfTag(93, 1)); break;
            case "duplicate-marker": tags.Insert(body, new DxfTag(100, "AcDbDataTable")); break;
            case "bool-range": tags[Find(71)] = new DxfTag(71, (short)2); break;
            case "point-missing-z": tags.RemoveAt(Find(30)); break;
            case "vector-order": tags[Find(21)] = new DxfTag(31, 20.0); break;
            case "unresolved-reference": tags[Find(331)] = new DxfTag(331, "FFFFFFFF"); break;
            case "wrong-owner": tags[tags.FindIndex(body, t => t.Code == 360)] = new DxfTag(360, (string)tags[Find(331)].Value); break;
            case "duplicate-owner": int hard = tags.FindIndex(body, t => t.Code == 360); tags[hard + 1] = tags[hard]; break;
            case "foreign-owner":
                var child = raw.Sections.Single(s => s.Name == "OBJECTS").Records.First(r => r.Name == "XRECORD");
                raw = raw.WithRecord(child, child.Tags.Select(t => t.Code == 330 ? new DxfTag(330, (string)tags[Find(330)].Value) : t));
                string tableHandle = (string)tags[Find(5)].Value;
                record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == tableHandle)); break;
            case "nonfinite":
                using (var wire = new MemoryStream())
                {
                    raw.Save(wire); byte[] bytes = wire.ToArray();
                    if (binary)
                    {
                        byte[] pattern = BitConverter.GetBytes(-2.5); int position = bytes.AsSpan().IndexOf(pattern); Check(position >= 0, "Nonfinite wire injection target");
                        BitConverter.GetBytes(double.PositiveInfinity).CopyTo(bytes, position);
                    }
                    else
                    {
                        string text = System.Text.Encoding.UTF8.GetString(bytes); Check(text.Contains("-2.5"), "Nonfinite text injection target");
                        bytes = System.Text.Encoding.UTF8.GetBytes(text.Replace("-2.5", "Infinity"));
                    }
                    using var malformed = new MemoryStream(bytes); bool failed = false;
                    try { failed = DxfDocument.Load(malformed) == null; } catch (FormatException) { failed = true; } catch (ArgumentException) { failed = true; } catch (InvalidDataException) { failed = true; }
                    Check(failed, "Nonfinite raw cell rejected");
                }
                return;
            case "surrogate": tags[Find(3)] = new DxfTag(3, @"\U+D800"); break;
        }
        raw = raw.WithRecord(record, tags);
        using var input = new MemoryStream(); raw.Save(input); input.Position = 0;
        bool rejected = false; try { rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; } catch (ArgumentException) { rejected = true; } Check(rejected, "Malformed public DATATABLE rejected: " + defect);
    }
    private static void DataTableOpaque(DxfVersion version, bool binary, string variant)
    {
        var raw = DataTableRaw(version, binary); var record = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 90 && (int)t.Value == 11)); var tags = record.Tags.ToList();
        int body = tags.FindIndex(t => t.Code == 100), terminal = tags.FindIndex(t => t.Code == 1001);
        if (variant == "version") tags[body + 1] = new DxfTag(70, (short)99);
        else if (variant == "type") tags[tags.FindIndex(t => t.Code == 92)] = new DxfTag(92, 77);
        else if (variant == "field") tags.Insert(terminal, new DxfTag(309, "Private"));
        else if (variant == "subclass") tags.Insert(terminal, new DxfTag(100, "PrivateDataTable"));
        else tags.Insert(body, new DxfTag(309, "Private header"));
        raw = raw.WithRecord(record, tags); using var input = new MemoryStream(); raw.Save(input); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Opaque table rejected.");
        var table = doc.GetObjectByHandle((string)record.Tags.Single(t => t.Code == 5).Value) as DxfOpaqueObject; Check(table != null, "Private table remains wholly opaque");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Opaque output"); output.Position = 0; var after = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == table!.Handle));
        Check(OwnershipTagValues(tags.Skip(body)).SequenceEqual(OwnershipTagValues(after.Tags.Skip(after.Tags.ToList().FindIndex(t => t.Code == (variant == "header" ? 309 : 100))))), "Complete private payload retained");
    }
    private static void DataTableOldProfile(bool binary)
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2000); doc.NamedObjects.Add("TABLE", new DxfDataTable());
        using var stream = new MemoryStream(); bool rejected = false; try { rejected = !doc.Save(stream, binary); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "R2000 typed table export rejected");
        var modern = new DxfDocument(DxfVersion.AutoCad2004); modern.NamedObjects.Add("TABLE", new DxfDataTable());
        using var original = new MemoryStream(); Check(modern.Save(original, false), "Older opaque seed"); original.Position = 0;
        var raw = DxfRawDocument.Load(original); var tags = raw.Tags.ToList(); int slot = tags.FindIndex(t => t.Code == 9 && (string)t.Value == "$ACADVER") + 1; tags[slot] = new DxfTag(1, "AC1015");
        using var input = new MemoryStream();
        using (var writer = new StreamWriter(input, new System.Text.UTF8Encoding(false), 1024, true))
            foreach (var tag in tags) { writer.WriteLine(tag.Code); writer.WriteLine(tag.Value is bool flag ? (flag ? "1" : "0") : Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture)); }
        input.Position = 0; var loaded = DxfDocument.Load(input) ?? throw new Exception("Older opaque table load failed."); Check(loaded.NamedObjects["TABLE"] is DxfOpaqueObject, "Older input remains opaque");
        using var saved = new MemoryStream(); Check(loaded.Save(saved, binary), "Older opaque output");
    }
    private static void DataTableNative(bool binary)
    {
        const string file = "sample_AC1018_ascii.dxf";
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", file + ".gz")); using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var original = new MemoryStream(); gzip.CopyTo(original);
        using var inventory = JsonDocument.Parse(File.ReadAllText(Path.Combine("tools", "table_oracle", "fixtures.json")));
        string hash = inventory.RootElement.GetProperty("files").EnumerateArray().Single(v => v.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(original.ToArray())).ToLowerInvariant(), "Pinned native source hash");
        original.Position = 0; var source = DxfRawDocument.Load(original); var sourceObjects = source.Sections.Single(s => s.Name == "OBJECTS").Records;
        foreach (string handle in new[] { "143D", "145B" })
        {
            var table = sourceObjects.Single(r => r.Name == "DATATABLE" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == handle));
            var children = sourceObjects.Where(r => r.Name == "XRECORD" && r.Tags.Any(t => t.Code == 330 && (string)t.Value == handle)).ToArray();
            int rows = handle == "143D" ? 21 : 20; Equal(rows, children.Length, "Native owned XRecord inventory");
            var setup = new DxfDocument(DxfVersion.AutoCad2004); string root = setup.NamedObjects.Handle; using var seed = new MemoryStream(); Check(setup.Save(seed, binary), "Native extraction seed"); seed.Position = 0;
            var raw = DxfRawDocument.Load(seed); var dictionary = raw.Sections.Single(s => s.Name == "OBJECTS").Records.Single(r => r.Name == "DICTIONARY" && r.Tags.Any(t => t.Code == 5 && (string)t.Value == root));
            raw = raw.WithRecord(dictionary, dictionary.Tags.Concat(new[] { new DxfTag(3, "NATIVE_DATA_TABLE"), new DxfTag(360, handle) }));
            int boundary = raw.Sections.Single(s => s.Name == "OBJECTS").EndTagIndex - 1;
            raw = raw.WithTags(raw.Tags.Take(boundary).Concat(table.Tags.Select(t => t.Code == 330 ? new DxfTag(330, root) : t)).Concat(children.SelectMany(r => r.Tags)).Concat(raw.Tags.Skip(boundary)));
            using var input = new MemoryStream(); raw.Save(input); input.Position = 0; var doc = DxfDocument.Load(input) ?? throw new Exception("Native DATATABLE extraction load failed.");
            var typed = (DxfDataTable)doc.GetObjectByHandle(handle); Equal(rows, typed.RowCount, "Native row count"); Check(typed.Columns.Select(c => (int)c.Type).SequenceEqual(new[] { 2, 1, 1, 6 }), "Native stored column types");
            foreach (DxfObject child in typed.Columns[3].Values) Check(child.Owner == typed, "Native reciprocal cell ownership"); Equal(0, doc.Objects.Validate().Count, "Native graph validates");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Native output"); File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"datatable-native-{handle}-{binary}.dxf"), output.ToArray()); output.Position = 0;
            var after = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "OBJECTS").Records;
            foreach (var expected in children.Append(table))
            {
                string childHandle = (string)expected.Tags.Single(t => t.Code == 5).Value; var actual = after.Single(r => r.Tags.Any(t => t.Code == 5 && (string)t.Value == childHandle));
                var expectedTags = expected == table ? expected.Tags.Select(t => t.Code == 330 ? new DxfTag(330, root) : t) : expected.Tags;
                Check(OwnershipTagValues(expectedTags).SequenceEqual(OwnershipTagValues(actual.Tags)), "Native complete record body retained: " + childHandle);
            }
            var clone = new DxfDocument(DxfVersion.AutoCad2004); for (int i = 0; i < 40; i++) clone.Layers.Add(new Layer("NativeOffset" + i)); var copy = (DxfDataTable)clone.Objects.CloneObject(typed, clone.NamedObjects, "NATIVE_COPY");
            Check(copy.Handle != typed.Handle && copy.Columns[3].Values.Cast<DxfObject>().All(c => c.Owner == copy), "Native owned graph clone");
            clone.Objects.EraseOwnedTree(copy); Check(copy.IsErased && copy.Columns[3].Values.Cast<DxfDatabaseObject>().All(c => c.IsErased), "Native owned graph erasure");
        }
    }
}
