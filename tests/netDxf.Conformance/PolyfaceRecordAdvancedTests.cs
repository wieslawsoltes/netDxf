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
    private static void RegisterPolyfaceRecordAdvancedTests()
    {
        foreach (bool binary in new[] { false, true })
        {
            foreach (int variant in Enumerable.Range(0, 3)) foreach (bool authored in new[] { false, true }) Run($"polyface-records/invalid-normal/{binary}/{variant}/{authored}", () => PolyfaceRecordNormalInvalid(binary, variant, authored));
            foreach (int variant in Enumerable.Range(0, 8)) Run($"polyface-records/slot-edits/{binary}/{variant}", () => PolyfaceRecordSlotEdit(binary, variant));
            Run($"polyface-records/missing-seqend/{binary}", () => PolyfaceRecordMissingEnd(binary));
            Run($"polyface-records/pinned-negative/{binary}", () =>
            {
                var bytes = File.ReadAllBytes($"tests/fixtures/polyface-records/negative-polyface-invalid-xdata-AutoCad2018-{binary}.dxf");
                bool rejected = false; try { using var input = new MemoryStream(bytes); rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
                Check(rejected, "pinned malformed XData tail became an admitted graph");
            });
        }
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"polyface-records/move/{version}/{binary}", () => PolyfaceRecordMove(version, binary));
            foreach (int fault in Enumerable.Range(0, 29)) Run($"polyface-records/malformed/{version}/{binary}/{fault}", () => PolyfaceRecordMalformed(version, binary, fault));
        }
        foreach (bool binary in new[] { false, true })
        {
            Run($"polyface-records/profile-rejection/{binary}", () => PolyfaceRecordProfileRejection(binary));
            foreach (short code in new short[] { 5, 330, 340, 320 }) Run($"polyface-records/header-removal/{binary}/{code}", () => PolyfaceRecordHeaderRemoval(binary, code));
            foreach (bool empty in new[] { false, true }) Run($"polyface-records/parent-xdata-clone/{binary}/{empty}", () => PolyfaceRecordParentXDataClone(binary, empty));
            foreach (bool xdata in new[] { false, true }) Run($"polyface-records/parent-reference-move/{binary}/{xdata}", () => PolyfaceRecordParentReferenceMove(binary, xdata));
            foreach (int scenario in Enumerable.Range(0, 14)) Run($"polyface-records/lifecycle/{binary}/{scenario}", () => PolyfaceRecordLifecycle(binary, scenario));
            foreach (int variant in Enumerable.Range(0, 6)) Run($"polyface-records/private/{binary}/{variant}", () => PolyfaceRecordPrivate(binary, variant));
            foreach (int scenario in Enumerable.Range(0, 8)) Run($"polyface-records/tag-budgets/{binary}/{scenario}", () => PolyfaceRecordTagBudget(binary, scenario));
            foreach (int variant in Enumerable.Range(0, 6)) Run($"polyface-records/private-header/{binary}/{variant}", () => PolyfaceRecordPrivateHeader(binary, variant));
            foreach (short code in new short[] { 330, 340, 320 }) foreach (int role in Enumerable.Range(0, 3))
                Run($"polyface-records/incoming-xrecord/{binary}/{code}/{role}", () => PolyfaceRecordIncoming(binary, code, role));
        }
    }
    private static void PolyfaceRecordMove(DxfVersion version, bool binary)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(version, binary)); var handles = PolyfaceRecordFixture(version, binary).GetProperty("handles");
        var polyline = (PolyfaceMesh)doc.GetObjectByHandle(handles.GetProperty("plain_mesh").GetString()!); var records = polyline.VertexRecords.ToArray();
        Check(doc.Entities.Remove(polyline), "unreferenced source polyline removal rejected");
        Check(records.All(r => doc.GetObjectByHandle(r.Handle) == null), "detached child identities remain registered");
        var destination = new Block("MOVED_VERTEX_PARENT"); destination.Entities.Add(polyline); doc.Blocks.Add(destination);
        Check(records.All(r => ReferenceEquals(doc.GetObjectByHandle(r.Handle), r) && ReferenceEquals(r.StoredOwner, destination.Record)), "moved block-record owner is stale");
        byte[] output = StoredDimAssocSave(doc, binary, $"polyface-records-move-{version}-{binary}.dxf");
        var loaded = StoredDimAssocLoad(output); var copy = loaded.Blocks[destination.Name].Entities.OfType<PolyfaceMesh>().Single();
        Check(copy.VertexRecords.All(r => ReferenceEquals(r.StoredOwner, loaded.Blocks[destination.Name].Record)), "move owner not retained across reload");
        Equal(0, loaded.Objects.Validate().Count, "move graph validation");
    }
    private static void PolyfaceRecordProfileRejection(bool binary)
    {
        var source = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var original = source.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var clone = (PolyfaceMesh)original.Clone(); var destination = new DxfDocument(DxfVersion.AutoCad2000);
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
    private static void PolyfaceRecordHeaderRemoval(bool binary, short code)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0); var child = plain.VertexRecords[0];
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
    private static void PolyfaceRecordParentXDataClone(bool binary, bool empty)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var data = new XData(new ApplicationRegistry("PARENT_VERTEX_REF"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, empty ? "0000" : plain.VertexRecords[0].Handle));
        plain.XData.Add(data); long seed = OwnershipSeed(doc); var handles = plain.VertexRecords.Select(r => r.Handle).ToArray();
        if (empty)
        {
            var clone = (PolyfaceMesh)plain.Clone(); var destination = new DxfDocument(DxfVersion.AutoCad2018); destination.Entities.Add(clone);
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
    private static void PolyfaceRecordParentReferenceMove(bool binary, bool xdata)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var plain = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0); string handle = plain.Handle;
        if (xdata)
        {
            var data = new XData(new ApplicationRegistry("CHILD_PARENT_REF"));
            data.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, handle)); plain.VertexRecords[0].XData.Add(data);
        }
        else plain.VertexRecords[0].PersistentReactors.Add(plain);
        doc = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); plain = (PolyfaceMesh)doc.GetObjectByHandle(handle); var first = plain.VertexRecords[0];
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
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"polyface-records-parent-reactor-move-{binary}.dxf"));
            var child = (PolyfaceMeshRecord)loaded.GetObjectByHandle(first.Handle);
            Check(ReferenceEquals(child.PersistentReactors.Single(), child.Owner), "move replayed stale raw parent reactor handle");
        }
    }
    private static void PolyfaceRecordLifecycle(bool binary, int scenario)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var handles = PolyfaceRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        var polyline = (PolyfaceMesh)doc.GetObjectByHandle(handles.GetProperty("mesh").GetString()!); var first = polyline.VertexRecords[0];
        if (scenario <= 3)
        {
            byte[] before = StoredDimAssocSave(doc, binary); var records = polyline.VertexRecords.Select(r => r.Handle).ToArray();
            if (scenario == 0) ((PolyfaceMeshFace[])polyline.Faces)[0] = new PolyfaceMeshFace(new short[] { 1, 2, 3 });
            if (scenario == 1) polyline.Vertexes[polyline.Vertexes.Length - 1] = new Vector3(0, double.PositiveInfinity, 0);
            if (scenario == 2) polyline.Faces[0].VertexIndexes[0] = 0;
            if (scenario == 3) polyline.Vertexes[0] = new Vector3(double.NaN, 0, 0);
            using var output = new MemoryStream(); output.WriteByte(23); bool rejected = false;
            try { rejected = !doc.Save(output, binary); } catch (Exception error) when (error is NotSupportedException or InvalidOperationException or ArgumentException) { rejected = true; }
            Check(rejected && output.Length == 1 && output.ToArray()[0] == 23 && records.SequenceEqual(polyline.VertexRecords.Select(r => r.Handle)), "unsupported geometry edit was not rejected before output/identity mutation");
        }
        else if (scenario == 4)
        {
            var original = polyline.VertexRecords.ToArray(); var points = polyline.Vertexes.ToArray();
            polyline.Normal = Vector3.UnitY;
            Check(polyline.VertexRecords.SequenceEqual(original) && polyline.Vertexes.SequenceEqual(points), "closure changed slot identities");
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); var copy = (PolyfaceMesh)loaded.GetObjectByHandle(polyline.Handle);
            Check(copy.Normal == polyline.Normal && copy.VertexRecords.Select(r => r.Handle).SequenceEqual(original.Select(r => r.Handle)), "closure roundtrip changed slot identities");
        }
        else if (scenario == 5)
        {
            polyline.Vertexes[0] = new Vector3(91, 92, 93); Check(ReferenceEquals(first, polyline.VertexRecords[0]), "coordinate replacement changed slot identity");
            byte[] output = StoredDimAssocSave(doc, binary, $"polyface-records-edited-{binary}.dxf"); var loaded = StoredDimAssocLoad(output);
            Equal(new Vector3(91, 92, 93), ((PolyfaceMesh)loaded.GetObjectByHandle(polyline.Handle)).Vertexes[0], "coordinate edit output");
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
            Check(!doc.Layers.Remove("COORDINATE_ONLY") && !doc.Linetypes.Remove("FACE_DASH"), "child named dependency removal allowed");
            doc.Layers["COORDINATE_ONLY"].Name = "RENAMED_VERTEX_LAYER"; doc.Linetypes["FACE_DASH"].Name = "RENAMED_VERTEX_LINE";
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); var record = (PolyfaceMeshRecord)loaded.GetObjectByHandle(first.Handle);
            Equal("RENAMED_VERTEX_LAYER", record.Layer.Name, "layer rename"); Equal("RENAMED_VERTEX_LINE", ((PolyfaceMeshRecord)loaded.GetObjectByHandle(polyline.FaceRecords[0].Handle)).Linetype.Name, "linetype rename");
        }
        else if (scenario == 9)
        {
            var registry = doc.ApplicationRegistries["POLYFACE_META"]; Check(!doc.ApplicationRegistries.Remove(registry), "child APPID removal allowed"); registry.Name = "RENAMED_VERTEX_META";
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); Check(((PolyfaceMeshRecord)loaded.GetObjectByHandle(first.Handle)).XData.ContainsAppId("RENAMED_VERTEX_META"), "APPID rename not written on child");
        }
        else if (scenario == 10)
        {
            var extension = polyline.FaceRecords[1].ExtensionDictionary; doc.Objects.EraseOwnedTree(extension);
            Check(polyline.FaceRecords[1].ExtensionDictionary == null, "extension erasure did not clear child slot");
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary)); Check(((PolyfaceMeshRecord)loaded.GetObjectByHandle(polyline.FaceRecords[1].Handle)).ExtensionDictionary == null, "erased extension wire group survived");
        }
        else if (scenario == 11)
        {
            var destination = new DxfDictionary(); doc.NamedObjects.Add("CLONE_DESTINATION", destination); int count = doc.Objects.Items.Count();
            Throws<NotSupportedException>(() => doc.Objects.Clone(polyline.FaceRecords[1].ExtensionDictionary, destination, "PARTIAL"));
            Check(!destination.Contains("PARTIAL") && doc.Objects.Items.Count() == count, "partial owned clone mutated destination");
        }
        else if (scenario == 12)
        {
            first.PersistentReactors.Clear(); first.PersistentReactors.Add(polyline.VertexRecords[2]); first.PersistentReactors.Add(polyline.VertexRecords[2]);
            var extension = new DxfDictionary(); extension.Add("NEW", new DxfDictionaryVariable { Value = "new child attachment" }); doc.Objects.SetExtensionDictionary(first, extension);
            var loaded = StoredDimAssocLoad(StoredDimAssocSave(doc, binary, $"polyface-records-metadata-edited-{binary}.dxf")); var copy = (PolyfaceMeshRecord)loaded.GetObjectByHandle(first.Handle);
            Equal(2, copy.PersistentReactors.Count, "duplicate explicit reactor order lost"); Check(ReferenceEquals(copy.ExtensionDictionary.Owner, copy), "new child extension not reciprocal");
        }
        else
        {
            var plain = (PolyfaceMesh)doc.GetObjectByHandle(handles.GetProperty("plain_mesh").GetString()!); Check(doc.Entities.Remove(plain), "foreign adoption source detach");
            var target = new DxfDocument(); var objects = target.Objects.Items.ToArray(); long seed = OwnershipSeed(target);
            var resources = (target.Layers.Count, target.Linetypes.Count, target.ApplicationRegistries.Count, target.Blocks.Count);
            Throws<NotSupportedException>(() => target.Entities.Add(plain)); Check(plain.Owner == null, "failed foreign adoption attached source");
            Check(target.Objects.Items.SequenceEqual(objects) && OwnershipSeed(target) == seed && !target.Entities.All.Any()
                && resources == (target.Layers.Count, target.Linetypes.Count, target.ApplicationRegistries.Count, target.Blocks.Count), "failed foreign adoption changed destination");
        }
    }
    private static void PolyfaceRecordPrivate(bool binary, int variant)
    {
        var source = PolyfaceRecordInput(DxfVersion.AutoCad2018, binary); var handles = PolyfaceRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        string handle = handles.GetProperty("plain_coordinates")[0].GetString()!; var raw = PolylineRecordRaw(source);
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
        byte[] input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var record = (PolyfaceMeshRecord)doc.GetObjectByHandle(handle);
        Check(doc.GetObjectByHandle("FFFFFF") == null && doc.GetObjectByHandle("EEEEEE") == null, "private handle became a physical source identity");
        var owner = (PolyfaceMesh)record.Owner; Equal(new Vector3(101, 102, 103), owner.Vertexes[0], "private coordinate or flags changed public geometry");
        if (variant < 3) Throws<NotSupportedException>(() => owner.Clone());
        byte[] output = StoredDimAssocSave(doc, binary, $"polyface-records-private-{binary}-{variant}.dxf");
        PolylineRecordPacketEqual(PolylineRecordPackets(input)[handle], PolylineRecordPackets(output)[handle]);
    }    private static void PolyfaceRecordMalformed(DxfVersion version, bool binary, int fault)
    {
        var raw = PolylineRecordRaw(PolyfaceRecordInput(version, binary));
        var handles = PolyfaceRecordFixture(version, binary).GetProperty("handles"); string handle = handles.GetProperty("coordinates")[0].GetString()!;
        if (fault >= 19 && fault <= 25) handle = handles.GetProperty("faces")[0].GetString()!;
        if (fault >= 26) handle = handles.GetProperty("mesh").GetString()!;
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
            else if (fault == 5) tags[publicOwner] = new(330, handles.GetProperty("plain_mesh").GetString()!);
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
            else if (fault == 16) tags[at(5)] = new(5, handles.GetProperty("coordinates")[1].GetString()!);
            else if (fault == 18) tags[at(10)] = new(10, 1.0000000000000002);
            else if (fault == 19) tags.RemoveAt(tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbFaceRecord")));
            else if (fault == 20) tags[publicOwner] = new(330, handles.GetProperty("coordinates")[0].GetString()!);
            else if (fault == 21) tags.Insert(at(71), tags[at(71)]);
            else if (fault == 22) tags[at(71)] = new(71, (short)7);
            else if (fault == 23) tags[at(71)] = new(71, short.MinValue);
            else if (fault == 24) tags[at(70)] = new(70, (short)192);
            else if (fault == 25) tags.RemoveAt(at(10));
            else if (fault == 26) tags.Insert(at(70), tags[at(70)]);
            else if (fault == 27) tags.Insert(at(71), tags[at(71)]);
            else if (fault == 28) tags.Add(new(75, (short)6));
            else { tags.RemoveAt(at(5)); tags.InsertRange(0, new DxfTag[] { new(102, "{PRIVATE"), new(5, handle), new(102, "}") }); }
            return tags;
        });
        byte[] wire = StoredDimAssocRawBytes(raw, binary);
        if (fault == 18)
        {
            if (binary)
            {
                int coordinate = wire.AsSpan().IndexOf(BitConverter.GetBytes(1.0000000000000002));
                Check(coordinate >= 0, "nonfinite wire injection coordinate");
                Buffer.BlockCopy(BitConverter.GetBytes(double.NaN), 0, wire, coordinate, 8);
            }
            else
            {
                string[] lines = System.Text.Encoding.UTF8.GetString(wire).Replace("\r\n", "\n").Split('\n');
                bool target = false, changed = false;
                for (int i = 0; i + 1 < lines.Length; i += 2)
                {
                    short code = short.Parse(lines[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
                    if (code == 0) target = false;
                    else if (code == 5 && lines[i + 1] == handle) target = true;
                    else if (target && code == 10) { lines[i + 1] = "NaN"; changed = true; break; }
                }
                Check(changed, "nonfinite text injection coordinate");
                wire = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
            }
        }
        bool rejected = false;
        try { using var stream = new MemoryStream(wire); rejected = DxfDocument.Load(stream) == null; }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidOperationException or InvalidDataException) { rejected = true; }
        Check(rejected, "malformed child record accepted");
    }
    private static void PolyfaceRecordIncoming(bool binary, short code, int role)
    {
        var doc = StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var mesh = doc.Entities.PolyfaceMeshes.Single(p => p.VertexRecords[0].PersistentReactors.Count == 0);
        var child = role == 0 ? mesh.VertexRecords[0] : role == 1 ? mesh.FaceRecords[0] : mesh.EndSequenceRecord;
        var reference = new DxfXRecord(); reference.Data.Add(new DxfTag(code, "000" + child.Handle.ToLowerInvariant())); doc.NamedObjects.Add("POLYFACE_REF", reference);
        long seed = OwnershipSeed(doc);
        if (code == 320) Check(doc.Entities.Remove(mesh), "arbitrary handle blocked mesh removal");
        else
        {
            Check(!doc.Entities.Remove(mesh) && seed == OwnershipSeed(doc) && ReferenceEquals(doc.GetObjectByHandle(child.Handle), child), "incoming role reference removal was not atomic");
            reference.Data.Clear(); Check(doc.Entities.Remove(mesh) && doc.GetObjectByHandle(child.Handle) == null, "released role reference still blocks removal");
        }
    }
    private static void PolyfaceRecordTagBudget(bool binary, int scenario)
    {
        var initial = new DxfDocument(DxfVersion.AutoCad2018);
        initial.Entities.Add(new PolyfaceMesh(Enumerable.Range(0, scenario == 4 ? 256 : 4).Select(i => new Vector3(i, i % 3, 1)), new[] { new short[] { 1, 2, 3 } }));
        initial.Entities.Add(new PolygonMesh(2, 2, new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }));
        initial.Entities.Add(new Polyline3D(new[] { Vector3.Zero, Vector3.UnitX }));
        var doc = StoredDimAssocLoad(StoredDimAssocSave(initial, binary)); var mesh = doc.Entities.PolyfaceMeshes.Single();
        DxfObject target = scenario == 1 ? mesh.FaceRecords[0] : scenario == 2 ? mesh.EndSequenceRecord : scenario == 6 ? doc.Entities.PolygonMeshes.Single().VertexRecords[0] : scenario == 7 ? doc.Entities.Polylines3D.Single().VertexRecords[0] : mesh.VertexRecords[0];
        var packets = PolylineRecordPackets(StoredDimAssocSave(doc, binary));
        if (scenario == 4)
        {
            foreach (var record in mesh.VertexRecords)
            {
                var data = new XData(new ApplicationRegistry("POLYFACE_BUDGET"));
                for (int i = packets[record.Handle].Tags.Count; i < 4096; i++) data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)1));
                record.XData.Add(data);
            }
        }
        else if (scenario == 3)
            for (int i = 0; i < 4096; i++) target.PersistentReactors.Add(mesh.FaceRecords[0]);
        else
        {
            var data = new XData(new ApplicationRegistry("POLYFACE_BUDGET"));
            for (int i = packets[target.Handle].Tags.Count; i < 4096; i++)
                data.XDataRecord.Add(scenario == 5 ? new XDataRecord(new[] { XDataCode.WorldSpacePositionX, XDataCode.WorldSpacePositionY, XDataCode.WorldSpacePositionZ }[(i - packets[target.Handle].Tags.Count) % 3], 1.0) : new XDataRecord(XDataCode.Int16, (short)1));
            target.XData.Add(data); var exact = StoredDimAssocSave(doc, binary);
            Equal(4097, PolylineRecordPackets(exact)[target.Handle].Tags.Count, "physical boundary plus opening marker");
            Check(StoredDimAssocLoad(exact) != null, "exact physical boundary rejected on reload");
            data.XDataRecord.Add(new XDataRecord(XDataCode.Int16, (short)2));
        }
        long seed = OwnershipSeed(doc); var ids = mesh.RecordSequence.Select(r => r.Handle).ToArray();
        using var output = new MemoryStream(); output.WriteByte(73); bool rejected = false;
        try { rejected = !doc.Save(output, binary); } catch (NotSupportedException) { rejected = true; }
        Check(rejected && output.Length == 1 && output.ToArray()[0] == 73 && seed == OwnershipSeed(doc) && ids.SequenceEqual(mesh.RecordSequence.Select(r => r.Handle)), "shared tag budget mutated output or identities");
        if (scenario == 4) Throws<NotSupportedException>(() => doc.Entities.Polylines3D.Single().InsertVertex(1, Vector3.UnitY));
    }
    private static void PolyfaceRecordPrivateHeader(bool binary, int variant)
    {
        var raw = PolylineRecordRaw(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary)); var handles = PolyfaceRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles");
        string parent = handles.GetProperty("mesh").GetString()!, plain = handles.GetProperty("plain_mesh").GetString()!, face = handles.GetProperty("faces")[0].GetString()!;
        var fake = new DxfTag[] { new(70, (short)16), new(71, (short)-12), new(72, (short)0), new(210, 7.0), new(220, 8.0), new(230, 9.0) };
        if (variant == 3)
            raw = ObjectStoreReplaceRecord(raw, face, tags =>
            {
                int marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbFaceRecord"));
                tags.RemoveAll(t => t.Code is 8 or 62 or 420 or 430); marker = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbFaceRecord"));
                tags.InsertRange(marker, new DxfTag[] { new(100, "PrivateBeforeFace"), new(8, "PRIVATE_LAYER"), new(62, (short)5), new(71, (short)-6), new(70, (short)192) }); return tags;
            });
        else
            raw = ObjectStoreReplaceRecord(raw, parent, tags =>
            {
                if (variant == 2) tags.RemoveAll(t => t.Code is 71 or 72);
                if (variant == 1) { tags.Add(new(100, "PrivatePolyfaceHeader")); tags.AddRange(fake); }
                else
                {
                    tags.Add(new(102, "{PRIVATE_POLYFACE_HEADER")); tags.AddRange(fake);
                    if (variant >= 4) tags.Add(new DxfTag(variant == 4 ? (short)330 : (short)320, handles.GetProperty("plain_coordinates")[0].GetString()!));
                    tags.Add(new(102, "}"));
                }
                return tags;
            });
        var input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var mesh = (PolyfaceMesh)doc.GetObjectByHandle(parent);
        Equal(Vector3.UnitZ, mesh.Normal, "private normal replaced public normal");
        Equal(variant == 2 ? null : (short?)6, mesh.DeclaredVertexCount, "private advisory vertex count became public");
        Equal(variant == 2 ? null : (short?)2, mesh.DeclaredFaceCount, "private advisory face count became public");
        var unchanged = StoredDimAssocSave(doc, binary);
        Check(PolyfaceRecordParent(input, parent).Tags.SkipWhile(t => t.Code != 100 || !Equals(t.Value, "AcDbPolyFaceMesh")).Select(PolylineRecordTagKey)
            .SequenceEqual(PolyfaceRecordParent(unchanged, parent).Tags.SkipWhile(t => t.Code != 100 || !Equals(t.Value, "AcDbPolyFaceMesh")).Select(PolylineRecordTagKey)), "complete stored subclass header changed");
        Throws<NotSupportedException>(() => mesh.Clone()); Check(!doc.Entities.Remove(mesh), "private header or child allowed unqualified removal");
        if (variant == 3)
        {
            Check(mesh.Faces[0].Layer == null && mesh.Faces[0].Color == null && mesh.Faces[0].VertexIndexes[0] == -1, "private prelude replaced face properties");
            mesh.Faces[0].Layer = new Layer("PUBLIC_FACE"); mesh.Faces[0].Color = new AciColor(4); mesh.Faces[0].VertexIndexes[1] = -6;
        }
        else if (variant < 3) mesh.Normal = Vector3.UnitY;
        else Check(doc.Entities.Remove((PolyfaceMesh)doc.GetObjectByHandle(plain)) == (variant == 5), "private header semantic reference removal guard");
        var output = StoredDimAssocSave(doc, binary, $"polyface-records-private-header-{binary}-{variant}.dxf"); var copy = (PolyfaceMesh)StoredDimAssocLoad(output).GetObjectByHandle(parent);
        if (variant < 3) Equal(Vector3.UnitY, copy.Normal, "typed normal inserted outside the qualified header");
        if (variant == 3)
        {
            Equal("PUBLIC_FACE", copy.Faces[0].Layer.Name, "typed layer inserted into private face prelude"); Equal((short)4, copy.Faces[0].Color.Index, "typed color inserted into private prelude");
            Check(copy.Faces[0].VertexIndexes.SequenceEqual(new short[] { -1, -6, -3, 4 }), "private face slot was patched instead of qualified slot");
        }
    }

    private static void PolyfaceRecordSlotEdit(bool binary, int variant)
    {
        var raw = PolyfaceGrammarInput(DxfVersion.AutoCad2018, variant); var input = StoredDimAssocRawBytes(raw, binary); var doc = StoredDimAssocLoad(input); var mesh = doc.Entities.PolyfaceMeshes.Single();
        var identities = mesh.RecordSequence.ToArray(); var original = PolylineRecordPackets(input);
        mesh.Faces[0].VertexIndexes[0] *= -1; mesh.Vertexes[2] = new Vector3(131, 132, 133);
        var output = StoredDimAssocSave(doc, binary, $"polyface-records-slot-edited-{binary}-{variant}.dxf"); var actual = PolylineRecordPackets(output);
        Check(original.Keys.SequenceEqual(actual.Keys) && identities.SequenceEqual(mesh.RecordSequence), "slot edits changed mixed physical sequence or identities");
        foreach (var pair in original)
        {
            var expected = pair.Value.Tags.Select(t => pair.Key == mesh.FaceRecords[0].Handle && t.Code == 71 ? new DxfTag(71, mesh.Faces[0].VertexIndexes[0])
                : pair.Key == mesh.VertexRecords[2].Handle && t.Code is 10 or 20 or 30 ? new DxfTag(t.Code, t.Code == 10 ? 131.0 : t.Code == 20 ? 132.0 : 133.0) : t);
            Check(expected.Select(PolylineRecordTagKey).SequenceEqual(actual[pair.Key].Tags.Select(PolylineRecordTagKey)), "qualified slot/coordinate edit normalized unrelated inactive fields");
        }
        var copy = StoredDimAssocLoad(output).Entities.PolyfaceMeshes.Single(); Check(copy.Faces[0].VertexIndexes.SequenceEqual(mesh.Faces[0].VertexIndexes), "edited signed face reloaded differently");
        Equal((short?)-17, copy.DeclaredVertexCount, "edited advisory vertex count normalized"); Equal((short?)123, copy.DeclaredFaceCount, "edited advisory face count normalized");
    }
    private static void PolyfaceRecordMissingEnd(bool binary)
    {
        var raw = PolylineRecordRaw(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary)); string handle = PolyfaceRecordFixture(DxfVersion.AutoCad2018, binary).GetProperty("handles").GetProperty("seqend").GetString()!;
        var end = raw.Sections.Single(s => s.Name == "ENTITIES").Records.Single(r => r.Name == "SEQEND" && r.Tags.Any(t => t.Code == 5 && Equals(t.Value, handle)));
        var bytes = StoredDimAssocRawBytes(DxfRawDocument.Create(raw.Tags.Take(end.StartTagIndex).Concat(raw.Tags.Skip(end.EndTagIndex))), binary);
        bool rejected = false; try { using var input = new MemoryStream(bytes); rejected = DxfDocument.Load(input) == null; } catch (FormatException) { rejected = true; }
        Check(rejected, "missing SEQEND consumed following entity or admitted a partial chain");
    }

    private static void PolyfaceRecordNormalInvalid(bool binary, int variant, bool authored)
    {
        var doc = authored ? new DxfDocument(DxfVersion.AutoCad2018) : StoredDimAssocLoad(PolyfaceRecordInput(DxfVersion.AutoCad2018, binary));
        var mesh = authored ? new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { new short[] { 1, 2, 3 } }) : doc.Entities.PolyfaceMeshes.Single(m => m.VertexRecords[0].PersistentReactors.Count == 0);
        if (authored) doc.Entities.Add(mesh);
        var clone = (PolyfaceMesh)mesh.Clone(); Vector3 invalid = variant == 0 ? new Vector3(double.NaN, 0, 1) : variant == 1 ? new Vector3(0, double.PositiveInfinity, 1) : Vector3.Zero;
        foreach (var target in new[] { mesh, clone })
        {
            var before = DirectionBits(target.Normal);
            Throws<ArgumentException>(() => target.Normal = invalid);
            Check(before.SequenceEqual(DirectionBits(target.Normal)), "invalid normal assignment changed retained geometry");
            // The public setter is now atomic. Deliberately corrupt private state
            // to retain the existing independent adoption/clone/save guards.
            typeof(EntityObject).GetField("normal", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(target, invalid);
        }
        Throws<InvalidOperationException>(() => mesh.Clone());
        var destination = new DxfDocument(DxfVersion.AutoCad2018); var objects = destination.Objects.Items.ToArray(); long targetSeed = OwnershipSeed(destination);
        Throws<InvalidOperationException>(() => destination.Entities.Add(clone));
        Check(clone.Owner == null && clone.RecordSequence.All(r => r.Handle == null) && targetSeed == OwnershipSeed(destination) && objects.SequenceEqual(destination.Objects.Items), "invalid normal adoption changed destination before validation");
        long seed = OwnershipSeed(doc); using var output = new MemoryStream(); output.WriteByte(91); bool rejected = false;
        try { rejected = !doc.Save(output, binary); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && output.Length == 1 && output.ToArray()[0] == 91 && seed == OwnershipSeed(doc), "invalid normal output changed bytes or handles");
    }

}