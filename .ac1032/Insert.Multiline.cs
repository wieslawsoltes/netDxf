// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using netDxf.Tables;

namespace netDxf.Entities
{
    public partial class Insert
    {
        private bool HasMultilineAttributes()
        {
            foreach (Attribute attribute in this.attributes) if (attribute.MultilineText != null) return true;
            return false;
        }
        private void TransformMultilineAttributes()
        {
            Matrix3 matrix = this.GetTransformation();
            Vector3 translation = this.Position - matrix * this.block.Origin;
            var candidates = new List<KeyValuePair<Attribute, Attribute>>();
            foreach (Attribute attribute in this.attributes)
            {
                AttributeDefinition definition = attribute.Definition;
                if (definition == null) continue;
                var candidate = new Attribute(definition) { Owner = attribute.Owner };
                // Geometry comes from the definition; content remains the instance's content.
                candidate.MultilineText = attribute.MultilineText == null ? null
                    : (definition.MultilineText ?? attribute.MultilineText).WithValue(attribute.MultilineText.Value);
                candidate.TransformBy(matrix, translation);
                candidates.Add(new KeyValuePair<Attribute, Attribute>(attribute, candidate));
            }
            // No candidate construction, callbacks or geometry validation after publication starts.
            foreach (var pair in candidates) pair.Key.CommitInsertTransform(pair.Value);
        }
        private void PreflightMultilineDefaults()
        {
            bool found = this.HasMultilineAttributes();
            foreach (AttributeDefinition definition in this.block.AttributeDefinitions.Values)
                found |= definition.MultilineText != null;
            if (!found) return;
            Matrix3 matrix = this.GetTransformation();
            Vector3 translation = this.Position - matrix * this.block.Origin;
            foreach (AttributeDefinition definition in this.block.AttributeDefinitions.Values)
            {
                Attribute attribute = this.attributes.AttributeWithTag(definition.Tag);
                AttributeMText text = attribute == null ? definition.MultilineText : attribute.MultilineText;
                if (text != null) (definition.MultilineText ?? text).TransformBy(matrix, translation);
            }
        }
        private static MText ExplodeMultilineAttribute(Attribute attribute, Vector3 arrayOffset)
        {
            MText text = attribute.MultilineText.Clone().ToMText();
            text.Position += arrayOffset;
            text.Layer = (Layer)attribute.Layer.Clone();
            text.Linetype = (Linetype)attribute.Linetype.Clone();
            text.Color = (AciColor)attribute.Color.Clone();
            text.Lineweight = attribute.Lineweight;
            text.Transparency = (Transparency)attribute.Transparency.Clone();
            text.LinetypeScale = attribute.LinetypeScale;
            text.IsVisible = attribute.IsVisible && (attribute.Flags & AttributeFlags.Hidden) == 0;
            return text;
        }
    }
}
