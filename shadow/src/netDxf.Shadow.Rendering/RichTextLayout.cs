using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public readonly record struct TextRunMetrics(double Advance, double Ascent, double Descent, Bounds2? GlyphBounds = null);
public sealed record PositionedTextRun(string Text, RichTextStyle Style, double X, double Y, double Advance);
public sealed record TextRule(double X1, double Y1, double X2, double Y2, double Thickness, uint? Color);
public sealed record RichTextLayoutResult(ImmutableArray<PositionedTextRun> Runs, ImmutableArray<TextRule> Rules,
    Bounds2 Bounds, ImmutableArray<TextFormatDiagnostic> Diagnostics);

/// <summary>Backend-neutral run layout. Normal runs stay intact for contextual shaping. Explicit
/// tracking operates on Unicode text elements, not UTF-16 code units. Long unbreakable words
/// may overflow rather than being split inside a shaping cluster.</summary>
public static class RichTextLayout
{
    private sealed record Box(ImmutableArray<PositionedTextRun> Runs, ImmutableArray<TextRule> Rules,
        double Width, double Ascent, double Descent, Bounds2 Bounds, int Alignment, bool Space = false);
    private sealed record Row(List<Box> Boxes, double Width, double Ascent, double Descent);

    public static RichTextLayoutResult Layout(CadText text, Func<RichTextStyle, string, TextRunMetrics> measure,
        int maximumRuns = 200_000)
    {
        ArgumentNullException.ThrowIfNull(text); ArgumentNullException.ThrowIfNull(measure);
        if (maximumRuns < 1) throw new ArgumentOutOfRangeException(nameof(maximumRuns));
        var document = RichText.Parse(text);
        var rows = new List<Row>(); var current = new List<Box>(); var word = new List<Box>();
        var width = 0.0; var wordWidth = 0.0; var measuredRuns = 0; var hasContent = false;
        var limit = text.Multiline && text.Width > 0 ? text.Width : double.PositiveInfinity;
        foreach (var inline in document.Inlines)
        {
            if (inline.Lower is not null) { AddWord(Stack(inline)); continue; }
            var start = 0;
            for (var i = 0; i <= inline.Text.Length; i++)
            {
                if (i < inline.Text.Length && inline.Text[i] is not (' ' or '\t' or '\n')) continue;
                if (i > start) AddWord(Plain(inline.Text[start..i], inline.Style));
                if (i == inline.Text.Length) break;
                CommitWord();
                if (inline.Text[i] == '\n') FinishRow();
                else
                {
                    var box = Plain(inline.Text[i] == '\t' ? "    " : " ", inline.Style) with { Space = true };
                    current.Add(box); width += box.Width;
                }
                start = i + 1;
            }
        }
        CommitWord(); FinishRow();
        var totalHeight = rows.Sum(row => row.Ascent + row.Descent) + Math.Max(0, rows.Count - 1) * text.Height * 0.2;
        var contentWidth = rows.Select(row => row.Width).DefaultIfEmpty(0).Max();
        var boxWidth = text.Multiline && text.Width > 0 ? Math.Max(contentWidth, text.Width) : contentWidth;
        var top = text.Alignment.StartsWith("Top", StringComparison.Ordinal) ? 0 :
            text.Alignment.StartsWith("Middle", StringComparison.Ordinal) || text.Alignment == "Middle" ? -totalHeight / 2 :
            text.Alignment.StartsWith("Bottom", StringComparison.Ordinal) ? -totalHeight : -rows[0].Ascent;
        var runs = ImmutableArray.CreateBuilder<PositionedTextRun>(); var rules = ImmutableArray.CreateBuilder<TextRule>();
        var bounds = Bounds2.Empty;
        foreach (var row in rows)
        {
            var alignedWidth = text.Multiline ? boxWidth : row.Width;
            var x = text.Alignment.EndsWith("Center", StringComparison.Ordinal) || text.Alignment == "Middle" ? -alignedWidth / 2 :
                text.Alignment.EndsWith("Right", StringComparison.Ordinal) ? -alignedWidth : 0;
            foreach (var box in row.Boxes)
            {
                var baseline = box.Alignment switch
                {
                    2 => top + box.Ascent,
                    1 => top + (row.Ascent + row.Descent - box.Ascent - box.Descent) / 2 + box.Ascent,
                    _ => top + row.Ascent
                };
                foreach (var run in box.Runs)
                {
                    if (runs.Count >= maximumRuns) throw new InvalidOperationException("Text layout run budget exceeded.");
                    runs.Add(run with { X = x + run.X, Y = baseline + run.Y });
                }
                foreach (var rule in box.Rules) rules.Add(rule with { X1 = x + rule.X1, X2 = x + rule.X2, Y1 = baseline + rule.Y1, Y2 = baseline + rule.Y2 });
                bounds = bounds.Union(Translate(box.Bounds, x, baseline)); x += box.Width;
            }
            top += row.Ascent + row.Descent + text.Height * 0.2;
        }
        return new(runs.ToImmutable(), rules.ToImmutable(), bounds, document.Diagnostics);

        void AddWord(Box box) { word.Add(box); wordWidth += box.Width; }
        void CommitWord()
        {
            if (word.Count == 0) return;
            if (hasContent && width + wordWidth > limit)
            {
                while (current.Count > 0 && current[^1].Space) { width -= current[^1].Width; current.RemoveAt(current.Count - 1); }
                FinishRow();
            }
            current.AddRange(word); width += wordWidth; hasContent = true; word.Clear(); wordWidth = 0;
        }
        void FinishRow()
        {
            if (rows.Count >= maximumRuns) throw new InvalidOperationException("Text row budget exceeded.");
            var ascent = Math.Max(text.Height * 0.8, current.Select(box => box.Ascent).DefaultIfEmpty(0).Max());
            var descent = Math.Max(text.Height * 0.2, current.Select(box => box.Descent).DefaultIfEmpty(0).Max());
            rows.Add(new(current, width, ascent, descent)); current = new(); width = 0; hasContent = false;
        }
        TextRunMetrics Metrics(RichTextStyle style, string content)
        {
            if (++measuredRuns > maximumRuns) throw new InvalidOperationException("Text measurement budget exceeded.");
            var metrics = measure(style, content);
            if (!double.IsFinite(metrics.Advance) || !double.IsFinite(metrics.Ascent) || !double.IsFinite(metrics.Descent)
                || metrics.Advance < 0 || metrics.Ascent < 0 || metrics.Descent < 0)
                throw new ArgumentException("Text metrics must be finite and nonnegative.", nameof(measure));
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
                    if (count++ > 0) advance += (style.Tracking - 1) * style.Height * 0.5 * style.WidthFactor;
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
                var m = Metrics(style, value); var runWidth = m.Advance * style.WidthFactor;
                var rectangle = m.GlyphBounds ?? new Bounds2(0, -m.Ascent, m.Advance, m.Descent);
                if (!rectangle.IsEmpty)
                {
                    var shear = -Math.Tan(style.ObliqueDegrees * Math.PI / 180);
                    foreach (var px in new[] { rectangle.MinX, rectangle.MaxX })
                        foreach (var py in new[] { rectangle.MinY, rectangle.MaxY })
                            ink = ink.Include(new(advance + px * style.WidthFactor + shear * py, py));
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
