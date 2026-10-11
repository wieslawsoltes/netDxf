using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public static class RenderHitTesting
{
    public static bool Contains(RenderItem item, Vec3 point, double tolerance)
    {
        if (!point.IsFinite || !(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        point = point with { Z = 0 };
        if (item.Text is not null && item.Bounds.Contains(point)) return true; // conservative text extent, not glyph-accurate
        if (item.Image is { Visible: true } image && PolygonContains(image.Corners, point, tolerance))
        {
            if (image.ClipPolygon.IsDefaultOrEmpty) return true;
            var inClip = PolygonContains(image.ClipPolygon, point, image.ClipOutside ? 0 : tolerance);
            if (image.ClipOutside ? !inClip : inClip) return true;
        }
        if (ContoursContain(item.Contours, point, tolerance, item.Filled, CadFillRule.EvenOdd)) return true;
        return item.Regions.Any(region => ContoursContain(region.Contours, point, tolerance, true, region.FillRule));
    }

    public static bool PolygonContains(ImmutableArray<Vec3> vertices, Vec3 point, double tolerance = 0)
    {
        if (vertices.IsDefaultOrEmpty) return false;
        var winding = 0;
        for (var i = 0; i < vertices.Length; i++)
        {
            var a = vertices[i] with { Z = 0 }; var b = vertices[(i + 1) % vertices.Length] with { Z = 0 };
            if (CurveSampler.DistanceToSegment(point with { Z = 0 }, a, b) <= tolerance) return true;
            winding += Crossing(a, b, point);
        }
        return (Math.Abs(winding) & 1) != 0;
    }

    private static bool ContoursContain(ImmutableArray<RenderContour> contours, Vec3 point, double tolerance,
        bool filled, CadFillRule rule)
    {
        var winding = 0;
        foreach (var contour in contours)
        {
            var vertices = contour.Points;
            var count = vertices.Length;
            if (count == 0) continue;
            if (count == 1 && Vec3.Distance(point, vertices[0] with { Z = 0 }) <= tolerance) return true;
            var edges = contour.Closed ? count : count - 1;
            for (var i = 0; i < edges; i++)
            {
                var a = vertices[i] with { Z = 0 }; var b = vertices[(i + 1) % count] with { Z = 0 };
                if (CurveSampler.DistanceToSegment(point, a, b) <= tolerance) return true;
                if (filled && contour.Closed) winding += Crossing(a, b, point);
            }
        }
        return filled && (rule == CadFillRule.NonZero ? winding != 0 : (Math.Abs(winding) & 1) != 0);
    }

    private static int Crossing(Vec3 a, Vec3 b, Vec3 point)
    {
        var side = (b.X - a.X) * (point.Y - a.Y) - (point.X - a.X) * (b.Y - a.Y);
        if (a.Y <= point.Y && b.Y > point.Y && side > 0) return 1;
        if (a.Y > point.Y && b.Y <= point.Y && side < 0) return -1;
        return 0;
    }
}
