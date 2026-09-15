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
    private static readonly string[] LayerIndexNames = { "Alpha", "Alpha", "alpha", "東京", @"Literal\U+0041" };
    private static void RunLayerIndexTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"layer-index/authored/{version}/{binary}", () => LayerIndexAuthored(version, binary));
            Run($"layer-index/clone-erase/{version}/{binary}", () => LayerIndexCloneErase(version, binary));
            foreach (bool sourceBinary in new[] { false, true })
                Run($"layer-index/producer/{version}/{sourceBinary}/{binary}", () => LayerIndexProducer(version, sourceBinary, binary));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true }) foreach (int fault in Enumerable.Range(0, 16))
            Run($"layer-index/malformed/{version}/{binary}/{fault}", () => LayerIndexMalformed(version, binary, fault));
        foreach (bool binary in new[] { false, true }) foreach (int variant in Enumerable.Range(0, 6))
            Run($"layer-index/opaque/{binary}/{variant}", () => LayerIndexOpaque(binary, variant));
        foreach (int scenario in Enumerable.Range(0, 6)) Run($"layer-index/classes/{scenario}", () => LayerIndexClasses(scenario));
        foreach (int scenario in Enumerable.Range(0, 10)) Run($"layer-index/api/{scenario}", () => LayerIndexApi(scenario));
    }

    private static DxfDocument LayerIndexDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        doc.Layers.Add(new Layer("Alpha"));
        var a = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
        var b = new Line(new Vector3(-1, -2, -3), new Vector3(-4, -5, -6));
        doc.Entities.Add(a); doc.Entities.Add(b);
        var index = new DxfLayerIndex { Timestamp = 2451545.125 };
        var buffers = Enumerable.Range(0, 5).Select(_ => new DxfIdBuffer()).ToArray();
        foreach (DxfObject? item in new DxfObject?[] { a, b, a, null }) buffers[0].References.Add(item!);
        buffers[2].References.Add(b); buffers[3].References.Add(index); buffers[4].References.Add(buffers[0]);
        index.SetEntries(LayerIndexNames.Select((name, i) => new DxfLayerIndexEntry(name, buffers[i])));
        var parent = new DxfDictionary(); parent.Add("INDEX", index); parent.Add("INDEX_ALIAS", index, false);
        parent.Add("EMPTY", new DxfLayerIndex { Timestamp = -17.125 }); doc.NamedObjects.Add("QA_LAYER_INDEX", parent);
        index.PersistentReactors.Add(parent); index.PersistentReactors.Add(a);
        var data = new XData(new ApplicationRegistry("INDEX_APP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 3, 0, 255 }));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, index.Handle)); buffers[1].XData.Add(data);
        var extension = new DxfDictionary(); var note = new DxfXRecord();
        note.Data.Add(new DxfTag(1, "index metadata")); note.Data.Add(new DxfTag(330, buffers[0].Handle));
        extension.Add("NOTE", note); doc.Objects.SetExtensionDictionary(index, extension);
        return doc;
    }
    private static void LayerIndexCheck(DxfDocument doc, string parentName = "QA_LAYER_INDEX")
    {
        var parent = (DxfDictionary)doc.NamedObjects[parentName]; var index = (DxfLayerIndex)parent["INDEX"];
        Check(ReferenceEquals(parent["INDEX_ALIAS"], index), "index alias identity changed");
        Check(index.Entries.Select(entry => entry.LayerName).SequenceEqual(LayerIndexNames), "stored layer-name order/case changed");
        Check(index.Entries.Select(entry => entry.Count).SequenceEqual(new[] { 4, 0, 1, 1, 1 }), "derived buffer counts changed");
        Equal(BitConverter.DoubleToInt64Bits(2451545.125), BitConverter.DoubleToInt64Bits(index.Timestamp), "index timestamp changed");
        var empty = (DxfLayerIndex)parent["EMPTY"]; Equal(-17.125, empty.Timestamp, "empty timestamp changed"); Equal(0, empty.Entries.Count, "empty index gained entries");
        foreach (DxfLayerIndexEntry entry in index.Entries)
            Check(ReferenceEquals(entry.Buffer.Owner, index) && ReferenceEquals(entry.Buffer.Database, doc.Objects), "IDBUFFER ownership/registration differs");
        var lines = doc.Entities.Lines.ToArray(); var refs = index.Entries[0].Buffer.References;
        Check(refs.SequenceEqual(new DxfObject?[] { lines[0], lines[1], lines[0], null }), "IDBUFFER duplicates/null/ordered references changed");
        Check(ReferenceEquals(index.Entries[2].Buffer.References[0], lines[1]), "external buffer reference changed");
        Check(ReferenceEquals(index.Entries[3].Buffer.References[0], index), "internal index reference changed");
        Check(ReferenceEquals(index.Entries[4].Buffer.References[0], index.Entries[0].Buffer), "internal buffer reference changed");
        Check(index.PersistentReactors.Contains(parent) && index.PersistentReactors.Contains(lines[0]), "index reactors changed");
        Equal(index.Entries[0].Buffer.Handle, (string)((DxfXRecord)index.ExtensionDictionary["NOTE"]).Data[1].Value, "extension reference changed");
        var data = index.Entries[1].Buffer.XData["INDEX_APP"];
        Check(((byte[])data.XDataRecord[0].Value).SequenceEqual(new byte[] { 3, 0, 255 }), "buffer XData binary payload changed");
        Equal(index.Handle, (string)data.XDataRecord[1].Value, "buffer XData internal handle changed");
        Equal(0, doc.Objects.Validate().Count, "layer-index graph validation");
    }
    private static byte[] LayerIndexSave(DxfDocument doc, bool binary, string? filename = null)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "layer-index save failed");
        byte[] data = stream.ToArray(); if (filename != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, filename), data); return data;
    }
    private static DxfDocument LayerIndexLoad(byte[] data) { using var stream = new MemoryStream(data); return DxfDocument.Load(stream)!; }
    private static void LayerIndexAuthored(DxfVersion version, bool binary)
    {
        var doc = LayerIndexDocument(version); LayerIndexCheck(doc);
        doc.Layers["Alpha"].Name = "RENAMED_LAYER";
        Check(((DxfLayerIndex)((DxfDictionary)doc.NamedObjects["QA_LAYER_INDEX"])["INDEX"]).Entries.Select(entry => entry.LayerName).SequenceEqual(LayerIndexNames), "layer rename rewrote stored names");
        var loaded = LayerIndexLoad(LayerIndexSave(doc, binary, $"layer-index-authored-{version}-{binary}.dxf")); LayerIndexCheck(loaded);
        Equal("AcDbLayerIndex", loaded.Classes["LAYER_INDEX"].CppClassName, "layer-index C++ CLASS name");
        Equal("ObjectDBX Classes", loaded.Classes["LAYER_INDEX"].ApplicationName, "layer-index application CLASS name");
        Check(!loaded.Classes["LAYER_INDEX"].IsEntity && loaded.Classes["LAYER_INDEX"].ProxyFlags == 0, "layer-index CLASS flags");
        if (version >= DxfVersion.AutoCad2004) Equal(2, loaded.Classes["LAYER_INDEX"].InstanceCount, "layer-index CLASS count");
    }
    private static void LayerIndexCloneErase(DxfVersion version, bool binary)
    {
        var source = LayerIndexDocument(version); var original = (DxfDictionary)source.NamedObjects["QA_LAYER_INDEX"];
        var target = new DxfDocument(version); for (int i = 0; i < 8; i++) target.Layers.Add(new Layer("PAD" + i));
        var originals = source.Entities.Lines.ToArray(); var lines = originals.Select(line => (Line)line.Clone()).ToArray(); target.Entities.Add(lines);
        _ = target.Objects; string seed = target.DrawingVariables.HandleSeed;
        Throws<InvalidOperationException>(() => target.Objects.Clone(original, target.NamedObjects, "COPY"));
        Equal(seed, target.DrawingVariables.HandleSeed, "failed clone allocated handles"); Check(!target.NamedObjects.Contains("COPY"), "failed clone attached root");
        var map = new Dictionary<DxfObject, DxfObject> { [originals[0]] = lines[0], [originals[1]] = lines[1] };
        var copy = target.Objects.Clone(original, target.NamedObjects, "COPY", map); LayerIndexCheck(target, "COPY");
        var sourceIndex = (DxfLayerIndex)original["INDEX"]; var copyIndex = (DxfLayerIndex)copy["INDEX"];
        Check(sourceIndex.Handle != copyIndex.Handle, "clone did not allocate independent index identity");
        for (int i = 0; i < copyIndex.Entries.Count; i++) Check(sourceIndex.Entries[i].Buffer.Handle != copyIndex.Entries[i].Buffer.Handle, "clone reused a child handle");
        copyIndex.SetEntries(copyIndex.Entries.Reverse()); copyIndex.SetEntries(copyIndex.Entries.Reverse());
        Equal(0, sourceIndex.Entries[1].Buffer.References.Count, "cloned entry mutation changed source");
        byte[] bytes = LayerIndexSave(target, binary, $"layer-index-copy-{version}-{binary}.dxf");
        var erased = LayerIndexLoad(bytes); var root = (DxfDictionary)erased.NamedObjects["COPY"]; var index = (DxfLayerIndex)root["INDEX"];
        var blocker = new DxfXRecord(); blocker.Data.Add(new DxfTag(340, index.Entries[0].Buffer.Handle)); erased.NamedObjects.Add("BLOCKER", blocker);
        ErasureReject(erased, () => erased.Objects.EraseOwnedTree(root));
        ErasureReject(erased, () => erased.Objects.EraseOwnedTree(index.Entries[0].Buffer));
        blocker.Data.Clear(); erased.Objects.EraseOwnedTree(blocker);
        var descendants = erased.Objects.Items.Where(item => item == root || LayerIndexOwnedBy(item, root)).ToArray();
        string priorSeed = erased.DrawingVariables.HandleSeed; erased.Objects.EraseOwnedTree(root);
        Equal(priorSeed, erased.DrawingVariables.HandleSeed, "erasure changed handle seed");
        foreach (DxfDatabaseObject item in descendants)
            Check(item.IsErased && item.Database == null && erased.GetObjectByHandle(item.Handle) == null, "owned index descendant survived erasure");
        Equal(2, erased.Entities.Lines.Count(), "erasure deleted referenced external geometry");
        Throws<InvalidOperationException>(() => new DxfLayerIndex().SetEntries(new[] { new DxfLayerIndexEntry("dead", index.Entries[0].Buffer) }));
        var reloaded = LayerIndexLoad(LayerIndexSave(erased, binary, $"layer-index-erased-{version}-{binary}.dxf"));
        Check(!reloaded.Objects.Items.Any(item => item.CodeName == "LAYER_INDEX" || item.CodeName == "IDBUFFER"), "erased index/buffers returned on reload");
        if (version >= DxfVersion.AutoCad2004) Equal(0, reloaded.Classes["LAYER_INDEX"].InstanceCount, "erasure left stale CLASS count");
        LayerIndexCheck(source);
    }
    private static bool LayerIndexOwnedBy(DxfObject item, DxfObject owner)
    { for (DxfObject? parent = item.Owner; parent != null; parent = parent.Owner) if (ReferenceEquals(parent, owner)) return true; return false; }
    private static void LayerIndexProducer(DxfVersion version, bool sourceBinary, bool binary)
    {
        string name = $"ixmilia-layer-index-R{version.ToString().Substring(7)}-{(sourceBinary ? "binary" : "ascii")}.dxf.gz";
        string folder = Path.Combine("tests", "fixtures", "layer-index");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        var fixture = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(item => item.GetProperty("file").GetString() == name);
        using var compressed = File.OpenRead(Path.Combine(folder, name)); using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var expanded = new MemoryStream(); gzip.CopyTo(expanded);
        byte[] bytes = expanded.ToArray(); Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "producer source hash");
        bytes = File.ReadAllBytes(Path.Combine(folder, fixture.GetProperty("extracted_file").GetString()!));
        Equal(fixture.GetProperty("extracted_sha256").GetString(), Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "extracted fixture hash");
        var doc = LayerIndexLoad(bytes); var parent = (DxfDictionary)doc.NamedObjects["QA_LAYER_INDEX"]; var index = (DxfLayerIndex)parent["INDEX"];
        Check(index.Entries.Select(entry => entry.LayerName).SequenceEqual(new[] { "Alpha", "Alpha", "alpha", "Missing" }), "producer grouped layer names changed");
        Check(index.Entries.Select(entry => entry.Count).SequenceEqual(new[] { 3, 0, 1, 1 }), "producer grouped buffer counts changed");
        Equal(2451545.625, index.Timestamp, "producer timestamp reinterpreted"); Equal(2451545.5, ((DxfLayerIndex)parent["EMPTY"]).Timestamp, "producer empty timestamp changed");
        foreach (DxfLayerIndexEntry entry in index.Entries) Check(ReferenceEquals(entry.Buffer.Owner, index), "producer buffer owner changed");
        var loaded = LayerIndexLoad(LayerIndexSave(doc, binary, $"layer-index-producer-{version}-{sourceBinary}-{binary}.dxf"));
        var copy = (DxfLayerIndex)((DxfDictionary)loaded.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
        Check(copy.Entries.Select(entry => entry.Count).SequenceEqual(new[] { 3, 0, 1, 1 }), "producer counts changed on reload");
        Equal(0, loaded.Objects.Validate().Count, "producer graph validation");
    }
    private static void LayerIndexMalformed(DxfVersion version, bool binary, int fault)
    {
        var doc = LayerIndexDocument(version); var parent = (DxfDictionary)doc.NamedObjects["QA_LAYER_INDEX"]; var index = (DxfLayerIndex)parent["INDEX"];
        using var input = new MemoryStream(LayerIndexSave(doc, binary)); var raw = DxfRawDocument.Load(input);
        string handle = fault == 12 ? index.Entries[0].Buffer.Handle : index.Handle;
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int firstName = tags.FindIndex(tag => tag.Code == 8); int layerMarker = tags.FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbLayerIndex");
            if (fault == 0) tags.RemoveAt(tags.FindIndex(tag => tag.Code == 40));
            else if (fault == 1) tags.Insert(layerMarker, new DxfTag(40, 17.0));
            else if (fault == 2) tags.RemoveAt(layerMarker);
            else if (fault == 3) tags[firstName] = new DxfTag(8, "");
            else if (fault == 4) tags[firstName] = new DxfTag(8, @"bad\U+D800");
            else if (fault == 5) tags[firstName + 1] = new DxfTag(360, "0");
            else if (fault == 6) tags[firstName + 1] = new DxfTag(360, "EEEEFFFF");
            else if (fault == 7) tags[firstName + 1] = new DxfTag(360, parent.Handle);
            else if (fault == 8) tags[firstName + 2] = new DxfTag(90, -1);
            else if (fault == 9) tags[firstName + 2] = new DxfTag(90, 3);
            else if (fault == 10) tags.RemoveAt(firstName + 2);
            else if (fault == 11) tags[firstName + 4] = tags[firstName + 1];
            else if (fault == 12) tags[tags.FindIndex(tag => tag.Code == 330)] = new DxfTag(330, parent.Handle);
            else if (fault == 13) tags.Insert(tags.FindIndex(tag => tag.Code == 5) + 1, new DxfTag(5, "ABC"));
            else if (fault == 14) tags.Insert(layerMarker + 1, new DxfTag(100, "AcDbLayerIndex"));
            else tags[firstName + 2] = new DxfTag(90, int.MaxValue);
            return tags;
        });
        using var bad = new MemoryStream(); raw.Save(bad, binary); bad.Position = 0;
        bool rejected; try { rejected = DxfDocument.Load(bad) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "malformed layer-index envelope/ownership/count accepted");
    }
    private static void LayerIndexOpaque(bool binary, int variant)
    {
        var doc = LayerIndexDocument(DxfVersion.AutoCad2018); var index = (DxfLayerIndex)((DxfDictionary)doc.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
        using var input = new MemoryStream(LayerIndexSave(doc, binary)); var raw = DxfRawDocument.Load(input);
        raw = ObjectStoreReplaceRecord(raw, index.Handle, tags =>
        {
            int first = tags.FindIndex(tag => tag.Code == 100); int marker = tags.FindIndex(tag => tag.Code == 100 && (string)tag.Value == "AcDbLayerIndex");
            if (variant == 0) tags.Insert(first, new DxfTag(1, "private header"));
            else if (variant == 1) tags.InsertRange(first, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(70, (short)17), new DxfTag(102, "}") });
            else if (variant == 2) tags.AddRange(new[] { new DxfTag(100, "PrivateLayerIndex"), new DxfTag(91, 27) });
            else if (variant == 3) tags.Insert(marker + 1, new DxfTag(90, 0));
            else if (variant == 4) tags[first] = new DxfTag(100, "PrivateIndexBase");
            else tags.Add(new DxfTag(91, 7));
            return tags;
        });
        using var altered = new MemoryStream(); raw.Save(altered, binary); var loaded = LayerIndexLoad(altered.ToArray());
        var opaque = (DxfOpaqueObject)((DxfDictionary)loaded.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
        var before = opaque.Tags.Select(tag => tag.Code + ":" + tag.Value).ToArray();
        var destination = new DxfDocument();
        Throws<NotSupportedException>(() => destination.Objects.CloneObject(opaque, destination.NamedObjects, "COPY"));
        var reloaded = LayerIndexLoad(LayerIndexSave(loaded, binary, $"layer-index-opaque-{variant}-{binary}.dxf"));
        var after = (DxfOpaqueObject)((DxfDictionary)reloaded.NamedObjects["QA_LAYER_INDEX"])["INDEX"];
        Check(after.Tags.Select(tag => tag.Code + ":" + tag.Value).SequenceEqual(before), "opaque layer-index tags changed");
        Equal(5, reloaded.Objects.Items.OfType<DxfIdBuffer>().Count(), "opaque input lost owned buffers");
    }
    private static void LayerIndexClasses(int scenario)
    {
        var doc = scenario == 0 ? new DxfDocument(DxfVersion.AutoCad2018) : LayerIndexDocument(DxfVersion.AutoCad2018);
        if (scenario == 2 || scenario == 3)
        {
            using var input = new MemoryStream(LayerIndexSave(doc, false)); var raw = DxfRawDocument.Load(input);
            foreach (var index in doc.Objects.Items.OfType<DxfLayerIndex>().Take(scenario == 2 ? 2 : 1))
                raw = ObjectStoreReplaceRecord(raw, index.Handle, tags => { tags.Add(new DxfTag(91, 13)); return tags; });
            using var output = new MemoryStream(); raw.Save(output, false); doc = LayerIndexLoad(output.ToArray());
        }
        if (scenario == 5)
        {
            doc = LayerIndexLoad(LayerIndexSave(doc, false)); doc.Objects.EraseOwnedTree((DxfDictionary)doc.NamedObjects["QA_LAYER_INDEX"]);
        }
        doc.Classes.Remove("LAYER_INDEX"); string cpp = scenario == 0 || scenario == 2 || scenario == 4 ? "PrivateLayerIndex" : "AcDbLayerIndex";
        var definition = new DxfClass("LAYER_INDEX", cpp, "Independent application") { InstanceCount = 99, ProxyFlags = 7, WasProxy = true }; doc.Classes.Add(definition);
        if (scenario == 4) { using var rejected = new MemoryStream(); CheckSaveRejected(doc, rejected); Equal(0L, rejected.Length, "conflicting CLASS wrote bytes"); return; }
        var loaded = LayerIndexLoad(LayerIndexSave(doc, false)); var actual = loaded.Classes["LAYER_INDEX"];
        Equal(cpp, actual.CppClassName, "CLASS C++ name changed"); Equal("Independent application", actual.ApplicationName, "CLASS application changed");
        Equal(7, actual.ProxyFlags, "CLASS flags changed"); Check(actual.WasProxy, "CLASS proxy state changed");
        Equal(scenario == 0 || scenario == 2 ? 99 : scenario == 5 ? 0 : 2, actual.InstanceCount, "physical/private CLASS count differs");
        Equal(99, definition.InstanceCount, "writer mutated caller CLASS count");
    }
    private static void LayerIndexApi(int scenario)
    {
        var index = new DxfLayerIndex(); var a = new DxfIdBuffer(); var b = new DxfIdBuffer();
        if (scenario == 0) { foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) Throws<ArgumentOutOfRangeException>(() => index.Timestamp = value); return; }
        if (scenario == 1) { foreach (string name in new[] { "", "x\0", "x\n", "x\r", "\uD800", "\uDC00" }) Throws<ArgumentException>(() => new DxfLayerIndexEntry(name, a)); return; }
        if (scenario == 2) { Throws<ArgumentNullException>(() => index.SetEntries(null!)); Throws<ArgumentException>(() => index.SetEntries(new DxfLayerIndexEntry[] { null! })); return; }
        index.SetEntries(new[] { new DxfLayerIndexEntry("A", a), new DxfLayerIndexEntry("B", b) });
        if (scenario == 3) { Throws<ArgumentException>(() => index.SetEntries(new[] { new DxfLayerIndexEntry("A", a), new DxfLayerIndexEntry("Again", a) })); Equal(2, index.Entries.Count, "duplicate rejection changed entries"); return; }
        if (scenario == 4) { var other = new DxfLayerIndex(); Throws<ArgumentException>(() => other.SetEntries(new[] { new DxfLayerIndexEntry("A", a) })); Check(ReferenceEquals(a.Owner, index), "foreign adoption changed child owner"); return; }
        if (scenario == 5) { index.SetEntries(new[] { new DxfLayerIndexEntry("B", b) }); Check(a.Owner == null && ReferenceEquals(b.Owner, index), "detached replacement did not release removed child"); return; }
        var doc = new DxfDocument(); doc.NamedObjects.Add("INDEX", index);
        if (scenario == 6) { string seed = doc.DrawingVariables.HandleSeed; Throws<InvalidOperationException>(() => index.SetEntries(new[] { new DxfLayerIndexEntry("A", a) })); Equal(2, index.Entries.Count, "live replacement orphaned child"); Equal(seed, doc.DrawingVariables.HandleSeed, "rejected replacement changed seed"); return; }
        if (scenario == 7) { index.SetEntries(new[] { new DxfLayerIndexEntry("renamed", b), new DxfLayerIndexEntry("renamed", a) }); Check(ReferenceEquals(index.Entries[0].Buffer, b), "live reorder failed"); Equal(0, doc.Objects.Validate().Count, "live reorder graph invalid"); return; }
        if (scenario == 8) { a.References.Add(null!); Equal(1, index.Entries[0].Count, "explicit buffer edit did not change derived count"); var loaded = LayerIndexLoad(LayerIndexSave(doc, true)); Equal(1, ((DxfLayerIndex)loaded.NamedObjects["INDEX"]).Entries[0].Count, "edited count failed roundtrip"); return; }
        doc.Objects.EraseOwnedTree(index); Throws<InvalidOperationException>(() => index.SetEntries(Array.Empty<DxfLayerIndexEntry>())); Throws<InvalidOperationException>(() => new DxfLayerIndex().SetEntries(new[] { new DxfLayerIndexEntry("erased", a) }));
    }
}
