using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public static class RenderHitTesting
{
    public static bool Contains(RenderItem item, Vec3 point, double tolerance)
    {
        if (!point.IsFinite || !(tolerance > 0) || !double.IsFinite(tolerance)) throw new ArgumentOutOfRangeException(nameof(tolerance));
        point = point with { Z = 0 };
        if (item.Clips.Any(clip => !ContainsClip(clip, point))) return false;
        if (item.Text is { } text && (item.TextClip is null || ContainsClip(item.TextClip, point)) && TextContains(item, text, point, tolerance)) return true;
        if (item.Image is { Visible: true } image && (item.ImageClip is null || ContainsClip(item.ImageClip, point)) && PolygonContains(image.Corners, point, tolerance))
        {
            if (image.ClipPolygon.IsDefaultOrEmpty) return true;
            var inClip = PolygonContains(image.ClipPolygon, point, image.ClipOutside ? 0 : tolerance);
            if (image.ClipOutside ? !inClip : inClip) return true;
        }
        if (ContoursContain(item.Contours, point, tolerance, item.Filled, CadFillRule.EvenOdd)) return true;
        return item.Regions.Any(region => ContoursContain(region.Contours, point, tolerance, true, region.FillRule));
    }

    public static bool ContainsClip(RenderClip clip, Vec3 point)
        => clip.Bounds.Contains(point) && ContoursContain(clip.Contours, point with { Z = 0 }, 0, true, CadFillRule.EvenOdd);

    private static bool TextContains(RenderItem item, CadText text, Vec3 point, double tolerance)
    {
        if (item.TextLayout is not { } layout) return item.Bounds.Contains(point);
        var x = text.XAxis; var y = -text.YAxis; var p = point - text.Origin;
        var determinant = x.X * y.Y - x.Y * y.X;
        if (!double.IsFinite(determinant) || determinant == 0) return false;
        var local = new Vec3((p.X * y.Y - p.Y * y.X) / determinant, (x.X * p.Y - x.Y * p.X) / determinant);
        var amount = tolerance * Math.Max(x.Length, y.Length) / Math.Abs(determinant);
        return layout.Bounds.Inflate(amount).Contains(local);
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
