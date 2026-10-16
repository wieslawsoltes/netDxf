using global::SkiaSharp;

namespace netDxf.Shadow.Rendering.SkiaSharp;

public sealed partial class SkiaTextLayoutEngine
{
    private Func<RichTextStyle, SKTypeface?>? typefaceResolver;

    /// <summary>Sets a host-owned font resolver and invalidates cached shaping and outlines.
    /// Return a newly owned typeface (the engine disposes it), or null to use system fallback.
    /// This allows browser hosts to supply fonts without relying on native system font discovery.</summary>
    public void SetTypefaceResolver(Func<RichTextStyle, SKTypeface?>? resolver)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            InvalidateFonts();
            typefaceResolver = resolver;
        }
    }
}
