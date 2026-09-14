using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMTextColumnTests()
    {
        Run("mtext/columns/model-validation", MTextColumnsModelValidation);
        Run("mtext/columns/clone-conversion", MTextColumnsCloneConversion);
        Run("mtext/columns/transform-guards", MTextColumnsTransforms);
        Run("mtext/columns/block-clone-ownership", MTextColumnsBlockClone);
        Run("mtext/columns/write-graph-guards", MTextColumnsWriteGuards);
        Run("mtext/columns/embedded-cache-and-validation", MTextColumnsEmbeddedMetadata);
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int mode in new[] { 0, 1, 2 })
                {
                    int m = mode;
                    Run($"mtext/columns/authored-wire/{v}/{b}/{m}", () => MTextColumnsWire(v, b, m));
                    Run($"mtext/columns/model-roundtrip/{v}/{b}/{m}", () => MTextColumnsModelRoundTrip(v, b, m));
                    if (version >= DxfVersion.AutoCad2007)
                        {
                        Run($"mtext/columns/direct/{v}/{b}/{m}", () => MTextColumnsDirect(v, b, m));
                        Run($"mtext/columns/direct-authored/{v}/{b}/{m}", () => MTextColumnsDirectWire(v, b, m));
                    }
                }
                Run($"mtext/columns/version-gate/{v}/{b}", () => MTextColumnsVersionGate(v, b));
                Run($"mtext/columns/defined-height/{v}/{b}", () => MTextColumnsDefinedHeight(v, b));
                foreach (int malformed in Enumerable.Range(0, version >= DxfVersion.AutoCad2018 ? 19 : 7))
                {
                    int bad = malformed;
                    Run($"mtext/columns/malformed/{v}/{b}/{bad}", () => MTextColumnsMalformed(v, b, bad));
                }
            }
        string independent = Environment.GetEnvironmentVariable("DXF_INDEPENDENT_FIXTURES") ?? Path.Combine("tests", "fixtures", "mtext-columns");
        Run("mtext/columns/independent-corpus", () => Equal(10, Directory.GetFiles(independent, "independent-mtext-*.dxf").Length, "Committed external corpus size"));
        Run("mtext/columns/conflicting-duplicate-wire", MTextColumnsConflictingDuplicate);
        foreach (string path in Directory.GetFiles(independent, "independent-mtext-*.dxf"))
            {
                string file = path;
                Run("mtext/columns/independent/" + Path.GetFileName(file), () => MTextColumnsIndependent(file));
            }
    }

    private static MTextColumns ColumnModel(int mode, MTextColumnStorage storage)
    {
        var columns = new MTextColumns { Type = mode == 0 ? MTextColumnType.Static : MTextColumnType.Dynamic,
            Storage = storage, Count = 3, AutoHeight = mode == 1, FlowReversed = true,
            Width = 12.5, Gutter = 1.75, DefinedHeight = mode == 2 ? 0 : 20.25, TotalHeight = mode == 2 ? 28.75 : 20.25 };
        if (mode == 2) foreach (double h in new[] { 20.25, 28.75, 0.0 }) columns.Heights.Add(h);
        return columns;
    }

    private static void CheckColumns(MText text, int mode)
    {
        MTextColumns c = text.Columns ?? throw new InvalidOperationException("Column definition was lost.");
        Equal(mode == 0 ? MTextColumnType.Static : MTextColumnType.Dynamic, c.Type, "column type");
        Equal(3, c.Count, "column count"); Equal(mode == 1, c.AutoHeight, "auto height");
        Near(12.5, c.Width, "column width"); Near(1.75, c.Gutter, "column gutter"); Near(41, c.TotalWidth, "total width");
        Near(mode == 2 ? 0 : 20.25, c.DefinedHeight, "defined height");
        Equal(mode == 2 ? 3 : 0, c.Heights.Count, "height count");
        if (mode == 2) { Near(20.25, c.Heights[0], "height 0"); Near(28.75, c.Heights[1], "height 1"); Near(0, c.Heights[2], "last height"); }
        c.Validate();
    }

    private static void MTextColumnsModelValidation()
    {
        Check(new MText().Columns == null, "Invented default columns.");
        var c = ColumnModel(0, MTextColumnStorage.Embedded);
        foreach (double bad in new[] { -1.0, 0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => c.Width = bad);
        foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Throws<ArgumentOutOfRangeException>(() => c.Gutter = bad);
            Throws<ArgumentOutOfRangeException>(() => c.DefinedHeight = bad);
            Throws<ArgumentOutOfRangeException>(() => c.TotalHeight = bad);
            Throws<ArgumentOutOfRangeException>(() => new MText().DefinedHeight = bad);
        }
        foreach (int bad in new[] { -1, 0, 32768, int.MaxValue }) Throws<ArgumentOutOfRangeException>(() => c.Count = bad);
        Throws<ArgumentOutOfRangeException>(() => c.Type = (MTextColumnType)3);
        Throws<ArgumentOutOfRangeException>(() => c.Storage = (MTextColumnStorage)3);
        c.AutoHeight = true; Throws<InvalidOperationException>(() => c.Validate()); c.AutoHeight = false;
        c.Heights.Add(1); Throws<InvalidOperationException>(() => c.Validate()); c.Heights.Clear();
        c = ColumnModel(2, MTextColumnStorage.Embedded); c.Heights[0] = double.NaN;
        Throws<ArgumentOutOfRangeException>(() => c.Validate()); c.Heights[0] = 0;
        Throws<ArgumentOutOfRangeException>(() => c.Validate()); c.Heights[0] = 20.25;
        c.Heights.RemoveAt(2); Throws<InvalidOperationException>(() => c.Validate());
    }

    private static void MTextColumnsCloneConversion()
    {
        var source = new MText("FIRSTSECONDTHIRD", new Vector3(17, -11, 3), 2.5) { Rotation = 23, Columns = ColumnModel(2, MTextColumnStorage.Embedded) };
        var legacy = source.ConvertToLinkedColumns(new[] { "FIRST", "SECOND", "THIRD" });
        Equal(3, legacy.Count, "converted entities"); Equal("FIRSTSECONDTHIRD", source.Value, "source modified");
        Equal(2, legacy[0].Columns!.LinkedColumns.Count, "links"); Equal("SECOND", legacy[1].Value, "partition");
        Check(legacy.All(c => c.Owner == null && c.Handle == null), "Conversion returned owned entities.");
        var cloned = (MText)legacy[0].Clone();
        Check(!ReferenceEquals(cloned.Columns!.LinkedColumns[0], legacy[1]), "Clone shared linked entity.");
        cloned.Columns.LinkedColumns[0].Value = "changed"; cloned.Columns.Heights[0] = 100;
        Equal("SECOND", legacy[1].Value, "Clone mutated linked text"); Near(20.25, legacy[0].Columns!.Heights[0], "Clone mutated heights");
        MText embedded = legacy[0].ConvertToEmbeddedColumns(); Equal(source.Value, embedded.Value, "lossless recombination");
        Equal(MTextColumnStorage.Embedded, embedded.Columns!.Storage, "embedded storage"); Equal(0, embedded.Columns.LinkedColumns.Count, "embedded links");
        Throws<ArgumentException>(() => source.ConvertToLinkedColumns(new[] { "FIRST", "SECOND", "wrong" }));
        Throws<ArgumentException>(() => source.ConvertToLinkedColumns(new[] { source.Value }));
        Throws<ArgumentNullException>(() => source.ConvertToLinkedColumns(null!));
        var cycle = ColumnModel(0, MTextColumnStorage.LegacyLinked); cycle.LinkedColumns.Add(new MText { Columns = cycle });
        Throws<InvalidOperationException>(() => cycle.Clone());
    }

    private static void MTextColumnsTransforms()
    {
        var text = new MText("abcdef", Vector3.Zero, 2) { Columns = ColumnModel(2, MTextColumnStorage.Embedded) };
        text.TransformBy(Matrix3.Scale(2), new Vector3(1, 2, 3));
        Near(25, text.Columns!.Width, "scaled width"); Near(57.5, text.Columns.Heights[1], "scaled manual height");
        Near(4, text.Height, "scaled text"); Equal(new Vector3(1, 2, 3), text.Position, "translated position");
        Vector3 position = text.Position;
        Throws<NotSupportedException>(() => text.TransformBy(Matrix3.Scale(2, 1, 1), Vector3.Zero));
        Throws<NotSupportedException>(() => text.TransformBy(Matrix3.Scale(-1, 1, 1), Vector3.Zero));
        Equal(position, text.Position, "Rejected transform changed position");
        var linked = text.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" });
        Throws<NotSupportedException>(() => linked[0].TransformBy(Matrix3.Identity, Vector3.UnitX));
    }

    private static void MTextColumnsBlockClone()
    {
        var original = new MText("abcdef") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
        var legacy = original.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" });
        var block = new Block("Columns"); block.Entities.AddRange(legacy);
        var copy = (Block)block.Clone("ColumnsCopy");
        MText copied = copy.Entities.OfType<MText>().Single(x => x.Columns != null);
        foreach (MText linked in copied.Columns!.LinkedColumns)
        {
            Check(linked.Owner == copy && copy.Entities.Contains(linked), "Block clone links are not owned clone entities.");
            Check(!legacy.Contains(linked), "Block clone reused source entity.");
        }
        var insertCopy = (Insert)new Insert(block).Clone();
        MText copiedFromInsert = insertCopy.Block.Entities.OfType<MText>().Single(x => x.Columns != null);
        Check(copiedFromInsert.Columns!.LinkedColumns.All(x => x.Owner == insertCopy.Block), "INSERT clone broke column ownership.");
        Throws<NotSupportedException>(() => new Insert(block).Explode());
        var doc = new DxfDocument(DxfVersion.AutoCad2013); doc.Entities.Add(new Insert(copy));
        using var output = new MemoryStream(); Check(doc.Save(output), "Cloned column block did not save.");
        var sourceDoc = new DxfDocument(DxfVersion.AutoCad2013); sourceDoc.Entities.Add(original.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" }));
        Block created = Block.Create(sourceDoc, "CreatedColumns");
        Check(created.Entities.OfType<MText>().Single(x => x.Columns != null).Columns!.LinkedColumns.All(x => x.Owner == created), "Block.Create broke ownership.");
        Check(block.Save(Path.Combine(ArtifactDirectory, "mtext-columns-block-save.dxf"), DxfVersion.AutoCad2013, false), "Block.Save broke ownership.");
        legacy[1].Height *= 2;
        Throws<NotSupportedException>(() => legacy[0].ConvertToEmbeddedColumns());
    }

    private static void MTextColumnsWriteGuards()
    {
        for (int scenario = 0; scenario < 5; scenario++)
        {
            var text = new MText("abcdef") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
            var linked = text.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" });
            var doc = new DxfDocument(DxfVersion.AutoCad2013);
            if (scenario == 0)
            {
                doc.Entities.Add(linked[0]); var another = new Block("Another"); another.Entities.Add(linked[1]); another.Entities.Add(linked[2]); doc.Blocks.Add(another);
            }
            else
            {
                doc.Entities.Add(linked);
                if (scenario == 1) linked[0].Columns!.LinkedColumns.RemoveAt(1);
                if (scenario == 2) linked[0].Columns!.LinkedColumns[1] = linked[1];
                if (scenario == 3) linked[0].Columns!.LinkedColumns[1] = linked[0];
                if (scenario == 4)
                {
                    var raw = new XData(ApplicationRegistry.Default); raw.XDataRecord.Add(new XDataRecord(XDataCode.String, "ACAD_MTEXT_COLUMN_INFO_BEGIN"));
                    linked[0].XData.Add(raw);
                }
            }
            using var output = new MemoryStream();
#if DEBUG
            Throws<InvalidOperationException>(() => doc.Save(output));
#else
            Check(!doc.Save(output), "Invalid column graph was saved.");
#endif
            Equal(0L, output.Length, "Invalid graph wrote partial bytes");
        }
    }

    private static void MTextColumnsEmbeddedMetadata()
    {
        var c = ColumnModel(0, MTextColumnStorage.Embedded);
        c.EmbeddedInsertionPoint = new Vector3(9, 8, 7); c.EmbeddedTextDirection = new Vector3(0, 1, 0);
        c.EmbeddedReferenceWidth = 99; c.StoredTotalWidth = 123;
        var text = new MText("embedded") { Columns = c, Position = new Vector3(1, 2, 3) };
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(text);
        using var output = new MemoryStream(); Check(doc.Save(output), "embedded metadata save"); output.Position = 0;
        MText loaded = (DxfDocument.Load(output) ?? throw new InvalidOperationException("embedded metadata load")).Entities.MTexts.Single();
        Equal(text.Position, loaded.Position, "Duplicate fields overwrote primary placement");
        Equal(c.EmbeddedInsertionPoint, loaded.Columns!.EmbeddedInsertionPoint, "Embedded placement lost");
        Equal(c.EmbeddedTextDirection, loaded.Columns.EmbeddedTextDirection, "Embedded direction lost");
        Equal((double?)99, loaded.Columns.EmbeddedReferenceWidth, "Embedded reference width lost");
        Equal((double?)123, loaded.Columns.StoredTotalWidth, "Embedded total width lost");
        loaded.Columns.Width = 15; Check(loaded.Columns.StoredTotalWidth == null, "Width edit retained stale total width.");
        loaded.TransformBy(Matrix3.Scale(2), Vector3.Zero);
        Check(loaded.Columns.EmbeddedInsertionPoint == null && loaded.Columns.EmbeddedTextDirection == null, "Transform retained stale duplicates.");
        c.EmbeddedTextDirection = Vector3.Zero;
        Throws<ArgumentOutOfRangeException>(() => c.Validate());
        using var invalidDirectionOutput = new MemoryStream();
#if DEBUG
        Throws<ArgumentOutOfRangeException>(() => doc.Save(invalidDirectionOutput));
#else
        Check(!doc.Save(invalidDirectionOutput), "An all-zero embedded direction was written.");
#endif
        Equal(0L, invalidDirectionOutput.Length, "Invalid embedded direction wrote partial bytes");
        c.EmbeddedTextDirection = Vector3.UnitX;
        c.EmbeddedInsertionPoint = new Vector3(double.NaN, 0, 0); Throws<ArgumentOutOfRangeException>(() => c.Validate());
    }

    private static MemoryStream ColumnsWireFixture(DxfVersion version, bool binary, int mode, int malformed = -1)
    {
        var stream = new MemoryStream(); object writer = NewCodeWriter(stream, binary);
        void T(short code, object value) => Invoke(writer, "Write", code, value);
        string id = version switch { DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018", DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", _ => "AC1032" };
        T(0, "SECTION"); T(2, "HEADER"); T(9, "$ACADVER"); T(1, id); T(9, "$HANDSEED"); T(5, "1000"); T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252"); T(0, "ENDSEC");
        T(0, "SECTION"); T(2, "ENTITIES");
        void Entity(string handle, string text, double x)
        {
            T(0, "MTEXT"); T(5, handle); T(100, "AcDbEntity"); T(8, "0"); T(100, "AcDbMText");
            T(10, x); T(20, -11.0); T(30, 3.0); T(40, 2.5); T(41, 12.5); T(50, 23.0); T(71, (short)2); T(72, (short)3); T(1, text);
        }
        Entity("200", "FIRST", 17);
        double width = malformed == 0 ? -1 : 12.5;
        int count = malformed == 1 ? -1 : 3;
        short flag = (short)(malformed == 2 ? 2 : mode == 1 ? 1 : 0);
        if (version >= DxfVersion.AutoCad2018)
        {
            T(101, "Embedded Object"); T(70, (short)1);
            // Repeated embedded coordinates must never overwrite entity placement/direction/height.
            if (malformed != 12) T(10, malformed == 18 ? 0.0 : 0.9);
            if (malformed != 13) T(20, malformed == 18 ? 0.0 : 0.1);
            if (malformed != 14) T(30, 0.0);
            if (malformed != 15) T(11, 17.0);
            if (malformed != 16) T(21, -11.0);
            if (malformed != 17) T(31, 3.0);
            T(40, 99.0); T(41, mode == 2 ? 0.0 : 20.25); T(42, malformed == 3 ? 40.0 : 41.0); T(43, mode == 2 ? 28.75 : 20.25);
            T(71, (short)(mode == 0 ? 1 : 2)); T(72, (short)(mode == 1 && malformed != 1 ? 0 : count));
            T(44, malformed == 9 ? double.NaN : width); T(45, 1.75); if (malformed != 7) T(73, flag); T(74, (short)1);
            if (malformed == 8) T(41, 0.0);
            if (malformed == 10) T(42, double.NaN);
            if (malformed == 11) T(72, (short)2);
            if (mode == 2) { T(46, malformed == 4 ? -2.0 : 20.25); T(46, 28.75); if (malformed != 5) T(46, 0.0); }
            if (malformed == 6) { T(101, "Embedded Object"); T(70, (short)1); }
        }
        else
        {
            T(1001, "ACAD"); T(1000, "unrelated-before"); T(1000, "ACAD_MTEXT_COLUMN_INFO_BEGIN");
            void F(short code, object value) { T(1070, code); T(value is short ? (short)1070 : (short)1040, value); }
            F(75, (short)(mode == 0 ? 1 : 2)); F(79, flag); F(76, (short)count); F(78, (short)1); F(48, width); F(49, 1.75);
            if (mode == 2) { F(50, (short)3); T(1040, malformed == 4 ? -2.0 : 20.25); T(1040, 28.75); if (malformed != 5) T(1040, 0.0); }
            T(1000, "ACAD_MTEXT_COLUMN_INFO_END"); T(1000, "ACAD_MTEXT_COLUMNS_BEGIN"); F(47, (short)3);
            T(1005, malformed == 3 ? "DEAD" : "201"); T(1005, malformed == 6 ? "201" : "202"); T(1000, "ACAD_MTEXT_COLUMNS_END");
            if (mode != 2) { T(1000, "ACAD_MTEXT_DEFINED_HEIGHT_BEGIN"); F(46, 20.25); T(1000, "ACAD_MTEXT_DEFINED_HEIGHT_END"); }
            T(1000, "unrelated-after");
        }
        T(1001, "COLUMN_TEST"); T(1000, "following-column-metadata");
        T(0, "LINE"); T(5, "203"); T(100, "AcDbEntity"); T(8, "0"); T(100, "AcDbLine"); T(10, 1.0); T(20, 2.0); T(30, 0.0); T(11, 3.0); T(21, 4.0); T(31, 0.0);
        if (version < DxfVersion.AutoCad2018)
        {
            // Linked entities need not appear immediately after the main entity or in handle order.
            Entity("202", "THIRD", 45.5); Entity("201", "SECOND", 31.25);
        }
        T(0, "ENDSEC"); T(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0; return stream;
    }

    private static void MTextColumnsWire(DxfVersion version, bool binary, int mode)
    {
        using var stream = ColumnsWireFixture(version, binary, mode);
        DxfDocument doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Authored columns failed to load.");
        var main = (MText)doc.GetObjectByHandle("200"); CheckColumns(main, mode);
        Check(main.Columns!.FlowReversed, "reversed flow"); Equal(new Vector3(17, -11, 3), main.Position, "placement"); Near(2.5, main.Height, "text height");
        Near(23, main.Rotation, "rotation"); Equal(MTextDrawingDirection.TopToBottom, main.DrawingDirection, "drawing direction");
        Equal(MTextAttachmentPoint.TopCenter, main.AttachmentPoint, "attachment");
        Equal("following-column-metadata", main.XData["COLUMN_TEST"].XDataRecord[0].Value, "following XData");
        Equal(1, doc.Entities.Lines.Count(), "following entity");
        if (version < DxfVersion.AutoCad2018)
        {
            Equal("SECOND", main.Columns.LinkedColumns[0].Value, "link order 0"); Equal("THIRD", main.Columns.LinkedColumns[1].Value, "link order 1");
            Equal(2, main.XData["ACAD"].XDataRecord.Count, "unrelated ACAD records");
        }
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Column save failed."); output.Position = 0;
        DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored columns failed roundtrip.");
        CheckColumns((MText)loaded.GetObjectByHandle("200"), mode);
    }

    private static void MTextColumnsModelRoundTrip(DxfVersion version, bool binary, int mode)
    {
        var main = new MText("FIRSTSECONDTHIRD", new Vector3(17, -11, 3), 2.5) { Columns = ColumnModel(mode, MTextColumnStorage.Embedded) };
        var doc = new DxfDocument(version);
        if (version < DxfVersion.AutoCad2018) doc.Entities.Add(main.ConvertToLinkedColumns(new[] { "FIRST", "SECOND", "THIRD" }));
        else doc.Entities.Add(main);
        string path = Path.Combine(ArtifactDirectory, $"mtext-columns-{version}-{binary}-{mode}.dxf");
        Check(doc.Save(path, binary), "model save failed");
        var loaded = DxfDocument.Load(path) ?? throw new InvalidOperationException("model load failed");
        CheckColumns(loaded.Entities.MTexts.Single(x => x.Columns != null), mode);
    }

    private static void MTextColumnsDirect(DxfVersion version, bool binary, int mode)
    {
        var text = new MText("direct") { Rotation = 37, Columns = ColumnModel(mode, MTextColumnStorage.Direct) };
        var doc = new DxfDocument(version); doc.Entities.Add(text);
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "direct save"); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("direct load");
        CheckColumns(loaded.Entities.MTexts.Single(), mode); Near(37, loaded.Entities.MTexts.Single().Rotation, "group 50 heights corrupted direction");
    }

    private static void MTextColumnsDirectWire(DxfVersion version, bool binary, int mode)
    {
        var tags = new List<(short Code, object Value)> { (50, 37.0), (46, mode == 2 ? 0.0 : 20.25),
            (75, (short)(mode == 0 ? 1 : 2)), (76, (short)3), (78, (short)1), (79, (short)(mode == 1 ? 1 : 0)),
            (48, 12.5), (49, 1.75) };
        if (mode == 2) { tags.Add((50, 3.0)); tags.Add((50, 20.25)); tags.Add((50, 28.75)); tags.Add((50, 0.0)); }
        using var fixture = BackgroundFixture(version, binary, tags);
        var doc = DxfDocument.Load(fixture) ?? throw new InvalidOperationException("Authored direct columns failed to load.");
        MText text = doc.Entities.MTexts.Single(); CheckColumns(text, mode); Near(37, text.Rotation, "Direct height codes consumed common rotation");
        Equal(MTextDrawingDirection.TopToBottom, text.DrawingDirection, "Following common field lost.");
    }

    private static void MTextColumnsVersionGate(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var text = new MText("abcdef") { Columns = ColumnModel(0, MTextColumnStorage.Embedded) };
        if (version >= DxfVersion.AutoCad2018) doc.Entities.Add(text.ConvertToLinkedColumns(new[] { "ab", "cd", "ef" }));
        else doc.Entities.Add(text);
        using var output = new MemoryStream();
#if DEBUG
        Throws<NotSupportedException>(() => doc.Save(output, binary));
#else
        Check(!doc.Save(output, binary), "Incompatible column version was saved.");
#endif
        Equal(0L, output.Length, "Version gate wrote partial file");
    }

    private static void MTextColumnsDefinedHeight(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.Entities.Add(new MText("defined") { DefinedHeight = 19.75 });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "height save"); output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("height load");
        Equal((double?)19.75, loaded.Entities.MTexts.Single().DefinedHeight, "standalone defined height");
        Check(loaded.Entities.MTexts.Single().Columns == null, "height invented columns");
    }

    private static void MTextColumnsMalformed(DxfVersion version, bool binary, int malformed)
    {
        int mode = malformed == 3 && version >= DxfVersion.AutoCad2018 ? 1 : 2;
        using var fixture = ColumnsWireFixture(version, binary, mode, malformed);
        bool rejected = false;
        try { rejected = DxfDocument.Load(fixture) == null; }
        catch (Exception exception) when (exception is ArgumentException || exception is FormatException || exception is InvalidDataException || exception is InvalidOperationException) { rejected = true; }
        Check(rejected, "Malformed column data was accepted.");
    }

    private static void MTextColumnsConflictingDuplicate()
    {
        string path = Path.Combine("tests", "fixtures", "mtext-columns", "conflicting", "independent-mtext-static-R2018.dxf");
        DxfDocument doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Conflicting duplicate source failed.");
        MText main = doc.Entities.MTexts.Single(); Near(23, main.Rotation, "Common rotation precedence");
        Equal((Vector3?)Vector3.UnitX, main.Columns!.EmbeddedTextDirection, "Independent duplicate source direction");
        using var output = new MemoryStream(); Check(doc.Save(output), "Conflicting duplicate save"); output.Position = 0;
        MText loaded = (DxfDocument.Load(output) ?? throw new InvalidOperationException("Conflicting duplicate reload")).Entities.MTexts.Single();
        Near(23, loaded.Rotation, "Canonical main direction changed");
        Equal(main.Columns.EmbeddedTextDirection, loaded.Columns!.EmbeddedTextDirection, "Independent duplicate direction was lost");
    }

    private static void MTextColumnsIndependent(string path)
    {
        DxfDocument doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Independent fixture failed to load.");
        MText main = doc.Entities.MTexts.Single(x => x.Columns != null);
        int mode = path.Contains("dynamic_auto") ? 1 : path.Contains("dynamic_manual") ? 2 : 0;
        CheckColumns(main, mode);
        Check(main.Columns!.FlowReversed, "Independent fixture reversed flow was lost.");
        Near(23, main.Rotation, "Independent fixture direction");
        Near(1.25, main.Height, "Independent fixture character height");
        Equal("after embedded object", main.XData["QA_AFTER_COLUMNS"].XDataRecord[0].Value, "Independent trailing XDATA");
        Equal(new Vector3(101, 102, 103), doc.Entities.Lines.Single().StartPoint, "Independent following entity");
        for (int i = 0; i < 2; i++)
        {
            string output = Path.Combine(ArtifactDirectory, Path.GetFileNameWithoutExtension(path) + (i == 0 ? "-text.dxf" : "-binary.dxf"));
            Check(doc.Save(output, i != 0), "independent output save failed");
            var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Independent roundtrip failed");
            CheckColumns(loaded.Entities.MTexts.Single(x => x.Columns != null), mode);
        }
    }
}
