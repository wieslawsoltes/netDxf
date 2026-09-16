// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using netDxf.Header;
using netDxf.IO;

namespace netDxf.Objects
{
    /// <summary>A scalar evaluator-data entry retained in source order, including duplicate names.</summary>
    public sealed class DxfFieldDataValue
    {
        internal DxfFieldDataValue(string name, object value, IReadOnlyList<DxfTag> tags)
        { this.Name = name; this.Value = value; this.Tags = tags; }
        /// <summary>Gets the decoded key without assigning private evaluator semantics.</summary>
        public string Name { get; }
        /// <summary>Gets null, int, finite double or decoded string.</summary>
        public object Value { get; }
        /// <summary>Gets the exact immutable stored value packet.</summary>
        public IReadOnlyList<DxfTag> Tags { get; }
    }

    /// <summary>An immutable projection of a complete recognized FIELD evaluation/cache envelope.</summary>
    /// <remarks>Unknown data kinds or extensions leave the parent FIELD's Evaluation null while its
    /// complete payload is preserved. The flags remain stored integers, not inferred private semantics.</remarks>
    public sealed class DxfFieldEvaluationSnapshot
    {
        private DxfFieldEvaluationSnapshot() { }
        internal DxfStoredField Field { get; private set; }
        internal IReadOnlyList<DxfTag> SourcePayload { get; private set; }
        internal int StateIndex, StatusIndex, ErrorIndex, MessageIndex, CacheStart, CacheEnd, TextStart;
        internal int Kind, ScalarStart, ScalarEnd, ValueDisplayIndex;
        /// <summary>Gets the stored group-91 evaluation options.</summary>
        public int StoredEvaluationOptions { get; private set; }
        /// <summary>Gets the stored group-92 filing options.</summary>
        public int StoredFilingOptions { get; private set; }
        /// <summary>Gets the stored group-94 state bits.</summary>
        public int StoredState { get; private set; }
        /// <summary>Gets the stored group-95 evaluation status.</summary>
        public int StoredStatus { get; private set; }
        /// <summary>Gets the stored group-96 evaluator error code.</summary>
        public int StoredErrorCode { get; private set; }
        /// <summary>Gets the decoded stored evaluator error message.</summary>
        public string ErrorMessage { get; private set; }
        /// <summary>Gets ordered scalar evaluator data; no key-specific meaning is inferred.</summary>
        public IReadOnlyList<DxfFieldDataValue> Data { get; private set; }
        /// <summary>Gets the cached null, int, finite double or decoded string value.</summary>
        public object Value { get; private set; }
        /// <summary>Gets the modern AcValue flags, or null for compact encoding.</summary>
        public int? StoredValueFlags { get; private set; }
        /// <summary>Gets the modern AcValue units, or null for compact encoding.</summary>
        public int? StoredUnitType { get; private set; }
        /// <summary>Gets the decoded format control string; retained, not executed.</summary>
        public string FormatString { get; private set; }
        /// <summary>Gets the modern AcValue display string, or null for compact encoding.</summary>
        public string ValueDisplayText { get; private set; }
        /// <summary>Gets decoded concatenated FIELD display text, with its length checked in UTF-16 units.</summary>
        public string FormattedText { get; private set; }
        /// <summary>Creates a result edit bound to this actual snapshot; it does not modify the field.</summary>
        public DxfFieldResultEdit WithResult(DxfFieldResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            return new DxfFieldResultEdit(this, result);
        }

        internal static DxfFieldEvaluationSnapshot TryRead(DxfStoredField field, IReadOnlyList<DxfTag> tags)
        {
            if (tags.Count > DxfStoredField.MaximumResultPayloadTags) return null;
            try
            {
                var read = new Reader(tags);
                read.Marker(100, "AcDbField"); read.Text(1); read.Text(2);
                while (read.Has(3)) read.Text(3);
                int count = read.Count(90);
                for (int i = 0; i < count; i++) read.Next(360);
                count = read.Count(97);
                for (int i = 0; i < count; i++) read.Next(331);
                bool modern = field.SourceVersion >= DxfVersion.AutoCad2007;
                string legacyFormat = modern ? null : read.Text(4);
                var result = new DxfFieldEvaluationSnapshot { Field = field, SourcePayload = tags };
                result.StoredEvaluationOptions = read.Integer(91); result.StoredFilingOptions = read.Integer(92);
                result.StateIndex = read.Index; result.StoredState = read.Integer(94);
                result.StatusIndex = read.Index; result.StoredStatus = read.Integer(95);
                result.ErrorIndex = read.Index; result.StoredErrorCode = read.Integer(96);
                result.MessageIndex = read.Index; result.ErrorMessage = read.Text(300);
                count = read.Count(93);
                var data = new List<DxfFieldDataValue>();
                for (int i = 0; i < count; i++)
                {
                    string name = read.Text(6); int first = read.Index;
                    var parsed = ReadValue(read, modern);
                    data.Add(new DxfFieldDataValue(name, parsed.Value, tags.Skip(first).Take(read.Index - first).ToList().AsReadOnly()));
                }
                result.Data = data.AsReadOnly();
                read.Marker(7, "ACFD_FIELD_VALUE"); result.CacheStart = read.Index;
                var cache = ReadValue(read, modern); result.CacheEnd = read.Index;
                result.Kind = cache.Kind; result.Value = cache.Value; result.ScalarStart = cache.ScalarStart;
                result.ScalarEnd = cache.ScalarEnd; result.ValueDisplayIndex = cache.DisplayIndex;
                result.StoredValueFlags = cache.Flags; result.StoredUnitType = cache.Units;
                result.FormatString = modern ? cache.Format : legacyFormat; result.ValueDisplayText = cache.Display;
                result.TextStart = read.Index;
                var text = new StringBuilder((string)read.Next(301).Value);
                while (read.Has(9))
                {
                    string part = (string)read.Next(9).Value;
                    if (part.Length > DxfStoredTableContent.MaximumEditedStringLength - text.Length) throw new FormatException();
                    text.Append(part);
                }
                result.FormattedText = DxfStoredField.DecodeResultText(text.ToString());
                if (read.Integer(98) != result.FormattedText.Length || !read.AtEnd) return null;
                return result;
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
        }
        private sealed class ParsedValue
        {
            internal int Kind, ScalarStart, ScalarEnd, DisplayIndex = -1;
            internal int? Flags, Units;
            internal object Value;
            internal string Format, Display;
        }
        private static ParsedValue ReadValue(Reader read, bool modern)
        {
            var value = new ParsedValue();
            if (modern) value.Flags = read.Integer(93);
            value.Kind = read.Integer(90); value.ScalarStart = read.Index;
            switch (value.Kind)
            {
                case 0:
                    if (read.Has(91) && read.Integer(91) != 0) throw new FormatException();
                    break;
                case 1: value.Value = read.Integer(91); break;
                case 2:
                    double real = (double)read.Next(140).Value;
                    if (double.IsNaN(real) || double.IsInfinity(real)) throw new FormatException();
                    value.Value = real; break;
                case 4: value.Value = read.Text(1); break;
                default: throw new FormatException("Unsupported FIELD value packet.");
            }
            value.ScalarEnd = read.Index;
            if (modern)
            {
                value.Units = read.Integer(94); value.Format = read.Text(300);
                value.DisplayIndex = read.Index; value.Display = read.Text(302);
                read.Marker(304, "ACVALUE_END");
            }
            return value;
        }
        private sealed class Reader
        {
            private readonly IReadOnlyList<DxfTag> tags;
            internal Reader(IReadOnlyList<DxfTag> tags) { this.tags = tags; }
            internal int Index { get; private set; }
            internal bool AtEnd { get { return this.Index == this.tags.Count; } }
            internal bool Has(short code) { return this.Index < this.tags.Count && this.tags[this.Index].Code == code; }
            internal DxfTag Next(short code)
            { if (!this.Has(code)) throw new FormatException("Unqualified FIELD evaluation framing."); return this.tags[this.Index++]; }
            internal int Integer(short code) { return (int)this.Next(code).Value; }
            internal int Count(short code)
            { int count = this.Integer(code); if (count < 0 || count > 16384 || count > this.tags.Count - this.Index) throw new FormatException(); return count; }
            internal string Text(short code) { return DxfStoredField.DecodeResultText((string)this.Next(code).Value); }
            internal void Marker(short code, string marker) { if ((string)this.Next(code).Value != marker) throw new FormatException(); }
        }
    }
}
