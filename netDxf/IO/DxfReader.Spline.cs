// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using netDxf.Entities;

namespace netDxf.IO
{
    internal sealed partial class DxfReader
    {
        private static bool CanCompactPeriodicSpline(List<Vector3> controls, List<double> weights,
            List<double> knots, short degree)
        {
            if (controls.Count < 2 * degree + 1) return false;
            for (int i = 0; i < degree; i++)
            {
                int tail = controls.Count - degree + i;
                Vector3 a = controls[i], b = controls[tail];
                if (!PeriodicSplineData.Same(a.X, b.X) || !PeriodicSplineData.Same(a.Y, b.Y)
                    || !PeriodicSplineData.Same(a.Z, b.Z)
                    || (weights != null && !PeriodicSplineData.Same(weights[i], weights[tail]))) return false;
            }
            // The compact evaluator has stricter cyclic-span admission rules.
            // A repeated-knot or noncyclic basis remains fully representable in
            // explicit form, without adjusting its controls, weights or knots.
            try
            {
                Vector3[] compact = controls.Skip(degree).ToArray();
                double[] compactWeights = weights == null
                    ? Enumerable.Repeat(1.0, compact.Length).ToArray() : weights.Skip(degree).ToArray();
                PeriodicSplineData.Validate(compact, compactWeights, knots.ToArray(), degree);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }
}
