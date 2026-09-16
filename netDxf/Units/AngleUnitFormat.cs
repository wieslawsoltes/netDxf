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
    /// <summary>Formats finite angles supplied in degrees using explicit unit settings.</summary>
    /// <remarks>Precision is 0–8. Rounding uses exact binary64 values with midpoint ties to even.
    /// Signed non-surveyor angles are not normalized unless explicitly requested.</remarks>
    public static class AngleUnitFormat
    {
        /// <summary>Formats an angle in degrees using any declared angular unit type.</summary>
        /// <param name="angle">Finite angle in degrees, not radians.</param>
        /// <param name="units">The requested angular unit type.</param>
        /// <param name="format">Explicit precision, symbols and zero-suppression settings.</param>
        /// <param name="normalize">Reduce the input to [0,360) before rounding. Surveyor bearings always normalize.</param>
        public static string Format(double angle, AngleUnitType units, UnitStyleFormat format, bool normalize = false)
        {
            UnitFormatMath.Validate(angle, format, true);
            if (normalize) angle = Normalize(angle);
            switch (units)
            {
                case AngleUnitType.DecimalDegrees: return ToDecimal(angle, format);
                case AngleUnitType.DegreesMinutesSeconds: return ToDegreesMinutesSeconds(angle, format);
                case AngleUnitType.Gradians: return ToGradians(angle, format);
                case AngleUnitType.Radians: return ToRadians(angle, format);
                case AngleUnitType.SurveyorUnits: return ToSurveyor(angle, format);
                default: throw new ArgumentOutOfRangeException(nameof(units));
            }
        }

        /// <summary>Converts an angle in degrees to decimal degrees with the configured symbol.</summary>
        public static string ToDecimal(double angle, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(angle, format, true);
            return UnitFormatMath.Fixed(angle, places, format.DecimalSeparator,
                format.SuppressAngularLeadingZeros, format.SuppressAngularTrailingZeros) + format.DegreesSymbol;
        }

        /// <summary>Converts an angle in degrees to degrees, minutes and seconds.</summary>
        /// <remarks>Precision 0 rounds degrees; 1–2 rounds minutes; 3–4 rounds seconds;
        /// 5–8 adds 1–4 fractional-second digits. Rounding occurs before decomposition,
        /// so seconds and minutes never become 60. A single sign applies to the whole angle.</remarks>
        public static string ToDegreesMinutesSeconds(double angle, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(angle, format, true);
            int decimals = Math.Max(0, places - 4);
            BigInteger scale = BigInteger.Pow(10, decimals);
            BigInteger perDegree = places == 0 ? BigInteger.One : places <= 2 ? new BigInteger(60) : 3600 * scale;
            BigInteger rounded = UnitFormatMath.RoundMagnitude(angle, perDegree);
            BigInteger degrees = BigInteger.DivRem(rounded, perDegree, out BigInteger remainder);
            string text = degrees.ToString(CultureInfo.InvariantCulture) + format.DegreesSymbol;
            if (places > 0)
            {
                BigInteger minutes = places <= 2 ? remainder : BigInteger.DivRem(remainder, 60 * scale, out remainder);
                text += minutes.ToString(CultureInfo.InvariantCulture) + format.MinutesSymbol;
                if (places > 2) text += UnitFormatMath.Decimal(remainder, decimals, format.DecimalSeparator) + format.SecondsSymbol;
            }
            return (angle < 0 && !rounded.IsZero ? "-" : string.Empty) + text;
        }

        /// <summary>Converts an angle in degrees to gradians.</summary>
        public static string ToGradians(double angle, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(angle, format, true);
            double converted = angle * MathHelper.DegToGrad;
            if (double.IsInfinity(converted)) throw new OverflowException("The gradian angle is outside binary64 range.");
            return UnitFormatMath.Fixed(converted, places, format.DecimalSeparator,
                format.SuppressAngularLeadingZeros, format.SuppressAngularTrailingZeros) + format.GradiansSymbol;
        }

        /// <summary>Converts an angle in degrees to radians.</summary>
        public static string ToRadians(double angle, UnitStyleFormat format)
        {
            int places = UnitFormatMath.Validate(angle, format, true);
            return UnitFormatMath.Fixed(angle * MathHelper.DegToRad, places, format.DecimalSeparator,
                format.SuppressAngularLeadingZeros, format.SuppressAngularTrailingZeros) + format.RadiansSymbol;
        }

        /// <summary>Converts a counterclockwise angle from east to a quadrant surveyor bearing.</summary>
        /// <param name="angle">Finite angle in degrees; reduced to [0,360) before quadrant selection.</param>
        /// <param name="format">DMS precision and symbols.</param>
        /// <param name="includeSpaces">Include a space on each side of the angle.</param>
        /// <remarks>Bearings use N/S then a nonnegative DMS angle then E/W. At exact axes the
        /// deterministic forms are N 90 E, N 0 E, N 90 W and S 0 W (with configured symbols).
        /// No document ANGBASE, ANGDIR or UNITMODE value is inferred.</remarks>
        public static string ToSurveyor(double angle, UnitStyleFormat format, bool includeSpaces = true)
        {
            UnitFormatMath.Validate(angle, format, true);
            double normalized = Normalize(angle), bearing;
            string northSouth, eastWest;
            if (normalized <= 90) { northSouth = "N"; eastWest = "E"; bearing = 90 - normalized; }
            else if (normalized <= 180) { northSouth = "N"; eastWest = "W"; bearing = normalized - 90; }
            else if (normalized <= 270) { northSouth = "S"; eastWest = "W"; bearing = 270 - normalized; }
            else { northSouth = "S"; eastWest = "E"; bearing = normalized - 270; }
            string gap = includeSpaces ? " " : string.Empty;
            return northSouth + gap + ToDegreesMinutesSeconds(bearing, format) + gap + eastWest;
        }

        private static double Normalize(double angle)
        {
            angle %= 360;
            if (angle < 0) angle += 360;
            // A tiny negative value can round up to 360 when the turn is added.
            return angle == 0 || angle == 360 ? 0 : angle;
        }
    }
}
