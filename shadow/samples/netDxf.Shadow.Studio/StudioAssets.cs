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
    private const string BitmapPrefix = "data:image/bmp;base64,";
    private const int MaximumBitmapBytes = 32 * 1024 * 1024;
    private readonly Dictionary<string, Raster> previews = new(StringComparer.Ordinal);
    private readonly LinkedList<string> previewOrder = new();
    private long previewBytes;
    private sealed record Raster(SKImage Image, SvgImageData Encoded, long Bytes, LinkedListNode<string> Order);
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
        return reference == CheckerReference ? checker : Preview(reference)?.Image;
    }
    SvgImageData? ISvgImageResolver.Resolve(string reference)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return reference == CheckerReference ? encoded : Preview(reference)?.Encoded;
    }
    private Raster? Preview(string reference)
    {
        if (previews.TryGetValue(reference, out var cached))
        {
            previewOrder.Remove(cached.Order); previewOrder.AddLast(cached.Order); return cached;
        }
        if (!reference.StartsWith(BitmapPrefix, StringComparison.Ordinal)
            || reference.Length > BitmapPrefix.Length + (MaximumBitmapBytes + 2L) / 3 * 4) return null;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(reference[BitmapPrefix.Length..]); }
        catch (FormatException) { return null; }
        if (bytes.Length < 54 || bytes[0] != 'B' || bytes[1] != 'M') return null;
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Bmp) return null;
        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > 16384 || info.Height > 16384
            || (long)info.Width * info.Height > 16 * 1024 * 1024) return null;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null) return null;
        var image = SKImage.FromBitmap(bitmap);
        bool retained = false;
        try
        {
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            if (png is null || png.Size > 16 * 1024 * 1024) return null;
            var encodedPreview = new SvgImageData("image/png", png.ToArray());
            long memory = (long)info.Width * info.Height * 4 + encodedPreview.Data.Length;
            if (memory > 64 * 1024 * 1024) return null;
            while (previewOrder.First is { } first && (previews.Count >= 8 || previewBytes + memory > 64 * 1024 * 1024))
            {
                var previous = previews[first.Value]; previews.Remove(first.Value); previewOrder.RemoveFirst();
                previewBytes -= previous.Bytes; previous.Image.Dispose();
            }
            var result = new Raster(image, encodedPreview, memory, previewOrder.AddLast(reference));
            previews.Add(reference, result); previewBytes += memory; retained = true; return result;
        }
        finally { if (!retained) image.Dispose(); }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; checker.Dispose();
        foreach (var preview in previews.Values) preview.Image.Dispose();
        previews.Clear(); previewOrder.Clear(); previewBytes = 0;
    }
}
