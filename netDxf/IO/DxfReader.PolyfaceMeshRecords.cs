using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<PolyfaceMeshRecord> loadedPolyfaceMeshRecords = new List<PolyfaceMeshRecord>();

        private PolyfaceMesh ReadStoredPolyfaceMesh()
        {
            var header = new List<DxfTag> { new DxfTag(100, SubclassMarker.PolyfaceMesh) };
            var xdata = new List<XData>(); bool xdataSeen = false;
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (this.chunk.Code == 1001)
                {
                    xdataSeen = true;
                    string appId = this.DecodeEncodedNonAsciiCharacters(this.chunk.ReadString());
                    xdata.Add(this.ReadXDataRecord(this.GetApplicationRegistry(appId))); continue;
                }
                if (xdataSeen) throw new FormatException("POLYFACE common XData must follow its complete subclass packet.");
                if (header.Count >= 4096) throw new FormatException("Retained POLYFACE header exceeds its tag admission budget.");
                if (this.chunk.Code != 999) header.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            PolylineTypeFlags flags = PolylineTypeFlags.PolyfaceMesh; bool flagsSeen = false, privateClass = false, hasPrivate = false;
            short? verticesDeclared = null, facesDeclared = null; Vector3 normal = Vector3.UnitZ;
            var normalIndices = new Dictionary<short, int>();
            for (int i = 1; i < header.Count; i++)
            {
                DxfTag tag = header[i];
                if (tag.Code == 102) { hasPrivate = true; i = StoredPolylineGroupEnd(header, i); continue; }
                if (tag.Code == 100)
                {
                    if ((string)tag.Value == SubclassMarker.PolyfaceMesh) throw new FormatException("Duplicate POLYFACE subclass.");
                    privateClass = true; hasPrivate = true; continue;
                }
                if (privateClass) continue;
                switch (tag.Code)
                {
                    case 70:
                        if (flagsSeen || ((short)tag.Value & ~128) != 64) throw new FormatException("Unsupported ordinary POLYFACE flags.");
                        flags = (PolylineTypeFlags)(short)tag.Value; flagsSeen = true; break;
                    case 71:
                        if (verticesDeclared.HasValue) throw new FormatException("Duplicate POLYFACE advisory vertex count.");
                        verticesDeclared = (short)tag.Value; break;
                    case 72:
                        if (facesDeclared.HasValue) throw new FormatException("Duplicate POLYFACE advisory face count.");
                        facesDeclared = (short)tag.Value; break;
                    case 210: case 220: case 230:
                        if (normalIndices.ContainsKey(tag.Code)) throw new FormatException("Duplicate POLYFACE normal coordinate.");
                        normalIndices.Add(tag.Code, i);
                        if (tag.Code == 210) normal.X = (double)tag.Value;
                        else if (tag.Code == 220) normal.Y = (double)tag.Value;
                        else normal.Z = (double)tag.Value; break;
                    case 75:
                        if ((short)tag.Value != 0) throw new FormatException("Fitted POLYFACE records require an unsupported schema.");
                        break;
                    case 10: case 20: case 30: case 66: break;
                    default: hasPrivate = true; break;
                }
            }
            if (!flagsSeen || normalIndices.Count != 0 && normalIndices.Count != 3)
                throw new FormatException("Incomplete POLYFACE subclass packet.");
            var sequence = new List<PolyfaceMeshRecord>(); var points = new List<Vector3>(); var faces = new List<PolyfaceMeshFace>();
            while (this.chunk.Code == 0 && this.chunk.ReadString() == DxfObjectCode.Vertex)
            {
                if (sequence.Count >= 65536) throw new FormatException("Retained POLYFACE child sequence exceeds its record admission budget.");
                var record = this.ReadStoredPolyfaceMeshRecord(false); sequence.Add(record);
                if (record.IsFaceRecord)
                {
                    var indices = new List<short>();
                    for (short code = 71; code <= 74; code++)
                    {
                        if (!record.FaceSlots.TryGetValue(code, out int slot) || (short)record.Tags[slot].Value == 0) break;
                        indices.Add((short)record.Tags[slot].Value);
                    }
                    record.OriginalIndexes = indices.ToArray();
                    try { record.StoredFace = new PolyfaceMeshFace(indices) { Layer = record.Layer, Color = record.OriginalColor == null ? null : (AciColor)record.OriginalColor.Clone() }; }
                    catch (ArgumentException error) { throw new FormatException("Invalid POLYFACE face indices.", error); }
                    record.FaceIndex = faces.Count; faces.Add(record.StoredFace);
                }
                else { record.CoordinateIndex = points.Count; points.Add(record.Position); }
            }
            if (this.chunk.Code != 0 || this.chunk.ReadString() != DxfObjectCode.EndSequence) throw new FormatException("A POLYFACE child sequence requires SEQEND.");
            sequence.Add(this.ReadStoredPolyfaceMeshRecord(true));
            PolyfaceMesh result;
            try { result = new PolyfaceMesh(points, faces) { Flags = flags, Normal = normal }; }
            catch (ArgumentException error) { throw new FormatException("POLYFACE signed indices must resolve against the complete coordinate sequence.", error); }
            result.SetStoredRecords(this.doc, sequence.ToArray()); result.XData.AddRange(xdata);
            result.StoredHeaderTags = header; result.StoredNormal = result.Normal; result.HasPrivateHeader = hasPrivate;
            result.DeclaredVertexCount = verticesDeclared; result.DeclaredFaceCount = facesDeclared;
            foreach (var pair in normalIndices) result.StoredNormalIndices.Add(pair.Key, pair.Value);
            return result;
        }

        private PolyfaceMeshRecord ReadStoredPolyfaceMeshRecord(bool end)
        {
            SourceRecordIdentity source = this.CurrentSourceRecord;
            var tags = new List<DxfTag>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (tags.Count >= 4096 || ++this.polylineRecordTags > 1048576)
                    throw new FormatException("Retained polyface mesh records exceed their tag admission budget.");
                if (this.chunk.Code != 999) tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            var record = new PolyfaceMeshRecord(end ? DxfObjectCode.EndSequence : DxfObjectCode.Vertex, tags)
            { SourceDocument = this.doc, SourceVersion = this.doc.DrawingVariables.AcadVer };
            int i = 0;
            for (; i < tags.Count && tags[i].Code != 100 && tags[i].Code != 1001; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 102)
                {
                    string name = (string)tag.Value; int first = i;
                    int last = StoredPolylineGroupEnd(tags, i);
                    if (name == "{ACAD_REACTORS" || name == "{ACAD_XDICTIONARY")
                    {
                        if (record.MetadataGroups.Keys.Any(key => (string)tags[key].Value == name))
                            throw new FormatException("Duplicate polyface mesh metadata control group.");
                        record.MetadataGroups.Add(first, last);
                        for (int at = first + 1; at < last; at++)
                        {
                            if (name == "{ACAD_REACTORS" && tags[at].Code == 330) record.ReactorHandles.Add(PolylineRecordHandle(tags[at]));
                            else if (name == "{ACAD_XDICTIONARY" && tags[at].Code == 360 && record.ExtensionHandle == null) record.ExtensionHandle = PolylineRecordHandle(tags[at]);
                            else throw new FormatException("Invalid polyface mesh metadata control group.");
                        }
                    }
                    else record.HasPrivateData = true;
                    i = last;
                }
                else if (tag.Code == 5)
                {
                    if (record.Handle != null) throw new FormatException("Duplicate VERTEX/SEQEND identity.");
                    record.Handle = PolylineRecordHandle(tag);
                    record.IdentityIndex = i;
                }
                else if (tag.Code == 330)
                {
                    if (record.SourceOwner != null) throw new FormatException("Duplicate VERTEX/SEQEND owner.");
                    record.SourceOwner = PolylineRecordHandle(tag);
                    record.OwnerIndex = i;
                }
                else record.HasPrivateData = true;
            }
            if (record.Handle == null || record.Handle == "0" || record.SourceOwner == null || record.SourceOwner == "0")
                throw new FormatException("Retained VERTEX/SEQEND requires a nonzero physical identity and owner.");
            if (source == null || source.Handle != ulong.Parse(record.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture))
                throw new FormatException("VERTEX/SEQEND identity must belong to its own common header.");
            if (long.TryParse(record.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long identity) && identity >= this.doc.NumHandles && identity < long.MaxValue)
                this.doc.NumHandles = identity + 1;
            record.CommonEnd = i;
            int stage = 0; bool privateSubclass = false; bool flagsSeen = false;
            for (; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 1001) { record.XDataStart = i; this.ReadDatabaseXData(record, tags, i); break; }
                if (tag.Code == 102) { record.HasPrivateData = true; i = StoredPolylineGroupEnd(tags, i); continue; }
                if (tag.Code == 100)
                {
                    string name = (string)tag.Value;
                    if (!privateSubclass && stage == 0 && name == "AcDbEntity") stage = 1;
                    else if (!end && !privateSubclass && stage == 1 && name == "AcDbVertex") stage = 2;
                    else if (!end && !privateSubclass && stage == 1 && name == "AcDbFaceRecord")
                    { stage = 3; record.IsFaceRecord = true; record.FaceCommonEnd = i; }
                    else if (!end && !privateSubclass && stage == 2 && name == "AcDbPolyFaceMeshVertex") stage = 3;
                    else
                    {
                        if (stage < (end ? 1 : 3) || name == "AcDbEntity" || name == "AcDbVertex" || name == "AcDbPolyFaceMeshVertex" || name == "AcDbFaceRecord")
                            throw new FormatException("Invalid retained POLYFACE subclass sequence.");
                        privateSubclass = true; record.HasPrivateData = true;
                    }
                    continue;
                }
                if (privateSubclass) continue;
                if (stage == 1)
                {
                    if (tag.Code == 8)
                    {
                        if (record.Layer != null) throw new FormatException("Duplicate polyface mesh record layer.");
                        record.FaceLayerIndex = i;
                        record.Resources.Add(i, this.GetLayer(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                        record.OriginalResourceNames.Add(i, record.Layer.Name);
                    }
                    if (tag.Code == 6)
                    {
                        if (record.Linetype != null) throw new FormatException("Duplicate polyface mesh record linetype.");
                        record.Resources.Add(i, this.GetLinetype(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                        record.OriginalResourceNames.Add(i, record.Linetype.Name);
                    }
                    if (tag.Code == 62 || tag.Code == 420 || tag.Code == 430) record.FaceColorIndices.Add(i);
                    if (tag.Code == 62 && (record.OriginalColor == null || !record.OriginalColor.UseTrueColor)) record.OriginalColor = AciColor.FromCadIndex((short)tag.Value);
                    if (tag.Code == 420) record.OriginalColor = AciColor.FromTrueColor((int)tag.Value);
                    if (tag.ValueType == DxfTagValueType.Handle)
                    {
                        if (tag.Code != 347 && tag.Code != 390) record.HasPrivateData = true;
                    }
                    else if (!new short[] { 8, 6, 62, 420, 430, 440, 370, 48, 60, 67, 410, 284 }.Contains(tag.Code)) record.HasPrivateData = true;
                }
                if (!end && stage == 2) record.HasPrivateData = true;
                if (!end && stage == 3)
                {
                    if (tag.Code == 10 || tag.Code == 20 || tag.Code == 30)
                    {
                        if (record.Coordinates.ContainsKey(tag.Code)) throw new FormatException("Duplicate retained vertex coordinate.");
                        record.Coordinates.Add(tag.Code, i);
                        double value = (double)tag.Value;
                        if (tag.Code == 10) record.Position.X = value;
                        if (tag.Code == 20) record.Position.Y = value;
                        if (tag.Code == 30) record.Position.Z = value;
                    }
                    if (tag.Code == 70)
                    {
                        if (flagsSeen || (short)tag.Value != (record.IsFaceRecord ? 128 : 192)) throw new FormatException("A retained POLYFACE VERTEX requires flags 192 for coordinates or 128 for faces.");
                        flagsSeen = true;
                    }
                    if (tag.Code >= 71 && tag.Code <= 74)
                    {
                        if (!record.IsFaceRecord || record.FaceSlots.ContainsKey(tag.Code)) throw new FormatException("Invalid or duplicate POLYFACE face slot.");
                        record.FaceSlots.Add(tag.Code, i);
                    }
                    if (!new short[] { 10, 20, 30, 70, 40, 41, 42, 50, 91, 71, 72, 73, 74 }.Contains(tag.Code)) record.HasPrivateData = true;
                }
            }
            if (stage != (end ? 1 : 3) || !end && (!flagsSeen || record.Coordinates.Count != 3))
                throw new FormatException("Incomplete ordinary polyface mesh VERTEX/SEQEND record.");
            this.RecordSourceObject(record, source); this.loadedPolyfaceMeshRecords.Add(record);
            return record;
        }
        private void ResolveStoredPolyfaceMeshRecords()
        {
            foreach (PolyfaceMeshRecord record in this.loadedPolyfaceMeshRecords)
            {
                DxfObject owner = this.GetObjectBySourceHandle(record.SourceOwner);
                if (!ReferenceEquals(owner, record.Owner))
                {
                    var block = (record.Owner as PolyfaceMesh)?.Owner as netDxf.Blocks.Block;
                    if (record.IsSequenceEnd || block == null || !ReferenceEquals(owner, block.Record))
                        throw new FormatException("A VERTEX owner must be its actual source POLYLINE or containing BLOCK_RECORD; SEQEND requires POLYLINE.");
                    record.UsesBlockRecordOwner = true;
                }
                record.PersistentReactors.Clear();
                foreach (string handle in record.ReactorHandles)
                {
                    if (handle == "0") continue;
                    DxfObject target = this.GetObjectBySourceHandle(handle);
                    if (target == null) throw new FormatException("Unresolved retained polyface mesh reactor: " + handle);
                    record.PersistentReactors.Add(target); record.OriginalReactors.Add(target);
                }
                if (record.ExtensionHandle != null && record.ExtensionHandle != "0")
                {
                    var extension = this.GetObjectBySourceHandle(record.ExtensionHandle) as netDxf.Objects.DxfDictionary;
                    if (extension == null || !ReferenceEquals(extension.Owner, record)) throw new FormatException("Invalid retained polyface mesh extension dictionary.");
                    record.ExtensionDictionary = extension;
                }
                record.OriginalExtension = record.ExtensionDictionary;
                bool entity = false;
                for (int i = record.CommonEnd; i < record.XDataStart; i++)
                {
                    DxfTag tag = record.Tags[i];
                    if (tag.Code == 102) { i = StoredPolylineGroupEnd(record.Tags, i); continue; }
                    if (tag.Code == 100) { entity = (string)tag.Value == "AcDbEntity"; continue; }
                    if (!entity || tag.Code != 347 && tag.Code != 390 || PolylineRecordHandle(tag) == "0") continue;
                    DxfObject target = this.GetObjectBySourceHandle(PolylineRecordHandle(tag));
                    if (target == null) throw new FormatException("Unresolved retained polyface mesh common reference: " + tag.Value);
                    record.Resources.Add(i, target);
                }
            }
        }
    }
}
