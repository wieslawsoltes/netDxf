// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    internal static class HatchSplineData
    {
        internal static void Validate(HatchBoundaryPath.Spline spline)
        {
            if (spline.Degree < 1)
                throw new ArgumentException("HATCH spline degree must be positive.");
            if (spline.Knots == null || spline.ControlPoints == null)
                throw new ArgumentException("HATCH spline knots and control points must be supplied.");
            // Periodic wire representations differ from the compressed ObjectARX
            // API representation. Do not impose an unqualified periodic relation.
            if (!spline.IsPeriodic)
            {
                if (spline.ControlPoints.Length < (int)spline.Degree + 1)
                    throw new ArgumentException("A nonperiodic HATCH spline requires at least degree plus one control points.");
                if ((long)spline.Knots.Length != (long)spline.ControlPoints.Length + spline.Degree + 1)
                    throw new ArgumentException("A nonperiodic HATCH spline knot count must equal its control count plus degree plus one.");
            }
            for (int i = 0; i < spline.Knots.Length; i++)
            {
                if (!Finite(spline.Knots[i]))
                    throw new ArgumentException("HATCH spline knots must be finite.");
                if (i > 0 && spline.Knots[i] < spline.Knots[i - 1])
                    throw new ArgumentException("HATCH spline knots must be nondecreasing.");
            }
            foreach (Vector3 point in spline.ControlPoints)
                if (!Finite(point.X) || !Finite(point.Y) || !Finite(point.Z))
                    throw new ArgumentException("HATCH spline control coordinates and stored weights must be finite.");
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
