using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private void RegisterStoredPolylineRecords(Polyline3D polyline)
        {
            if (polyline == null || !polyline.HasStoredRecords) return;
            foreach (Polyline3DRecord record in polyline.StoredRecords)
            {
                foreach (int key in record.Resources.Keys.ToList())
                {
                    if (record.Resources[key] is Layer layer) record.Resources[key] = this.Layers.Add(layer);
                    else if (record.Resources[key] is Linetype linetype) record.Resources[key] = this.Linetypes.Add(linetype);
                }
                if (record.Handle == null) this.NumHandles = record.AssignHandle(this.NumHandles);
                record.SourceDocument = this;
                this.AddedObjects.Add(record.Handle, record);
            }
            polyline.BindStoredRecordDocument(this);
        }
        private void UnregisterStoredPolylineRecords(Polyline3D polyline)
        {
            if (polyline == null) return;
            foreach (Polyline3DRecord record in polyline.StoredRecords) this.AddedObjects.Remove(record.Handle);
        }
        private bool StoredPolylineReferencesRemoval(HashSet<DxfObject> removed)
        {
            bool removesRecords = removed.OfType<Polyline3DRecord>().Any();
            foreach (Polyline3DRecord record in removed.OfType<Polyline3DRecord>())
                if (record.HasPrivateData || record.ExtensionDictionary != null) return true;
            // Existing collection moves allocate a new parent handle. Object references
            // can follow that identity, but raw XData handle text needs an explicit map.
            var parentHandles = new HashSet<DxfObject>(removed.OfType<Polyline3D>().Where(polyline => polyline.HasStoredRecords));
            foreach (DxfObject item in removed)
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && this.RemovedPolylineHandle((string)tag.Value, parentHandles)) return true;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (removed.Contains(item)) continue;
                if (item is Polyline3DRecord record && (record.References.Any(removed.Contains)
                    || record.OpaqueHandleTags.Any(tag => this.RemovedPolylineHandle((string)tag.Value, removed)))) return true;
                if (!removesRecords) continue;
                if (item.Owner != null && removed.Contains(item.Owner) || item.ExtensionDictionary != null && removed.Contains(item.ExtensionDictionary)) return true;
                if (item.PersistentReactors.Any(removed.Contains)) return true;
                if (item is EntityObject entity && entity.Reactors.Any(removed.Contains)) return true;
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && this.RemovedPolylineHandle((string)tag.Value, removed)) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(removed.Contains)) return true;
                if (item is DxfDictionary dictionary && dictionary.Entries.Any(entry => removed.Contains(entry.Target))) return true;
                if (item is DxfDictionaryWithDefault fallback && fallback.Default != null && removed.Contains(fallback.Default)) return true;
                IEnumerable<DxfTag> tags = item is DxfXRecord xrecord ? xrecord.Data : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                foreach (DxfTag tag in tags)
                        if (DxfObjectDatabase.IsReference(tag) && this.RemovedPolylineHandle((string)tag.Value, removed)) return true;
            }
            if (removesRecords)
                foreach (HeaderVariable variable in this.DrawingVariables.CustomValues())
                {
                    DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                    if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary
                        && variable.Value is string handle && this.RemovedPolylineHandle(handle, removed)) return true;
                }
            return false;
        }
        private bool RemovedPolylineHandle(string handle, HashSet<DxfObject> removed)
        {
            if (!ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value) || value == 0) return false;
            DxfObject target = this.GetObjectByHandle(value.ToString("X", CultureInfo.InvariantCulture));
            return target != null && removed.Contains(target);
        }
    }
}
