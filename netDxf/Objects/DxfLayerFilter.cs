using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace netDxf.Objects
{
    /// <summary>The published LAYER_FILTER envelope containing ordered layer-name strings.</summary>
    /// <remarks>Names are stored verbatim, including duplicates and unresolved names. Layer edits and graph cloning do not resolve or rename these strings. No filtering operation is evaluated.</remarks>
    public sealed class DxfLayerFilter : DxfDatabaseObject
    {
        private readonly LayerNameCollection layerNames = new LayerNameCollection();
        /// <summary>Creates a detached filter with no stored names.</summary>
        public DxfLayerFilter() : base("LAYER_FILTER") { }
        /// <summary>Creates a detached filter from an independent snapshot of the supplied names.</summary>
        public DxfLayerFilter(IEnumerable<string> names) : this()
        {
            if (names == null) throw new ArgumentNullException(nameof(names));
            foreach (string name in names) this.layerNames.Add(name);
        }
        /// <summary>Gets the ordered editable names. Entries are not references to the document's layer table.</summary>
        public Collection<string> LayerNames { get { return this.layerNames; } }
        internal override DxfDatabaseObject CloneShell() { return new DxfLayerFilter(this.layerNames); }
        internal override void ValidateDatabaseSchema(DxfObjectDatabase database, List<string> errors)
        {
            if (!(this.Owner is DxfDictionary)) errors.Add("LAYER_FILTER requires a dictionary owner.");
        }
        private sealed class LayerNameCollection : Collection<string>
        {
            private static void Check(string value)
            {
                if (string.IsNullOrEmpty(value) || value.IndexOfAny(new[] { '\0', '\r', '\n' }) >= 0)
                    throw new ArgumentException("A stored layer name must be nonempty single-line Unicode text.", nameof(value));
                for (int index = 0; index < value.Length; index++)
                {
                    if (char.IsHighSurrogate(value[index]))
                    {
                        if (index + 1 >= value.Length || !char.IsLowSurrogate(value[++index]))
                            throw new ArgumentException("A stored layer name contains an unpaired surrogate.", nameof(value));
                    }
                    else if (char.IsLowSurrogate(value[index]))
                        throw new ArgumentException("A stored layer name contains an unpaired surrogate.", nameof(value));
                }
            }
            protected override void InsertItem(int index, string item) { Check(item); base.InsertItem(index, item); }
            protected override void SetItem(int index, string item) { Check(item); base.SetItem(index, item); }
        }
    }
}
