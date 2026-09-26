// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using netDxf.Objects;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        // The public manager has always represented the LAYER extension wrapper.
        // Retain its child as a registered identity too, rather than allocating at every save.
        private DictionaryObject statesDictionary;
        private bool statesEntryHardOwner = true;
        private readonly Dictionary<string, bool> stateEntryHardOwners = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DictionaryCloningFlags> stateCloning = new Dictionary<string, DictionaryCloningFlags>(StringComparer.OrdinalIgnoreCase);
        private bool dictionaryHardOwner = true;
        private DictionaryCloningFlags dictionaryCloning = DictionaryCloningFlags.KeepExisting;
        internal bool HasLoadedDictionaryIdentity { get; private set; }
        internal DictionaryObject StoredStatesDictionary { get { return this.statesDictionary; } }

        internal void RestoreDictionaryIdentity(DictionaryObject wrapper, DictionaryObject states)
        {
            if (this.HasLoadedDictionaryIdentity) throw new InvalidOperationException("Layer-state identity is already initialized.");
            this.CheckLoadedHandle(wrapper.Handle, this);
            if (states != null)
            {
                this.CheckLoadedHandle(states.Handle, null);
                if (string.Equals(wrapper.Handle, states.Handle, StringComparison.OrdinalIgnoreCase))
                    throw new FormatException("A layer-state dictionary cannot own itself.");
            }
            this.Owner.AddedObjects.Remove(this.Handle);
            this.Handle = wrapper.Handle;
            this.Owner.AddedObjects.Add(this.Handle, this);
            this.dictionaryHardOwner = wrapper.IsHardOwner;
            this.dictionaryCloning = wrapper.Cloning;
            if (states != null)
            {
                this.statesDictionary = new DictionaryObject(this) { Handle = states.Handle,
                    IsHardOwner = states.IsHardOwner, Cloning = states.Cloning };
                this.Owner.AddedObjects.Add(states.Handle, this.statesDictionary);
            }
            // The reader attaches dictionary XData in its existing collection pass.
            this.HasLoadedDictionaryIdentity = true;
        }

        private void CheckLoadedHandle(string handle, DxfObject permitted)
        {
            if (!long.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long number)
                || number <= 0 || number == long.MaxValue)
                throw new FormatException("Invalid layer-state dictionary handle.");
            DxfObject existing = this.Owner.GetObjectByHandle(handle);
            if (existing != null && !ReferenceEquals(existing, permitted))
                throw new FormatException("Layer-state dictionary handle conflicts with another object: " + handle);
            if (number >= this.Owner.NumHandles) this.Owner.NumHandles = number + 1;
        }

        internal void RestoreEntryOwnership(bool hardOwner, IEnumerable<KeyValuePair<string, bool>> entries)
        {
            this.statesEntryHardOwner = hardOwner;
            foreach (var entry in entries) this.stateEntryHardOwners.Add(entry.Key, entry.Value);
        }

        internal void RestoreStateCloning(string handle, DictionaryCloningFlags flags)
        { this.stateCloning.Add(handle, flags); }

        internal DictionaryCloningFlags StateCloning(LayerState state)
        { return this.stateCloning.TryGetValue(state.Handle, out DictionaryCloningFlags flags) ? flags : DictionaryCloningFlags.KeepExisting; }

        internal DictionaryObject PrepareDictionaries(List<DictionaryObject> dictionaries)
        {
            if (this.statesDictionary == null)
            {
                long next = this.Owner.NumHandles;
                while (next > 0 && next < long.MaxValue && this.Owner.GetObjectByHandle(next.ToString("X", CultureInfo.InvariantCulture)) != null) next++;
                if (next <= 0 || next == long.MaxValue) throw new InvalidOperationException("The document handle range is exhausted.");
                var created = new DictionaryObject(this);
                this.Owner.NumHandles = created.AssignHandle(next);
                this.Owner.AddedObjects.Add(created.Handle, created);
                this.statesDictionary = created;
            }
            if (!ReferenceEquals(this.Owner.GetObjectByHandle(this.Handle), this)
                || !ReferenceEquals(this.Owner.GetObjectByHandle(this.statesDictionary.Handle), this.statesDictionary))
                throw new InvalidOperationException("Layer-state dictionaries must retain their registered identities.");
            var wrapper = new DictionaryObject(this.Owner.Layers) { Handle = this.Handle,
                IsHardOwner = this.dictionaryHardOwner, Cloning = this.dictionaryCloning };
            wrapper.XData.AddRange(this.XData.Values);
            wrapper.Entries.Add(this.statesDictionary.Handle, DxfObjectCode.LayerStates);
            wrapper.EntryOwnership.Add(this.statesDictionary.Handle, this.statesEntryHardOwner);
            this.statesDictionary.Entries.Clear();
            this.statesDictionary.EntryOwnership.Clear();
            foreach (LayerState state in this.Items)
            {
                this.statesDictionary.Entries.Add(state.Handle, state.Name);
                if (this.stateEntryHardOwners.TryGetValue(state.Handle, out bool hardOwner))
                    this.statesDictionary.EntryOwnership.Add(state.Handle, hardOwner);
            }
            dictionaries.Add(wrapper);
            dictionaries.Add(this.statesDictionary);
            return this.statesDictionary;
        }
    }
}
