using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

/// <summary>Transports sampled items, paint frames and text metrics into an immutable paper view.</summary>
internal static class PaperItemProjection
{
    internal static RenderItem? Project(RenderItem source, PaperViewport view)
    {
        var matrix = view.Projection.ModelToPaper;
        var style = source.Style with
        {
            Dashes = source.Style.Dashes.Select(d => d * view.Projection.Scale).ToImmutableArray()
        };
        var raw = source with { Contours = Transform(source.Contours, matrix), Style = style };
        // Resolve dash phase before depth clipping, so a clipped leading segment cannot restart the pattern.
        var strokes = StrokePattern.GetContours(raw);
        var contours = ViewportClipping.ClipLines(strokes, view.Back, view.Front);
        var regions = ImmutableArray.CreateBuilder<RenderRegion>();
        foreach (var region in source.Regions)
        {
            var loops = ViewportClipping.ClipPolygons(Transform(region.Contours, matrix), view.Back, view.Front);
            if (!loops.IsEmpty) regions.Add(region with
            {
                Contours = loops,
                Gradient = region.Gradient is { } gradient ? gradient with { Frame = matrix * gradient.Frame } : null
            });
        }
        if (source.Filled)
        {
            var loops = ViewportClipping.ClipPolygons(raw.Contours, view.Back, view.Front);
            if (!loops.IsEmpty) regions.Add(new(loops, CadFillRule.EvenOdd, null));
        }
        var bounds = Bounds2.Empty;
        Include(contours); foreach (var region in regions) Include(region.Contours);
        var painted = source.Strokes.Select(stroke => stroke with
        { Contours = ViewportClipping.ClipLines(Transform(stroke.Contours, matrix), view.Back, view.Front) }).ToImmutableArray();
        foreach (var stroke in painted) Include(stroke.Contours);
        CadText? text = null; RenderClip? textClip = null;
        if (source.Text is { } originalText && source.TextLayout is { Bounds.IsEmpty: false } layout)
        {
            var candidate = originalText with
            {
                Origin = matrix.Point(originalText.Origin), XAxis = matrix.Direction(originalText.XAxis), YAxis = matrix.Direction(originalText.YAxis)
            };
            var box = layout.Bounds;
            ImmutableArray<Vec3> footprint = [At(box.MinX, box.MinY), At(box.MaxX, box.MinY), At(box.MaxX, box.MaxY), At(box.MinX, box.MaxY)];
            var clipped = ViewportClipping.ClipPolygon(footprint, view.Back, view.Front);
            if (clipped.Length >= 3)
            {
                text = candidate;
                if (!footprint.AsSpan().SequenceEqual(clipped.AsSpan())) textClip = Mask(clipped);
                foreach (var point in clipped) bounds = bounds.Include(point);
            }
            Vec3 At(double x, double y) => candidate.Origin + candidate.XAxis * x - candidate.YAxis * y;
        }
        CadImage? image = null; RenderClip? imageClip = null;
        if (source.Image is { Visible: true } originalImage)
        {
            var candidate = originalImage with
            {
                Corners = originalImage.Corners.Select(matrix.Point).ToImmutableArray(),
                ClipPolygon = originalImage.ClipPolygon.Select(matrix.Point).ToImmutableArray()
            };
            var clipped = ViewportClipping.ClipPolygon(candidate.Corners, view.Back, view.Front);
            if (clipped.Length >= 3)
            {
                image = candidate;
                if (!candidate.Corners.AsSpan().SequenceEqual(clipped.AsSpan())) imageClip = Mask(clipped);
                foreach (var point in clipped) bounds = bounds.Include(point);
            }
        }
        bounds = Intersection(bounds, view.Clip.Bounds);
        if (bounds.IsEmpty) return null;
        return source with
        {
            Key = view.Projection.ViewportEntity.Value + "/viewport/" + source.Key,
            SourceToWorld = matrix * source.SourceToWorld, Projection = view.Projection,
            Style = style with { Dashes = [], Linetype = null }, Contours = contours, Filled = false, Regions = regions.ToImmutable(),
            Strokes = painted, Text = text, Image = image, TextClip = textClip, ImageClip = imageClip, Clips = [view.Clip], Bounds = bounds
        };
        void Include(ImmutableArray<RenderContour> values)
        { foreach (var contour in values) foreach (var point in contour.Points) bounds = bounds.Include(point); }
    }

    internal static Bounds2 Intersection(Bounds2 a, Bounds2 b) => a.IsEmpty || b.IsEmpty ? Bounds2.Empty :
        new(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY));
    private static RenderClip Mask(ImmutableArray<Vec3> points)
    {
        var bounds = Bounds2.Empty; foreach (var point in points) bounds = bounds.Include(point);
        return new([new(points, true)], bounds);
    }
    private static ImmutableArray<RenderContour> Transform(ImmutableArray<RenderContour> source, Affine3 matrix)
        => source.Select(c => c with { Points = c.Points.Select(matrix.Point).ToImmutableArray() }).ToImmutableArray();

}
