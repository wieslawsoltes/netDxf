// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using Attribute = netDxf.Entities.Attribute;

namespace netDxf
{
    public sealed partial class DxfDocument
    {
        internal IEnumerable<KeyValuePair<DxfObject, AttributeMText>> EmbeddedAttributeTextEntries()
        {
            foreach (Block block in this.Blocks)
            {
                foreach (AttributeDefinition definition in block.AttributeDefinitions.Values)
                    if (definition.MultilineText != null)
                        yield return new KeyValuePair<DxfObject, AttributeMText>(definition, definition.MultilineText);
                foreach (EntityObject entity in block.Entities)
                    if (entity is Insert insert)
                        foreach (Attribute attribute in insert.Attributes)
                            if (attribute.MultilineText != null)
                                yield return new KeyValuePair<DxfObject, AttributeMText>(attribute, attribute.MultilineText);
            }
        }
        internal void ValidateEmbeddedAttributeBlock(Block root)
        {
            if (root == null) return;
            var seen = new HashSet<Block>();
            var pending = new Stack<Block>(); pending.Push(root);
            while (pending.Count != 0)
            {
                Block block = pending.Pop();
                if (!seen.Add(block)) continue;
                foreach (AttributeDefinition definition in block.AttributeDefinitions.Values)
                    AttributeMTextBinding.Validate(definition.MultilineText, this);
                foreach (EntityObject entity in block.Entities)
                    if (entity is Insert insert)
                    {
                        foreach (Attribute attribute in insert.Attributes)
                            AttributeMTextBinding.Validate(attribute.MultilineText, this);
                        pending.Push(insert.Block);
                    }
            }
        }
        internal void ValidateEmbeddedAttributeEntity(EntityObject entity)
        {
            if (!(entity is Insert insert)) return;
            foreach (Attribute attribute in insert.Attributes)
                AttributeMTextBinding.Validate(attribute.MultilineText, this);
            this.ValidateEmbeddedAttributeBlock(insert.Block);
        }
        internal bool HasEmbeddedAttributeTextStyleReference(TextStyle style)
        {
            foreach (var entry in this.EmbeddedAttributeTextEntries())
                if (ReferenceEquals(entry.Value.Style, style)) return true;
            return false;
        }
    }
}
