// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Numerics;

namespace netDxf.Entities
{
    // RAY/XLINE store a WCS origin and a unit direction, not a finite endpoint.
    // Prepare every result before mutating either entity. Exact bounded dyadic
    // dots avoid overflow/cancellation and premature subnormal rounding.
    internal sealed class InfiniteLineTransform
    {
        internal Vector3 Origin { get; private set; }
        internal Vector3 Direction { get; private set; }
        internal Vector3 Normal { get; private set; }
        internal bool Changed { get; private set; }

        internal static InfiniteLineTransform Prepare(Matrix3 matrix, Vector3 translation,
            Vector3 origin, Vector3 direction, Vector3 normal)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Finite(matrix[r, c]);
            Finite(translation); Finite(origin); UnitSource(direction); UnitSource(normal);
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) linearIdentity &= matrix[r, c] == (r == c ? 1 : 0);
            if (linearIdentity && translation.X == 0 && translation.Y == 0 && translation.Z == 0)
                return new InfiniteLineTransform { Origin = origin, Direction = direction, Normal = normal };

            Vector3 point = TransformPoint(matrix, origin, translation);
            Vector3 nextDirection = direction, nextNormal = normal;
            if (!linearIdentity)
            {
                if (!TryDirection(matrix, direction, out nextDirection))
                    throw new NotSupportedException("The affine map collapses the RAY/XLINE direction to a point.");
                // This auxiliary model normal is not a geometric plane normal
                // in the RAY/XLINE wire schema. Preserve its existing convention:
                // transform as a direction, or retain it on exact collapse.
                if (!TryDirection(matrix, normal, out nextNormal)) nextNormal = normal;
            }
            return new InfiniteLineTransform { Origin = point, Direction = nextDirection, Normal = nextNormal,
                Changed = !Same(point, origin) || !Same(nextDirection, direction) || !Same(nextNormal, normal) };
        }

        // Callers validate all input components before using these shared,
        // exact affine-point and scale-safe auxiliary-direction operations.
        internal static Vector3 TransformPoint(Matrix3 matrix, Vector3 point, Vector3 translation)
        {
            return new Vector3(Coordinate(Dot(matrix, 0, point, translation.X)),
                Coordinate(Dot(matrix, 1, point, translation.Y)), Coordinate(Dot(matrix, 2, point, translation.Z)));
        }
        internal static Vector3 AuxiliaryNormal(Matrix3 matrix, Vector3 normal)
        {
            UnitSource(normal);
            return TryDirection(matrix, normal, out Vector3 result) ? result : normal;
        }

        internal static void CheckAffine(Matrix4 matrix)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Finite(matrix[r, c]);
            if (matrix.M41 != 0 || matrix.M42 != 0 || matrix.M43 != 0 || matrix.M44 != 1)
                throw new NotSupportedException("Projective matrices are not affine entity transforms.");
        }
        private static bool Same(Vector3 a, Vector3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Infinite-line geometry and transforms must be finite.");
        }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static void UnitSource(Vector3 value)
        {
            Finite(value);
            if (Math.Abs(Vector3.DotProduct(value, value) - 1.0) > 2e-15)
                throw new InvalidOperationException("The stored RAY/XLINE directions must have finite unit components.");
        }
        private static double Coordinate(Dyadic value)
        {
            double result = value.Round(0);
            if (!value.IsZero && result == 0)
                throw new NotSupportedException("A nonzero transformed origin coordinate underflows to zero.");
            return result;
        }
        private static bool TryDirection(Matrix3 matrix, Vector3 value, out Vector3 direction)
        {
            Dyadic x = Dot(matrix, 0, value, 0), y = Dot(matrix, 1, value, 0), z = Dot(matrix, 2, value, 0);
            direction = value;
            if (x.IsZero && y.IsZero && z.IsZero) return false;
            int exponent = Math.Max(x.TopExponent, Math.Max(y.TopExponent, z.TopExponent));
            direction = Vector3.NormalizeFiniteDirection(new Vector3(x.Round(-exponent), y.Round(-exponent), z.Round(-exponent)), nameof(value));
            if ((!x.IsZero && direction.X == 0) || (!y.IsZero && direction.Y == 0) || (!z.IsZero && direction.Z == 0))
                throw new NotSupportedException("A nonzero transformed direction component underflows to zero.");
            return true;
        }
        private static Dyadic Dot(Matrix3 matrix, int row, Vector3 value, double translation)
        {
            return Dyadic.From(matrix[row, 0]) * Dyadic.From(value.X) +
                Dyadic.From(matrix[row, 1]) * Dyadic.From(value.Y) +
                Dyadic.From(matrix[row, 2]) * Dyadic.From(value.Z) + Dyadic.From(translation);
        }

        // Binary64 inputs bound every dot to roughly 4,200 integer bits. No
        // caller enumeration, input-dependent recursion, division or GCD is used.
        // This arithmetic follows the independently qualified LINE task; it is
        // private here so this PR does not depend on an unmerged LINE API.
        private readonly struct Dyadic
        {
            private readonly BigInteger integer;
            private readonly int exponent;
            private Dyadic(BigInteger integer, int exponent) { this.integer = integer; this.exponent = exponent; }
            internal bool IsZero { get { return this.integer.IsZero; } }
            internal int TopExponent { get { return this.IsZero ? int.MinValue : BitLength(BigInteger.Abs(this.integer)) - 1 + this.exponent; } }
            internal static Dyadic From(double value)
            {
                long bits = BitConverter.DoubleToInt64Bits(value);
                int field = (int)((bits >> 52) & 0x7ff);
                long mantissa = bits & 0x000fffffffffffffL;
                if (field != 0) mantissa |= 1L << 52;
                return new Dyadic(new BigInteger(bits < 0 ? -mantissa : mantissa), field == 0 ? -1074 : field - 1075);
            }
            public static Dyadic operator *(Dyadic a, Dyadic b)
            { return new Dyadic(a.integer * b.integer, a.exponent + b.exponent); }
            public static Dyadic operator +(Dyadic a, Dyadic b)
            {
                if (a.IsZero) return b; if (b.IsZero) return a;
                int exponent = Math.Min(a.exponent, b.exponent);
                return new Dyadic((a.integer << (a.exponent - exponent)) + (b.integer << (b.exponent - exponent)), exponent);
            }
            internal double Round(int scale)
            {
                if (this.IsZero) return 0;
                BigInteger magnitude = BigInteger.Abs(this.integer);
                int exponent = this.exponent + scale;
                int top = BitLength(magnitude) - 1 + exponent;
                if (top > 1023) throw new NotSupportedException("Transformed infinite-line geometry exceeds the finite binary64 range.");
                int spacing = Math.Max(-1074, top - 52), shift = spacing - exponent;
                BigInteger significand;
                if (shift > 0)
                {
                    significand = magnitude >> shift;
                    BigInteger remainder = magnitude - (significand << shift);
                    int comparison = (remainder << 1).CompareTo(BigInteger.One << shift);
                    if (comparison > 0 || comparison == 0 && !significand.IsEven) significand++;
                }
                else significand = magnitude << -shift;
                long sign = this.integer.Sign < 0 ? long.MinValue : 0;
                if (top < -1022) return BitConverter.Int64BitsToDouble(sign | (long)significand);
                if (significand == (BigInteger.One << 53)) { significand >>= 1; top++; }
                if (top > 1023) throw new NotSupportedException("Transformed infinite-line geometry rounds outside the finite binary64 range.");
                return BitConverter.Int64BitsToDouble(sign | ((long)(top + 1023) << 52) | (long)(significand - (BigInteger.One << 52)));
            }
            private static int BitLength(BigInteger value)
            {
                byte[] bytes = value.ToByteArray(); int last = bytes.Length - 1;
                while (last > 0 && bytes[last] == 0) last--;
                int high = bytes[last], bits = 0;
                while (high != 0) { bits++; high >>= 1; }
                return 8 * last + bits;
            }
        }
    }
}
