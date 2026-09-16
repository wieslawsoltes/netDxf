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
using System.Numerics;

namespace netDxf.Units
{
    /// <summary>Converts DXF midnight-based Julian day serials and elapsed-day values.</summary>
    /// <remarks>The calendar is DateTime's proleptic Gregorian calendar. DateTime.Kind is not
    /// used to infer a timezone or convert the supplied clock fields. Serial precision is
    /// limited by binary64; decoded values use the nearest representable DateTime tick.</remarks>
    public static class DrawingTime
    {
        /// <summary>Serial of January 1, year 1 at midnight.</summary>
        public const double MinimumJulianDate = 1721426.0;
        /// <summary>Exclusive upper serial bound, January 1, year 10000.</summary>
        public const double MaximumJulianDateExclusive = 5373485.0;

        /// <summary>Converts DateTime clock fields to a DXF day number plus fraction since midnight.</summary>
        /// <remarks>Sub-millisecond ticks are included. At the last representable day, rounding
        /// that would leave DateTime's range is clamped to the greatest in-range double.
        /// This is not the astronomical Julian convention with its noon boundary.</remarks>
        public static double ToJulianCalendar(DateTime date)
        {
            long days = date.Ticks / TimeSpan.TicksPerDay;
            long remainder = date.Ticks % TimeSpan.TicksPerDay;
            double serial = MinimumJulianDate + days + remainder / (double)TimeSpan.TicksPerDay;
            if (serial >= MaximumJulianDateExclusive)
                return BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(MaximumJulianDateExclusive) - 1);
            return serial;
        }

        /// <summary>Decodes a finite in-range DXF serial, including times on December 31, 9999.</summary>
        /// <remarks>The returned DateTime has Kind Unspecified. Day fractions are rounded once
        /// to ticks with midpoint ties to even, rather than truncating each clock component.</remarks>
        public static DateTime FromJulianCalendar(double date)
        {
            UnitFormatMath.CheckFinite(date, nameof(date));
            if (date < MinimumJulianDate || date >= MaximumJulianDateExclusive)
                throw new ArgumentOutOfRangeException(nameof(date), "The date is outside years 1 through 9999.");
            long day = (long)Math.Floor(date);
            long fraction = (long)UnitFormatMath.RoundMagnitude(date - day, new BigInteger(TimeSpan.TicksPerDay));
            long ticks = (day - (long)MinimumJulianDate) * TimeSpan.TicksPerDay + fraction;
            return new DateTime(ticks, DateTimeKind.Unspecified);
        }

        /// <summary>Converts a finite signed elapsed-day value to the nearest TimeSpan tick.</summary>
        /// <remarks>Uses midpoint ties to even and rejects values outside TimeSpan's range.
        /// Negative durations remain supported; drawing-specific nonnegative policy is separate.</remarks>
        public static TimeSpan EditingTime(double elapsed)
        {
            UnitFormatMath.CheckFinite(elapsed, nameof(elapsed));
            BigInteger ticks = UnitFormatMath.RoundMagnitude(elapsed, new BigInteger(TimeSpan.TicksPerDay));
            if (elapsed < 0) ticks = -ticks;
            if (ticks < long.MinValue || ticks > long.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(elapsed), "The elapsed value is outside TimeSpan's range.");
            return new TimeSpan((long)ticks);
        }
    }
}
