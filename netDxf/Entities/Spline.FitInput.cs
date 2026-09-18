// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace netDxf.Entities
{
    public partial class Spline
    {
        // Capture the caller's enumerable before fitting. Fitting and the
        // retained fit-point packet must describe the very same traversal.
        private sealed class FitPointSnapshot
        {
            internal readonly Vector3[] Points;
            internal FitPointSnapshot(IEnumerable<Vector3> fitPoints)
            {
                if (fitPoints == null) throw new ArgumentNullException(nameof(fitPoints));
                this.Points = fitPoints.ToArray();
            }
        }

        private Spline(FitPointSnapshot snapshot)
            : this(BezierCurveCubic.CreateFromFitPoints(snapshot.Points))
        {
            this.creationMethod = SplineCreationMethod.FitPoints;
            this.fitPoints = snapshot.Points;
        }
    }
}
