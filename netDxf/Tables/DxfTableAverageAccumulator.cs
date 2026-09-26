// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Numerics;

namespace netDxf.Tables
{
    // Exact sum in units of the minimum subnormal, followed by one rounded division.
    // At most 1,000,000 finite binary64 operands need fewer than 2,119 magnitude bits.
    // This is local to one AVERAGE invocation; no resolver or mutable shared state is retained.
    internal struct DxfTableAverageAccumulator
    {
        private BigInteger units;

        internal void Add(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 0x7ff);
            if (exponent == 2047) throw new ArithmeticException("AVERAGE requires finite operands.");
            long mantissa = bits & 0xfffffffffffffL;
            if (exponent != 0) mantissa |= 1L << 52;
            if (mantissa == 0) return;
            BigInteger term = new BigInteger(mantissa);
            if (exponent > 1) term <<= exponent - 1;
            this.units += bits < 0 ? -term : term;
        }

        internal double Mean(int count)
        {
            if (count < 1) throw new InvalidOperationException("An empty AVERAGE has no numeric result.");
            if (this.units.IsZero) return 0;
            bool negative = this.units.Sign < 0;
            BigInteger numerator = BigInteger.Abs(this.units), denominator = new BigInteger(count);
            int exponent = BitLength(numerator) - BitLength(denominator);
            if (exponent >= 0)
            {
                if (numerator < (denominator << exponent)) exponent--;
            }
            else if ((numerator << -exponent) < denominator) exponent--;

            // Scale to a 53-bit normal significand, or the fixed subnormal quantum.
            int shift = Math.Max(0, exponent - 52);
            denominator <<= shift;
            BigInteger remainder;
            BigInteger rounded = BigInteger.DivRem(numerator, denominator, out remainder);
            int halfway = (remainder << 1).CompareTo(denominator);
            if (halfway > 0 || (halfway == 0 && !rounded.IsEven)) rounded++;
            ulong significand = (ulong)rounded;
            if (significand == 0) return 0; // Preserve the formula engine's positive-zero policy.
            if (significand == (1UL << 53)) { significand >>= 1; shift++; }
            ulong encoded = significand;
            if (significand >= (1UL << 52))
            {
                // A finite-input mean lies inside the input range; overflow is impossible.
                if (shift > 2045) throw new ArithmeticException("AVERAGE result exceeds binary64 range.");
                encoded = ((ulong)(shift + 1) << 52) | (significand - (1UL << 52));
            }
            if (negative) encoded |= 1UL << 63;
            return BitConverter.Int64BitsToDouble(unchecked((long)encoded));
        }

        private static int BitLength(BigInteger value)
        {
            byte[] bytes = value.ToByteArray();
            int last = bytes.Length - 1;
            while (last > 0 && bytes[last] == 0) last--;
            int result = last * 8;
            for (byte high = bytes[last]; high != 0; high >>= 1) result++;
            return result;
        }
    }
}
