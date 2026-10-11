using global::SkiaSharp;
using global::SkiaSharp.HarfBuzz;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

/// <summary>Returns borrowed images. The host controls trust, synchronization and resource lifetime.</summary>
public interface ISkiaImageResolver { SKImage? Resolve(string reference); }
/// <summary>Changing an image without changing its DXF reference must advance Revision.</summary>
public interface IVersionedSkiaImageResolver : ISkiaImageResolver { long Revision { get; } }

/// <summary>PNG output and direct rendering to caller-owned CPU/GPU canvases.</summary>
public sealed partial class SkiaRenderBackend : IRenderBackend
{
    private sealed record FontEntry(SKTypeface Typeface, SKShaper Shaper) : IDisposable
    {
        public void Dispose() { Shaper.Dispose(); Typeface.Dispose(); }
    }
    private readonly object gate = new();
    private readonly Dictionary<string, FontEntry> fonts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(RenderScene, Viewport, long, uint), SKPicture> pictures = new();
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
            cancellationToken.ThrowIfCancellationRequested();
            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(0, 0, frame.Viewport.Width, frame.Viewport.Height));
                canvas.Clear(new SKColor(frame.Background));
                // Unversioned resolvers may return new assets on every call. Never retain a stale picture.
                if (images is not null && images is not IVersionedSkiaImageResolver) DrawItems(canvas);
                else
                {
                    var key = (frame.Scene, frame.Viewport, (images as IVersionedSkiaImageResolver)?.Revision ?? 0, frame.Background);
                    if (!pictures.TryGetValue(key, out var picture))
                    {
                        using var recorder = new SKPictureRecorder();
                        var recording = recorder.BeginRecording(new SKRect(0, 0, frame.Viewport.Width, frame.Viewport.Height));
                        DrawItems(recording);
                        picture = recorder.EndRecording();
                        if (pictures.Count >= 4)
                        {
                            foreach (var old in pictures.Values) old.Dispose();
                            pictures.Clear();
                        }
                        pictures[key] = picture;
                    }
                    canvas.DrawPicture(picture);
                }
                if (frame.Selection is { Count: > 0 })
                    foreach (var item in frame.VisibleItems())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (frame.Selection.Contains(item.Key)) DrawItem(canvas, item, frame, frame.SelectionColor, true);
                    }
            }
            finally { canvas.Restore(); }

            void DrawItems(SKCanvas target)
            {
                foreach (var item in frame.VisibleItems())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    DrawItem(target, item, frame, item.Style.Argb, false);
                }
            }
        }
    }

    private void DrawItem(SKCanvas canvas, RenderItem item, RenderFrame frame, uint color, bool selected)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true, Color = new SKColor(color), Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)Math.Max(1, item.Style.Lineweight * 96 / 25.4),
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        DrawRegions(canvas, item, frame, paint, selected);
        if (item.Filled)
        {
            using var fill = BuildPath(item.Contours, frame.Viewport, CadFillRule.EvenOdd);
            paint.Style = SKPaintStyle.Fill; canvas.DrawPath(fill, paint); paint.Style = SKPaintStyle.Stroke;
        }
        var contours = StrokePattern.GetContours(item);
        using (var strokes = BuildPath(contours, frame.Viewport, CadFillRule.EvenOdd)) canvas.DrawPath(strokes, paint);
        foreach (var contour in contours)
        {
            if (contour.Points.Length == 2 && contour.Points[0] == contour.Points[1])
            {
                var point = frame.Viewport.ToScreen(contour.Points[0]);
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(F(point.X), F(point.Y), Math.Max(0.5f, paint.StrokeWidth / 2), paint);
                paint.Style = SKPaintStyle.Stroke;
            }
        }
        if (item.Image is { Visible: true } reference) DrawImage(canvas, reference, frame, paint, selected);
        if (item.Text is { Height: > 0 } text) DrawText(canvas, text, frame, paint);
    }

    private static SKPath BuildPath(IEnumerable<RenderContour> contours, Viewport viewport, CadFillRule fillRule)
    {
        var path = new SKPath { FillType = fillRule == CadFillRule.NonZero ? SKPathFillType.Winding : SKPathFillType.EvenOdd };
        try
        {
            foreach (var contour in contours)
            {
                for (var i = 0; i < contour.Points.Length; i++)
                {
                    var p = viewport.ToScreen(contour.Points[i]);
                    if (i == 0) path.MoveTo(F(p.X), F(p.Y)); else path.LineTo(F(p.X), F(p.Y));
                }
                if (contour.Closed) path.Close();
            }
            return path;
        }
        catch { path.Dispose(); throw; }
    }

    private void DrawText(SKCanvas canvas, CadText text, RenderFrame frame, SKPaint paint)
    {
        var fontEntry = GetFont(text.FontFamily);
        using var font = new SKFont(fontEntry.Typeface, F(text.Height)) { Edging = SKFontEdging.Antialias, Subpixel = true };
#pragma warning disable CS0618
        using var metricsPaint = new SKPaint { Typeface = fontEntry.Typeface, TextSize = F(text.Height) };
        double Measure(string value) => fontEntry.Shaper.Shape(value, metricsPaint).Width;
#pragma warning restore CS0618
        paint.Style = SKPaintStyle.Fill;
        var origin = frame.Viewport.ToScreen(text.Origin);
        var scale = frame.Viewport.PixelsPerUnit;
        var matrix = new SKMatrix
        {
            ScaleX = F(text.XAxis.X * scale), SkewY = F(-text.XAxis.Y * scale),
            SkewX = F(-text.YAxis.X * scale), ScaleY = F(text.YAxis.Y * scale),
            TransX = F(origin.X), TransY = F(origin.Y), Persp2 = 1
        };
        canvas.Save();
        try
        {
            canvas.Concat(in matrix);
            foreach (var line in TextLayout.Layout(text, Measure))
                canvas.DrawShapedText(fontEntry.Shaper, line.Text, F(line.X), F(line.Y), SKTextAlign.Left, font, paint);
        }
        finally { canvas.Restore(); }
    }

    private FontEntry GetFont(string family)
    {
        if (fonts.TryGetValue(family, out var font)) return font;
        if (fonts.Count >= 64)
        {
            foreach (var old in fonts.Values) old.Dispose();
            fonts.Clear();
        }
        var typeface = SKTypeface.FromFamilyName(family) ?? SKTypeface.FromFamilyName("sans-serif")
            ?? throw new InvalidOperationException("No usable system font. Install fonts or configure a custom text renderer.");
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
            if (disposed) return;
            disposed = true;
            foreach (var picture in pictures.Values) picture.Dispose(); pictures.Clear();
            foreach (var font in fonts.Values) font.Dispose(); fonts.Clear();
        }
    }
}
