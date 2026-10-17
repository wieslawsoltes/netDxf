using System.Buffers;
using global::SkiaSharp;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaRenderBackend
{
    private void ApplyClip(SKCanvas canvas, RenderClip clip, Viewport viewport)
    {
        var key = (clip, viewport);
        if (!clipPaths.TryGetValue(key, out var path))
        {
            path = BuildPath(clip.Contours, viewport, CadFillRule.EvenOdd);
            if (clipPaths.Count >= 32) { foreach (var old in clipPaths.Values) old.Dispose(); clipPaths.Clear(); }
            clipPaths[key] = path;
        }
        canvas.ClipPath(path, SKClipOperation.Intersect, true);
    }

    private void DrawItem(SKCanvas canvas, RenderItem item, RenderFrame frame, uint color, bool selected)
    {
        if (item.Clips.IsDefaultOrEmpty) { DrawItemContent(canvas, item, frame, color, selected); return; }
        canvas.Save();
        try
        {
            foreach (var clip in item.Clips) ApplyClip(canvas, clip, frame.Viewport);
            DrawItemContent(canvas, item, frame, color, selected);
        }
        finally { canvas.Restore(); }
    }

    private void DrawItemContent(SKCanvas canvas, RenderItem item, RenderFrame frame, uint color, bool selected)
    {
        // Keep one idle native paint. Leasing also preserves state when a host image
        // resolver synchronously re-enters this backend; Skia copies each draw's paint.
        var paint = itemPaint ?? CreateItemPaint(); itemPaint = null;
        try
        {
            paint.Color = new SKColor(color); paint.Style = SKPaintStyle.Stroke; paint.Shader = null;
            paint.StrokeWidth = (float)Math.Max(1, item.Style.Lineweight * 96 / 25.4);
            DrawRegions(canvas, item, frame, paint, selected);
            if (item.Filled)
            {
                using var fill = BuildPath(item.Contours, frame.Viewport, CadFillRule.EvenOdd);
                paint.Style = SKPaintStyle.Fill; canvas.DrawPath(fill, paint); paint.Style = SKPaintStyle.Stroke;
            }
            foreach (var group in item.Strokes)
            {
                using var strokePath = BuildPath(group.Contours, frame.Viewport, CadFillRule.EvenOdd);
                paint.Color = new SKColor(selected ? color : RenderPaint.Resolve(group.Argb, color));
                canvas.DrawPath(strokePath, paint);
            }
            paint.Color = new SKColor(color);
            var contours = StrokePattern.GetContours(item);
            using (var strokes = BuildPath(contours, frame.Viewport, CadFillRule.EvenOdd)) canvas.DrawPath(strokes, paint);
            foreach (var contour in contours)
            {
                if (contour.Points.Length == 2 && contour.Points[0] == contour.Points[1])
                {
                    var point = frame.Viewport.ToScreen(contour.Points[0]); paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(F(point.X), F(point.Y), Math.Max(0.5f, paint.StrokeWidth / 2), paint); paint.Style = SKPaintStyle.Stroke;
                }
            }
            if (item.Image is { Visible: true } reference)
            {
                canvas.Save();
                try { if (item.ImageClip is { } clip) ApplyClip(canvas, clip, frame.Viewport); DrawImage(canvas, reference, frame, paint, selected); }
                finally { canvas.Restore(); }
            }
            if (item.Text is { Height: > 0 } text)
            {
                canvas.Save();
                try { if (item.TextClip is { } clip) ApplyClip(canvas, clip, frame.Viewport); textEngine.Draw(canvas, text, item.TextLayout ?? textEngine.Layout(text), frame.Viewport, color, selected); }
                finally { canvas.Restore(); }
            }
        }
        finally
        {
            if (itemPaint is null && !disposed) itemPaint = paint;
            else paint.Dispose();
        }
    }

    private static SKPath BuildPath(IEnumerable<RenderContour> contours, Viewport viewport, CadFillRule fillRule)
    {
        var path = new SKPath { FillType = fillRule == CadFillRule.NonZero ? SKPathFillType.Winding : SKPathFillType.EvenOdd };
        try
        {
            Span<SKPoint> small = stackalloc SKPoint[256];
            foreach (var contour in contours)
            {
                var count = contour.Points.Length;
                if (count == 0)
                {
                    if (contour.Closed) path.Close();
                    continue;
                }
                // Submit the same screen-space vertices in one native call. Keeping the
                // double camera transform before conversion preserves large-coordinate precision.
                SKPoint[]? rented = count is > 256 and <= 65_536 ? ArrayPool<SKPoint>.Shared.Rent(count) : null;
                Span<SKPoint> points = count <= small.Length ? small[..count] :
                    rented is not null ? rented.AsSpan(0, count) : GC.AllocateUninitializedArray<SKPoint>(count);
                try
                {
                    for (var i = 0; i < count; i++)
                    {
                        var p = viewport.ToScreen(contour.Points[i]);
                        points[i] = new(F(p.X), F(p.Y));
                    }
                    path.AddPoly(points, contour.Closed);
                }
                finally { if (rented is not null) ArrayPool<SKPoint>.Shared.Return(rented); }
            }
            return path;
        }
        catch { path.Dispose(); throw; }
    }
}
