// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    // Shared preparation for stored affine geometry. All fallible computation is
    // finished before entity methods assign fields; no caller-overridable mutator is invoked.
    internal sealed class AffineEntityGeometry
    {
        private readonly Matrix3 matrix;
        private readonly Vector3 translation;
        internal bool IsIdentity { get; }
        internal bool IsTranslation { get; }

        internal AffineEntityGeometry(Matrix3 matrix, Vector3 translation)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Finite(matrix[r, c]);
            Finite(translation);
            this.matrix = matrix; this.translation = translation;
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
                linearIdentity &= matrix[r, c] == (r == c ? 1 : 0);
            this.IsTranslation = linearIdentity;
            this.IsIdentity = linearIdentity && translation.X == 0 && translation.Y == 0 && translation.Z == 0;
        }

        internal static void CheckAffine(Matrix4 matrix)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Finite(matrix[r, c]);
            if (matrix.M41 != 0 || matrix.M42 != 0 || matrix.M43 != 0 || matrix.M44 != 1)
                throw new NotSupportedException("A projective matrix is not an affine entity transform.");
        }

        internal Vector3 Point(Vector3 point)
        {
            Finite(point);
            if (this.IsIdentity) return point;
            Vector3 result = this.IsTranslation ? point + this.translation : this.matrix * point + this.translation;
            Finite(result); return result;
        }

        internal Vector3[] Points(IList<Vector3> points)
        {
            var result = new Vector3[points.Count];
            for (int i = 0; i < result.Length; i++) result[i] = this.Point(points[i]);
            return result;
        }

        internal Vector3 Direction(Vector3 direction)
        {
            Vector3 source = Unit(direction);
            if (this.IsTranslation) return direction;
            Vector3 result = this.matrix * source;
            Finite(result);
            if (Max(result) == 0) throw new NotSupportedException("The transform collapses an infinite entity's direction.");
            return Unit(result);
        }

        // These WCS entities have no single geometric plane normal in their DXF
        // payload. Retain the legacy auxiliary-direction convention, with a stable
        // normalized result or the original auxiliary direction on exact collapse.
        internal Vector3 AuxiliaryNormal(Vector3 normal)
        {
            Vector3 source = Unit(normal);
            if (this.IsTranslation) return normal;
            Vector3 transformed = this.matrix * source;
            Finite(transformed);
            return Max(transformed) == 0 ? normal : Unit(transformed);
        }

        // LINE's normal is its extrusion direction, not an inferred face normal.
        internal void LineExtrusion(Vector3 normal, double thickness, out Vector3 nextNormal, out double nextThickness)
        {
            Finite(thickness); Vector3 source = Unit(normal);
            nextNormal = normal; nextThickness = thickness;
            if (this.IsTranslation) return;
            Vector3 transformed = this.matrix * source; Finite(transformed);
            if (Max(transformed) == 0) { nextThickness = 0; return; }
            nextNormal = Unit(transformed);
            if (thickness != 0)
            {
                nextThickness = thickness * Length(transformed); Finite(nextThickness);
                if (nextThickness == 0) throw new NotSupportedException("Nonzero line extrusion underflows.");
            }
        }

        internal Quad Plane(Vector2[] vertices, double elevation, Vector3 normal, double thickness)
        {
            Finite(elevation); Finite(thickness); Vector3 sourceNormal = Unit(normal);
            foreach (var v in vertices) { Finite(v.X); Finite(v.Y); }
            if (this.IsIdentity) return new Quad(vertices, elevation, normal, thickness);
            Matrix3 source = MathHelper.ArbitraryAxis(sourceNormal);
            Vector3 nextNormal = normal;
            if (!this.IsTranslation)
            {
                Vector3 u = this.matrix * (source * Vector3.UnitX), v = this.matrix * (source * Vector3.UnitY);
                Finite(u); Finite(v);
                if (Max(u) == 0 || Max(v) == 0) throw new NotSupportedException("The transform collapses the stored entity plane.");
                Vector3 cross = Vector3.CrossProduct(Unit(u), Unit(v));
                if (Max(cross) == 0) throw new NotSupportedException("A rank-one plane cannot retain a SOLID/TRACE coordinate frame.");
                nextNormal = Unit(cross);
            }
            Matrix3 target = MathHelper.ArbitraryAxis(nextNormal).Transpose();
            double nextThickness = thickness;
            if (!this.IsTranslation && thickness != 0)
            {
                Vector3 extrusion = this.matrix * sourceNormal; Finite(extrusion);
                if (Max(extrusion) == 0) nextThickness = 0;
                else
                {
                    Vector3 direction = Unit(extrusion);
                    if (Length(Vector3.CrossProduct(direction, nextNormal)) > 1e-12)
                        throw new NotSupportedException("An oblique extrusion is not representable by SOLID/TRACE normal and thickness.");
                    double sign = Vector3.DotProduct(direction, nextNormal) < 0 ? -1 : 1;
                    nextThickness = thickness * (sign * Length(extrusion)); Finite(nextThickness);
                    if (nextThickness == 0) throw new NotSupportedException("Nonzero planar extrusion underflows.");
                }
            }
            Vector3 anchor = target * this.Point(source * new Vector3(0, 0, elevation));
            Finite(anchor);
            var points = new Vector2[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 world = this.Point(source * new Vector3(vertices[i].X, vertices[i].Y, elevation));
                Vector3 local = target * world; Finite(local);
                // Reject a numerically unresolved plane rather than dropping its Z.
                double tolerance = 1e-12 * Math.Max(1, Math.Max(Max(world), Math.Abs(anchor.Z)));
                if (Math.Abs(local.Z - anchor.Z) > tolerance)
                    throw new NotSupportedException("The transformed planar coordinates cannot be resolved at this precision.");
                points[i] = new Vector2(local.X, local.Y);
            }
            return new Quad(points, anchor.Z, nextNormal, nextThickness);
        }

        internal sealed class Quad
        {
            internal readonly Vector2[] Vertices;
            internal readonly double Elevation, Thickness;
            internal readonly Vector3 Normal;
            internal Quad(Vector2[] vertices, double elevation, Vector3 normal, double thickness)
            { this.Vertices = vertices; this.Elevation = elevation; this.Normal = normal; this.Thickness = thickness; }
        }

        internal static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Entity geometry and transforms must be finite.");
        }
        internal static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static double Max(Vector3 value) { return Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z))); }
        private static Vector3 Divide(Vector3 value, double scale) { return new Vector3(value.X / scale, value.Y / scale, value.Z / scale); }
        internal static Vector3 Unit(Vector3 value)
        {
            Finite(value); double scale = Max(value);
            if (scale == 0) throw new ArgumentException("A direction must be nonzero.", nameof(value));
            Vector3 scaled = Divide(value, scale);
            return Divide(scaled, Math.Sqrt(Vector3.DotProduct(scaled, scaled)));
        }
        private static double Length(Vector3 value)
        {
            Finite(value); double scale = Max(value);
            if (scale == 0) return 0;
            Vector3 scaled = Divide(value, scale);
            double result = scale * Math.Sqrt(Vector3.DotProduct(scaled, scaled));
            Finite(result); return result;
        }
    }
}
