using System;
using System.Linq;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateStoredPolylineRecords()
        {
            foreach (Polyline3D polyline in this.doc.AddedObjects.Values.OfType<Polyline3D>())
            {
                polyline.ValidateStoredRecords(this.doc, true);
                if (this.isBinary) continue;
                foreach (Polyline3DRecord record in polyline.StoredRecords)
                {
                    foreach (DxfTag tag in record.Tags) if (tag.Value is string text) CheckDatabaseText(text);
                    foreach (XData data in record.XData.Values)
                        foreach (XDataRecord tag in data.XDataRecord) if (tag.Value is string text) CheckDatabaseText(text);
                }
            }
        }
        private void WriteStoredPolylineRecords(Polyline3D polyline)
        {
            for (int i = 0; i < polyline.VertexRecords.Count; i++) this.WriteStoredPolylineRecord(polyline.VertexRecords[i], polyline.Vertexes[i]);
            this.WriteStoredPolylineRecord(polyline.EndSequenceRecord, Vector3.Zero);
        }
        private void WriteStoredPolylineRecord(Polyline3DRecord record, Vector3 point)
        {
            this.chunk.Write(0, record.CodeName);
            bool extensionWritten = false, reactorsWritten = false;
            for (int i = 0; i < record.XDataStart; i++)
            {
                if (i == record.CommonEnd)
                {
                    if (!extensionWritten && record.ExtensionDictionary != null) this.WritePolylineExtension(record);
                    if (!reactorsWritten && record.PersistentReactors.Count > 0) this.WritePolylineReactors(record);
                }
                DxfTag tag = record.Tags[i];
                if (record.MetadataGroups.TryGetValue(i, out int last))
                {
                    bool extension = (string)tag.Value == "{ACAD_XDICTIONARY";
                    bool unchanged = extension ? ReferenceEquals(record.ExtensionDictionary, record.OriginalExtension)
                        : record.PersistentReactors.SequenceEqual(record.OriginalReactors);
                    if (unchanged) for (int at = i; at <= last; at++) this.WriteDatabaseTag(record.Tags[at], false);
                    else if (extension) this.WritePolylineExtension(record);
                    else this.WritePolylineReactors(record);
                    if (extension) extensionWritten = true; else reactorsWritten = true;
                    i = last; continue;
                }
                if (i == record.IdentityIndex) this.chunk.Write(5, record.Handle);
                else if (i == record.OwnerIndex) this.chunk.Write(330, record.Owner.Handle);
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
        private void WritePolylineExtension(Polyline3DRecord record)
        {
            if (record.ExtensionDictionary == null) return;
            this.chunk.Write(102, "{ACAD_XDICTIONARY"); this.chunk.Write(360, record.ExtensionDictionary.Handle); this.chunk.Write(102, "}");
        }
        private void WritePolylineReactors(Polyline3DRecord record)
        {
            if (record.PersistentReactors.Count == 0) return;
            this.chunk.Write(102, "{ACAD_REACTORS");
            foreach (DxfObject target in record.PersistentReactors) this.chunk.Write(330, target.Handle);
            this.chunk.Write(102, "}");
        }
    }
}
