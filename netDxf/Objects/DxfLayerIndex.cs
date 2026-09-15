using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Objects
{
    /// <summary>One stored layer name and its hard-owned IDBUFFER in a LAYER_INDEX.</summary>
    /// <remarks>The name is stored text, not a reference to the layer table. Buffer counts are derived from the current IDBUFFER sequence.</remarks>
    public sealed class DxfLayerIndexEntry
    {
        /// <summary>Creates an entry for a nonempty stored layer name and an IDBUFFER.</summary>
        public DxfLayerIndexEntry(string layerName, DxfIdBuffer buffer)
        {
            if (string.IsNullOrEmpty(layerName) || layerName.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException("A layer-index name must be nonempty single-line Unicode text.", nameof(layerName));
            for (int i = 0; i < layerName.Length; i++)
            {
                if (char.IsHighSurrogate(layerName[i]))
                {
                    if (i + 1 >= layerName.Length || !char.IsLowSurrogate(layerName[++i]))
                        throw new ArgumentException("A layer-index name contains an unpaired surrogate.", nameof(layerName));
                }
                else if (char.IsLowSurrogate(layerName[i]))
                    throw new ArgumentException("A layer-index name contains an unpaired surrogate.", nameof(layerName));
            }
            this.LayerName = layerName;
            this.Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        }
        /// <summary>Gets the exact stored name, including case and unresolved names.</summary>
        public string LayerName { get; }
        /// <summary>Gets the IDBUFFER hard-owned by this entry's index.</summary>
        public DxfIdBuffer Buffer { get; }
        /// <summary>Gets the group-90 count derived from all buffer entries, including duplicates and nulls.</summary>
        public int Count { get { return this.Buffer.References.Count; } }
    }

    /// <summary>A stored LAYER_INDEX timestamp and ordered layer-name/IDBUFFER entries.</summary>
    /// <remarks>This object preserves a stored index graph; it does not build, update, or evaluate a layer index. Layer-table renaming does not rewrite stored names.</remarks>
    public sealed class DxfLayerIndex : DxfDatabaseObject
    {
        private double timestamp;
        private List<DxfLayerIndexEntry> entries = new List<DxfLayerIndexEntry>();
        /// <summary>Creates a detached index with no entries and a zero timestamp.</summary>
        public DxfLayerIndex() : base("LAYER_INDEX") { }
        /// <summary>Gets or sets the finite group-40 Julian-date value without calendar conversion.</summary>
        public double Timestamp
        {
            get { return this.timestamp; }
            set
            {
                if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(nameof(value), "The stored timestamp must be finite.");
                this.timestamp = value;
            }
        }
        /// <summary>Gets the ordered entries; duplicate names are retained.</summary>
        public IReadOnlyList<DxfLayerIndexEntry> Entries { get { return this.entries.AsReadOnly(); } }
        /// <summary>Atomically replaces entries and establishes reciprocal ownership of their IDBUFFER objects.</summary>
        /// <remarks>Detached indexes can adopt unowned detached buffers. Registered indexes can rename or reorder entries using the same owned buffer set; adding or orphaning registered children is rejected. Buffer reference lists remain editable.</remarks>
        public void SetEntries(IEnumerable<DxfLayerIndexEntry> values) { this.SetEntries(values, false); }
        internal void LoadEntries(IEnumerable<DxfLayerIndexEntry> values) { this.SetEntries(values, true); }
        private void SetEntries(IEnumerable<DxfLayerIndexEntry> values, bool loading)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var snapshot = values.ToList();
            if (this.IsErased) throw new InvalidOperationException("An erased index cannot adopt objects.");
            var children = new HashSet<DxfIdBuffer>();
            foreach (DxfLayerIndexEntry entry in snapshot)
            {
                if (entry == null) throw new ArgumentException("A layer-index entry cannot be null.", nameof(values));
                DxfIdBuffer child = entry.Buffer;
                if (!children.Add(child)) throw new ArgumentException("Each layer-index entry must own a distinct IDBUFFER.", nameof(values));
                if (child.IsErased) throw new InvalidOperationException("An erased buffer cannot be attached again.");
                if (child.Owner != null && !ReferenceEquals(child.Owner, this)) throw new ArgumentException("An IDBUFFER already has another owner.", nameof(values));
                if (child.Database != this.Database) throw new ArgumentException("An index and its buffers must share their registration state and database.", nameof(values));
                if (DxfObjectDatabase.IsAncestor(child, this)) throw new ArgumentException("Layer-index ownership cannot form a cycle.", nameof(values));
                if (this.Database != null)
                {
                    this.Database.CheckRegistered(this); this.Database.CheckRegistered(child);
                    if (!ReferenceEquals(child.Owner, this)) throw new ArgumentException("A registered buffer must already be owned by its index.", nameof(values));
                }
            }
            if (this.Database != null && !loading && !children.SetEquals(this.entries.Select(entry => entry.Buffer)))
                throw new InvalidOperationException("Replacing registered entries must preserve their complete owned buffer set.");
            foreach (DxfLayerIndexEntry entry in this.entries)
                if (!children.Contains(entry.Buffer)) entry.Buffer.Owner = null;
            foreach (DxfIdBuffer child in children) child.Owner = this;
            this.entries = snapshot;
        }
        internal override IEnumerable<DxfDatabaseObject> DeclaredOwnedObjects { get { return this.entries.Select(entry => entry.Buffer); } }
        internal override IEnumerable<DxfObject> DatabaseReferences { get { return this.entries.Select(entry => entry.Buffer); } }
        internal override DxfDatabaseObject CloneShell() { return new DxfLayerIndex { Timestamp = this.Timestamp }; }
        internal override void CopyDatabaseReferencesTo(DxfDatabaseObject clone, Func<DxfObject, DxfObject> resolve)
        {
            ((DxfLayerIndex)clone).SetEntries(this.entries.Select(entry => new DxfLayerIndexEntry(entry.LayerName, (DxfIdBuffer)resolve(entry.Buffer))));
        }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (this.Database != null && !(this.Owner is DxfDictionary)) errors.Add("LAYER_INDEX requires a dictionary owner.");
            if (this.entries.Any(entry => !ReferenceEquals(entry.Buffer.Owner, this))) errors.Add("LAYER_INDEX ownership is not reciprocal.");
            if (this.entries.Select(entry => entry.Buffer).Distinct().Count() != this.entries.Count) errors.Add("LAYER_INDEX repeats an owned IDBUFFER.");
        }
    }
}
