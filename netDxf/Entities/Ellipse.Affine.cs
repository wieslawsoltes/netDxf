// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    public partial class Ellipse
    {
        // Factor the image of the two parametric semi-axis vectors, independently of
        // center/translation. A scaled 2x2 Gram eigensystem gives the major direction;
        // the cross-product area divided by the major length gives the minor length
        // without subtracting nearly equal eigenvalues.
        private void ApplyReviewedAffine(Matrix3 matrix, Vector3 translation)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) EllipseFinite(matrix[r, c]);
            EllipseFinite(translation); EllipseFinite(this.center); EllipseFinite(this.Normal);
            EllipseFinite(this.majorAxis); EllipseFinite(this.minorAxis);
            EllipseFinite(this.rotation); EllipseFinite(this.startAngle); EllipseFinite(this.endAngle); EllipseFinite(this.thickness);
            if (this.majorAxis <= 0 || this.minorAxis <= 0 || this.minorAxis > this.majorAxis)
                throw new InvalidOperationException("The source ellipse must have positive ordered axes.");
            Vector3 normal = EllipseUnit(this.Normal);
            Vector3 nextCenter = matrix * this.center + translation;
            EllipseFinite(nextCenter);
            bool identity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) identity &= matrix[r, c] == (r == c ? 1 : 0);
            if (identity)
            {
                if (translation.X == 0 && translation.Y == 0 && translation.Z == 0) return;
                this.center = nextCenter; this.ClearProxyGraphics(); return;
            }
            double major = this.majorAxis * 0.5, minor = this.minorAxis * 0.5;
            if (minor == 0 || major == 0 || minor / major == 0)
                throw new NotSupportedException("The ellipse semi-axes cannot be represented at this precision.");
            double angle = this.rotation * MathHelper.DegToRad;
            Matrix3 sourceAxes = MathHelper.ArbitraryAxis(normal);
            Vector3 a = matrix * (sourceAxes * new Vector3(major * Math.Cos(angle), major * Math.Sin(angle), 0));
            Vector3 b = matrix * (sourceAxes * new Vector3(-minor * Math.Sin(angle), minor * Math.Cos(angle), 0));
            EllipseFinite(a); EllipseFinite(b);
            double scale = Math.Max(EllipseMax(a), EllipseMax(b));
            if (scale == 0) throw new NotSupportedException("The transform collapses the ellipse.");
            a = EllipseDivide(a, scale); b = EllipseDivide(b, scale);
            Vector3 cross = Vector3.CrossProduct(a, b);
            double area = EllipseLength(cross);
            if (area == 0) throw new NotSupportedException("A rank-one ellipse image cannot be represented.");
            Vector3 nextNormal = EllipseDivide(cross, area);
            double aa = Vector3.DotProduct(a, a), bb = Vector3.DotProduct(b, b), ab = Vector3.DotProduct(a, b);
            double phase = 0.5 * Math.Atan2(2 * ab, aa - bb);
            Vector3 principal = Math.Cos(phase) * a + Math.Sin(phase) * b;
            double first = EllipseLength(principal), second = area / first;
            double nextMajor = scale * (2 * first), nextMinor = scale * (2 * second);
            EllipseFinite(nextMajor); EllipseFinite(nextMinor);
            if (nextMinor <= 0 || nextMajor <= 0 || nextMinor / nextMajor == 0)
                throw new NotSupportedException("The transformed ellipse axes underflow or have an unrepresentable aspect ratio.");
            // At a repeated singular value, roundoff can reverse the order by a few ulps.
            if (nextMinor > nextMajor)
            {
                if ((nextMinor - nextMajor) / nextMinor > 1e-12)
                    throw new NotSupportedException("The principal ellipse axes could not be resolved.");
                nextMinor = nextMajor;
            }
            Vector3 majorDirection = EllipseDivide(principal, first);
            Matrix3 targetAxes = MathHelper.ArbitraryAxis(nextNormal);
            Vector3 local = targetAxes.Transpose() * majorDirection;
            double rotationRadians = Math.Atan2(local.Y, local.X);
            double nextRotation = MathHelper.NormalizeAngle(rotationRadians * MathHelper.RadToDeg);
            // Use exactly the orientation that the public rotation will reconstruct.
            rotationRadians = nextRotation * MathHelper.DegToRad;
            Vector3 targetX = targetAxes * new Vector3(Math.Cos(rotationRadians), Math.Sin(rotationRadians), 0);
            Vector3 targetY = targetAxes * new Vector3(-Math.Sin(rotationRadians), Math.Cos(rotationRadians), 0);
            double nextStart = this.startAngle, nextEnd = this.endAngle;
            if (!this.IsFullEllipse)
            {
                nextStart = ImagePolarAngle(this.startAngle, minor / major, a, b, targetX, targetY);
                nextEnd = ImagePolarAngle(this.endAngle, minor / major, a, b, targetX, targetY);
                if (MathHelper.IsEqual(nextStart, nextEnd))
                    throw new NotSupportedException("The transformed elliptical arc endpoints are indistinguishable at this precision.");
            }
            double nextThickness = this.thickness;
            if (this.thickness != 0)
            {
                Vector3 extrusion = matrix * normal;
                double length = EllipseLength(extrusion);
                if (length == 0) nextThickness = 0;
                else
                {
                    Vector3 direction = EllipseDivide(extrusion, length);
                    if (EllipseLength(Vector3.CrossProduct(direction, nextNormal)) > 1e-12)
                        throw new NotSupportedException("Oblique extrusion cannot be represented by ellipse normal and thickness.");
                    nextThickness = this.thickness * (Vector3.DotProduct(direction, nextNormal) < 0 ? -length : length);
                    EllipseFinite(nextThickness);
                    if (nextThickness == 0) throw new NotSupportedException("Nonzero transformed thickness underflows.");
                }
            }
            // No validation or user code remains after publication starts.
            this.center = nextCenter; this.majorAxis = nextMajor; this.minorAxis = nextMinor;
            this.rotation = nextRotation; this.startAngle = nextStart; this.endAngle = nextEnd;
            this.thickness = nextThickness; this.Normal = nextNormal; this.ClearProxyGraphics();
        }

        private static double ImagePolarAngle(double angle, double ratio, Vector3 a, Vector3 b, Vector3 x, Vector3 y)
        {
            double radians = MathHelper.NormalizeAngle(angle) * MathHelper.DegToRad;
            double parameter = Math.Atan2(Math.Sin(radians), ratio * Math.Cos(radians));
            Vector3 point = Math.Cos(parameter) * a + Math.Sin(parameter) * b;
            return MathHelper.NormalizeAngle(Math.Atan2(Vector3.DotProduct(point, y), Vector3.DotProduct(point, x)) * MathHelper.RadToDeg);
        }

        private static Vector2 StablePolarPoint(double majorAxis, double minorAxis, double angle)
        {
            EllipseFinite(angle); EllipseFinite(majorAxis); EllipseFinite(minorAxis);
            double a = majorAxis * 0.5, b = minorAxis * 0.5;
            if (a <= 0 || b <= 0 || b > a) throw new InvalidOperationException("Positive ordered semi-axes are required.");
            double radians = MathHelper.NormalizeAngle(angle) * MathHelper.DegToRad;
            double cos = Math.Cos(radians), sin = Math.Sin(radians);
            if (sin == 0) return new Vector2(cos < 0 ? -a : a, 0);
            double denominator = EllipseLength(new Vector3((b / a) * cos, sin, 0));
            double radius = b / denominator;
            EllipseFinite(radius);
            return new Vector2(radius * cos, radius * sin);
        }

        private static void EllipseFinite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Ellipse geometry and transforms must be finite.");
        }
        private static void EllipseFinite(Vector3 value) { EllipseFinite(value.X); EllipseFinite(value.Y); EllipseFinite(value.Z); }
        private static double EllipseMax(Vector3 v) { return Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z))); }
        private static Vector3 EllipseDivide(Vector3 v, double scale) { return new Vector3(v.X / scale, v.Y / scale, v.Z / scale); }
        private static double EllipseLength(Vector3 v)
        {
            EllipseFinite(v); double scale = EllipseMax(v);
            if (scale == 0) return 0;
            Vector3 u = EllipseDivide(v, scale); double result = scale * Math.Sqrt(Vector3.DotProduct(u, u));
            EllipseFinite(result); return result;
        }
        private static Vector3 EllipseUnit(Vector3 v)
        {
            double length = EllipseLength(v);
            if (length == 0) throw new InvalidOperationException("The ellipse normal must be nonzero.");
            return EllipseDivide(v, length);
        }
    }
}
