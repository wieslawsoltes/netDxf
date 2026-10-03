// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using Attribute = netDxf.Entities.Attribute;

namespace netDxf.IO
{
    public static partial class DxfR12Codec
    {
        private static string AttributeTag(string tag)
        {
            if (string.IsNullOrEmpty(tag) || tag.Length > 31)
                throw new NotSupportedException("R12 attribute tags require 1 through 31 ASCII characters.");
            foreach (char c in tag)
                if (c < 33 || c > 126 || c == '!')
                    throw new NotSupportedException("R12 attribute tags cannot contain spaces, control characters or exclamation marks.");
            return tag;
        }

        private static short AttributeBits(AttributeFlags flags)
        {
            if (((int)flags & ~15) != 0)
                throw new NotSupportedException("This R12 profile supports the four classic attribute flags only.");
            return (short)flags;
        }

        private static Text AttributeText(AttributeDefinition value)
        {
            var text = new Text(value.Value, value.Position, value.Height, value.Style)
            {
                Normal = value.Normal, Width = value.Width, WidthFactor = value.WidthFactor,
                ObliqueAngle = value.ObliqueAngle, Rotation = value.Rotation, Alignment = value.Alignment,
                IsBackward = value.IsBackward, IsUpsideDown = value.IsUpsideDown,
                Color = value.Color, Layer = value.Layer, Linetype = value.Linetype,
                Lineweight = value.Lineweight, Transparency = value.Transparency,
                LinetypeScale = value.LinetypeScale, IsVisible = value.IsVisible
            };
            if (value.CommonData.ProxyGraphics != null)
                throw new NotSupportedException("Attribute proxy graphics require a different interchange profile.");
            value.CommonData.CopyTo(text.CommonData);
            return text;
        }

        private static Text AttributeText(Attribute value)
        {
            var text = new Text(value.Value, value.Position, value.Height, value.Style)
            {
                Normal = value.Normal, Width = value.Width, WidthFactor = value.WidthFactor,
                ObliqueAngle = value.ObliqueAngle, Rotation = value.Rotation, Alignment = value.Alignment,
                IsBackward = value.IsBackward, IsUpsideDown = value.IsUpsideDown,
                Color = value.Color, Layer = value.Layer, Linetype = value.Linetype,
                Lineweight = value.Lineweight, Transparency = value.Transparency,
                LinetypeScale = value.LinetypeScale, IsVisible = value.IsVisible
            };
            if (value.CommonData.ProxyGraphics != null)
                throw new NotSupportedException("Attribute proxy graphics require a different interchange profile.");
            value.CommonData.CopyTo(text.CommonData);
            return text;
        }

        private sealed partial class PrimitiveWriter
        {
            private void AttributeHeader(DxfObject source, Text text)
            {
                Metadata(source); Metadata(text.Linetype);
                if (text.Color.UseTrueColor || text.ColorName != null || text.ShadowMode.HasValue
                    || text.CommonData.ProxyGraphics != null || !text.IsVisible
                    || text.Lineweight != Lineweight.ByLayer || text.LinetypeScale != 1
                    || !text.Transparency.IsByLayer || text.Transparency.StoredAlphaValue.HasValue)
                    throw new NotSupportedException("The attribute has common fields outside the R12 profile.");
                string layer = this.RegisterLayer(text.Layer).Name;
                string pattern = this.RegisterLinetype(text.Linetype);
                this.Tag(0, source.CodeName);
                this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                this.Tag(8, layer); this.Tag(6, pattern); this.Tag(62, text.Color.Index);
            }

            private void Definition(AttributeDefinition value)
            {
                if (value.GetType() != typeof(AttributeDefinition))
                    throw new NotSupportedException("Derived attribute definitions require an explicit projection.");
                Text text = AttributeText(value);
                this.AttributeHeader(value, text);
                this.Tag(2, AttributeTag(value.Tag)); this.Tag(3, EncodeText(value.Prompt));
                this.Tag(70, AttributeBits(value.Flags)); this.Tag(73, (short)0);
                this.TextEntity(text, UnitNormal(text.Normal), 74);
            }

            private void Attributes(Insert insert, Attribute[] values, EndSequence end)
            {
                var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (Attribute value in values)
                {
                    if (value == null || value.GetType() != typeof(Attribute) || !tags.Add(AttributeTag(value.Tag)))
                        throw new NotSupportedException("Null, derived or duplicate attribute entries are ambiguous.");
                    if (value.Definition != null && (!insert.Block.AttributeDefinitions.ContainsTag(value.Tag)
                        || !ReferenceEquals(insert.Block.AttributeDefinitions[value.Tag], value.Definition)))
                        throw new NotSupportedException("An attribute definition must belong to the referenced block.");
                    Text text = AttributeText(value);
                    this.AttributeHeader(value, text);
                    this.Tag(2, value.Tag); this.Tag(70, AttributeBits(value.Flags)); this.Tag(73, (short)0);
                    this.TextEntity(text, UnitNormal(text.Normal), 74);
                }
                if (values.Length == 0 && end == null) return;
                if (end != null) Metadata(end);
                Layer layer = end == null || end.StoredLayer == null ? insert.Layer : end.StoredLayer;
                this.Tag(0, "SEQEND"); this.Tag(5, (this.nextHandle++).ToString("X", CultureInfo.InvariantCulture));
                this.Tag(8, this.RegisterLayer(layer).Name);
            }
        }

        private sealed partial class BlockReader
        {
            private readonly List<Insert> attributeOwners = new List<Insert>();

            private AttributeDefinition ReadAttributeFields(DxfRawRecord record, bool definition)
            {
                var fields = new Fields(record);
                string handle = this.Identity(fields);
                Layer layer = this.Layer(fields.Text(8, "0"));
                Linetype pattern = ResolveLinetype(fields.Text(6, "BYLAYER"), this.patterns, false);
                short color = fields.Integer(62, 256);
                if (color < 0 || color > 256 || fields.Integer(67, 0) != 0 || fields.Number(39, 0) != 0)
                    throw new NotSupportedException("Attribute appearance, paper-space or thickness is outside the R12 profile.");
                string tag = AttributeTag(fields.Text(2, null, true));
                string prompt = definition ? DecodeText(fields.Text(3, "")) : "";
                AttributeFlags flags = (AttributeFlags)fields.Integer(70, 0); AttributeBits(flags);
                if (fields.Integer(73, 0) != 0)
                    throw new NotSupportedException("Nonzero legacy attribute field lengths are not represented by the typed model.");
                Vector3 normal = UnitNormal(fields.Vector(210, Vector3.UnitZ));
                Text text = ReadTextEntity(fields, normal, 0, this.styles, 74);
                fields.Finish();
                return new AttributeDefinition(tag, text.Height, text.Style)
                {
                    Handle = handle, Prompt = prompt, Flags = flags, Value = text.Value,
                    Position = text.Position, Normal = text.Normal, Width = text.Width,
                    WidthFactor = text.WidthFactor, ObliqueAngle = text.ObliqueAngle,
                    Rotation = text.Rotation, Alignment = text.Alignment,
                    IsBackward = text.IsBackward, IsUpsideDown = text.IsUpsideDown,
                    Layer = layer, Linetype = pattern, Color = AciColor.FromCadIndex(color)
                };
            }

            internal void ReadDefinition(DxfRawRecord record, Block owner)
            {
                AttributeDefinition value = this.ReadAttributeFields(record, true);
                if (owner.AttributeDefinitions.ContainsTag(value.Tag))
                    throw new FormatException("Duplicate attribute definition tags are ambiguous.");
                owner.AttributeDefinitions.Add(value);
            }

            private List<Attribute> ReadAttributes(IReadOnlyList<DxfRawRecord> records, ref int position, out EndSequence end)
            {
                var result = new List<Attribute>(); var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                while (++position < records.Count)
                {
                    DxfRawRecord record = records[position];
                    if (string.Equals(record.Name, "SEQEND", StringComparison.OrdinalIgnoreCase))
                    {
                        var fields = new Fields(record);
                        string handle = this.Identity(fields);
                        Layer layer = this.Layer(fields.Text(8, "0"));
                        if (fields.Integer(67, 0) != 0)
                            throw new NotSupportedException("Paper-space attribute sequence ends require layout-aware interchange.");
                        fields.Finish();
                        end = new EndSequence(null) { Handle = handle, StoredLayer = layer };
                        return result;
                    }
                    if (!string.Equals(record.Name, "ATTRIB", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("INSERT attribute sequences must terminate with SEQEND.");
                    AttributeDefinition fieldsValue = this.ReadAttributeFields(record, false);
                    if (!tags.Add(fieldsValue.Tag)) throw new FormatException("Duplicate attribute instance tags are ambiguous.");
                    result.Add(new Attribute(fieldsValue) { Handle = fieldsValue.Handle, Definition = null });
                }
                throw new FormatException("Unterminated INSERT attribute sequence.");
            }

            private void BindAttributes()
            {
                foreach (Insert insert in this.attributeOwners)
                    foreach (Attribute value in insert.Attributes)
                        value.Definition = insert.Block.AttributeDefinitions.ContainsTag(value.Tag)
                            ? insert.Block.AttributeDefinitions[value.Tag] : null;
            }
        }
    }
}
