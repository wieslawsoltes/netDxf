using netDxf.Shadow.Rendering;
using global::SkiaSharp;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    // Called before exposing a new document to the browser UI. The resolver is owned by
    // the browser application; each returned typeface is owned by this text engine.
    internal void ConfigureBrowserFonts(Func<RichTextStyle, SKTypeface?> resolver) => textEngine.SetTypefaceResolver(resolver);
}
