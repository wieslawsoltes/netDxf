using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
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
    private static void RegisterFifthMixedModuleTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"fifth-mixed/ownership-clone-erase/{version}/{binary}", () => FifthMixedGraph(version, binary));
    }

    private sealed record FifthNativePacket(string File, string Sha256, string EntityHandle, short PointerCode, string PointerHandle, List<DxfTag> Payload);
    private static readonly string[] FifthIndexNames = { "Unresolved layer", "Unresolved layer", "unresolved layer" };

    private static void FifthMixedGraph(DxfVersion version, bool binary)
    {
        FifthNativePacket? native = FifthNative(version);
        var source = FifthBuild(version, binary, native);
        var graph = (DxfDictionary)source.Objects.Root["FIFTH_GRAPH"];
        var shared = (DxfXRecord)graph["SHARED"];
        var external = (DxfXRecord)source.Objects.Root["FIFTH_EXTERNAL"];
        var line = source.Entities.Lines.Single();
        var index = (DxfLayerIndex)graph["INDEX"];
        var links = (DxfXRecord)graph["LINKS"];
        links.Data.Add(new DxfTag(340, "000" + shared.Handle.ToLowerInvariant())); links.Data.Add(new DxfTag(330, external.Handle));
        links.Data.Add(new DxfTag(320, shared.Handle)); links.Data.Add(new DxfTag(329, index.Entries[0].Buffer.Handle));
        external.Data.Add(new DxfTag(320, shared.Handle)); external.Data.Add(new DxfTag(329, index.Entries[0].Buffer.Handle));
        var data = new XData(new ApplicationRegistry("FIFTH_APP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, shared.Handle));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, external.Handle));
        data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 255, 7 }));
        graph["POINTER"].XData.Add(data);
        index.Entries[1].Buffer.XData.Add((XData)data.Clone());
        if (graph.Contains("DATA"))
        {
            var table = (DxfDataTable)graph["DATA"];
            ((DxfXRecord)table.Columns[2].Values[0]).Data.Add(new DxfTag(340, shared.Handle));
        }
        string? storedTableHandle = null;
        if (native != null)
        {
            var table = source.Entities.StoredTables.Single(); storedTableHandle = table.Handle;
            Check(FifthTagValues(native.Payload).SequenceEqual(FifthTagValues(table.Payload)), "native TABLE payload changed on extraction");
            Check(table.References.Contains(shared), "native TABLE pointer did not bind numerically equivalent shared identity");
            Equal(FifthNumeric(native.PointerHandle), FifthNumeric(shared.Handle), "native pointer identity mapping");
            table.ColorName = "Book$Ω"; table.Layer = new Layer("FIFTH_TABLE_LAYER");
            var metadata = new XData(new ApplicationRegistry("FIFTH_TABLE_APP")); metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "allowed common metadata edit"));
            table.XData.Add(metadata); source.ApplicationRegistries["FIFTH_TABLE_APP"].Name = "FIFTH_TABLE_EDITED";
            Check(FifthTagValues(native.Payload).SequenceEqual(FifthTagValues(table.Payload)), "common metadata edit changed native subclass tags");
            Throws<NotSupportedException>(() => table.Clone());
            var otherVersion = version == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018;
            source.DrawingVariables.AcadVer = otherVersion;
            using var invalid = new MemoryStream(); bool failed;
            try { failed = !source.Save(invalid, binary); } catch (NotSupportedException) { failed = true; }
            Check(failed && invalid.Length == 0, "stored TABLE profile conversion wrote output"); source.DrawingVariables.AcadVer = version;
        }
        source = FifthSave(source, version, binary, "source");
        graph = (DxfDictionary)source.Objects.Root["FIFTH_GRAPH"]; external = (DxfXRecord)source.Objects.Root["FIFTH_EXTERNAL"]; line = source.Entities.Lines.Single();
        shared = (DxfXRecord)graph["SHARED"]; index = (DxfLayerIndex)graph["INDEX"];
        string[] arbitrary = ((DxfXRecord)graph["LINKS"]).Data.Where(t => t.Code is 320 or 329).Select(t => (string)t.Value).ToArray();
        FifthAssert(source, graph, external, line, arbitrary, "FIFTH_APP");
        if (native != null)
        {
            var table = source.Entities.StoredTables.Single();
            Check(FifthTagValues(native.Payload).SequenceEqual(FifthTagValues(table.Payload)), "saved native TABLE payload differs");
            Equal("Book$Ω", table.ColorName, "TABLE common color edit"); Equal("FIFTH_TABLE_LAYER", table.Layer.Name, "TABLE common layer edit");
            Equal("allowed common metadata edit", (string)table.XData["FIFTH_TABLE_EDITED"].XDataRecord.Single().Value, "TABLE common XData edit");
        }

        var destination = new DxfDocument(version); for (int i = 0; i < 80; i++) destination.Layers.Add(new Layer("FIFTH_PADDING_" + i));
        var mappedExternal = new DxfXRecord(); mappedExternal.Data.Add(new DxfTag(1, "external survivor")); destination.Objects.Root.Add("FIFTH_EXTERNAL", mappedExternal);
        var mappedLine = (Line)line.Clone(); destination.Entities.Add(mappedLine);
        if (native != null)
        {
            long before = FifthSeed(destination); int entities = destination.Entities.All.Count(); bool rejected = false;
            try { destination.Entities.Add(source.Entities.StoredTables.Single()); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "foreign stored TABLE adoption was accepted");
            Equal(entities, destination.Entities.All.Count(), "foreign stored TABLE adoption changed entity collection"); Equal(before, FifthSeed(destination), "foreign TABLE adoption allocated handles");
        }
        long failedCloneSeed = FifthSeed(destination);
        Throws<InvalidOperationException>(() => destination.Objects.Clone(graph, destination.Objects.Root, "FIFTH_GRAPH"));
        Equal(failedCloneSeed, FifthSeed(destination), "unmapped mixed clone allocated handles"); Check(!destination.Objects.Root.Contains("FIFTH_GRAPH"), "unmapped clone attached graph");
        var mappings = new Dictionary<DxfObject, DxfObject>(ReferenceEqualityComparer.Instance) { [external] = mappedExternal, [line] = mappedLine };
        var copied = destination.Objects.Clone(graph, destination.Objects.Root, "FIFTH_GRAPH", mappings);
        var sourcePaths = FifthPaths(graph); var copiedPaths = FifthPaths(copied);
        Equal(sourcePaths.Count, copiedPaths.Count, "mixed declared-owned clone size");
        var pairs = sourcePaths.Select(pair =>
        {
            var copy = copiedPaths[pair.Key]; Check(!ReferenceEquals(pair.Value, copy), "mixed clone retained object identity");
            Check(FifthNumeric(pair.Value.Handle) != FifthNumeric(copy.Handle), "mixed clone reused numeric handle");
            return new { path = pair.Key, type = pair.Value.CodeName, source = pair.Value.Handle, destination = copy.Handle };
        }).ToArray();
        var copyIndex = (DxfLayerIndex)copied["INDEX"];
        copyIndex.SetEntries(copyIndex.Entries.Reverse()); copyIndex.SetEntries(copyIndex.Entries.Reverse());
        ((DxfLayerFilter)copied["FILTER"]).LayerNames[0] = "clone mutation";
        Equal("Unresolved layer", ((DxfLayerFilter)graph["FILTER"]).LayerNames[0], "mixed filter copy shares names");
        ((DxfLayerFilter)copied["FILTER"]).LayerNames[0] = "Unresolved layer";
        if (copied.Contains("DATA"))
        {
            var table = (DxfDataTable)copied["DATA"]; var original = (DxfDataTable)graph["DATA"];
            table.Name = "copy mutation"; Equal("Mixed Ω data", original.Name, "DATATABLE clone name shares state"); table.Name = original.Name;
            var columns = table.Columns;
            Throws<ArgumentException>(() => table.SetColumns(3, columns.Select((column, i) => i == 2 ? new DxfDataColumn(DxfDataCellType.HardOwner, column.Name, new object[] { copyIndex.Entries[0].Buffer, null!, null! }) : column)));
            Check(ReferenceEquals(columns, table.Columns), "failed cross-owner adoption changed DATATABLE snapshot");
            Check(ReferenceEquals(copyIndex.Entries[0].Buffer.Owner, copyIndex), "DATATABLE stole the index's IDBUFFER");
        }
        destination.ApplicationRegistries["FIFTH_APP"].Name = "FIFTH_APP_CLONE";
        Check(graph["POINTER"].XData.AppIds.Contains("FIFTH_APP"), "clone APPID rename changed source");
        mappedExternal.Data.Add(new DxfTag(320, copied["SHARED"].Handle)); mappedExternal.Data.Add(new DxfTag(329, copyIndex.Entries[0].Buffer.Handle));
        string[] copyArbitrary = mappedExternal.Data.Where(t => t.Code is 320 or 329).Select(t => (string)t.Value).ToArray();
        FifthAssert(destination, copied, mappedExternal, mappedLine, arbitrary, "FIFTH_APP_CLONE");
        destination = FifthSave(destination, version, binary, "copy");
        copied = (DxfDictionary)destination.Objects.Root["FIFTH_GRAPH"]; mappedExternal = (DxfXRecord)destination.Objects.Root["FIFTH_EXTERNAL"]; mappedLine = destination.Entities.Lines.Single();
        FifthAssert(destination, copied, mappedExternal, mappedLine, arbitrary, "FIFTH_APP_CLONE");
        var blocker = new DxfXRecord(); destination.Objects.Root.Add("FIFTH_BLOCKER", blocker);
        blocker.Data.Add(new DxfTag(340, "000" + copied["SHARED"].Handle.ToLowerInvariant()));
        FifthEraseRejected(destination, copied);
        blocker.Data.Clear(); destination.Objects.EraseOwnedTree(blocker);
        FifthErase(destination, copied);
        destination = FifthSave(destination, version, binary, "copy-erased");
        Check(destination.GetObjectByHandle(mappedExternal.Handle) is DxfXRecord && destination.Entities.Lines.Count() == 1, "copy erasure removed external survivors");

        if (native != null)
        {
            FifthEraseRejected(source, graph); // The native TABLE pointer is the only semantic incoming blocker.
            var table = source.Entities.StoredTables.Single(); Check(source.Entities.Remove(table), "stored TABLE blocker removal failed");
            Throws<InvalidOperationException>(() => source.Entities.Add(table));
            Throws<InvalidOperationException>(() => destination.Entities.Add(table));
        }
        FifthErase(source, graph); source = FifthSave(source, version, binary, "source-erased");
        Check(source.GetObjectByHandle(external.Handle) is DxfXRecord && source.Entities.Lines.Count() == 1, "source erasure removed external survivors");
        var manifest = new
        {
            version = version.ToString(), binary, pairs,
            external = new { source = external.Handle, destination = mappedExternal.Handle },
            line = new { source = line.Handle, destination = mappedLine.Handle },
            sourceArbitrary = arbitrary, copyArbitrary,
            native = native == null ? null : new { file = native.File, sha256 = native.Sha256, sourceEntity = native.EntityHandle, sourceProfile = version.ToString(), emittedEntity = storedTableHandle,
                pointerCode = native.PointerCode, pointerValue = native.PointerHandle, syntheticTarget = "SHARED XRECORD; native TABLESTYLE semantics are not qualified" }
        };
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"fifth-mixed-{version}-{binary}-map.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static DxfDocument FifthBuild(DxfVersion version, bool binary, FifthNativePacket? native)
    {
        var doc = new DxfDocument(version); var line = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)); doc.Entities.Add(line);
        var external = new DxfXRecord(); external.Data.Add(new DxfTag(1, "external survivor")); doc.Objects.Root.Add("FIFTH_EXTERNAL", external);
        var graph = new DxfDictionary { IsHardOwner = false }; var shared = new DxfXRecord(); shared.Data.Add(new DxfTag(1, "shared source pointer target"));
        var index = new DxfLayerIndex { Timestamp = 2451545.625 }; var buffers = new[] { new DxfIdBuffer(), new DxfIdBuffer(), new DxfIdBuffer() };
        var pointer = new DxfObjectPointer(); var data = version >= DxfVersion.AutoCad2004 ? new DxfDataTable { Name = "Mixed Ω data" } : null;
        foreach (DxfObject? target in new DxfObject?[] { shared, shared, null, line, index, data ?? (DxfObject)pointer }) buffers[0].References.Add(target!);
        foreach (DxfObject? target in new DxfObject?[] { buffers[0], external, null }) buffers[1].References.Add(target!);
        index.SetEntries(FifthIndexNames.Select((name, i) => new DxfLayerIndexEntry(name, buffers[i])));
        graph.Add("SHARED", shared); graph.Add("INDEX", index); graph.Add("INDEX_ALIAS", index, false); graph.Add("POINTER", pointer);
        graph.Add("FILTER", new DxfLayerFilter(FifthIndexNames)); graph.Add("LINKS", new DxfXRecord());
        if (data != null)
        {
            var ownedRecord = new DxfXRecord(); ownedRecord.Data.Add(new DxfTag(1, "hard owned row"));
            var ownedVariable = new DxfDictionaryVariable { Value = "soft owned row" }; var ownedPlaceholder = new DxfPlaceholder();
            data.SetColumns(3, new[]
            {
                new DxfDataColumn(DxfDataCellType.Integer, "values", new object[] { int.MinValue, 0, int.MaxValue }),
                new DxfDataColumn(DxfDataCellType.String, "values", new object[] { "Ω中", @"literal \U+0041", "" }),
                new DxfDataColumn(DxfDataCellType.HardOwner, "owned", new object[] { ownedRecord, null!, ownedPlaceholder }),
                new DxfDataColumn(DxfDataCellType.SoftOwner, "owned", new object[] { ownedVariable, null!, null! }),
                new DxfDataColumn(DxfDataCellType.HardPointer, "pointers", new object[] { shared, shared, index }),
                new DxfDataColumn(DxfDataCellType.SoftPointer, "pointers", new object[] { buffers[0], external, null! }),
                new DxfDataColumn(DxfDataCellType.ObjectId, "geometry", new object[] { line, null!, line })
            }); graph.Add("DATA", data);
        }
        doc.Objects.Root.Add("FIFTH_GRAPH", graph);
        var extension = new DxfDictionary(); extension.Add("NOTE", new DxfDictionaryVariable { Value = "mixed extension" }); doc.Objects.SetExtensionDictionary(pointer, extension);
        index.PersistentReactors.Add(shared); shared.PersistentReactors.Add(index); graph.PersistentReactors.Add(external);
        Point? carrier = null; if (native != null) { carrier = new Point(Vector3.Zero); doc.Entities.Add(carrier); }
        using var setup = new MemoryStream(); Check(doc.Save(setup, binary), "mixed graph setup save"); setup.Position = 0; var raw = DxfRawDocument.Load(setup);
        string sharedHandle = shared.Handle;
        string Remap(string value)
        {
            ulong numeric = FifthNumeric(value); if (numeric == 0) return value;
            if (native != null && numeric == FifthNumeric(sharedHandle)) return "000" + native.PointerHandle.ToLowerInvariant();
            return checked(numeric + 0x10000).ToString("X", CultureInfo.InvariantCulture);
        }
        raw = raw.WithTags(raw.Tags.Select(t => t.ValueType == DxfTagValueType.Handle && t.HandleKind != DxfHandleKind.Arbitrary ? new DxfTag(t.Code, Remap((string)t.Value)) : t));
        if (native != null)
        {
            var point = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "POINT");
            int boundary = point.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbPoint");
            raw = raw.WithRecord(point, point.Tags.Take(boundary).Select(t => t.Code == 0 ? new DxfTag(0, "ACAD_TABLE") : t).Concat(native.Payload));
        }
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        return DxfDocument.Load(input) ?? throw new Exception("Mixed native-packet carrier load failed");
    }

    private static FifthNativePacket? FifthNative(DxfVersion version)
    {
        string? file = version switch
        {
            DxfVersion.AutoCad2004 => "sample_AC1018_ascii.dxf", DxfVersion.AutoCad2007 => "sample_AC1021_ascii.dxf", DxfVersion.AutoCad2010 => "sample_AC1024_ascii.dxf",
            DxfVersion.AutoCad2013 => "acad_table_simple.dxf", DxfVersion.AutoCad2018 => "acad_table_with_blk_ref.dxf", _ => null
        };
        if (file == null) return null;
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", file + ".gz")); using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes); string sha = Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant();
        using var inventory = JsonDocument.Parse(File.ReadAllText("tools/table_oracle/fixtures.json"));
        Equal(inventory.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("file").GetString() == file).GetProperty("sha256").GetString(), sha, "pinned mixed TABLE source hash");
        bytes.Position = 0; var raw = DxfRawDocument.Load(bytes); Equal(version, raw.Version, "native TABLE extraction profile");
        var table = raw.Sections.Single(s => s.Name == "ENTITIES").Records.First(r => r.Name == "ACAD_TABLE");
        int start = table.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbBlockReference");
        var payload = table.Tags.Skip(start).TakeWhile(t => t.Code != 1001).ToList(); var pointer = payload.First(t => t.Code == 342 && FifthNumeric((string)t.Value) != 0);
        return new FifthNativePacket(file, sha, (string)table.Tags.Single(t => t.Code == 5).Value, pointer.Code, (string)pointer.Value, payload);
    }

    private static Dictionary<string, DxfDatabaseObject> FifthPaths(DxfDictionary graph)
    {
        var result = new Dictionary<string, DxfDatabaseObject> { ["/"] = graph };
        foreach (string name in new[] { "SHARED", "INDEX", "POINTER", "FILTER", "LINKS" }) result[name] = (DxfDatabaseObject)graph[name];
        var index = (DxfLayerIndex)graph["INDEX"]; for (int i = 0; i < index.Entries.Count; i++) result["INDEX/buffer/" + i] = index.Entries[i].Buffer;
        result["POINTER/extension"] = graph["POINTER"].ExtensionDictionary; result["POINTER/extension/NOTE"] = (DxfDatabaseObject)graph["POINTER"].ExtensionDictionary["NOTE"];
        if (graph.Contains("DATA"))
        {
            var data = (DxfDataTable)graph["DATA"]; result["DATA"] = data;
            result["DATA/hard/0"] = (DxfDatabaseObject)data.Columns[2].Values[0]; result["DATA/hard/2"] = (DxfDatabaseObject)data.Columns[2].Values[2]; result["DATA/soft/0"] = (DxfDatabaseObject)data.Columns[3].Values[0];
        }
        return result;
    }
    private static void FifthAssert(DxfDocument doc, DxfDictionary graph, DxfXRecord external, Line line, string[] arbitrary, string app)
    {
        var shared = graph["SHARED"]; var index = (DxfLayerIndex)graph["INDEX"];
        Check(ReferenceEquals(graph["INDEX_ALIAS"], index), "mixed index alias identity"); Check(index.Entries.Select(e => e.LayerName).SequenceEqual(FifthIndexNames), "stored index name order/case");
        Equal(BitConverter.DoubleToInt64Bits(2451545.625), BitConverter.DoubleToInt64Bits(index.Timestamp), "stored index timestamp bits");
        Check(index.Entries.Select(e => e.Count).SequenceEqual(new[] { 6, 3, 0 }), "index counts changed");
        foreach (var entry in index.Entries) Check(ReferenceEquals(entry.Buffer.Owner, index), "index buffer reciprocal ownership");
        Check(index.Entries[0].Buffer.References.SequenceEqual(new DxfObject?[] { shared, shared, null, line, index, graph.Contains("DATA") ? graph["DATA"] : graph["POINTER"] }), "index internal/external/repeated/null references");
        Check(index.Entries[1].Buffer.References.SequenceEqual(new DxfObject?[] { index.Entries[0].Buffer, external, null }), "second index buffer references");
        var links = (DxfXRecord)graph["LINKS"]; Equal(FifthNumeric(shared.Handle), FifthNumeric((string)links.Data[0].Value), "mixed hard reference mapping"); Equal(external.Handle, (string)links.Data[1].Value, "mixed external reference mapping");
        Check(links.Data.Where(t => t.Code is 320 or 329).Select(t => (string)t.Value).SequenceEqual(arbitrary), "arbitrary values were remapped");
        foreach (DxfObject carrier in new DxfObject[] { graph["POINTER"], index.Entries[1].Buffer })
        {
            var metadata = carrier.XData[app]; Equal(shared.Handle, (string)metadata.XDataRecord[0].Value, "APPID internal handle binding"); Equal(external.Handle, (string)metadata.XDataRecord[1].Value, "APPID external handle binding");
            Check(((byte[])metadata.XDataRecord[2].Value).SequenceEqual(new byte[] { 0, 255, 7 }), "APPID binary value changed");
        }
        Check(index.PersistentReactors.Contains(shared) && shared.PersistentReactors.Contains(index) && graph.PersistentReactors.Contains(external), "mixed persistent reactors changed");
        Equal("mixed extension", ((DxfDictionaryVariable)graph["POINTER"].ExtensionDictionary["NOTE"]).Value, "mixed extension payload");
        if (graph.Contains("DATA"))
        {
            var data = (DxfDataTable)graph["DATA"]; Equal(3, data.RowCount, "mixed DATATABLE rows"); Equal(7, data.Columns.Count, "mixed DATATABLE columns");
            Check(data.Columns[0].Values.Cast<int>().SequenceEqual(new[] { int.MinValue, 0, int.MaxValue }), "DATATABLE integer extremes");
            Check(data.Columns[1].Values.Cast<string>().SequenceEqual(new[] { "Ω中", @"literal \U+0041", "" }), "DATATABLE string spelling");
            foreach (var column in data.Columns.Where(c => c.Type is DxfDataCellType.HardOwner or DxfDataCellType.SoftOwner))
                foreach (DxfDatabaseObject child in column.Values.OfType<DxfDatabaseObject>()) Check(ReferenceEquals(child.Owner, data), "DATATABLE owner was reinterpreted as pointer");
            Check(data.Columns[4].Values.SequenceEqual(new object[] { shared, shared, index }), "DATATABLE hard pointers");
            Check(data.Columns[5].Values.SequenceEqual(new object?[] { index.Entries[0].Buffer, external, null }), "DATATABLE soft pointers");
            Check(data.Columns[6].Values.SequenceEqual(new object?[] { line, null, line }), "DATATABLE object IDs");
            Equal(shared.Handle, (string)((DxfXRecord)data.Columns[2].Values[0]).Data[1].Value, "owned XRECORD remapping");
        }
        Equal(0, doc.Objects.Validate().Count, "mixed graph validation");
    }
    private static DxfDocument FifthSave(DxfDocument doc, DxfVersion version, bool binary, string phase)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "mixed " + phase + " save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"fifth-mixed-{version}-{binary}-{phase}.dxf"), stream.ToArray()); stream.Position = 0;
        var loaded = DxfDocument.Load(stream) ?? throw new Exception("Mixed " + phase + " reload failed");
        if (phase.EndsWith("erased", StringComparison.Ordinal) && version >= DxfVersion.AutoCad2004)
            foreach (string name in new[] { "LAYER_INDEX", "DATATABLE" }) if (loaded.Classes.Contains(name)) Equal(0, loaded.Classes[name].InstanceCount, "erased mixed CLASS count stale");
        return loaded;
    }
    private static void FifthEraseRejected(DxfDocument doc, DxfDictionary graph)
    {
        var identities = FifthPaths(graph).Values.ToArray(); long seed = FifthSeed(doc); int count = doc.Objects.Items.Count;
        Throws<InvalidOperationException>(() => doc.Objects.EraseOwnedTree(graph));
        Equal(seed, FifthSeed(doc), "failed mixed erasure changed seed"); Equal(count, doc.Objects.Items.Count, "failed mixed erasure changed registry size");
        foreach (var item in identities) Check(!item.IsErased && ReferenceEquals(doc.GetObjectByHandle(item.Handle), item), "failed mixed erasure changed identity");
    }
    private static void FifthErase(DxfDocument doc, DxfDictionary graph)
    {
        var identities = FifthPaths(graph).Values.ToArray(); long seed = FifthSeed(doc); doc.Objects.EraseOwnedTree(graph);
        Equal(seed, FifthSeed(doc), "mixed erasure allocated or reused handles");
        foreach (var item in identities) Check(item.IsErased && item.Database == null && doc.GetObjectByHandle(item.Handle) == null, "mixed declared-owned descendant survived erasure");
        Check(!doc.Objects.Root.Contains("FIFTH_GRAPH"), "erased mixed alias survived");
    }
    private static long FifthSeed(DxfDocument doc) => (long)typeof(DxfDocument).GetProperty("NumHandles", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(doc)!;
    private static ulong FifthNumeric(string handle) => ulong.Parse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
    private static IEnumerable<string> FifthTagValues(IEnumerable<DxfTag> tags) => tags.Select(t => t.Code + ":" + (t.Value is byte[] bytes ? Convert.ToHexString(bytes) : t.Value is double real ? BitConverter.DoubleToInt64Bits(real).ToString("X") : Convert.ToString(t.Value, CultureInfo.InvariantCulture)));
}
