using System.Collections.Immutable;

namespace netDxf.Shadow.Drafting;

public readonly record struct DraftingArc(Vec3 Center, double Radius, double StartDegrees, double EndDegrees);

/// <summary>Double-precision, translated-coordinate construction primitives. No canvas or source types.</summary>
public static class PlanarGeometry
{
    public static double Cross(Vec3 a, Vec3 b) => a.X * b.Y - a.Y * b.X;
    public static Vec3 Left(Vec3 value) => new(-value.Y, value.X);
    public static double Degrees(Vec3 value) => (Math.Atan2(value.Y, value.X) * 180 / Math.PI + 360) % 360;

    public static DraftingArc ThreePointArc(Vec3 first, Vec3 through, Vec3 last, double tolerance = 1e-9)
    {
        var a = through - first; var b = last - first;
        var scale = Math.Max(a.Length, b.Length);
        if (!first.IsFinite || !through.IsFinite || !last.IsFinite || !(scale > tolerance))
            throw new ArgumentException("Arc points must be distinct and finite.");
        // Normalize before taking squared lengths. Large absolute drawing coordinates are not squared.
        var u = a / scale; var v = b / scale; var cross = Cross(u, v);
        if (Math.Abs(cross) <= 1e-12) throw new ArgumentException("Arc points are collinear or numerically indistinguishable.");
        var offset = new Vec3((Vec3.Dot(u, u) * v.Y - Vec3.Dot(v, v) * u.Y) / (2 * cross),
            (u.X * Vec3.Dot(v, v) - v.X * Vec3.Dot(u, u)) / (2 * cross)) * scale;
        var center = first + offset; var radius = offset.Length;
        if (!center.IsFinite || !double.IsFinite(radius) || radius <= tolerance)
            throw new ArithmeticException("The arc cannot be represented at this precision.");
        var start = Degrees(first - center); var end = Degrees(last - center);
        // DXF ARC sweeps counterclockwise. Reverse endpoints for a clockwise three-point input.
        return cross > 0 ? new(center, radius, start, end) : new(center, radius, end, start);
    }

    public static bool LineIntersection(Vec3 a, Vec3 b, Vec3 c, Vec3 d, out double t, out double u)
    {
        var first = b - a; var second = d - c;
        var scale = Math.Max(first.Length, second.Length);
        t = u = 0;
        if (!(scale > 0) || !double.IsFinite(scale)) return false;
        first /= scale; second /= scale; var delta = (c - a) / scale;
        var determinant = Cross(first, second);
        if (Math.Abs(determinant) <= 1e-12 * first.Length * second.Length) return false;
        t = Cross(delta, second) / determinant; u = Cross(delta, first) / determinant;
        return double.IsFinite(t) && double.IsFinite(u);
    }

    public static ImmutableArray<double> LineCircle(Vec3 start, Vec3 end, Vec3 center, double radius)
    {
        var direction = end - start; var length = direction.Length;
        if (!(length > 0) || !double.IsFinite(length) || !(radius > 0)) return [];
        var unit = direction / length; var delta = center - start;
        var projection = Vec3.Dot(delta, unit); var perpendicular = Cross(delta, unit);
        var ratio = perpendicular / radius;
        if (Math.Abs(ratio) > 1 + 1e-12) return [];
        var half = radius * Math.Sqrt(Math.Max(0, 1 - ratio * ratio));
        if (half <= 1e-12 * radius) return [projection / length];
        return [(projection - half) / length, (projection + half) / length];
    }

    public static Vec3 NearestOnSegment(Vec3 point, Vec3 a, Vec3 b)
    {
        var delta = b - a; var length = delta.Length;
        if (!(length > 0)) return a;
        var unit = delta / length;
        return a + unit * Math.Clamp(Vec3.Dot(point - a, unit), 0, length);
    }

    public static bool SegmentIntersectsBox(Vec3 a, Vec3 b, Bounds2 box)
    {
        var low = 0.0; var high = 1.0; var delta = b - a;
        return Clip(-delta.X, a.X - box.MinX) && Clip(delta.X, box.MaxX - a.X) &&
            Clip(-delta.Y, a.Y - box.MinY) && Clip(delta.Y, box.MaxY - a.Y);
        bool Clip(double p, double q)
        {
            if (p == 0) return q >= 0;
            var r = q / p;
            if (p < 0) { if (r > high) return false; low = Math.Max(low, r); }
            else { if (r < low) return false; high = Math.Min(high, r); }
            return true;
        }
    }
}
