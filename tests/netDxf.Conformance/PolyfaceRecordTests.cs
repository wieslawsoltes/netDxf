using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPolyfaceRecordTests()
    {
        RegisterPolyfaceRecordAdvancedTests();
        foreach (DxfVersion version in SupportedVersions) foreach (bool inputBinary in new[] { false, true })
        {
            Run($"polyface-records/native/{version}/{inputBinary}", () => PolyfaceRecordNative(version, inputBinary));
            foreach (bool binary in new[] { false, true })
                Run($"polyface-records/producer/{version}/{inputBinary}/{binary}", () => PolyfaceRecordProducer(version, inputBinary, binary));
            Run($"polyface-records/clone/{version}/{inputBinary}", () => PolyfaceRecordClone(version, inputBinary));
            Run($"polyface-records/face-edits/{version}/{inputBinary}", () => PolyfaceRecordEdit(version, inputBinary));
            Run($"polyface-records/null-layer-removal/{version}/{inputBinary}", () => PolyfaceRecordNullLayerRemoval(version, inputBinary));
        }
    }
    private static JsonElement PolyfaceRecordFixture(DxfVersion version, bool binary)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/polyface-records/manifest.json"));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..]) && f.GetProperty("binary").GetBoolean() == binary).Clone();
    }
    private static byte[] PolyfaceRecordInput(DxfVersion version, bool binary)
    {
        var fixture = PolyfaceRecordFixture(version, binary);
        var bytes = File.ReadAllBytes(Path.Combine("tests/fixtures/polyface-records", fixture.GetProperty("file").GetString()!));
        Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "actual producer source hash"); return bytes;
    }
    private static DxfRawRecord PolyfaceRecordParent(byte[] bytes, string handle) => PolylineRecordRaw(bytes).Sections.SelectMany(s => s.Records)
        .Single(r => r.Name == "POLYLINE" && r.Tags.Any(t => t.Code == 5 && Equals(t.Value, handle)));
    private static void PolyfaceRecordNative(DxfVersion version, bool binary)
    {
        var input = File.ReadAllBytes($"tests/fixtures/polyface-records/native-R{version.ToString()[7..]}.dxf"); var doc = StoredDimAssocLoad(input);
        var mesh = (PolyfaceMesh)doc.GetObjectByHandle("4E4");
        Equal(6, mesh.VertexRecords.Count, "native coordinates"); Equal(3, mesh.FaceRecords.Count, "native faces"); Equal(10, mesh.RecordSequence.Count, "native child sequence");
        Equal((short?)6, mesh.DeclaredVertexCount, "native advisory coordinates"); Equal((short?)3, mesh.DeclaredFaceCount, "native advisory faces");
        Check(mesh.RecordSequence.All(r => ReferenceEquals(r.Owner, mesh) && ReferenceEquals(r.StoredOwner, mesh) && !r.UsesBlockRecordOwner && ReferenceEquals(doc.GetObjectByHandle(r.Handle), r)), "native child actual source identities");
        Check(mesh.FaceRecords.Select(r => r.Face.VertexIndexes.Length).SequenceEqual(new[] { 1, 2, 4 }), "native point/line/quad roles");
        var output = StoredDimAssocSave(doc, binary, $"polyface-records-native-{version}-{binary}.dxf");
        var before = PolylineRecordPackets(input); var after = PolylineRecordPackets(output);
        foreach (var pair in before) PolylineRecordPacketEqual(pair.Value, after[pair.Key]);
        var header = PolyfaceRecordParent(input, mesh.Handle).Tags.ToList();
        int layer = header.FindIndex(t => t.Code == 8); header.Insert(layer, new(67, (short)0)); layer++;
        header.InsertRange(layer + 1, new DxfTag[] { new(62, (short)256), new(6, "ByLayer"), new(370, (short)-1), new(48, 1.0), new(60, (short)0) });
        Check(header.Select(PolylineRecordTagKey).SequenceEqual(PolyfaceRecordParent(output, mesh.Handle).Tags.Select(PolylineRecordTagKey)), "native complete header changed beyond the named existing common defaults");
        Equal(0, StoredDimAssocLoad(output).Objects.Validate().Count, "native polyface graph");
    }
    private static void PolyfaceRecordProducer(DxfVersion version, bool inputBinary, bool binary)
    {
        var input = PolyfaceRecordInput(version, inputBinary); var doc = StoredDimAssocLoad(input); var handles = PolyfaceRecordFixture(version, inputBinary).GetProperty("handles");
        var mesh = (PolyfaceMesh)doc.GetObjectByHandle(handles.GetProperty("mesh").GetString()!);
        Check(mesh.VertexRecords.Select(r => r.Handle).SequenceEqual(handles.GetProperty("coordinates").EnumerateArray().Select(v => v.GetString())), "producer coordinate identities");
        Check(mesh.FaceRecords.Select(r => r.Handle).SequenceEqual(handles.GetProperty("faces").EnumerateArray().Select(v => v.GetString())), "producer face identities");
        Check(mesh.RecordSequence.Where(r => !r.IsSequenceEnd).All(r => r.UsesBlockRecordOwner && ReferenceEquals(r.StoredOwner, doc.GetObjectByHandle(handles.GetProperty("block_record").GetString()!))), "producer coordinate and face block-record owners");
        Check(mesh.Faces[0].VertexIndexes.SequenceEqual(new short[] { -1, 2, -3, 4 }) && mesh.Faces[1].VertexIndexes.SequenceEqual(new short[] { 4, -5, 6 }), "signed face references");
        Check(mesh.VertexRecords[0].PersistentReactors.Contains(mesh.FaceRecords[0]) && mesh.VertexRecords[0].PersistentReactors.Contains(mesh.VertexRecords[1]) && ReferenceEquals(mesh.FaceRecords[0].PersistentReactors[0], mesh.VertexRecords[0]), "coordinate/face reactor cycle");
        Check(ReferenceEquals(mesh.FaceRecords[1].ExtensionDictionary.Owner, mesh.FaceRecords[1]) && ReferenceEquals(mesh.EndSequenceRecord.ExtensionDictionary.Owner, mesh.EndSequenceRecord), "face/SEQEND ownership graph");
        Equal(4, doc.ApplicationRegistries["POLYFACE_META"].GetReferences().Count(r => r.Reference is PolyfaceMeshRecord), "all role APPID references");
        var output = StoredDimAssocSave(doc, binary, $"polyface-records-producer-{version}-{inputBinary}-{binary}.dxf"); var after = PolylineRecordPackets(output);
        foreach (var pair in PolylineRecordPackets(input)) PolylineRecordPacketEqual(pair.Value, after[pair.Key]);
        Equal(0, StoredDimAssocLoad(output).Objects.Validate().Count, "producer output graph");
    }
    private static void PolyfaceRecordClone(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(version, binary)); var source = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var clone = (PolyfaceMesh)source.Clone(); Check(clone.RecordSequence.All(r => r.Handle == null), "clone acquired source child handles");
        var target = new DxfDocument(version); target.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX)); target.Entities.Add(clone);
        Check(clone.RecordSequence.All(r => ReferenceEquals(target.GetObjectByHandle(r.Handle), r) && ReferenceEquals(r.Owner, clone)), "clone record registration");
        Check(clone.FaceRecords.Select(r => r.Face).SequenceEqual(clone.Faces) && !ReferenceEquals(clone.Faces[0], source.Faces[0]), "clone face model identities");
        var before = (byte[])source.FaceRecords[0].XData["POLYFACE_META"].XDataRecord.Single(t => t.Code == XDataCode.BinaryData).Value;
        var after = (byte[])clone.FaceRecords[0].XData["POLYFACE_META"].XDataRecord.Single(t => t.Code == XDataCode.BinaryData).Value;
        Check(!ReferenceEquals(before, after), "clone binary XData aliases source"); after[0] = 9; Equal((byte)1, before[0], "clone binary mutation leaked"); after[0] = 1;
        var output = StoredDimAssocSave(target, !binary, $"polyface-records-clone-{version}-{binary}.dxf"); Equal(0, StoredDimAssocLoad(output).Objects.Validate().Count, "clone graph output");
        var rich = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count != 0); long seed = OwnershipSeed(doc);
        Throws<NotSupportedException>(() => rich.Clone()); Equal(seed, OwnershipSeed(doc), "owned/private clone refusal changed source seed");
    }
    private static void PolyfaceRecordEdit(DxfVersion version, bool binary)
    {
        var input = PolyfaceRecordInput(version, binary); var doc = StoredDimAssocLoad(input); var mesh = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count != 0);
        var plain = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0); Check(doc.Entities.Remove(plain), "clean mesh detach");
        var records = mesh.RecordSequence.ToArray(); mesh.Vertexes[2] = new Vector3(81, 82, 83); mesh.Faces[0].VertexIndexes[1] = -6;
        mesh.Faces[0].Layer = new Layer("EDITED_FACE"); mesh.Faces[0].Color = new AciColor(4);
        Check(doc.Layers.Remove("FACE_ONLY"), "retained face kept its old layer referenced after switching");
        var output = StoredDimAssocSave(doc, binary, $"polyface-records-edited-{version}-{binary}.dxf");
        var copy = (PolyfaceMesh)StoredDimAssocLoad(output).GetObjectByHandle(mesh.Handle);
        Check(copy.Faces[0].VertexIndexes.SequenceEqual(new short[] { -1, -6, -3, 4 }), "face index edits did not follow signed coordinate numbering");
        Equal(new Vector3(81, 82, 83), copy.Vertexes[2], "coordinate edit"); Equal("EDITED_FACE", copy.Faces[0].Layer.Name, "face layer edit"); Equal((short)4, copy.Faces[0].Color.Index, "face color edit");
        Check(records.SequenceEqual(mesh.RecordSequence), "edits replaced physical child identities");
        mesh.Faces[0].Layer = null; mesh.Faces[0].Color = null;
        output = StoredDimAssocSave(doc, binary, $"polyface-records-inherited-{version}-{binary}.dxf"); copy = (PolyfaceMesh)StoredDimAssocLoad(output).GetObjectByHandle(mesh.Handle);
        Check(copy.Faces[0].Layer == null && copy.Faces[0].Color == null, "null face resource inheritance was not represented by omitted common fields");
    }
    private static void PolyfaceRecordNullLayerRemoval(DxfVersion version, bool binary)
    {
        var authored = new DxfDocument(version);
        var baseline = new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { new PolyfaceMeshFace(new short[] { 1, 2, 3 }) });
        authored.Entities.Add(baseline); Check(baseline.Faces[0].Layer == null, "authored face baseline inherits layer");
        Check(authored.Entities.Remove(baseline), "authored null face layer removal"); authored.Entities.Add(baseline);
        Equal(0, authored.Objects.Validate().Count, "authored remove/re-adopt graph");
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(version, binary));
        var plain = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        plain.Faces[0].Layer = null; plain.Faces[0].Color = null;
        using var memory = new MemoryStream(); Check(doc.Save(memory, binary), "null face layer source save");
        doc = StoredDimAssocLoad(memory.ToArray()); plain = (PolyfaceMesh)doc.GetObjectByHandle(plain.Handle);
        Check(plain.Faces[0].Layer == null && plain.Faces[0].Color == null, "loaded null face inheritance");
        var records = plain.RecordSequence.ToArray(); var handles = records.Select(r => r.Handle).ToArray();
        Check(doc.Entities.Remove(plain), "loaded null face layer removal");
        Check(records.All(r => doc.GetObjectByHandle(r.Handle) == null && ReferenceEquals(r.Owner, plain)), "detached retained child lifecycle");
        doc.Entities.Add(plain); Check(records.SequenceEqual(plain.RecordSequence) && handles.SequenceEqual(records.Select(r => r.Handle)), "re-adoption changed retained identities");
        Check(records.All(r => ReferenceEquals(doc.GetObjectByHandle(r.Handle), r)), "re-adoption did not restore child registration");
        Equal(0, doc.Objects.Validate().Count, "loaded remove/re-adopt graph");
        StoredDimAssocSave(doc, binary, $"polyface-records-readopt-{version}-{binary}.dxf");
    }

}
