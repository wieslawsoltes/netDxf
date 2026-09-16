// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.IO;
using netDxf.Tables;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredCellStyleMap
    {
        /// <summary>Atomically replaces selected entry names and qualified nested format values and resource references.</summary>
        /// <remarks>
        /// Requests must use distinct current entry snapshots. Enumeration and disposal complete before validation.
        /// Caught reentry through either replacement API rejects the outer operation. Existing structure, entry
        /// identifiers/types, border masks/order and frame-presence flags are preserved. STYLE/LTYPE targets must
        /// already be registered in this source document; null explicitly clears a selected handle. Earlier
        /// payload, entry and reference-membership snapshots remain unchanged. Equal requests preserve snapshots.
        /// No handles are allocated, no resources imported, and no geometry regenerated. Independent document
        /// changes made by caller callbacks are not rolled back. Concurrent-thread mutation is not supported.
        /// </remarks>
        public void ReplaceEntries(IEnumerable<DxfStoredCellStyleMapEntryEdit> entries)
        {
            if (this.editing)
            { this.reentered = true; throw new InvalidOperationException("CELLSTYLEMAP replacement cannot be reentered."); }
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            this.editing = true; this.reentered = false;
            try
            {
                var edits = new List<DxfStoredCellStyleMapEntryEdit>();
                foreach (var edit in entries)
                {
                    if (edit == null || edits.Count >= this.Entries.Count)
                        throw new ArgumentException("Too many or null CELLSTYLEMAP entry edits.", nameof(entries));
                    edits.Add(edit);
                }
                if (this.reentered) throw new InvalidOperationException("CELLSTYLEMAP replacement was reentered during enumeration.");
                this.ValidateNameReplacementSource();
                var current = new HashSet<DxfStoredCellStyleMapEntry>(this.Entries);
                var seen = new HashSet<DxfStoredCellStyleMapEntry>();
                var replacements = new Dictionary<DxfTag, DxfTag>();
                var selectedTargets = new Dictionary<DxfTag, DxfObject>();
                foreach (var edit in edits)
                {
                    if (!current.Contains(edit.Original) || !seen.Add(edit.Original))
                        throw new ArgumentException("Entry edits must use distinct current snapshots.", nameof(entries));
                    if (edit.Name != null && edit.Name != edit.Original.Name)
                        ReplaceFormatValue(replacements, this.Payload[edit.Original.NameIndex], this.EncodeEntryName(edit.Name));
                    if (edit.Format != null)
                    {
                        if (!ReferenceEquals(edit.Original.Format, edit.Format.Original))
                            throw new ArgumentException("The format snapshot is not current.", nameof(entries));
                        this.StageFormatEdit(edit.Format, replacements, selectedTargets);
                    }
                }
                if (replacements.Count == 0) return;
                var packet = this.Payload.Select(tag => replacements.TryGetValue(tag, out DxfTag next) ? next : tag).ToList();
                var candidate = new DxfStoredCellStyleMap(this.source, packet, DecodeFormatString);
                // Projection admission may never change as a side effect of an edit.
                if (candidate.Entries.Count != this.Entries.Count || candidate.Entries.Where((entry, i) =>
                    (entry.Format == null) != (this.Entries[i].Format == null)).Any())
                    throw new InvalidOperationException("Replacement changed format projection admission.");
                var nextHandles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
                var nextReferences = new List<DxfObject>();
                foreach (var tag in packet)
                {
                    if (!DxfObjectDatabase.IsReference(tag) || Convert.ToUInt64((string)tag.Value, 16) == 0) continue;
                    if (!selectedTargets.TryGetValue(tag, out DxfObject target) && !this.handles.TryGetValue((string)tag.Value, out target))
                        throw new InvalidOperationException("Replacement lost an exact CELLSTYLEMAP dependency.");
                    nextHandles[(string)tag.Value] = target;
                    nextReferences.Add(target);
                }
                candidate.BindFormats(nextHandles);
                // All allocation, validation, caller enumeration and binding finished; only state swaps follow.
                this.Payload = candidate.Payload; this.Entries = candidate.Entries;
                this.handles = nextHandles; this.references = nextReferences;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        private void StageFormatEdit(DxfCellStyleFormatEdit edit, Dictionary<DxfTag, DxfTag> replacements,
            Dictionary<DxfTag, DxfObject> selectedTargets)
        {
            var original = edit.Original;
            if (edit.Values != null)
            {
                var values = edit.Values;
                ReplaceFormatValues(replacements, original.HeaderTags, new object[] {
                    values.StoredPropertyOverrides, values.StoredMergeFlags, values.StoredBackgroundColor, values.StoredContentLayout });
            }
            if (edit.Content != null)
            {
                var values = edit.Content; var tags = original.Content.Tags;
                ReplaceFormatValues(replacements, tags.Take(4).ToList(), new object[] {
                    values.StoredPropertyOverrides, values.StoredPropertyFlags, values.StoredDataType, values.StoredUnitType });
                if (values.FormatString != original.Content.Values.FormatString)
                    ReplaceFormatValue(replacements, tags[4], this.EncodeEntryName(values.FormatString));
                ReplaceFormatValues(replacements, tags.Skip(5).Take(4).ToList(), new object[] {
                    values.Rotation, values.BlockScale, values.StoredAlignment, values.StoredColor });
                ReplaceFormatValue(replacements, tags[10], values.TextHeight);
            }
            if (edit.ChangesTextStyle) this.StageFormatResource(original.Content.Tags[9], edit.TextStyle, replacements, selectedTargets);
            if (edit.Margins != null)
            {
                var m = edit.Margins;
                ReplaceFormatValues(replacements, original.MarginTags, new object[] {
                    m.VerticalMargin, m.HorizontalMargin, m.BottomMargin, m.RightMargin, m.HorizontalSpacing, m.VerticalSpacing });
            }
            foreach (var border in edit.Borders)
            {
                var tags = border.Original.Tags;
                if (border.Values != null)
                {
                    var v = border.Values;
                    ReplaceFormatValues(replacements, tags.Take(4).ToList(), new object[] {
                        v.StoredPropertyOverrides, v.StoredBorderType, v.StoredColor, v.StoredLineweight });
                    ReplaceFormatValue(replacements, tags[5], v.StoredVisibility);
                    ReplaceFormatValue(replacements, tags[6], v.DoubleLineSpacing);
                }
                if (border.ChangesLinetype) this.StageFormatResource(tags[4], border.Linetype, replacements, selectedTargets);
            }
        }

        private void StageFormatResource(DxfTag tag, DxfObject target, Dictionary<DxfTag, DxfTag> replacements,
            Dictionary<DxfTag, DxfObject> selectedTargets)
        {
            if (target != null)
            {
                bool registered = target is TextStyle style
                    ? ReferenceEquals(style.Owner, this.source.TextStyles) && this.source.TextStyles.TryGetValue(style.Name, out TextStyle text) && ReferenceEquals(text, style)
                    : target is Linetype line && ReferenceEquals(line.Owner, this.source.Linetypes) && this.source.Linetypes.TryGetValue(line.Name, out Linetype ltype) && ReferenceEquals(ltype, line);
                if (!registered || target.Handle == null || !ReferenceEquals(this.source.GetObjectByHandle(target.Handle), target))
                    throw new ArgumentException("The selected formatting resource must already be registered in the source document.", nameof(target));
            }
            this.handles.TryGetValue((string)tag.Value, out DxfObject prior);
            if (ReferenceEquals(prior, target)) return;
            var replacement = new DxfTag(tag.Code, target == null ? "0" : target.Handle);
            replacements.Add(tag, replacement);
            if (target != null) selectedTargets.Add(replacement, target);
        }

        private static void ReplaceFormatValues(Dictionary<DxfTag, DxfTag> replacements, IList<DxfTag> tags, object[] values)
        { for (int i = 0; i < values.Length; i++) ReplaceFormatValue(replacements, tags[i], values[i]); }

        private static void ReplaceFormatValue(Dictionary<DxfTag, DxfTag> replacements, DxfTag tag, object value)
        {
            bool equal = tag.Value is double first && value is double second
                ? BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second) : Equals(tag.Value, value);
            if (!equal) replacements.Add(tag, new DxfTag(tag.Code, value));
        }

        private static string DecodeFormatString(string text)
        {
            var result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '\\' && i + 6 < text.Length && (text[i + 1] == 'U' || text[i + 1] == 'u') && text[i + 2] == '+' &&
                    int.TryParse(text.Substring(i + 3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int decoded))
                { value = (char)decoded; i += 6; }
                result.Append(value);
            }
            return result.ToString();
        }
    }
}
