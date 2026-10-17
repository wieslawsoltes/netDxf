using System.Runtime.InteropServices;
using global::SkiaSharp;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

/// <summary>Returns borrowed images. The host controls trust, synchronization and resource lifetime.</summary>
public interface ISkiaImageResolver { SKImage? Resolve(string reference); }
/// <summary>Changing an image without changing its DXF reference must advance Revision.</summary>
public interface IVersionedSkiaImageResolver : ISkiaImageResolver { long Revision { get; } }

/// <summary>PNG output and direct rendering to caller-owned CPU/GPU canvases.</summary>
public sealed partial class SkiaRenderBackend : IRasterRenderBackend
{
    private readonly object gate = new();
    private readonly Dictionary<(RenderScene, Viewport, long ImageRevision, long FontRevision, uint Background), SKPicture> pictures = new();
    private readonly Dictionary<(RenderClip, Viewport), SKPath> clipPaths = new();
    private SKPaint? itemPaint = CreateItemPaint();
    private static SKPaint CreateItemPaint() => new()
    {
        IsAntialias = true, Style = SKPaintStyle.Stroke,
        StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
    };
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

    public RenderImage RenderPixels(RenderFrame frame, CancellationToken cancellationToken = default)
    {
        frame.Viewport.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            using var surface = SKSurface.Create(new SKImageInfo(frame.Viewport.Width, frame.Viewport.Height, SKColorType.Rgba8888, SKAlphaType.Premul))
                ?? throw new InvalidOperationException("Skia could not allocate the render surface.");
            Draw(surface.Canvas, frame, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var info = new SKImageInfo(frame.Viewport.Width, frame.Viewport.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            var bytes = GC.AllocateUninitializedArray<byte>(checked(info.Width * info.Height * 4));
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                if (!surface.ReadPixels(info, pinned.AddrOfPinnedObject(), checked(info.Width * 4), 0, 0))
                    throw new InvalidOperationException("Skia could not read the render pixels.");
            }
            finally { pinned.Free(); }
            return new(bytes, "application/x-shadow-rgba");
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
            foreach (var path in clipPaths.Values) path.Dispose(); clipPaths.Clear();
            itemPaint?.Dispose(); itemPaint = null;
            if (ownsTextEngine) textEngine.Dispose();
        }
    }
}
