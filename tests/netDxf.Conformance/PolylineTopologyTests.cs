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
    private static void RegisterPolylineTopologyTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool input in new[] { false, true })
        {
            foreach (bool output in new[] { false, true })
            {
                Run($"polyline-topology/edit/{version}/{input}/{output}", () => PolylineTopologyEdit(version, input, output));
                Run($"polyline-topology/producer/{version}/{input}/{output}", () => PolylineTopologyProducer(version, input, output));
                Run($"polyline-topology/native/{version}/{input}/{output}", () => PolylineTopologyNative(version, input, output));
            }
            Run($"polyline-topology/clone/{version}/{input}", () => PolylineTopologyClone(version, input));
            Run($"polyline-topology/unicode/{version}/{input}", () => PolylineTopologyUnicode(version, input));
        }
        foreach (bool binary in new[] { false, true })
        {
            foreach (int fault in Enumerable.Range(0, 18)) Run($"polyline-topology/rejection/{binary}/{fault}", () => PolylineTopologyRejection(binary, fault));
            foreach (int kind in Enumerable.Range(0, 14)) Run($"polyline-topology/reference/{binary}/{kind}", () => PolylineTopologyReference(binary, kind));
            Run($"polyline-topology/minimum/{binary}", () => PolylineTopologyMinimum(binary));
            Run($"polyline-topology/low-source-seed/{binary}", () => PolylineTopologyLowSeed(binary));
            Run($"polyline-topology/retained-handle-reservation/{binary}", () => PolylineTopologyRetainedReservation(binary));
        }
        Run("polyline-topology/authored", PolylineTopologyAuthored);
        Run("polyline-topology/admission-bound", PolylineTopologyBound);
        Run("polyline-topology/tag-admission-bound", PolylineTopologyTagBound);
    }

    private static (DxfDocument Document, Polyline3D Polyline) PolylineTopologySeed(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version);
        // Equal coordinates must not collapse distinct slot identities.
        doc.Entities.Add(new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX, Vector3.Zero, Vector3.UnitY }));
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary));
        return (loaded, loaded.Entities.Polylines3D.Single());
    }

    private static void PolylineTopologyEdit(DxfVersion version, bool input, bool output)
    {
        var (doc, polyline) = PolylineTopologySeed(version, input);
        var original = polyline.VertexRecords.ToArray(); var end = polyline.EndSequenceRecord;
        long seed = OwnershipSeed(doc);
        polyline.InsertVertex(2, new Vector3(11, 12, 13)); var inserted = polyline.VertexRecords[2];
        Equal(seed + 1, OwnershipSeed(doc), "insertion must allocate exactly one handle");
        Check(!original.Contains(inserted) && ReferenceEquals(doc.GetObjectByHandle(inserted.Handle), inserted), "fresh inserted identity");
        Check(ReferenceEquals(inserted.Owner, polyline) && ReferenceEquals(inserted.StoredOwner, polyline) && ReferenceEquals(inserted.Layer, polyline.Layer), "canonical inserted owner and layer");
        Check(inserted.XData.Count == 0 && inserted.PersistentReactors.Count == 0 && inserted.ExtensionDictionary == null, "new vertex inherited metadata");
        polyline.MoveVertex(0, 4);
        Check(polyline.VertexRecords.SequenceEqual(new[] { original[1], inserted, original[2], original[3], original[0] }), "move final index semantics");
        polyline.MoveVertex(4, 0); polyline.Reverse(); polyline.Reverse();
        Check(ReferenceEquals(polyline.VertexRecords[2], inserted), "move/reverse identities");
        polyline.RemoveVertexAt(1);
        Check(original[1].IsRemoved && original[1].Owner == null && doc.GetObjectByHandle(original[1].Handle) == null, "removed record registration and lifecycle");
        Check(ReferenceEquals(polyline.EndSequenceRecord, end) && OwnershipSeed(doc) == seed + 1, "remove/move allocated handles or changed SEQEND");
        var expected = new[] { original[0], inserted, original[2], original[3] };
        Check(polyline.VertexRecords.SequenceEqual(expected), "unaffected record identities");
        string name = $"polyline-topology-edit-{version}-{input}-{output}.dxf";
        var bytes = StoredDimAssocSave(doc, output, name); var loaded = StoredDimAssocLoad(bytes); var copy = loaded.Entities.Polylines3D.Single();
        Check(copy.VertexRecords.Select(r => r.Handle).SequenceEqual(expected.Select(r => r.Handle)) && copy.Vertexes.SequenceEqual(polyline.Vertexes), "topology reload mismatch");
        Equal(end.Handle, copy.EndSequenceRecord.Handle, "stable SEQEND across topology reload");
        Equal(0, loaded.Objects.Validate().Count, "topology output graph");
        var repeated = PolylineRecordPackets(StoredDimAssocSave(doc, output));
        foreach (var packet in PolylineRecordPackets(bytes)) PolylineRecordPacketEqual(packet.Value, repeated[packet.Key]);
    }

    private static void PolylineTopologyProducer(DxfVersion version, bool input, bool output)
    {
        byte[] bytes = PolylineRecordInput(version, input); var doc = StoredDimAssocLoad(bytes);
        var handles = PolylineRecordFixture(version, input).GetProperty("handles");
        var polyline = (Polyline3D)doc.GetObjectByHandle(handles.GetProperty("polyline").GetString()!);
        var before = polyline.VertexRecords.ToArray(); var end = polyline.EndSequenceRecord; long seed = OwnershipSeed(doc);
        polyline.InsertVertex(1, new Vector3(11, 12, 13)); var inserted = polyline.VertexRecords[1];
        polyline.MoveVertex(0, 3);
        Check(polyline.VertexRecords.SequenceEqual(new[] { inserted, before[1], before[2], before[0] }), "producer identity permutation");
        Check(before.All(r => r.UsesBlockRecordOwner) && !inserted.UsesBlockRecordOwner, "mixed legitimate owner forms");
        Throws<NotSupportedException>(() => polyline.RemoveVertexAt(1));
        Check(OwnershipSeed(doc) == seed + 1 && ReferenceEquals(end, polyline.EndSequenceRecord), "producer refusal changed identities");
        var result = StoredDimAssocSave(doc, output, $"polyline-topology-producer-{version}-{input}-{output}.dxf");
        var packets = PolylineRecordPackets(result);
        foreach (var pair in PolylineRecordPackets(bytes)) PolylineRecordPacketEqual(pair.Value, packets[pair.Key]);
        Equal(0, StoredDimAssocLoad(result).Objects.Validate().Count, "producer topology graph");
    }

    private static void PolylineTopologyNative(DxfVersion version, bool input, bool output)
    {
        byte[] bytes = StoredDimAssocInput(version, input); var doc = StoredDimAssocLoad(bytes);
        var polyline = (Polyline3D)doc.GetObjectByHandle(StoredDimAssocHandle(version, "41A"));
        var target = polyline.VertexRecords.Single(r => r.Handle == StoredDimAssocHandle(version, "41E"));
        long seed = OwnershipSeed(doc); var original = polyline.VertexRecords.ToArray();
        Throws<NotSupportedException>(() => polyline.RemoveVertexAt(Array.IndexOf(original, target)));
        Equal(seed, OwnershipSeed(doc), "native dependency refusal allocated handles");
        polyline.InsertVertex(0, new Vector3(11, 12, 13)); var inserted = polyline.VertexRecords[0];
        polyline.MoveVertex(0, polyline.Vertexes.Count - 1); polyline.RemoveVertexAt(polyline.Vertexes.Count - 1);
        Check(inserted.IsRemoved && polyline.VertexRecords.SequenceEqual(original), "native insert/move/remove did not restore identity slots");
        byte[] result = StoredDimAssocSave(doc, output, $"polyline-topology-native-{version}-{input}-{output}.dxf");
        var packets = PolylineRecordPackets(result);
        foreach (var pair in PolylineRecordPackets(bytes)) PolylineRecordPacketEqual(pair.Value, packets[pair.Key]);
        Equal(0, StoredDimAssocLoad(result).Objects.Validate().Count, "native dependency graph after topology edits");
    }

    private static void PolylineTopologyClone(DxfVersion version, bool binary)
    {
        var (doc, source) = PolylineTopologySeed(version, binary); var clone = (Polyline3D)source.Clone();
        Throws<InvalidOperationException>(() => clone.InsertVertex(0, Vector3.UnitZ));
        Throws<InvalidOperationException>(() => clone.RemoveVertexAt(0));
        Throws<InvalidOperationException>(() => clone.MoveVertex(0, 1));
        Check(clone.VertexRecords.All(r => r.Handle == null), "detached clone rejection allocated handles");
        var destination = new DxfDocument(version); destination.Entities.Add(clone);
        clone.InsertVertex(0, new Vector3(11, 12, 13)); clone.RemoveVertexAt(2); clone.MoveVertex(0, 3);
        var copy = (Polyline3D)clone.Clone(); var second = new DxfDocument(version); second.Entities.Add(copy);
        Check(copy.VertexRecords.All(r => !r.IsRemoved) && copy.Vertexes.SequenceEqual(clone.Vertexes), "edited clone state");
        Equal(0, StoredDimAssocLoad(StoredDimAssocSave(second, binary, $"polyline-topology-clone-{version}-{binary}.dxf")).Objects.Validate().Count, "edited clone output graph");
        Equal(4, source.Vertexes.Count, "clone topology edit changed source");
    }

    private static void PolylineTopologyUnicode(DxfVersion version, bool binary)
    {
        var (doc, polyline) = PolylineTopologySeed(version, binary);
        polyline.Layer = new Layer("Łódź层"); polyline.InsertVertex(0, new Vector3(11, 12, 13));
        string child = polyline.VertexRecords[0].Handle;
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"polyline-topology-unicode-{version}-{binary}.dxf"));
        Equal("Łódź层", ((Polyline3DRecord)loaded.GetObjectByHandle(child)).Layer.Name, "inserted resource text encoding");
    }

    private static void PolylineTopologyRejection(bool binary, int fault)
    {
        var (doc, polyline) = PolylineTopologySeed(DxfVersion.AutoCad2018, binary);
        Action edit = () => polyline.InsertVertex(1, Vector3.UnitZ);
        if (fault == 0) edit = () => polyline.InsertVertex(-1, Vector3.Zero);
        else if (fault == 1) edit = () => polyline.InsertVertex(5, Vector3.Zero);
        else if (fault == 2) edit = () => polyline.InsertVertex(1, new Vector3(double.NaN, 0, 0));
        else if (fault == 3) edit = () => polyline.InsertVertex(1, new Vector3(0, double.PositiveInfinity, 0));
        else if (fault == 4) edit = () => polyline.RemoveVertexAt(4);
        else if (fault == 5) edit = () => polyline.RemoveVertexAt(-1);
        else if (fault == 6) edit = () => polyline.MoveVertex(-1, 0);
        else if (fault == 7) edit = () => polyline.MoveVertex(0, 4);
        else if (fault == 8) polyline.Vertexes.Add(Vector3.UnitZ);
        else if (fault == 9) { polyline.Vertexes.RemoveAt(0); edit = () => polyline.RemoveVertexAt(0); }
        else if (fault == 10) { polyline.SmoothType = PolylineSmoothType.Cubic; edit = () => polyline.MoveVertex(0, 0); }
        else if (fault == 11) { polyline.Vertexes[0] = new Vector3(0, 0, double.NegativeInfinity); edit = () => polyline.MoveVertex(0, 0); }
        else if (fault == 12) doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
        else if (fault == 13) { Check(doc.Entities.Remove(polyline), "detach fixture"); }
        else if (fault == 14) { Check(doc.Entities.Remove(polyline), "foreign fixture detach"); edit = () => new DxfDocument().Entities.Add(polyline); }
        else if (fault == 15) { doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000; edit = () => polyline.RemoveVertexAt(0); }
        else if (fault == 16) { doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000; edit = () => polyline.MoveVertex(0, 1); }
        else { typeof(DxfDocument).GetProperty("NumHandles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(doc, long.MaxValue); }
        var records = polyline.VertexRecords.ToArray(); var points = polyline.Vertexes.ToArray(); var end = polyline.EndSequenceRecord; long seed = OwnershipSeed(doc);
        int callbacks = 0; foreach (var record in records) { record.XDataAddAppReg += (_, _) => callbacks++; record.XDataRemoveAppReg += (_, _) => callbacks++; }
        bool rejected = false; try { edit(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException) { rejected = true; }
        Check(rejected && records.SequenceEqual(polyline.VertexRecords) && points.Zip(polyline.Vertexes).All(pair => pair.First.X.Equals(pair.Second.X) && pair.First.Y.Equals(pair.Second.Y) && pair.First.Z.Equals(pair.Second.Z))
            && ReferenceEquals(end, polyline.EndSequenceRecord) && OwnershipSeed(doc) == seed && callbacks == 0 && records.All(r => !r.IsRemoved), "rejected topology edit mutated records, points, metadata or seed");
    }

    private static void PolylineTopologyReference(bool binary, int kind)
    {
        var (doc, polyline) = PolylineTopologySeed(DxfVersion.AutoCad2018, binary); var target = polyline.VertexRecords[1];
        var other = new Line(Vector3.Zero, Vector3.UnitX); doc.Entities.Add(other);
        bool arbitrary = kind == 8 || kind == 9;
        if (kind == 0) other.PersistentReactors.Add(target);
        else if (kind == 1) polyline.VertexRecords[0].PersistentReactors.Add(target);
        else if (kind == 2 || kind == 3)
        {
            var data = new XData(new ApplicationRegistry("TOPOLOGY_REF")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "000" + target.Handle.ToLowerInvariant()));
            (kind == 2 ? (DxfObject)other : polyline).XData.Add(data);
        }
        else if (kind is 4 or 5 or 8)
        {
            var record = new DxfXRecord(); record.Data.Add(new DxfTag(kind == 8 ? (short)320 : kind == 4 ? (short)330 : (short)340, "000" + target.Handle.ToLowerInvariant())); doc.Objects.Root.Add("TOPOLOGY_REF", record);
        }
        else if (kind is 6 or 7 or 9) doc.DrawingVariables.AddCustomVariable(new HeaderVariable("$TOPOLOGY_REF", kind == 9 ? (short)329 : kind == 6 ? (short)340 : (short)5, target.Handle));
        else if (kind == 10) { var buffer = new DxfIdBuffer(); buffer.References.Add(target); doc.Objects.Root.Add("TOPOLOGY_REF", buffer); }
        else if (kind == 11) { var extension = new DxfDictionary(); extension.Add("PAYLOAD", new DxfXRecord()); doc.Objects.SetExtensionDictionary(target, extension); }
        else if (kind == 12) { var data = new XData(new ApplicationRegistry("TOPOLOGY_SELF")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, "remove safe metadata")); target.XData.Add(data); arbitrary = true; }
        else { target.PersistentReactors.Add(other); arbitrary = true; }
        long seed = OwnershipSeed(doc); var records = polyline.VertexRecords.ToArray();
        if (!arbitrary) Throws<NotSupportedException>(() => polyline.RemoveVertexAt(1)); else polyline.RemoveVertexAt(1);
        Equal(seed, OwnershipSeed(doc), "reference check consumed handles");
        Check(arbitrary ? target.IsRemoved && target.Owner == null && doc.GetObjectByHandle(target.Handle) == null : records.SequenceEqual(polyline.VertexRecords) && ReferenceEquals(doc.GetObjectByHandle(target.Handle), target), "reference or arbitrary-handle removal policy");
        if (kind == 12) Check(doc.ApplicationRegistries.Remove("TOPOLOGY_SELF"), "removed child APPID subscription/reference was retained");
    }

    private static void PolylineTopologyMinimum(bool binary)
    {
        var (doc, polyline) = PolylineTopologySeed(DxfVersion.AutoCad2018, binary); string end = polyline.EndSequenceRecord.Handle;
        while (polyline.Vertexes.Count > 2) polyline.RemoveVertexAt(0);
        long seed = OwnershipSeed(doc); var records = polyline.VertexRecords.ToArray();
        Throws<InvalidOperationException>(() => polyline.RemoveVertexAt(0));
        Check(records.SequenceEqual(polyline.VertexRecords) && OwnershipSeed(doc) == seed, "minimum point guard mutated the sequence");
        polyline.InsertVertex(0, new Vector3(11, 12, 13));
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"polyline-topology-minimum-{binary}.dxf"));
        Equal(end, loaded.Entities.Polylines3D.Single().EndSequenceRecord.Handle, "minimum/refilled SEQEND identity");
    }

    private static void PolylineTopologyLowSeed(bool binary)
    {
        var (original, _) = PolylineTopologySeed(DxfVersion.AutoCad2018, binary);
        var raw = DxfRawDocument.Load(new MemoryStream(StoredDimAssocSave(original, binary)));
        var tags = raw.Tags.ToList(); int seedIndex = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$HANDSEED")); tags[seedIndex + 1] = new DxfTag(5, "1");
        DxfDocument? loaded;
        try { using var input = new MemoryStream(StoredDimAssocRawBytes(DxfRawDocument.Create(tags), binary)); loaded = DxfDocument.Load(input); }
        catch (Exception error) when (error is ArgumentException or FormatException) { loaded = null; }
        // The existing table reader can reject this low seed before reaching POLYLINE.
        // Rejection is safe; a successfully admitted drawing must not reuse a physical identity.
        if (loaded == null) return;
        var polyline = loaded.Entities.Polylines3D.Single();
        var existing = raw.Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records).SelectMany(r => r.Tags.Where(t => t.Code == 5)).Select(t => (string)t.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        polyline.InsertVertex(0, Vector3.UnitZ);
        Check(!existing.Contains(polyline.VertexRecords[0].Handle), "authored insertion reused a physical source handle from an advertised low seed");
    }

    private static void PolylineTopologyAuthored()
    {
        var polyline = new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX });
        polyline.InsertVertex(1, Vector3.UnitY); polyline.MoveVertex(0, 2); polyline.RemoveVertexAt(1);
        Check(polyline.Vertexes.SequenceEqual(new[] { Vector3.UnitY, Vector3.Zero }) && polyline.VertexRecords.Count == 0 && polyline.EndSequenceRecord == null, "authored topology semantics");
        Throws<ArgumentException>(() => polyline.InsertVertex(0, new Vector3(double.NaN, 0, 0)));
    }

    private static void PolylineTopologyRetainedReservation(bool binary)
    {
        var (doc, polyline) = PolylineTopologySeed(DxfVersion.AutoCad2018, binary);
        var block = new Block("TOPOLOGY_HANDLE_RESERVATION"); block.AttributeDefinitions.Add(new AttributeDefinition("TAG"));
        doc.Entities.Add(new Insert(block));
        byte[] source = StoredDimAssocSave(doc, binary); var loaded = StoredDimAssocLoad(source); polyline = loaded.Entities.Polylines3D.Single();
        var physical = PolylineRecordRaw(source).Sections.Where(s => s.Name != "HEADER").SelectMany(s => s.Records)
            .SelectMany(r => r.Tags.Where(t => t.Code == 5)).Select(t => (string)t.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var attribute = loaded.Entities.Inserts.Single().Attributes.Single();
        Check(physical.Contains(attribute.Handle) && loaded.GetObjectByHandle(attribute.Handle) == null, "probe must exercise an owner-held physical identity outside AddedObjects");
        // Deliberately lower the private allocator counter to exercise reservation independently
        // of the normal input seed normalization. This is not claimed as native producer input.
        typeof(DxfDocument).GetProperty("NumHandles", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(loaded, Convert.ToInt64(attribute.Handle, 16));
        polyline.InsertVertex(0, Vector3.UnitZ);
        Check(!physical.Contains(polyline.VertexRecords[0].Handle) && attribute.Owner != null, "inserted VERTEX captured an ATTRIB/ENDBLK source identity");
    }

    private static void PolylineTopologyTagBound()
    {
        var doc = new DxfDocument(); doc.Entities.Add(new Polyline3D(Enumerable.Repeat(Vector3.Zero, 300)));
        var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, false)); var polyline = loaded.Entities.Polylines3D.Single();
        var data = new XData(new ApplicationRegistry("TOPOLOGY_TAG_BUDGET"));
        for (int i = 0; i < 3500; i++) data.XDataRecord.Add(new XDataRecord(XDataCode.String, "stored"));
        foreach (var record in polyline.VertexRecords) record.XData.Add(data);
        long seed = OwnershipSeed(loaded); var records = polyline.VertexRecords.ToArray();
        Throws<NotSupportedException>(() => polyline.InsertVertex(0, Vector3.Zero));
        Check(records.SequenceEqual(polyline.VertexRecords) && OwnershipSeed(loaded) == seed, "global tag admission failure mutated records or seed");
    }

    private static void PolylineTopologyBound()
    {
        var polyline = new Polyline3D(Enumerable.Repeat(Vector3.Zero, 65536));
        Throws<NotSupportedException>(() => polyline.InsertVertex(0, Vector3.Zero));
        polyline.RemoveVertexAt(0); polyline.InsertVertex(65535, Vector3.UnitX); Equal(65536, polyline.Vertexes.Count, "shared topology admission bound");
    }
}
