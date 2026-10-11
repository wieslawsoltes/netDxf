using global::SkiaSharp;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

/// <summary>Returns borrowed images. The host controls trust, synchronization and resource lifetime.</summary>
public interface ISkiaImageResolver { SKImage? Resolve(string reference); }
/// <summary>Changing an image without changing its DXF reference must advance Revision.</summary>
public interface IVersionedSkiaImageResolver : ISkiaImageResolver { long Revision { get; } }

/// <summary>PNG output and direct rendering to caller-owned CPU/GPU canvases.</summary>
public sealed partial class SkiaRenderBackend : IRenderBackend
{
    private readonly object gate = new();
    private readonly Dictionary<(RenderScene, Viewport, long ImageRevision, long FontRevision, uint Background), SKPicture> pictures = new();
    private readonly ISkiaImageResolver? images;
    private readonly SkiaTextLayoutEngine textEngine;
    private readonly bool ownsTextEngine;
    private bool disposed;
    public string Id => "skia";
    public SkiaRenderBackend(ISkiaImageResolver? images = null, SkiaTextLayoutEngine? textLayout = null)
    {
        this.images = images; textEngine = textLayout ?? new(); ownsTextEngine = textLayout is null;
    }

    public RenderImage Render(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var surface = SKSurface.Create(new SKImageInfo(frame.Viewport.Width, frame.Viewport.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Skia could not allocate the render surface.");
            Draw(surface.Canvas, frame, cancellationToken);
            using var image = surface.Snapshot(); using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            return new(encoded.ToArray(), "image/png");
        }
    }

    public void Draw(SKCanvas canvas, RenderFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(canvas); frame.Viewport.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this); cancellationToken.ThrowIfCancellationRequested();
            canvas.Save();
            try
            {
                canvas.ClipRect(new SKRect(0, 0, frame.Viewport.Width, frame.Viewport.Height)); canvas.Clear(new SKColor(frame.Background));
                if (images is not null && images is not IVersionedSkiaImageResolver) DrawItems(canvas);
                else
                {
                    var key = (frame.Scene, frame.Viewport, (images as IVersionedSkiaImageResolver)?.Revision ?? 0, textEngine.Revision, frame.Background);
                    if (!pictures.TryGetValue(key, out var picture))
                    {
                        using var recorder = new SKPictureRecorder();
                        var recording = recorder.BeginRecording(new SKRect(0, 0, frame.Viewport.Width, frame.Viewport.Height));
                        DrawItems(recording); picture = recorder.EndRecording();
                        if (pictures.Count >= 4) { foreach (var old in pictures.Values) old.Dispose(); pictures.Clear(); }
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
                { cancellationToken.ThrowIfCancellationRequested(); DrawItem(target, item, frame, item.Style.Argb, false); }
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
                var point = frame.Viewport.ToScreen(contour.Points[0]); paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(F(point.X), F(point.Y), Math.Max(0.5f, paint.StrokeWidth / 2), paint); paint.Style = SKPaintStyle.Stroke;
            }
        }
        if (item.Image is { Visible: true } reference) DrawImage(canvas, reference, frame, paint, selected);
        if (item.Text is { Height: > 0 } text) textEngine.Draw(canvas, text, item.TextLayout ?? textEngine.Layout(text), frame.Viewport, color, selected);
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
            disposed = true; foreach (var picture in pictures.Values) picture.Dispose(); pictures.Clear();
            if (ownsTextEngine) textEngine.Dispose();
        }
    }
}
