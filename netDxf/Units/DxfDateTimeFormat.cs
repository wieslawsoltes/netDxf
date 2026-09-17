// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace netDxf.Units
{
    /// <summary>An immutable explicit date/time display mask for host-evaluated FIELD values.</summary>
    /// <remarks>No current clock, timezone conversion or system-variable lookup runs. The culture
    /// is copied and must use a Gregorian calendar. Unknown mask letters are not silently ignored.</remarks>
    public sealed class DxfDateTimeFormat
    {
        /// <summary>Maximum mask and output length in UTF-16 code units.</summary>
        public const int MaximumLength = 4096;
        private readonly List<Tuple<string, bool>> parts;
        private readonly CultureInfo culture;
        private DxfDateTimeFormat(string expression, List<Tuple<string, bool>> parts, CultureInfo culture)
        { this.Expression = expression; this.parts = parts; this.culture = culture; }
        /// <summary>Gets the original decoded mask.</summary>
        public string Expression { get; }

        /// <summary>Compiles a mask with a frozen culture, invariant by default.</summary>
        /// <remarks>Tokens: d through dddd, M through MMMM, y/yy/yyy/yyyy, h/hh, H/HH,
        /// m/mm, s/ss, t/tt. Regional controls: %c, %#c, %x, %#x and %X.
        /// Quotes enclose literals, doubled matching quotes produce a quote, and a backslash
        /// escapes the following character. Both yyy and yyyy emit a four-digit year.</remarks>
        public static DxfDateTimeFormat Parse(string expression, CultureInfo culture = null)
        {
            CheckText(expression, nameof(expression));
            var frozen = CultureInfo.ReadOnly((CultureInfo)(culture ?? CultureInfo.InvariantCulture).Clone());
            if (!(frozen.DateTimeFormat.Calendar is GregorianCalendar))
                throw new NotSupportedException("Date FIELD masks require an explicitly Gregorian culture.");
            var parts = new List<Tuple<string, bool>>();
            var literal = new StringBuilder();
            Action flush = () => { if (literal.Length != 0) { parts.Add(Tuple.Create(literal.ToString(), false)); literal.Clear(); } };
            for (int i = 0; i < expression.Length;)
            {
                char ch = expression[i++];
                if (ch == '\\')
                {
                    if (i == expression.Length) throw new FormatException("A date-mask escape requires a character.");
                    literal.Append(expression[i++]); continue;
                }
                if (ch == '\'' || ch == '"')
                {
                    if (i < expression.Length && expression[i] == ch) { literal.Append(ch); i++; continue; }
                    bool closed = false;
                    while (i < expression.Length)
                    {
                        char next = expression[i++];
                        if (next == ch)
                        {
                            if (i < expression.Length && expression[i] == ch) { literal.Append(ch); i++; }
                            else { closed = true; break; }
                        }
                        else if (next == '\\')
                        {
                            if (i == expression.Length) throw new FormatException("Unterminated date-mask escape.");
                            literal.Append(expression[i++]);
                        }
                        else literal.Append(next);
                    }
                    if (!closed) throw new FormatException("Unterminated date-mask literal.");
                    continue;
                }
                if (ch == '%')
                {
                    int start = i - 1;
                    if (i < expression.Length && expression[i] == '#') i++;
                    if (i == expression.Length) throw new FormatException("Incomplete regional date control.");
                    i++;
                    string code = expression.Substring(start, i - start);
                    if (code != "%c" && code != "%#c" && code != "%x" && code != "%#x" && code != "%X")
                        throw new NotSupportedException("Unsupported regional date control: " + code);
                    flush(); parts.Add(Tuple.Create(code, true)); continue;
                }
                if ("dMyhHmst".IndexOf(ch) >= 0)
                {
                    int start = i - 1;
                    while (i < expression.Length && expression[i] == ch) i++;
                    int count = i - start, maximum = ch == 'd' || ch == 'M' || ch == 'y' ? 4 : 2;
                    if (count > maximum) throw new NotSupportedException("Unsupported repeated date token.");
                    flush(); parts.Add(Tuple.Create(expression.Substring(start, count), true)); continue;
                }
                if (ch >= 'a' && ch <= 'z' || ch >= 'A' && ch <= 'Z')
                    throw new NotSupportedException("Unknown date-mask letters must be quoted: " + ch);
                literal.Append(ch);
            }
            flush(); return new DxfDateTimeFormat(expression, parts, frozen);
        }
        /// <summary>Formats supplied clock fields; DateTime.Kind does not trigger timezone conversion.</summary>
        public string Format(DateTime value)
        {
            var output = new StringBuilder();
            foreach (var part in this.parts)
            {
                string text = part.Item2 ? this.Token(part.Item1, value) : part.Item1;
                if (text.Length > MaximumLength - output.Length) throw new ArgumentOutOfRangeException(nameof(value), "Date display exceeds its limit.");
                output.Append(text);
            }
            string result = output.ToString(); CheckText(result, nameof(value)); return result;
        }
        /// <summary>Formats an in-range midnight-based DXF Julian clock serial.</summary>
        public string FormatJulian(double value) { return this.Format(DrawingTime.FromJulianCalendar(value)); }
        private string Token(string token, DateTime value)
        {
            var info = this.culture.DateTimeFormat;
            switch (token)
            {
                case "%c": return value.ToString(info.ShortDatePattern + " " + info.LongTimePattern, this.culture);
                case "%#c": return value.ToString(info.LongDatePattern + " " + info.LongTimePattern, this.culture);
                case "%x": return value.ToString(info.ShortDatePattern, this.culture);
                case "%#x": return value.ToString(info.LongDatePattern, this.culture);
                case "%X": return value.ToString(info.LongTimePattern, this.culture);
                case "ddd": return info.GetAbbreviatedDayName(value.DayOfWeek);
                case "dddd": return info.GetDayName(value.DayOfWeek);
                case "MMM": return info.GetAbbreviatedMonthName(value.Month);
                case "MMMM": return info.GetMonthName(value.Month);
                case "t":
                    string designator = value.Hour < 12 ? info.AMDesignator : info.PMDesignator;
                    return designator.Length == 0 ? string.Empty : StringInfo.GetNextTextElement(designator);
                case "tt": return value.Hour < 12 ? info.AMDesignator : info.PMDesignator;
            }
            int number;
            switch (token[0])
            {
                case 'd': number = value.Day; break;
                case 'M': number = value.Month; break;
                case 'y': number = token.Length < 3 ? value.Year % 100 : value.Year; break;
                case 'h': number = (value.Hour + 11) % 12 + 1; break;
                case 'H': number = value.Hour; break;
                case 'm': number = value.Minute; break;
                default: number = value.Second; break;
            }
            int width = token[0] == 'y' && token.Length >= 3 ? 4 : token.Length;
            return number.ToString(width == 1 ? "0" : new string('0', width), CultureInfo.InvariantCulture);
        }
        internal static void CheckText(string text, string name)
        {
            if (text == null) throw new ArgumentNullException(name);
            if (text.Length > MaximumLength) throw new ArgumentOutOfRangeException(name);
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (ch == '\0') throw new ArgumentException("Text cannot contain NUL.", name);
                if (char.IsSurrogate(ch) && (!char.IsHighSurrogate(ch) || i + 1 == text.Length || !char.IsLowSurrogate(text[++i])))
                    throw new ArgumentException("Text must be valid UTF-16.", name);
            }
        }
    }
}
