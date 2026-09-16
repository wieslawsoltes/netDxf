// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Text;

namespace netDxf.Objects
{
    /// <summary>An immutable explicit scalar result and its two FIELD display strings.</summary>
    /// <remarks>Value must be null, int, finite double or decoded string. Strings are never executed.
    /// Formatting is supplied by the evaluator, not guessed from private FIELD options.</remarks>
    public sealed class DxfFieldResult
    {
        /// <summary>Creates a result; a missing value-display string uses the explicit formatted text.</summary>
        public DxfFieldResult(object value, string formattedText, string valueDisplayText = null)
        {
            if (value != null && !(value is int) && !(value is double) && !(value is string))
                throw new ArgumentException("FIELD results require null, int, finite double or string.", nameof(value));
            if (value is double number && (double.IsNaN(number) || double.IsInfinity(number)))
                throw new ArgumentOutOfRangeException(nameof(value));
            if (value is string text) DxfStoredTableContent.CheckEditableText(text, nameof(value));
            DxfStoredTableContent.CheckEditableText(formattedText, nameof(formattedText));
            if (valueDisplayText != null) DxfStoredTableContent.CheckEditableText(valueDisplayText, nameof(valueDisplayText));
            this.Value = value; this.FormattedText = formattedText; this.ValueDisplayText = valueDisplayText ?? formattedText;
        }
        /// <summary>Gets the immutable scalar; null is an explicitly empty cached value.</summary>
        public object Value { get; }
        /// <summary>Gets the decoded group-301/group-9 text stored on the FIELD.</summary>
        public string FormattedText { get; }
        /// <summary>Gets the decoded group-302 text for modern AcValue caches; ignored for compact caches.</summary>
        public string ValueDisplayText { get; }
    }

    /// <summary>An immutable result request bound to an actual FIELD evaluation snapshot.</summary>
    public sealed class DxfFieldResultEdit
    {
        internal DxfFieldResultEdit(DxfFieldEvaluationSnapshot original, DxfFieldResult result)
        { this.Original = original; this.Result = result; }
        /// <summary>Gets the exact current snapshot required when applying the request.</summary>
        public DxfFieldEvaluationSnapshot Original { get; }
        /// <summary>Gets the explicit result.</summary>
        public DxfFieldResult Result { get; }
    }

    /// <summary>Input to one explicit host evaluator, with children already evaluated in stored order.</summary>
    /// <remarks>Field is the live identity; Evaluation and Children are immutable snapshots. The library
    /// does not execute evaluator IDs, scripts, external services, files or native FIELD code.</remarks>
    public sealed class DxfFieldEvaluationInput
    {
        internal DxfFieldEvaluationInput(DxfStoredField field, DxfFieldEvaluationSnapshot evaluation, List<DxfFieldResult> children)
        { this.Field = field; this.Evaluation = evaluation; this.Children = children.AsReadOnly(); }
        /// <summary>Gets the source FIELD identity.</summary>
        public DxfStoredField Field { get; }
        /// <summary>Gets the original evaluation snapshot.</summary>
        public DxfFieldEvaluationSnapshot Evaluation { get; }
        /// <summary>Gets detached evaluated child results, in the FIELD's stored child order.</summary>
        public IReadOnlyList<DxfFieldResult> Children { get; }
        /// <summary>Expands the qualified _text evaluator's literal text and %&lt;\_FldIdx N&gt;% child slots.</summary>
        /// <remarks>Unknown controls, malformed markers and out-of-range child indices reject. Child text
        /// is appended literally and is never reparsed, so its marker-like contents are inert.</remarks>
        public DxfFieldResult ComposeText()
        {
            if (this.Field.EvaluatorId != "_text") throw new NotSupportedException("Only the _text child-slot grammar is supported by ComposeText.");
            string code = this.Field.FieldCode;
            var output = new StringBuilder();
            const string prefix = "%<\\_FldIdx ";
            int index = 0;
            while (index < code.Length)
            {
                int marker = code.IndexOf("%<", index, StringComparison.Ordinal);
                int stop = marker < 0 ? code.Length : marker;
                string literal = code.Substring(index, stop - index);
                if (literal.IndexOf(">%", StringComparison.Ordinal) >= 0)
                    throw new FormatException("Unmatched FIELD closing delimiter.");
                Append(output, literal);
                if (marker < 0) break;
                if (code.Length - marker < prefix.Length || string.CompareOrdinal(code, marker, prefix, 0, prefix.Length) != 0)
                    throw new NotSupportedException("Only explicit child-index controls can be composed.");
                int at = marker + prefix.Length, child = 0, digits = 0;
                while (at < code.Length && code[at] >= '0' && code[at] <= '9')
                {
                    if (++digits > 6) throw new FormatException("FIELD child index exceeds its limit.");
                    child = checked(child * 10 + code[at++] - '0');
                }
                if (digits == 0 || at + 1 >= code.Length || code[at] != '>' || code[at + 1] != '%')
                    throw new FormatException("Malformed FIELD child-index control.");
                if (child >= this.Children.Count) throw new ArgumentOutOfRangeException(nameof(child));
                Append(output, this.Children[child].FormattedText);
                index = at + 2;
            }
            string result = output.ToString();
            return new DxfFieldResult(result, result);
        }
        private static void Append(StringBuilder output, string text)
        {
            if (text.Length > DxfStoredTableContent.MaximumEditedStringLength - output.Length)
                throw new ArgumentOutOfRangeException(nameof(text), "Composed FIELD text exceeds the result limit.");
            output.Append(text);
        }
    }
}
