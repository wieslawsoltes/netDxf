// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Spline
    {
        /// <summary>Maximum estimated homogeneous blends for one parameter-interval trim.</summary>
        public const int MaximumTrimBlendOperations = 5000000;

        /// <summary>Returns the specified active parameter interval as an independent rational spline.</summary>
        /// <param name="startParameter">Finite inclusive start, at or after the active-domain start.</param>
        /// <param name="endParameter">Finite end, after the start and at or before the active-domain end.</param>
        /// <returns>A clamped, degree-preserving spline on the original unscaled parameter interval.</returns>
        /// <remarks>
        /// Supports ordinary nonperiodic positive-weight control-point definitions, including unclamped
        /// endpoints. The start uses the right-hand limit; the end uses the left-hand limit. Interior
        /// discontinuities are retained. Exact homogeneous restriction precedes one projection of each
        /// stored coefficient. No sampled fitting, parameter normalization or source mutation occurs.
        /// Continuous interior joins use degree knot multiplicity: redundant knots are not removed.
        /// Fit constraints, derived entities and private dependencies reject. Generated controls and
        /// exact blends are bounded; caller metadata size and total process memory are not bounded.
        /// </remarks>
        public Spline Trim(double startParameter, double endParameter)
        {
            if (!NonPeriodicFinite(startParameter))
                throw new ArgumentOutOfRangeException(nameof(startParameter), "Trim start must be finite.");
            if (!NonPeriodicFinite(endParameter) || endParameter <= startParameter)
                throw new ArgumentOutOfRangeException(nameof(endParameter), "Trim end must be finite and after the start.");
            this.ValidateRefinementDefinition(0);
            if (this.creationMethod != SplineCreationMethod.ControlPoints || this.fitPoints.Length != 0
                || this.startTangent.HasValue || this.endTangent.HasValue)
                throw new NotSupportedException("Trim requires a control-point definition without fitting constraints.");
            if (startParameter < this.knots[this.degree])
                throw new ArgumentOutOfRangeException(nameof(startParameter), "Trim start lies before the active domain.");
            if (endParameter > this.knots[this.controlPoints.Length])
                throw new ArgumentOutOfRangeException(nameof(endParameter), "Trim end lies after the active domain.");

            int spans = 0, breaks = 0, previous = -1;
            for (int span = this.degree; span < this.controlPoints.Length; span++)
            {
                if (!(Math.Max(startParameter, this.knots[span]) < Math.Min(endParameter, this.knots[span + 1]))) continue;
                if (previous >= 0 && span - previous == this.degree + 1) breaks++;
                spans++; previous = span;
            }
            // Bound only the retained spans; trimming a small interval need not
            // allocate or perform exact algebra on the discarded control net.
            if ((long)spans * (this.degree + 1) > MaximumRefinedControlPoints)
                throw new NotSupportedException("The trim output control-point budget would be exceeded.");
            long blends = (long)spans * this.degree * (this.degree + 1) * (this.degree + 1) / 2;
            if (blends > MaximumTrimBlendOperations)
                throw new NotSupportedException("The exact trim algebra budget would be exceeded.");
            int count = spans * this.degree + 1 + breaks;
            var points = new Vector3[count]; var weights = new double[count];
            var knots = new double[count + this.degree + 1];
            int at = 0, knotAt = 0; previous = -1;
            for (int span = this.degree; span < this.controlPoints.Length; span++)
            {
                double a = Math.Max(startParameter, this.knots[span]), b = Math.Min(endParameter, this.knots[span + 1]);
                if (!(a < b)) continue;
                bool separate = previous < 0 || span - previous == this.degree + 1;
                int repeat = separate ? this.degree + 1 : this.degree;
                for (int i = 0; i < repeat; i++) knots[knotAt++] = a;
                PeriodicSplineExactEvaluation.RestrictBezierSpan(this.controlPoints, this.weights, this.knots,
                    this.degree, span, a, b, out Vector3[] local, out double[] localWeights);
                int first = separate ? 0 : 1;
                if (!separate && (points[at - 1].X != local[0].X || points[at - 1].Y != local[0].Y
                    || points[at - 1].Z != local[0].Z || weights[at - 1] != localWeights[0]))
                    throw new InvalidOperationException("Adjacent restricted spans have inconsistent continuous endpoints.");
                Array.Copy(local, first, points, at, this.degree + 1 - first);
                Array.Copy(localWeights, first, weights, at, this.degree + 1 - first);
                at += this.degree + 1 - first; previous = span;
            }
            for (int i = 0; i <= this.degree; i++) knots[knotAt++] = endParameter;
            return this.CreateKnotResult(points, weights, knots, Array.Empty<Vector3>(),
                SplineCreationMethod.ControlPoints, null, null);
        }
    }
}
