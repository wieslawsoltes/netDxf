// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Spline
    {
        /// <summary>Maximum exact homogeneous blends estimated for one degree-elevation operation.</summary>
        public const int MaximumDegreeElevationBlendOperations = 5000000;

        /// <summary>Raises the degree of a nonperiodic rational control-point spline without sampled refitting.</summary>
        /// <param name="times">Number of degree increments; the resulting degree must not exceed MaxDegree.</param>
        /// <returns>An independent clamped spline preserving the source active domain and traversal.</returns>
        /// <remarks>
        /// Exact homogeneous Bézier extraction and elevation precede one rounding of the stored coefficients.
        /// Continuous joins use target-degree knot multiplicity; source degree-plus-one discontinuities use
        /// target-degree-plus-one multiplicity and retain separate endpoints. Redundant knots are not removed:
        /// this is a piecewise-Bézier representation, not minimum-knot degree elevation. Positive weights,
        /// resource bounds, fit/dependency admission and numerical limits follow ToBezierSegments.
        /// </remarks>
        public Spline ElevateDegree(int times = 1)
        {
            if (times < 1 || times > MaxDegree - this.degree)
                throw new ArgumentOutOfRangeException(nameof(times), "The resulting spline degree must be between Degree+1 and MaxDegree.");
            int q = this.degree + times;
            int count = this.ValidateDegreeElevation(q);
            int breaks = 0, previous = -1;
            for (int span = this.degree; span < this.controlPoints.Length; span++)
            {
                if (this.knots[span] == this.knots[span + 1]) continue;
                if (previous >= 0 && span - previous == this.degree + 1) breaks++;
                previous = span;
            }
            int controlCount = count * q + 1 + breaks; // bounded by validated count*(q+1)
            var points = new Vector3[controlCount]; var resultWeights = new double[controlCount];
            var resultKnots = new double[controlCount + q + 1];
            int at = 0, knotAt = 0; previous = -1;
            for (int span = this.degree; span < this.controlPoints.Length; span++)
            {
                if (this.knots[span] == this.knots[span + 1]) continue;
                bool broken = previous >= 0 && span - previous == this.degree + 1;
                int repeated = previous < 0 || broken ? q + 1 : q;
                for (int i = 0; i < repeated; i++) resultKnots[knotAt++] = this.knots[span];
                PeriodicSplineExactEvaluation.ElevateBezierSpan(this.controlPoints, this.weights,
                    this.knots, this.degree, span, q, out Vector3[] local, out double[] localWeights);
                int first = previous < 0 || broken ? 0 : 1;
                // A continuous source has the same exact homogeneous endpoint
                // from both adjacent blossoms. Keep only one stored copy.
                if (first == 1 && (points[at - 1].X != local[0].X || points[at - 1].Y != local[0].Y
                    || points[at - 1].Z != local[0].Z || resultWeights[at - 1] != localWeights[0]))
                    throw new InvalidOperationException("Adjacent elevated spans have inconsistent continuous endpoints.");
                Array.Copy(local, first, points, at, q + 1 - first);
                Array.Copy(localWeights, first, resultWeights, at, q + 1 - first);
                at += q + 1 - first; previous = span;
            }
            for (int i = 0; i <= q; i++) resultKnots[knotAt++] = this.knots[this.controlPoints.Length];
            var result = new Spline(points, resultWeights, resultKnots, (short)q, Array.Empty<Vector3>(),
                SplineCreationMethod.ControlPoints, false)
            {
                knotTolerance = this.knotTolerance, ctrlPointTolerance = this.ctrlPointTolerance,
                fitTolerance = this.fitTolerance, knotParameterization = this.knotParameterization
            };
            CurvePolylineConversion.CopyAppearance(this, result, base.Normal);
            return result;
        }
        private int ValidateDegreeElevation(int targetDegree)
        {
            int spans = this.ValidateBasisConversion(this.degree, false, out _);
            if ((long)spans * (targetDegree + 1) > MaximumRefinedControlPoints)
                throw new NotSupportedException("The degree-elevation output control-point budget would be exceeded.");
            long blends = (long)this.degree * (this.degree + 1) * (this.degree + 1) / 2;
            for (int q = this.degree + 1; q <= targetDegree; q++) blends += q - 1;
            if (blends * spans > MaximumDegreeElevationBlendOperations)
                throw new NotSupportedException("The exact degree-elevation algebra budget would be exceeded.");
            return spans;
        }
    }
}
