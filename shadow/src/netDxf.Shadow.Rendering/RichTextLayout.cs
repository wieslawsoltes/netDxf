using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public readonly record struct TextRunMetrics(double Advance, double Ascent, double Descent, Bounds2? GlyphBounds = null);
public sealed record PositionedTextRun(string Text, RichTextStyle Style, double X, double Y, double Advance);
public sealed record TextRule(double X1, double Y1, double X2, double Y2, double Thickness, uint? Color);
public sealed record RichTextLine(int Paragraph, int Line, double Left, double Right, double Baseline, bool IsParagraphEnd);
public sealed record RichTextLayoutResult(ImmutableArray<PositionedTextRun> Runs, ImmutableArray<TextRule> Rules,
    Bounds2 Bounds, ImmutableArray<TextFormatDiagnostic> Diagnostics)
{
    public ImmutableArray<RichTextLine> Lines { get; init; } = [];
    public Bounds2 LayoutBounds { get; init; } = Bounds2.Empty;
}

/// <summary>Backend-neutral paragraph/run layout. Normal words stay intact for contextual
/// shaping. Tracking uses Unicode text elements. Overlong words and aligned tab fields
/// overflow instead of being split inside a shaping cluster.</summary>
public static partial class RichTextLayout
{
    private sealed record Box(ImmutableArray<PositionedTextRun> Runs, ImmutableArray<TextRule> Rules,
        double Width, double Ascent, double Descent, Bounds2 Bounds, int Alignment, bool Space = false);

    public static RichTextLayoutResult Layout(CadText text, Func<RichTextStyle, string, TextRunMetrics> measure,
        int maximumRuns = 200_000)
        => Layout(text, measure, false, maximumRuns);

    /// <summary>Composes vertical-flow metrics without applying horizontal width/shear a second time.
    /// The provider supplies flow/cross-flow ink bounds; runs retain their authored style.</summary>
    public static RichTextLayoutResult Layout(CadText text, Func<RichTextStyle, string, TextRunMetrics> measure,
        bool verticalFlow, int maximumRuns = 200_000)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(measure);
        if (maximumRuns < 1) throw new ArgumentOutOfRangeException(nameof(maximumRuns));
        if (!double.IsFinite(text.Width) || text.Width < 0) throw new ArgumentOutOfRangeException(nameof(text), "Text width must be finite and nonnegative.");
        var document = RichText.Parse(text);
        var measuredRuns = 0;
        return ComposeParagraphs(text, document, Plain, Stack, maximumRuns);

        TextRunMetrics Metrics(RichTextStyle style, string content)
        {
            if (++measuredRuns > maximumRuns) throw new InvalidOperationException("Text measurement budget exceeded.");
            var metrics = measure(style, content);
            if (!double.IsFinite(metrics.Advance) || !double.IsFinite(metrics.Ascent) || !double.IsFinite(metrics.Descent)
                || metrics.Advance < 0 || metrics.Ascent < 0 || metrics.Descent < 0)
                throw new ArgumentException("Text metrics must be finite and nonnegative.", nameof(measure));
            if (metrics.GlyphBounds is { IsEmpty: false } b &&
                (!double.IsFinite(b.MinX) || !double.IsFinite(b.MinY) || !double.IsFinite(b.MaxX) || !double.IsFinite(b.MaxY)))
                throw new ArgumentException("Glyph bounds must be finite.", nameof(measure));
            return metrics;
        }
        Box Plain(string content, RichTextStyle style)
        {
            var output = ImmutableArray.CreateBuilder<PositionedTextRun>();
            var ascent = 0.0; var descent = 0.0; var advance = 0.0; var ink = Bounds2.Empty;
            if (style.Tracking == 1) AppendRun(content);
            else
            {
                var elements = StringInfo.GetTextElementEnumerator(content); var count = 0;
                while (elements.MoveNext())
                {
                    if (count++ > 0) advance += (style.Tracking - 1) * style.Height * 0.5 * (verticalFlow ? 1 : style.WidthFactor);
                    AppendRun(elements.GetTextElement());
                }
            }
            var decoration = ImmutableArray.CreateBuilder<TextRule>();
            if (style.Decoration.HasFlag(TextDecoration.Underline)) decoration.Add(new(0, descent * 0.5, advance, descent * 0.5, style.Height / 16, style.Color));
            if (style.Decoration.HasFlag(TextDecoration.Overline)) decoration.Add(new(0, -ascent, advance, -ascent, style.Height / 16, style.Color));
            if (style.Decoration.HasFlag(TextDecoration.StrikeThrough)) decoration.Add(new(0, -ascent * 0.4, advance, -ascent * 0.4, style.Height / 16, style.Color));
            foreach (var rule in decoration) ink = ink.Union(RuleBounds(rule));
            return new(output.ToImmutable(), decoration.ToImmutable(), Math.Max(0, advance), ascent, descent, ink, style.VerticalAlignment);

            void AppendRun(string value)
            {
                var m = Metrics(style, value); var runWidth = m.Advance * (verticalFlow ? 1 : style.WidthFactor);
                if (!double.IsFinite(runWidth) || !double.IsFinite(advance + runWidth))
                    throw new ArgumentException("Text advance overflowed.", nameof(measure));
                var rectangle = m.GlyphBounds ?? new Bounds2(0, -m.Ascent, m.Advance, m.Descent);
                if (!rectangle.IsEmpty)
                {
                    var shear = verticalFlow ? 0 : -Math.Tan(style.ObliqueDegrees * Math.PI / 180);
                    foreach (var px in new[] { rectangle.MinX, rectangle.MaxX })
                        foreach (var py in new[] { rectangle.MinY, rectangle.MaxY })
                            ink = ink.Include(new(advance + px * (verticalFlow ? 1 : style.WidthFactor) + shear * py, py));
                }
                output.Add(new(value, style, advance, 0, runWidth)); advance += runWidth;
                ascent = Math.Max(ascent, m.Ascent); descent = Math.Max(descent, m.Descent);
            }
        }
        Box Stack(RichTextInline inline)
        {
            var style = inline.Style; var small = style with { Height = style.Height * 0.7, Decoration = TextDecoration.None };
            var a = Plain(inline.Text, small); var b = Plain(inline.Lower!, small);
            var padding = style.Height * 0.12; var diagonal = inline.StackSeparator == '#';
            var size = diagonal ? a.Width + b.Width + padding * 3 : Math.Max(a.Width, b.Width) + padding * 2;
            var x1 = diagonal ? 0 : (size - a.Width) / 2; var x2 = diagonal ? a.Width + padding * 3 : (size - b.Width) / 2;
            var y1 = -style.Height * 0.5; var y2 = style.Height * 0.25;
            var output = a.Runs.Select(r => r with { X = r.X + x1, Y = r.Y + y1 })
                .Concat(b.Runs.Select(r => r with { X = r.X + x2, Y = r.Y + y2 })).ToImmutableArray();
            var lines = ImmutableArray<TextRule>.Empty;
            if (inline.StackSeparator == '/') lines = [new(0, -style.Height * 0.25, size, -style.Height * 0.25, style.Height / 20, style.Color)];
            else if (diagonal) lines = [new(a.Width + padding * 0.5, y2, a.Width + padding * 2.5, y1 - a.Ascent, style.Height / 20, style.Color)];
            var ink = Translate(a.Bounds, x1, y1).Union(Translate(b.Bounds, x2, y2));
            foreach (var line in lines) ink = ink.Union(RuleBounds(line));
            return new(output, lines, size, a.Ascent - y1, b.Descent + y2, ink, style.VerticalAlignment);
        }
    }
    private static Bounds2 Translate(Bounds2 bounds, double x, double y) => bounds.IsEmpty ? bounds :
        new(bounds.MinX + x, bounds.MinY + y, bounds.MaxX + x, bounds.MaxY + y);
    private static Bounds2 RuleBounds(TextRule rule) => new Bounds2(Math.Min(rule.X1, rule.X2), Math.Min(rule.Y1, rule.Y2),
        Math.Max(rule.X1, rule.X2), Math.Max(rule.Y1, rule.Y2)).Inflate(rule.Thickness / 2);
    public static TextRunMetrics ApproximateMetrics(RichTextStyle style, string text)
        => new(text.EnumerateRunes().Count() * style.Height * 0.6, style.Height * 0.8, style.Height * 0.2);
}
