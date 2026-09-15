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
    private static void RegisterPolyline2DRecordAdvancedTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"legacy2d-records/representation/{version}/{binary}", () => Legacy2DRepresentation(version, binary));
            Run($"legacy2d-records/incoming/{version}/{binary}", () => Legacy2DIncoming(version, binary));
            foreach (bool end in new[] { false, true }) foreach (bool reactor in new[] { false, true }) foreach (bool block in new[] { false, true })
                Run($"legacy2d-records/outgoing/{version}/{binary}/{end}/{reactor}/{block}", () => Legacy2DOutgoing(version, binary, end, reactor, block));
        }
        foreach (bool binary in new[] { false, true })
        {
            Run($"legacy2d-records/source-profile/{binary}", () => Legacy2DSourceProfile(binary));
            Run($"legacy2d-records/owned-erasure/{binary}", () => Legacy2DOwnedErasure(binary));
            foreach (bool end in new[] { false, true }) foreach (bool xyz in new[] { false, true })
                Run($"legacy2d-records/tag-budget/{binary}/{end}/{xyz}", () => Legacy2DBudget(binary, end, xyz));
            Run($"legacy2d-records/transform-overflow/{binary}", () => Legacy2DTransformOverflow(binary));
            Run($"legacy2d-records/sparse-header/{binary}", () => Legacy2DSparseHeader(binary));
            foreach (int count in new[] { 0, 1 }) Run($"legacy2d-records/degenerate/{binary}/{count}", () => Legacy2DDegenerate(binary, count));
            for (int mode = 0; mode < 3; mode++) { int m = mode; Run($"legacy2d-records/default-presence/{binary}/{m}", () => Legacy2DDefaultPresence(binary, m)); }
        }
    }
    private static void Legacy2DRepresentation(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); var ordinary = new Polyline2D(new[] { new Vector2(1, 2), new Vector2(4, 7), new Vector2(8, 12), new Vector2(13, 19) });
        var fitted = (Polyline2D)ordinary.Clone(); fitted.SmoothType = PolylineSmoothType.Cubic;
        doc.Entities.Add(ordinary); doc.Entities.Add(fitted); var output = StoredDimAssocSave(doc, binary);
        var copy = StoredDimAssocLoad(output); var lightweight = (Polyline2D)copy.GetObjectByHandle(ordinary.Handle); var spline = (Polyline2D)copy.GetObjectByHandle(fitted.Handle);
        Equal("LWPOLYLINE", lightweight.CodeName, "authored ordinary representation"); Equal(0, lightweight.VertexRecords.Count, "lightweight acquired legacy records");
        Equal(PolylineSmoothType.Cubic, spline.SmoothType, "existing fitted reader path"); Equal(0, spline.VertexRecords.Count, "fitted path claimed retained metadata");
        Equal(4, spline.Vertexes.Count, "fitted control point count");
        if (version < DxfVersion.AutoCad2013)
        {
            ordinary.Vertexes[0].VertexIdentifier = 7; long seed = OwnershipSeed(doc); using var stream = new MemoryStream(); stream.WriteByte(11); bool rejected = false;
            try { rejected = !doc.Save(stream, binary); } catch (NotSupportedException) { rejected = true; }
            Check(rejected && stream.Length == 1 && seed == OwnershipSeed(doc), "authored LWP identifier profile guard changed");
        }
    }
    private static void Legacy2DIncoming(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var line = Legacy2DPolyline(doc, version, binary, true); var record = line.VertexRecords[0];
        var xr = new DxfXRecord(); xr.Data.Add(new DxfTag(330, record.Handle)); doc.NamedObjects.Add("LEGACY_INCOMING", xr);
        long seed = OwnershipSeed(doc); Check(!doc.Entities.Remove(line) && seed == OwnershipSeed(doc) && ReferenceEquals(doc.GetObjectByHandle(record.Handle), record), "incoming actual child reference allowed removal");
        xr.Data.Clear(); xr.Data.Add(new DxfTag(320, record.Handle)); Check(doc.Entities.Remove(line), "arbitrary320 blocked child removal");
        Check(line.VertexRecords.All(r => ReferenceEquals(r.Owner, line) && doc.GetObjectByHandle(r.Handle) == null), "child detach state");
        doc.Entities.Add(line); Check(ReferenceEquals(doc.GetObjectByHandle(record.Handle), record), "same source readoption identity");
    }
    private static void Legacy2DOutgoing(DxfVersion version, bool binary, bool end, bool reactor, bool containingBlock)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var line = Legacy2DPolyline(doc, version, binary, true);
        var target = new Line(Vector3.Zero, Vector3.UnitX); Block? block = null;
        if (containingBlock) { block = new Block("LEGACY_TARGET_BLOCK"); block.Entities.Add(target); doc.Blocks.Add(block); }
        else doc.Entities.Add(target);
        var child = end ? line.EndSequenceRecord : line.VertexRecords[0];
        if (reactor) child.PersistentReactors.Add(target);
        else { var data = new XData(new ApplicationRegistry("LEGACY_LINK")); data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, target.Handle)); child.XData.Add(data); }
        int count = doc.Entities.All.Count(); long seed = OwnershipSeed(doc);
        Check(!(containingBlock ? doc.Blocks.Remove(block!) : doc.Entities.Remove(target)) && seed == OwnershipSeed(doc) && count == doc.Entities.All.Count(), "retained common metadata allowed ordinary target removal");
        if (reactor) child.PersistentReactors.Clear(); else child.XData.Remove("LEGACY_LINK");
        Check(containingBlock ? doc.Blocks.Remove(block!) : doc.Entities.Remove(target), "released reference still blocked ordinary target removal");
    }
    private static void Legacy2DSourceProfile(bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018, binary)); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary, true);
        var clone = (Polyline2D)line.Clone(); var other = new DxfDocument(DxfVersion.AutoCad2000); var objects = other.Objects.Items.ToArray(); long seed = OwnershipSeed(other);
        Throws<NotSupportedException>(() => other.Entities.Add(clone)); Check(clone.Owner == null && clone.VertexRecords.All(r => r.Handle == null) && seed == OwnershipSeed(other) && objects.SequenceEqual(other.Objects.Items), "profile clone adoption was not atomic");
        var current = new DxfDocument(DxfVersion.AutoCad2018); _ = current.Objects.Items.Count(); seed = OwnershipSeed(current);
        Check(doc.Entities.Remove(line), "clean source detach"); Throws<NotSupportedException>(() => current.Entities.Add(line));
        Check(line.Owner == null && seed == OwnershipSeed(current), "foreign retained adoption changed destination");
        doc.Entities.Add(line); doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000; seed = OwnershipSeed(doc); using var output = new MemoryStream(); output.WriteByte(33); bool rejected = false;
        try { rejected = !doc.Save(output, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && output.Length == 1 && seed == OwnershipSeed(doc), "changed source profile output was not atomic");
    }
    private static void Legacy2DOwnedErasure(bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018, binary)); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary);
        var vertex = line.VertexRecords[2]; var end = line.EndSequenceRecord;
        doc.Objects.EraseOwnedTree(vertex.ExtensionDictionary); doc.Objects.EraseOwnedTree(end.ExtensionDictionary);
        Check(vertex.ExtensionDictionary == null && end.ExtensionDictionary == null, "explicit owned metadata erasure not attached to actual child");
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-owned-erased-{binary}.dxf"); var copy = (Polyline2D)StoredDimAssocLoad(output).GetObjectByHandle(line.Handle);
        Check(copy.VertexRecords[2].ExtensionDictionary == null && copy.EndSequenceRecord.ExtensionDictionary == null, "erased extension packet survived");
        Check(doc.Entities.Remove(line), "released owned child metadata still blocked parent removal");
    }
    private static void Legacy2DBudget(bool binary, bool end, bool xyz)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018, binary)); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary, true); var child = end ? line.EndSequenceRecord : line.VertexRecords[0];
        var packets = PolylineRecordPackets(StoredDimAssocSave(doc, binary)); var data = new XData(new ApplicationRegistry("LEGACY_BUDGET"));
        for (int i = packets[child.Handle].Tags.Count; i < 4096; i++)
            data.XDataRecord.Add(xyz ? new XDataRecord(new[] { XDataCode.WorldSpacePositionX, XDataCode.WorldSpacePositionY, XDataCode.WorldSpacePositionZ }[(i - packets[child.Handle].Tags.Count) % 3], 1.0) : new XDataRecord(XDataCode.Int16, (short)1));
        child.XData.Add(data); var exact = StoredDimAssocSave(doc, binary); Equal(4097, PolylineRecordPackets(exact)[child.Handle].Tags.Count, "physical4096 plus opening marker"); Check(StoredDimAssocLoad(exact) != null, "exact child budget rejected");
        data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)2)); long seed = OwnershipSeed(doc); using var output = new MemoryStream(); output.WriteByte(73); bool rejected = false;
        try { rejected = !doc.Save(output, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && output.Length == 1 && seed == OwnershipSeed(doc), "child budget save changed output/allocation");
        Throws<NotSupportedException>(() => line.Clone());
    }
    private static void Legacy2DTransformOverflow(bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018, binary)); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary, true);
        var points = line.Vertexes.Select(v => v.Position).ToArray(); var ids = line.VertexRecords.ToArray(); var elevation = line.Elevation;
        bool rejected = false; try { line.TransformBy(Matrix3.Identity, new Vector3(double.PositiveInfinity, 0, 0)); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && points.SequenceEqual(line.Vertexes.Select(v => v.Position)) && ids.SequenceEqual(line.VertexRecords) && elevation == line.Elevation, "transform overflow partially mutated geometry");
    }
    private static void Legacy2DDefaultPresence(bool binary, int mode)
    {
        var input = Legacy2DInput(DxfVersion.AutoCad2018, binary); string parent = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles").GetProperty("plain_polyline").GetString()!;
        var raw = ObjectStoreReplaceRecord(PolylineRecordRaw(input), parent, tags =>
        {
            if (mode != 1) tags.RemoveAll(t => t.Code == 40);
            if (mode != 0) tags.RemoveAll(t => t.Code == 41);
            return tags;
        });
        var doc = StoredDimAssocLoad(StoredDimAssocRawBytes(raw, binary)); var line = (Polyline2D)doc.GetObjectByHandle(parent);
        double? start = mode == 1 ? 2.0 : null, end = mode == 0 ? 3.0 : null;
        Equal(start, line.LegacyDefaultStartWidth, "omitted default start"); Equal(end, line.LegacyDefaultEndWidth, "omitted default end");
        line.Reverse(); Equal(end, line.LegacyDefaultStartWidth, "reversed optional default start"); Equal(start, line.LegacyDefaultEndWidth, "reversed optional default end");
        line.TransformBy(Matrix3.Scale(2), Vector3.Zero);
        var copy = (Polyline2D)StoredDimAssocLoad(StoredDimAssocSave(doc, binary)).GetObjectByHandle(parent);
        Equal(end * 2, copy.LegacyDefaultStartWidth, "scaled nullable start on wire"); Equal(start * 2, copy.LegacyDefaultEndWidth, "scaled nullable end on wire");
        Equal((end * 2).GetValueOrDefault(), copy.GetEffectiveStartWidth(0), "omitted default effective geometry");
    }
    private static void Legacy2DSparseHeader(bool binary)
    {
        var input = Legacy2DInput(DxfVersion.AutoCad2018, binary); string parent = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles").GetProperty("plain_polyline").GetString()!;
        var raw = ObjectStoreReplaceRecord(PolylineRecordRaw(input), parent, tags => { tags.RemoveAll(t => t.Code == 10 || t.Code == 20 || t.Code == 30); tags.Add(new DxfTag(100, "PrivateHeaderTail")); tags.Add(new DxfTag(1, "private marker")); return tags; });
        input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var line = (Polyline2D)doc.GetObjectByHandle(parent);
        var unchanged = StoredDimAssocSave(doc, binary, $"legacy2d-records-sparse-header-{binary}.dxf");
        var before = PolyfaceRecordParent(input, parent).Tags; var after = PolyfaceRecordParent(unchanged, parent).Tags;
        int first = before.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline")); int second = after.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline"));
        Check(before.Skip(first).Select(PolylineRecordTagKey).SequenceEqual(after.Skip(second).Select(PolylineRecordTagKey)), "omitted parent dummy point was materialized without an edit");
        line.Elevation = 7; var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-sparse-header-edited-{binary}.dxf"); var packet = PolyfaceRecordParent(output, parent).Tags.ToList();
        int point = packet.FindIndex(t => t.Code == 10); Check(point >= 0 && packet[point + 1].Code == 20 && packet[point + 2].Code == 30, "elevation edit failed to emit a complete contiguous parent point");
        Check(point < packet.FindIndex(t => t.Code == 100 && Equals(t.Value, "PrivateHeaderTail")), "new parent point was inserted into a private subclass");
        Equal(7.0, ((Polyline2D)StoredDimAssocLoad(output).GetObjectByHandle(parent)).Elevation, "sparse parent elevation edit roundtrip");
        foreach (short code in new short[] { 10, 20, 30 })
        {
            var malformed = ObjectStoreReplaceRecord(PolylineRecordRaw(Legacy2DInput(DxfVersion.AutoCad2018, binary)), parent, tags => { tags.RemoveAll(t => t.Code == code); return tags; }); bool rejected = false;
            try { rejected = DxfDocument.Load(new MemoryStream(StoredDimAssocRawBytes(malformed, binary))) == null; } catch (FormatException) { rejected = true; }
            Check(rejected, "partial legacy parent dummy vector admitted");
        }
    }
    private static void Legacy2DDegenerate(bool binary, int count)
    {
        var input = Legacy2DInput(DxfVersion.AutoCad2018, binary); var handles = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        string parent = handles.GetProperty("plain_polyline").GetString()!;
        var removed = handles.GetProperty("plain_vertices").EnumerateArray().Skip(count).Select(v => v.GetString()).ToHashSet();
        var raw = PolylineRecordRaw(input);
        foreach (string? handle in removed) raw = ObjectStoreReplaceRecord(raw, handle!, _ => new List<DxfTag>());
        foreach (string handle in new[] { parent, handles.GetProperty("plain_seqend").GetString()! })
            raw = ObjectStoreReplaceRecord(raw, handle, tags => { tags.Add(new DxfTag(1001, "LEGACY2D_META")); tags.Add(new DxfTag(1000, "degenerate metadata")); tags.Add(new DxfTag(1004, new byte[] { 1, 0, 255 })); return tags; });
        input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var line = (Polyline2D)doc.GetObjectByHandle(parent); var end = line.EndSequenceRecord;
        Equal(count, line.Vertexes.Count, "degenerate source model count"); Equal(count, line.VertexRecords.Count, "degenerate physical record count");
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-degenerate-{binary}-{count}.dxf");
        foreach (var pair in PolylineRecordPackets(input)) PolylineRecordPacketEqual(pair.Value, PolylineRecordPackets(output)[pair.Key]);
        Equal(count, ((Polyline2D)StoredDimAssocLoad(output).GetObjectByHandle(parent)).VertexRecords.Count, "degenerate save/reload representation");
        line.Reverse(); Check(ReferenceEquals(end, line.EndSequenceRecord) && line.LegacyDefaultStartWidth == 2 && line.LegacyDefaultEndWidth == 3, "degenerate Reverse must be a no-op");
        var clone = (Polyline2D)line.Clone(); var target = new DxfDocument(DxfVersion.AutoCad2018); target.Entities.Add(clone);
        var cloneOutput = StoredDimAssocSave(target, binary); Equal(count, ((Polyline2D)StoredDimAssocLoad(cloneOutput).GetObjectByHandle(clone.Handle)).VertexRecords.Count, "degenerate clone save/reload");
        Check(clone.EndSequenceRecord.XData.ContainsAppId("LEGACY2D_META") && clone.XData.ContainsAppId("LEGACY2D_META"), "degenerate clone common metadata");
        Equal(count, clone.VertexRecords.Count, "degenerate clone count"); Check(clone.EndSequenceRecord.Handle != null && !ReferenceEquals(clone.EndSequenceRecord, end), "degenerate cloned terminator identity");
        Check(doc.Entities.Remove(line) && doc.GetObjectByHandle(end.Handle) == null && ReferenceEquals(end.Owner, line), "degenerate detach lifecycle"); doc.Entities.Add(line);
        var records = line.VertexRecords.ToArray(); var points = line.Vertexes.Select(v => v.Position).ToArray(); line.TransformBy(Matrix3.Scale(2), new Vector3(5, 7, 11));
        Check(records.SequenceEqual(line.VertexRecords) && ReferenceEquals(end, line.EndSequenceRecord), "degenerate transform changed identities");
        Equal(11.0, line.Elevation, "degenerate transformed plane"); Equal((double?)4, line.LegacyDefaultStartWidth, "degenerate scaled default width");
        if (count == 1) Equal(points[0] * 2 + new Vector2(5, 7), line.Vertexes[0].Position, "single-point transform");
        output = StoredDimAssocSave(doc, binary, $"legacy2d-records-degenerate-transformed-{binary}-{count}.dxf");
        var copy = (Polyline2D)StoredDimAssocLoad(output).GetObjectByHandle(line.Handle); Equal(count, copy.VertexRecords.Count, "transformed degenerate representation"); Equal(11.0, copy.Elevation, "transformed degenerate plane persisted");
    }
}
