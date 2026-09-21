// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using netDxf.Entities;

namespace netDxf.IO
{
    // DXF stores an eccentric parameter; the public entity exposes polar degrees.
    // Decide closure from parameters, never from approximately equal coordinates.
    internal static class DxfEllipseParameterCodec
    {
        internal static void ReadAxes(Vector3 axis, Vector3 normal, double ratio,
            out double major, out double minor, out double rotation)
        {
            Finite(axis.X); Finite(axis.Y); Finite(axis.Z); Finite(ratio);
            if (ratio <= 0 || ratio > 1)
                throw new ArgumentOutOfRangeException(nameof(ratio), "ELLIPSE ratio must be in (0,1].");
            double scale = Math.Max(Math.Abs(axis.X), Math.Max(Math.Abs(axis.Y), Math.Abs(axis.Z)));
            if (scale == 0) throw new ArgumentException("ELLIPSE major semi-axis must be nonzero.", nameof(axis));
            // Divide each component, not by an overflowing reciprocal of scale.
            Vector3 unitScale = new Vector3(axis.X / scale, axis.Y / scale, axis.Z / scale);
            double length = Math.Sqrt(Vector3.DotProduct(unitScale, unitScale));
            major = scale * (2 * length);
            minor = major * ratio;
            if (double.IsInfinity(major) || minor == 0)
                throw new NotSupportedException("ELLIPSE diameters are not representable by the typed entity.");
            ConicParameter.CheckAxes(major, minor);
            Vector3 local = MathHelper.ArbitraryAxis(normal).Transpose() * unitScale;
            if (local.X == 0 && local.Y == 0)
                throw new NotSupportedException("ELLIPSE major axis has no representable component in its plane.");
            rotation = Math.Atan2(local.Y, local.X) * MathHelper.RadToDeg;
        }

        internal static void Decode(Ellipse ellipse, double start, double end)
        {
            Finite(start); Finite(end);
            double first = Phase(start), last = Phase(end);
            if (first == last)
            {
                ellipse.StartAngle = 0;
                ellipse.EndAngle = 0;
                return;
            }
            double a = Polar(ellipse.MajorAxis, ellipse.MinorAxis, first);
            double b = Polar(ellipse.MajorAxis, ellipse.MinorAxis, last);
            if (a == b)
                throw new NotSupportedException("Distinct ELLIPSE parameters cannot be represented by distinct polar angles.");
            // Publish only after both conversions have succeeded.
            ellipse.StartAngle = a;
            ellipse.EndAngle = b;
        }

        internal static double[] Encode(Ellipse ellipse)
        {
            ConicParameter.CheckAxes(ellipse.MajorAxis, ellipse.MinorAxis);
            Finite(ellipse.StartAngle); Finite(ellipse.EndAngle);
            if (ellipse.IsFullEllipse) return new[] { 0.0, MathHelper.TwoPI };
            double first = ConicParameter.FromPolar(ellipse.MajorAxis, ellipse.MinorAxis, ellipse.StartAngle);
            double last = ConicParameter.FromPolar(ellipse.MajorAxis, ellipse.MinorAxis, ellipse.EndAngle);
            if (first == last)
                throw new NotSupportedException("Distinct ELLIPSE polar angles cannot be represented by distinct parameters.");
            // Retain the existing writer's signed atan2 range for non-full arcs.
            return new[] { Signed(first), Signed(last) };
        }

        internal static Vector3 WriteAxis(Ellipse ellipse)
        {
            ConicParameter.CheckAxes(ellipse.MajorAxis, ellipse.MinorAxis);
            Finite(ellipse.Rotation);
            double ratio = ellipse.MinorAxis / ellipse.MajorAxis;
            if (ratio == 0)
                throw new NotSupportedException("ELLIPSE ratio underflows in the DXF representation.");
            ConicParameter.SinCos(ellipse.Rotation * MathHelper.DegToRad, out double sine, out double cosine);
            double radius = ellipse.MajorAxis * 0.5;
            Vector3 axis = MathHelper.ArbitraryAxis(ellipse.Normal) * new Vector3(radius * cosine, radius * sine, 0);
            Finite(axis.X); Finite(axis.Y); Finite(axis.Z);
            if (axis.X == 0 && axis.Y == 0 && axis.Z == 0)
                throw new NotSupportedException("ELLIPSE major semi-axis underflows in world coordinates.");
            return axis;
        }

        private static double Polar(double major, double minor, double parameter)
        {
            if (parameter == 0) return 0;
            if (parameter == MathHelper.HalfPI) return 90;
            if (parameter == Math.PI) return 180;
            if (parameter == Math.PI + MathHelper.HalfPI) return 270;
            double ratio = minor / major;
            ConicParameter.SinCos(parameter, out double sine, out double cosine);
            double y = ratio * sine;
            if (ratio == 0 || sine != 0 && y == 0)
                throw new NotSupportedException("ELLIPSE polar-angle conversion underflows.");
            double result = Math.Atan2(y, cosine) * MathHelper.RadToDeg;
            if (result < 0) result += 360;
            return result == 360 || result == 0 ? 0 : result;
        }
        private static double Phase(double value)
        {
            double result = value % MathHelper.TwoPI;
            if (result < 0) result += MathHelper.TwoPI;
            return result == MathHelper.TwoPI || result == 0 ? 0 : result;
        }
        private static double Signed(double value) { return value > Math.PI ? value - MathHelper.TwoPI : value; }
        private static void Finite(double value) { ConicParameter.Finite(value); }
    }
}
