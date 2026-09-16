#region netDxf library licensed under the MIT License
// 
//                       netDxf library
// Copyright (c) Daniel Carvajal (haplokuon@gmail.com)
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// 
#endregion

using System;
using System.Globalization;
using System.Numerics;

namespace netDxf.Units
{
    /// <summary>Formats finite lengths with explicit DXF precision and unit symbols.</summary>
    /// <remarks>Engineering and architectural values are in inches. Precision is 0–8.
    /// Fixed/fractional output rounds exact binary64 input once, with midpoint ties to even.</remarks>
    public static class LinearUnitFormat
    {
        /// <summary>Formats a finite length using scientific notation.</summary>
        public static string ToScientific(double length, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(length, format, false);
            string pattern = (format.SuppressLinearLeadingZeros ? "#" : "0") + "."
                + new string(format.SuppressLinearTrailingZeros ? '#' : '0', places) + "E+00";
            return length.ToString(pattern, new NumberFormatInfo { NumberDecimalSeparator = format.DecimalSeparator });
        }

        /// <summary>Formats a finite length as a fixed decimal value.</summary>
        public static string ToDecimal(double length, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(length, format, false);
            return UnitFormatMath.Fixed(length, places, format.DecimalSeparator,
                format.SuppressLinearLeadingZeros, format.SuppressLinearTrailingZeros);
        }

        /// <summary>Formats inches as feet and decimal inches, carrying rounded inches into feet.</summary>
        public static string ToEngineering(double length, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(length, format, false);
            BigInteger scale = BigInteger.Pow(10, places);
            BigInteger rounded = UnitFormatMath.RoundMagnitude(length, scale);
            BigInteger feet = BigInteger.DivRem(rounded, 12 * scale, out BigInteger inches);
            string text = UnitFormatMath.Decimal(inches, places, format.DecimalSeparator,
                format.SuppressLinearLeadingZeros, format.SuppressLinearTrailingZeros);
            return Sign(length, rounded) + FeetAndInches(feet, text, inches.IsZero, format);
        }

        /// <summary>Formats inches as feet and fractional inches, with normalized fractions and carries.</summary>
        public static string ToArchitectural(double length, UnitStyleFormat format)
        {
            int places = ValidateFraction(length, format);
            BigInteger denominator = BigInteger.One << places;
            BigInteger rounded = UnitFormatMath.RoundMagnitude(length, denominator);
            BigInteger feet = BigInteger.DivRem(rounded, 12 * denominator, out BigInteger inchUnits);
            BigInteger whole = BigInteger.DivRem(inchUnits, denominator, out BigInteger numerator);
            string inches = FractionText(whole, numerator, denominator, format);
            string text = Sign(length, rounded) + FeetAndInches(feet, inches, inchUnits.IsZero, format);
            return !numerator.IsZero && format.FractionType != FractionFormatType.NotStacked ? "\\A1;" + text : text;
        }

        /// <summary>Formats a length as a whole number and a reduced fraction.</summary>
        public static string ToFractional(double length, UnitStyleFormat format)
        {
            int places = ValidateFraction(length, format);
            BigInteger denominator = BigInteger.One << places;
            BigInteger rounded = UnitFormatMath.RoundMagnitude(length, denominator);
            BigInteger whole = BigInteger.DivRem(rounded, denominator, out BigInteger numerator);
            string text = Sign(length, rounded) + FractionText(whole, numerator, denominator, format);
            return !numerator.IsZero && format.FractionType != FractionFormatType.NotStacked ? "\\A1;" + text : text;
        }

        private static int ValidateFraction(double length, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(length, format, false);
            if (format.FractionType != FractionFormatType.NotStacked && format.FractionType != FractionFormatType.Horizontal && format.FractionType != FractionFormatType.Diagonal)
                throw new ArgumentOutOfRangeException(nameof(format), "Unknown fraction style.");
            if (double.IsNaN(format.FractionHeightScale) || double.IsInfinity(format.FractionHeightScale) || format.FractionHeightScale <= 0)
                throw new ArgumentOutOfRangeException(nameof(format), "The fraction height scale must be finite and positive.");
            return places;
        }

        private static string Sign(double value, BigInteger rounded)
        { return value < 0 && !rounded.IsZero ? "-" : string.Empty; }

        private static string FractionText(BigInteger whole, BigInteger numerator, BigInteger denominator, UnitStyleFormat format)
        {
            string text = whole.ToString(CultureInfo.InvariantCulture);
            if (numerator.IsZero) return text;
            BigInteger gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
            string n = (numerator / gcd).ToString(CultureInfo.InvariantCulture);
            string d = (denominator / gcd).ToString(CultureInfo.InvariantCulture);
            if (format.FractionType == FractionFormatType.NotStacked) return text + " " + n + "/" + d;
            string separator = format.FractionType == FractionFormatType.Diagonal ? "#" : "/";
            return text + "{\\H" + format.FractionHeightScale.ToString("R", CultureInfo.InvariantCulture)
                + "x;\\S" + n + separator + d + ";}";
        }

        private static string FeetAndInches(BigInteger feet, string inches, bool zeroInches, UnitStyleFormat format)
        {
            if (feet.IsZero && format.SuppressZeroFeet) return inches + format.InchesSymbol;
            string text = feet.ToString(CultureInfo.InvariantCulture) + format.FeetSymbol;
            if (zeroInches && format.SuppressZeroInches) return text;
            return text + format.FeetInchesSeparator + inches + format.InchesSymbol;
        }
    }
}
