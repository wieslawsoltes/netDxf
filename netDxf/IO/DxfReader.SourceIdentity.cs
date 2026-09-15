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

        private sealed class SourceRecordIdentity
        {
            // Filled only from this physical record's eligible common header.
            internal ulong Handle;
        }

        // The LAYER table extension dictionary can be consumed by the explicit
        // layer-state conversion without surviving as a registered DxfDictionary.
        private bool IsAcceptedSourceDictionary(string handle)
        {
            return ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                && this.sourceObjectIdentities.Contains(value)
                && this.acceptedSourceObjects.TryGetValue(value, out DxfObject accepted)
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
                && source.Handle == actual) this.acceptedSourceObjects[actual] = item;
        }

        private DxfObject GetObjectBySourceHandle(string handle)
        {
            if (!ulong.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                || value == 0 || !this.sourceObjectIdentities.Contains(value)
                || !this.acceptedSourceObjects.TryGetValue(value, out DxfObject accepted)) return null;
            DxfObject current = this.doc.GetObjectByHandle(value.ToString("X", CultureInfo.InvariantCulture));
            return ReferenceEquals(accepted, current) ? current : null;
        }
    }
}
