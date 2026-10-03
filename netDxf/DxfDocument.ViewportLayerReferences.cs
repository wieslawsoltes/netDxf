// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Entities;
using netDxf.Tables;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // FrozenLayers is publicly mutable. A live scan covers Add/Insert/indexed
        // replacement/Remove/Clear without retaining subscriptions after removal.
        // Count only registered viewports and the exact canonical Layer identity.
        internal List<DxfObjectReference> ViewportLayerReferences(TableObject target)
        {
            var result = new List<DxfObjectReference>();
            if (!(target is Layer layer) || !ReferenceEquals(layer.Owner, this.Layers)
                || !ReferenceEquals(this.Layers[layer.Name], layer)) return result;

            var seen = new HashSet<Viewport>();
            Action<Viewport> add = viewport =>
            {
                if (viewport == null || !seen.Add(viewport)) return;
                int uses = 0;
                foreach (Layer frozen in viewport.FrozenLayers)
                    if (ReferenceEquals(frozen, layer)) uses = checked(uses + 1);
                if (uses != 0) result.Add(new DxfObjectReference(viewport, uses));
            };
            foreach (DxfObject item in this.AddedObjects.Values)
                if (item is Viewport viewport) add(viewport);
            foreach (netDxf.Objects.Layout layout in this.Layouts.Items)
                add(layout.Viewport);
            return result;
        }
    }
}
