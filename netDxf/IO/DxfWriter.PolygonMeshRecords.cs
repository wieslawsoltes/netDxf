using System;
using System.Linq;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateStoredPolygonMeshRecords()
        {
            // Metadata may grow after load through the public XData/reactor APIs.
            // Validate the shared reader budget before writing or allocating anything.
            long total = 0;
            foreach (DxfObject item in this.doc.AddedObjects.Values)
            {
                int count = item is PolygonMeshRecord mesh ? mesh.TopologyTagCount()
                    : item is Polyline3DRecord polylineRecord ? polylineRecord.TopologyTagCount() : 0;
                if (count > 4096)
                    throw new NotSupportedException("A retained VERTEX/SEQEND exceeds the 4096-tag packet admission budget.");
                total += count;
                if (total > 1048576)
                    throw new NotSupportedException("Retained VERTEX/SEQEND records exceed the shared document tag admission budget.");
            }
            foreach (PolygonMesh polyline in this.doc.AddedObjects.Values.OfType<PolygonMesh>())
            {
                polyline.ValidateStoredRecords(this.doc, true);
                if (this.isBinary) continue;
                foreach (PolygonMeshRecord record in polyline.StoredRecords)
                {
                    foreach (DxfTag tag in record.Tags) if (tag.Value is string text) CheckDatabaseText(text);
                    foreach (XData data in record.XData.Values)
                        foreach (XDataRecord tag in data.XDataRecord) if (tag.Value is string text) CheckDatabaseText(text);
                }
            }
        }
        private void WriteStoredPolygonMeshRecords(PolygonMesh polyline)
        {
            for (int i = 0; i < polyline.U; i++) for (int j = 0; j < polyline.V; j++)
            { int slot = i + j * polyline.U; this.WriteStoredPolygonMeshRecord(polyline.VertexRecords[slot], polyline.Vertexes[slot]); }
            this.WriteStoredPolygonMeshRecord(polyline.EndSequenceRecord, Vector3.Zero);
        }
        private void WriteStoredPolygonMeshRecord(PolygonMeshRecord record, Vector3 point)
        {
            this.chunk.Write(0, record.CodeName);
            bool extensionWritten = false, reactorsWritten = false;
            for (int i = 0; i < record.XDataStart; i++)
            {
                if (i == record.CommonEnd)
                {
                    if (!extensionWritten && record.ExtensionDictionary != null) this.WritePolygonMeshExtension(record);
                    if (!reactorsWritten && record.PersistentReactors.Count > 0) this.WritePolygonMeshReactors(record);
                }
                DxfTag tag = record.Tags[i];
                if (record.MetadataGroups.TryGetValue(i, out int last))
                {
                    bool extension = (string)tag.Value == "{ACAD_XDICTIONARY";
                    bool unchanged = extension ? ReferenceEquals(record.ExtensionDictionary, record.OriginalExtension)
                        : record.PersistentReactors.SequenceEqual(record.OriginalReactors)
                            && record.ReactorHandles.Where(handle => handle != "0").SequenceEqual(record.PersistentReactors.Select(target => target.Handle));
                    if (unchanged) for (int at = i; at <= last; at++) this.WriteDatabaseTag(record.Tags[at], false);
                    else if (extension) this.WritePolygonMeshExtension(record);
                    else this.WritePolygonMeshReactors(record);
                    if (extension) extensionWritten = true; else reactorsWritten = true;
                    i = last; continue;
                }
                if (i == record.IdentityIndex) this.chunk.Write(5, record.Handle);
                else if (i == record.OwnerIndex) this.chunk.Write(330, record.StoredOwner.Handle);
                else if (record.Coordinates.TryGetValue(tag.Code, out int coordinate) && coordinate == i)
                    this.chunk.Write(tag.Code, tag.Code == 10 ? point.X : tag.Code == 20 ? point.Y : point.Z);
                else if (record.Resources.TryGetValue(i, out DxfObject resource))
                {
                    if (resource is TableObject table && record.OriginalResourceNames.TryGetValue(i, out string original))
                    {
                        if (original == table.Name) this.WriteDatabaseTag(tag, false);
                        else this.chunk.Write(tag.Code, this.EncodeNonAsciiCharacters(table.Name));
                    }
                    else this.chunk.Write(tag.Code, resource.Handle);
                }
                else this.WriteDatabaseTag(tag, false);
            }
            this.WriteXData(record.XData);
        }
        private void WritePolygonMeshExtension(PolygonMeshRecord record)
        {
            if (record.ExtensionDictionary == null) return;
            this.chunk.Write(102, "{ACAD_XDICTIONARY"); this.chunk.Write(360, record.ExtensionDictionary.Handle); this.chunk.Write(102, "}");
        }
        private void WritePolygonMeshReactors(PolygonMeshRecord record)
        {
            if (record.PersistentReactors.Count == 0) return;
            this.chunk.Write(102, "{ACAD_REACTORS");
            foreach (DxfObject target in record.PersistentReactors) this.chunk.Write(330, target.Handle);
            this.chunk.Write(102, "}");
        }
    }
}
