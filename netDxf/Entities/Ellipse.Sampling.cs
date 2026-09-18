// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    public partial class Ellipse
    {
        /// <summary>Maximum number of vertices allocated by one ellipse sampling call.</summary>
        public const int MaximumSampledVertices = 1000000;

        private List<Vector2> SampleEllipse(int precision)
        {
            if (precision < 2 || precision > MaximumSampledVertices)
                throw new ArgumentOutOfRangeException(nameof(precision), precision,
                    "Ellipse sampling requires between two and MaximumSampledVertices points.");
            ConicParameter.CheckAxes(this.majorAxis, this.minorAxis);
            ConicParameter.Finite(this.rotation); ConicParameter.Finite(this.startAngle); ConicParameter.Finite(this.endAngle);
            bool full = this.IsFullEllipse;
            double start = full ? 0 : ConicParameter.FromPolar(this.majorAxis, this.minorAxis, this.startAngle);
            double end = full ? MathHelper.TwoPI : ConicParameter.FromPolar(this.majorAxis, this.minorAxis, this.endAngle);
            if (!full && end < start) end += MathHelper.TwoPI;
            if (!full && end == start)
                throw new NotSupportedException("Distinct ellipse angles have indistinguishable binary64 parameters.");
            ConicParameter.SinCos(this.rotation * MathHelper.DegToRad, out double sr, out double cr);
            // Halve before adding rotated terms: a diameter sum can overflow even
            // when every point on a finite ellipse is representable.
            double a = this.majorAxis * 0.5, b = this.minorAxis * 0.5;
            int intervals = full ? precision : precision - 1;
            var points = new List<Vector2>(precision);
            for (int i = 0; i < precision; i++)
            {
                double parameter = !full && i == precision - 1 ? end : start + (end - start) * ((double)i / intervals);
                ConicParameter.SinCos(parameter, out double sine, out double cosine);
                double x = a * cosine, y = b * sine;
                points.Add(new Vector2(x * cr - y * sr, x * sr + y * cr));
            }
            return points;
        }
    }

    // Polar angles are public ellipse angles; DXF uses eccentric parameters.
    // No reciprocals of tiny semi-axes and no coordinate-based angle inference.
    internal static class ConicParameter
    {
        internal static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Conic geometry must be finite.");
        }

        internal static void CheckAxes(double major, double minor)
        {
            Finite(major); Finite(minor);
            if (major <= 0 || minor <= 0 || minor > major)
                throw new InvalidOperationException("Ellipse axes must be positive and ordered.");
            if (major * 0.5 == 0 || minor * 0.5 == 0)
                throw new NotSupportedException("Ellipse semi-axes must be representable as nonzero binary64 values.");
        }

        internal static double FromPolar(double major, double minor, double angle)
        {
            Finite(angle);
            double reduced = angle % 360;
            if (reduced < 0) reduced += 360;
            // Exact quadrants must not inherit sin(pi)'s residual on thin ellipses.
            if (reduced == 0) return 0;
            if (reduced == 90) return MathHelper.HalfPI;
            if (reduced == 180) return Math.PI;
            if (reduced == 270) return Math.PI + MathHelper.HalfPI;
            double ratio = minor / major;
            if (ratio == 0)
                throw new NotSupportedException("Ellipse axis ratio is too small for a nonquadrant parameter.");
            double radians = reduced * MathHelper.DegToRad;
            double parameter = Math.Atan2(Math.Sin(radians), ratio * Math.Cos(radians));
            return parameter < 0 ? parameter + MathHelper.TwoPI : parameter;
        }

        internal static void SinCos(double parameter, out double sine, out double cosine)
        {
            double p = parameter % MathHelper.TwoPI;
            if (p < 0) p += MathHelper.TwoPI;
            if (p == 0) { sine = 0; cosine = 1; }
            else if (p == MathHelper.HalfPI) { sine = 1; cosine = 0; }
            else if (p == Math.PI) { sine = 0; cosine = -1; }
            else if (p == Math.PI + MathHelper.HalfPI) { sine = -1; cosine = 0; }
            else { sine = Math.Sin(p); cosine = Math.Cos(p); }
        }
    }
}
