using System.Collections.Generic;
using System.Globalization;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        // Physical identities alone are insufficient: a parser can discard a source
        // record and a generated default can subsequently reuse that record's handle.
        private readonly HashSet<ulong> sourceObjectIdentities = new HashSet<ulong>();
        private readonly Dictionary<ulong, DxfObject> acceptedSourceObjects = new Dictionary<ulong, DxfObject>();
        private readonly Dictionary<ulong, SourceRecordIdentity> acceptedSourceRecords = new Dictionary<ulong, SourceRecordIdentity>();

        private sealed class SourceRecordIdentity
        {
            // Filled only from this physical record's eligible common header.
            internal ulong Handle;
            internal bool IdentitySeen;
            internal bool Ambiguous;
        }

        // The LAYER table extension dictionary can be consumed by the explicit
        // layer-state conversion without surviving as a registered DxfDictionary.
        private bool IsAcceptedSourceDictionary(string handle)
        {
            return ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                && this.sourceObjectIdentities.Contains(value)
                && this.acceptedSourceObjects.TryGetValue(value, out DxfObject accepted)
                && this.acceptedSourceRecords.TryGetValue(value, out SourceRecordIdentity source)
                && !source.Ambiguous
                && accepted is netDxf.Objects.DxfDictionary;
        }

        private SourceRecordIdentity CurrentSourceRecord
        {
            get { return ((DatabaseMetadataReader)this.chunk).SourceRecord; }
        }

        private void RecordSourceObject(DxfObject item, SourceRecordIdentity source)
        {
            // Some legacy parsers also read group5 from private payloads; only the
            // common identity of the exact record that produced this object counts.
            if (item != null && source != null && source.Handle != 0
                && ulong.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong actual)
                && source.Handle == actual)
            {
                this.acceptedSourceObjects[actual] = item;
                this.acceptedSourceRecords[actual] = source;
            }
        }

        private void ValidateSourceIdentityDeclarations()
        {
            // Validate retained records even when no other object points at them.
            // Otherwise a failed lookup could silently skip their common metadata.
            foreach (KeyValuePair<ulong, SourceRecordIdentity> entry in this.acceptedSourceRecords)
                if (entry.Value.Ambiguous)
                    throw new System.FormatException("A retained DXF object has an ambiguous physical source identity: "
                        + entry.Key.ToString("X", CultureInfo.InvariantCulture));
        }

        private DxfObject GetObjectBySourceHandle(string handle, bool includeMetadata = false)
        {
            if (!ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                || value == 0 || !this.sourceObjectIdentities.Contains(value)
                || !this.acceptedSourceObjects.TryGetValue(value, out DxfObject accepted)) return null;
            // A retained record cannot legitimize a handle also declared by a
            // discarded physical record. Every source-bound consumer needs the
            // same uniqueness proof, regardless of lookup order or target type.
            if (!this.acceptedSourceRecords.TryGetValue(value, out SourceRecordIdentity source) || source.Ambiguous) return null;
            string canonical = value.ToString("X", CultureInfo.InvariantCulture);
            DxfObject current = includeMetadata ? this.doc.StoredTableHandleTarget(canonical) : this.doc.GetObjectByHandle(canonical);
            return ReferenceEquals(accepted, current) ? current : null;
        }
    }
}
