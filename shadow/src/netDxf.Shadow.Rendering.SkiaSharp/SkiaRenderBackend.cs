using global::SkiaSharp;
using global::SkiaSharp.HarfBuzz;

namespace netDxf.Shadow.Rendering.SkiaSharp;

/// <summary>The resolver returns a borrowed image. It must not fetch untrusted DXF paths without an explicit policy.</summary>
public interface ISkiaImageResolver { SKImage? Resolve(string reference); }

/// <summary>Raster PNG backend plus direct drawing to any caller-owned CPU/GPU SKCanvas.</summary>
public sealed class SkiaRenderBackend : IRenderBackend
{
    private sealed record FontEntry(SKTypeface Typeface, SKShaper Shaper) : IDisposable
    {
        public void Dispose() { Shaper.Dispose(); Typeface.Dispose(); }
    }
    private readonly object gate = new();
    private readonly Dictionary<string, FontEntry> fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(RenderScene, Viewport), SKPicture> pictures = new();
    private readonly ISkiaImageResolver? images;
    private bool disposed;
    public string Id => "skia";
    public SkiaRenderBackend(ISkiaImageResolver? images = null) => this.images = images;

    public RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var surface = SKSurface.Create(new SKImageInfo(frame.Viewport.Width, frame.Viewport.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Skia could not allocate the render surface.");
            Draw(surface.Canvas, frame, cancellationToken);
            using var image = surface.Snapshot();
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return new(encoded.ToArray(), "image/png");
        }
    }

    public void Draw(SKCanvas canvas, RenderFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        frame.Viewport.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            canvas.Save();
            try
            {
                canvas.ClipRect(new(0, 0, frame.Viewport.Width, frame.Viewport.Height));
                canvas.Clear(new SKColor(frame.Background));
                var key = (frame.Scene, frame.Viewport);
                if (!pictures.TryGetValue(key, out var picture))
                {
                    using var recorder = new SKPictureRecorder();
                    var recording = recorder.BeginRecording(new(0, 0, frame.Viewport.Width, frame.Viewport.Height));
                    foreach (var item in frame.VisibleItems())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        DrawItem(recording, item, frame, item.Style.Argb);
                    }
                    picture = recorder.EndRecording();
                    if (pictures.Count >= 4) { foreach (var old in pictures.Values) old.Dispose(); pictures.Clear(); }
                    pictures[key] = picture;
                }
                canvas.DrawPicture(picture);
                if (frame.Selection is { Count: > 0 })
                    foreach (var item in frame.VisibleItems())
                        if (frame.Selection.Contains(item.Key)) DrawItem(canvas, item, frame, frame.SelectionColor);
            }
            finally { canvas.Restore(); }
        }
    }

    private void DrawItem(SKCanvas canvas, RenderItem item, RenderFrame frame, uint color)
    {
        using var paint = new SKPaint { IsAntialias = true, Color = new(color), Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(1, item.Style.Lineweight * 96 / 25.4), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
        using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
        foreach (var contour in item.Contours)
        {
            for (var i = 0; i < contour.Points.Length; i++)
            {
                var p = frame.Viewport.ToScreen(contour.Points[i]);
                if (i == 0) path.MoveTo(F(p.X), F(p.Y)); else path.LineTo(F(p.X), F(p.Y));
            }
            if (contour.Closed) path.Close();
            if (contour.Points.Length == 2 && contour.Points[0] == contour.Points[1])
            {
                var p = frame.Viewport.ToScreen(contour.Points[0]); canvas.DrawCircle(F(p.X), F(p.Y), Math.Max(1.5f, paint.StrokeWidth), paint);
            }
        }
        if (item.Filled) { paint.Style = SKPaintStyle.Fill; canvas.DrawPath(path, paint); paint.Style = SKPaintStyle.Stroke; }
        var intervals = item.Style.Dashes.Select(d => F(Math.Clamp(Math.Abs(d) * frame.Viewport.PixelsPerUnit, 1, 1e8))).ToArray();
        if (intervals.Length % 2 != 0) intervals = intervals.Concat(intervals).ToArray();
        using var dash = intervals.Length >= 2 ? SKPathEffect.CreateDash(intervals, 0) : null;
        paint.PathEffect = dash; canvas.DrawPath(path, paint); paint.PathEffect = null;
        if (item.Image is { } reference)
        {
            var image = images?.Resolve(reference.FileName);
            if (image is not null && reference.Corners.Length == 4)
            {
                var origin = frame.Viewport.ToScreen(reference.Corners[3]);
                var right = frame.Viewport.ToScreen(reference.Corners[2]);
                var bottom = frame.Viewport.ToScreen(reference.Corners[0]);
                var matrix = new SKMatrix { ScaleX = F((right.X - origin.X) / image.Width), SkewY = F((right.Y - origin.Y) / image.Width),
                    SkewX = F((bottom.X - origin.X) / image.Height), ScaleY = F((bottom.Y - origin.Y) / image.Height), TransX = F(origin.X), TransY = F(origin.Y), Persp2 = 1 };
                canvas.Save();
                try { canvas.Concat(ref matrix); canvas.DrawImage(image, 0, 0); }
                finally { canvas.Restore(); }
            }
            else
            {
                using var outline = new SKPath();
                for (var i = 0; i < reference.Corners.Length; i++)
                {
                    var p = frame.Viewport.ToScreen(reference.Corners[i]); if (i == 0) outline.MoveTo(F(p.X), F(p.Y)); else outline.LineTo(F(p.X), F(p.Y));
                }
                outline.Close(); canvas.DrawPath(outline, paint);
            }
        }
        if (item.Text is not { } text || text.Height <= 0) return;
        var fontEntry = GetFont(text.FontFamily);
        using var font = new SKFont(fontEntry.Typeface, F(text.Height)) { Edging = SKFontEdging.Antialias, Subpixel = true };
        paint.Style = SKPaintStyle.Fill;
        var textOrigin = frame.Viewport.ToScreen(text.Origin);
        var scale = frame.Viewport.PixelsPerUnit;
        var textMatrix = new SKMatrix { ScaleX = F(text.XAxis.X * scale), SkewY = F(-text.XAxis.Y * scale),
            SkewX = F(-text.YAxis.X * scale), ScaleY = F(text.YAxis.Y * scale), TransX = F(textOrigin.X), TransY = F(textOrigin.Y), Persp2 = 1 };
        canvas.Save();
        try
        {
            canvas.Concat(ref textMatrix);
            foreach (var line in TextLayout.Layout(text, s => fontEntry.Shaper.Shape(s, font).Width))
                canvas.DrawShapedText(fontEntry.Shaper, line.Text, F(line.X), F(line.Y), SKTextAlign.Left, font, paint);
        }
        finally { canvas.Restore(); }
    }

    private FontEntry GetFont(string family)
    {
        if (fonts.TryGetValue(family, out var font)) return font;
        if (fonts.Count >= 64) { foreach (var old in fonts.Values) old.Dispose(); fonts.Clear(); }
        var typeface = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName("sans-serif")
            ?? throw new InvalidOperationException("No usable system font; install fonts or configure a custom text renderer.");
        font = new(typeface, new SKShaper(typeface)); fonts[family] = font; return font;
    }
    private static float F(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue) throw new ArithmeticException("Geometry exceeds the backend coordinate range.");
        return (float)value;
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return; disposed = true;
            foreach (var picture in pictures.Values) picture.Dispose(); pictures.Clear();
            foreach (var font in fonts.Values) font.Dispose(); fonts.Clear();
        }
    }
}
