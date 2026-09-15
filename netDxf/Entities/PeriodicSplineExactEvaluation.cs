using System;
using BigInteger = System.Numerics.BigInteger;

namespace netDxf.Entities
{
    /// <summary>Bounded exact evaluation for ill-conditioned periodic samples.</summary>
    internal static class PeriodicSplineExactEvaluation
    {
        // A sample uses at most eleven controls and 55 local basis steps. The
        // explicit component budget also bounds pathological rational growth.
        private const int MaximumIntegerBytes = 65536;

        internal static Vector3 Evaluate(Vector3[] expandedControls, double[] expandedWeights,
            double[] knots, int degree, double parameter, int firstControl)
        {
            if (degree < 1 || degree > Spline.MaxDegree)
                throw new ArgumentException("Exact periodic SPLINE evaluation requires a supported degree.");
            Rational u = Rational.FromDouble(parameter);
            var localKnots = new Rational[2 * degree + 1];
            for (int i = 0; i < localKnots.Length; i++)
                localKnots[i] = Rational.FromDouble(knots[firstControl + i]);

            // Local nonzero basis recurrence (Algorithm A2.2), evaluated over
            // exact rational values of the actual binary64 inputs. Reusing a
            // rounded double basis would retain the cancellation defect.
            var basis = new Rational[degree + 1];
            var left = new Rational[degree + 1];
            var right = new Rational[degree + 1];
            basis[0] = Rational.One;
            for (int j = 1; j <= degree; j++)
            {
                left[j] = u - localKnots[degree + 1 - j];
                right[j] = localKnots[degree + j] - u;
                Rational saved = Rational.Zero;
                for (int r = 0; r < j; r++)
                {
                    Rational divisor = right[r + 1] + left[j - r];
                    if (divisor.Sign <= 0)
                        throw new ArgumentException("Exact periodic SPLINE evaluation requires increasing knots.");
                    Rational temporary = basis[r] / divisor;
                    basis[r] = saved + right[r + 1] * temporary;
                    saved = left[j - r] * temporary;
                }
                basis[j] = saved;
            }

            Rational denominator = Rational.Zero;
            Rational x = Rational.Zero, y = Rational.Zero, z = Rational.Zero;
            for (int i = 0; i <= degree; i++)
            {
                Rational weighted = basis[i] * Rational.FromDouble(expandedWeights[firstControl + i]);
                if (weighted.Sign < 0)
                    throw new ArgumentException("Exact periodic SPLINE evaluation requires positive rational weights.");
                if (weighted.Sign == 0) continue;
                Vector3 point = expandedControls[firstControl + i];
                denominator += weighted;
                x += weighted * Rational.FromDouble(point.X);
                y += weighted * Rational.FromDouble(point.Y);
                z += weighted * Rational.FromDouble(point.Z);
            }
            if (denominator.Sign <= 0)
                throw new ArgumentException("The exact periodic SPLINE denominator is zero.");
            return new Vector3((x / denominator).ToDouble(), (y / denominator).ToDouble(), (z / denominator).ToDouble());
        }

        private static void CheckSize(BigInteger value)
        {
            if (value.ToByteArray().Length > MaximumIntegerBytes)
                throw new NotSupportedException("Exact periodic SPLINE evaluation exceeds its bounded arithmetic budget.");
        }

        private static BigInteger Product(BigInteger a, BigInteger b)
        {
            if (a.IsZero || b.IsZero) return BigInteger.Zero;
            if ((long)a.ToByteArray().Length + b.ToByteArray().Length > MaximumIntegerBytes)
                throw new NotSupportedException("Exact periodic SPLINE evaluation exceeds its bounded arithmetic budget.");
            return a * b;
        }

        private readonly struct Rational
        {
            internal static readonly Rational Zero = new Rational(BigInteger.Zero, BigInteger.One, false);
            internal static readonly Rational One = new Rational(BigInteger.One, BigInteger.One, false);
            private readonly BigInteger numerator;
            private readonly BigInteger denominator;

            private Rational(BigInteger numerator, BigInteger denominator, bool reduce = true)
            {
                if (denominator.IsZero) throw new ArgumentException("An exact periodic SPLINE divisor is zero.");
                if (numerator.IsZero)
                { this.numerator = BigInteger.Zero; this.denominator = BigInteger.One; return; }
                if (denominator.Sign < 0) { numerator = -numerator; denominator = -denominator; }
                CheckSize(numerator); CheckSize(denominator);
                if (reduce)
                {
                    BigInteger divisor = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
                    numerator /= divisor; denominator /= divisor;
                }
                this.numerator = numerator;
                this.denominator = denominator;
            }

            internal int Sign { get { return this.numerator.Sign; } }

            internal static Rational FromDouble(double value)
            {
                long bits = BitConverter.DoubleToInt64Bits(value);
                int field = (int)((bits >> 52) & 0x7ff);
                if (field == 0x7ff) throw new ArgumentException("Exact periodic SPLINE inputs must be finite.");
                long significand = bits & 0x000fffffffffffffL;
                if (field != 0) significand |= 1L << 52;
                if (significand == 0) return Zero;
                BigInteger integer = new BigInteger(significand);
                if (bits < 0) integer = -integer;
                int exponent = field == 0 ? -1074 : field - 1075;
                return exponent >= 0 ? new Rational(integer << exponent, BigInteger.One, false)
                    : new Rational(integer, BigInteger.One << -exponent);
            }

            public static Rational operator +(Rational a, Rational b)
            {
                if (a.numerator.IsZero) return b;
                if (b.numerator.IsZero) return a;
                BigInteger shared = BigInteger.GreatestCommonDivisor(a.denominator, b.denominator);
                BigInteger leftScale = b.denominator / shared, rightScale = a.denominator / shared;
                return new Rational(Product(a.numerator, leftScale) + Product(b.numerator, rightScale),
                    Product(a.denominator, leftScale));
            }

            public static Rational operator -(Rational a, Rational b)
            { return a + new Rational(-b.numerator, b.denominator, false); }

            public static Rational operator *(Rational a, Rational b)
            {
                if (a.numerator.IsZero || b.numerator.IsZero) return Zero;
                BigInteger first = BigInteger.GreatestCommonDivisor(BigInteger.Abs(a.numerator), b.denominator);
                BigInteger second = BigInteger.GreatestCommonDivisor(BigInteger.Abs(b.numerator), a.denominator);
                return new Rational(Product(a.numerator / first, b.numerator / second),
                    Product(a.denominator / second, b.denominator / first), false);
            }

            public static Rational operator /(Rational a, Rational b)
            {
                if (b.numerator.IsZero) throw new ArgumentException("An exact periodic SPLINE divisor is zero.");
                return a * new Rational(b.denominator, b.numerator, false);
            }

            internal double ToDouble()
            {
                if (this.numerator.IsZero) return 0.0;
                long sign = this.numerator.Sign < 0 ? long.MinValue : 0;
                BigInteger magnitude = BigInteger.Abs(this.numerator);
                int exponent = BitLength(magnitude) - BitLength(this.denominator);
                if (exponent >= 0)
                {
                    if (magnitude < (this.denominator << exponent)) exponent--;
                }
                else if ((magnitude << -exponent) < this.denominator) exponent--;
                if (exponent > 1023)
                    throw new ArgumentException("The exact periodic SPLINE sample exceeds the finite binary64 range.");

                // Divide once at the destination's spacing. This avoids the
                // double rounding of separately rounded numerator/denominator
                // or a normal result subsequently scaled into subnormal range.
                int spacing = exponent < -1022 ? -1074 : exponent - 52;
                BigInteger dividend = magnitude, divisor = this.denominator;
                if (spacing < 0) dividend <<= -spacing;
                else divisor <<= spacing;
                BigInteger remainder;
                BigInteger significand = BigInteger.DivRem(dividend, divisor, out remainder);
                int comparison = (remainder << 1).CompareTo(divisor);
                if (comparison > 0 || comparison == 0 && !significand.IsEven) significand++;

                if (exponent < -1022)
                    return BitConverter.Int64BitsToDouble(sign | (long)significand);
                if (significand == (BigInteger.One << 53))
                { significand >>= 1; exponent++; }
                if (exponent > 1023)
                    throw new ArgumentException("The exact periodic SPLINE sample rounds outside the finite binary64 range.");
                long fraction = (long)(significand - (BigInteger.One << 52));
                return BitConverter.Int64BitsToDouble(sign | ((long)(exponent + 1023) << 52) | fraction);
            }

            private static int BitLength(BigInteger positive)
            {
                byte[] bytes = positive.ToByteArray();
                int last = bytes.Length - 1;
                while (last > 0 && bytes[last] == 0) last--;
                int high = bytes[last], bits = 0;
                while (high != 0) { bits++; high >>= 1; }
                return last * 8 + bits;
            }
        }
    }
}
