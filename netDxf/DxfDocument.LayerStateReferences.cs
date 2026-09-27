// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // LayerState is managed, not a DxfDatabaseObject. Report current exposed
        // incoming references rather than stale resource-subscription counts.
        // Uses counts represented reference slots, not hidden application semantics.
        internal List<DxfObjectReference> LayerStateReferences(LayerState target)
        {
            var result = new List<DxfObjectReference>();
            if (target == null || !ReferenceEquals(target.Owner, this.Layers.StateManager)
                || !ReferenceEquals(this.Layers.StateManager[target.Name], target)) return result;
            ulong targetHandle = ulong.Parse(target.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (ReferenceEquals(item, target)) continue;
                int uses = ReferenceEquals(item.Owner, target) ? 1 : 0;
                uses += item.PersistentReactors.Count(r => ReferenceEquals(r, target));
                if (item is EntityObject entity) uses += entity.Reactors.Count(r => ReferenceEquals(r, target));
                if (item is DxfDatabaseObject database) uses += database.DatabaseReferences.Count(r => ReferenceEquals(r, target));
                if (item is DxfDictionary dictionary) uses += dictionary.Entries.Count(e => ReferenceEquals(e.Target, target));
                if (item is DxfDictionaryWithDefault fallback && ReferenceEquals(fallback.Default, target)) uses++;
                IEnumerable<DxfTag> tags = item is DxfXRecord xrecord ? xrecord.Data
                    : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                uses += CountLayerStateHandles(tags, targetHandle);
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && LayerStateHandleMatches(tag.Value, targetHandle)) uses++;
                if (item is Polyline3DRecord polylineRecord)
                {
                    uses += polylineRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountLayerStateHandles(polylineRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolygonMeshRecord meshRecord)
                {
                    uses += meshRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountLayerStateHandles(meshRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolyfaceMeshRecord polyfaceRecord)
                {
                    uses += polyfaceRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountLayerStateHandles(polyfaceRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolyfaceMesh polyface) uses += CountLayerStateHandles(polyface.StoredHeaderReferences, targetHandle);
                if (item is Polyline2DRecord legacyRecord)
                {
                    uses += legacyRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountLayerStateHandles(legacyRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is Polyline2D legacy) uses += CountLayerStateHandles(legacy.StoredHeaderReferences, targetHandle);
                if (item is DxfOpaqueEntity opaqueEntity) uses += opaqueEntity.References.Count(r => ReferenceEquals(r, target));
                if (item is StoredTable table) uses += table.References.Count(r => ReferenceEquals(r, target));
                if (item is MultiLeader leader)
                    foreach (MLeaderData data in leader.Data) uses += data.References.Count(r => ReferenceEquals(r, target));
                if (uses != 0) result.Add(new DxfObjectReference(item, uses));
            }
            int headerUses = 0;
            foreach (var variable in this.DrawingVariables.CustomValues())
            {
                DxfHandleKind kind = DxfGroupCode.GetHandleKind(variable.GroupCode);
                if (kind == DxfHandleKind.None || kind == DxfHandleKind.Arbitrary
                    || variable.Name.Equals("$HANDSEED", StringComparison.OrdinalIgnoreCase)) continue;
                if (LayerStateHandleMatches(variable.Value, targetHandle)) headerUses++;
            }
            if (headerUses != 0) result.Add(new DxfObjectReference(this, headerUses));
            return result;
        }

        private static int CountLayerStateHandles(IEnumerable<DxfTag> tags, ulong target)
        {
            int count = 0;
            foreach (DxfTag tag in tags)
                if (DxfObjectDatabase.IsReference(tag) && LayerStateHandleMatches(tag.Value, target)) count++;
            return count;
        }

        private static bool LayerStateHandleMatches(object value, ulong target)
        {
            if (!(value is string text) || text.Length == 0 || text.Length > 16
                || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong handle))
                throw new InvalidOperationException("An exposed layer-state reference has an invalid handle value.");
            return handle != 0 && handle == target;
        }

        internal bool LayerStateReferencesRemoval(LayerState target)
        {
            // Keep conservative private/owned-subtree guards in addition to the
            // public direct-reference inventory. Removal is not recursive erasure.
            return target.ExtensionDictionary != null || this.StoredTableReferencesRemoval(target)
                || this.LayerStateReferences(target).Count != 0;
        }
    }
}
