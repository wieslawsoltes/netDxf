// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Entities;
using netDxf.IO;
using netDxf.Objects;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        // LayerState is managed, not a DxfDatabaseObject. Report current exposed
        // incoming references rather than stale resource-subscription counts.
        // Uses counts represented reference slots, not hidden application semantics.
        internal List<DxfObjectReference> LayerStateReferences(LayerState target)
        {
            if (target == null || !ReferenceEquals(target.Owner, this.Layers.StateManager)
                || !ReferenceEquals(this.Layers.StateManager[target.Name], target)) return new List<DxfObjectReference>();
            return this.ExposedObjectReferences(target);
        }

        internal bool LayerStateReferencesRemoval(LayerState target)
        {
            // Keep conservative private/owned-subtree guards in addition to the
            // public direct-reference inventory. Removal is not recursive erasure.
            return target.ExtensionDictionary != null || this.StoredTableReferencesRemoval(target)
                || this.LayerStateReferences(target).Count != 0;
        }
    }
}
