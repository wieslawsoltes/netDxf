// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    internal static partial class PeriodicSplineExactEvaluation
    {
        internal static void ExtractBezierSpan(Vector3[] points, double[] weights, double[] knots,
            int degree, int span, out Vector3[] result, out double[] resultWeights)
        {
            result = new Vector3[degree + 1];
            resultWeights = new double[degree + 1];
            if (knots[span - degree + 1] == knots[span] && knots[span + degree] == knots[span + 1])
            {
                // A span already in Bezier form needs no arithmetic. Preserve
                // the caller's exact bits, including signed zeros and weights.
                Array.Copy(points, span - degree, result, 0, degree + 1);
                Array.Copy(weights, span - degree, resultWeights, 0, degree + 1);
                return;
            }
            HomogeneousControl[] controls = BezierBlossom(points, weights, knots, degree, span);
            for (int i = 0; i <= degree; i++) controls[i].Store(result, resultWeights, i);
        }

        // On a fixed nonempty span, Bernstein coefficient i is the polar form
        // evaluated at (p-i) copies of its left endpoint and i of its right.
        // This uses only p+1 controls and 2p+2 knots; no full-curve refinement,
        // guessed sample spacing, interior floating-point parameters or refit.
        private static HomogeneousControl[] BezierBlossom(Vector3[] points, double[] weights,
            double[] knots, int degree, int span)
        {
            var input = new HomogeneousControl[degree + 1];
            for (int i = 0; i <= degree; i++)
                input[i] = new HomogeneousControl(points[span - degree + i], weights[span - degree + i]);
            var localKnots = new Rational[2 * degree + 2];
            for (int i = 0; i < localKnots.Length; i++)
                localKnots[i] = Rational.FromDouble(knots[span - degree + i]);
            var result = new HomogeneousControl[degree + 1];
            var work = new HomogeneousControl[degree + 1];
            for (int coefficient = 0; coefficient <= degree; coefficient++)
            {
                Array.Copy(input, work, input.Length);
                for (int level = 1; level <= degree; level++)
                {
                    Rational parameter = localKnots[level <= degree - coefficient ? degree : degree + 1];
                    for (int j = degree; j >= level; j--)
                    {
                        Rational lo = localKnots[j], hi = localKnots[degree + 1 + j - level];
                        work[j] = HomogeneousControl.Blend(work[j - 1], work[j], (parameter - lo) / (hi - lo));
                    }
                }
                result[coefficient] = work[degree];
            }
            return result;
        }
    }
}
