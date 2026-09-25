// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Globalization;
using System.Numerics;

namespace netDxf.IO
{
    // DXF numbers are invariant finite binary64 values, regardless of the host CLR.
    internal static class DxfDoubleParser
    {
        // Every binary64 midpoint has fewer than 1129 significant decimal digits:
        // it is m*2^e, m < 2^54 and e >= -1075. Retaining 1152 digits therefore
        // reaches every rounding boundary. A nonzero discarded tail only breaks
        // an exact midpoint tie. See doc/dxf-conformance/portable-double-parsing.md.
        private const int RetainedDigits = 1152;
        private const long ExponentLimit = 10000000000L;

        internal static bool TryParse(string text, out double value)
        {
#if NET6_0_OR_GREATER
            return text != null && text.IndexOf('\0') < 0
                ? TryParseModern(text, out value) : Fail(out value);
#else
            return TryParsePortable(text, out value);
#endif
        }

#if NET6_0_OR_GREATER
        private static bool TryParseModern(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !double.IsNaN(value) && !double.IsInfinity(value);
        }
#endif

        private static bool Fail(out double value) { value = 0; return false; }
        private static bool Space(char c) { return c == ' ' || (c >= '\t' && c <= '\r'); }
        private static bool Digit(char c) { return c >= '0' && c <= '9'; }

        // Also compiled on modern targets so the same implementation can be
        // compared with an independent runtime parser in conformance tests.
        internal static bool TryParsePortable(string text, out double value)
        {
            value = 0;
            if (text == null) return false;
            int first = 0, end = text.Length;
            while (first < end && Space(text[first])) first++;
            while (end > first && Space(text[end - 1])) end--;
            if (first == end) return false;
            bool negative = text[first] == '-';
            if (text[first] == '+' || negative) first++;
            if (first == end) return false;

            BigInteger coefficient = BigInteger.Zero;
            uint chunk = 0, chunkPower = 1;
            int kept = 0, significant = 0, fractional = 0;
            bool point = false, anyDigit = false, sticky = false;
            int index = first;
            for (; index < end; index++)
            {
                char c = text[index];
                if (c == '.' && !point) { point = true; continue; }
                if (!Digit(c)) break;
                anyDigit = true;
                if (point) fractional++;
                int digit = c - '0';
                if (significant == 0 && digit == 0) continue;
                significant++;
                if (kept == RetainedDigits) { sticky |= digit != 0; continue; }
                kept++;
                chunk = chunk * 10 + (uint)digit;
                chunkPower *= 10;
                if (chunkPower == 1000000000)
                {
                    coefficient = coefficient * chunkPower + chunk;
                    chunk = 0; chunkPower = 1;
                }
            }
            if (!anyDigit) return false;
            if (chunkPower != 1) coefficient = coefficient * chunkPower + chunk;

            long exponent = 0;
            if (index < end && (text[index] == 'e' || text[index] == 'E'))
            {
                index++;
                bool exponentNegative = index < end && text[index] == '-';
                if (index < end && (text[index] == '+' || exponentNegative)) index++;
                int exponentStart = index;
                for (; index < end && Digit(text[index]); index++)
                    exponent = Math.Min(ExponentLimit, exponent * 10 + text[index] - '0');
                if (index == exponentStart) return false;
                if (exponentNegative) exponent = -exponent;
            }
            // Validate the entire token before even returning zero or overflow.
            if (index != end) return false;
            long sign = negative ? long.MinValue : 0;
            value = BitConverter.Int64BitsToDouble(sign);
            if (significant == 0) return true;
            long order = exponent - fractional + significant - 1;
            if (order > 308) return false;
            if (order < -324) return true;

            int power = (int)(exponent - fractional + significant - kept);
            BigInteger numerator = coefficient, denominator = BigInteger.One;
            if (power >= 0) numerator *= BigInteger.Pow(10, power);
            else denominator = BigInteger.Pow(10, -power);
            int binaryExponent = BitLength(numerator) - BitLength(denominator);
            if (binaryExponent >= 0)
            {
                if (numerator < (denominator << binaryExponent)) binaryExponent--;
            }
            else if ((numerator << -binaryExponent) < denominator) binaryExponent--;

            int step = Math.Max(binaryExponent - 52, -1074);
            if (step < 0) numerator <<= -step;
            else denominator <<= step;
            BigInteger remainder;
            BigInteger significand = BigInteger.DivRem(numerator, denominator, out remainder);
            int halfway = (remainder << 1).CompareTo(denominator);
            if (halfway > 0 || (halfway == 0 && (sticky || !significand.IsEven))) significand++;
            ulong bits = (ulong)significand;
            if (bits >= (1UL << 53)) { bits >>= 1; step++; }
            if (bits >= (1UL << 52))
            {
                int biasedExponent = step + 1075;
                if (biasedExponent >= 2047) return false;
                bits = ((ulong)biasedExponent << 52) | (bits - (1UL << 52));
            }
            value = BitConverter.Int64BitsToDouble(unchecked((long)bits) | sign);
            return true;
        }

        private static int BitLength(BigInteger value)
        {
            byte[] bytes = value.ToByteArray();
            int last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0) last--;
            int bits = last * 8;
            byte high = bytes[last];
            while (high != 0) { bits++; high >>= 1; }
            return bits;
        }
    }
}
