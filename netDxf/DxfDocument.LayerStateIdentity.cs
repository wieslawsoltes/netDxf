// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // Legacy collection removal cannot cascade a database-owned subtree or leave
        // exposed incoming handles dangling. Private embedded strings/binary data are
        // not interpreted, matching the existing guarded database-erasure boundary.
        internal bool LayerStateReferencesRemoval(LayerState state)
        {
            if (state.ExtensionDictionary != null || this.StoredTableReferencesRemoval(state)) return true;
            var removed = new HashSet<DxfObject>(new MetadataIdentityComparer()) { state };
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (ReferenceEquals(item, state)) continue;
                if (ReferenceEquals(item.Owner, state) || ReferenceEquals(item.ExtensionDictionary, state)) return true;
                if (item.PersistentReactors.Any(r => ReferenceEquals(r, state))) return true;
                if (item is EntityObject entity && entity.Reactors.Any(r => ReferenceEquals(r, state))) return true;
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && this.RemovedPolylineHandle((string)tag.Value, removed)) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(r => ReferenceEquals(r, state))) return true;
                if (item is DxfDictionary dictionary && dictionary.Entries.Any(e => ReferenceEquals(e.Target, state))) return true;
                if (item is DxfDictionaryWithDefault fallback && ReferenceEquals(fallback.Default, state)) return true;
                if (item is DxfXRecord record)
                    foreach (DxfTag tag in record.Data)
                        if (DxfObjectDatabase.IsReference(tag) && this.RemovedPolylineHandle((string)tag.Value, removed)) return true;
                if (item is DxfOpaqueObject opaque)
                    foreach (DxfTag tag in opaque.Tags)
                        if (tag.ValueType == DxfTagValueType.Handle && this.RemovedPolylineHandle((string)tag.Value, removed)) return true;
                if (item is MultiLeader leader)
                    foreach (MLeaderData data in leader.Data)
                        if (data.References.Any(r => ReferenceEquals(r, state))) return true;
            }
            foreach (HeaderVariable variable in this.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind != DxfHandleKind.None && kind != DxfHandleKind.Arbitrary
                    && variable.Value is string handle && this.RemovedPolylineHandle(handle, removed)) return true;
            }
            return false;
        }
    }
}
