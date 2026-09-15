using System.Security.Cryptography;
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
    private static void RegisterPolyline3DRecordTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool inputBinary in new[] { false, true })
        {
            foreach (bool binary in new[] { false, true })
            {
                Run($"polyline-records/native/{version}/{inputBinary}/{binary}", () => PolylineRecordNative(version, inputBinary, binary));
                Run($"polyline-records/producer/{version}/{inputBinary}/{binary}", () => PolylineRecordProducer(version, inputBinary, binary));
            }
            foreach (int fault in Enumerable.Range(0, 18))
                Run($"polyline-records/malformed/{version}/{inputBinary}/{fault}", () => PolylineRecordMalformed(version, inputBinary, fault));
            Run($"polyline-records/historical-writer/{version}/{inputBinary}", () => PolylineRecordHistoricalWriter(version, inputBinary));
            Run($"polyline-records/clone/{version}/{inputBinary}", () => PolylineRecordClone(version, inputBinary));
            Run($"polyline-records/move/{version}/{inputBinary}", () => PolylineRecordMove(version, inputBinary));
            foreach (bool legacy in new[] { false, true })
                Run($"polyline-records/missing-seqend/{version}/{inputBinary}/{legacy}", () => PolylineRecordMissingSequenceEnd(version, inputBinary, legacy));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (int scenario in Enumerable.Range(0, 14))
                Run($"polyline-records/lifecycle/{binary}/{scenario}", () => PolylineRecordLifecycle(binary, scenario));
            foreach (int variant in Enumerable.Range(0, 6))
                Run($"polyline-records/private/{binary}/{variant}", () => PolylineRecordPrivate(binary, variant));
            Run($"polyline-records/profile-rejection/{binary}", () => PolylineRecordProfileRejection(binary));
            foreach (short code in new short[] { 5, 330, 340, 320 })
                Run($"polyline-records/header-removal/{binary}/{code}", () => PolylineRecordHeaderRemoval(binary, code));
            foreach (bool empty in new[] { false, true })
                Run($"polyline-records/parent-xdata-clone/{binary}/{empty}", () => PolylineRecordParentXDataClone(binary, empty));
            foreach (bool xdata in new[] { false, true })
                Run($"polyline-records/parent-reference-move/{binary}/{xdata}", () => PolylineRecordParentReferenceMove(binary, xdata));
        }
    }
    private static JsonElement PolylineRecordFixture(DxfVersion version, bool binary)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/polyline3d-records/manifest.json"));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..]) && f.GetProperty("binary").GetBoolean() == binary).Clone();
    }
    private static byte[] PolylineRecordInput(DxfVersion version, bool binary)
    {
        var fixture = PolylineRecordFixture(version, binary);
        byte[] data = File.ReadAllBytes(Path.Combine("tests/fixtures/polyline3d-records", fixture.GetProperty("file").GetString()!));
        Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(), "producer source hash");
        return data;
    }
    private static DxfRawDocument PolylineRecordRaw(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return DxfRawDocument.Load(stream); }
    private static Dictionary<string, DxfRawRecord> PolylineRecordPackets(byte[] bytes)
    {
        var result = new Dictionary<string, DxfRawRecord>();
        foreach (var record in PolylineRecordRaw(bytes).Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records).Where(r => r.Name is "VERTEX" or "SEQEND"))
            result.Add((string)record.Tags.First(t => t.Code == 5).Value, record);
        return result;
    }
    private static string PolylineRecordTagKey(DxfTag tag) => tag.Code + ":" + (tag.Value is byte[] bytes ? Convert.ToHexString(bytes) : Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
    private static void PolylineRecordPacketEqual(DxfRawRecord before, DxfRawRecord after, bool identities = true)
    {
        var first = before.Tags.Where(t => identities || t.Code is not (5 or 330)).Select(PolylineRecordTagKey);
        var second = after.Tags.Where(t => identities || t.Code is not (5 or 330)).Select(PolylineRecordTagKey);
        Check(first.SequenceEqual(second), "retained child packet fields, presence or ordering changed");
    }
    private static void PolylineRecordNative(DxfVersion version, bool inputBinary, bool binary)
    {
        byte[] source = StoredDimAssocInput(version, inputBinary); var doc = StoredDimAssocLoad(source);
        var polyline = (Polyline3D)doc.GetObjectByHandle(StoredDimAssocHandle(version, "41A"));
        Equal(4, polyline.VertexRecords.Count, "native record count");
        var references = polyline.VertexRecords.Cast<DxfObject>().Append(polyline.EndSequenceRecord).ToArray();
        foreach (var record in polyline.VertexRecords.Append(polyline.EndSequenceRecord))
            Check(ReferenceEquals(doc.GetObjectByHandle(record.Handle), record) && ReferenceEquals(record.Owner, polyline) && ReferenceEquals(record.StoredOwner, polyline) && !record.UsesBlockRecordOwner, "native child identity and reciprocal owner");
        var vertex = (Polyline3DRecord)doc.GetObjectByHandle(StoredDimAssocHandle(version, "41E"));
        Check(vertex.PersistentReactors.Single() is DxfOpaqueObject association && association.Handle == StoredDimAssocHandle(version, "42F"), "native vertex reactor / opaque DIMASSOC boundary");
        byte[] output = StoredDimAssocSave(doc, binary, $"vertex-native-{version}-{inputBinary}-{binary}.dxf");
        var packets = PolylineRecordPackets(output); var originals = PolylineRecordPackets(source);
        foreach (var record in references) PolylineRecordPacketEqual(originals[record.Handle], packets[record.Handle]);
        byte[] repeated = StoredDimAssocSave(doc, binary);
        var repeat = PolylineRecordPackets(repeated);
        foreach (var record in references) { Check(ReferenceEquals(doc.GetObjectByHandle(record.Handle), record), "save replaced child object identity"); PolylineRecordPacketEqual(packets[record.Handle], repeat[record.Handle]); }
        var loaded = StoredDimAssocLoad(output); Equal(0, loaded.Objects.Validate().Count, "native retained graph validation");
    }
    private static void PolylineRecordProducer(DxfVersion version, bool inputBinary, bool binary)
    {
        byte[] input = PolylineRecordInput(version, inputBinary); var doc = StoredDimAssocLoad(input);
        var handles = PolylineRecordFixture(version, inputBinary).GetProperty("handles");
        var polyline = (Polyline3D)doc.GetObjectByHandle(handles.GetProperty("polyline").GetString()!);
        Equal(3, polyline.VertexRecords.Count, "producer vertex count");
        foreach (var record in polyline.VertexRecords)
            Check(record.UsesBlockRecordOwner && ReferenceEquals(record.Owner, polyline) && ReferenceEquals(record.StoredOwner, doc.GetObjectByHandle(handles.GetProperty("block_record").GetString()!)), "producer structural and stored owners were conflated");
        Check(!polyline.EndSequenceRecord.UsesBlockRecordOwner && ReferenceEquals(polyline.EndSequenceRecord.StoredOwner, polyline), "producer SEQEND owner");
        Check(ReferenceEquals(polyline.VertexRecords[0].Layer, doc.Layers["VERTEX_ONLY"]) && ReferenceEquals(polyline.EndSequenceRecord.Linetype, doc.Linetypes["VERTEX_DASH"]), "child named resources must resolve actual identities");
        Check(ReferenceEquals(polyline.VertexRecords[1].ExtensionDictionary.Owner, polyline.VertexRecords[1]) && ReferenceEquals(polyline.EndSequenceRecord.ExtensionDictionary.Owner, polyline.EndSequenceRecord), "child extension ownership");
        Check(ReferenceEquals(polyline.VertexRecords[0].PersistentReactors[0], polyline.VertexRecords[1]) && ReferenceEquals(polyline.VertexRecords[1].PersistentReactors[0], polyline.VertexRecords[0]), "cross-record reactor cycle");
        Equal(3, doc.ApplicationRegistries["VERTEX_RECORD_META"].GetReferences().Count(r => r.Reference is Polyline3DRecord), "child APPID reference carriers");
        Equal(0, doc.Objects.Validate().Count, "producer source graph validation");
        byte[] output = StoredDimAssocSave(doc, binary, $"vertex-producer-{version}-{inputBinary}-{binary}.dxf");
        var originals = PolylineRecordPackets(input); var packets = PolylineRecordPackets(output);
        foreach (var pair in originals) PolylineRecordPacketEqual(pair.Value, packets[pair.Key]);
        var loaded = StoredDimAssocLoad(output); Equal(0, loaded.Objects.Validate().Count, "producer output graph validation");
    }
    private static void PolylineRecordMalformed(DxfVersion version, bool binary, int fault)
    {
        var raw = PolylineRecordRaw(PolylineRecordInput(version, binary));
        var handles = PolylineRecordFixture(version, binary).GetProperty("handles"); string handle = handles.GetProperty("vertices")[0].GetString()!;
        if (fault == 14 || fault == 15) handle = handles.GetProperty("seqend").GetString()!;
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int at(short code) => tags.FindIndex(t => t.Code == code);
            int publicOwner = -1, depth = 0;
            for (int i = 0; i < tags.Count && tags[i].Code != 100; i++)
            {
                if (tags[i].Code == 102) depth += ((string)tags[i].Value).StartsWith('{') ? 1 : -1;
                else if (depth == 0 && tags[i].Code == 330) { publicOwner = i; break; }
            }
            Check(publicOwner >= 0, "malformed fixture has no ordinary owner to mutate");
            if (fault == 0) tags.RemoveAt(at(5));
            else if (fault == 1) tags[at(5)] = new(5, "0");
            else if (fault == 2) tags.Insert(at(5), tags[at(5)]);
            else if (fault == 3) tags[publicOwner] = new(330, "0");
            else if (fault == 4) tags[publicOwner] = new(330, "FFFFFFFF");
            else if (fault == 5) tags[publicOwner] = new(330, handles.GetProperty("plain_polyline").GetString()!);
            else if (fault == 6) tags.Insert(publicOwner, new(330, "000" + (string)tags[publicOwner].Value));
            else if (fault == 7) tags.RemoveAt(tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbVertex")));
            else if (fault == 8) tags.Insert(at(100), tags[at(100)]);
            else if (fault == 9) tags[at(70)] = new(70, (short)48);
            else if (fault == 10) tags.RemoveAt(at(30));
            else if (fault == 11) tags.Insert(at(10), tags[at(10)]);
            else if (fault == 12) tags[at(330)] = new(330, "FFFFFFFF");
            else if (fault == 13) tags.Insert(at(5) + 1, new(102, "{PRIVATE"));
            else if (fault == 14) tags[publicOwner] = new(330, handles.GetProperty("block_record").GetString()!);
            else if (fault == 15) tags.RemoveAt(at(5));
            else if (fault == 16) tags[at(5)] = new(5, handles.GetProperty("vertices")[1].GetString()!);
            else { tags.RemoveAt(at(5)); tags.InsertRange(0, new DxfTag[] { new(102, "{PRIVATE"), new(5, handle), new(102, "}") }); }
            return tags;
        });
        bool rejected = false;
        try { using var stream = new MemoryStream(StoredDimAssocRawBytes(raw, binary)); rejected = DxfDocument.Load(stream) == null; }
        catch (Exception error) when (error is FormatException or ArgumentException) { rejected = true; }
        Check(rejected, "malformed child record accepted");
    }
    private static void PolylineRecordHistoricalWriter(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var polyline = new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }); doc.Entities.Add(polyline);
        byte[] output = StoredDimAssocSave(doc, binary); var loaded = StoredDimAssocLoad(output); var copy = loaded.Entities.Polylines3D.Single();
        Equal(3, copy.VertexRecords.Count, "existing writer's children admitted");
        Check(copy.VertexRecords.All(r => !r.UsesBlockRecordOwner && ReferenceEquals(r.StoredOwner, copy)), "existing writer owner form");
        Polyline3D clone = (Polyline3D)copy.Clone(); var destination = new DxfDocument(version); destination.Entities.Add(clone);
        Equal(3, clone.VertexRecords.Count, "existing geometry-only clone remains available");
        Equal(0, destination.Objects.Validate().Count, "existing clone destination valid");
    }
    private static void PolylineRecordMissingSequenceEnd(DxfVersion version, bool binary, bool legacy)
    {
        var raw = PolylineRecordRaw(PolylineRecordInput(version, binary));
        var handles = PolylineRecordFixture(version, binary).GetProperty("handles");
        if (legacy)
            raw = ObjectStoreReplaceRecord(raw, handles.GetProperty("polyline").GetString()!, tags =>
            {
                int at = tags.FindIndex(t => t.Code == 70); tags[at] = new(70, (short)12); return tags;
            });
        string handle = handles.GetProperty("seqend").GetString()!;
        var end = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "SEQEND" && r.Tags.Any(t => t.Code == 5 && Equals(t.Value, handle)));
        var input = StoredDimAssocRawBytes(DxfRawDocument.Create(raw.Tags.Take(end.StartTagIndex).Concat(raw.Tags.Skip(end.EndTagIndex))), binary);
        // The next POLYLINE is an actual record boundary. Neither reader path may spin
        // waiting for SEQEND or consume that following entity as part of this sequence.
        bool rejected = false;
        try { using var stream = new MemoryStream(input); rejected = DxfDocument.Load(stream) == null; }
        catch (FormatException) { rejected = true; }
        Check(rejected, "missing SEQEND did not reject at the next physical record boundary");
    }
    private static void PolylineRecordClone(DxfVersion version, bool binary)
    {
        var source = StoredDimAssocLoad(PolylineRecordInput(version, binary));
        var handles = PolylineRecordFixture(version, binary).GetProperty("handles"); var original = (Polyline3D)source.GetObjectByHandle(handles.GetProperty("plain_polyline").GetString()!);
        Polyline3D clone = (Polyline3D)original.Clone(); var destination = new DxfDocument(version); destination.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX)); destination.Entities.Add(clone);
        Equal(original.VertexRecords.Count, clone.VertexRecords.Count, "clone record count");
        for (int i = 0; i < original.VertexRecords.Count; i++)
            Check(!ReferenceEquals(original.VertexRecords[i], clone.VertexRecords[i]) && ReferenceEquals(clone.VertexRecords[i].Owner, clone) && ReferenceEquals(destination.GetObjectByHandle(clone.VertexRecords[i].Handle), clone.VertexRecords[i]), "clone owned identity");
        var from = (byte[])original.VertexRecords[0].XData["VERTEX_RECORD_META"].XDataRecord.Single(t => t.Code == XDataCode.BinaryData).Value;
        var to = (byte[])clone.VertexRecords[0].XData["VERTEX_RECORD_META"].XDataRecord.Single(t => t.Code == XDataCode.BinaryData).Value;
        Check(!ReferenceEquals(from, to), "clone XData binary aliases source"); to[0] = 9; Equal((byte)1, from[0], "clone binary mutation leaked"); to[0] = 1;
        destination.Layers["VERTEX_ONLY"].Name = "CLONED_VERTEX_LAYER";
        Check(original.VertexRecords[0].Layer.Name == "VERTEX_ONLY" && clone.VertexRecords[0].Layer.Name == "CLONED_VERTEX_LAYER", "clone named resource adoption");
        byte[] output = StoredDimAssocSave(destination, !binary, $"vertex-clone-{version}-{binary}.dxf");
        var loaded = StoredDimAssocLoad(output); Equal(0, loaded.Objects.Validate().Count, "clone roundtrip graph");
        Equal("CLONED_VERTEX_LAYER", loaded.Entities.Polylines3D.Single().VertexRecords[0].Layer.Name, "clone layer renamed on wire");
    }
    private static void PolylineRecordMove(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(PolylineRecordInput(version, binary)); var handles = PolylineRecordFixture(version, binary).GetProperty("handles");
        var polyline = (Polyline3D)doc.GetObjectByHandle(handles.GetProperty("plain_polyline").GetString()!); var records = polyline.VertexRecords.ToArray();
        Check(doc.Entities.Remove(polyline), "unreferenced source polyline removal rejected");
        Check(records.All(r => doc.GetObjectByHandle(r.Handle) == null), "detached child identities remain registered");
        var destination = new Block("MOVED_VERTEX_PARENT"); destination.Entities.Add(polyline); doc.Blocks.Add(destination);
        Check(records.All(r => ReferenceEquals(doc.GetObjectByHandle(r.Handle), r) && ReferenceEquals(r.StoredOwner, destination.Record)), "moved block-record owner is stale");
        byte[] output = StoredDimAssocSave(doc, binary, $"vertex-move-{version}-{binary}.dxf");
        var loaded = StoredDimAssocLoad(output); var copy = loaded.Blocks[destination.Name].Entities.OfType<Polyline3D>().Single();
        Check(copy.VertexRecords.All(r => ReferenceEquals(r.StoredOwner, loaded.Blocks[destination.Name].Record)), "move owner not retained across reload");
        Equal(0, loaded.Objects.Validate().Count, "move graph validation");
    }
    private static void PolylineRecordLifecycle(bool binary, int scenario)
    {
        var doc = StoredDimAssocLoad(PolylineRecordInput(DxfVersion.AutoCad2018, binary));
        var handles = PolylineRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        var polyline = (Polyline3D)doc.GetObjectByHandle(handles.GetProperty("polyline").GetString()!); var first = polyline.VertexRecords[0];
        if (scenario <= 3)
        {
            byte[] before = StoredDimAssocSave(doc, binary); var records = polyline.VertexRecords.Select(r => r.Handle).ToArray();
            if (scenario == 0) polyline.Vertexes.Add(Vector3.UnitZ);
            if (scenario == 1) polyline.Vertexes.RemoveAt(0);
            if (scenario == 2) polyline.SmoothType = PolylineSmoothType.Cubic;
            if (scenario == 3) polyline.Vertexes[0] = new Vector3(double.NaN, 0, 0);
            using var output = new MemoryStream(); output.WriteByte(23); bool rejected = false;
            try { rejected = !doc.Save(output, binary); } catch (Exception error) when (error is NotSupportedException or InvalidOperationException) { rejected = true; }
            Check(rejected && output.Length == 1 && output.ToArray()[0] == 23 && records.SequenceEqual(polyline.VertexRecords.Select(r => r.Handle)), "unsupported geometry edit was not rejected before output/identity mutation");
        }
        else if (scenario == 4)
        {
            var original = polyline.VertexRecords.ToArray(); var points = polyline.Vertexes.ToArray(); polyline.Reverse();
            Check(polyline.VertexRecords.SequenceEqual(original.Reverse()) && polyline.Vertexes.SequenceEqual(points.Reverse()), "Reverse did not move identities with points");
            polyline.Reverse(); Check(polyline.VertexRecords.SequenceEqual(original) && polyline.Vertexes.SequenceEqual(points), "Reverse twice changed identities");
        }
        else if (scenario == 5)
        {
            polyline.Vertexes[0] = new Vector3(91, 92, 93); Check(ReferenceEquals(first, polyline.VertexRecords[0]), "coordinate replacement changed slot identity");
            byte[] output = StoredDimAssocSave(doc, binary, $"vertex-edited-{binary}.dxf"); var loaded = StoredDimAssocLoad(output);
            Equal(new Vector3(91, 92, 93), ((Polyline3D)loaded.GetObjectByHandle(polyline.Handle)).Vertexes[0], "coordinate edit output");
        }
        else if (scenario == 6)
        {
            int objects = doc.Objects.Items.Count(); Throws<NotSupportedException>(() => polyline.Clone());
            Throws<NotSupportedException>(() => ((Block)polyline.Owner).Clone("CLONE_RECORD_GRAPH")); Equal(objects, doc.Objects.Items.Count(), "clone refusal changed source graph");
        }
        else if (scenario == 7)
        {
            Check(!doc.Entities.Remove(polyline), "owning polyline removed despite child extension graphs");
            Check(ReferenceEquals(doc.GetObjectByHandle(first.Handle), first), "failed removal detached child");
        }
        else if (scenario == 8)
        {
            Check(!doc.Layers.Remove("VERTEX_ONLY") && !doc.Linetypes.Remove("VERTEX_DASH"), "child named dependency removal allowed");
            doc.Layers["VERTEX_ONLY"].Name = "RENAMED_VERTEX_LAYER"; doc.Linetypes["VERTEX_DASH"].Name = "RENAMED_VERTEX_LINE";
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); var record = (Polyline3DRecord)loaded.GetObjectByHandle(first.Handle);
            Equal("RENAMED_VERTEX_LAYER", record.Layer.Name, "layer rename"); Equal("RENAMED_VERTEX_LINE", record.Linetype.Name, "linetype rename");
        }
        else if (scenario == 9)
        {
            var registry = doc.ApplicationRegistries["VERTEX_RECORD_META"]; Check(!doc.ApplicationRegistries.Remove(registry), "child APPID removal allowed"); registry.Name = "RENAMED_VERTEX_META";
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); Check(((Polyline3DRecord)loaded.GetObjectByHandle(first.Handle)).XData.ContainsAppId("RENAMED_VERTEX_META"), "APPID rename not written on child");
        }
        else if (scenario == 10)
        {
            var extension = polyline.VertexRecords[1].ExtensionDictionary; doc.Objects.EraseOwnedTree(extension);
            Check(polyline.VertexRecords[1].ExtensionDictionary == null, "extension erasure did not clear child slot");
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); Check(((Polyline3DRecord)loaded.GetObjectByHandle(polyline.VertexRecords[1].Handle)).ExtensionDictionary == null, "erased extension wire group survived");
        }
        else if (scenario == 11)
        {
            var destination = new DxfDictionary(); doc.NamedObjects.Add("CLONE_DESTINATION", destination); int count = doc.Objects.Items.Count();
            Throws<NotSupportedException>(() => doc.Objects.Clone(polyline.VertexRecords[1].ExtensionDictionary, destination, "PARTIAL"));
            Check(!destination.Contains("PARTIAL") && doc.Objects.Items.Count() == count, "partial owned clone mutated destination");
        }
        else if (scenario == 12)
        {
            first.PersistentReactors.Clear(); first.PersistentReactors.Add(polyline.VertexRecords[2]); first.PersistentReactors.Add(polyline.VertexRecords[2]);
            var extension = new DxfDictionary(); extension.Add("NEW", new DxfDictionaryVariable { Value = "new child attachment" }); doc.Objects.SetExtensionDictionary(first, extension);
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"vertex-metadata-edited-{binary}.dxf")); var copy = (Polyline3DRecord)loaded.GetObjectByHandle(first.Handle);
            Equal(2, copy.PersistentReactors.Count, "duplicate explicit reactor order lost"); Check(ReferenceEquals(copy.ExtensionDictionary.Owner, copy), "new child extension not reciprocal");
        }
        else
        {
            var plain = (Polyline3D)doc.GetObjectByHandle(handles.GetProperty("plain_polyline").GetString()!); Check(doc.Entities.Remove(plain), "foreign adoption source detach");
            var target = new DxfDocument(); var objects = target.Objects.Items.ToArray(); long seed = OwnershipSeed(target);
            var resources = (target.Layers.Count, target.Linetypes.Count, target.ApplicationRegistries.Count, target.Blocks.Count);
            Throws<NotSupportedException>(() => target.Entities.Add(plain)); Check(plain.Owner == null, "failed foreign adoption attached source");
            Check(target.Objects.Items.SequenceEqual(objects) && OwnershipSeed(target) == seed && !target.Entities.All.Any()
                && resources == (target.Layers.Count, target.Linetypes.Count, target.ApplicationRegistries.Count, target.Blocks.Count), "failed foreign adoption changed destination");
        }
    }
    private static void PolylineRecordPrivate(bool binary, int variant)
    {
        var source = PolylineRecordInput(DxfVersion.AutoCad2018, binary); var handles = PolylineRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        string handle = handles.GetProperty("plain_vertices")[0].GetString()!; var raw = PolylineRecordRaw(source);
        raw = ObjectStoreReplaceRecord(raw, handle, tags =>
        {
            int at = tags.FindIndex(t => t.Code == 1001); if (at < 0) at = tags.Count;
            if (variant == 0) tags.InsertRange(tags.FindIndex(t => t.Code == 100), new DxfTag[] { new(102, "{PRIVATE"), new(5, "FFFFFF"), new(330, "EEEEEE"), new(102, "}") });
            else if (variant == 1) tags.InsertRange(at, new DxfTag[] { new(102, "{PRIVATE"), new(5, "FFFFFF"), new(70, (short)-1), new(10, 77.0), new(20, 88.0), new(30, 99.0), new(102, "}") });
            else if (variant == 2) tags.InsertRange(at, new DxfTag[] { new(100, "PrivateVertexClass"), new(5, "FFFFFF"), new(70, (short)-1), new(10, 77.0), new(20, 88.0), new(30, 99.0), new(330, "EEEEEE") });
            else if (variant == 3) tags.InsertRange(tags.FindIndex(t => t.Code == 100), new DxfTag[] { new(102, "{ACAD_REACTORS"), new(330, "0000"), new(102, "}") });
            else if (variant == 4) tags.InsertRange(tags.FindIndex(t => t.Code == 100), new DxfTag[] { new(102, "{ACAD_XDICTIONARY"), new(360, "0000"), new(102, "}") });
            else tags.InsertRange(tags.FindIndex(t => t.Code == 100), new DxfTag[] { new(102, "{ACAD_REACTORS"), new(102, "}") });
            return tags;
        });
        byte[] input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var record = (Polyline3DRecord)doc.GetObjectByHandle(handle);
        Check(doc.GetObjectByHandle("FFFFFF") == null && doc.GetObjectByHandle("EEEEEE") == null, "private handle became a physical source identity");
        var owner = (Polyline3D)record.Owner; Equal(new Vector3(11, 12, 13), owner.Vertexes[0], "private coordinate or flags changed public geometry");
        if (variant < 3) Throws<NotSupportedException>(() => owner.Clone());
        byte[] output = StoredDimAssocSave(doc, binary, $"vertex-private-{binary}-{variant}.dxf");
        PolylineRecordPacketEqual(PolylineRecordPackets(input)[handle], PolylineRecordPackets(output)[handle]);
    }
    private static void PolylineRecordProfileRejection(bool binary)
    {
        var source = StoredDimAssocLoad(PolylineRecordInput(DxfVersion.AutoCad2018, binary));
        var original = source.Entities.Polylines3D.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var clone = (Polyline3D)original.Clone(); var destination = new DxfDocument(DxfVersion.AutoCad2000);
        int objects = destination.Objects.Items.Count(); long destinationSeed = OwnershipSeed(destination);
        Throws<NotSupportedException>(() => destination.Entities.Add(clone));
        Check(clone.Owner == null && clone.VertexRecords.All(r => r.Handle == null)
            && destinationSeed == OwnershipSeed(destination) && objects == destination.Objects.Items.Count(), "profile conversion adoption changed destination");
        Equal(DxfVersion.AutoCad2018, original.VertexRecords[0].SourceVersion, "stored source profile");
        source.DrawingVariables.AcadVer = DxfVersion.AutoCad2000; long sourceSeed = OwnershipSeed(source);
        using var stream = new MemoryStream(); stream.WriteByte(29); bool rejected = false;
        try { rejected = !source.Save(stream, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && stream.Length == 1 && stream.ToArray()[0] == 29 && sourceSeed == OwnershipSeed(source), "profile conversion output changed stream or allocated handles");
    }
    private static void PolylineRecordHeaderRemoval(bool binary, short code)
    {
        var doc = StoredDimAssocLoad(PolylineRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.Polylines3D.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0); var child = plain.VertexRecords[0];
        doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$VERTEX_REF", code, "000" + child.Handle.ToLowerInvariant()));
        long seed = OwnershipSeed(doc);
        if (code == 320) Check(doc.Entities.Remove(plain), "arbitrary custom header handle should not block removal");
        else
        {
            Check(!doc.Entities.Remove(plain) && ReferenceEquals(doc.GetObjectByHandle(child.Handle), child)
                && OwnershipSeed(doc) == seed, "header reference allowed child removal or mutated registration");
            doc.DrawingVariables.RemoveCustomVariable("$VERTEX_REF");
            Check(doc.Entities.Remove(plain), "cleared custom header reference still blocked removal");
        }
    }
    private static void PolylineRecordParentXDataClone(bool binary, bool empty)
    {
        var doc = StoredDimAssocLoad(PolylineRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.Polylines3D.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var data = new XData(new ApplicationRegistry("PARENT_VERTEX_REF"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, empty ? "0000" : plain.VertexRecords[0].Handle));
        plain.XData.Add(data); long seed = OwnershipSeed(doc); var handles = plain.VertexRecords.Select(r => r.Handle).ToArray();
        if (empty)
        {
            var clone = (Polyline3D)plain.Clone(); var destination = new DxfDocument(DxfVersion.AutoCad2018); destination.Entities.Add(clone);
            Equal(0, destination.Objects.Validate().Count, "explicit null parent XData must remain cloneable");
        }
        else
        {
            Throws<NotSupportedException>(() => plain.Clone());
            Check(OwnershipSeed(doc) == seed && handles.SequenceEqual(plain.VertexRecords.Select(r => r.Handle)), "parent XData clone refusal changed source identities");
            Check(doc.Entities.Remove(plain), "internal parent-to-child XData prevented complete detach");
            var container = new Block("PARENT_XDATA_CONTAINER"); container.Entities.Add(plain);
            Throws<NotSupportedException>(() => container.Clone("INVALID_COPY"));
        }
    }
    private static void PolylineRecordParentReferenceMove(bool binary, bool xdata)
    {
        var doc = StoredDimAssocLoad(PolylineRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.Polylines3D.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0); string handle = plain.Handle;
        if (xdata)
        {
            var data = new XData(new ApplicationRegistry("CHILD_PARENT_REF"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, handle)); plain.VertexRecords[0].XData.Add(data);
        }
        else plain.VertexRecords[0].PersistentReactors.Add(plain);
        doc = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); plain = (Polyline3D)doc.GetObjectByHandle(handle); var first = plain.VertexRecords[0];
        if (xdata)
        {
            long seed = OwnershipSeed(doc);
            Check(!doc.Entities.Remove(plain) && ReferenceEquals(doc.GetObjectByHandle(first.Handle), first) && seed == OwnershipSeed(doc), "textual child-to-parent reference allowed an unmapped parent move");
        }
        else
        {
            Check(doc.Entities.Remove(plain), "object reactor prevented same-document move");
            var block = new Block("MOVED_PARENT_REACTOR"); block.Entities.Add(plain); doc.Blocks.Add(block);
            Check(plain.Handle != handle, "move did not exercise parent handle reallocation");
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"vertex-parent-reactor-move-{binary}.dxf"));
            var child = (Polyline3DRecord)loaded.GetObjectByHandle(first.Handle);
            Check(ReferenceEquals(child.PersistentReactors.Single(), child.Owner), "move replayed stale raw parent reactor handle");
        }
    }
}
