// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    /// <summary>Selects a one-sided limit when evaluating a SPLINE at a knot.</summary>
    public enum SplineParameterSide
    {
        /// <summary>Use the right-hand limit, except at the active end where the left-hand limit is used.</summary>
        Automatic = 0,
        /// <summary>Use the left-hand limit. Not available at the active-domain start.</summary>
        Left = 1,
        /// <summary>Use the right-hand limit. Not available at the active-domain end.</summary>
        Right = 2
    }

    public partial class Spline
    {
        /// <summary>Maximum derivative order supported by exact parameter evaluation.</summary>
        public const int MaximumDerivativeOrder = 10;

        /// <summary>Evaluates the stored rational spline at an original knot-domain parameter.</summary>
        /// <param name="parameter">Finite parameter in the closed active knot domain.</param>
        /// <param name="side">The desired one-sided limit at a knot.</param>
        /// <returns>The world-coordinate point, without tessellation or sampled fitting.</returns>
        /// <remarks>See EvaluateDerivatives for supported definitions and numerical boundaries.</remarks>
        public Vector3 PointAt(double parameter, SplineParameterSide side = SplineParameterSide.Automatic)
        {
            return this.EvaluateDerivatives(parameter, 0, side)[0];
        }

        /// <summary>Evaluates a point and its parameter derivatives, not normalized tangent directions.</summary>
        /// <param name="parameter">Finite parameter in the original closed active knot domain.</param>
        /// <param name="order">Highest derivative order, from zero through MaximumDerivativeOrder.</param>
        /// <param name="side">One-sided limit. Automatic selects right, except left at the active end.</param>
        /// <returns>An independent array containing position at index zero and derivative k at index k.</returns>
        /// <remarks>
        /// Evaluates the stored control definition; fit points and optional fit tangents are not refitted.
        /// Nonperiodic degrees 1 through 10 support finite signed or zero weights when the selected
        /// denominator is nonzero. Compact periodic definitions retain the existing cyclic-knot and
        /// positive-weight admission rules. Parameters are not wrapped, clamped or rescaled.
        /// Exact local homogeneous Taylor arithmetic and rational division precede one ties-to-even
        /// binary64 rounding per result component. Derivatives above the degree need not vanish for
        /// rational curves. A zero denominator rejects without inferring a removable singularity.
        /// Overflow rejects; representable subnormals and rounded underflow to signed zero are retained.
        /// Exact zero is returned as positive zero. Source arrays, metadata and proxy state do not change.
        /// Full input validation is linear in stored size; exact work uses at most eleven local controls,
        /// orders zero through ten and the shared integer-size budget. No global CPU bound is implied.
        /// Concurrent mutation of caller-visible source arrays is not supported.
        /// </remarks>
        public Vector3[] EvaluateDerivatives(double parameter, int order = 1,
            SplineParameterSide side = SplineParameterSide.Automatic)
        {
            if (!NonPeriodicFinite(parameter))
                throw new ArgumentOutOfRangeException(nameof(parameter), "The parameter must be finite.");
            if (order < 0 || order > MaximumDerivativeOrder)
                throw new ArgumentOutOfRangeException(nameof(order), "The derivative order must be between zero and ten.");
            if (side < SplineParameterSide.Automatic || side > SplineParameterSide.Right)
                throw new ArgumentOutOfRangeException(nameof(side), "Unknown parameter side.");
            this.ValidateParameterDefinition();
            int count = this.controlPoints.Length + (this.isClosedPeriodic ? this.degree : 0);
            double start = this.knots[this.degree], end = this.knots[count];
            if (parameter < start || parameter > end)
                throw new ArgumentOutOfRangeException(nameof(parameter), "The parameter lies outside the active domain.");
            bool left = side == SplineParameterSide.Left ||
                (side == SplineParameterSide.Automatic && parameter == end);
            if ((left && parameter == start) || (!left && parameter == end))
                throw new ArgumentOutOfRangeException(nameof(side), "The requested limit lies outside the active domain.");

            // Lower/upper-bound search selects an actual nonempty span, even
            // at repeated knots and degree-plus-one discontinuities.
            int lo = this.degree, hi = count + 1;
            while (lo < hi)
            {
                int middle = lo + (hi - lo) / 2;
                if (this.knots[middle] < parameter || (!left && this.knots[middle] == parameter))
                    lo = middle + 1;
                else hi = middle;
            }
            int span = lo - 1;
            if (span < this.degree || span >= count || !(this.knots[span] < this.knots[span + 1]))
                throw new InvalidOperationException("No nonempty spline span supports the requested limit.");
            return PeriodicSplineExactEvaluation.Derivatives(this.controlPoints, this.weights, this.knots,
                this.degree, this.isClosedPeriodic, span, parameter, order);
        }

        private void ValidateParameterDefinition()
        {
            if (this.isClosedPeriodic)
            {
                PeriodicSplineData.Validate(this.controlPoints, this.weights, this.knots, this.degree);
                return;
            }
            if (this.degree < 1 || this.degree > MaxDegree || this.controlPoints == null ||
                this.weights == null || this.knots == null || this.controlPoints.Length <= this.degree ||
                this.weights.Length != this.controlPoints.Length ||
                (long)this.knots.Length != (long)this.controlPoints.Length + this.degree + 1)
                throw new InvalidOperationException("Spline degree, control, weight and knot counts must agree.");
            for (int i = 0; i < this.controlPoints.Length; i++)
            {
                Vector3 point = this.controlPoints[i];
                if (!NonPeriodicFinite(point.X) || !NonPeriodicFinite(point.Y) ||
                    !NonPeriodicFinite(point.Z) || !NonPeriodicFinite(this.weights[i]))
                    throw new InvalidOperationException("Spline controls and weights must be finite.");
            }
            int repeat = 0;
            for (int i = 0; i < this.knots.Length; i++)
            {
                double knot = this.knots[i];
                if (!NonPeriodicFinite(knot) || (i > 0 && knot < this.knots[i - 1]))
                    throw new InvalidOperationException("Spline knots must be finite and nondecreasing.");
                repeat = i > 0 && knot == this.knots[i - 1] ? repeat + 1 : 1;
                if (repeat > this.degree + 1)
                    throw new InvalidOperationException("Spline knot multiplicity exceeds degree plus one.");
            }
            if (!(this.knots[this.degree] < this.knots[this.controlPoints.Length]))
                throw new InvalidOperationException("The spline active domain must have positive length.");
        }
    }
}
