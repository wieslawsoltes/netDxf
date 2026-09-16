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
    private static void RegisterPolyline2DRecordTests()
    {
        RegisterPolyline2DRecordAdvancedTests();
        foreach (bool binary in new[] { false, true }) Run($"legacy2d-records/native/{binary}", () => Legacy2DNative(binary));
        foreach (DxfVersion version in SupportedVersions) foreach (bool inputBinary in new[] { false, true })
        {
            foreach (bool binary in new[] { false, true }) Run($"legacy2d-records/producer/{version}/{inputBinary}/{binary}", () => Legacy2DProducer(version, inputBinary, binary));
            Run($"legacy2d-records/clone/{version}/{inputBinary}", () => Legacy2DClone(version, inputBinary));
            Run($"legacy2d-records/reverse/{version}/{inputBinary}", () => Legacy2DReverse(version, inputBinary));
            Run($"legacy2d-records/transform/{version}/{inputBinary}", () => Legacy2DTransform(version, inputBinary));
            Run($"legacy2d-records/edits/{version}/{inputBinary}", () => Legacy2DEdits(version, inputBinary));
        }
        foreach (bool binary in new[] { false, true })
        {
            Run($"legacy2d-records/explicit-zero/{binary}", () => Legacy2DExplicitZero(binary));
            for (int fault = 0; fault < 16; fault++) { int f = fault; Run($"legacy2d-records/malformed/{binary}/{f}", () => Legacy2DMalformed(binary, f)); }
            for (int fault = 0; fault < 9; fault++) { int f = fault; Run($"legacy2d-records/invalid-state/{binary}/{f}", () => Legacy2DInvalid(binary, f)); }
            for (int variant = 0; variant < 4; variant++) { int v = variant; Run($"legacy2d-records/private/{binary}/{v}", () => Legacy2DPrivate(binary, v)); }
        }
    }
    private static JsonElement Legacy2DFixture(DxfVersion version, bool binary)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText("tests/fixtures/polyline2d-records/manifest.json"));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("year").GetInt32() == int.Parse(version.ToString()[7..]) && f.GetProperty("binary").GetBoolean() == binary).Clone();
    }
    private static byte[] Legacy2DInput(DxfVersion version, bool binary)
    {
        var fixture = Legacy2DFixture(version, binary); var bytes = File.ReadAllBytes("tests/fixtures/polyline2d-records/" + fixture.GetProperty("file").GetString());
        Equal(fixture.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "unchanged producer hash"); return bytes;
    }
    private static Polyline2D Legacy2DPolyline(DxfDocument doc, DxfVersion version, bool binary, bool plain = false)
        => (Polyline2D)doc.GetObjectByHandle(Legacy2DFixture(version, binary).GetProperty("handles").GetProperty(plain ? "plain_polyline" : "polyline").GetString()!);
    private static void Legacy2DNative(bool binary)
    {
        var input = File.ReadAllBytes("tests/fixtures/polyline2d-records/native-R2000.dxf"); var doc = StoredDimAssocLoad(input);
        foreach (var pair in new[] { (Handle: "1EF", Width: 0.15, Closed: false), (Handle: "1FF", Width: 0.5, Closed: true) })
        {
            var line = (Polyline2D)doc.GetObjectByHandle(pair.Handle); Equal(2, line.VertexRecords.Count, "native vertex count");
            Equal(pair.Closed, line.IsClosed, "native closure"); Equal((double?)pair.Width, line.LegacyDefaultStartWidth, "native default start width");
            Equal((double?)pair.Width, line.LegacyDefaultEndWidth, "native default end width");
            Check(line.Vertexes.All(v => !v.StartWidthOverride.HasValue && !v.EndWidthOverride.HasValue), "native absent overrides materialized");
            Check(line.VertexRecords.All(r => ReferenceEquals(r.StoredOwner, line) && !r.UsesBlockRecordOwner), "native parent owner identity");
            Equal(pair.Width, line.GetEffectiveStartWidth(0), "native inherited effective width");
        }
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-native-{binary}.dxf");
        foreach (var pair in PolylineRecordPackets(input)) PolylineRecordPacketEqual(pair.Value, PolylineRecordPackets(output)[pair.Key]);
        foreach (string handle in new[] { "1EF", "1FF" })
        {
            var before = PolyfaceRecordParent(input, handle).Tags; var after = PolyfaceRecordParent(output, handle).Tags;
            int b = before.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline")); int a = after.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline"));
            Check(before.Skip(b).Select(PolylineRecordTagKey).SequenceEqual(after.Skip(a).Select(PolylineRecordTagKey)), "native full subclass packet changed");
        }
        Equal(0, StoredDimAssocLoad(output).Objects.Validate().Count, "native output graph");
    }
    private static void Legacy2DProducer(DxfVersion version, bool inputBinary, bool binary)
    {
        var input = Legacy2DInput(version, inputBinary); var doc = StoredDimAssocLoad(input); var line = Legacy2DPolyline(doc, version, inputBinary);
        var handles = Legacy2DFixture(version, inputBinary).GetProperty("handles");
        Check(line.VertexRecords.Select(r => r.Handle).SequenceEqual(handles.GetProperty("vertices").EnumerateArray().Select(v => v.GetString())), "producer child identities");
        Check(line.VertexRecords.Select(r => r.Vertex).SequenceEqual(line.Vertexes), "retained record/model alignment");
        Check(line.VertexRecords.All(r => r.UsesBlockRecordOwner && ReferenceEquals(r.StoredOwner, doc.GetObjectByHandle(handles.GetProperty("block_record").GetString()!))), "actual containing block owners");
        Check(ReferenceEquals(line.EndSequenceRecord.StoredOwner, line) && ReferenceEquals(line.VertexRecords[2].ExtensionDictionary.Owner, line.VertexRecords[2]), "actual sequence and extension ownership");
        Check(line.VertexRecords[0].PersistentReactors.Contains(line.VertexRecords[1]) && line.VertexRecords[1].PersistentReactors.Contains(line.VertexRecords[0]), "reactor cycle");
        Equal(0.75, line.GetEffectiveStartWidth(0), "producer omitted zero start inherits default"); Equal(1.25, line.GetEffectiveEndWidth(1), "omitted end inherits default");
        Equal(0.75, line.GetEffectiveStartWidth(2), "omitted start inherits default"); Equal(1.25, line.GetEffectiveEndWidth(2), "producer omitted zero end inherits default");
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-producer-{version}-{inputBinary}-{binary}.dxf");
        foreach (var pair in PolylineRecordPackets(input)) PolylineRecordPacketEqual(pair.Value, PolylineRecordPackets(output)[pair.Key]);
        var loaded = StoredDimAssocLoad(output); Equal(0, loaded.Objects.Validate().Count, "producer output graph");
        Check(((Polyline2D)loaded.GetObjectByHandle(line.Handle)).VertexRecords.Count == 4, "legacy output changed representation");
    }
    private static void Legacy2DClone(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var source = Legacy2DPolyline(doc, version, binary, true);
        var clone = (Polyline2D)source.Clone(); Check(clone.VertexRecords.All(r => r.Handle == null) && clone.EndSequenceRecord.Handle == null, "clone borrowed source handles");
        Check(clone.VertexRecords.Select(r => r.Vertex).SequenceEqual(clone.Vertexes) && !ReferenceEquals(clone.Vertexes[0], source.Vertexes[0]), "cloned model identities");
        var target = new DxfDocument(version); target.Entities.Add(clone);
        Check(clone.VertexRecords.All(r => ReferenceEquals(target.GetObjectByHandle(r.Handle), r)), "cloned child registration");
        var bytes = StoredDimAssocSave(target, !binary, $"legacy2d-records-clone-{version}-{binary}.dxf"); var copy = (Polyline2D)StoredDimAssocLoad(bytes).GetObjectByHandle(clone.Handle);
        Equal(2.0, copy.GetEffectiveStartWidth(0), "cloned inherited width");
        string[] handles = source.VertexRecords.Select(r => r.Handle).ToArray(); var end = source.EndSequenceRecord;
        Check(doc.Entities.Remove(source), "clean source detach"); Check(source.VertexRecords.All(r => doc.GetObjectByHandle(r.Handle) == null && ReferenceEquals(r.Owner, source)), "detached child state");
        doc.Entities.Add(source); Check(handles.SequenceEqual(source.VertexRecords.Select(r => r.Handle)) && ReferenceEquals(end, source.EndSequenceRecord), "re-adoption changed children");
        var rich = Legacy2DPolyline(doc, version, binary); Throws<NotSupportedException>(() => rich.Clone()); Check(!doc.Entities.Remove(rich), "owned record removal accepted");
    }
    private static void Legacy2DReverse(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var line = Legacy2DPolyline(doc, version, binary);
        var vertices = line.Vertexes.ToArray(); var records = line.VertexRecords.ToArray(); var end = line.EndSequenceRecord;
        var starts = Enumerable.Range(0, 4).Select(line.GetEffectiveStartWidth).ToArray(); var finishes = Enumerable.Range(0, 4).Select(line.GetEffectiveEndWidth).ToArray(); var bulges = vertices.Select(v => v.Bulge).ToArray();
        line.Reverse(); Check(line.VertexRecords.SequenceEqual(records.Reverse()) && line.Vertexes.SequenceEqual(vertices.Reverse()), "Reverse lost point record identities");
        Equal((double?)1.25, line.LegacyDefaultStartWidth, "Reverse default start"); Equal((double?)0.75, line.LegacyDefaultEndWidth, "Reverse default end");
        for (int i = 0; i < 4; i++) { int original = (6 - i) % 4; Equal(finishes[original], line.GetEffectiveStartWidth(i), "reversed segment start"); Equal(starts[original], line.GetEffectiveEndWidth(i), "reversed segment end"); Equal(-bulges[original], line.Vertexes[i].Bulge, "reversed outgoing bulge"); }
        Check(ReferenceEquals(end, line.EndSequenceRecord), "Reverse changed SEQEND");
        var bytes = StoredDimAssocSave(doc, binary, $"legacy2d-records-reversed-{version}-{binary}.dxf"); var loaded = (Polyline2D)StoredDimAssocLoad(bytes).GetObjectByHandle(line.Handle);
        Check(loaded.VertexRecords.Select(r => r.Handle).SequenceEqual(records.Reverse().Select(r => r.Handle)), "wire order did not follow reverse");
        line.Reverse(); Check(line.Vertexes.SequenceEqual(vertices) && line.VertexRecords.SequenceEqual(records), "double Reverse identities");
        for (int i = 0; i < 4; i++) { Equal(starts[i], line.GetEffectiveStartWidth(i), "double reverse start"); Equal(bulges[i], line.Vertexes[i].Bulge, "double reverse bulge"); }
    }
    private static void Legacy2DTransform(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var line = Legacy2DPolyline(doc, version, binary); var records = line.VertexRecords.ToArray();
        var before = line.Vertexes.Select(v => v.Position).ToArray(); var starts = Enumerable.Range(0, 4).Select(line.GetEffectiveStartWidth).ToArray();
        line.TransformBy(Matrix3.Scale(2), new Vector3(5, 7, 11));
        for (int i = 0; i < 4; i++) { Equal(before[i] * 2 + new Vector2(5, 7), line.Vertexes[i].Position, "transformed point"); Equal(starts[i] * 2, line.GetEffectiveStartWidth(i), "scaled inherited or explicit width"); }
        Equal(17.0, line.Elevation, "transformed plane elevation"); Equal(1.0, line.Thickness, "transformed thickness"); Check(records.SequenceEqual(line.VertexRecords), "transform replaced identities");
        var bytes = StoredDimAssocSave(doc, binary, $"legacy2d-records-transformed-{version}-{binary}.dxf"); var copy = (Polyline2D)StoredDimAssocLoad(bytes).GetObjectByHandle(line.Handle); Equal((double?)1.5, copy.LegacyDefaultStartWidth, "transformed wire defaults");
        var snapshot = line.Vertexes.Select(v => v.Position).ToArray(); Throws<NotSupportedException>(() => line.TransformBy(Matrix3.Scale(2, 3, 1), Vector3.Zero));
        Check(snapshot.SequenceEqual(line.Vertexes.Select(v => v.Position)), "nonuniform refusal mutated points");
    }
    private static void Legacy2DEdits(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(version, binary)); var line = Legacy2DPolyline(doc, version, binary);
        line.Vertexes[0].StartWidthOverride = null; line.Vertexes[1].EndWidthOverride = 0; line.Vertexes[3].Bulge = 0.125;
        line.Vertexes[3].VertexIdentifier = 99; line.Vertexes[2].Position = new Vector2(81, 82); line.IsClosed = false; line.LinetypeGeneration = false;
        line.Elevation = 7; line.Thickness = 2; line.Normal = Vector3.UnitY; line.SmoothType = PolylineSmoothType.NoSmooth;
        var bytes = StoredDimAssocSave(doc, binary, $"legacy2d-records-edited-{version}-{binary}.dxf"); var copy = (Polyline2D)StoredDimAssocLoad(bytes).GetObjectByHandle(line.Handle);
        Equal(0.75, copy.GetEffectiveStartWidth(0), "cleared override inheritance"); Equal((double?)0, copy.Vertexes[1].EndWidthOverride, "appended explicit zero"); Equal(0.125, copy.Vertexes[3].Bulge, "appended bulge"); Equal((int?)99, copy.Vertexes[3].VertexIdentifier, "identifier edit"); Equal(new Vector2(81, 82), copy.Vertexes[2].Position, "point edit");
        Check(!copy.IsClosed && !copy.LinetypeGeneration && copy.Normal == Vector3.UnitY && copy.Elevation == 7 && copy.Thickness == 2, "qualified header edits");
        Throws<NotSupportedException>(() => line.ConstantWidth = 1); Throws<NotSupportedException>(() => line.SmoothType = PolylineSmoothType.Cubic);
    }
    private static void Legacy2DExplicitZero(bool binary)
    {
        var original = Legacy2DInput(DxfVersion.AutoCad2018, binary); var handles = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        var raw = ObjectStoreReplaceRecord(PolylineRecordRaw(original), handles.GetProperty("vertices")[0].GetString()!, tags => { tags.Insert(tags.FindIndex(t => t.Code == 41), new(40, 0.0)); return tags; });
        raw = ObjectStoreReplaceRecord(raw, handles.GetProperty("vertices")[2].GetString()!, tags => { tags.Add(new(41, 0.0)); return tags; });
        var input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary);
        Equal((double?)0, line.Vertexes[0].StartWidthOverride, "raw explicit zero start presence"); Equal(0.0, line.GetEffectiveStartWidth(0), "raw zero overrides header start");
        Equal((double?)0, line.Vertexes[2].EndWidthOverride, "raw explicit zero end presence"); Equal(0.0, line.GetEffectiveEndWidth(2), "raw zero overrides header end");
        var unchanged = StoredDimAssocSave(doc, binary, $"legacy2d-records-explicit-zero-{binary}.dxf");
        foreach (var pair in PolylineRecordPackets(input)) PolylineRecordPacketEqual(pair.Value, PolylineRecordPackets(unchanged)[pair.Key]);
        line.Reverse(); Equal(0.0, line.GetEffectiveStartWidth(0), "reverse moved zero end to start"); Equal(0.0, line.GetEffectiveEndWidth(2), "reverse moved zero start to end");
        line.TransformBy(Matrix3.Scale(3), Vector3.Zero); Equal(0.0, line.GetEffectiveStartWidth(0), "scaling preserved explicit zero"); Equal(3.75, line.GetEffectiveStartWidth(1), "scaling inherited swapped default");
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-zero-reversed-scaled-{binary}.dxf"); var copy = (Polyline2D)StoredDimAssocLoad(output).GetObjectByHandle(line.Handle);
        Equal((double?)0, copy.Vertexes[0].StartWidthOverride, "reversed/scaled zero presence on wire");
    }
    private static void Legacy2DMalformed(bool binary, int fault)
    {
        var handles = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles"); string handle = handles.GetProperty(fault >= 12 ? "polyline" : fault == 11 ? "seqend" : "vertices").ValueKind == JsonValueKind.Array ? handles.GetProperty("vertices")[0].GetString()! : handles.GetProperty(fault >= 12 ? "polyline" : "seqend").GetString()!;
        var raw = ObjectStoreReplaceRecord(PolylineRecordRaw(Legacy2DInput(DxfVersion.AutoCad2018, binary)), handle, tags =>
        {
            int at(short code) => tags.FindIndex(t => t.Code == code);
            int owner = -1, depth = 0;
            for (int i = 0; i < tags.Count && tags[i].Code != 100; i++)
            {
                if (tags[i].Code == 102) depth += ((string)tags[i].Value).StartsWith('{') ? 1 : -1;
                else if (depth == 0 && tags[i].Code == 330) { owner = i; break; }
            }
            if (fault == 0) tags[at(30)] = new(30, 1.0);
            else if (fault == 1) tags.RemoveAt(at(30));
            else if (fault == 2) tags.Insert(at(10), tags[at(10)]);
            else if (fault == 3) tags[at(70)] = new(70, (short)32);
            else if (fault == 4) tags.Insert(at(41), new(40, -1.0));
            else if (fault == 5) tags.Insert(at(42), tags[at(42)]);
            else if (fault == 6) tags.RemoveAt(at(5));
            else if (fault == 7) tags[at(5)] = new(5, "0");
            else if (fault == 8) tags[owner] = new(330, handles.GetProperty("plain_polyline").GetString()!);
            else if (fault == 9) tags.RemoveAt(tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dVertex")));
            else if (fault == 10) tags.Insert(tags.FindIndex(t => t.Code == 1001), new(50, 1.0));
            else if (fault == 11) tags[owner] = new(330, handles.GetProperty("block_record").GetString()!);
            else if (fault == 12) tags[at(10)] = new(10, 1.0);
            else if (fault == 13) tags.Insert(at(40), tags[at(40)]);
            else if (fault == 14) tags[at(40)] = new(40, -1.0);
            else tags[at(70)] = new(70, (short)16);
            return tags;
        });
        byte[] bytes = StoredDimAssocRawBytes(raw, binary); bool rejected = false;
        try { rejected = DxfDocument.Load(new MemoryStream(bytes)) == null; } catch (Exception) { rejected = true; }
        Check(rejected, "malformed legacy record admitted " + fault);
    }
    private static void Legacy2DInvalid(bool binary, int fault)
    {
        var doc = StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018, binary)); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary, true);
        if (fault == 0) line.Vertexes[0].Position = new Vector2(double.NaN, 0);
        else if (fault == 1) line.Vertexes[0].Bulge = double.PositiveInfinity;
        else if (fault == 2) line.Elevation = double.NaN;
        else if (fault == 3) line.Normal = new Vector3(double.NaN, 0, 0);
        else if (fault == 4) line.Thickness = double.PositiveInfinity;
        else if (fault == 5) line.Vertexes.Add(new Polyline2DVertex(1, 1));
        else if (fault == 6) line.Vertexes[0] = (Polyline2DVertex)line.Vertexes[0].Clone();
        else if (fault == 7) line.Vertexes.Reverse();
        else doc.DrawingVariables.AcadVer = DxfVersion.AutoCad2000;
        long seed = OwnershipSeed(doc); using var output = new MemoryStream(); output.WriteByte(71); bool rejected = false;
        try { rejected = !doc.Save(output, binary); } catch (Exception) { rejected = true; }
        Check(rejected && output.Length == 1 && output.ToArray()[0] == 71 && seed == OwnershipSeed(doc), "invalid save was not atomic");
        if (fault < 8) { bool cloneRejected = false; try { line.Clone(); } catch (Exception) { cloneRejected = true; } Check(cloneRejected, "invalid retained clone admitted"); }
    }
    private static void Legacy2DPrivate(bool binary, int variant)
    {
        var input = Legacy2DInput(DxfVersion.AutoCad2018, binary); var handles = Legacy2DFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles"); string handle = handles.GetProperty(variant < 2 ? "plain_vertices" : "plain_polyline").ValueKind == JsonValueKind.Array ? handles.GetProperty("plain_vertices")[0].GetString()! : handles.GetProperty("plain_polyline").GetString()!;
        var raw = ObjectStoreReplaceRecord(PolylineRecordRaw(input), handle, tags =>
        {
            int at = tags.FindIndex(t => t.Code == 1001); if (at < 0) at = tags.Count;
            var extra = new List<DxfTag> { new(70, (short)127), new(10, 99.0), new(20, 88.0), new(30, 77.0), new(40, -7.0), new(41, -8.0), new(210, 0.0), new(220, 0.0), new(230, 1.0), new(5, "FFFFFF") };
            if (variant % 2 == 0) { extra.Insert(0, new(102, "{PRIVATE")); extra.Add(new(102, "}")); } else extra.Insert(0, new(100, "PrivateLegacyClass"));
            tags.InsertRange(at, extra); return tags;
        });
        input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var line = Legacy2DPolyline(doc, DxfVersion.AutoCad2018, binary, true);
        Equal(new Vector2(101, 102), line.Vertexes[0].Position, "private geometry lookalikes"); Equal((double?)2, line.LegacyDefaultStartWidth, "private default width lookalike");
        Throws<NotSupportedException>(() => line.Clone()); Check(!doc.Entities.Remove(line), "private retained erasure accepted");
        var output = StoredDimAssocSave(doc, binary, $"legacy2d-records-private-{binary}-{variant}.dxf");
        if (variant < 2) PolylineRecordPacketEqual(PolylineRecordPackets(input)[handle], PolylineRecordPackets(output)[handle]);
        else { var a = PolyfaceRecordParent(input, handle).Tags; var b = PolyfaceRecordParent(output, handle).Tags; int i = a.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline")); int j = b.ToList().FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDb2dPolyline")); Check(a.Skip(i).Select(PolylineRecordTagKey).SequenceEqual(b.Skip(j).Select(PolylineRecordTagKey)), "private header packet changed"); }
    }
}
