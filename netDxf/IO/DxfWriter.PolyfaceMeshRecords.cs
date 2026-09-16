using System;
using System.Linq;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateStoredPolyfaceMeshRecords()
        {
            foreach (PolyfaceMesh polyline in this.doc.AddedObjects.Values.OfType<PolyfaceMesh>())
            {
                polyline.ValidateStoredRecords(this.doc, true);
                if (polyline.HasStoredRecords && polyline.StoredHeaderTags.Count + (polyline.StoredNormalIndices.Count == 0 && polyline.Normal != polyline.StoredNormal ? 3 : 0) > 4096)
                    throw new NotSupportedException("Retained POLYFACE header exceeds its physical tag admission budget.");
                if (this.isBinary) continue;
                if (polyline.StoredHeaderTags != null)
                    foreach (DxfTag tag in polyline.StoredHeaderTags) if (tag.Value is string text) CheckDatabaseText(text);
                foreach (PolyfaceMeshRecord record in polyline.StoredRecords)
                {
                    foreach (DxfTag tag in record.Tags) if (tag.Value is string text) CheckDatabaseText(text);
                    foreach (XData data in record.XData.Values)
                        foreach (XDataRecord tag in data.XDataRecord) if (tag.Value is string text) CheckDatabaseText(text);
                }
            }
        }
        private void WriteStoredPolyfaceMeshRecords(PolyfaceMesh polyline)
        {
            foreach (PolyfaceMeshRecord record in polyline.RecordSequence)
                this.WriteStoredPolyfaceMeshRecord(record, record.CoordinateIndex < 0 ? Vector3.Zero : polyline.Vertexes[record.CoordinateIndex]);
        }
        private void WriteStoredPolyfaceMeshHeader(PolyfaceMesh mesh)
        {
            bool changed = mesh.Normal != mesh.StoredNormal;
            bool appendNormal = changed && mesh.StoredNormalIndices.Count == 0;
            for (int i = 0; i < mesh.StoredHeaderTags.Count; i++)
            {
                if (appendNormal && i == mesh.StoredHeaderPublicEnd)
                { this.chunk.Write(210, mesh.Normal.X); this.chunk.Write(220, mesh.Normal.Y); this.chunk.Write(230, mesh.Normal.Z); }
                DxfTag tag = mesh.StoredHeaderTags[i];
                if (changed && mesh.StoredNormalIndices.TryGetValue(tag.Code, out int normal) && normal == i)
                    this.chunk.Write(tag.Code, tag.Code == 210 ? mesh.Normal.X : tag.Code == 220 ? mesh.Normal.Y : mesh.Normal.Z);
                else this.WriteDatabaseTag(tag, false);
            }
            if (appendNormal && mesh.StoredHeaderPublicEnd == mesh.StoredHeaderTags.Count)
            { this.chunk.Write(210, mesh.Normal.X); this.chunk.Write(220, mesh.Normal.Y); this.chunk.Write(230, mesh.Normal.Z); }
            this.WriteXData(mesh.XData);
        }

        private void WriteStoredPolyfaceMeshRecord(PolyfaceMeshRecord record, Vector3 point)
        {
            this.chunk.Write(0, record.CodeName);
            bool extensionWritten = false, reactorsWritten = false;
            for (int i = 0; i < record.XDataStart; i++)
            {
                if (i == record.CommonEnd)
                {
                    if (!extensionWritten && record.ExtensionDictionary != null) this.WritePolyfaceMeshExtension(record);
                    if (!reactorsWritten && record.PersistentReactors.Count > 0) this.WritePolyfaceMeshReactors(record);
                }
                if (record.Face != null && i == record.FaceCommonEnd)
                {
                    if (record.FaceLayerIndex < 0 && record.Face.Layer != null)
                        this.chunk.Write(8, this.EncodeNonAsciiCharacters(record.Face.Layer.Name));
                    if (!record.FaceColorUnchanged && record.Face.Color != null)
                    {
                        this.chunk.Write(62, record.Face.Color.Index);
                        this.WriteTrueColor(record.Face.Color);
                    }
                }
                if (record.Face != null && !record.FaceColorUnchanged && record.FaceColorIndices.Contains(i)) continue;
                DxfTag tag = record.Tags[i];
                if (record.MetadataGroups.TryGetValue(i, out int last))
                {
                    bool extension = (string)tag.Value == "{ACAD_XDICTIONARY";
                    bool unchanged = extension ? ReferenceEquals(record.ExtensionDictionary, record.OriginalExtension)
                        : record.PersistentReactors.SequenceEqual(record.OriginalReactors)
                            && record.ReactorHandles.Where(handle => handle != "0").SequenceEqual(record.PersistentReactors.Select(target => target.Handle));
                    if (unchanged) for (int at = i; at <= last; at++) this.WriteDatabaseTag(record.Tags[at], false);
                    else if (extension) this.WritePolyfaceMeshExtension(record);
                    else this.WritePolyfaceMeshReactors(record);
                    if (extension) extensionWritten = true; else reactorsWritten = true;
                    i = last; continue;
                }
                if (i == record.IdentityIndex) this.chunk.Write(5, record.Handle);
                else if (i == record.OwnerIndex) this.chunk.Write(330, record.StoredOwner.Handle);
                else if (record.Face != null && i == record.FaceLayerIndex)
                {
                    if (record.Face.Layer != null)
                    {
                        if (record.OriginalResourceNames.TryGetValue(i, out string original) && original == record.Face.Layer.Name) this.WriteDatabaseTag(tag, false);
                        else this.chunk.Write(8, this.EncodeNonAsciiCharacters(record.Face.Layer.Name));
                    }
                }
                else if (record.Face != null && !record.FaceIndexesUnchanged && record.FaceSlots.TryGetValue(tag.Code, out int faceSlot) && faceSlot == i)
                {
                    int slot = tag.Code - 71;
                    if (slot < record.Face.VertexIndexes.Length) this.chunk.Write(tag.Code, record.Face.VertexIndexes[slot]);
                    else this.WriteDatabaseTag(tag, false);
                }
                else if (!record.IsFaceRecord && record.Coordinates.TryGetValue(tag.Code, out int coordinate) && coordinate == i)
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
        private void WritePolyfaceMeshExtension(PolyfaceMeshRecord record)
        {
            if (record.ExtensionDictionary == null) return;
            this.chunk.Write(102, "{ACAD_XDICTIONARY"); this.chunk.Write(360, record.ExtensionDictionary.Handle); this.chunk.Write(102, "}");
        }
        private void WritePolyfaceMeshReactors(PolyfaceMeshRecord record)
        {
            if (record.PersistentReactors.Count == 0) return;
            this.chunk.Write(102, "{ACAD_REACTORS");
            foreach (DxfObject target in record.PersistentReactors) this.chunk.Write(330, target.Handle);
            this.chunk.Write(102, "}");
        }
    }
}
