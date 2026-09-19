// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    internal static partial class PeriodicSplineExactEvaluation
    {
        internal static void ElevateBezierSpan(Vector3[] points, double[] weights, double[] knots,
            int degree, int span, int targetDegree, out Vector3[] result, out double[] resultWeights)
        {
            HomogeneousControl[] local = BezierBlossom(points, weights, knots, degree, span);
            for (int q = degree + 1; q <= targetDegree; q++)
            {
                var elevated = new HomogeneousControl[q + 1];
                elevated[0] = local[0]; elevated[q] = local[q - 1];
                for (int i = 1; i < q; i++)
                {
                    // i/q must be exact, not the rational value of its rounded
                    // floating-point quotient. Do not project between degrees.
                    Rational alpha = Rational.FromDouble(i) / Rational.FromDouble(q);
                    elevated[i] = HomogeneousControl.Blend(local[i], local[i - 1], alpha);
                }
                local = elevated;
            }
            result = new Vector3[targetDegree + 1]; resultWeights = new double[targetDegree + 1];
            for (int i = 0; i <= targetDegree; i++) local[i].Store(result, resultWeights, i);
        }
    }
}
