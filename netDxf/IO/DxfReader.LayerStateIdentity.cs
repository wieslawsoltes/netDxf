// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Linq;
using System.Collections.Generic;
using netDxf.Objects;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private Dictionary<string, DatabaseRecord> layerStateSourceIndex;
        private readonly List<KeyValuePair<DatabaseRecord, string>> layerStateOwnerChecks = new List<KeyValuePair<DatabaseRecord, string>>();

        private DatabaseRecord LayerStateSource(string handle)
        {
            if (this.layerStateSourceIndex == null)
            {
                this.layerStateSourceIndex = new Dictionary<string, DatabaseRecord>(StringComparer.OrdinalIgnoreCase);
                foreach (DatabaseRecord record in this.databaseRecords)
                {
                    string key = record.Object.Handle;
                    if (string.IsNullOrEmpty(key)) continue;
                    // Duplicate declarations are invalid source targets, never first/last-wins.
                    if (this.layerStateSourceIndex.ContainsKey(key)) this.layerStateSourceIndex[key] = null;
                    else this.layerStateSourceIndex.Add(key, record);
                }
            }
            if (!this.layerStateSourceIndex.TryGetValue(handle, out DatabaseRecord result) || result == null)
                throw new FormatException("Missing or repeated layer-state source record: " + handle);
            return result;
        }

        private DictionaryObject RestoreLayerStateDictionaries(DictionaryObject wrapper)
        {
            // Do not guess the first extension entry is a layer-state dictionary or
            // silently discard siblings that the legacy projection cannot represent.
            var outer = this.LayerStateSource(wrapper.Handle);
            this.CheckLayerStateSource(outer, this.doc.Layers.Handle);
            if (!(outer.Object is DxfDictionary) || outer.Object is DxfDictionaryWithDefault)
                throw new NotSupportedException("Unsupported LAYER extension dictionary type.");
            DictionaryObject states = null;
            if (outer.Entries.Count != wrapper.Entries.Count)
                throw new FormatException("Aliased LAYER extension entries cannot be projected losslessly.");
            if (wrapper.Entries.Count != 0)
            {
                if (wrapper.Entries.Count != 1 || !string.Equals(wrapper.Entries.Single().Value, DxfObjectCode.LayerStates, StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException("The LAYER extension has entries outside the supported layer-state projection; use raw preservation.");
                string handle = wrapper.Entries.Single().Key;
                if (!this.dictionaries.TryGetValue(handle, out states))
                    throw new FormatException("The layer-state entry does not refer to a dictionary.");
                var inner = this.LayerStateSource(handle);
                this.CheckLayerStateSource(inner, wrapper.Handle);
                if (!(inner.Object is DxfDictionary) || inner.Object is DxfDictionaryWithDefault)
                    throw new NotSupportedException("Unsupported layer-state dictionary type.");
                if (inner.Entries.Count != states.Entries.Count
                    || inner.Entries.Select(e => e.Item1).Distinct(StringComparer.OrdinalIgnoreCase).Count() != inner.Entries.Count)
                    throw new FormatException("Aliased or repeated layer-state entries cannot be projected losslessly.");
                foreach (var entry in states.Entries)
                {
                    if (!this.xRecords.TryGetValue(entry.Key, out XRecord record))
                        throw new FormatException("A layer-state entry does not refer to a supported XRECORD.");
                    this.CheckLayerStateSource(this.LayerStateSource(record.Handle), states.Handle);
                }
            }
            this.doc.Layers.StateManager.RestoreDictionaryIdentity(wrapper, states);
            if (states != null)
            {
                this.doc.Layers.StateManager.RestoreEntryOwnership(outer.Entries[0].Item3,
                    this.LayerStateSource(states.Handle).Entries.Select(e => new KeyValuePair<string, bool>(e.Item2, e.Item3)));
            }
            return states;
        }

        private void CheckLayerStateSource(DatabaseRecord record, string owner)
        {
            if (record.SourceIdentity == null || record.SourceIdentity.Ambiguous || !record.SourceIdentity.IdentitySeen)
                throw new FormatException("Ambiguous or missing layer-state source identity.");
            this.layerStateOwnerChecks.Add(new KeyValuePair<DatabaseRecord, string>(record, owner));
        }

        private void ValidateLayerStateOwners()
        {
            // Resolve existing source-bound consumers first. Missing TABLE identities
            // must retain their original dangling-reference diagnostics, not be masked
            // by a consequent generated-parent mismatch. No partially loaded document
            // is exposed: every explicit owner still validates before Read returns.
            foreach (var check in this.layerStateOwnerChecks)
                if (!string.Equals(check.Key.Metadata.Owner, check.Value, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("Layer-state structural owner mismatch.");
        }

        private DxfObject ManagedLayerStateObject(string handle)
        {
            var manager = this.doc.Layers.StateManager;
            if (!manager.HasLoadedDictionaryIdentity) return null;
            if (string.Equals(handle, manager.Handle, StringComparison.OrdinalIgnoreCase)) return manager;
            if (manager.StoredStatesDictionary != null && string.Equals(handle, manager.StoredStatesDictionary.Handle, StringComparison.OrdinalIgnoreCase))
                return manager.StoredStatesDictionary;
            DxfObject item = this.doc.GetObjectByHandle(handle);
            return item is LayerState state && ReferenceEquals(state.Owner, manager) ? item : null;
        }
    }
}
