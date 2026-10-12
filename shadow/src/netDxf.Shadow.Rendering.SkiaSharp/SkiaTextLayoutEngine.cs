using System.Runtime.CompilerServices;
using global::SkiaSharp;
using global::SkiaSharp.HarfBuzz;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.SkiaSharp;

/// <summary>HarfBuzz shaping and native glyph bounds, detached into immutable layout results.
/// May be shared between a RenderCompiler and SkiaRenderBackend; the host owns its lifetime.</summary>
public sealed class SkiaTextLayoutEngine : ITextLayoutEngine, IDisposable
{
    private sealed record FontEntry(SKTypeface Typeface, SKShaper Shaper) : IDisposable
    {
        public void Dispose() { Shaper.Dispose(); Typeface.Dispose(); }
    }
    private readonly object gate = new();
    private readonly Dictionary<(string Family, bool Bold, bool Italic), FontEntry> fonts = new();
    private ConditionalWeakTable<CadText, RichTextLayoutResult> layouts = new();
    private long revision;
    private bool disposed;
    public long Revision => Interlocked.Read(ref revision);

    public RichTextLayoutResult Layout(CadText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return layouts.GetValue(text, value => RichTextLayout.Layout(value, Measure));
        }
    }

    public void InvalidateFonts()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            foreach (var font in fonts.Values) font.Dispose();
            fonts.Clear(); layouts = new(); Interlocked.Increment(ref revision);
        }
    }

    private TextRunMetrics Measure(RichTextStyle style, string content)
    {
        var entry = GetFont(style);
        using var font = new SKFont(entry.Typeface, F(style.Height)) { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None };
        var shape = entry.Shaper.Shape(content, font);
        var glyphs = new ushort[shape.Codepoints.Length];
        for (var i = 0; i < glyphs.Length; i++) glyphs[i] = checked((ushort)shape.Codepoints[i]);
        var rectangles = new SKRect[glyphs.Length];
        font.GetGlyphWidths(glyphs, Span<float>.Empty, rectangles);
        var bounds = Bounds2.Empty;
        for (var i = 0; i < rectangles.Length; i++)
        {
            var r = rectangles[i]; var p = shape.Points[i];
            if (r.IsEmpty) continue;
            bounds = bounds.Include(new(r.Left + p.X, r.Top + p.Y)).Include(new(r.Right + p.X, r.Bottom + p.Y));
        }
        var metrics = font.Metrics;
        return new(Math.Max(0, shape.Width), Math.Max(0, -metrics.Ascent), Math.Max(0, metrics.Descent), bounds);
    }

    internal void Draw(SKCanvas canvas, CadText text, RichTextLayoutResult layout, Viewport viewport, uint color, bool selected)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var origin = viewport.ToScreen(text.Origin); var scale = viewport.PixelsPerUnit;
            var matrix = new SKMatrix
            {
                ScaleX = F(text.XAxis.X * scale), SkewY = F(-text.XAxis.Y * scale),
                SkewX = F(-text.YAxis.X * scale), ScaleY = F(text.YAxis.Y * scale),
                TransX = F(origin.X), TransY = F(origin.Y), Persp2 = 1
            };
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.Save();
            try
            {
                canvas.Concat(in matrix);
                foreach (var run in layout.Runs)
                {
                    var entry = GetFont(run.Style);
                    using var font = new SKFont(entry.Typeface, F(run.Style.Height))
                    { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
                    paint.Color = new SKColor(Resolve(run.Style.Color));
                    var local = new SKMatrix
                    {
                        ScaleX = F(run.Style.WidthFactor), SkewX = F(-Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180)), ScaleY = 1,
                        TransX = F(run.X), TransY = F(run.Y), Persp2 = 1
                    };
                    canvas.Save();
                    try { canvas.Concat(in local); canvas.DrawShapedText(entry.Shaper, run.Text, 0, 0, SKTextAlign.Left, font, paint); }
                    finally { canvas.Restore(); }
                }
                paint.Style = SKPaintStyle.Stroke;
                foreach (var rule in layout.Rules)
                {
                    paint.Color = new SKColor(Resolve(rule.Color)); paint.StrokeWidth = F(rule.Thickness);
                    canvas.DrawLine(F(rule.X1), F(rule.Y1), F(rule.X2), F(rule.Y2), paint);
                }
            }
            finally { canvas.Restore(); }
            uint Resolve(uint? authored) => selected || authored is null ? color :
                (authored.Value & 0xffffffu) | ((authored.Value >> 24) * (color >> 24) / 255) << 24;
        }
    }

    private FontEntry GetFont(RichTextStyle style)
    {
        var key = (style.FontFamily, style.Bold, style.Italic);
        if (fonts.TryGetValue(key, out var entry)) return entry;
        if (fonts.Count >= 64) { foreach (var old in fonts.Values) old.Dispose(); fonts.Clear(); }
        using var nativeStyle = new SKFontStyle(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
        var family = style.FontFamily;
        if (family.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || family.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            family = Path.GetFileNameWithoutExtension(family);
        var typeface = SKTypeface.FromFamilyName(family, nativeStyle) ?? SKTypeface.FromFamilyName("sans-serif", nativeStyle)
            ?? throw new InvalidOperationException("No usable system font is installed.");
        entry = new(typeface, new SKShaper(typeface)); fonts[key] = entry; return entry;
    }
    private static float F(double value)
    {
        if (!double.IsFinite(value) || Math.Abs(value) > float.MaxValue) throw new ArithmeticException("Text coordinate exceeds the native backend range.");
        return (float)value;
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true; foreach (var entry in fonts.Values) entry.Dispose(); fonts.Clear(); layouts = new();
        }
    }
}
