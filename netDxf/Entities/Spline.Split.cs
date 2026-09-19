// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Spline
    {
        /// <summary>Splits a clamped, nonperiodic control-point spline at an interior parameter.</summary>
        /// <param name="parameter">Finite parameter strictly inside the active knot domain.</param>
        /// <returns>Two independent splines in traversal order, retaining the original parameter intervals.</returns>
        /// <remarks>
        /// Uses knot insertion, not sampled refitting. A continuous join shares identical endpoint values,
        /// not mutable arrays. A pre-existing degree-plus-one discontinuity keeps its distinct one-sided
        /// endpoints. Positive weights, supported degree and the InsertKnot resource/numerical contract
        /// apply. Fit-point definitions or optional fit tangents require explicit conversion and reject.
        /// Source geometry, metadata and ownership remain unchanged, including after failure.
        /// </remarks>
        public Spline[] SplitAt(double parameter)
        {
            this.ValidateKnotOperation(parameter, 0, out int span, out int multiplicity);
            if (this.creationMethod != SplineCreationMethod.ControlPoints || this.fitPoints.Length != 0
                || this.startTangent.HasValue || this.endTangent.HasValue)
                throw new NotSupportedException("SplitAt requires a control-point definition without fit points or fit tangents.");
            double first = this.knots[this.degree], last = this.knots[this.controlPoints.Length];
            for (int i = 0; i <= this.degree; i++)
                if (this.knots[i] != first || this.knots[this.knots.Length - 1 - i] != last)
                    throw new NotSupportedException("SplitAt requires clamped active-domain endpoints.");

            int extra = Math.Max(0, this.degree - multiplicity);
            Vector3[] points = this.controlPoints;
            double[] resultWeights = this.weights, resultKnots = this.knots;
            if (extra > 0)
            {
                if ((long)points.Length + extra > MaximumRefinedControlPoints)
                    throw new NotSupportedException("The split refinement control-point budget would be exceeded.");
                PeriodicSplineExactEvaluation.InsertKnot(points, resultWeights, resultKnots,
                    this.degree, parameter, span, multiplicity, extra, out points, out resultWeights, out resultKnots);
                span += extra;
                multiplicity += extra;
            }

            // At multiplicity p the curve meets at one control. At p+1 the
            // break already separates two control polygons and must stay broken.
            bool continuous = multiplicity == this.degree;
            int rightFirst = span - this.degree;
            int leftCount = rightFirst + (continuous ? 1 : 0);
            int rightCount = points.Length - rightFirst;
            var leftPoints = new Vector3[leftCount]; var rightPoints = new Vector3[rightCount];
            var leftWeights = new double[leftCount]; var rightWeights = new double[rightCount];
            Array.Copy(points, 0, leftPoints, 0, leftCount);
            Array.Copy(points, rightFirst, rightPoints, 0, rightCount);
            Array.Copy(resultWeights, 0, leftWeights, 0, leftCount);
            Array.Copy(resultWeights, rightFirst, rightWeights, 0, rightCount);
            var leftKnots = new double[leftCount + this.degree + 1];
            var rightKnots = new double[rightCount + this.degree + 1];
            Array.Copy(resultKnots, 0, leftKnots, 0, span + 1);
            if (continuous)
            {
                leftKnots[leftKnots.Length - 1] = parameter;
                rightKnots[0] = parameter;
                Array.Copy(resultKnots, rightFirst + 1, rightKnots, 1, rightKnots.Length - 1);
            }
            else Array.Copy(resultKnots, rightFirst, rightKnots, 0, rightKnots.Length);

            var left = this.CreateKnotResult(leftPoints, leftWeights, leftKnots,
                Array.Empty<Vector3>(), SplineCreationMethod.ControlPoints, null, null);
            var right = this.CreateKnotResult(rightPoints, rightWeights, rightKnots,
                Array.Empty<Vector3>(), SplineCreationMethod.ControlPoints, null, null);
            return new[] { left, right };
        }
    }
}
