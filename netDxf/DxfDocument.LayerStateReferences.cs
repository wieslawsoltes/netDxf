// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // LayerState is a managed table object, not a DxfDatabaseObject. Its removal
        // must nevertheless respect incoming database and common-metadata references.
        internal bool LayerStateReferencesRemoval(LayerState target)
        {
            if (target.ExtensionDictionary != null || this.StoredTableReferencesRemoval(target)) return true;
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (ReferenceEquals(item, target)) continue;
                if (ReferenceEquals(item.Owner, target) || item.PersistentReactors.Any(r => ReferenceEquals(r, target))) return true;
                if (item is EntityObject entity && entity.Reactors.Any(r => ReferenceEquals(r, target))) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(r => ReferenceEquals(r, target))) return true;
                if (item is DxfDictionary dictionary && dictionary.Entries.Any(e => ReferenceEquals(e.Target, target))) return true;
                if (item is DxfDictionaryWithDefault fallback && ReferenceEquals(fallback.Default, target)) return true;
                IEnumerable<DxfTag> tags = item is DxfXRecord xrecord ? xrecord.Data
                    : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                foreach (DxfTag tag in tags)
                    if (DxfObjectDatabase.IsReference(tag) && ReferenceEquals(this.StoredTableHandleTarget((string)tag.Value), target)) return true;
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle
                            && ReferenceEquals(this.StoredTableHandleTarget((string)tag.Value), target)) return true;
            }
            return false;
        }
    }
}
