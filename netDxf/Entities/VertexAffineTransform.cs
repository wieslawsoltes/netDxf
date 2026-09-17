// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;

namespace netDxf.Entities
{
    // MESH and 3DFACE store WCS vertices. Their inherited Normal is auxiliary
    // model state, not a single inferred face normal (a MESH need not be planar).
    internal sealed class VertexAffineTransform
    {
        internal Vector3[] Points { get; private set; }
        internal Vector3 Normal { get; private set; }
        internal bool Changed { get; private set; }

        internal static VertexAffineTransform Prepare(IList<Vector3> points, Vector3 normal,
            Matrix3 matrix, Vector3 translation)
        {
            bool linearIdentity = true;
            for (int r = 0; r < 3; r++) for (int c = 0; c < 3; c++)
            {
                Finite(matrix[r, c]);
                linearIdentity &= matrix[r, c] == (r == c ? 1 : 0);
            }
            Finite(translation); Finite(normal);
            if (Math.Abs(Vector3.DotProduct(normal, normal) - 1.0) > 2e-15)
                throw new InvalidOperationException("A stored auxiliary normal must have finite unit components.");
            // Validate even an identity operation, without allocating a point copy.
            foreach (Vector3 point in points) Finite(point);
            if (linearIdentity && translation.X == 0 && translation.Y == 0 && translation.Z == 0)
                return new VertexAffineTransform { Normal = normal };

            Vector3 nextNormal = linearIdentity ? normal : InfiniteLineTransform.AuxiliaryNormal(matrix, normal);
            var nextPoints = new Vector3[points.Count];
            bool changed = !Same(nextNormal, normal);
            for (int i = 0; i < points.Count; i++)
            {
                nextPoints[i] = InfiniteLineTransform.TransformPoint(matrix, points[i], translation);
                changed |= !Same(points[i], nextPoints[i]);
            }
            return new VertexAffineTransform { Points = nextPoints, Normal = nextNormal, Changed = changed };
        }

        private static bool Same(Vector3 a, Vector3 b) { return a.X == b.X && a.Y == b.Y && a.Z == b.Z; }
        private static void Finite(Vector3 value) { Finite(value.X); Finite(value.Y); Finite(value.Z); }
        private static void Finite(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Vertex geometry and transforms must be finite.");
        }
    }
}
