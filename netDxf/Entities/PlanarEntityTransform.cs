// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;

namespace netDxf.Entities
{
    // SOLID and TRACE store four OCS points at one elevation. Build the entire
    // candidate before publishing: A*n is an extrusion, not a plane normal.
    internal sealed class PlanarEntityTransform
    {
        internal Vector2[] Vertexes { get; private set; }
        internal Vector3 Normal { get; private set; }
        internal double Elevation { get; private set; }
        internal double Thickness { get; private set; }
        internal bool Changed { get; private set; }

        internal static PlanarEntityTransform Prepare(Matrix3 matrix, Vector3 translation,
            Vector2[] vertexes, Vector3 normal, double elevation, double thickness)
        {
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) Finite(matrix[r, c]);
            Finite(translation); Finite(normal); Finite(elevation); Finite(thickness);
            foreach (Vector2 vertex in vertexes) { Finite(vertex.X); Finite(vertex.Y); }
            Vector3 unitNormal = Unit(normal);
            bool identity = Same(translation, Vector3.Zero);
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++) identity &= matrix[r, c] == (r == c ? 1 : 0);
            if (identity)
                return new PlanarEntityTransform { Vertexes = vertexes, Normal = normal, Elevation = elevation, Thickness = thickness };

            Matrix3 axes = MathHelper.ArbitraryAxis(unitNormal);
            Vector3 x = matrix * (axes * Vector3.UnitX), y = matrix * (axes * Vector3.UnitY);
            Vector3 ux = Unit(x), uy = Unit(y);
            Vector3 cross = Vector3.CrossProduct(ux, uy);
            // Near-rank-one planes cannot safely define the OCS orientation at
            // binary64 precision. Reject instead of flattening or guessing it.
            if (Max(cross) <= 1e-14)
                throw new NotSupportedException("The transform collapses or numerically degenerates the entity plane.");
            Vector3 nextNormal = Unit(cross);
            Matrix3 toObject = MathHelper.ArbitraryAxis(nextNormal).Transpose();

            // Separate the plane origin from its in-plane offsets. Elevation must
            // not be inferred from whichever corner happens to be written last.
            Vector3 origin = matrix * (unitNormal * elevation) + translation;
            Finite(origin);
            Vector3 offset = toObject * origin, dx = toObject * x, dy = toObject * y;
            Finite(offset); Finite(dx); Finite(dy);
            var next = new Vector2[vertexes.Length];
            for (int i = 0; i < vertexes.Length; i++)
            {
                next[i] = new Vector2(offset.X + dx.X * vertexes[i].X + dy.X * vertexes[i].Y,
                    offset.Y + dx.Y * vertexes[i].X + dy.Y * vertexes[i].Y);
                Finite(next[i].X); Finite(next[i].Y);
            }

            double nextThickness = thickness;
            if (thickness != 0)
            {
                Vector3 extrusion = matrix * unitNormal;
                double scale = Max(extrusion); Finite(extrusion);
                if (scale == 0) nextThickness = 0; // rank-two map flattens extrusion, not the plane
                else
                {
                    Vector3 direction = Unit(extrusion);
                    if (Max(Vector3.CrossProduct(direction, nextNormal)) > 1e-12)
                        throw new NotSupportedException("Oblique extrusion cannot be stored as a normal and signed thickness.");
                    Vector3 scaled = Divide(extrusion, scale);
                    double factor = Math.Sqrt(Vector3.DotProduct(scaled, scaled));
                    nextThickness = (thickness * scale) * factor;
                    if (Vector3.DotProduct(direction, nextNormal) < 0) nextThickness = -nextThickness;
                    Finite(nextThickness);
                    if (nextThickness == 0)
                        throw new NotSupportedException("Nonzero transformed thickness underflows to zero.");
                }
            }
            bool changed = !Same(normal, nextNormal) || elevation != offset.Z || thickness != nextThickness;
            for (int i = 0; i < next.Length; i++) changed |= next[i].X != vertexes[i].X || next[i].Y != vertexes[i].Y;
            return new PlanarEntityTransform { Vertexes = next, Normal = nextNormal, Elevation = offset.Z,
                Thickness = nextThickness, Changed = changed };
        }

        internal static void CheckAffine(Matrix4 matrix)
        {
            for (int r = 0; r < 4; r++) for (int c = 0; c < 4; c++) Finite(matrix[r, c]);
            if (matrix.M41 != 0 || matrix.M42 != 0 || matrix.M43 != 0 || matrix.M44 != 1)
                throw new NotSupportedException("Projective matrices cannot be applied as planar affine transforms.");
        }

        private static Vector3 Unit(Vector3 value)
        {
            Finite(value); double scale = Max(value);
            if (scale == 0) throw new NotSupportedException("A nonzero transformed plane basis is required.");
            Vector3 scaled = Divide(value, scale);
            return Divide(scaled, Math.Sqrt(Vector3.DotProduct(scaled, scaled)));
        }
        private static Vector3 Divide(Vector3 value, double divisor)
        { return new Vector3(value.X / divisor, value.Y / divisor, value.Z / divisor); }
        private static double Max(Vector3 value)
        { return Math.Max(Math.Abs(value.X), Math.Max(Math.Abs(value.Y), Math.Abs(value.Z))); }
        private static bool Same(Vector3 a, Vector3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Planar geometry and its transform must be finite.");
        }
    }
}
