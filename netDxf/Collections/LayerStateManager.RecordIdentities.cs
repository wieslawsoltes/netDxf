// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        internal void ValidateLoadedStateIdentities(IReadOnlyList<KeyValuePair<LayerState, string>> states,
            string outer, string inner)
        {
            var identities = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { CanonicalDictionaryHandle(outer), CanonicalDictionaryHandle(inner), this.Owner.Layers.Handle };
            if (identities.Count != 3) throw new FormatException("Layer dictionary identities overlap.");
            foreach (var pair in states)
            {
                string handle = CanonicalDictionaryHandle(pair.Value);
                if (!identities.Add(handle) || !ReferenceEquals(pair.Key.Owner, this)
                    || !ReferenceEquals(this.Owner.GetObjectByHandle(pair.Key.Handle), pair.Key))
                    throw new FormatException("Layer-state identity has conflicting ownership or registration.");
                var occupied = this.Owner.GetObjectByHandle(handle);
                if (occupied != null && !ReferenceEquals(occupied, pair.Key))
                    throw new FormatException("Layer-state source identity is already occupied: " + handle);
            }
        }

        internal void RestoreLoadedStateIdentities(IReadOnlyList<KeyValuePair<LayerState, string>> states)
        {
            // Hydration-only, after all source and registration checks. The whole candidate
            // document is discarded on failure; no caller-owned source document is modified.
            foreach (var pair in states) this.Owner.AddedObjects.Remove(pair.Key.Handle);
            foreach (var pair in states)
            {
                pair.Key.Handle = CanonicalDictionaryHandle(pair.Value);
                this.Owner.AddedObjects.Add(pair.Key.Handle, pair.Key);
            }
        }

        private bool HasIdentityReferences(LayerState target)
        {
            // Layer states previously lost identity on reload. Retained incoming references
            // now need the same conservative removal protection as other database targets.
            if (target.ExtensionDictionary != null) return true;
            foreach (var item in this.Owner.AddedObjects.Values.Concat(
                this.Owner.AddedObjects.Values.OfType<netDxf.Entities.Insert>().SelectMany(i => i.Attributes).Cast<DxfObject>()))
            {
                if (ReferenceEquals(item.Owner, target)) return true;
                IEnumerable<DxfTag> tags = item is DxfXRecord record ? record.Data
                    : item is DxfOpaqueObject opaque ? opaque.Tags : Enumerable.Empty<DxfTag>();
                foreach (var tag in tags)
                    if (DxfObjectDatabase.IsReference(tag)
                        && ReferenceEquals(this.Owner.GetObjectByHandle((string)tag.Value), target)) return true;
                if (item.PersistentReactors.Any(r => ReferenceEquals(r, target))) return true;
                if (item is DxfDatabaseObject database && database.DatabaseReferences.Any(r => ReferenceEquals(r, target))) return true;
                foreach (var data in item.XData.Values)
                    foreach (var value in data.XDataRecord)
                        if (value.Code == XDataCode.DatabaseHandle
                            && ReferenceEquals(this.Owner.GetObjectByHandle((string)value.Value), target)) return true;
            }
            return false;
        }
    }
}
