using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Polyline3DRecord> loadedPolylineRecords = new List<Polyline3DRecord>();
        private int polylineRecordTags;

        private Polyline3D ReadStoredPolylineSequence(PolylineTypeFlags flags, Vector3 normal, List<XData> xdata)
        {
            var records = new List<Polyline3DRecord>();
            while (this.chunk.Code == 0 && this.chunk.ReadString() == DxfObjectCode.Vertex)
            {
                if (records.Count >= 65536) throw new FormatException("A retained polyline exceeds the vertex admission budget.");
                records.Add(this.ReadStoredPolylineRecord(false));
            }
            if (this.chunk.Code != 0 || this.chunk.ReadString() != DxfObjectCode.EndSequence)
                throw new FormatException("A POLYLINE vertex sequence requires SEQEND.");
            Polyline3DRecord end = this.ReadStoredPolylineRecord(true);
            var result = new Polyline3D(records.Select(record => record.Position), flags.HasFlag(PolylineTypeFlags.ClosedPolylineOrClosedPolygonMeshInM))
            { Flags = flags, Normal = normal };
            result.XData.AddRange(xdata); result.SetStoredRecords(this.doc, records, end);
            return result;
        }

        private Polyline3DRecord ReadStoredPolylineRecord(bool end)
        {
            SourceRecordIdentity source = this.CurrentSourceRecord;
            var tags = new List<DxfTag>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (tags.Count >= 4096 || ++this.polylineRecordTags > 1048576)
                    throw new FormatException("Retained polyline records exceed their tag admission budget.");
                if (this.chunk.Code != 999) tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            var record = new Polyline3DRecord(end ? DxfObjectCode.EndSequence : DxfObjectCode.Vertex, tags)
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
                            throw new FormatException("Duplicate polyline metadata control group.");
                        record.MetadataGroups.Add(first, last);
                        for (int at = first + 1; at < last; at++)
                        {
                            if (name == "{ACAD_REACTORS" && tags[at].Code == 330) record.ReactorHandles.Add(PolylineRecordHandle(tags[at]));
                            else if (name == "{ACAD_XDICTIONARY" && tags[at].Code == 360 && record.ExtensionHandle == null) record.ExtensionHandle = PolylineRecordHandle(tags[at]);
                            else throw new FormatException("Invalid polyline metadata control group.");
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
                    string expected = stage == 0 ? "AcDbEntity" : stage == 1 ? "AcDbVertex" : "AcDb3dPolylineVertex";
                    if (!privateSubclass && stage < (end ? 1 : 3) && name == expected) stage++;
                    else
                    {
                        if (stage < (end ? 1 : 3) || name == "AcDbEntity" || name == "AcDbVertex" || name == "AcDb3dPolylineVertex")
                            throw new FormatException("Invalid retained polyline subclass sequence.");
                        privateSubclass = true;
                        record.HasPrivateData = true;
                    }
                    continue;
                }
                if (privateSubclass) continue;
                if (stage == 1)
                {
                    if (tag.Code == 8)
                    {
                        if (record.Layer != null) throw new FormatException("Duplicate polyline record layer.");
                        record.Resources.Add(i, this.GetLayer(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                        record.OriginalResourceNames.Add(i, record.Layer.Name);
                    }
                    if (tag.Code == 6)
                    {
                        if (record.Linetype != null) throw new FormatException("Duplicate polyline record linetype.");
                        record.Resources.Add(i, this.GetLinetype(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                        record.OriginalResourceNames.Add(i, record.Linetype.Name);
                    }
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
                        if (flagsSeen || (short)tag.Value != 32) throw new FormatException("The retained VERTEX slice requires ordinary 3D vertex flags 32.");
                        flagsSeen = true;
                    }
                    if (!new short[] { 10, 20, 30, 70, 40, 41, 42, 50, 91 }.Contains(tag.Code)) record.HasPrivateData = true;
                }
            }
            if (stage != (end ? 1 : 3) || !end && (!flagsSeen || record.Coordinates.Count != 3))
                throw new FormatException("Incomplete ordinary 3D VERTEX/SEQEND record.");
            this.RecordSourceObject(record, source); this.loadedPolylineRecords.Add(record);
            return record;
        }
        private static int StoredPolylineGroupEnd(List<DxfTag> tags, int start)
        {
            if (!((string)tags[start].Value).StartsWith("{", StringComparison.Ordinal)) throw new FormatException("Invalid polyline control group.");
            int depth = 1;
            for (int i = start + 1; i < tags.Count; i++)
            {
                if (tags[i].Code != 102) continue;
                string text = (string)tags[i].Value;
                if (text == "}" && --depth == 0) return i;
                if (text.StartsWith("{", StringComparison.Ordinal) && ++depth > 32) throw new FormatException("Polyline control groups are too deeply nested.");
            }
            throw new FormatException("Unterminated polyline control group.");
        }
        private static string PolylineRecordHandle(DxfTag tag)
        { return ulong.Parse((string)tag.Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture).ToString("X", CultureInfo.InvariantCulture); }

        private void ResolveStoredPolylineRecords()
        {
            foreach (Polyline3DRecord record in this.loadedPolylineRecords)
            {
                DxfObject owner = this.GetObjectBySourceHandle(record.SourceOwner);
                if (!ReferenceEquals(owner, record.Owner))
                {
                    var block = (record.Owner as Polyline3D)?.Owner as netDxf.Blocks.Block;
                    if (record.IsSequenceEnd || block == null || !ReferenceEquals(owner, block.Record))
                        throw new FormatException("A VERTEX owner must be its actual source POLYLINE or containing BLOCK_RECORD; SEQEND requires POLYLINE.");
                    record.UsesBlockRecordOwner = true;
                }
                record.PersistentReactors.Clear();
                foreach (string handle in record.ReactorHandles)
                {
                    if (handle == "0") continue;
                    DxfObject target = this.GetObjectBySourceHandle(handle);
                    if (target == null) throw new FormatException("Unresolved retained polyline reactor: " + handle);
                    record.PersistentReactors.Add(target); record.OriginalReactors.Add(target);
                }
                if (record.ExtensionHandle != null && record.ExtensionHandle != "0")
                {
                    var extension = this.GetObjectBySourceHandle(record.ExtensionHandle) as netDxf.Objects.DxfDictionary;
                    if (extension == null || !ReferenceEquals(extension.Owner, record)) throw new FormatException("Invalid retained polyline extension dictionary.");
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
                    if (target == null) throw new FormatException("Unresolved retained polyline common reference: " + tag.Value);
                    record.Resources.Add(i, target);
                }
            }
        }
    }
}
