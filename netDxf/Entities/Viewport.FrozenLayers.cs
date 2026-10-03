// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public partial class Viewport
    {
        internal void ValidateFrozenLayers(DxfDocument document)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Layer layer in this.frozenLayers)
            {
                if (layer == null || !names.Add(layer.Name))
                    throw new InvalidOperationException("Viewport frozen layers must have unique non-null names.");
                if (layer.Owner != null && !ReferenceEquals(layer.Owner.Owner, document))
                    throw new InvalidOperationException("A frozen layer belongs to another document. Clone the viewport instead.");
            }
        }

        internal void CanonicalizeFrozenLayers(DxfDocument document, bool assignHandle)
        {
            // Validate before allocating resources. Public name/owner validation
            // cannot be used here: a viewport is still detached while it is adopted.
            this.ValidateFrozenLayers(document);
            for (int i = 0; i < this.frozenLayers.Count; i++)
                this.frozenLayers.SetCanonicalItem(i, document.Layers.Add(this.frozenLayers[i], assignHandle));
        }
    }
}
