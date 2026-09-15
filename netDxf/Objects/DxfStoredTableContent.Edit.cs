// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    public sealed partial class DxfStoredTableContent
    {
        /// <summary>Gets the admission limit for each newly encoded string tag, in UTF-16 code units.</summary>
        public const int MaximumEditedStringLength = 1048576;
        private bool editing;
        private bool reentered;

        /// <summary>Atomically replaces the small header, standalone style and selected qualified scalar values.</summary>
        /// <param name="name">The decoded linked-data name. Its existing three-tag header must be recognized.</param>
        /// <param name="description">The decoded linked-data description.</param>
        /// <param name="tableStyle">The actual registered TABLESTYLE identity, or null for a stored null handle.</param>
        /// <param name="values">Distinct edits made from the current StoredValues snapshot; an empty sequence is allowed.</param>
        /// <remarks>
        /// Enumeration and disposal complete before source and target identities are revalidated.
        /// Caught reentry still rejects the outer operation. Stale, duplicate and foreign value snapshots reject.
        /// Style changes reject when an ACAD_TABLE owns this content; cross-object synchronization is not performed.
        /// Scalar kinds, counts, units, format flags, format strings, dependencies and all other stored tags remain
        /// unchanged. Newer values require explicit display text. No formulas, formatting or layout are evaluated.
        /// Earlier Payload, Subclasses, StoredValues and References snapshots remain unchanged. An equivalent
        /// request preserves the current snapshots. The source profile, ownership and object registration remain fixed.
        /// The complete record is limited to 1,048,576 tags. Each newly encoded string is limited to
        /// MaximumEditedStringLength code units after escaping. CR/LF may be stored for binary DXF; text save rejects
        /// those strings before output. Independent document changes made by caller callbacks are not rolled back.
        /// </remarks>
        public void ReplaceContent(string name, string description, DxfDatabaseObject tableStyle, IEnumerable<DxfStoredTableContentValueEdit> values)
        {
            if (this.editing)
            { this.reentered = true; throw new InvalidOperationException("TABLECONTENT replacement cannot be reentered."); }
            if (values == null) throw new ArgumentNullException(nameof(values));
            CheckEditableText(name, nameof(name)); CheckEditableText(description, nameof(description));
            this.editing = true; this.reentered = false;
            try
            {
                var edits = new List<DxfStoredTableContentValueEdit>();
                foreach (DxfStoredTableContentValueEdit edit in values)
                {
                    if (edit == null) throw new ArgumentException("A value edit cannot be null.", nameof(values));
                    if (edits.Count >= this.StoredValues.Count) throw new ArgumentException("Too many value edits for this snapshot.", nameof(values));
                    edits.Add(edit);
                }
                if (this.reentered) throw new InvalidOperationException("TABLECONTENT replacement was reentered during enumeration.");
                this.ValidateReplacementSource();
                if (this.Name == null || this.Description == null)
                    throw new NotSupportedException("The linked-data header is not qualified for replacement.");
                if (tableStyle != null && (tableStyle.CodeName != "TABLESTYLE" || tableStyle.Handle == null
                    || !ReferenceEquals(this.source.GetObjectByHandle(tableStyle.Handle), tableStyle)))
                    throw new ArgumentException("The style must be an actual registered TABLESTYLE in the source document.", nameof(tableStyle));
                if (!ReferenceEquals(tableStyle, this.TableStyle))
                    for (DxfObject owner = this.Owner; owner != null; owner = owner.Owner)
                        if (owner.CodeName == "ACAD_TABLE")
                            throw new NotSupportedException("Changing the style of TABLE-owned content requires cross-object synchronization.");

                var current = new HashSet<DxfStoredTableContentValue>(this.StoredValues);
                var seen = new HashSet<DxfStoredTableContentValue>();
                foreach (DxfStoredTableContentValueEdit edit in edits)
                    if (!current.Contains(edit.Original) || !seen.Add(edit.Original))
                        throw new ArgumentException("Value edits must identify distinct values from the current snapshot.", nameof(values));
                var tags = this.Payload.ToList();
                bool changed = false;
                if (name != this.Name) { tags[1] = new DxfTag(1, this.EncodeEditedText(name)); changed = true; }
                if (description != this.Description) { tags[2] = new DxfTag(300, this.EncodeEditedText(description)); changed = true; }
                if (!ReferenceEquals(tableStyle, this.TableStyle))
                { tags[tags.Count - 1] = new DxfTag(340, tableStyle == null ? "0" : tableStyle.Handle); changed = true; }
                foreach (DxfStoredTableContentValueEdit edit in edits)
                {
                    var original = edit.Original;
                    if (!SameScalar(original.Value, edit.Value))
                    {
                        int index = original.ScalarIndex;
                        if (edit.Value is Vector3 point)
                        {
                            tags[index] = new DxfTag(11, point.X); tags[index + 1] = new DxfTag(21, point.Y); tags[index + 2] = new DxfTag(31, point.Z);
                        }
                        else tags[index] = new DxfTag(tags[index].Code, edit.Value is string text ? this.EncodeEditedText(text) : edit.Value);
                        changed = true;
                    }
                    if (original.DisplayIndex >= 0 && original.FormattedText != edit.FormattedText)
                    { tags[original.DisplayIndex] = new DxfTag(302, this.EncodeEditedText(edit.FormattedText)); changed = true; }
                }
                if (!changed) return;

                var candidate = new DxfStoredTableContent(this.source, tags, DecodeStoredText);
                // Replacement preserves the exact qualified frame inventory, including every untouched value.
                if (candidate.StoredValues.Count != this.StoredValues.Count)
                    throw new InvalidOperationException("Replacement changed the qualified value frame inventory.");
                var nextHandles = new Dictionary<string, DxfObject>(StringComparer.OrdinalIgnoreCase);
                var nextReferences = new List<DxfObject>();
                for (int i = 0; i < tags.Count; i++)
                {
                    DxfTag tag = tags[i];
                    if (!DxfObjectDatabase.IsReference(tag) || Convert.ToUInt64((string)tag.Value, 16) == 0) continue;
                    string handle = (string)tag.Value;
                    DxfObject target = i == tags.Count - 1 ? tableStyle : this.handles[handle];
                    nextReferences.Add(target); nextHandles[handle] = target;
                }
                // All callbacks, allocations and validation have completed. Only state swaps remain.
                this.Payload = candidate.Payload; this.Subclasses = candidate.Subclasses;
                this.StoredValues = candidate.StoredValues; this.Name = candidate.Name; this.Description = candidate.Description;
                this.styleHandle = candidate.styleHandle; this.TableStyle = tableStyle;
                this.references = nextReferences; this.handles = nextHandles;
            }
            finally { this.editing = false; this.reentered = false; }
        }

        private void ValidateReplacementSource()
        {
            if (!this.resolved || this.IsErased || this.Database == null || !ReferenceEquals(this.Database.Document, this.source)
                || !ReferenceEquals(this.source.GetObjectByHandle(this.Handle), this))
                throw new InvalidOperationException("TABLECONTENT must remain registered in its source document.");
            IReadOnlyList<string> errors = this.Database.Validate();
            if (errors.Count != 0) throw new InvalidOperationException("Cannot replace TABLECONTENT in an invalid source database: " + string.Join("; ", errors));
            for (DxfObject owner = this.Owner; owner != null; owner = owner.Owner)
                if (owner is StoredTable table) table.Validate(this.source);
        }

        private long StoredRecordTagCount(long payloadCount)
        {
            long count = payloadCount + 2;
            if (this.ExtensionDictionary != null) count += 3;
            int reactors = this.PersistentReactors.Where(item => item != null).Select(item => item.Handle).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (reactors != 0) count += reactors + 2L;
            foreach (XData data in this.XData.Values)
            {
                count++;
                foreach (XDataRecord record in data.XDataRecord)
                    count += record.Code == XDataCode.BinaryData ? Math.Max(1L, (((byte[])record.Value).LongLength + 126L) / 127L) : 1L;
            }
            return count;
        }

        internal static void CheckEditableText(string text, string parameter)
        {
            if (text == null) throw new ArgumentNullException(parameter);
            if (text.Length > MaximumEditedStringLength) throw new ArgumentOutOfRangeException(parameter, "The edited string exceeds the admission limit.");
            for (int i = 0; i < text.Length; i++)
                if (text[i] == '\0' || char.IsSurrogate(text[i]) && (!char.IsHighSurrogate(text[i]) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                    throw new ArgumentException("Edited text must be valid UTF-16 without NUL.", parameter);
        }

        private string EncodeEditedText(string text)
        {
            var result = new StringBuilder();
            foreach (char value in text)
            {
                bool escape = value == '\\' || this.SourceVersion < DxfVersion.AutoCad2007 && value > 127;
                if (result.Length > MaximumEditedStringLength - (escape ? 7 : 1))
                    throw new ArgumentOutOfRangeException(nameof(text), "The encoded string exceeds the admission limit.");
                if (escape) result.Append("\\U+").Append(((int)value).ToString("X4", CultureInfo.InvariantCulture));
                else result.Append(value);
            }
            return result.ToString();
        }

        private static string DecodeStoredText(string text)
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

        private static bool SameScalar(object first, object second)
        {
            if (first is double a && second is double b) return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b);
            if (first is Vector3 x && second is Vector3 y) return SameScalar(x.X, y.X) && SameScalar(x.Y, y.Y) && SameScalar(x.Z, y.Z);
            return Equals(first, second);
        }
    }
}
