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

        private void RecordSourceObject(DxfObject item)
        {
            if (item != null && ulong.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong value)
                && value != 0) this.acceptedSourceObjects[value] = item;
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

        private void RecordSourceObject(DxfObject item, string sourceHandle)
        {
            // A collection constructor can allocate when its source handle is absent.
            // Such generated identities cannot borrow a discarded record's identity.
            if (item != null && ulong.TryParse(sourceHandle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong source)
                && source != 0 && ulong.TryParse(item.Handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong actual)
                && source == actual) this.acceptedSourceObjects[source] = item;
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
