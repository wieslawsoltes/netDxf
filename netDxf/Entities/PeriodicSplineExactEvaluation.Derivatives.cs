// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    internal static partial class PeriodicSplineExactEvaluation
    {
        internal static Vector3[] Derivatives(Vector3[] points, double[] weights, double[] knots,
            int degree, bool periodic, int span, double parameter, int order)
        {
            // Jet coefficient k is the kth Taylor coefficient, i.e. derivative/k!.
            // Descending j and k keep both previous-level controls available.
            int homogeneousOrder = Math.Min(degree, order);
            var jet = new Rational[degree + 1, homogeneousOrder + 1, 4];
            for (int j = 0; j <= degree; j++)
            {
                int at = span - degree + j;
                if (periodic) at = at < degree ? points.Length - degree + at : at - degree;
                for (int k = 0; k <= homogeneousOrder; k++)
                    for (int c = 0; c < 4; c++) jet[j, k, c] = Rational.Zero;
                Rational w = Rational.FromDouble(weights[at]);
                jet[j, 0, 0] = Rational.FromDouble(points[at].X) * w;
                jet[j, 0, 1] = Rational.FromDouble(points[at].Y) * w;
                jet[j, 0, 2] = Rational.FromDouble(points[at].Z) * w;
                jet[j, 0, 3] = w;
            }
            Rational u = Rational.FromDouble(parameter);
            for (int level = 1; level <= degree; level++)
            {
                for (int j = degree; j >= level; j--)
                {
                    int i = span - degree + j;
                    Rational low = Rational.FromDouble(knots[i]);
                    Rational divisor = Rational.FromDouble(knots[i + degree + 1 - level]) - low;
                    if (divisor.Sign <= 0)
                        throw new InvalidOperationException("The selected spline span has no basis support.");
                    Rational slope = Rational.One / divisor;
                    Rational alpha = (u - low) * slope, other = Rational.One - alpha;
                    for (int k = Math.Min(level, homogeneousOrder); k >= 0; k--)
                        for (int c = 0; c < 4; c++)
                        {
                            Rational value = other * jet[j - 1, k, c] + alpha * jet[j, k, c];
                            if (k > 0) value += slope * (jet[j, k - 1, c] - jet[j - 1, k - 1, c]);
                            jet[j, k, c] = value;
                        }
                }
            }

            Rational denominator = jet[degree, 0, 3];
            if (denominator.Sign == 0)
                throw new InvalidOperationException("The selected spline denominator is zero.");
            var quotient = new Rational[order + 1, 3];
            var result = new Vector3[order + 1];
            Rational factorial = Rational.One;
            for (int k = 0; k <= order; k++)
            {
                if (k > 1) factorial *= Rational.FromDouble(k);
                var components = new double[3];
                for (int c = 0; c < 3; c++)
                {
                    Rational numerator = k <= homogeneousOrder ? jet[degree, k, c] : Rational.Zero;
                    for (int i = 1; i <= Math.Min(k, homogeneousOrder); i++)
                        numerator -= jet[degree, i, 3] * quotient[k - i, c];
                    quotient[k, c] = numerator / denominator;
                    components[c] = (factorial * quotient[k, c]).ToDouble();
                }
                result[k] = new Vector3(components[0], components[1], components[2]);
            }
            return result;
        }
    }
}
