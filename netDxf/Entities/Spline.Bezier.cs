// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Spline
    {
        /// <summary>Maximum estimated local algebra steps for one whole-curve basis conversion.</summary>
        public const int MaximumBasisConversionSteps = 20000000;

        /// <summary>Returns independent rational Bezier splines for every nonempty active knot span.</summary>
        /// <returns>Clamped, degree-preserving splines in parameter order, on their original intervals.</returns>
        /// <remarks>
        /// Supports clamped and unclamped nonperiodic control-point definitions with finite positive
        /// weights. Empty knot spans are omitted; full-multiplicity breaks keep distinct one-sided
        /// endpoints. Exact homogeneous blossom evaluation precedes one rounding of each stored
        /// coefficient. Fit constraints and private dependencies require explicit conversion and reject.
        /// No sampling, fitting, parameter rescaling or source mutation is performed.
        /// </remarks>
        public Spline[] ToBezierSegments()
        {
            int count = this.ValidateBasisConversion(this.degree, false, out _);
            var result = new Spline[count];
            int next = 0;
            for (int span = this.degree; span < this.controlPoints.Length; span++)
            {
                double a = this.knots[span], b = this.knots[span + 1];
                if (a == b) continue;
                PeriodicSplineExactEvaluation.ExtractBezierSpan(this.controlPoints, this.weights,
                    this.knots, this.degree, span, out Vector3[] points, out double[] resultWeights);
                var resultKnots = new double[2 * (this.degree + 1)];
                for (int i = 0; i <= this.degree; i++)
                { resultKnots[i] = a; resultKnots[this.degree + 1 + i] = b; }
                result[next++] = this.CreateKnotResult(points, resultWeights, resultKnots,
                    Array.Empty<Vector3>(), SplineCreationMethod.ControlPoints, null, null);
            }
            return result;
        }

        private int ValidateBasisConversion(int targetDegree, bool clamped, out int elevatedCount)
        {
            this.ValidateRefinementDefinition(0);
            if (this.creationMethod != SplineCreationMethod.ControlPoints || this.fitPoints.Length != 0
                || this.startTangent.HasValue || this.endTangent.HasValue)
                throw new NotSupportedException("Basis conversion requires control points without fit constraints.");
            if (clamped)
            {
                double a = this.knots[this.degree], b = this.knots[this.controlPoints.Length];
                for (int i = 0; i <= this.degree; i++)
                    if (this.knots[i] != a || this.knots[this.knots.Length - 1 - i] != b)
                        throw new NotSupportedException("Degree elevation requires clamped active-domain endpoints.");
            }
            int spans = 0, repeat = 0;
            long controls = targetDegree + 1;
            for (int i = this.degree; i < this.controlPoints.Length; i++)
            {
                repeat++;
                if (this.knots[i] == this.knots[i + 1]) continue;
                if (spans != 0) controls += repeat + targetDegree - this.degree;
                spans++;
                repeat = 0;
            }
            long storedControls = clamped ? controls : (long)spans * (this.degree + 1);
            if (storedControls > MaximumRefinedControlPoints)
                throw new NotSupportedException("The basis-conversion control-point budget would be exceeded.");
            // Blossom work is cubic in degree, not in the caller's control count.
            // Elevation and local knot removal add a bounded quadratic tail.
            long steps = (long)spans * ((this.degree + 1) * this.degree * (this.degree + 1) / 2
                + (clamped ? 4 * (targetDegree + 1) * (targetDegree + 1) : 0));
            if (steps > MaximumBasisConversionSteps)
                throw new NotSupportedException("The basis-conversion algebra budget would be exceeded.");
            elevatedCount = (int)controls;
            return spans;
        }
    }
}
