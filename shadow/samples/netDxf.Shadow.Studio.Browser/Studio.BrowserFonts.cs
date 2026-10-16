using netDxf.Shadow.Rendering;
using global::SkiaSharp;

namespace netDxf.Shadow.Studio;

public sealed partial class Studio
{
    private sealed record BrowserFont(string File, string Family, bool Bold, bool Italic, byte[] Bytes);
    private readonly Dictionary<string, BrowserFont> browserFonts = new(StringComparer.OrdinalIgnoreCase);
    private Func<RichTextStyle, SKTypeface?>? browserFontFallback;

    // Each document owns its explicit fonts. No filename embedded in a drawing is opened
    // automatically, and font bytes never leave the browser. Native handles belong to the
    // text engine; the catalog retains only validated immutable copies of the input bytes.
    internal void ConfigureBrowserFonts(Func<RichTextStyle, SKTypeface?> resolver)
    {
        lock (gate)
        {
            browserFontFallback = resolver;
            textEngine.SetTypefaceResolver(ResolveBrowserFont);
        }
    }

    private SKTypeface? ResolveBrowserFont(RichTextStyle style)
    {
        var name = BrowserFontName(style.FontFamily);
        var candidates = browserFonts.Values.Where(font =>
            string.Equals(font.File, name, StringComparison.OrdinalIgnoreCase)
            || string.Equals(font.Family, name, StringComparison.OrdinalIgnoreCase));
        var selected = candidates.OrderBy(font => (font.Bold == style.Bold ? 0 : 2) + (font.Italic == style.Italic ? 0 : 1))
            .ThenBy(font => font.File, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if (selected is null) return browserFontFallback?.Invoke(style);
        using var data = SKData.CreateCopy(selected.Bytes);
        return SKTypeface.FromData(data) ?? throw new InvalidOperationException("A registered browser font could not be opened.");
    }

    internal object LoadBrowserFont(long revision, string fileName, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        if (!fileName.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            return LoadFont(revision, fileName, bytes);
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length < 16 || bytes.Length > MaximumFontBytes) throw new ArgumentException("Choose a valid TTF or OTF resource of at most 4 MiB.");
        var file = BrowserFontName(fileName);
        if (file.Length is 0 or > 120 || file.Any(char.IsControl)) throw new ArgumentException("Invalid font resource name.");
        // Parse before publication, so malformed input never poisons the active resolver.
        using var data = SKData.CreateCopy(bytes);
        using var typeface = SKTypeface.FromData(data) ?? throw new ArgumentException("The uploaded file is not a supported OpenType or TrueType font.");
        if (typeface.GlyphCount == 0) throw new ArgumentException("The uploaded font has no glyphs.");
        var font = new BrowserFont(file, typeface.FamilyName, typeface.IsBold, typeface.IsItalic, bytes.ToArray());
        lock (gate)
        {
            Check(revision); RequireLive();
            if (!browserFonts.ContainsKey(file) && browserFonts.Count >= 32)
                throw new InvalidOperationException("Each browser drawing allows at most 32 explicit TTF/OTF font resources.");
            browserFonts[file] = font;
            textEngine.InvalidateFonts();
            return UpdatedState();
        }
    }

    private static string BrowserFontName(string value)
    {
        var file = value.Replace('\\', '/').Split('/')[^1].Trim();
        return file.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".otf", StringComparison.OrdinalIgnoreCase)
            ? file[..^4] : file;
    }
}
