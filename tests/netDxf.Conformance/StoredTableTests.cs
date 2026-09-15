using System.IO.Compression;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredTableTests()
    {
        foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf", "sample_AC1018_ascii.dxf", "sample_AC1021_ascii.dxf", "sample_AC1024_ascii.dxf" })
            foreach (bool binary in new[] { false, true })
                Run($"stored-table/native-packets/{file}/{binary}", () => StoredTableNative(file, binary));
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                Run($"stored-table/lifecycle/{version}/{binary}", () => StoredTableLifecycle(version, binary));
        foreach (string file in new[] { "acad_table_simple.dxf", "acad_table_with_blk_ref.dxf" })
            foreach (bool binary in new[] { false, true })
                Run($"stored-table/native-document/{file}/{binary}", () => StoredTableFullNative(file, binary));
        foreach (short code in Enumerable.Range(320, 50).Concat(Enumerable.Range(390, 10)).Concat(new[] { 480, 481 }).Select(v => (short)v))
            Run($"stored-table/semantic-dependency/{code}", () => StoredTableDependency(code));
        foreach (string kind in new[] { "APPID", "ENTITY", "BLOCK_MEMBER" })
            foreach (bool binary in new[] { false, true })
                Run($"stored-table/resource-removal/{kind}/{binary}", () => StoredTableResourceRemoval(kind, binary));
        foreach (bool binary in new[] { false, true })
        {
            Run($"stored-table/unknown-schema/{binary}", () => StoredTableUnknown(binary));
            Run($"stored-table/non-z-normal/{binary}", () => StoredTableNonZ(binary));
            Run($"stored-table/invalid-count/{binary}", () => StoredTableInvalidCount(binary));
        }
    }
    private static DxfRawDocument StoredTableSource(string file)
    {
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", file + ".gz"));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        using var manifest = JsonDocument.Parse(File.ReadAllText("tools/table_oracle/fixtures.json"));
        string hash = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(hash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes.ToArray())).ToLowerInvariant(), "pinned native source hash");
        bytes.Position = 0; return DxfRawDocument.Load(bytes);
    }
    private static DxfDocument StoredTableCarrier(DxfVersion version, IEnumerable<DxfTag> payload, bool binary, out string tableHandle, string? referenceKind = null)
    {
        var doc = new DxfDocument(version); for (int i = 0; i < 500; i++) doc.Layers.Add(new Layer("TABLE_PADDING_" + i));
        doc.Blocks.Add(new Block("TABLE_DISPLAY")); var external = new DxfXRecord(); doc.Objects.Root.Add("TABLE_DEPENDENCY", external);
        DxfObject reference = external;
        if (referenceKind == "APPID") reference = doc.ApplicationRegistries.Add(new ApplicationRegistry("TABLE_REFERENCE_APP"));
        if (referenceKind == "ENTITY") { var line = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(line); reference = line; }
        if (referenceKind == "BLOCK_MEMBER")
        { var host = new Block("TABLE_REFERENCE_BLOCK"); var line = new Line(Vector3.Zero, Vector3.UnitX); host.Entities.Add(line); doc.Blocks.Add(host); reference = line; }
        var point = new Point(Vector3.Zero); doc.Entities.Add(point); tableHandle = point.Handle;
        using var setup = new MemoryStream(); Check(doc.Save(setup, binary), "table carrier setup"); setup.Position = 0;
        var raw = DxfRawDocument.Load(setup); var record = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "POINT");
        int firstSubclass = record.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value != "AcDbEntity");
        var prefix = record.Tags.Take(firstSubclass).Select(t => t.Code == 0 ? new DxfTag(0, "ACAD_TABLE") : t);
        raw = raw.WithRecord(record, prefix.Concat(payload.Select(t => t.Value is string value && value == "FFFFFFFF" ? new DxfTag(t.Code, reference.Handle) : t))); using var source = new MemoryStream(); raw.Save(source); source.Position = 0;
        return DxfDocument.Load(source) ?? throw new Exception("Stored TABLE carrier failed to load.");
    }
    private static void StoredTableNative(string file, bool binary)
    {
        var source = StoredTableSource(file); using var manifest = JsonDocument.Parse(File.ReadAllText("tools/table_oracle/qualification.json"));
        var oracle = manifest.RootElement.GetProperty("files").EnumerateArray().Single(f => f.GetProperty("file").GetString() == file);
        foreach (var record in source.Sections.Single(s => s.Name == "ENTITIES").Records.Where(r => r.Name == "ACAD_TABLE"))
        {
            string handle = (string)record.Tags.Single(t => t.Code == 5).Value;
            var expected = oracle.GetProperty("tables").EnumerateArray().Single(t => t.GetProperty("handle").GetString() == handle);
            int start = record.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbBlockReference");
            var payload = record.Tags.Skip(start).TakeWhile(t => t.Code != 1001).ToList();
            var doc = StoredTableCarrier(source.Version, payload, binary, out _); var table = doc.Entities.StoredTables.Single();
            Equal(source.Version, table.SourceVersion, "native source version"); Check(table.Grid != null, "native flat grid recognized");
            Equal(expected.GetProperty("rows").GetInt32(), table.Grid!.RowCount, "independent native row count");
            Equal(expected.GetProperty("columns").GetInt32(), table.Grid.ColumnCount, "independent native column count");
            Check(expected.GetProperty("row_heights").EnumerateArray().Select(v => v.GetDouble()).SequenceEqual(table.Grid.RowHeights), "independent native heights");
            Check(expected.GetProperty("column_widths").EnumerateArray().Select(v => v.GetDouble()).SequenceEqual(table.Grid.ColumnWidths), "independent native widths");
            Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(table.Payload)), "native payload input preservation");
            if (file == "acad_table_simple.dxf")
            {
                Equal("Title", (string)table.Grid[0, 0].LiteralValue, "native title value");
                for (int i = 3; i < 9; i++) Equal((i - 2).ToString(), (string)table.Grid.Cells[i].LiteralValue, "native numbered literal");
            }
            if (handle == "A35" && source.Version != DxfVersion.AutoCad2004)
            {
                Equal(0.5, (double)table.Grid.Cells[10].LiteralValue, "native unformatted real");
                Equal(1.047197551196597, (double)table.Grid.Cells[11].LiteralValue, "native angle retains raw value, not formatted degrees");
                Check(!table.Grid.Cells[12].HasLiteralValue && !table.Grid.Cells[13].HasLiteralValue, "native point/date values are not coerced");
                Equal(5, (int)table.Grid.Cells[14].LiteralValue, "native integer value");
                Equal(35.0, (double)table.Grid.Cells[15].LiteralValue, "native percent retains raw value");
                Equal(100.0, (double)table.Grid.Cells[16].LiteralValue, "native currency retains raw value");
            }
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "native TABLE output");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-native-{file}-{handle}-{binary}.dxf"), output.ToArray()); output.Position = 0;
            var saved = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "ACAD_TABLE");
            int after = saved.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbBlockReference");
            Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(saved.Tags.Skip(after))), "native subclass payload output preservation");
            output.Position = 0; var round = DxfDocument.Load(output)!; Equal(table.Grid.RowCount, round.Entities.StoredTables.Single().Grid!.RowCount, "native typed reload");
        }
    }
    private static void StoredTableFullNative(string file, bool binary)
    {
        var original = StoredTableSource(file); using var input = new MemoryStream(); original.Save(input); input.Position = 0;
        var doc = DxfDocument.Load(input) ?? throw new Exception("Full native document failed to load.");
        var table = doc.Entities.StoredTables.Single(); Check(table.Grid != null, "full native flat grid");
        Check(table.BackingContent != null, "full native backing content linkage");
        if (file == "acad_table_simple.dxf") Equal((bool?)true, table.BackingLiteralValuesAgree, "independent entity/backing literal agreement");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "full native TABLE document save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"stored-table-full-{file}-{binary}.dxf"), output.ToArray());
        output.Position = 0; var after = DxfRawDocument.Load(output);
        var beforeRecord = original.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "ACAD_TABLE");
        var afterRecord = after.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "ACAD_TABLE");
        int first = beforeRecord.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbBlockReference");
        int second = afterRecord.Tags.ToList().FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbBlockReference");
        Check(OwnershipTagValues(beforeRecord.Tags.Skip(first)).SequenceEqual(OwnershipTagValues(afterRecord.Tags.Skip(second))), "full native TABLE packet exactness");
    }
    private static List<DxfTag> StoredTableTestPayload()
    {
        return new() { new(100, "AcDbBlockReference"), new(2, "TABLE_DISPLAY"), new(10, 1.0), new(20, 2.0), new(30, 3.0),
            new(100, "AcDbTable"), new(90, 22), new(91, 1), new(92, 1), new(141, 4.0), new(142, 5.0),
            new(171, (short)1), new(301, "CELL_VALUE"), new(93, 2), new(90, 4), new(1, "Literal\\U+0041"), new(304, "ACVALUE_END") };
    }
    private static void StoredTableLifecycle(DxfVersion version, bool binary)
    {
        var doc = StoredTableCarrier(version, StoredTableTestPayload(), binary, out string handle); var table = doc.Entities.StoredTables.Single();
        Equal("LiteralA", (string)table.Grid![0, 0].LiteralValue, "decoded literal value");
        Throws<ArgumentOutOfRangeException>(() => { _ = table.Grid[1, 0]; });
        Throws<ArgumentOutOfRangeException>(() => { _ = table.Grid[0, -1]; });
        Throws<NotSupportedException>(() => table.Clone());
        table.TransformBy(Matrix3.Identity, Vector3.Zero); table.TransformBy(Matrix4.Identity);
        Throws<NotSupportedException>(() => table.TransformBy(Matrix3.Identity, new Vector3(1, 0, 0)));
        var perspective = Matrix4.Identity; perspective.M41 = 0.001; Throws<NotSupportedException>(() => table.TransformBy(perspective));
        EntityObject baseView = table; Throws<NotSupportedException>(() => baseView.Normal = Vector3.UnitX);
        var block = doc.Blocks["TABLE_DISPLAY"]; Check(doc.Blocks.HasReferences(block), "display block dependency indexed");
        Check(!doc.Blocks.Remove(block), "display block removal rejected");
        block.Name = "RENAMED_TABLE_DISPLAY";
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "renamed display block output"); output.Position = 0;
        var saved = DxfRawDocument.Load(output).Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "ACAD_TABLE");
        Equal("RENAMED_TABLE_DISPLAY", (string)saved.Tags.Single(t => t.Code == 2).Value, "display name follows exact bound resource");
        var different = version == DxfVersion.AutoCad2018 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018;
        doc.DrawingVariables.AcadVer = different; StoredTableSaveFailure(doc, binary); doc.DrawingVariables.AcadVer = version;
        Check(doc.Entities.Remove(table), "remove stored table");
        Throws<InvalidOperationException>(() => doc.Entities.Add(table));
        Throws<InvalidOperationException>(() => new DxfDocument(version).Entities.Add(table));
        Throws<InvalidOperationException>(() => new Block("DETACHED_TABLE").Entities.Add(table));
        Check(!doc.Entities.StoredTables.Any(), "rejected reattachment left collection unchanged");
        Equal(null, doc.GetObjectByHandle(handle), "removed table is unregistered");
    }
    private static void StoredTableUnknown(bool binary)
    {
        var payload = StoredTableTestPayload(); payload.Add(new DxfTag(100, "PrivateTableSubclass")); payload.Add(new DxfTag(310, new byte[] { 0, 255, 4 }));
        var doc = StoredTableCarrier(DxfVersion.AutoCad2018, payload, binary, out _); var table = doc.Entities.StoredTables.Single();
        Equal(null, table.Grid, "private schema receives no invented grid interpretation");
        byte[] bytes = (byte[])table.Payload.Last().Value; bytes[0] = 23; Equal((byte)0, ((byte[])table.Payload.Last().Value)[0], "payload byte defensive copy");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "private payload save"); output.Position = 0;
        var reloaded = DxfDocument.Load(output)!; Check(OwnershipTagValues(payload).SequenceEqual(OwnershipTagValues(reloaded.Entities.StoredTables.Single().Payload)), "unknown payload preserved");
    }
    private static void StoredTableNonZ(bool binary)
    {
        var payload = StoredTableTestPayload(); payload.InsertRange(5, new[] { new DxfTag(210, 0.0), new DxfTag(220, 1.0), new DxfTag(230, 0.0) });
        var doc = StoredTableCarrier(DxfVersion.AutoCad2018, payload, binary, out _); var table = doc.Entities.StoredTables.Single();
        Equal(Vector3.UnitY, table.Normal, "non-Z stored normal"); Equal(Vector3.UnitY, ((EntityObject)table).Normal, "base accessor non-Z normal"); table.Normal = Vector3.UnitY;
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "non-Z output"); output.Position = 0;
        Equal(Vector3.UnitY, DxfDocument.Load(output)!.Entities.StoredTables.Single().Normal, "non-Z roundtrip");
    }
    private static void StoredTableInvalidCount(bool binary)
    {
        var payload = StoredTableTestPayload(); payload[8] = new DxfTag(91, 2);
        bool rejected = false;
        try { StoredTableCarrier(DxfVersion.AutoCad2018, payload, binary, out _); } catch (Exception) { rejected = true; }
        Check(rejected, "invalid fixed-grid count rejected");
    }
    private static void StoredTableResourceRemoval(string kind, bool binary)
    {
        var payload = StoredTableTestPayload(); payload.Add(new DxfTag(340, "FFFFFFFF"));
        var doc = StoredTableCarrier(DxfVersion.AutoCad2018, payload, binary, out _, kind);
        var table = doc.Entities.StoredTables.Single(); long seed = OwnershipSeed(doc);
        if (kind == "APPID")
        {
            var registry = doc.ApplicationRegistries["TABLE_REFERENCE_APP"];
            Check(doc.ApplicationRegistries.HasReferences(registry), "TABLE semantic APPID reference included");
            Check(doc.ApplicationRegistries.GetReferences(registry).Any(r => ReferenceEquals(r.Reference, table)), "TABLE APPID reference reported");
            Check(!doc.ApplicationRegistries.Remove(registry), "TABLE semantic APPID removal rejected");
        }
        else if (kind == "ENTITY")
        {
            var line = doc.Entities.Lines.Single(); Check(!doc.Entities.Remove(line), "referenced entity removal rejected");
            Check(ReferenceEquals(doc.GetObjectByHandle(line.Handle), line), "referenced entity retained");
        }
        else
        {
            var block = doc.Blocks["TABLE_REFERENCE_BLOCK"]; string handle = block.Handle;
            Check(!block.Entities.Remove(block.Entities.OfType<Line>().Single()), "referenced block member removal rejected");
            Check(!doc.Blocks.Remove(block), "containing block removal rejected");
            Check(ReferenceEquals(doc.GetObjectByHandle(handle), block), "containing block retained");
        }
        Equal(seed, OwnershipSeed(doc), "rejected resource removal retained allocation seed");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "document remains saveable after rejected removal");
    }
    private static void StoredTableDependency(short code)
    {
        var payload = StoredTableTestPayload(); payload.Add(new DxfTag(code, "FFFFFFFF"));
        var doc = StoredTableCarrier(DxfVersion.AutoCad2018, payload, false, out _);
        var table = doc.Entities.StoredTables.Single(); var dependency = (DxfXRecord)doc.Objects.Root["TABLE_DEPENDENCY"];
        long seed = OwnershipSeed(doc); string handle = dependency.Handle;
        if (code < 330)
        {
            Check(!table.References.Any(r => ReferenceEquals(r, dependency)), "arbitrary handle excluded");
            doc.Objects.EraseOwnedTree(dependency); Check(dependency.IsErased, "arbitrary value did not block erasure");
        }
        else
        {
            Check(table.References.Any(r => ReferenceEquals(r, dependency)), "semantic handle bound");
            Throws<InvalidOperationException>(() => doc.Objects.EraseOwnedTree(dependency));
            Equal(seed, OwnershipSeed(doc), "failed erasure retained allocation seed");
            Check(ReferenceEquals(doc.GetObjectByHandle(handle), dependency) && !dependency.IsErased, "failed erasure retained exact dependency");
        }
    }
    private static void StoredTableSaveFailure(DxfDocument doc, bool binary)
    {
        using var output = new MemoryStream(); bool failed;
        try { failed = !doc.Save(output, binary); } catch (NotSupportedException) { failed = true; }
        Check(failed, "unsupported version rejected"); Equal(0L, output.Length, "unsupported version did not write bytes");
    }

}
