// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using netDxf.Tables;
using netDxf.Units;

namespace netDxf.Objects
{
    /// <summary>An opt-in evaluator for explicit AcVar bindings, bounded numeric AcExpr and _text composition.</summary>
    /// <remarks>No scripts, reflection, files, implicit current clock, object-ID lookup or native
    /// evaluator runs. Numeric expressions use child scalar values, never their display strings.
    /// Variable memberships and culture are copied. Use the existing FIELD transactions to publish.</remarks>
    public sealed class DxfStandardFieldEvaluator
    {
        /// <summary>Maximum explicit variable bindings.</summary>
        public const int MaximumVariables = 4096;
        private readonly Dictionary<string, DxfFieldVariable> variables;
        private readonly CultureInfo culture;
        /// <summary>Copies distinct case-sensitive bindings; null supplies an empty set.</summary>
        /// <remarks>Bounded enumeration and disposal finish before a provider is returned.</remarks>
        public DxfStandardFieldEvaluator(IEnumerable<KeyValuePair<string, DxfFieldVariable>> variables = null, CultureInfo culture = null)
        {
            var copied = new Dictionary<string, DxfFieldVariable>(StringComparer.Ordinal);
            long characters = 0;
            if (variables != null)
                foreach (var pair in variables)
                {
                    ValidateName(pair.Key);
                    if (pair.Value == null) throw new ArgumentException("A FIELD variable binding cannot be null.", nameof(variables));
                    if (copied.Count == MaximumVariables || copied.ContainsKey(pair.Key))
                        throw new ArgumentException("Variable names must be distinct and within their limit.", nameof(variables));
                    characters += pair.Key.Length + ((pair.Value.Value as string)?.Length ?? 0);
                    if (characters > DxfObjectDatabase.MaximumFieldResultCharacters)
                        throw new ArgumentException("Combined FIELD variable text exceeds its limit.", nameof(variables));
                    copied.Add(pair.Key, pair.Value);
                }
            this.variables = copied;
            this.culture = CultureInfo.ReadOnly((CultureInfo)(culture ?? CultureInfo.InvariantCulture).Clone());
        }
        /// <summary>Evaluates supported code and records expected failures as retained-cache outcomes.</summary>
        /// <remarks>Unknown evaluators produce EvaluatorNotFound; unsupported code/format produces
        /// InvalidCode; missing bindings produce InvalidContext. Unexpected runtime exceptions are
        /// not swallowed. A failed child cannot silently become a successful numeric operand.</remarks>
        public DxfFieldResult Evaluate(DxfFieldEvaluationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            string id = input.Field.EvaluatorId;
            if (id != "_text" && id != "AcVar" && id != "AcExpr")
                return DxfFieldResult.Failure(input.Evaluation, DxfFieldResultStatus.EvaluatorNotFound, 1, "No bounded standard evaluator for this FIELD ID.");
            try { return this.EvaluateOrThrow(input); }
            catch (FormatException error) { return Failure(input, DxfFieldResultStatus.SyntaxError, 2, error); }
            catch (NotSupportedException error) { return Failure(input, DxfFieldResultStatus.InvalidCode, 3, error); }
            catch (KeyNotFoundException error) { return Failure(input, DxfFieldResultStatus.InvalidContext, 4, error); }
            catch (ArithmeticException error) { return Failure(input, DxfFieldResultStatus.OtherError, 5, error); }
            catch (ArgumentException error) { return Failure(input, DxfFieldResultStatus.InvalidCode, 6, error); }
            catch (InvalidOperationException error) { return Failure(input, DxfFieldResultStatus.OtherError, 7, error); }
        }
        private static DxfFieldResult Failure(DxfFieldEvaluationInput input, DxfFieldResultStatus status, int code, Exception error)
        {
            // Runtime parameter diagnostics can contain newlines; keep provider-produced
            // errors portable to ASCII DXF without changing explicit host failure APIs.
            string message = error.Message.Replace("\r", " ").Replace("\n", " ");
            if (message.Length > DxfDateTimeFormat.MaximumLength)
            {
                int length = DxfDateTimeFormat.MaximumLength;
                if (char.IsHighSurrogate(message[length - 1])) length--;
                message = message.Substring(0, length);
            }
            return DxfFieldResult.Failure(input.Evaluation, status, code, message);
        }
        /// <summary>Evaluates supported code, throwing on failure so the enclosing transaction can abort all selected trees.</summary>
        public DxfFieldResult EvaluateOrThrow(DxfFieldEvaluationInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (input.Field.EvaluatorId == "_text") return input.ComposeText();
            string id = input.Field.EvaluatorId;
            if (id != "AcVar" && id != "AcExpr") throw new NotSupportedException("Unknown standard FIELD evaluator.");
            string code = input.Field.FieldCode;
            DxfDateTimeFormat.CheckText(code, nameof(input)); code = Trim(code);
            if (code.StartsWith("%<", StringComparison.Ordinal))
            {
                if (!code.EndsWith(">%", StringComparison.Ordinal)) throw new FormatException("Unclosed FIELD wrapper.");
                code = Trim(code.Substring(2, code.Length - 4));
            }
            string prefix = "\\" + id;
            if (!code.StartsWith(prefix, StringComparison.Ordinal) || code.Length == prefix.Length || !Space(code[prefix.Length]))
                throw new FormatException("FIELD evaluator ID and command must agree.");
            string argument = Trim(code.Substring(prefix.Length)), format = null;
            SplitFormat(ref argument, ref format);
            // Code controls are authoritative. Without them, use the retained cache format;
            // neither choice rewrites code or cache-format metadata.
            format = format ?? input.Evaluation.FormatString ?? string.Empty;
            if (id == "AcVar")
            {
                ValidateName(argument);
                if (!this.variables.TryGetValue(argument, out DxfFieldVariable variable))
                    throw new KeyNotFoundException("No explicit FIELD variable: " + argument);
                string text;
                if (variable.Clock.HasValue)
                {
                    if (format.Length == 0) throw new NotSupportedException("Date variables require an explicit date mask.");
                    text = DxfDateTimeFormat.Parse(format, this.culture).Format(variable.Clock.Value);
                }
                else if (variable.Value == null)
                {
                    if (format.Length != 0) throw new NotSupportedException("An empty variable cannot be implicitly formatted.");
                    text = string.Empty;
                }
                else if (variable.StoredUnitType == 2)
                    text = DxfAngularValueFormat.Parse(format).FormatRadians(DxfTableFormula.Number(variable.Value));
                else text = DxfValueFormat.Parse(format).Format(variable.Value, variable.StoredUnitType);
                return new DxfFieldResult(variable.Value, text);
            }
            string expression = NumericChildren(argument, input.Children);
            double value = DxfTableFormula.ParseScalar("=" + expression).EvaluateScalar();
            int units = input.Evaluation.StoredUnitType ?? 0;
            string display = units == 2 ? DxfAngularValueFormat.Parse(format).FormatRadians(value)
                : DxfValueFormat.Parse(format).Format(value, units);
            return new DxfFieldResult(value, display);
        }
        private static void SplitFormat(ref string expression, ref string format)
        {
            for (int i = 0; i < expression.Length; i++)
            {
                if (i + 1 < expression.Length && expression[i] == '%' && expression[i + 1] == '<')
                {
                    int end = expression.IndexOf(">%", i + 2, StringComparison.Ordinal);
                    if (end < 0) throw new FormatException("Unclosed nested FIELD slot.");
                    i = end + 1; continue;
                }
                if (expression[i] != '\\' || i + 1 == expression.Length || expression[i + 1] != 'f') continue;
                if (i == 0 || !Space(expression[i - 1])) throw new FormatException("Format switch requires a separator.");
                int at = i + 2;
                if (at == expression.Length || !Space(expression[at])) throw new FormatException("Format switch requires quoted controls.");
                while (at < expression.Length && Space(expression[at])) at++;
                if (at == expression.Length || expression[at++] != '"') throw new FormatException("Expected quoted FIELD format.");
                int start = at;
                while (at < expression.Length && expression[at] != '"') at++;
                if (at == expression.Length) throw new FormatException("Unclosed FIELD format string.");
                format = expression.Substring(start, at - start);
                if (Trim(expression.Substring(at + 1)).Length != 0) throw new FormatException("Trailing or repeated FIELD format switch.");
                expression = Trim(expression.Substring(0, i)); return;
            }
        }
        private static string NumericChildren(string expression, IReadOnlyList<DxfFieldResult> children)
        {
            var output = new StringBuilder(); const string prefix = "%<\\_FldIdx ";
            for (int i = 0; i < expression.Length;)
            {
                if (expression[i] != '%') { Append(output, expression[i++].ToString()); continue; }
                if (expression.Length - i < prefix.Length || string.CompareOrdinal(expression, i, prefix, 0, prefix.Length) != 0)
                    throw new NotSupportedException("Only explicit owned child-index slots are supported in numeric FIELD expressions.");
                i += prefix.Length; int index = 0, digits = 0;
                while (i < expression.Length && expression[i] >= '0' && expression[i] <= '9')
                { if (++digits > 6) throw new FormatException("Child index is too long."); index = checked(index * 10 + expression[i++] - '0'); }
                if (digits == 0 || i + 1 >= expression.Length || expression[i] != '>' || expression[i + 1] != '%')
                    throw new FormatException("Malformed FIELD child index.");
                i += 2;
                if (index >= children.Count) throw new ArgumentOutOfRangeException(nameof(children), "FIELD child index is outside the owned slots.");
                var child = children[index];
                if (child.Status != DxfFieldResultStatus.Success) throw new InvalidOperationException("A numeric FIELD dependency failed.");
                double number = DxfTableFormula.Number(child.Value);
                Append(output, "(" + number.ToString("R", CultureInfo.InvariantCulture) + ")");
            }
            return output.ToString();
        }
        private static void Append(StringBuilder output, string text)
        {
            if (text.Length > DxfTableFormula.MaximumLength - 1 - output.Length)
                throw new ArgumentOutOfRangeException(nameof(text), "Expanded numeric FIELD expression exceeds its budget.");
            output.Append(text);
        }
        private static bool Space(char value) { return value == ' ' || value == '\t'; }
        private static string Trim(string value) { return value.Trim(' ', '\t'); }
        private static void ValidateName(string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));
            if (name.Length == 0 || name.Length > 128) throw new ArgumentException("FIELD variable names require 1–128 ASCII characters.", nameof(name));
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (!(ch >= 'A' && ch <= 'Z') && !(ch >= 'a' && ch <= 'z') && ch != '_' && !(i > 0 && ch >= '0' && ch <= '9'))
                    throw new ArgumentException("Unsupported FIELD variable name.", nameof(name));
            }
        }
    }
}
