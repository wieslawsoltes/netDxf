using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private readonly List<Polyline2DRecord> loadedPolyline2DRecords = new List<Polyline2DRecord>();

        private Polyline2D ReadStoredPolyline2D()
        {
            var header = new List<DxfTag> { new DxfTag(100, SubclassMarker.Polyline2D) };
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
                if (xdataSeen) throw new FormatException("Legacy POLYLINE common XData must follow its complete subclass packet.");
                if (header.Count >= 4096) throw new FormatException("Retained legacy POLYLINE header exceeds its tag admission budget.");
                if (this.chunk.Code != 999) header.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            var indices = new Dictionary<short, int>(); var seen = new HashSet<short>();
            var flags = PolylineTypeFlags.OpenPolyline; short smooth = 0;
            double elevation = 0, thickness = 0; double? startWidth = null, endWidth = null;
            Vector3 normal = Vector3.UnitZ; bool privateClass = false, hasPrivate = false; int publicEnd = header.Count;
            for (int i = 1; i < header.Count; i++)
            {
                DxfTag tag = header[i];
                if (tag.Code == 102) { hasPrivate = true; i = StoredPolylineGroupEnd(header, i); continue; }
                if (tag.Code == 100)
                {
                    if ((string)tag.Value == SubclassMarker.Polyline2D) throw new FormatException("Duplicate legacy POLYLINE subclass.");
                    if (!privateClass) publicEnd = i;
                    privateClass = true; hasPrivate = true; continue;
                }
                if (privateClass) continue;
                if (new short[] { 10, 20, 30, 39, 40, 41, 66, 70, 75, 210, 220, 230 }.Contains(tag.Code) && !seen.Add(tag.Code))
                    throw new FormatException("Duplicate qualified legacy POLYLINE field.");
                switch (tag.Code)
                {
                    case 10: case 20:
                        if ((double)tag.Value != 0) throw new FormatException("Legacy POLYLINE requires zero dummy X/Y coordinates.");
                        indices.Add(tag.Code, i); break;
                    case 30: elevation = (double)tag.Value; indices.Add(tag.Code, i); break;
                    case 39: thickness = (double)tag.Value; indices.Add(tag.Code, i); break;
                    case 40: startWidth = (double)tag.Value; Polyline2D.ValidateWidth(startWidth.Value, "LegacyDefaultStartWidth"); indices.Add(tag.Code, i); break;
                    case 41: endWidth = (double)tag.Value; Polyline2D.ValidateWidth(endWidth.Value, "LegacyDefaultEndWidth"); indices.Add(tag.Code, i); break;
                    case 70: flags = (PolylineTypeFlags)(short)tag.Value; indices.Add(tag.Code, i); break;
                    case 75: smooth = (short)tag.Value; break;
                    case 210: normal.X = (double)tag.Value; indices.Add(tag.Code, i); break;
                    case 220: normal.Y = (double)tag.Value; indices.Add(tag.Code, i); break;
                    case 230: normal.Z = (double)tag.Value; indices.Add(tag.Code, i); break;
                    case 66:
                        if ((short)tag.Value != 1) throw new FormatException("Legacy POLYLINE group 66 must indicate its child sequence.");
                        break;
                    default: hasPrivate = true; break;
                }
            }
            if (((int)flags & ~135) != 0) throw new FormatException("The AcDb2dPolyline subclass cannot contain 3D or mesh roles.");
            // Preserve the existing fitted reader path; retained ordinary records do not claim that schema.
            if (((int)flags & 6) != 0 || smooth != 0)
                return this.ReadFittedLegacyPolyline2D(flags, normal, elevation, thickness, smooth, xdata);
            int pointCount = indices.Keys.Count(code => code == 10 || code == 20 || code == 30);
            if (pointCount != 0 && pointCount != 3)
                throw new FormatException("A retained legacy POLYLINE requires a complete dummy point or its complete omission.");
            int normalCount = indices.Keys.Count(code => code == 210 || code == 220 || code == 230);
            if (normalCount != 0 && normalCount != 3 || Vector3.IsZero(normal))
                throw new FormatException("Legacy POLYLINE requires a complete finite nonzero extrusion normal.");
            var records = new List<Polyline2DRecord>();
            while (this.chunk.Code == 0 && this.chunk.ReadString() == DxfObjectCode.Vertex)
            {
                if (records.Count >= 65536) throw new FormatException("Retained legacy POLYLINE exceeds its record admission budget.");
                records.Add(this.ReadStoredPolyline2DRecord(false));
            }
            if (this.chunk.Code != 0 || this.chunk.ReadString() != DxfObjectCode.EndSequence)
                throw new FormatException("A legacy POLYLINE child sequence requires SEQEND.");
            Polyline2DRecord end = this.ReadStoredPolyline2DRecord(true);
            var result = new Polyline2D(records.Select(record => record.Vertex))
            { Flags = flags, Normal = normal, Elevation = elevation, Thickness = thickness };
            result.XData.AddRange(xdata); result.SetStoredRecords(this.doc, records.ToArray(), end);
            result.StoredHeaderTags = header; result.StoredHeaderPublicEnd = publicEnd; result.HasPrivateHeader = hasPrivate;
            result.StoredNormal = result.Normal; result.LegacyDefaultStartWidth = startWidth; result.LegacyDefaultEndWidth = endWidth;
            foreach (var pair in indices) result.StoredHeaderIndices.Add(pair.Key, pair.Value);
            return result;
        }
        private Polyline2D ReadFittedLegacyPolyline2D(PolylineTypeFlags flags, Vector3 normal, double elevation,
            double thickness, short smooth, List<XData> xdata)
        {
            var vertices = new List<Vertex>();
            while (this.chunk.Code == 0 && this.chunk.ReadString() == DxfObjectCode.Vertex) vertices.Add(this.ReadVertex(false));
            if (this.chunk.Code != 0 || this.chunk.ReadString() != DxfObjectCode.EndSequence)
                throw new FormatException("A fitted legacy POLYLINE requires SEQEND.");
            this.chunk.Next(); while (this.chunk.Code != 0) this.chunk.Next();
            var result = new Polyline { Vertexes = vertices, Flags = flags, Normal = normal, Elevation = elevation,
                Thickness = thickness, SmoothType = smooth == 8 ? PolylineSmoothType.NoSmooth : (PolylineSmoothType)smooth };
            result.XData.AddRange(xdata); return this.ReadPolyline2D(result);
        }

        private Polyline2DRecord ReadStoredPolyline2DRecord(bool end)
        {
            SourceRecordIdentity source = this.CurrentSourceRecord;
            var tags = new List<DxfTag>();
            this.chunk.Next();
            while (this.chunk.Code != 0)
            {
                if (tags.Count >= 4096 || ++this.polylineRecordTags > 1048576)
                    throw new FormatException("Retained legacy 2D polyline records exceed their tag admission budget.");
                if (this.chunk.Code != 999) tags.Add(new DxfTag(this.chunk.Code, this.chunk.Value));
                this.chunk.Next();
            }
            var record = new Polyline2DRecord(end ? DxfObjectCode.EndSequence : DxfObjectCode.Vertex, tags)
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
                            throw new FormatException("Duplicate legacy 2D polyline metadata control group.");
                        record.MetadataGroups.Add(first, last);
                        for (int at = first + 1; at < last; at++)
                        {
                            if (name == "{ACAD_REACTORS" && tags[at].Code == 330) record.ReactorHandles.Add(PolylineRecordHandle(tags[at]));
                            else if (name == "{ACAD_XDICTIONARY" && tags[at].Code == 360 && record.ExtensionHandle == null) record.ExtensionHandle = PolylineRecordHandle(tags[at]);
                            else throw new FormatException("Invalid legacy 2D polyline metadata control group.");
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
            record.GeometryEnd = tags.Count;
            for (; i < tags.Count; i++)
            {
                DxfTag tag = tags[i];
                if (tag.Code == 1001) { if (record.GeometryEnd == tags.Count) record.GeometryEnd = i; record.XDataStart = i; this.ReadDatabaseXData(record, tags, i); break; }
                if (tag.Code == 102) { record.HasPrivateData = true; i = StoredPolylineGroupEnd(tags, i); continue; }
                if (tag.Code == 100)
                {
                    string name = (string)tag.Value;
                    string expected = stage == 0 ? "AcDbEntity" : stage == 1 ? "AcDbVertex" : "AcDb2dVertex";
                    if (!privateSubclass && stage < (end ? 1 : 3) && name == expected) stage++;
                    else
                    {
                        if (stage < (end ? 1 : 3) || name == "AcDbEntity" || name == "AcDbVertex" || name == "AcDb2dVertex")
                            throw new FormatException("Invalid retained legacy 2D polyline subclass sequence.");
                        if (!privateSubclass) record.GeometryEnd = i;
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
                        if (record.Layer != null) throw new FormatException("Duplicate legacy 2D polyline record layer.");
                        record.Resources.Add(i, this.GetLayer(this.DecodeEncodedNonAsciiCharacters((string)tag.Value)));
                        record.OriginalResourceNames.Add(i, record.Layer.Name);
                    }
                    if (tag.Code == 6)
                    {
                        if (record.Linetype != null) throw new FormatException("Duplicate legacy 2D polyline record linetype.");
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
                    if (new short[] { 10, 20, 30, 40, 41, 42, 91 }.Contains(tag.Code))
                    {
                        if (record.GeometryIndices.ContainsKey(tag.Code)) throw new FormatException("Duplicate retained legacy vertex geometry field.");
                        record.GeometryIndices.Add(tag.Code, i);
                        if (tag.Code == 10 || tag.Code == 20 || tag.Code == 30) record.Coordinates.Add(tag.Code, i);
                    }
                    else if (tag.Code == 70)
                    {
                        if (flagsSeen || (short)tag.Value != 0) throw new FormatException("Retained legacy VERTEX requires ordinary flags zero or absent.");
                        flagsSeen = true;
                    }
                    else if (tag.Code == 50) throw new FormatException("Fitted legacy VERTEX tangents require an unsupported retained schema.");
                    else record.HasPrivateData = true;
                }
            }
            if (stage != (end ? 1 : 3) || !end && record.Coordinates.Count != 3)
                throw new FormatException("Incomplete ordinary legacy VERTEX/SEQEND record.");
            if (!end)
            {
                double x = (double)tags[record.GeometryIndices[10]].Value;
                double y = (double)tags[record.GeometryIndices[20]].Value;
                if ((double)tags[record.GeometryIndices[30]].Value != 0)
                    throw new FormatException("The retained legacy 2D slice requires planar child Z=0; elevation belongs to the parent.");
                var vertex = new Polyline2DVertex(x, y);
                if (record.GeometryIndices.TryGetValue(40, out int start)) vertex.StartWidthOverride = (double)tags[start].Value;
                if (record.GeometryIndices.TryGetValue(41, out int finish)) vertex.EndWidthOverride = (double)tags[finish].Value;
                if (record.GeometryIndices.TryGetValue(42, out int bulge)) vertex.Bulge = (double)tags[bulge].Value;
                if (record.GeometryIndices.TryGetValue(91, out int identifier)) vertex.VertexIdentifier = (int)tags[identifier].Value;
                record.StoredVertex = vertex;
            }
            this.RecordSourceObject(record, source); this.loadedPolyline2DRecords.Add(record);
            return record;
        }
        private void ResolveStoredPolyline2DRecords()
        {
            foreach (Polyline2DRecord record in this.loadedPolyline2DRecords)
            {
                DxfObject owner = this.GetObjectBySourceHandle(record.SourceOwner);
                if (!ReferenceEquals(owner, record.Owner))
                {
                    var block = (record.Owner as Polyline2D)?.Owner as netDxf.Blocks.Block;
                    if (record.IsSequenceEnd || block == null || !ReferenceEquals(owner, block.Record))
                        throw new FormatException("A VERTEX owner must be its actual source POLYLINE or containing BLOCK_RECORD; SEQEND requires POLYLINE.");
                    record.UsesBlockRecordOwner = true;
                }
                record.PersistentReactors.Clear();
                foreach (string handle in record.ReactorHandles)
                {
                    if (handle == "0") continue;
                    DxfObject target = this.GetObjectBySourceHandle(handle);
                    if (target == null) throw new FormatException("Unresolved retained legacy 2D polyline reactor: " + handle);
                    record.PersistentReactors.Add(target); record.OriginalReactors.Add(target);
                }
                if (record.ExtensionHandle != null && record.ExtensionHandle != "0")
                {
                    var extension = this.GetObjectBySourceHandle(record.ExtensionHandle) as netDxf.Objects.DxfDictionary;
                    if (extension == null || !ReferenceEquals(extension.Owner, record)) throw new FormatException("Invalid retained legacy 2D polyline extension dictionary.");
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
                    if (target == null) throw new FormatException("Unresolved retained legacy 2D polyline common reference: " + tag.Value);
                    record.Resources.Add(i, target);
                }
            }
        }
    }
}
