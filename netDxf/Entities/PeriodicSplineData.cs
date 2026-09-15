using System;

namespace netDxf.Entities
{
    internal static class PeriodicSplineData
    {
        internal static bool Same(double a, double b)
        { return BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b); }

        internal static void Validate(Vector3[] controls, double[] weights, double[] knots, int degree)
        {
            if (degree < 1 || degree > Spline.MaxDegree || controls == null || weights == null || knots == null
                || controls.Length < degree + 1 || weights.Length != controls.Length
                || (long)knots.Length != (long)controls.Length + 2 * degree + 1)
                throw new NotSupportedException("Periodic SPLINE conversion/evaluation requires the supported compact control and extended knot layout.");
            double maximumWeight = 0, minimumWeight = double.MaxValue;
            for (int i = 0; i < controls.Length; i++)
            {
                Finite(controls[i]); Finite(weights[i]);
                if (weights[i] <= 0) throw new NotSupportedException("Periodic SPLINE conversion/evaluation requires positive finite weights.");
                maximumWeight = Math.Max(maximumWeight, weights[i]); minimumWeight = Math.Min(minimumWeight, weights[i]);
            }
            if (minimumWeight / maximumWeight == 0)
                throw new NotSupportedException("The periodic SPLINE weight range cannot be represented during evaluation.");
            foreach (double knot in knots) Finite(knot);
            for (int i = 0; i + 1 < knots.Length; i++)
            {
                double span = knots[i + 1] - knots[i]; Finite(span);
                if (span <= 0) throw new NotSupportedException("Periodic SPLINE conversion/evaluation requires strictly increasing knots.");
            }
            double period = knots[controls.Length + degree] - knots[degree]; Finite(period);
            if (period <= 0) throw new NotSupportedException("The stored periodic SPLINE active domain must have positive length.");
            for (int i = 0; i < 2 * degree; i++)
            {
                double left = knots[i + 1] - knots[i], right = knots[i + controls.Length + 1] - knots[i + controls.Length];
                if (Math.Abs(left - right) > 1e-12 * Math.Max(left, right))
                    throw new NotSupportedException("The stored periodic SPLINE exterior spans do not continue the supplied active domain cyclically.");
            }
        }

        internal static void Validate(Spline spline)
        {
            Validate(spline.ControlPoints, spline.Weights, spline.Knots, spline.Degree);
            foreach (Vector3 point in spline.FitPoints) Finite(point);
            if (spline.StartTangent.HasValue) Finite(spline.StartTangent.Value);
            if (spline.EndTangent.HasValue) Finite(spline.EndTangent.Value);
        }

        internal static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        internal static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentException("Periodic SPLINE conversion/evaluation requires finite, representable values.");
        }
    }
}
