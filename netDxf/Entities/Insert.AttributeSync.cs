// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Insert
    {
        private void SynchronizeAttributes()
        {
            if (this.replacingAttributes) throw new InvalidOperationException("Attribute synchronization cannot run from an attribute notification.");
            var document = this.Owner?.Record.Owner?.Owner;
            var retained = new List<Attribute>();
            var definitions = new List<AttributeDefinition>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Attribute attribute in this.attributes)
            {
                if (!this.block.AttributeDefinitions.TryGetValue(attribute.Tag, out AttributeDefinition definition))
                {
                    if (document != null && document.StoredTableReferencesRemoval(attribute))
                        throw new InvalidOperationException("A stored TABLE references an attribute that synchronization would remove.");
                }
                else { retained.Add(attribute); definitions.Add(definition); tags.Add(attribute.Tag); }
            }
            foreach (AttributeDefinition definition in this.block.AttributeDefinitions.Values)
                if (tags.Add(definition.Tag))
                {
                    var attribute = new Attribute(definition) { Owner = this };
                    retained.Add(attribute); definitions.Add(definition);
                }
            // Validate every transform before publishing membership or rebinding a
            // surviving attribute. Values and resource overrides remain independent.
            Matrix3 matrix = this.GetTransformation();
            Vector3 translation = this.Position - matrix * this.block.Origin;
            var prepared = new Attribute[retained.Count];
            for (int i = 0; i < retained.Count; i++)
                prepared[i] = retained[i].PrepareDefinitionTransform(definitions[i], matrix, translation);
            var replacement = this.PrepareAttributeReplacement(retained);
            for (int i = 0; i < retained.Count; i++)
            {
                Attribute attribute = retained[i];
                attribute.Definition = definitions[i];
                if (attribute.CommitInsertTransform(prepared[i])) this.ClearProxyGraphics();
            }
            replacement.Apply();
        }
    }
}
