// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    internal static partial class PeriodicSplineExactEvaluation
    {
        // Multiple-knot corner cutting. Only the degree+1 affected homogeneous
        // controls enter exact arithmetic; the unaffected arrays are copied.
        internal static void InsertKnot(Vector3[] points, double[] weights, double[] knots,
            int degree, double parameter, int span, int multiplicity, int times,
            out Vector3[] result, out double[] resultWeights, out double[] resultKnots)
        {
            result = new Vector3[points.Length + times];
            resultWeights = new double[result.Length];
            resultKnots = new double[knots.Length + times];
            Array.Copy(knots, 0, resultKnots, 0, span + 1);
            for (int j = 1; j <= times; j++) resultKnots[span + j] = parameter;
            Array.Copy(knots, span + 1, resultKnots, span + times + 1, knots.Length - span - 1);
            int prefix = span - degree + 1, suffix = span - multiplicity;
            Array.Copy(points, 0, result, 0, prefix);
            Array.Copy(weights, 0, resultWeights, 0, prefix);
            Array.Copy(points, suffix, result, suffix + times, points.Length - suffix);
            Array.Copy(weights, suffix, resultWeights, suffix + times, weights.Length - suffix);
            var local = new HomogeneousControl[degree - multiplicity + 1];
            for (int i = 0; i < local.Length; i++)
                local[i] = new HomogeneousControl(points[span - degree + i], weights[span - degree + i]);
            Rational u = Rational.FromDouble(parameter);
            int left = span - degree;
            for (int j = 1; j <= times; j++)
            {
                left = span - degree + j;
                for (int i = 0; i <= degree - j - multiplicity; i++)
                {
                    Rational lo = Rational.FromDouble(knots[left + i]);
                    Rational hi = Rational.FromDouble(knots[i + span + 1]);
                    Rational alpha = (u - lo) / (hi - lo);
                    local[i] = HomogeneousControl.Blend(local[i], local[i + 1], alpha);
                }
                local[0].Store(result, resultWeights, left);
                local[degree - j - multiplicity].Store(result, resultWeights, span + times - j - multiplicity);
            }
            for (int i = left + 1; i < span - multiplicity; i++)
                local[i - left].Store(result, resultWeights, i);
        }

        private readonly struct HomogeneousControl
        {
            private readonly Rational x, y, z, w;
            internal HomogeneousControl(Vector3 point, double weight)
            {
                this.w = Rational.FromDouble(weight);
                this.x = this.w * Rational.FromDouble(point.X);
                this.y = this.w * Rational.FromDouble(point.Y);
                this.z = this.w * Rational.FromDouble(point.Z);
            }
            private HomogeneousControl(Rational x, Rational y, Rational z, Rational w)
            { this.x = x; this.y = y; this.z = z; this.w = w; }
            internal static HomogeneousControl Blend(HomogeneousControl a, HomogeneousControl b, Rational t)
            {
                Rational other = Rational.One - t;
                if (t.Sign < 0 || other.Sign < 0)
                    throw new InvalidOperationException("The knot insertion coefficient is outside its span.");
                return new HomogeneousControl(other * a.x + t * b.x, other * a.y + t * b.y,
                    other * a.z + t * b.z, other * a.w + t * b.w);
            }
            internal void Store(Vector3[] points, double[] weights, int index)
            {
                if (this.w.Sign <= 0) throw new NotSupportedException("A refined weight must be positive.");
                points[index] = new Vector3(Representable(this.x / this.w),
                    Representable(this.y / this.w), Representable(this.z / this.w));
                weights[index] = Representable(this.w);
            }
            private static double Representable(Rational value)
            {
                double result = value.ToDouble();
                if (result == 0 && value.Sign != 0)
                    throw new NotSupportedException("A nonzero refined component cannot be represented in binary64.");
                return result;
            }
        }
    }
}
