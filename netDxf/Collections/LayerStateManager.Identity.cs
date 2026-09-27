// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;
using System.Collections.Generic;
using netDxf.Objects;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        // Registered separately from OBJECTS database items: the existing layer-state
        // writer emits this identity through its DictionaryObject projection exactly once.
        private sealed class StatesDictionaryIdentity : DxfObject
        {
            internal StatesDictionaryIdentity(LayerStateManager owner) : base(DxfObjectCode.Dictionary)
            { this.Owner = owner; }
        }
        private StatesDictionaryIdentity statesDictionaryIdentity;
        internal bool HasRetainedDictionaryIdentity { get; private set; }
        internal DxfObject StatesDictionaryObject { get { return this.statesDictionaryIdentity; } }

        internal long AssignDictionaryProjection(DictionaryObject projection, long next)
        {
            if (this.statesDictionaryIdentity == null)
            {
                if (next <= 0 || next == long.MaxValue)
                    throw new InvalidOperationException("The layer-state dictionary handle range is exhausted.");
                while (this.Owner.GetObjectByHandle(next.ToString("X", CultureInfo.InvariantCulture)) != null)
                {
                    if (++next == long.MaxValue) throw new InvalidOperationException("The layer-state dictionary handle range is exhausted.");
                }
                var identity = new StatesDictionaryIdentity(this);
                next = identity.AssignHandle(next);
                this.Owner.AddedObjects.Add(identity.Handle, identity);
                this.statesDictionaryIdentity = identity;
            }
            if (!ReferenceEquals(this.Owner.GetObjectByHandle(this.statesDictionaryIdentity.Handle), this.statesDictionaryIdentity)
                || !ReferenceEquals(this.statesDictionaryIdentity.Owner, this))
                throw new InvalidOperationException("The layer-state dictionary identity is not registered in its document.");
            // This managed identity is not a general-purpose dictionary editing API.
            // Refuse unsupported custom metadata rather than silently omitting it or
            // producing a noncanonical payload that this scoped reader cannot retain.
            if (this.statesDictionaryIdentity.XData.Count != 0
                || this.statesDictionaryIdentity.ExtensionDictionary != null
                || this.statesDictionaryIdentity.PersistentReactors.Count != 0)
                throw new NotSupportedException("Custom metadata on the managed layer-state dictionary is not supported.");
            projection.Handle = this.statesDictionaryIdentity.Handle;
            return next;
        }

        internal void RestoreDictionaryIdentities(string extensionHandle, string statesHandle)
        {
            // Called only after physical source-identity validation and exact supported-shape checks.
            string extension = CanonicalDictionaryHandle(extensionHandle);
            string states = CanonicalDictionaryHandle(statesHandle);
            if (extension == states || extension == this.Owner.Layers.Handle || states == this.Owner.Layers.Handle)
                throw new FormatException("Layer-state dictionary identities must be distinct from each other and the LAYER table.");
            DxfObject occupied = this.Owner.GetObjectByHandle(extension);
            if (occupied != null && !ReferenceEquals(occupied, this))
                throw new FormatException("Layer-state extension handle is already registered: " + extension);
            if (this.Owner.GetObjectByHandle(states) != null || this.statesDictionaryIdentity != null)
                throw new FormatException("Layer-state child handle is already registered: " + states);
            if (!ReferenceEquals(this.Owner.GetObjectByHandle(this.Handle), this))
                throw new FormatException("Layer-state collection has inconsistent registration.");
            var identity = new StatesDictionaryIdentity(this) { Handle = states };
            // The reader discards the whole candidate document on failure. No caller-owned
            // document or shared object is rebound by this hydration-only operation.
            if (this.Handle != extension)
            {
                this.Owner.AddedObjects.Remove(this.Handle);
                this.Handle = extension;
                this.Owner.AddedObjects.Add(extension, this);
            }
            this.Owner.AddedObjects.Add(states, identity);
            this.statesDictionaryIdentity = identity;
            this.HasRetainedDictionaryIdentity = true;
        }

        internal void ValidateLayerStateSourceIdentities(IReadOnlyList<KeyValuePair<LayerState, string>> states)
        {
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in states)
            {
                string handle = CanonicalDictionaryHandle(pair.Value);
                if (!handles.Add(handle) || !ReferenceEquals(pair.Key.Owner, this)
                    || !ReferenceEquals(this.Owner.GetObjectByHandle(pair.Key.Handle), pair.Key)
                    || this.Owner.GetObjectByHandle(handle) != null)
                    throw new FormatException("Layer-state source identity conflicts with its registered projection.");
            }
        }

        internal void RestoreLayerStateSourceIdentities(IReadOnlyList<KeyValuePair<LayerState, string>> states)
        {
            foreach (var pair in states)
            {
                this.Owner.AddedObjects.Remove(pair.Key.Handle);
                pair.Key.Handle = CanonicalDictionaryHandle(pair.Value);
                this.Owner.AddedObjects.Add(pair.Key.Handle, pair.Key);
            }
        }

        private static string CanonicalDictionaryHandle(string value)
        {
            if (value == null || value.Length == 0 || value.Length > 16
                || !long.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long number)
                || number <= 0 || number == long.MaxValue)
                throw new FormatException("Unsupported layer-state dictionary identity.");
            return number.ToString("X", CultureInfo.InvariantCulture);
        }
    }
}
