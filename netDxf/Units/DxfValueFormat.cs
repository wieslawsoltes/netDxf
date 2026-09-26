// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace netDxf.Units
{
    /// <summary>A compiled, immutable scalar DXF value-format expression.</summary>
    /// <remarks>
    /// Supports explicit linear modes 1–5, precision, prefix/suffix, conversion factor,
    /// decimal/thousands separators, and decimal zero suppression. It never executes fields,
    /// formulas, MText, paths or user callbacks. Unknown controls reject rather than disappear.
    /// Display is invariant-culture plain text. Native CAD qualification is separate.
    /// </remarks>
    public sealed class DxfValueFormat
    {
        /// <summary>Maximum expression and resulting display length in UTF-16 code units.</summary>
        public const int MaximumLength = 4096;
        private int mode;
        private int precision;
        private int suppression;
        private double factor = 1.0;
        private char decimalSeparator = '.';
        private char? thousandsSeparator;
        private string prefix = string.Empty;
        private string suffix = string.Empty;
        private bool numeric;

        private DxfValueFormat(string expression) { this.Expression = expression; }
        /// <summary>Gets the exact decoded input expression, not DXF-escaped text.</summary>
        public string Expression { get; }

        /// <summary>Compiles supported controls; malformed, ambiguous or unsupported expressions throw.</summary>
        /// <remarks>
        /// A numeric mode or precision requires both %lu and %pr, so document defaults are not guessed.
        /// An empty expression uses round-trip invariant numeric text or unchanged string text.
        /// %ps is literal prefix/suffix text separated by exactly one comma. Literal percent signs
        /// inside that bracket are data. Repeated controls and nested brackets reject.
        /// </remarks>
        public static DxfValueFormat Parse(string expression)
        {
            CheckText(expression, nameof(expression));
            var result = new DxfValueFormat(expression);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            int i = 0;
            while (i < expression.Length)
            {
                if (i + 3 > expression.Length || expression[i++] != '%')
                    throw new FormatException("A value-format control must start with percent and two letters.");
                string code = expression.Substring(i, 2); i += 2;
                if (!seen.Add(code)) throw new FormatException("Repeated value-format control: " + code);
                if (code == "ps")
                {
                    string pair = ReadBracket(expression, ref i);
                    int comma = pair.IndexOf(',');
                    if (comma < 0 || comma != pair.LastIndexOf(','))
                        throw new FormatException("Prefix/suffix requires exactly one comma.");
                    result.prefix = pair.Substring(0, comma); result.suffix = pair.Substring(comma + 1);
                    continue;
                }
                if (code != "lu" && code != "pr" && code != "ct" && code != "ds" && code != "th" && code != "zs")
                    throw new NotSupportedException("Unsupported value-format control: " + code);
                int start = i;
                while (i < expression.Length && expression[i] >= '0' && expression[i] <= '9') i++;
                if (start == i || i - start > 3 || !int.TryParse(expression.Substring(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                    throw new FormatException("A bounded unsigned integer must follow " + code + ".");
                result.numeric = true;
                switch (code)
                {
                    case "lu":
                        if (value < 1 || value > 5) throw new NotSupportedException("Only explicit linear modes 1 through 5 are supported.");
                        result.mode = value; break;
                    case "pr":
                        if (value > 8) throw new NotSupportedException("Precision must be in the range 0 through 8.");
                        result.precision = value; break;
                    case "ct":
                        if (value != 8) throw new NotSupportedException("Only explicit conversion factor mode 8 is supported.");
                        string scale = ReadBracket(expression, ref i);
                        if (scale.Trim() != scale || !netDxf.IO.DxfDoubleParser.TryParse(scale, out double factor)) throw new FormatException("Conversion factor must be a finite invariant number.");
                        result.factor = factor; break;
                    case "ds":
                        if (value != 44 && value != 46) throw new NotSupportedException("Decimal separator must be comma or period.");
                        result.decimalSeparator = (char)value; break;
                    case "th":
                        if (value != 0 && value != 32 && value != 44 && value != 46) throw new NotSupportedException("Unsupported thousands separator.");
                        result.thousandsSeparator = value == 0 ? (char?)null : (char)value; break;
                    case "zs":
                        if (value != 0 && value != 4 && value != 8 && value != 12) throw new NotSupportedException("Only decimal leading/trailing zero suppression is supported.");
                        result.suppression = value; break;
                }
            }
            if (seen.Contains("lu") != seen.Contains("pr")) throw new NotSupportedException("Specify both linear mode and precision; drawing defaults are not inferred.");
            if (result.numeric && !seen.Contains("lu")) throw new NotSupportedException("Numeric controls require explicit linear mode and precision.");
            if (result.thousandsSeparator == result.decimalSeparator) throw new FormatException("Decimal and thousands separators must differ.");
            if ((result.mode == 4 || result.mode == 5) && (seen.Contains("ds") || seen.Contains("th") || seen.Contains("zs")))
                throw new NotSupportedException("Decimal separators and zero suppression do not apply to fractional output.");
            return result;
        }

        /// <summary>Attempts compilation without masking errors unrelated to expression validation.</summary>
        public static bool TryParse(string expression, out DxfValueFormat format, out string diagnostic)
        {
            try { format = Parse(expression); diagnostic = null; return true; }
            catch (Exception error) when (error is ArgumentException || error is FormatException || error is NotSupportedException)
            { format = null; diagnostic = error.Message; return false; }
        }

        /// <summary>Formats an int, finite double, or string using this expression without changing any document.</summary>
        /// <param name="value">An exact supported CLR scalar type; no arbitrary conversion callbacks run.</param>
        /// <param name="storedUnitType">Unitless, distance, area, volume, currency or percentage (0,1,4,8,16,32). Angles and unknown unit codes reject.</param>
        /// <remarks>
        /// Percentage does not implicitly multiply by 100. A conversion factor is applied once as an
        /// IEEE double operation. Fixed/fractional rounding then uses exact binary rational arithmetic
        /// with midpoint ties away from zero. No font-dependent fraction stacking is inserted.
        /// </remarks>
        public string Format(object value, int storedUnitType = 0)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (storedUnitType != 0 && storedUnitType != 1 && storedUnitType != 4 && storedUnitType != 8 && storedUnitType != 16 && storedUnitType != 32)
                throw new NotSupportedException("Angular or unknown unit semantics require a different evaluator.");
            string text;
            if (value is string literal)
            {
                CheckText(literal, nameof(value));
                if (this.numeric || storedUnitType != 0) throw new NotSupportedException("Numeric formatting cannot coerce string data.");
                text = literal;
            }
            else if (value is int || value is double)
            {
                double number = value is int integer ? integer : (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentOutOfRangeException(nameof(value));
                number *= this.factor;
                if (double.IsNaN(number) || double.IsInfinity(number)) throw new OverflowException("The scaled value is not finite.");
                if (this.mode == 0)
                    text = number == 0 ? "0" : number.ToString("R", CultureInfo.InvariantCulture);
                else
                    text = this.FormatNumber(number);
            }
            else throw new NotSupportedException("The value must be an int, double or string; fields and other scalar kinds are not evaluated.");
            if ((long)this.prefix.Length + text.Length + this.suffix.Length > MaximumLength)
                throw new ArgumentOutOfRangeException(nameof(value), "Formatted text exceeds the display limit.");
            return this.prefix + text + this.suffix;
        }

        private string FormatNumber(double value)
        {
            bool negative = value < 0;
            Ratio(Math.Abs(value), out BigInteger n, out BigInteger d);
            BigInteger scale = BigInteger.Pow(10, this.precision);
            string text;
            BigInteger rounded;
            if (this.mode == 1)
            {
                int exponent = value == 0 ? 0 : (int)Math.Floor(Math.Log10(Math.Abs(value)));
                while (ComparePower(n, d, exponent) < 0 && !n.IsZero) exponent--;
                while (ComparePower(n, d, exponent + 1) >= 0 && !n.IsZero) exponent++;
                int power = this.precision - exponent;
                rounded = power >= 0 ? Round(n * BigInteger.Pow(10, power), d) : Round(n, d * BigInteger.Pow(10, -power));
                if (rounded >= scale * 10) { rounded /= 10; exponent++; }
                text = this.DecimalText(rounded, this.precision, false) + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
            }
            else if (this.mode == 2)
            {
                rounded = Round(n * scale, d);
                text = this.DecimalText(rounded, this.precision, true);
            }
            else if (this.mode == 3)
            {
                rounded = Round(n * scale, d);
                BigInteger feet = BigInteger.DivRem(rounded, 12 * scale, out BigInteger inches);
                text = this.DecimalText(feet, 0, true) + "'-" + this.DecimalText(inches, this.precision, false) + "\"";
            }
            else
            {
                BigInteger denominator = BigInteger.One << this.precision;
                rounded = Round(n * denominator, d);
                BigInteger whole = BigInteger.DivRem(rounded, denominator, out BigInteger numerator);
                string fraction = string.Empty;
                if (!numerator.IsZero)
                {
                    BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
                    fraction = (numerator / gcd).ToString(CultureInfo.InvariantCulture) + "/" + (denominator / gcd).ToString(CultureInfo.InvariantCulture);
                }
                if (this.mode == 4)
                {
                    BigInteger feet = BigInteger.DivRem(whole, 12, out BigInteger inches);
                    text = feet.ToString(CultureInfo.InvariantCulture) + "'-" + inches.ToString(CultureInfo.InvariantCulture) + (fraction.Length == 0 ? "" : " " + fraction) + "\"";
                }
                else text = whole.IsZero && fraction.Length != 0 ? fraction : whole.ToString(CultureInfo.InvariantCulture) + (fraction.Length == 0 ? "" : " " + fraction);
            }
            return (negative && !rounded.IsZero ? "-" : "") + text;
        }

        private string DecimalText(BigInteger number, int places, bool group)
        {
            string digits = number.ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
            string whole = digits.Substring(0, digits.Length - places);
            string fraction = places == 0 ? string.Empty : digits.Substring(digits.Length - places);
            if ((this.suppression & 8) != 0) fraction = fraction.TrimEnd('0');
            if ((this.suppression & 4) != 0 && whole == "0" && fraction.Length != 0) whole = string.Empty;
            if (group && this.thousandsSeparator.HasValue && whole.Length > 3)
            {
                var grouped = new StringBuilder();
                for (int i = 0; i < whole.Length; i++)
                {
                    if (i > 0 && (whole.Length - i) % 3 == 0) grouped.Append(this.thousandsSeparator.Value);
                    grouped.Append(whole[i]);
                }
                whole = grouped.ToString();
            }
            return whole + (fraction.Length == 0 ? "" : this.decimalSeparator + fraction);
        }

        private static BigInteger Round(BigInteger numerator, BigInteger denominator)
        {
            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            return remainder * 2 >= denominator ? quotient + 1 : quotient;
        }

        private static int ComparePower(BigInteger numerator, BigInteger denominator, int power)
        {
            return power >= 0 ? numerator.CompareTo(denominator * BigInteger.Pow(10, power)) : (numerator * BigInteger.Pow(10, -power)).CompareTo(denominator);
        }

        private static void Ratio(double value, out BigInteger numerator, out BigInteger denominator)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            long mantissa = bits & 0xfffffffffffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            int shift = exponent == 0 ? -1074 : exponent - 1075;
            numerator = new BigInteger(mantissa); denominator = BigInteger.One;
            if (shift >= 0) numerator <<= shift; else denominator <<= -shift;
        }

        private static string ReadBracket(string text, ref int index)
        {
            if (index == text.Length || text[index++] != '[') throw new FormatException("Expected bracketed format argument.");
            int start = index;
            while (index < text.Length && text[index] != ']')
            {
                if (text[index] == '[') throw new FormatException("Nested format arguments are not supported.");
                index++;
            }
            if (index == text.Length) throw new FormatException("Unterminated format argument.");
            string result = text.Substring(start, index - start); index++; return result;
        }

        private static void CheckText(string text, string parameter)
        {
            if (text == null) throw new ArgumentNullException(parameter);
            if (text.Length > MaximumLength) throw new ArgumentOutOfRangeException(parameter);
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (value == '\0') throw new ArgumentException("Text contains NUL.", parameter);
                if (!char.IsSurrogate(value)) continue;
                if (!char.IsHighSurrogate(value) || ++i == text.Length || !char.IsLowSurrogate(text[i]))
                    throw new ArgumentException("Text contains an unpaired surrogate.", parameter);
            }
        }
    }
}
