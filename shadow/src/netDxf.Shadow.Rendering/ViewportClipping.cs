using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Clips sampled lines and filled polygons to the viewport depth interval.</summary>
internal static class ViewportClipping
{
    internal static ImmutableArray<RenderContour> ClipLines(ImmutableArray<RenderContour> source, double back, double front)
    {
        if (double.IsNegativeInfinity(back) && double.IsPositiveInfinity(front)) return source;
        var output = ImmutableArray.CreateBuilder<RenderContour>();
        foreach (var contour in source)
        {
            if (contour.Points.All(p => p.Z >= back && p.Z <= front)) { output.Add(contour); continue; }
            var run = ImmutableArray.CreateBuilder<Vec3>();
            var count = contour.Points.Length;
            for (var i = 0; i < count - (contour.Closed ? 0 : 1); i++)
            {
                var a = contour.Points[i]; var b = contour.Points[(i + 1) % count];
                var low = 0.0; var high = 1.0; var dz = b.Z - a.Z;
                if (dz == 0)
                { if (a.Z < back || a.Z > front) { Flush(); continue; } }
                else
                {
                    var t0 = (back - a.Z) / dz; var t1 = (front - a.Z) / dz;
                    low = Math.Max(0, Math.Min(t0, t1)); high = Math.Min(1, Math.Max(t0, t1));
                    if (low > high) { Flush(); continue; }
                }
                var start = a * (1 - low) + b * low; var end = a * (1 - high) + b * high;
                if (run.Count > 0 && run[^1] != start) Flush();
                if (run.Count == 0) run.Add(start);
                run.Add(end);
                if (high < 1) Flush();
            }
            Flush();
            void Flush() { if (run.Count > 1) output.Add(new(run.ToImmutable(), false)); run.Clear(); }
        }
        return output.ToImmutable();
    }

    internal static ImmutableArray<RenderContour> ClipPolygons(ImmutableArray<RenderContour> contours, double back, double front)
    {
        var output = ImmutableArray.CreateBuilder<RenderContour>();
        foreach (var contour in contours)
        {
            var points = ClipPolygon(contour.Points, back, front);
            if (points.Length >= 3) output.Add(new(points, true));
        }
        return output.ToImmutable();
    }
    internal static ImmutableArray<Vec3> ClipPolygon(ImmutableArray<Vec3> polygon, double back, double front)
    {
        if (polygon.IsEmpty) return polygon;
        if (double.IsFinite(back)) polygon = Plane(polygon, back, true);
        if (double.IsFinite(front)) polygon = Plane(polygon, front, false);
        return polygon;
        static ImmutableArray<Vec3> Plane(ImmutableArray<Vec3> input, double plane, bool minimum)
        {
            if (input.IsEmpty || input.All(p => minimum ? p.Z >= plane : p.Z <= plane)) return input;
            var output = ImmutableArray.CreateBuilder<Vec3>();
            var previous = input[^1]; var wasInside = minimum ? previous.Z >= plane : previous.Z <= plane;
            foreach (var point in input)
            {
                var inside = minimum ? point.Z >= plane : point.Z <= plane;
                if (inside != wasInside)
                {
                    var t = (plane - previous.Z) / (point.Z - previous.Z);
                    output.Add((previous * (1 - t) + point * t) with { Z = plane });
                }
                if (inside) output.Add(point);
                previous = point; wasInside = inside;
            }
            return output.ToImmutable();
        }
    }
}
