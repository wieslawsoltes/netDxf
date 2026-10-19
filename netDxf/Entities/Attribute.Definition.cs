// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Attribute
    {
        // Adoption changes canonical identities, not authored resource values. Do not
        // dispatch property-edit callbacks in the middle of collection registration.
        internal void SetCanonicalResources(netDxf.Tables.Layer layer, netDxf.Tables.Linetype linetype, netDxf.Tables.TextStyle style)
        {
            this.layer = layer; this.linetype = linetype; this.style = style;
        }

        /// <summary>Rebinds the definition without changing the instance's tag, values or appearance.</summary>
        /// <param name="definition">A definition with the same tag (case insensitive), or null to retain an orphan instance.</param>
        /// <remarks>This method does not add the definition to a block or transform geometry.
        /// It supports prepared block edits where definitions are attached later in the transaction.</remarks>
        public void SetDefinition(AttributeDefinition definition)
        {
            if (definition != null && !string.Equals(this.tag, definition.Tag, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("The definition tag must match the attribute tag.", nameof(definition));
            this.definition = definition;
        }
    }
}
