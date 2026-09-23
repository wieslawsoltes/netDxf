// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    // POINT stores a WCS position and an independent signed extrusion.
    // Stage every calculation before changing the point or its common proxy.
    internal static class PointAffineTransform
    {
        internal static void Apply(Point point, Matrix3 matrix, Vector3 translation)
        {
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            {
                Finite(matrix[r, c]);
                linearIdentity &= matrix[r, c] == (r == c ? 1.0 : 0.0);
            }
            Vector3 normal = point.AffineNormal;
            Finite(point.Position); Finite(translation); Finite(normal);
            Finite(point.Thickness); Finite(point.Rotation);
            if (Math.Abs(Vector3.DotProduct(normal, normal) - 1.0) > 2e-15)
                throw new InvalidOperationException("The stored POINT extrusion must be a finite unit direction.");
            if (linearIdentity && translation.X == 0.0 && translation.Y == 0.0 && translation.Z == 0.0) return;

            Vector3 position = InfiniteLineTransform.TransformPoint(matrix, point.Position, translation);
            double thickness = point.Thickness, rotation = point.Rotation;
            if (!linearIdentity)
            {
                LineAffineTransform.TransformExtrusion(matrix, normal, thickness, out Vector3 nextNormal,
                    out thickness, "POINT");
                nextNormal = Vector3.NormalizeFiniteDirection(nextNormal, nameof(normal));
                // Retain the existing projected marker-axis convention. A sheared marker
                // glyph is not itself representable as a new glyph shape by this schema.
                Matrix3 from = MathHelper.ArbitraryAxis(normal);
                Matrix3 to = MathHelper.ArbitraryAxis(nextNormal).Transpose();
                Vector2 axis = Vector2.Rotate(Vector2.UnitX, rotation * MathHelper.DegToRad);
                Vector3 originalAxis = from * new Vector3(axis.X, axis.Y, 0.0);
                if (InfiniteLineTransform.TryDirection(matrix, originalAxis, out Vector3 direction))
                {
                    Vector3 local = to * direction;
                    Finite(local);
                    rotation = MathHelper.NormalizeAngle(Vector2.Angle(new Vector2(local.X, local.Y)) * MathHelper.RadToDeg);
                }
                else
                {
                    // The previous Atan2(0,0) path also chose zero for a collapsed marker axis.
                    rotation = 0.0;
                }
                normal = nextNormal;
            }
            Finite(rotation); Finite(thickness);
            bool changed = !SameBits(position, point.Position) || !SameBits(normal, point.AffineNormal)
                || Bits(thickness) != Bits(point.Thickness) || Bits(rotation) != Bits(point.Rotation);
            if (changed) point.PublishAffine(position, normal, thickness, rotation);
        }

        private static long Bits(double value) { return BitConverter.DoubleToInt64Bits(value); }
        private static bool SameBits(Vector3 a, Vector3 b)
        { return Bits(a.X) == Bits(b.X) && Bits(a.Y) == Bits(b.Y) && Bits(a.Z) == Bits(b.Z); }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "POINT geometry and affine transforms must be finite.");
        }
    }
}
