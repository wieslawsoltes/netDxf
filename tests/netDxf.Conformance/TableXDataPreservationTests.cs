// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Collections;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Units;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private const string TxBlock = "TABLEX_BLOCK", TxLayer = "TABLEX_LAYER";
    private static XDataRecord[] TxUnitRecords(short units) => new[]
    {
        DxString("BEFORE_Ł"), DxOpen, DxString("DesignCenter Data"), DxOpen, DxId(1), DxId(7), DxClose, DxClose,
        DxString("dEsIgNcEnTeR dAtA"), DxOpen, DxId(7), DxId(units),
        DxId(24), DxOpen, DxString("extension"), DxClose, DxClose,
        new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 255, 10, 125 }), DxString("AFTER")
    };
    private static XDataRecord[] TxDescription(string description) => new[]
    {
        DxString("private-first"), new XDataRecord(XDataCode.Int16, (short)17),
        DxString("earlier-string"), DxReal(-0.0), DxString(description),
        new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 1, 255 }), new XDataRecord(XDataCode.Int32, 8123)
    };
    private static XDataRecord[] TxTransparency(int alpha) => new[]
    {
        DxString("alpha-prefix"), new XDataRecord(XDataCode.Int32, 0x020000AB),
        DxOpen, DxString("opaque"), DxClose, new XDataRecord(XDataCode.Int32, alpha),
        new XDataRecord(XDataCode.BinaryData, new byte[] { 1, 255, 0 }), DxReal(1e-13)
    };

    private static void TxAttach(DxfDocument doc, DxfObject host, string app, IEnumerable<XDataRecord> records)
    {
        var data = new XData(doc.ApplicationRegistries.Add(new ApplicationRegistry(app)));
        data.XDataRecord.AddRange(records); host.XData.Add(data);
    }
    private static Action TxSnapshot(DxfObject host)
    {
        var apps = host.XData.Values.ToArray();
        var records = apps.Select(a => a.XDataRecord.ToArray()).ToArray();
        var binary = records.SelectMany(r => r).Where(r => r?.Value is byte[])
            .Select(r => ((byte[])r.Value).ToArray()).ToArray();
        int events = 0; host.XData.AddAppReg += (_, _) => events++; host.XData.RemoveAppReg += (_, _) => events++;
        return () =>
        {
            Equal(0, events, "Table save fired XData registry events");
            Check(apps.SequenceEqual(host.XData.Values), "Table save changed application identity/order");
            for (int i = 0; i < apps.Length; i++)
                Check(records[i].SequenceEqual(apps[i].XDataRecord), "Table save changed record identity/order");
            int at = 0;
            foreach (var r in records.SelectMany(r => r).Where(r => r?.Value is byte[]))
                Check(binary[at++].SequenceEqual((byte[])r.Value), "Table save mutated binary data");
        };
    }
    private static void TxRecords(IEnumerable<XDataRecord> expected, IEnumerable<XDataRecord> actual)
    {
        var a = expected.ToArray(); var b = actual.ToArray(); Equal(a.Length, b.Length, "Table record count");
        for (int i = 0; i < a.Length; i++) DxRecordEqual(a[i], b[i]);
    }
    private static BlockRecord TxRecord(DxfDocument doc, int placement) => placement == 3
        ? doc.Layouts[Layout.ModelSpaceName].AssociatedBlock.Record : placement == 4
        ? doc.Layouts["TABLEX_PAPER"].AssociatedBlock.Record : doc.Blocks[TxBlock].Record;

    private static DxfDocument TxDocument(DxfVersion version, int placement, out Layer layer, out BlockRecord record)
    {
        var doc = new DxfDocument(version); doc.Comments.Clear();
        layer = new Layer(TxLayer) { Description = "Initial_Ł", Transparency = new Transparency(37) };
        doc.Layers.Add(layer);
        TxAttach(doc, layer, "TABLEX_BEFORE", new[] { DxString("first") });
        TxAttach(doc, layer, "AcAecLayerStandard", TxDescription("Initial_Ł"));
        TxAttach(doc, layer, "AcCmTransparency", TxTransparency(Transparency.ToAlphaValue(layer.Transparency)));
        TxAttach(doc, layer, "TABLEX_AFTER", new[] { DxString("last") });
        if (placement is 2 or 4) doc.Layouts.Add(new Layout("TABLEX_PAPER"));
        if (placement < 3)
        {
            var block = new Block(TxBlock, new EntityObject[] {
                new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6)) { Layer = layer }
            });
            block.Record.Units = DrawingUnits.Millimeters;
            if (placement == 1) doc.Blocks.Add(block);
            else if (placement == 2) doc.Layouts["TABLEX_PAPER"].AssociatedBlock.Entities.Add(new Insert(block));
            else doc.Entities.Add(new Insert(block));
        }
        record = TxRecord(doc, placement);
        TxAttach(doc, record, "TABLEX_BEFORE", new[] { DxString("first") });
        TxAttach(doc, record, "ACAD", TxUnitRecords((short)(placement < 3 ? 4 : 0)));
        TxAttach(doc, record, "TABLEX_AFTER", new[] { DxString("last") });
        doc.Entities.Add(new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)) { Layer = layer });
        return doc;
    }
    private static byte[] TxSave(DxfDocument doc, bool binary, int placement)
    {
        var layer = doc.Layers[TxLayer]; var record = TxRecord(doc, placement);
        var a = TxSnapshot(layer); var b = TxSnapshot(record);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Table XData save");
        a(); b(); Check(stream.CanWrite, "Save closed caller stream"); return stream.ToArray();
    }
    private static void TxCheck(DxfDocument doc, int placement, int stage, string layerHandle, string recordHandle)
    {
        var layer = doc.Layers[TxLayer]; var record = TxRecord(doc, placement);
        string description = stage == 0 ? "Initial_Ł" : stage == 1 ? "Updated_Ł" : "";
        short alpha = stage == 0 ? (short)37 : stage == 1 ? (short)62 : (short)0;
        short units = placement < 3 ? stage == 0 ? (short)4 : stage == 1 ? (short)6 : (short)0 : (short)0;
        Equal(description, layer.Description, "Table layer description");
        Equal(alpha, layer.Transparency.Value, "Table layer transparency");
        Equal(layerHandle, layer.Handle, "Layer identity");
        Equal(recordHandle, record.Handle, "Block record identity");
        TxRecords(TxDescription(description), layer.XData["AcAecLayerStandard"].XDataRecord);
        TxRecords(TxTransparency(Transparency.ToAlphaValue(new Transparency(alpha))), layer.XData["AcCmTransparency"].XDataRecord);
        TxRecords(TxUnitRecords(units), record.XData["ACAD"].XDataRecord);
        if (placement < 3) Equal((DrawingUnits)units, record.Units, "Table insertion units");
        Check(new[] { "TABLEX_BEFORE", "ACAD", "TABLEX_AFTER" }.SequenceEqual(record.XData.AppIds), "Block application order");
        Check(new[] { "TABLEX_BEFORE", "AcAecLayerStandard", "AcCmTransparency", "TABLEX_AFTER" }.SequenceEqual(layer.XData.AppIds), "Layer application order");
        foreach (DxfObject host in new DxfObject[] { layer, record })
        {
            Equal("first", (string)host.XData["TABLEX_BEFORE"].XDataRecord.Single().Value, "First opaque application");
            Equal("last", (string)host.XData["TABLEX_AFTER"].XDataRecord.Single().Value, "Last opaque application");
        }
        var clone = (Layer)layer.Clone("TABLEX_CLONE"); Equal(description, clone.Description, "Layer clone description");
        clone.Description = "CHANGED"; clone.XData["AcAecLayerStandard"].XDataRecord.Clear();
        Equal(description, layer.Description, "Layer clone property isolation");
        Equal(7, layer.XData["AcAecLayerStandard"].XDataRecord.Count, "Layer clone record isolation");
        Equal(0, doc.Objects.Validate().Count, "Table XData graph validation");
        var sentinel = doc.Entities.Lines.Single();
        RawLinePointBits(new Vector3(17.25, -4.5, 2), sentinel.StartPoint);
        RawLinePointBits(new Vector3(18.5, 9.25, -3), sentinel.EndPoint);
        if (placement < 3)
        {
            var line = doc.Blocks[TxBlock].Entities.OfType<Line>().Single();
            RawLinePointBits(new Vector3(1, 2, 3), line.StartPoint); RawLinePointBits(new Vector3(4, 5, 6), line.EndPoint);
        }
    }

    private static void RegisterTableXDataPreservationTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            for (int placement = 0; placement < 5; placement++)
            {
                int p = placement;
                Run($"table-xdata/wire/{version}/{binary}/{p}", () =>
                {
                    var doc = TxDocument(version, p, out var layer, out var record);
                    string lh = layer.Handle, rh = record.Handle;
                    string stem = $"table-xdata-{version}-{binary}-{p}";
                    byte[] source = TxSave(doc, binary, p);
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source);
                    using var input = new MemoryStream(source);
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Table source load");
                    TxCheck(loaded, p, 0, lh, rh);
                    for (int stage = 1; stage <= 2; stage++)
                    {
                        loaded.Layers[TxLayer].Description = stage == 1 ? "Updated_Ł" : "";
                        loaded.Layers[TxLayer].Transparency = new Transparency(stage == 1 ? (short)62 : (short)0);
                        if (p < 3) TxRecord(loaded, p).Units = stage == 1 ? DrawingUnits.Meters : DrawingUnits.Unitless;
                        foreach (bool output in new[] { false, true })
                        {
                            byte[] bytes = TxSave(loaded, output, p);
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{stage}-{output}.dxf"), bytes);
                            using var stream = new MemoryStream(bytes);
                            var second = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Table output load");
                            TxCheck(second, p, stage, lh, rh);
                            // A further save does not change the loaded opaque values.
                            using var repeated = new MemoryStream(TxSave(second, output, p));
                            var third = DxfDocument.Load(repeated) ?? throw new InvalidOperationException("Table repeated load");
                            TxCheck(third, p, stage, lh, rh);
                        }
                    }
                    Check(input.CanRead, "Load closed caller stream");
                });
            }
            for (short unit = 0; unit <= 24; unit++)
            {
                short u = unit;
                Run($"table-xdata/legacy-units/{version}/{binary}/{u}", () =>
                {
                    var doc = TxDocument(version, 1, out _, out var record);
                    record.Units = (DrawingUnits)u;
                    var raw = LoadRaw(TxSave(doc, binary, 1));
                    var target = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "BLOCK_RECORD" &&
                        r.Tags.Any(t => t.Code == 2 && Equals(t.Value, TxBlock)));
                    raw = raw.WithRecord(target, target.Tags.Where(t => t.Code != 70));
                    byte[] source = SaveRaw(raw, binary);
                    string stem = $"table-legacy-units-{version}-{binary}-{u}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), source);
                    using var input = new MemoryStream(source);
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Legacy unit load");
                    Equal((DrawingUnits)u, loaded.Blocks[TxBlock].Record.Units, "Legacy XData units ignored");
                    TxRecords(TxUnitRecords(u), loaded.Blocks[TxBlock].Record.XData["ACAD"].XDataRecord);
                    byte[] output = TxSave(loaded, binary, 1);
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-output.dxf"), output);
                    using var stream = new MemoryStream(output);
                    Equal((DrawingUnits)u, DxfDocument.Load(stream)!.Blocks[TxBlock].Record.Units, "Legacy units resave");
                });
            }
            foreach (bool missing in new[] { false, true })
                Run($"table-xdata/nested-only/{version}/{binary}/{missing}", () =>
                {
                    var doc = TxDocument(version, 1, out _, out _);
                    var raw = LoadRaw(TxSave(doc, binary, 1));
                    var target = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "BLOCK_RECORD" &&
                        r.Tags.Any(t => t.Code == 2 && Equals(t.Value, TxBlock)));
                    var tags = target.Tags.ToList();
                    int at = tags.FindIndex(t => t.Code == 1001 && Equals(t.Value, "ACAD"));
                    int end = tags.FindIndex(at + 1, t => t.Code == 1001);
                    tags.RemoveRange(at + 1, end - at - 1);
                    tags.InsertRange(at + 1, new[] {
                        new DxfTag(1002, "{"), new DxfTag(1000, "DesignCenter Data"), new DxfTag(1002, "{"),
                        new DxfTag(1070, (short)1), new DxfTag(1070, (short)7), new DxfTag(1002, "}"), new DxfTag(1002, "}") });
                    raw = raw.WithRecord(target, missing ? tags.Where(t => t.Code != 70) : tags);
                    using var input = new MemoryStream(SaveRaw(raw, binary));
                    var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Nested lookalike load");
                    Equal(missing ? DrawingUnits.Unitless : DrawingUnits.Millimeters, loaded.Blocks[TxBlock].Record.Units,
                        "Nested DesignCenter marker acquired semantic meaning");
                });
            Run($"table-xdata/native-priority/{version}/{binary}", () =>
            {
                var doc = TxDocument(version, 1, out _, out _);
                var raw = LoadRaw(TxSave(doc, binary, 1));
                var target = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "BLOCK_RECORD" &&
                    r.Tags.Any(t => t.Code == 2 && Equals(t.Value, TxBlock)));
                raw = raw.WithRecord(target, target.Tags.Select(t => t.Code == 70 ? new DxfTag(70, (short)6) : t));
                using var input = new MemoryStream(SaveRaw(raw, binary));
                Equal(DrawingUnits.Meters, DxfDocument.Load(input)!.Blocks[TxBlock].Record.Units, "Native group 70 precedence");
            });
        }
        foreach (bool binary in new[] { false, true }) foreach (string description in new[] { "", " ", "  ", "\t", "Łab_Ø", "unchanged" })
            Run($"table-xdata/clone-and-create/{binary}/{description}", () =>
            {
                var layer = new Layer(TxLayer) { Description = description, Transparency = new Transparency(37) };
                var clone = (Layer)layer.Clone(); Equal(description, clone.Description, "Detached clone lost description");
                var doc = new DxfDocument(); doc.Layers.Add(clone); var unchanged = TxSnapshot(clone);
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "New layer save"); unchanged();
                Check(!clone.XData.ContainsAppId("AcAecLayerStandard") && !clone.XData.ContainsAppId("AcCmTransparency"), "Generated source apps attached");
                output.Position = 0; var loaded = DxfDocument.Load(output)!;
                Equal(description, loaded.Layers[TxLayer].Description, "New layer description load");
            });
        foreach (bool binary in new[] { false, true }) foreach (bool clone in new[] { false, true })
            Run($"table-xdata/raw-layer/{binary}/{clone}", () =>
            {
                var doc = new DxfDocument(); var layer = new Layer(TxLayer);
                TxAttach(doc, layer, "AcAecLayerStandard", TxDescription("raw-only"));
                if (clone) layer = (Layer)layer.Clone();
                doc.Layers.Add(layer); var unchanged = TxSnapshot(layer);
                using var output = new MemoryStream(); Check(doc.Save(output, binary), "Raw-only layer save"); unchanged();
                output.Position = 0; var loaded = DxfDocument.Load(output)!;
                Equal("raw-only", loaded.Layers[TxLayer].Description, "Default property erased raw-only description");
                loaded.Layers[TxLayer].Description = null!;
                using var cleared = new MemoryStream(); Check(loaded.Save(cleared, binary), "Explicit null clear"); cleared.Position = 0;
                Equal("", DxfDocument.Load(cleared)!.Layers[TxLayer].Description, "Explicit clear was resurrected");
            });
        var invalid = new XDataRecord[][]
        {
            new[] { DxString("DesignCenter Data") },
            new[] { DxString("DesignCenter Data"), DxOpen, DxId(1), DxReal(4), DxClose },
            new[] { DxString("DesignCenter Data"), DxOpen, DxString("1"), DxId(4), DxClose },
            new[] { DxString("DesignCenter Data"), DxOpen, DxId(1), DxId(4) },
            new[] { DxClose },
            new[] { DxString("DesignCenter Data"), DxOpen, DxId(1), DxId(4), DxClose,
                    DxString("DesignCenter Data"), DxOpen, DxId(1), DxId(4), DxClose }
        };
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"table-xdata/append-units/{version}/{binary}", () =>
            {
                var doc = TxDocument(version, 1, out _, out var record);
                // An existing unrelated ACAD list must survive canonical subsection creation.
                var opaque = new[] { DxString("PRIVATE"), DxOpen, DxId(42), DxClose,
                    new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 255, 42 }) };
                record.XData["ACAD"].XDataRecord.Clear(); record.XData["ACAD"].XDataRecord.AddRange(opaque);
                using var input = new MemoryStream(TxSave(doc, binary, 1));
                var loaded = DxfDocument.Load(input)!;
                TxRecords(opaque.Concat(new[] { DxString("DesignCenter Data"), DxOpen, DxId(1), DxId(4), DxClose }),
                    loaded.Blocks[TxBlock].Record.XData["ACAD"].XDataRecord);
                Equal(DrawingUnits.Millimeters, loaded.Blocks[TxBlock].Record.Units, "Appended units");
            });
            for (int variant = 0; variant < invalid.Length; variant++)
            {
                int v = variant;
                Run($"table-xdata/reject-input/{version}/{binary}/{v}", () =>
                {
                    var doc = TxDocument(version, 1, out _, out _);
                    var raw = LoadRaw(TxSave(doc, binary, 1));
                    var target = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "BLOCK_RECORD" &&
                        r.Tags.Any(t => t.Code == 2 && Equals(t.Value, TxBlock)));
                    var tags = target.Tags.Where(t => t.Code != 70).ToList();
                    int at = tags.FindIndex(t => t.Code == 1001 && Equals(t.Value, "ACAD"));
                    int end = tags.FindIndex(at + 1, t => t.Code == 1001);
                    tags.RemoveRange(at + 1, end - at - 1);
                    tags.InsertRange(at + 1, invalid[v].Select(r => new DxfTag((short)r.Code, r.Value)));
                    raw = raw.WithRecord(target, tags);
                    using var input = new MemoryStream(SaveRaw(raw, binary)); bool rejected;
                    try { rejected = DxfDocument.Load(input) == null; }
                    catch (FormatException) { rejected = true; }
                    Check(rejected, "Malformed legacy units input accepted");
                    Check(input.CanRead, "Rejected legacy input closed caller stream");
                });
            }
        }
        foreach (bool binary in new[] { false, true })
            Run($"table-xdata/append-layer-slots/{binary}", () =>
            {
                var doc = new DxfDocument();
                var layer = new Layer(TxLayer) { Description = "new", Transparency = new Transparency(37) };
                TxAttach(doc, layer, "AcAecLayerStandard", new[] { DxId(17), DxReal(-0.0) });
                TxAttach(doc, layer, "AcCmTransparency", new[] { DxString("opaque"), DxReal(1e-13) });
                doc.Layers.Add(layer); var unchanged = TxSnapshot(layer);
                using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Append layer slots save"); unchanged();
                stream.Position = 0; var result = DxfDocument.Load(stream)!.Layers[TxLayer];
                TxRecords(new[] { DxId(17), DxReal(-0.0), DxString(""), DxString("new") },
                    result.XData["AcAecLayerStandard"].XDataRecord);
                TxRecords(new[] { DxString("opaque"), DxReal(1e-13), new XDataRecord(XDataCode.Int32, 0x020000A0) },
                    result.XData["AcCmTransparency"].XDataRecord);
                Equal("new", result.Description, "Appended layer description");
                Equal((short)37, result.Transparency.Value, "Appended layer alpha");
            });
        for (int variant = 0; variant < invalid.Length; variant++) foreach (bool binary in new[] { false, true })
        {
            int v = variant;
            Run($"table-xdata/reject/{v}/{binary}", () =>
            {
                var doc = TxDocument(DxfVersion.AutoCad2018, 1, out _, out var record);
                record.XData["ACAD"].XDataRecord.Clear(); record.XData["ACAD"].XDataRecord.AddRange(invalid[v]);
                var unchanged = TxSnapshot(record);
                using var output = new MemoryStream(); bool rejected;
                try { rejected = !doc.Save(output, binary); } catch (FormatException) { rejected = true; }
                Check(rejected, "Malformed DesignCenter list saved"); unchanged();
                Check(output.CanWrite, "Failed save closed caller stream");
            });
        }
    }
}
