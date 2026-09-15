using System;
using System.Linq;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf.IO
{
    internal sealed partial class DxfWriter
    {
        private void ValidateStoredPolyline2DRecords()
        {
            foreach (Polyline2D polyline in this.doc.AddedObjects.Values.OfType<Polyline2D>())
            {
                polyline.ValidateStoredRecords(this.doc, true);
                if (polyline.HasStoredRecords && polyline.StoredHeaderTags.Count + polyline.LegacyHeaderValues().Count - polyline.StoredHeaderIndices.Count > 4096)
                    throw new NotSupportedException("Retained legacy POLYLINE header exceeds its tag admission budget.");
                if (this.isBinary) continue;
                if (polyline.StoredHeaderTags != null)
                    foreach (DxfTag tag in polyline.StoredHeaderTags) if (tag.Value is string text) CheckDatabaseText(text);
                foreach (Polyline2DRecord record in polyline.StoredRecords)
                {
                    foreach (DxfTag tag in record.Tags) if (tag.Value is string text) CheckDatabaseText(text);
                    foreach (XData data in record.XData.Values)
                        foreach (XDataRecord tag in data.XDataRecord) if (tag.Value is string text) CheckDatabaseText(text);
                }
            }
        }
        private void WriteStoredPolyline2DRecords(Polyline2D polyline)
        {
            this.WriteStoredPolyline2DHeader(polyline);
            foreach (Polyline2DRecord record in polyline.StoredRecords) this.WriteStoredPolyline2DRecord(record);
        }
        private void WriteStoredPolyline2DHeader(Polyline2D polyline)
        {
            var values = polyline.LegacyHeaderValues();
            for (int i = 0; i < polyline.StoredHeaderTags.Count; i++)
            {
                if (i == polyline.StoredHeaderPublicEnd)
                    foreach (var pair in values) if (!polyline.StoredHeaderIndices.ContainsKey(pair.Key)) this.WriteDatabaseTag(pair.Value, false);
                DxfTag tag = polyline.StoredHeaderTags[i];
                if (polyline.StoredHeaderIndices.TryGetValue(tag.Code, out int index) && index == i)
                { if (values.TryGetValue(tag.Code, out DxfTag value)) this.WriteDatabaseTag(value, false); }
                else this.WriteDatabaseTag(tag, false);
            }
            if (polyline.StoredHeaderPublicEnd == polyline.StoredHeaderTags.Count)
                foreach (var pair in values) if (!polyline.StoredHeaderIndices.ContainsKey(pair.Key)) this.WriteDatabaseTag(pair.Value, false);
            this.WriteXData(polyline.XData);
        }
        private void WriteStoredPolyline2DRecord(Polyline2DRecord record)
        {
            this.chunk.Write(0, record.CodeName);
            var geometry = record.GeometryTags();
            bool extensionWritten = false, reactorsWritten = false;
            for (int i = 0; i < record.XDataStart; i++)
            {
                if (i == record.CommonEnd)
                {
                    if (!extensionWritten && record.ExtensionDictionary != null) this.WritePolyline2DExtension(record);
                    if (!reactorsWritten && record.PersistentReactors.Count > 0) this.WritePolyline2DReactors(record);
                }
                if (i == record.GeometryEnd)
                    foreach (var pair in geometry) if (!record.GeometryIndices.ContainsKey(pair.Key)) this.WriteDatabaseTag(pair.Value, false);
                DxfTag tag = record.Tags[i];
                if (record.MetadataGroups.TryGetValue(i, out int last))
                {
                    bool extension = (string)tag.Value == "{ACAD_XDICTIONARY";
                    bool unchanged = extension ? ReferenceEquals(record.ExtensionDictionary, record.OriginalExtension)
                        : record.PersistentReactors.SequenceEqual(record.OriginalReactors)
                            && record.ReactorHandles.Where(handle => handle != "0").SequenceEqual(record.PersistentReactors.Select(target => target.Handle));
                    if (unchanged) for (int at = i; at <= last; at++) this.WriteDatabaseTag(record.Tags[at], false);
                    else if (extension) this.WritePolyline2DExtension(record);
                    else this.WritePolyline2DReactors(record);
                    if (extension) extensionWritten = true; else reactorsWritten = true;
                    i = last; continue;
                }
                if (i == record.IdentityIndex) this.chunk.Write(5, record.Handle);
                else if (i == record.OwnerIndex) this.chunk.Write(330, record.StoredOwner.Handle);
                else if (record.GeometryIndices.TryGetValue(tag.Code, out int coordinate) && coordinate == i)
                { if (geometry.TryGetValue(tag.Code, out DxfTag value)) this.WriteDatabaseTag(value, false); }
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
            if (record.GeometryEnd == record.XDataStart)
                foreach (var pair in geometry) if (!record.GeometryIndices.ContainsKey(pair.Key)) this.WriteDatabaseTag(pair.Value, false);
            this.WriteXData(record.XData);
        }
        private void WritePolyline2DExtension(Polyline2DRecord record)
        {
            if (record.ExtensionDictionary == null) return;
            this.chunk.Write(102, "{ACAD_XDICTIONARY"); this.chunk.Write(360, record.ExtensionDictionary.Handle); this.chunk.Write(102, "}");
        }
        private void WritePolyline2DReactors(Polyline2DRecord record)
        {
            if (record.PersistentReactors.Count == 0) return;
            this.chunk.Write(102, "{ACAD_REACTORS");
            foreach (DxfObject target in record.PersistentReactors) this.chunk.Write(330, target.Handle);
            this.chunk.Write(102, "}");
        }
    }
}
