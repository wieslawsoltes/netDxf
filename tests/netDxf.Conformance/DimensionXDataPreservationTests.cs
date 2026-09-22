// Copyright (c) netDxf contributors. Licensed under the MIT License.
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
    private static XDataRecord DxString(string value) => new(XDataCode.String, value);
    private static XDataRecord DxId(short value) => new(XDataCode.Int16, value);
    private static XDataRecord DxReal(double value) => new(XDataCode.Real, value);
    private static XDataRecord DxOpen => XDataRecord.OpenControlString;
    private static XDataRecord DxClose => XDataRecord.CloseControlString;

    private static EntityObject DxHost(int kind)
    {
        EntityObject host = kind == 9 ? new Tolerance { Style = new DimensionStyle("DXDATA_STYLE") }
            : kind == 8 ? new Leader(new[] { Vector2.Zero, new Vector2(5, 3), new Vector2(9, 3) }, new DimensionStyle("DXDATA_STYLE"))
            : TextBlockDimension(kind);
        host.Layer = new Layer("DXDATA_HOST");
        if (host is Dimension dim) dim.UserText = "FIXED";
        return host;
    }

    private static XDataRecord[] DxPrefix(string handle) => new[]
    {
        DxString("BEFORE_Ł"), DxOpen, DxString("DSTYLE"), DxOpen, DxId(140), DxReal(99), DxClose, DxClose,
        new XDataRecord(XDataCode.BinaryData, new byte[] { 0, 255, 125, 10 }),
        new XDataRecord(XDataCode.DatabaseHandle, handle),
        new XDataRecord(XDataCode.RealX, 1e-13),
        new XDataRecord(XDataCode.RealY, BitConverter.Int64BitsToDouble(long.MinValue)),
        new XDataRecord(XDataCode.RealZ, 3.75)
    };
    private static XDataRecord[] DxSuffix() => new[] { DxString("AFTER"), DxOpen, DxString("tail"), new XDataRecord(XDataCode.Int32, 123456), DxClose };
    private static XDataRecord[] DxBody() => new[]
    {
        DxString("dStYlE"), DxOpen, DxId(43), DxReal(42), DxId(140), DxReal(1.25),
        DxId(999), DxString("PRIVATE"), DxId(144), DxReal(2), DxClose
    };
    private static void DxAttach(EntityObject host, string handle, IEnumerable<XDataRecord>? body = null)
    {
        var before = new XData(new ApplicationRegistry("DXDATA_BEFORE")); before.XDataRecord.Add(DxString("first")); host.XData.Add(before);
        var acad = new XData(new ApplicationRegistry("ACAD"));
        acad.XDataRecord.AddRange(DxPrefix(handle)); acad.XDataRecord.AddRange(body ?? DxBody()); acad.XDataRecord.AddRange(DxSuffix());
        host.XData.Add(acad);
        var after = new XData(new ApplicationRegistry("DXDATA_AFTER")); after.XDataRecord.Add(DxString("last")); host.XData.Add(after);
    }
    private static void DxSet(EntityObject host, int stage)
    {
        if (host is Tolerance t) t.TextHeight = stage == 0 ? 1.25 : stage == 1 ? 2.75 : 1;
        else
        {
            var overrides = ContainerOverrides(host); overrides.Clear();
            if (stage < 2) overrides.Add(DimensionStyleOverrideType.TextHeight, stage == 0 ? 1.25 : 2.75);
            if (stage == 0) overrides.Add(DimensionStyleOverrideType.DimScaleLinear, 2.0);
        }
    }
    private static DxfDocument DxDocument(DxfVersion version, int kind, int placement, out EntityObject host, bool attach = true)
    {
        var doc = new DxfDocument(version) { BuildDimensionBlocks = true }; doc.Comments.Clear();
        var sentinel = new Line(new Vector3(17.25, -4.5, 2), new Vector3(18.5, 9.25, -3)); doc.Entities.Add(sentinel);
        host = DxHost(kind); DxSet(host, 0); if (attach) DxAttach(host, sentinel.Handle);
        if (placement == 0) doc.Entities.Add(host);
        else if (placement == 1)
        {
            doc.Layouts.Add(new Layout("DXDATA_PAPER")); doc.Layouts["DXDATA_PAPER"].AssociatedBlock.Entities.Add(host);
        }
        else
        {
            var block = new Block("DXDATA_HOLDER", new[] { host });
            if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block);
        }
        return doc;
    }
    private static EntityObject DxFind(DxfDocument doc) => doc.Blocks.SelectMany(b => b.Entities).Single(e => e.Layer.Name == "DXDATA_HOST");
    private static void DxRecordEqual(XDataRecord expected, XDataRecord actual)
    {
        Equal(expected.Code, actual.Code, "XData code");
        if (expected.Value is double d) SameDoubleBits(d, (double)actual.Value, "XData scalar bits");
        else if (expected.Value is byte[] bytes) Check(bytes.SequenceEqual((byte[])actual.Value), "XData binary bytes");
        else Equal(expected.Value, actual.Value, "XData value");
    }
    private static Action DxSnapshot(EntityObject host)
    {
        var entries = host.XData.Values.ToArray();
        var records = entries.Select(e => e.XDataRecord.ToArray()).ToArray();
        var binary = records.SelectMany(a => a).Where(r => r.Value is byte[]).Select(r => ((byte[])r.Value).ToArray()).ToArray();
        int events = 0; host.XData.AddAppReg += (_, _) => events++; host.XData.RemoveAppReg += (_, _) => events++;
        return () =>
        {
            Equal(0, events, "Save fired XData registry events");
            Check(entries.SequenceEqual(host.XData.Values), "Save changed XData application identity/order");
            for (int a = 0; a < entries.Length; a++) Check(records[a].SequenceEqual(entries[a].XDataRecord), "Save mutated XData record identity/order");
            int i = 0; foreach (var r in records.SelectMany(a => a).Where(r => r.Value is byte[]))
                Check(binary[i++].SequenceEqual((byte[])r.Value), "Save changed binary payload");
        };
    }
    private static void DxCheck(DxfDocument doc, int kind, int stage, string handle)
    {
        var host = DxFind(doc); Equal(handle, host.Handle, "XData host identity"); Equal(DxHost(kind).GetType(), host.GetType(), "Host family");
        string sentinel = doc.Entities.Lines.Single().Handle;
        Check(new[] { "DXDATA_BEFORE", "ACAD", "DXDATA_AFTER" }.SequenceEqual(host.XData.AppIds), "Application order");
        Equal("first", (string)host.XData["DXDATA_BEFORE"].XDataRecord.Single().Value, "First application");
        Equal("last", (string)host.XData["DXDATA_AFTER"].XDataRecord.Single().Value, "Last application");
        var actual = host.XData["ACAD"].XDataRecord;
        var expected = new List<XDataRecord>(DxPrefix(sentinel));
        expected.AddRange(new[] { DxString("dStYlE"), DxOpen, DxId(43), DxReal(42) });
        if (stage < 2 || kind == 9) expected.AddRange(new[] { DxId(140), DxReal(stage == 0 ? 1.25 : stage == 1 ? 2.75 : 1) });
        expected.AddRange(new[] { DxId(999), DxString("PRIVATE") });
        if (stage == 0 || kind == 9) expected.AddRange(new[] { DxId(144), DxReal(2) });
        expected.Add(DxClose); expected.AddRange(DxSuffix());
        Equal(expected.Count, actual.Count, "Preserved ACAD record count");
        for (int i = 0; i < expected.Count; i++) DxRecordEqual(expected[i], actual[i]);
        if (host is Tolerance tolerance) SameDoubleBits(stage == 0 ? 1.25 : stage == 1 ? 2.75 : 1, tolerance.TextHeight, "Tolerance height");
        else
        {
            var overrides = ContainerOverrides(host); Equal(stage == 0 ? 2 : stage == 1 ? 1 : 0, overrides.Count, "Typed override removal");
            if (stage < 2) SameDoubleBits(stage == 0 ? 1.25 : 2.75, (double)overrides[DimensionStyleOverrideType.TextHeight].Value, "Real top-level height");
            if (stage == 0) SameDoubleBits(2, (double)overrides[DimensionStyleOverrideType.DimScaleLinear].Value, "Real top-level scale");
        }
        var clone = (EntityObject)host.Clone();
        clone.XData["ACAD"].XDataRecord.Clear(); Equal(expected.Count, actual.Count, "Clone XData isolation");
        if (host is Dimension dim)
        {
            var unchanged = DxSnapshot(host); dim.Update(); unchanged();
            Equal("FIXED", dim.Block.Entities.OfType<MText>().Single().Value, "Fixed dimension label");
        }
        Equal(0, doc.Objects.Validate().Count, "XData graph validation");
        RawLinePointBits(new Vector3(17.25, -4.5, 2), doc.Entities.Lines.Single().StartPoint);
    }
    private static byte[] DxSave(DxfDocument doc, bool binary)
    {
        var host = DxFind(doc); var unchanged = DxSnapshot(host);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "XData save"); unchanged(); Check(stream.CanWrite, "Save closed caller stream");
        return stream.ToArray();
    }
    private static void RegisterDimensionXDataPreservationTests()
    {
        for (int kind = 0; kind < 10; kind++) foreach (bool binary in new[] { false, true })
        foreach (bool rawOnly in new[] { false, true })
        {
            int k = kind;
            Run($"dimension-xdata/api/{k}/{binary}/{rawOnly}", () =>
            {
                var doc = DxDocument(DxfVersion.AutoCad2018, k, 0, out var host, !rawOnly);
                byte[] bytes = DxSave(doc, binary); // no ACAD input must not be mutated by generated XData
                using var input = new MemoryStream(bytes); var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("All-family XData load");
                if (!rawOnly) DxCheck(loaded, k, 0, host.Handle);
                else
                {
                    var copy = DxFind(loaded);
                    if (copy is Tolerance t) SameDoubleBits(1.25, t.TextHeight, "Generated tolerance height");
                    else Equal(2, ContainerOverrides(copy).Count, "Generated DSTYLE count");
                    DxSet(copy, 2); byte[] clear = DxSave(loaded, binary);
                    using var second = new MemoryStream(clear); var cleared = DxfDocument.Load(second) ?? throw new InvalidOperationException("Cleared XData load");
                    if (k != 9) { Equal(0, ContainerOverrides(DxFind(cleared)).Count, "Cleared overrides resurrected"); Check(!DxFind(cleared).XData.ContainsAppId("ACAD"), "Empty DSTYLE application retained"); }
                }
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        foreach (int kind in new[] { 0, 7, 8, 9 }) for (int placement = 0; placement < 4; placement++)
        {
            if (kind == 7 && version < DxfVersion.AutoCad2004) continue;
            int k = kind, p = placement;
            Run($"dimension-xdata/wire/{version}/{binary}/{k}/{p}", () =>
            {
                var doc = DxDocument(version, k, p, out var host); string handle = host.Handle;
                string stem = $"dimension-xdata-{version}-{binary}-{k}-{p}";
                byte[] bytes = DxSave(doc, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + "-source.dxf"), bytes);
                using var input = new MemoryStream(bytes); var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Mixed XData load");
                DxCheck(loaded, k, 0, handle);
                for (int stage = 1; stage <= 2; stage++)
                {
                    DxSet(DxFind(loaded), stage);
                    foreach (bool output in new[] { false, true })
                    {
                        byte[] edited = DxSave(loaded, output);
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, stem + $"-{stage}-{output}.dxf"), edited);
                        using var second = new MemoryStream(edited); var copy = DxfDocument.Load(second) ?? throw new InvalidOperationException("Edited XData load");
                        DxCheck(copy, k, stage, handle);
                        using var third = new MemoryStream(DxSave(copy, output)); var repeated = DxfDocument.Load(third) ?? throw new InvalidOperationException("Repeated XData load");
                        DxCheck(repeated, k, stage, handle);
                    }
                }
                Check(input.CanRead, "Read closed source stream");
            });
        }
        foreach (int kind in new[] { 0, 8, 9 }) foreach (bool binary in new[] { false, true })
        for (int variant = 0; variant < 3; variant++)
        {
            int k = kind, v = variant;
            Run($"dimension-xdata/opaque-only/{k}/{binary}/{v}", () =>
            {
                var doc = DxDocument(DxfVersion.AutoCad2018, k, 0, out var host, false); DxSet(host, 2);
                var body = v == 0 ? Array.Empty<XDataRecord>() : v == 1 ? new[] { DxString("DSTYLE"), DxOpen, DxId(999), DxString("PRIVATE"), DxClose }
                    : new[] { DxString("DSTYLE"), DxOpen, DxClose };
                DxAttach(host, doc.Entities.Lines.Single().Handle, body);
                using var input = new MemoryStream(DxSave(doc, binary));
                var loaded = DxfDocument.Load(input) ?? throw new InvalidOperationException("Opaque-only XData load");
                var copy = DxFind(loaded);
                if (k != 9) Equal(0, ContainerOverrides(copy).Count, "Nested lookalike was decoded as DSTYLE");
                else SameDoubleBits(1, ((Tolerance)copy).TextHeight, "Generated height after opaque-only list");
                var expected = new List<XDataRecord>(DxPrefix(doc.Entities.Lines.Single().Handle));
                if (v == 1 || k == 9)
                {
                    if (v != 0 || k != 9) // An absent real list is appended after all original ACAD records.
                    {
                        expected.Add(DxString("DSTYLE")); expected.Add(DxOpen);
                        if (v == 1) { expected.Add(DxId(999)); expected.Add(DxString("PRIVATE")); }
                        if (k == 9) { expected.Add(DxId(140)); expected.Add(DxReal(1)); }
                        expected.Add(DxClose);
                    }
                }
                expected.AddRange(DxSuffix());
                if (v == 0 && k == 9) expected.AddRange(new[] { DxString("DSTYLE"), DxOpen, DxId(140), DxReal(1), DxClose });
                var actual = copy.XData["ACAD"].XDataRecord;
                Equal(expected.Count, actual.Count, "Opaque-only record count");
                for (int i = 0; i < expected.Count; i++) DxRecordEqual(expected[i], actual[i]);
                using var second = new MemoryStream(DxSave(loaded, binary)); Check(DxfDocument.Load(second) != null, "Opaque-only repeated load");
            });
        }
        var badBodies = new[]
        {
            new[] { DxString("DSTYLE"), DxId(140), DxReal(1) },
            new[] { DxString("DSTYLE"), DxClose },
            new[] { DxString("DSTYLE"), DxOpen, DxId(140), DxReal(1) },
            new[] { DxString("DSTYLE"), DxOpen, DxId(140), DxClose },
            new[] { DxString("DSTYLE"), DxOpen, DxId(140), DxString("WRONG"), DxClose },
            new[] { DxString("DSTYLE"), DxOpen, DxId(140), DxReal(1), DxId(140), DxReal(2), DxClose },
            new[] { DxString("DSTYLE"), DxOpen, DxClose, DxString("DSTYLE"), DxOpen, DxClose },
            new[] { DxString("DSTYLE"), DxOpen, DxOpen, DxClose, DxClose },
            new[] { DxClose }, new[] { DxOpen },
            new[] { DxString("DSTYLE"), DxOpen, DxId(999), DxString("A"), DxId(999), DxString("B"), DxClose },
            new[] { DxString("DSTYLE"), DxOpen, new XDataRecord(XDataCode.Int32, 140), DxReal(1), DxClose }
        };
        foreach (int kind in new[] { 0, 8, 9 }) foreach (bool binary in new[] { false, true })
        for (int problem = 0; problem < badBodies.Length; problem++)
        {
            int k = kind, q = problem;
            Run($"dimension-xdata/malformed/{k}/{binary}/{q}", () =>
            {
                var doc = DxDocument(DxfVersion.AutoCad2018, k, 0, out var host);
                byte[] seed = DxSave(doc, binary);
                var acad = host.XData["ACAD"].XDataRecord; acad.Clear(); acad.AddRange(badBodies[q]); var unchanged = DxSnapshot(host);
                using var output = new MemoryStream(); bool rejected;
                try { rejected = !doc.Save(output, binary); } catch (FormatException) { rejected = true; }
                Check(rejected, "Malformed DSTYLE accepted by writer"); unchanged(); Check(output.CanWrite, "Failed save closed output");
                var raw = LoadRaw(seed); var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 8 && Equals(t.Value, "DXDATA_HOST")));
                var tags = record.Tags.ToList(); int at = tags.FindIndex(t => t.Code == 1001 && Equals(t.Value, "ACAD"));
                int end = tags.FindIndex(at + 1, t => t.Code == 1001); if (end < 0) end = tags.Count;
                tags.RemoveRange(at + 1, end - at - 1); tags.InsertRange(at + 1, badBodies[q].Select(r => new DxfTag((short)r.Code, r.Value)));
                using var input = new MemoryStream(SaveRaw(raw.WithRecord(record, tags), binary));
                try { rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
                Check(rejected, "Malformed DSTYLE accepted by reader"); Check(input.CanRead, "Failed load closed input");
            });
        }
        foreach (int kind in new[] { 0, 8, 9 })
            Run($"dimension-xdata/write-failure/{kind}", () =>
            {
                var doc = DxDocument(DxfVersion.AutoCad2018, kind, 0, out var host); var unchanged = DxSnapshot(host);
                using var output = new DxFailStream(); bool rejected;
                try { rejected = !doc.Save(output, false); } catch (IOException) { rejected = true; }
                Check(rejected, "Injected IO failure not reported"); unchanged();
            });
    }
    private sealed class DxFailStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Injected write failure");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("Injected write failure");
    }
}
