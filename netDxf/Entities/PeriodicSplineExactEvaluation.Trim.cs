// Copyright (c) netDxf contributors. Licensed under the MIT License.
namespace netDxf.Entities
{
    internal static partial class PeriodicSplineExactEvaluation
    {
        internal static void RestrictBezierSpan(Vector3[] points, double[] weights, double[] knots,
            int degree, int span, double start, double end, out Vector3[] result, out double[] resultWeights)
        {
            if (start == knots[span] && end == knots[span + 1])
            {
                ExtractBezierSpan(points, weights, knots, degree, span, out result, out resultWeights);
                return;
            }
            // Restrict the original homogeneous polynomial directly. Projecting
            // a full extracted span first would introduce avoidable double rounding.
            HomogeneousControl[] controls = BezierBlossom(points, weights, knots, degree, span,
                Rational.FromDouble(start), Rational.FromDouble(end));
            result = new Vector3[degree + 1]; resultWeights = new double[degree + 1];
            for (int i = 0; i <= degree; i++) controls[i].Store(result, resultWeights, i);
        }
    }
}
