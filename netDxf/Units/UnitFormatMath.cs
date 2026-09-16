// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;
using System.Numerics;

namespace netDxf.Units
{
    // Exact binary64 rounding for the legacy unit utilities. Their historical
    // midpoint policy is ToEven, unlike DxfValueFormat's explicit AwayFromZero.
    internal static class UnitFormatMath
    {
        internal static void CheckFinite(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameter, "The value must be finite.");
        }

        internal static int Validate(double value, UnitStyleFormat format, bool angular)
        {
            if (format == null) throw new ArgumentNullException(nameof(format));
            CheckFinite(value, nameof(value));
            int places = angular ? format.AngularDecimalPlaces : format.LinearDecimalPlaces;
            if (places < 0 || places > 8)
                throw new ArgumentOutOfRangeException(nameof(format), "DXF display precision must be between zero and eight.");
            if (string.IsNullOrEmpty(format.DecimalSeparator))
                throw new ArgumentException("A nonempty decimal separator is required.", nameof(format));
            return places;
        }

        internal static BigInteger RoundMagnitude(double value, BigInteger scale)
        {
            long bits = BitConverter.DoubleToInt64Bits(Math.Abs(value));
            int exponent = (int)((bits >> 52) & 0x7ff);
            long mantissa = bits & 0xfffffffffffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            int shift = exponent == 0 ? -1074 : exponent - 1075;
            BigInteger numerator = new BigInteger(mantissa) * scale;
            BigInteger denominator = BigInteger.One;
            if (shift >= 0) numerator <<= shift; else denominator <<= -shift;
            BigInteger whole = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            int midpoint = (remainder * 2).CompareTo(denominator);
            return midpoint > 0 || midpoint == 0 && !whole.IsEven ? whole + 1 : whole;
        }

        internal static string Decimal(BigInteger value, int places, string separator,
            bool suppressLeading = false, bool suppressTrailing = false)
        {
            string digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(places + 1, '0');
            string whole = digits.Substring(0, digits.Length - places);
            string fraction = places == 0 ? string.Empty : digits.Substring(digits.Length - places);
            if (suppressTrailing) fraction = fraction.TrimEnd('0');
            if (suppressLeading && whole == "0" && fraction.Length > 0) whole = string.Empty;
            return whole + (fraction.Length == 0 ? string.Empty : separator + fraction);
        }

        internal static string Fixed(double value, int places, string separator,
            bool suppressLeading = false, bool suppressTrailing = false)
        {
            CheckFinite(value, nameof(value));
            BigInteger rounded = RoundMagnitude(value, BigInteger.Pow(10, places));
            return (value < 0 && !rounded.IsZero ? "-" : string.Empty)
                + Decimal(rounded, places, separator, suppressLeading, suppressTrailing);
        }
    }
}
