// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace netDxf.Units
{
    /// <summary>An immutable explicit angular value-format expression for radians.</summary>
    /// <remarks>Supports %au0–4, %pr0–8, %ds44/46, decimal %zs0/4/8/12 and %ps[prefix,suffix].
    /// Explicit mode and precision are required. Rounding uses the angular utility's exact
    /// binary64 ties-to-even policy, not the linear value formatter's ties-away policy.</remarks>
    public sealed class DxfAngularValueFormat
    {
        private int mode, precision, suppression;
        private string separator = ".", prefix = string.Empty, suffix = string.Empty;
        private DxfAngularValueFormat(string expression) { this.Expression = expression; }
        /// <summary>Gets the original decoded format.</summary>
        public string Expression { get; }
        /// <summary>Compiles explicit mode and precision; duplicate, ambiguous or unsupported controls reject.</summary>
        public static DxfAngularValueFormat Parse(string expression)
        {
            DxfDateTimeFormat.CheckText(expression, nameof(expression));
            var result = new DxfAngularValueFormat(expression);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < expression.Length;)
            {
                if (i + 3 > expression.Length || expression[i++] != '%') throw new FormatException("Expected angular format control.");
                string code = expression.Substring(i, 2); i += 2;
                if (!seen.Add(code)) throw new FormatException("Repeated angular format control: " + code);
                if (code == "ps")
                {
                    if (i == expression.Length || expression[i++] != '[') throw new FormatException("Expected prefix/suffix bracket.");
                    int start = i;
                    while (i < expression.Length && expression[i] != ']')
                    { if (expression[i++] == '[') throw new FormatException("Nested prefix/suffix brackets are not supported."); }
                    if (i == expression.Length) throw new FormatException("Unclosed prefix/suffix bracket.");
                    string pair = expression.Substring(start, i++ - start); int comma = pair.IndexOf(',');
                    if (comma < 0 || comma != pair.LastIndexOf(',')) throw new FormatException("Prefix/suffix requires one comma.");
                    result.prefix = pair.Substring(0, comma); result.suffix = pair.Substring(comma + 1); continue;
                }
                if (code != "au" && code != "pr" && code != "ds" && code != "zs") throw new NotSupportedException("Unsupported angular control: " + code);
                int first = i;
                while (i < expression.Length && expression[i] >= '0' && expression[i] <= '9') i++;
                if (i == first || i - first > 3) throw new FormatException("Expected bounded unsigned format value.");
                int value = int.Parse(expression.Substring(first, i - first), CultureInfo.InvariantCulture);
                switch (code)
                {
                    case "au": if (value > 4) throw new NotSupportedException("Drawing-default angular units are not inferred."); result.mode = value; break;
                    case "pr": if (value > 8) throw new NotSupportedException("Angular precision must be 0 through 8."); result.precision = value; break;
                    case "ds": if (value != 44 && value != 46) throw new NotSupportedException("Unsupported decimal separator."); result.separator = ((char)value).ToString(); break;
                    default: if (value != 0 && value != 4 && value != 8 && value != 12) throw new NotSupportedException("Unsupported angular zero suppression."); result.suppression = value; break;
                }
            }
            if (!seen.Contains("au") || !seen.Contains("pr")) throw new NotSupportedException("Explicit angular units and precision are required.");
            if ((result.mode == 1 || result.mode == 4) && result.suppression != 0)
                throw new NotSupportedException("DMS and bearing zero-suppression controls require separate semantics.");
            return result;
        }
        /// <summary>Formats finite radians without inferring drawing units, ANGBASE or ANGDIR.</summary>
        /// <remarks>Decimal degrees/gradians/radians have no unit suffix. DMS uses a Unicode degree
        /// sign and ASCII prime marks. Bearings use the existing deterministic N/S/E/W convention.</remarks>
        public string FormatRadians(double radians)
        {
            UnitFormatMath.Finite(radians, nameof(radians));
            var format = new UnitStyleFormat {
                AngularDecimalPlaces = (short)this.precision, DecimalSeparator = this.separator,
                DegreesSymbol = "°", MinutesSymbol = "'", SecondsSymbol = "\"",
                SuppressAngularLeadingZeros = (this.suppression & 4) != 0,
                SuppressAngularTrailingZeros = (this.suppression & 8) != 0
            };
            string text;
            if (this.mode == 3)
                text = UnitFormatMath.Fixed(radians, this.precision, format, true);
            else
            {
                double value = (this.mode == 4 ? radians % (2 * Math.PI) : radians) * MathHelper.RadToDeg;
                UnitFormatMath.Finite(value, nameof(radians));
                if (this.mode == 1 || this.mode == 4)
                    text = AngleUnitFormat.Format(value, this.mode == 1 ? AngleUnitType.DegreesMinutesSeconds : AngleUnitType.SurveyorUnits, format);
                else
                {
                    if (this.mode == 2) value = radians * (200 / Math.PI);
                    text = UnitFormatMath.Fixed(value, this.precision, format, true);
                }
            }
            if ((long)this.prefix.Length + text.Length + this.suffix.Length > DxfDateTimeFormat.MaximumLength)
                throw new ArgumentOutOfRangeException(nameof(radians), "Angular display exceeds its limit.");
            return this.prefix + text + this.suffix;
        }
    }
}
