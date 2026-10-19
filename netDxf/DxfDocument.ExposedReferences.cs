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
        // Shared by managed records and INSERT attributes; these targets are not
        // ordinary database-table entries with subscription-based reference lists.
        internal List<DxfObjectReference> ExposedObjectReferences(DxfObject target)
        {
            var result = new List<DxfObjectReference>();
            ulong targetHandle = ulong.Parse(target.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            foreach (DxfObject item in this.RetainedMetadataObjects())
            {
                if (ReferenceEquals(item, target)) continue;
                int uses = ReferenceEquals(item.Owner, target) ? 1 : 0;
                if (ReferenceEquals(item.ExtensionDictionary, target)) uses++;
                uses += item.PersistentReactors.Count(r => ReferenceEquals(r, target));
                if (item is EntityObject entity) uses += entity.Reactors.Count(r => ReferenceEquals(r, target));
                if (item is DxfDatabaseObject database) uses += database.DatabaseReferences.Count(r => ReferenceEquals(r, target));
                if (item is DxfDictionary dictionary) uses += dictionary.Entries.Count(e => ReferenceEquals(e.Target, target));
                if (item is DxfDictionaryWithDefault fallback && ReferenceEquals(fallback.Default, target)) uses++;
                IEnumerable<DxfTag> tags = item is DxfXRecord xrecord ? xrecord.Data
                    : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                uses += CountExposedHandles(tags, targetHandle);
                foreach (XData data in item.XData.Values)
                    foreach (XDataRecord tag in data.XDataRecord)
                        if (tag.Code == XDataCode.DatabaseHandle && ExposedHandleMatches(tag.Value, targetHandle)) uses++;
                if (item is Polyline3DRecord polylineRecord)
                {
                    uses += polylineRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountExposedHandles(polylineRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolygonMeshRecord meshRecord)
                {
                    uses += meshRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountExposedHandles(meshRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolyfaceMeshRecord polyfaceRecord)
                {
                    uses += polyfaceRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountExposedHandles(polyfaceRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is PolyfaceMesh polyface) uses += CountExposedHandles(polyface.StoredHeaderReferences, targetHandle);
                if (item is Polyline2DRecord legacyRecord)
                {
                    uses += legacyRecord.References.Count(r => ReferenceEquals(r, target));
                    uses += CountExposedHandles(legacyRecord.OpaqueHandleTags, targetHandle);
                }
                if (item is Polyline2D legacy) uses += CountExposedHandles(legacy.StoredHeaderReferences, targetHandle);
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
                if (ExposedHandleMatches(variable.Value, targetHandle)) headerUses++;
            }
            if (headerUses != 0) result.Add(new DxfObjectReference(this, headerUses));
            return result;
        }

        private static int CountExposedHandles(IEnumerable<DxfTag> tags, ulong target)
        {
            int count = 0;
            foreach (DxfTag tag in tags)
                if (DxfObjectDatabase.IsReference(tag) && ExposedHandleMatches(tag.Value, target)) count++;
            return count;
        }

        private static bool ExposedHandleMatches(object value, ulong target)
        {
            if (!(value is string text) || text.Length == 0 || text.Length > 16
                || !ulong.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong handle))
                throw new InvalidOperationException("An exposed object reference has an invalid handle value.");
            return handle != 0 && handle == target;
        }

    }
}
