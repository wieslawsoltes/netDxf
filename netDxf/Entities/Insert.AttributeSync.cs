// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Collections;

namespace netDxf.Entities
{
    public partial class Insert
    {
        private void SynchronizeAttributes()
        {
            var document = this.Owner?.Record.Owner?.Owner;
            var removed = new List<Attribute>();
            var retained = new List<Attribute>();
            var definitions = new List<AttributeDefinition>();
            var added = new HashSet<Attribute>();
            var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Attribute attribute in this.attributes)
            {
                if (!this.block.AttributeDefinitions.TryGetValue(attribute.Tag, out AttributeDefinition definition))
                {
                    if (document != null && document.StoredTableReferencesRemoval(attribute))
                        throw new InvalidOperationException("A stored TABLE references an attribute that synchronization would remove.");
                    removed.Add(attribute);
                }
                else { retained.Add(attribute); definitions.Add(definition); tags.Add(attribute.Tag); }
            }
            foreach (AttributeDefinition definition in this.block.AttributeDefinitions.Values)
                if (tags.Add(definition.Tag))
                {
                    var attribute = new Attribute(definition) { Owner = this };
                    retained.Add(attribute); definitions.Add(definition); added.Add(attribute);
                }
            // Validate every transform before publishing membership or rebinding a
            // surviving attribute. Values and resource overrides remain independent.
            Matrix3 matrix = this.GetTransformation();
            Vector3 translation = this.Position - matrix * this.block.Origin;
            var prepared = new Attribute[retained.Count];
            for (int i = 0; i < retained.Count; i++)
                prepared[i] = retained[i].PrepareDefinitionTransform(definitions[i], matrix, translation);
            foreach (Attribute attribute in removed)
            {
                this.OnAttributeRemovedEvent(attribute); attribute.Handle = null; attribute.Owner = null;
            }
            for (int i = 0; i < retained.Count; i++)
            {
                Attribute attribute = retained[i];
                attribute.Definition = definitions[i];
                if (attribute.CommitInsertTransform(prepared[i])) this.ClearProxyGraphics();
                if (added.Contains(attribute)) this.OnAttributeAddedEvent(attribute);
            }
            this.attributes = new AttributeCollection(retained);
            if (removed.Count != 0 || added.Count != 0) this.ClearProxyGraphics();
        }
    }
}
