using global::SkiaSharp;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaRenderBackend
{
    private static void DrawRegions(SKCanvas canvas, RenderItem item, RenderFrame frame, SKPaint paint, bool selected)
    {
        foreach (var region in item.Regions)
        {
            using var path = BuildPath(region.Contours, frame.Viewport, region.FillRule);
            using var shader = !selected && region.Gradient is { } gradient ? GradientShader(gradient, frame.Viewport, paint.Color.Alpha) : null;
            paint.Style = SKPaintStyle.Fill; paint.Shader = shader;
            // Shader stops already contain the entity opacity. White prevents applying it twice.
            var color = paint.Color;
            if (shader is not null) paint.Color = SKColors.White;
            try { canvas.DrawPath(path, paint); }
            finally { paint.Color = color; paint.Shader = null; paint.Style = SKPaintStyle.Stroke; }
        }
    }
    private static SKShader GradientShader(CadGradient gradient, Viewport viewport, byte alpha)
    {
        var plan = GradientPlan.Create(gradient, alpha);
        var origin = viewport.ToScreen(gradient.Frame.Translation);
        var scale = viewport.PixelsPerUnit;
        var matrix = new SKMatrix
        {
            ScaleX = F(gradient.Frame.X.X * scale), SkewY = F(-gradient.Frame.X.Y * scale),
            SkewX = F(gradient.Frame.Y.X * scale), ScaleY = F(-gradient.Frame.Y.Y * scale),
            TransX = F(origin.X), TransY = F(origin.Y), Persp2 = 1
        };
        var colors = plan.Colors.Select(c => new SKColor(c)).ToArray();
        var positions = plan.Positions.Select(F).ToArray();
        return plan.Radial
            ? SKShader.CreateRadialGradient(new(F(plan.Start.X), F(plan.Start.Y)), F(plan.Radius), colors, positions, SKShaderTileMode.Clamp, matrix)
            : SKShader.CreateLinearGradient(new(F(plan.Start.X), F(plan.Start.Y)), new(F(plan.End.X), F(plan.End.Y)), colors, positions, SKShaderTileMode.Clamp, matrix);
    }
    private void DrawImage(SKCanvas canvas, CadImage reference, RenderFrame frame, SKPaint stroke, bool selected)
    {
        if (reference.Corners.Length != 4) return;
        var image = selected ? null : images?.Resolve(reference.FileName);
        if (image is null)
        {
            var vertices = !reference.ClipOutside && !reference.ClipPolygon.IsEmpty ? reference.ClipPolygon : reference.Corners;
            using var outline = BuildPath([new(vertices, true)], frame.Viewport, CadFillRule.EvenOdd);
            canvas.DrawPath(outline, stroke); return;
        }
        var origin = frame.Viewport.ToScreen(reference.Corners[3]);
        var right = frame.Viewport.ToScreen(reference.Corners[2]);
        var bottom = frame.Viewport.ToScreen(reference.Corners[0]);
        var matrix = new SKMatrix
        {
            ScaleX = F((right.X - origin.X) / image.Width), SkewY = F((right.Y - origin.Y) / image.Width),
            SkewX = F((bottom.X - origin.X) / image.Height), ScaleY = F((bottom.Y - origin.Y) / image.Height),
            TransX = F(origin.X), TransY = F(origin.Y), Persp2 = 1
        };
        using var paint = new SKPaint { IsAntialias = true, Color = new SKColor(255, 255, 255, stroke.Color.Alpha) };
        using var filter = ImageFilter(reference, frame.Background);
        paint.ColorFilter = filter;
        canvas.Save();
        try
        {
            if (!reference.ClipPolygon.IsDefaultOrEmpty)
            {
                using var clip = BuildPath([new(reference.ClipPolygon, true)], frame.Viewport, CadFillRule.EvenOdd);
                canvas.ClipPath(clip, reference.ClipOutside ? SKClipOperation.Difference : SKClipOperation.Intersect, true);
            }
            canvas.Concat(in matrix); canvas.DrawImage(image, 0, 0, paint);
        }
        finally { canvas.Restore(); }
    }
    private static SKColorFilter? ImageFilter(CadImage image, uint background)
    {
        if (!double.IsFinite(image.Brightness) || !double.IsFinite(image.Contrast) || !double.IsFinite(image.Fade))
            throw new ArgumentException("Image display adjustments must be finite.");
        if (image.Brightness == 0.5 && image.Contrast == 0.5 && image.Fade == 0) return null;
        var contrast = Math.Clamp(image.Contrast, 0, 1) * 2;
        var fade = Math.Clamp(image.Fade, 0, 1);
        var gain = F(contrast * (1 - fade));
        var offset = (127.5 * (1 - contrast) + (Math.Clamp(image.Brightness, 0, 1) - 0.5) * 255) * (1 - fade);
        var bg = new SKColor(background);
        return SKColorFilter.CreateColorMatrix([
            gain, 0, 0, 0, F(offset + fade * bg.Red),
            0, gain, 0, 0, F(offset + fade * bg.Green),
            0, 0, gain, 0, F(offset + fade * bg.Blue),
            0, 0, 0, 1, 0]);
    }
}
