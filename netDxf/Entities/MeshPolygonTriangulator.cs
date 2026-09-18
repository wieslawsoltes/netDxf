// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace netDxf.Entities
{
    // Ear clipping on exact projected binary64 predicates. Planarity is an explicit
    // relative floating-point admission rule, not an exact coplanarity claim.
    internal static class MeshPolygonTriangulator
    {
        private struct Point
        {
            internal BigInteger X, Y;
            internal Point(double x, double y) { X = Integer(x); Y = Integer(y); }
        }
        internal static int RingLength(int[] ring)
        {
            if (ring == null) throw new InvalidOperationException("A mesh face cannot be null.");
            int count = ring.Length;
            if (count > 1 && ring[0] == ring[count - 1]) count--;
            if (count < 3 || count > 1024)
                throw new NotSupportedException("A mesh polygon must contain between 3 and 1024 ring vertices.");
            return count;
        }
        internal static bool Boundary(int a, int b, int count)
        { return (a + 1) % count == b || (b + 1) % count == a; }
        internal static void CheckFinite(Vector3 p)
        {
            if (!Finite(p.X) || !Finite(p.Y) || !Finite(p.Z))
                throw new InvalidOperationException("Mesh vertices and auxiliary normal must be finite.");
        }
        private static bool Finite(double v) { return !double.IsNaN(v) && !double.IsInfinity(v); }
        private static double Max(Vector3 v) { return Math.Max(Math.Abs(v.X), Math.Max(Math.Abs(v.Y), Math.Abs(v.Z))); }
        private static Vector3 Divide(Vector3 v, double s) { return new Vector3(v.X / s, v.Y / s, v.Z / s); }
        private static BigInteger Integer(double value)
        {
            long bits = BitConverter.DoubleToInt64Bits(value);
            int exponent = (int)((bits >> 52) & 2047);
            long mantissa = bits & 0x000fffffffffffffL;
            BigInteger result = exponent == 0 ? new BigInteger(mantissa) : new BigInteger(mantissa | 0x0010000000000000L) << (exponent - 1);
            return bits < 0 ? -result : result;
        }
        private static BigInteger Cross(Point a, Point b, Point c)
        { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }
        private static bool OnSegment(Point a, Point b, Point p)
        { return p.X >= BigInteger.Min(a.X, b.X) && p.X <= BigInteger.Max(a.X, b.X) && p.Y >= BigInteger.Min(a.Y, b.Y) && p.Y <= BigInteger.Max(a.Y, b.Y); }
        private static bool Intersects(Point a, Point b, Point c, Point d)
        {
            int abC = Cross(a, b, c).Sign, abD = Cross(a, b, d).Sign;
            int cdA = Cross(c, d, a).Sign, cdB = Cross(c, d, b).Sign;
            return (abC == 0 && OnSegment(a, b, c)) || (abD == 0 && OnSegment(a, b, d)) ||
                   (cdA == 0 && OnSegment(c, d, a)) || (cdB == 0 && OnSegment(c, d, b)) ||
                   (abC * abD < 0 && cdA * cdB < 0);
        }
        private static int Projection(IList<Vector3> vertices, int[] ring, int count)
        {
            Vector3 origin = vertices[ring[0]];
            var offsets = new Vector3[count];
            double scale = 0; bool overflow = false;
            for (int i = 0; i < count; i++)
            {
                offsets[i] = vertices[ring[i]] - origin;
                double magnitude = Max(offsets[i]);
                overflow |= !Finite(magnitude); scale = Math.Max(scale, magnitude);
            }
            if (overflow)
            {
                double absoluteScale = 0;
                for (int i = 0; i < count; i++) absoluteScale = Math.Max(absoluteScale, Max(vertices[ring[i]]));
                Vector3 anchor = Divide(origin, absoluteScale); scale = 0;
                for (int i = 0; i < count; i++)
                { offsets[i] = Divide(vertices[ring[i]], absoluteScale) - anchor; scale = Math.Max(scale, Max(offsets[i])); }
            }
            if (scale == 0) throw new InvalidOperationException("A mesh polygon has no extent.");
            for (int i = 0; i < count; i++) offsets[i] = Divide(offsets[i], scale);
            Vector3 normal = Vector3.Zero; double best = 0;
            // Choose a stable anchor axis, then the largest cross with that axis.
            int axis = 1; double longest = 0;
            for (int i = 1; i < count; i++)
            { double length = Vector3.DotProduct(offsets[i], offsets[i]); if (length > longest) { longest = length; axis = i; } }
            for (int i = 1; i < count; i++)
            { Vector3 cross = Vector3.CrossProduct(offsets[axis], offsets[i]); double size = Max(cross); if (size > best) { best = size; normal = cross; } }
            if (best == 0) throw new NotSupportedException("A degenerate or numerically unresolved polygon cannot be triangulated.");
            normal = Vector3.NormalizeFiniteDirection(normal, nameof(normal));
            for (int i = 0; i < count; i++)
                if (Math.Abs(Vector3.DotProduct(offsets[i], normal)) > 1e-10)
                    throw new NotSupportedException("Mesh polygon is not planar within the relative 1e-10 admission tolerance.");
            double x = Math.Abs(normal.X), y = Math.Abs(normal.Y), z = Math.Abs(normal.Z);
            return x >= y && x >= z ? 0 : y >= z ? 1 : 2;
        }
        internal static List<int[]> Triangulate(IList<Vector3> vertices, int[] ring, int count)
        {
            int drop = Projection(vertices, ring, count);
            var points = new Point[count];
            for (int i = 0; i < count; i++)
            {
                Vector3 p = vertices[ring[i]];
                points[i] = drop == 0 ? new Point(p.Y, p.Z) : drop == 1 ? new Point(p.Z, p.X) : new Point(p.X, p.Y);
            }
            BigInteger area = BigInteger.Zero;
            for (int i = 0; i < count; i++)
            {
                Point a = points[i], b = points[(i + 1) % count];
                if (a.X == b.X && a.Y == b.Y) throw new InvalidOperationException("A polygon contains a zero projected edge.");
                area += a.X * b.Y - b.X * a.Y;
                for (int j = i + 1; j < count; j++)
                    if (!Boundary(i, j, count) && Intersects(a, b, points[j], points[(j + 1) % count]))
                        throw new NotSupportedException("A polygon self-intersects or touches a nonadjacent edge.");
                // Adjacent backtracking overlaps are invalid even though the edges share a vertex.
                Point c = points[(i + 2) % count];
                if (Cross(a, b, c).IsZero && (OnSegment(a, b, c) || OnSegment(b, c, a)))
                    throw new NotSupportedException("Adjacent polygon edges overlap.");
            }
            int winding = area.Sign;
            if (winding == 0) throw new InvalidOperationException("A polygon has zero projected area.");
            var remaining = new List<int>(count);
            for (int i = 0; i < count; i++) remaining.Add(i);
            var triangles = new List<int[]>(count - 2);
            while (remaining.Count > 3)
            {
                bool found = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int a = remaining[(i + remaining.Count - 1) % remaining.Count], b = remaining[i], c = remaining[(i + 1) % remaining.Count];
                    if (Cross(points[a], points[b], points[c]).Sign * winding <= 0) continue;
                    bool blocked = false;
                    foreach (int p in remaining)
                    {
                        if (p == a || p == b || p == c) continue;
                        if (Cross(points[a], points[b], points[p]).Sign * winding >= 0 &&
                            Cross(points[b], points[c], points[p]).Sign * winding >= 0 &&
                            Cross(points[c], points[a], points[p]).Sign * winding >= 0) { blocked = true; break; }
                    }
                    if (blocked) continue;
                    triangles.Add(new[] { a, b, c }); remaining.RemoveAt(i); found = true; break;
                }
                if (!found) throw new NotSupportedException("No nondegenerate polygon ear was found.");
            }
            if (Cross(points[remaining[0]], points[remaining[1]], points[remaining[2]]).Sign * winding <= 0)
                throw new NotSupportedException("The final triangle is degenerate.");
            triangles.Add(remaining.ToArray()); return triangles;
        }
    }
}
