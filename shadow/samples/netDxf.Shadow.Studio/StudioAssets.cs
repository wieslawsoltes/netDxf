using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;

namespace netDxf.Shadow.Studio;

/// <summary>Explicit in-memory sample assets. Untrusted drawing references never cause file or network access.</summary>
internal sealed class StudioAssets : IVersionedSkiaImageResolver, ISvgImageResolver, IDisposable
{
    internal const string CheckerReference = "netdxf-shadow-demo-checker.png";
    private readonly SKImage checker;
    private readonly SvgImageData encoded;
    private bool disposed;
    public long Revision => 0;
    public StudioAssets()
    {
        using var bitmap = new SKBitmap(128, 128, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint();
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
            {
                paint.Color = ((x + y) & 1) == 0 ? new SKColor(60, 155, 200) : new SKColor(228, 234, 242);
                canvas.DrawRect(x * 16, y * 16, 16, 16, paint);
            }
        }
        checker = SKImage.FromBitmap(bitmap);
        using var png = checker.Encode(SKEncodedImageFormat.Png, 100);
        encoded = new("image/png", png.ToArray());
    }
    SKImage? ISkiaImageResolver.Resolve(string reference)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reference == CheckerReference ? checker : null;
    }
    SvgImageData? ISvgImageResolver.Resolve(string reference)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reference == CheckerReference ? encoded : null;
    }
    public void Dispose() { if (disposed) return; disposed = true; checker.Dispose(); }
}
