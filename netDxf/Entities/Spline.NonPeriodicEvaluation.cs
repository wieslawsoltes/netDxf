// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Spline
    {
        private static List<Vector3> EvaluateNonPeriodicSpline(Vector3[] controls, double[] weights,
            double[] knots, int degree, bool closed, int precision)
        {
            // Validate counts before generating default arrays or reserving output.
            if (precision < 2 || precision > MaximumConvertedVertices)
                throw new ArgumentOutOfRangeException(nameof(precision), precision, "Nonperiodic evaluation requires 2 to 1000000 samples.");
            if (degree < 1 || degree > MaxDegree)
                throw new ArgumentOutOfRangeException(nameof(degree), degree, "The spline degree must be between 1 and 10.");
            if (controls == null) throw new ArgumentNullException(nameof(controls));
            if (controls.Length <= degree)
                throw new ArgumentException("At least degree + 1 control points are required.", nameof(controls));
            if (weights != null && weights.Length != controls.Length)
                throw new ArgumentException("The control and weight counts must agree.", nameof(weights));
            if (knots != null && (long)knots.Length != (long)controls.Length + degree + 1)
                throw new ArgumentException("Invalid number of knots.", nameof(knots));
            foreach (Vector3 point in controls)
                if (!NonPeriodicFinite(point.X) || !NonPeriodicFinite(point.Y) || !NonPeriodicFinite(point.Z))
                    throw new ArgumentException("Spline controls must be finite.", nameof(controls));
            bool signedWeights = false;
            if (weights == null)
            {
                weights = new double[controls.Length];
                for (int i = 0; i < weights.Length; i++) weights[i] = 1;
            }
            else foreach (double weight in weights)
            {
                if (!NonPeriodicFinite(weight)) throw new ArgumentException("Spline weights must be finite.", nameof(weights));
                signedWeights |= weight <= 0;
            }
            if (knots == null) knots = CreateKnotVector(controls.Length, degree, false);
            int multiplicity = 0;
            for (int i = 0; i < knots.Length; i++)
            {
                if (!NonPeriodicFinite(knots[i]) || (i > 0 && knots[i] < knots[i - 1]))
                    throw new ArgumentException("Spline knots must be finite and nondecreasing.", nameof(knots));
                multiplicity = i > 0 && knots[i] == knots[i - 1] ? multiplicity + 1 : 1;
                if (multiplicity > degree + 1)
                    throw new ArgumentException("Knot multiplicity exceeds degree + 1.", nameof(knots));
            }
            double start = knots[degree], end = knots[controls.Length];
            if (!(start < end)) throw new ArgumentException("The active spline knot domain must have positive length.", nameof(knots));
            int intervals = closed ? precision : precision - 1;
            double step = (end - start) / intervals;
            var points = new List<Vector3>(precision);
            double previous = start;
            for (int i = 0; i < precision; i++)
            {
                double u;
                if (i == 0) u = start;
                else if (!closed && i == intervals) u = end;
                else if (NonPeriodicFinite(step) && step > 0) u = start + step * i;
                else
                {
                    // Convex interpolation avoids overflowing end - start.
                    double t = (double)i / intervals;
                    u = (1 - t) * start + t * end;
                }
                if (!NonPeriodicFinite(u) || (i > 0 && u <= previous) || u > end || (i < intervals && u >= end))
                    throw new ArgumentException("Distinct requested spline sampling parameters cannot be represented.", nameof(precision));
                previous = u;
                points.Add(NonPeriodicPoint(controls, weights, knots, degree, u, signedWeights));
            }
            return points;
        }

        private static bool NonPeriodicFinite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }

        private static Vector3 NonPeriodicPoint(Vector3[] controls, double[] weights, double[] knots,
            int degree, double parameter, bool signedWeights)
        {
            int span;
            if (parameter == knots[controls.Length])
            {
                // Evaluate the left-hand limit at the end of the active domain,
                // not the last control point of an unclamped control polygon.
                span = controls.Length - 1;
                while (span > degree && knots[span] == parameter) span--;
                if (knots[controls.Length] == knots[knots.Length - 1] && weights[weights.Length - 1] != 0)
                    return controls[controls.Length - 1];
            }
            else
            {
                int low = degree, high = controls.Length;
                while (low + 1 < high)
                {
                    int middle = low + (high - low) / 2;
                    if (parameter < knots[middle]) high = middle; else low = middle;
                }
                span = low;
                if (parameter == knots[0] && weights[0] != 0) return controls[0];
            }
            int first = span - degree;
            if (signedWeights || parameter == knots[controls.Length])
                return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first, true);

            // Local nonzero basis recurrence: O(degree^2), independent of the
            // total control count. Repeated knots outside the active span are legal.
            var basis = new double[degree + 1];
            var left = new double[degree + 1];
            var right = new double[degree + 1];
            basis[0] = 1;
            for (int j = 1; j <= degree; j++)
            {
                left[j] = parameter - knots[span + 1 - j];
                right[j] = knots[span + j] - parameter;
                double saved = 0;
                for (int r = 0; r < j; r++)
                {
                    double divisor = right[r + 1] + left[j - r];
                    if (!NonPeriodicFinite(divisor) || divisor <= 0)
                        return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
                    double temporary = basis[r] / divisor;
                    if (!NonPeriodicFinite(temporary) || (temporary == 0 && basis[r] != 0))
                        return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
                    basis[r] = saved + right[r + 1] * temporary;
                    saved = left[j - r] * temporary;
                }
                basis[j] = saved;
            }
            var coefficients = new double[degree + 1];
            var exponents = new int[degree + 1];
            for (int i = 0; i <= degree; i++)
            {
                double value = basis[i];
                if (!NonPeriodicFinite(value) || value < 0 || (value > 0 && value < 2.2250738585072014e-308)
                    || (value == 0 && knots[first + i] < parameter && parameter < knots[first + i + degree + 1]))
                    return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
                if (value == 0) continue;
                double bm = PeriodicMantissa(value, out int be);
                double wm = PeriodicMantissa(weights[first + i], out int we);
                coefficients[i] = bm * wm; exponents[i] = be + we;
            }
            var terms = (double[])coefficients.Clone(); var powers = (int[])exponents.Clone();
            double denominator = PeriodicScaledSum(terms, powers, out int denominatorExponent);
            if (!(denominator > 0))
                return PeriodicSplineExactEvaluation.Evaluate(controls, weights, knots, degree, parameter, first);
            Vector3? exact = null;
            return new Vector3(
                PeriodicCoordinate(controls, first, 0, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exact),
                PeriodicCoordinate(controls, first, 1, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exact),
                PeriodicCoordinate(controls, first, 2, coefficients, exponents, terms, powers, denominator, denominatorExponent, weights, knots, degree, parameter, ref exact));
        }
    }
}
