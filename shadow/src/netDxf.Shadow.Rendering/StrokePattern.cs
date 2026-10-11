using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Backend-independent DXF dash semantics, compiled once per immutable render item.
/// Distances follow the sampled world-space contour and do not change with camera zoom.</summary>
public static class StrokePattern
{
    private sealed record Entry(ImmutableArray<RenderContour> Contours);
    private static readonly ConditionalWeakTable<RenderItem, Entry> cache = new();
    public static ImmutableArray<RenderContour> GetContours(RenderItem item)
        => item.Style.Dashes.IsDefaultOrEmpty ? item.Contours : cache.GetValue(item,
            static value => new(Apply(value.Contours, value.Style.Dashes))).Contours;

    public static ImmutableArray<RenderContour> Apply(ImmutableArray<RenderContour> contours,
        ImmutableArray<double> pattern, int maximumSegments = 200_000)
    {
        if (pattern.IsDefaultOrEmpty) return contours;
        if (maximumSegments < 1) throw new ArgumentOutOfRangeException(nameof(maximumSegments));
        var result = ImmutableArray.CreateBuilder<RenderContour>();
        foreach (var contour in contours)
        {
            var points = contour.Points;
            if (points.IsDefaultOrEmpty) continue;
            if (contour.Closed && points[0] != points[^1]) points = points.Add(points[0]);
            var distance = new double[points.Length];
            for (var i = 1; i < points.Length; i++) distance[i] = distance[i - 1] + Vec3.Distance(points[i - 1], points[i]);
            if (distance[^1] == 0) { result.Add(contour); continue; }
            var remaining = maximumSegments - result.Count;
            if (remaining <= 0) throw new InvalidOperationException("Stroke segment budget exceeded.");
            var intervals = DashPattern.Clip(0, distance[^1], pattern, maximumSegments: remaining);
            var edge = 0;
            foreach (var interval in intervals)
            {
                while (edge + 1 < points.Length - 1 && distance[edge + 1] <= interval.Start) edge++;
                var vertices = ImmutableArray.CreateBuilder<Vec3>();
                vertices.Add(Interpolate(edge, interval.Start));
                var last = edge;
                while (last + 1 < points.Length - 1 && distance[last + 1] < interval.End)
                { vertices.Add(points[++last]); }
                vertices.Add(Interpolate(last, interval.End));
                result.Add(new(vertices.ToImmutable(), false));
                edge = last;
            }
            Vec3 Interpolate(int index, double at)
            {
                while (index + 1 < points.Length - 1 && distance[index + 1] <= distance[index]) index++;
                var length = distance[index + 1] - distance[index];
                var t = length > 0 ? Math.Clamp((at - distance[index]) / length, 0, 1) : 0;
                return points[index] * (1 - t) + points[index + 1] * t;
            }
        }
        return result.ToImmutable();
    }
}
