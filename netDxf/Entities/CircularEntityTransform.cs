// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    // A circle remains circular iff its transformed unit plane axes are orthogonal
    // and equally scaled. Test the actual plane, not diagonal matrix entries.
    internal sealed class CircularEntityTransform
    {
        private const double RelativeTolerance = 1e-12;
        internal Vector3 Center { get; private set; }
        internal Vector3 Normal { get; private set; }
        internal Vector3 AxisX { get; private set; }
        internal Vector3 AxisY { get; private set; }
        internal double Radius { get; private set; }
        internal double Thickness { get; private set; }
        internal bool IsIdentity { get; private set; }

        internal static CircularEntityTransform Prepare(Matrix3 matrix, Vector3 translation,
            Vector3 center, Vector3 normal, double radius, double thickness)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Finite(matrix[r, c]);
            Finite(translation); Finite(center); Finite(normal); Finite(radius); Finite(thickness);
            if (radius <= 0) throw new InvalidOperationException("The source circular radius must be positive.");
            double normalLength = Length(normal);
            if (normalLength == 0) throw new InvalidOperationException("The source circular normal must be nonzero.");
            normal = Divide(normal, normalLength);
            Matrix3 sourceAxes = MathHelper.ArbitraryAxis(normal);
            Vector3 x = matrix * (sourceAxes * Vector3.UnitX), y = matrix * (sourceAxes * Vector3.UnitY);
            double sx = Length(x), sy = Length(y);
            if (sx == 0 || sy == 0 || Math.Abs(sx - sy) / Math.Max(sx, sy) > RelativeTolerance)
                throw new NotSupportedException("The transform collapses or nonuniformly scales the circular plane; use an ellipse representation.");
            x = Divide(x, sx); y = Divide(y, sy);
            if (Math.Abs(Vector3.DotProduct(x, y)) > RelativeTolerance)
                throw new NotSupportedException("An in-plane shear cannot be represented by a circular entity.");
            Vector3 cross = Vector3.CrossProduct(x, y);
            Vector3 nextNormal = Divide(cross, Length(cross));
            double nextRadius = radius * sx;
            Finite(nextRadius);
            if (nextRadius <= 0) throw new NotSupportedException("The transformed circular radius underflows to zero.");
            Vector3 nextCenter = matrix * center + translation;
            Finite(nextCenter);
            double nextThickness = thickness;
            if (thickness != 0)
            {
                Vector3 extrusion = matrix * normal;
                double extrusionLength = Length(extrusion);
                if (extrusionLength == 0) nextThickness = 0;
                else
                {
                    Vector3 direction = Divide(extrusion, extrusionLength);
                    double alignment = Vector3.DotProduct(direction, nextNormal);
                    if (Length(Vector3.CrossProduct(direction, nextNormal)) > RelativeTolerance)
                        throw new NotSupportedException("Oblique extrusion cannot be represented by a normal and signed thickness.");
                    nextThickness = thickness * (alignment < 0 ? -extrusionLength : extrusionLength);
                    Finite(nextThickness);
                    if (nextThickness == 0) throw new NotSupportedException("Nonzero transformed thickness underflows to zero.");
                }
            }
            bool identity = translation.X == 0 && translation.Y == 0 && translation.Z == 0;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) identity &= matrix[r, c] == (r == c ? 1 : 0);
            return new CircularEntityTransform { Center = nextCenter, Normal = nextNormal, Radius = nextRadius,
                Thickness = nextThickness, AxisX = x, AxisY = y, IsIdentity = identity };
        }

        internal double Angle(double angle)
        {
            Finite(angle);
            double radians = MathHelper.NormalizeAngle(angle) * MathHelper.DegToRad;
            Vector3 direction = Math.Cos(radians) * this.AxisX + Math.Sin(radians) * this.AxisY;
            direction = MathHelper.ArbitraryAxis(this.Normal).Transpose() * direction;
            return MathHelper.NormalizeAngle(Math.Atan2(direction.Y, direction.X) * MathHelper.RadToDeg);
        }

        internal static void CheckAffine(Matrix4 matrix)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Finite(matrix[r, c]);
            if (matrix.M41 != 0 || matrix.M42 != 0 || matrix.M43 != 0 || matrix.M44 != 1)
                throw new NotSupportedException("Projective matrices are not circular affine transforms.");
        }

        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Circular geometry and its transform must be finite.");
        }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static Vector3 Divide(Vector3 v, double scale)
        { return new Vector3(v.X / scale, v.Y / scale, v.Z / scale); }
        private static double Length(Vector3 v)
        {
            Finite(v);
            double scale = Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z)));
            if (scale == 0) return 0;
            Vector3 unit = Divide(v, scale);
            double result = scale * Math.Sqrt(Vector3.DotProduct(unit, unit));
            Finite(result); return result;
        }
    }
}
