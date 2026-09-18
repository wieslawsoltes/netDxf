// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    /// <summary>Non-tessellated rational quadratic conversion of circular and elliptical curves.</summary>
    public static class ConicSplineExtensions
    {
        /// <summary>Converts a zero-thickness circle to a detached rational quadratic spline.</summary>
        /// <param name="circle">Circle whose WCS geometry and ordinary appearance are copied.</param>
        /// <returns>A closed, nonperiodic, clamped spline with four conic spans.</returns>
        /// <remarks>Source identities, proxies and unsupported dependency graphs are not copied.</remarks>
        public static Spline ToSpline(this Circle circle)
        {
            if (circle == null) throw new ArgumentNullException(nameof(circle));
            return Create(circle, circle.Center, circle.Normal, circle.Radius, circle.Radius,
                0, 0, MathHelper.TwoPI, true, circle.Thickness);
        }

        /// <summary>Converts a zero-thickness counterclockwise circular arc to a rational quadratic spline.</summary>
        /// <param name="arc">Arc whose WCS geometry and ordinary appearance are copied.</param>
        /// <returns>A detached clamped spline with one to four conic spans.</returns>
        /// <remarks>An equal-angle, zero-sweep arc rejects; use a Circle for a complete circle.</remarks>
        public static Spline ToSpline(this Arc arc)
        {
            if (arc == null) throw new ArgumentNullException(nameof(arc));
            ConicParameter.Finite(arc.StartAngle); ConicParameter.Finite(arc.EndAngle);
            double start = arc.StartAngle * MathHelper.DegToRad, end = arc.EndAngle * MathHelper.DegToRad;
            if (end < start) end += MathHelper.TwoPI;
            return Create(arc, arc.Center, arc.Normal, arc.Radius, arc.Radius, 0, start, end, false, arc.Thickness);
        }

        /// <summary>Converts a zero-thickness ellipse or elliptical arc to a rational quadratic spline.</summary>
        /// <param name="ellipse">Ellipse whose plane, sweep and ordinary appearance are copied.</param>
        /// <returns>A detached clamped spline; full ellipses have an exactly repeated seam control.</returns>
        /// <remarks>
        /// Public ellipse angles are polar angles, not eccentric parameters. The existing
        /// IsFullEllipse convention is retained. Conversion has no tessellation precision.
        /// </remarks>
        public static Spline ToSpline(this Ellipse ellipse)
        {
            if (ellipse == null) throw new ArgumentNullException(nameof(ellipse));
            ConicParameter.CheckAxes(ellipse.MajorAxis, ellipse.MinorAxis);
            ConicParameter.Finite(ellipse.StartAngle); ConicParameter.Finite(ellipse.EndAngle);
            bool full = ellipse.IsFullEllipse;
            double start = full ? 0 : ConicParameter.FromPolar(ellipse.MajorAxis, ellipse.MinorAxis, ellipse.StartAngle);
            double end = full ? MathHelper.TwoPI : ConicParameter.FromPolar(ellipse.MajorAxis, ellipse.MinorAxis, ellipse.EndAngle);
            if (!full && end < start) end += MathHelper.TwoPI;
            return Create(ellipse, ellipse.Center, ellipse.Normal, ellipse.MajorAxis * .5, ellipse.MinorAxis * .5,
                ellipse.Rotation * MathHelper.DegToRad, start, end, full, ellipse.Thickness);
        }

        private static Spline Create(EntityObject source, Vector3 center, Vector3 normal,
            double a, double b, double rotation, double start, double end, bool closed, double thickness)
        {
            CurvePolylineConversion.CheckDependencies(source);
            CurvePolylineConversion.CheckPoint(center); CurvePolylineConversion.CheckNormal(normal);
            ConicParameter.Finite(a); ConicParameter.Finite(b); ConicParameter.Finite(rotation);
            ConicParameter.Finite(start); ConicParameter.Finite(end); ConicParameter.Finite(thickness);
            if (a <= 0 || b <= 0 || b > a)
                throw new InvalidOperationException("Conic radii must be positive and ordered.");
            if (thickness != 0)
                throw new NotSupportedException("A SPLINE cannot preserve a conic's nonzero extrusion thickness.");
            double sweep = end - start;
            if (sweep <= 0 || sweep > MathHelper.TwoPI)
                throw new NotSupportedException("A conic spline requires a representable positive sweep of at most one turn.");
            int spans = Math.Min(4, (int)Math.Ceiling(sweep / MathHelper.HalfPI));
            var controls = new Vector3[2 * spans + 1];
            var weights = new double[controls.Length];
            var knots = new double[controls.Length + 3];
            Matrix3 ocs = MathHelper.ArbitraryAxis(normal);
            ConicParameter.SinCos(rotation, out double sr, out double cr);
            Matrix3 frame = ocs * new Matrix3(cr, -sr, 0, sr, cr, 0, 0, 0, 1);
            for (int span = 0; span < spans; span++)
            {
                double lo = start + sweep * ((double)span / spans);
                double hi = span == spans - 1 ? end : start + sweep * ((double)(span + 1) / spans);
                double half = (hi - lo) * .5;
                if (half <= 0) throw new NotSupportedException("Conic span endpoints coincide in binary64.");
                double weight = Math.Cos(half);
                if (span == 0) controls[0] = Control(frame, center, a, b, lo, 1);
                controls[2 * span + 1] = Control(frame, center, a, b, lo + half, weight);
                controls[2 * span + 2] = Control(frame, center, a, b, hi, 1);
                weights[2 * span] = 1; weights[2 * span + 1] = weight; weights[2 * span + 2] = 1;
                if (span > 0) { knots[2 * span + 1] = span; knots[2 * span + 2] = span; }
            }
            knots[knots.Length - 3] = spans; knots[knots.Length - 2] = spans; knots[knots.Length - 1] = spans;
            if (closed) controls[controls.Length - 1] = controls[0];
            var result = new Spline(controls, weights, knots, 2, false);
            // The existing model determines closure from tolerance-based endpoint
            // equality. Do not silently turn a small open arc into a closed curve.
            if (result.IsClosed != closed)
                throw new NotSupportedException("The conic sweep is indistinguishable under the spline model's closure tolerance.");
            CurvePolylineConversion.CopyAppearance(source, result, normal);
            return result;
        }

        private static Vector3 Control(Matrix3 frame, Vector3 center, double a, double b, double parameter, double weight)
        {
            ConicParameter.SinCos(parameter, out double sine, out double cosine);
            var local = new Vector3(a * (cosine / weight), b * (sine / weight), 0);
            if (double.IsNaN(local.X) || double.IsInfinity(local.X) || double.IsNaN(local.Y) || double.IsInfinity(local.Y))
                throw new NotSupportedException("The rational conic control exceeds the finite binary64 range.");
            // One final rounding for each affine coordinate; no partially created
            // target is exposed if a control cannot be represented.
            return InfiniteLineTransform.TransformPoint(frame, local, center);
        }
    }
}
