// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredCellStyleMap
    {
        /// <summary>Maximum number of entries accepted by structural authoring before the separate tag limit.</summary>
        public const int MaximumAuthoredEntries = 65536;

        /// <summary>Atomically replaces the complete qualified entry structure, including counts, order and frame presence.</summary>
        /// <remarks>Unknown existing formats reject rather than being discarded. This explicit operation does not synchronize TABLE consumers or private references to entry IDs. Common metadata, ownership and source profile remain fixed.</remarks>
        public void ReplaceStructure(IEnumerable<DxfCellStyleMapEntryDefinition> entries)
        {
            if (this.editing) { this.reentered = true; throw new InvalidOperationException("CELLSTYLEMAP replacement cannot be reentered."); }
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            this.editing = true; this.reentered = false;
            try
            {
                var definitions = MaterializeDefinitions(entries);
                if (this.reentered) throw new InvalidOperationException("Recursive replacement invalidated structural editing.");
                this.ValidateNameReplacementSource();
                if (this.Entries.Any(entry => entry.Format == null))
                    throw new NotSupportedException("Structural replacement cannot discard unqualified format packets.");
                var candidate = BuildDefinition(this.source, this.Owner, definitions);
                this.ValidateStructureBudget(candidate.Payload.Count);
                if (SameDefinitionPacket(this.Payload, candidate.Payload)) return;
                this.Payload = candidate.Payload; this.Entries = candidate.Entries;
                this.references = candidate.references; this.handles = candidate.handles;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        internal static List<DxfCellStyleMapEntryDefinition> MaterializeDefinitions(IEnumerable<DxfCellStyleMapEntryDefinition> entries)
        {
            var result = new List<DxfCellStyleMapEntryDefinition>();
            long count = 2;
            foreach (var entry in entries)
            {
                if (entry == null || result.Count == MaximumAuthoredEntries)
                    throw new ArgumentException("Too many or null map definitions.", nameof(entries));
                count += 6 + entry.Format.TagCount;
                if (count > MaximumPayloadTags - 5) throw new ArgumentException("Authored map exceeds its stored tag limit.", nameof(entries));
                result.Add(entry);
            }
            return result;
        }

        internal static DxfStoredCellStyleMap BuildDefinition(DxfDocument document, DxfObject owner, IList<DxfCellStyleMapEntryDefinition> entries)
        {
            var version = document.DrawingVariables.AcadVer;
            if (version != DxfVersion.AutoCad2004 && version != DxfVersion.AutoCad2007 && version != DxfVersion.AutoCad2010 &&
                version != DxfVersion.AutoCad2013 && version != DxfVersion.AutoCad2018)
                throw new NotSupportedException("Map authoring requires an R2004 through R2018 typed profile.");
            foreach (var resource in entries.SelectMany(entry => entry.Format.Resources))
            {
                bool member = resource is TextStyle style
                    ? ReferenceEquals(style.Owner, document.TextStyles) && document.TextStyles.TryGetValue(style.Name, out TextStyle text) && ReferenceEquals(text, style)
                    : resource is Linetype line && ReferenceEquals(line.Owner, document.Linetypes) && document.Linetypes.TryGetValue(line.Name, out Linetype ltype) && ReferenceEquals(ltype, line);
                if (!member || resource.Handle == null || !ReferenceEquals(document.GetObjectByHandle(resource.Handle), resource))
                    throw new ArgumentException("Authored format resources must already be registered in the destination document.", nameof(entries));
            }
            var tags = new List<DxfTag>();
            void Add(short code, object value) { tags.Add(new DxfTag(code, value)); }
            void Fields(short[] codes, params object[] values) { for (int i = 0; i < codes.Length; i++) Add(codes[i], values[i]); }
            Add(100, "AcDbCellStyleMap"); Add(90, entries.Count);
            foreach (var entry in entries)
            {
                var f = entry.Format;
                Add(300, "CELLSTYLE"); Add(1, "TABLEFORMAT_BEGIN"); Add(90, f.StoredType); Add(170, f.StoredDataFlags);
                if (f.StoredDataFlags != 0)
                {
                    var v = f.Values; var c = f.Content;
                    Fields(new short[] { 91, 92, 62, 93 }, v.StoredPropertyOverrides, v.StoredMergeFlags, v.StoredBackgroundColor, v.StoredContentLayout);
                    Add(300, "CONTENTFORMAT"); Add(1, "CONTENTFORMAT_BEGIN");
                    Fields(new short[] { 90, 91, 92, 93, 300, 40, 140, 94, 62, 340, 144 }, c.StoredPropertyOverrides,
                        c.StoredPropertyFlags, c.StoredDataType, c.StoredUnitType, EncodeDefinitionText(c.FormatString, version),
                        c.Rotation, c.BlockScale, c.StoredAlignment, c.StoredColor, f.TextStyle?.Handle ?? "0", c.TextHeight);
                    Add(309, "CONTENTFORMAT_END"); Add(171, f.StoredMarginFlags);
                    if (f.Margins != null)
                    {
                        var m = f.Margins;
                        Add(301, "MARGIN"); Add(1, "CELLMARGIN_BEGIN");
                        Fields(new short[] { 40, 40, 40, 40, 40, 40 }, m.VerticalMargin, m.HorizontalMargin,
                            m.BottomMargin, m.RightMargin, m.HorizontalSpacing, m.VerticalSpacing);
                        Add(309, "CELLMARGIN_END");
                    }
                    Add(94, f.Borders.Count);
                    foreach (var border in f.Borders)
                    {
                        var b = border.Values;
                        Add(95, border.StoredIndexMask); Add(302, "GRIDFORMAT"); Add(1, "GRIDFORMAT_BEGIN");
                        Fields(new short[] { 90, 91, 62, 92, 340, 93, 40 }, b.StoredPropertyOverrides, b.StoredBorderType,
                            b.StoredColor, b.StoredLineweight, border.Linetype?.Handle ?? "0", b.StoredVisibility, b.DoubleLineSpacing);
                        Add(309, "GRIDFORMAT_END");
                    }
                }
                Add(309, "TABLEFORMAT_END"); Add(1, "CELLSTYLE_BEGIN");
                Add(90, entry.Id); Add(91, entry.StoredType); Add(300, EncodeDefinitionText(entry.Name, version)); Add(309, "CELLSTYLE_END");
            }
            var candidate = new DxfStoredCellStyleMap(document, tags, DecodeFormatString) { Owner = owner };
            candidate.Resolve(document.GetObjectByHandle);
            if (candidate.Entries.Any(entry => entry.Format == null)) throw new InvalidOperationException("Authored packet failed its projection admission.");
            return candidate;
        }

        private static string EncodeDefinitionText(string text, DxfVersion version)
        {
            var result = new StringBuilder();
            foreach (char value in text)
            {
                bool escape = value == '\\' || version < DxfVersion.AutoCad2007 && value > 127;
                if (result.Length > DxfStoredTableContent.MaximumEditedStringLength - (escape ? 7 : 1))
                    throw new ArgumentOutOfRangeException(nameof(text), "The encoded map string exceeds the edit limit.");
                if (escape) result.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(value);
            }
            return result.ToString();
        }

        private void ValidateStructureBudget(int payloadCount)
        {
            long count = payloadCount + 2L + (this.ExtensionDictionary == null ? 0 : 3);
            int reactors = this.PersistentReactors.Where(item => item != null).Select(item => item.Handle).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (reactors != 0) count += reactors + 2L;
            foreach (XData data in this.XData.Values)
            {
                count++;
                foreach (var tag in data.XDataRecord)
                    count += tag.Code == XDataCode.BinaryData ? Math.Max(1L, (((byte[])tag.Value).LongLength + 126L) / 127L) : 1L;
            }
            if (count > MaximumPayloadTags) throw new InvalidOperationException("Map structure and common metadata exceed the record tag limit.");
        }

        private static bool SameDefinitionPacket(IReadOnlyList<DxfTag> left, IReadOnlyList<DxfTag> right)
        {
            if (left.Count != right.Count) return false;
            for (int i = 0; i < left.Count; i++)
            {
                if (left[i].Code != right[i].Code) return false;
                if (left[i].Value is double a && right[i].Value is double b)
                { if (BitConverter.DoubleToInt64Bits(a) != BitConverter.DoubleToInt64Bits(b)) return false; }
                else if (!Equals(left[i].Value, right[i].Value)) return false;
            }
            return true;
        }
    }
}
