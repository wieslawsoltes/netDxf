// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Numerics;

namespace netDxf.Entities
{
    // LINE stores WCS endpoints and an independent signed extrusion vector.
    // Unlike a filled planar entity, its normal need not be perpendicular to
    // its direction. A shear of the extrusion remains representable.
    internal static class LineAffineTransform
    {
        internal static void Apply(Line line, Matrix3 matrix, Vector3 translation)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Finite(matrix[r, c]);
            Finite(translation); Finite(line.StartPoint); Finite(line.EndPoint);
            Finite(line.AffineNormal); Finite(line.Thickness);
            Vector3 normal = line.AffineNormal;
            if (normal.X == 0 && normal.Y == 0 && normal.Z == 0)
                throw new InvalidOperationException("A LINE extrusion direction must be nonzero.");
            bool identity = translation.X == 0 && translation.Y == 0 && translation.Z == 0;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) identity &= matrix[r, c] == (r == c ? 1 : 0);
            if (identity) return;

            Vector3 start = Point(matrix, line.StartPoint, translation);
            Vector3 end = Point(matrix, line.EndPoint, translation);
            Dyadic x = Dot(matrix, 0, normal, 0), y = Dot(matrix, 1, normal, 0), z = Dot(matrix, 2, normal, 0);
            double thickness;
            if (x.IsZero && y.IsZero && z.IsZero)
            {
                // A singular map can remove extrusion without destroying the
                // WCS segment. Retain the old direction as unused metadata.
                thickness = 0;
            }
            else
            {
                int exponent = Math.Max(x.TopExponent, Math.Max(y.TopExponent, z.TopExponent));
                double sx = x.Round(-exponent), sy = y.Round(-exponent), sz = z.Round(-exponent);
                double length = Math.Sqrt(sx * sx + sy * sy + sz * sz);
                normal = new Vector3(sx / length, sy / length, sz / length);
                if ((!x.IsZero && normal.X == 0) || (!y.IsZero && normal.Y == 0) || (!z.IsZero && normal.Z == 0))
                    throw new NotSupportedException("A transformed LINE normal component underflows to zero.");
                thickness = (Dyadic.From(line.Thickness) * Dyadic.From(length)).Round(exponent);
                if (line.Thickness != 0 && thickness == 0)
                    throw new NotSupportedException("Nonzero transformed LINE thickness underflows to zero.");
            }
            // All arithmetic and representability checks finish before setters
            // or proxy invalidation. Publication bypasses virtual Normal accessors.
            bool changed = !Same(start, line.StartPoint) || !Same(end, line.EndPoint) ||
                !Same(normal, line.AffineNormal) || thickness != line.Thickness;
            if (!changed) return;
            line.PublishAffine(start, end, normal, thickness);
        }

        internal static void CheckAffine(Matrix4 matrix)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Finite(matrix[r, c]);
            if (matrix.M41 != 0 || matrix.M42 != 0 || matrix.M43 != 0 || matrix.M44 != 1)
                throw new NotSupportedException("Projective matrices are not LINE affine transforms.");
        }

        internal static bool Same(Vector3 a, Vector3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "LINE geometry and transforms must be finite.");
        }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static Vector3 Point(Matrix3 matrix, Vector3 point, Vector3 translation)
        {
            return new Vector3(Coordinate(Dot(matrix, 0, point, translation.X)),
                Coordinate(Dot(matrix, 1, point, translation.Y)), Coordinate(Dot(matrix, 2, point, translation.Z)));
        }
        private static double Coordinate(Dyadic value)
        {
            double result = value.Round(0);
            if (!value.IsZero && result == 0)
                throw new NotSupportedException("A nonzero transformed LINE coordinate underflows to zero.");
            return result;
        }
        private static Dyadic Dot(Matrix3 matrix, int row, Vector3 value, double translation)
        {
            return Dyadic.From(matrix[row, 0]) * Dyadic.From(value.X) +
                Dyadic.From(matrix[row, 1]) * Dyadic.From(value.Y) +
                Dyadic.From(matrix[row, 2]) * Dyadic.From(value.Z) + Dyadic.From(translation);
        }

        // Every input has a 53-bit significand and bounded binary exponent.
        // A three-product affine dot needs at most about 4,200 integer bits;
        // no input-dependent recursion, unbounded enumeration or rational GCD.
        // Round once at the final spacing, including subnormal ties to even.
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
                if (top > 1023) throw new NotSupportedException("Transformed LINE geometry exceeds the finite binary64 range.");
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
                if (top > 1023) throw new NotSupportedException("Transformed LINE geometry rounds outside the finite binary64 range.");
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
