// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // Saved properties are mutable name-based references; compute the live view
        // instead of caching subscriptions that can miss direct property changes.
        // One use per stored property plus a saved current-layer use where applicable.
        // This covers registered LayerState objects, not arbitrary private payloads
        // or unsaved/detached snapshots. Header variables are not DxfObject references.
        internal List<DxfObjectReference> LayerStateResourceReferences(TableObject target)
        {
            var result = new List<DxfObjectReference>();
            bool layer = target is Layer l && ReferenceEquals(l.Owner, this.Layers)
                && ReferenceEquals(this.Layers[l.Name], l);
            bool linetype = target is Linetype t && ReferenceEquals(t.Owner, this.Linetypes)
                && ReferenceEquals(this.Linetypes[t.Name], t);
            if (!layer && !linetype) return result;
            foreach (LayerState state in this.Layers.StateManager.Items)
            {
                int uses = layer && string.Equals(state.CurrentLayer, target.Name, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                foreach (LayerStateProperties property in state.Properties.Values)
                    if (property != null && string.Equals(layer ? property.Name : property.LinetypeName,
                        target.Name, StringComparison.OrdinalIgnoreCase)) uses = checked(uses + 1);
                if (uses != 0) result.Add(new DxfObjectReference(state, uses));
            }
            return result;
        }
    }
}
