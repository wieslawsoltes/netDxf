// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfTableStyle
    {
        private bool editing, reentered;

        /// <summary>Atomically replaces qualified header and selected row scalar or border values.</summary>
        /// <param name="header">New recognized header values, or null to keep the existing header unchanged.</param>
        /// <param name="rows">Distinct scalar and/or border edits made from the current Rows snapshot; an empty sequence is allowed.</param>
        /// <remarks>
        /// Only recognized header scalars, five projected row scalars and six complete border triples are edited.
        /// Unknown headers remain intact when header is null; a recognized leading version remains fixed. Row order, STYLE identities and names,
        /// raw formats, map contents, common metadata, source profile and ownership remain fixed.
        /// Enumeration and disposal finish before graph validation; caught reentry, stale and duplicate
        /// row snapshots reject before mutation. Equal requests retain current snapshots, and prior
        /// Tags, Header and Rows snapshots remain immutable. No handles are allocated. This explicit
        /// stored-data operation does not synchronize TABLE/CELLSTYLEMAP formats, evaluate layout or
        /// regenerate geometry. Independent document changes made by caller callbacks are not rolled back.
        /// </remarks>
        public void ReplaceStyle(DxfTableStyleHeader header, IEnumerable<DxfTableStyleRowEdit> rows)
        {
            if (this.editing)
            { this.reentered = true; throw new InvalidOperationException("TABLESTYLE replacement cannot be reentered."); }
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            this.editing = true; this.reentered = false;
            try
            {
                var edits = new List<DxfTableStyleRowEdit>();
                foreach (DxfTableStyleRowEdit edit in rows)
                {
                    if (edit == null) throw new ArgumentException("A row edit cannot be null.", nameof(rows));
                    if (edits.Count >= this.Rows.Count) throw new ArgumentException("Too many row edits for this snapshot.", nameof(rows));
                    edits.Add(edit);
                }
                if (this.reentered) throw new InvalidOperationException("TABLESTYLE replacement was reentered during enumeration.");
                this.ValidateReplacementSource();
                if (header != null && this.Header == null)
                    throw new NotSupportedException("The stored TABLESTYLE header is not qualified for editing.");
                var current = new HashSet<DxfTableStyleRow>(this.Rows);
                var seen = new HashSet<DxfTableStyleRow>();
                foreach (DxfTableStyleRowEdit edit in edits)
                    if (!current.Contains(edit.Original) || !seen.Add(edit.Original))
                        throw new ArgumentException("Row edits must identify distinct rows from the current snapshot.", nameof(rows));

                var replacements = new Dictionary<DxfTag, DxfTag>();
                if (header != null)
                {
                    int offset = this.Header.StoredVersion.HasValue ? 1 : 0;
                    if (header.Description != this.Header.Description)
                        replacements.Add(this.publicTags[offset + 0], new DxfTag(3, this.EncodeEditedDescription(header.Description)));
                    ReplaceScalar(replacements, this.publicTags[offset + 1], header.FlowDirection);
                    ReplaceScalar(replacements, this.publicTags[offset + 2], header.StoredFlags);
                    ReplaceScalar(replacements, this.publicTags[offset + 3], header.HorizontalCellMargin);
                    ReplaceScalar(replacements, this.publicTags[offset + 4], header.VerticalCellMargin);
                    ReplaceScalar(replacements, this.publicTags[offset + 5], header.SuppressTitle ? (short)1 : (short)0);
                    ReplaceScalar(replacements, this.publicTags[offset + 6], header.SuppressColumnHeading ? (short)1 : (short)0);
                }
                foreach (DxfTableStyleRowEdit edit in edits)
                {
                    var tags = edit.Original.Tags; var values = edit.Values;
                    if (values != null)
                    {
                        ReplaceScalar(replacements, tags.Single(t => t.Code == 140), values.TextHeight);
                        ReplaceScalar(replacements, tags.Single(t => t.Code == 170), values.CellAlignment);
                        ReplaceScalar(replacements, tags.Single(t => t.Code == 62), values.StoredTextColor);
                        ReplaceScalar(replacements, tags.Single(t => t.Code == 63), values.StoredFillColor);
                        ReplaceScalar(replacements, tags.Single(t => t.Code == 283), values.BackgroundColorEnabled ? (short)1 : (short)0);
                    }
                    if (edit.Borders != null)
                        for (int i = 0; i < DxfTableStyleRowBorders.BorderCount; i++)
                        {
                            DxfTableStyleBorderValues border = edit.Borders.Values[i];
                            ReplaceScalar(replacements, tags.Single(t => t.Code == 274 + i), border.StoredLineweight);
                            ReplaceScalar(replacements, tags.Single(t => t.Code == 284 + i), border.IsVisible ? (short)1 : (short)0);
                            ReplaceScalar(replacements, tags.Single(t => t.Code == 64 + i), border.StoredColor);
                        }
                }
                if (replacements.Count == 0) return;
                var packet = this.Tags.Select(t => replacements.TryGetValue(t, out DxfTag value) ? value : t).ToList();
                var candidate = new DxfTableStyle(this.source, packet, DecodeEditedDescription);
                if (candidate.Rows.Count != this.Rows.Count || candidate.Rows.Where((row, index) =>
                    (row.Values == null) != (this.Rows[index].Values == null) ||
                    (row.Borders == null) != (this.Rows[index].Borders == null)).Any())
                    throw new InvalidOperationException("Replacement changed the qualified row inventory.");
                foreach (DxfTableStyleRow row in candidate.Rows)
                    if (this.namedStyles.TryGetValue(row.Tags[0], out Tuple<netDxf.Tables.TextStyle, string> binding))
                        row.BindTextStyle(binding.Item1);
                // Every callback, validation and allocation has completed; only state swaps remain.
                this.Tags = candidate.Tags; this.Header = candidate.Header;
                this.Rows = candidate.Rows; this.publicTags = candidate.publicTags;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        private void ValidateReplacementSource()
        {
            if (!this.resolved || this.IsErased || this.Database == null || !ReferenceEquals(this.Database.Document, this.source)
                || !ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                throw new InvalidOperationException("TABLESTYLE must remain registered in its source document.");
            IReadOnlyList<string> errors = this.Database.Validate();
            if (errors.Count != 0)
                throw new InvalidOperationException("Cannot replace TABLESTYLE in an invalid source database: " + string.Join("; ", errors));
        }

        private static void ReplaceScalar(Dictionary<DxfTag, DxfTag> replacements, DxfTag original, object value)
        {
            bool same = original.Value is double first && value is double second
                ? BitConverter.DoubleToInt64Bits(first) == BitConverter.DoubleToInt64Bits(second) : Equals(original.Value, value);
            if (!same) replacements.Add(original, new DxfTag(original.Code, value));
        }

        private string EncodeEditedDescription(string text)
        {
            var result = new StringBuilder();
            foreach (char value in text)
                if (value == '\\' || this.SourceVersion < DxfVersion.AutoCad2007 && value > 127)
                    result.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(value);
            return result.ToString();
        }

        private static string DecodeEditedDescription(string text)
        {
            var result = new StringBuilder();
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '\\' && i + 6 < text.Length && (text[i + 1] == 'U' || text[i + 1] == 'u') && text[i + 2] == '+'
                    && int.TryParse(text.Substring(i + 3, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int decoded))
                { value = (char)decoded; i += 6; }
                result.Append(value);
            }
            return result.ToString();
        }
    }
}
