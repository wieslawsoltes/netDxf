// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;
namespace netDxf
{
    public sealed partial class DxfDocument
    {
        private bool OpaqueEntityReferencesRemoval(HashSet<DxfObject> removed)
        {
            foreach (DxfOpaqueEntity entity in this.AddedObjects.Values.OfType<DxfOpaqueEntity>())
                if (!removed.Contains(entity) && entity.ReferencesRemoval(removed)) return true;
            if (!removed.Any(item => item is DxfOpaqueEntity)) return false;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (removed.Contains(item)) continue;
                if (item.Owner != null && removed.Contains(item.Owner) || item.ExtensionDictionary != null && removed.Contains(item.ExtensionDictionary)) return true;
                if (item.PersistentReactors.Any(removed.Contains)) return true;
                if (item is EntityObject entity && entity.Reactors.Any(removed.Contains)) return true;
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && this.RemovedOpaqueHandle((string)tag.Value, removed)) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(removed.Contains)) return true;
                if (item is DxfDictionary dictionary && dictionary.Entries.Any(entry => removed.Contains(entry.Target))) return true;
                if (item is DxfDictionaryWithDefault fallback && fallback.Default != null && removed.Contains(fallback.Default)) return true;
                IEnumerable<DxfTag> tags = item is DxfXRecord record ? record.Data : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                foreach (DxfTag tag in tags)
                    if (DxfObjectDatabase.IsReference(tag) && this.RemovedOpaqueHandle((string)tag.Value, removed)) return true;
            }
            foreach (Header.HeaderVariable variable in this.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary && variable.Value is string handle && this.RemovedOpaqueHandle(handle, removed)) return true;
            }
            return false;
        }
        private bool RemovedOpaqueHandle(string handle, HashSet<DxfObject> removed)
        { return removed.Contains(this.StoredTableHandleTarget(DxfOpaqueEntity.CanonicalHandle(handle))); }
    }
}
