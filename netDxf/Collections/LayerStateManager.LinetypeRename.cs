// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Objects;
using netDxf.Tables;

namespace netDxf.Collections
{
    public partial class LayerStateManager
    {
        // Called AFTER rename observers, against the current document. Preparation
        // finishes before table-index publication. Snapshot setters are nonvirtual
        // and newName was already validated by the owning Linetype operation.
        // Do not replace properties or dictionaries: their values, aliases and
        // enumerators stay intact. Unknown/private payload names are not interpreted.
        internal Action PrepareLinetypeRename(Linetype target, string newName)
        {
            if (!ReferenceEquals(target.Owner, this.Owner.Linetypes))
                throw new InvalidOperationException("Linetype rename must use its current owning document.");
            var changes = new List<LayerStateProperties>();
            foreach (LayerState state in this.Items)
                foreach (LayerStateProperties property in state.Properties.Values)
                    if (property != null && string.Equals(property.LinetypeName, target.Name, StringComparison.OrdinalIgnoreCase))
                        changes.Add(property);
            bool header = string.Equals(this.Owner.DrawingVariables.CeLtype, target.Name, StringComparison.OrdinalIgnoreCase);
            return () =>
            {
                foreach (LayerStateProperties property in changes) property.LinetypeName = newName;
                if (header) this.Owner.DrawingVariables.CeLtype = newName;
            };
        }
    }
}
