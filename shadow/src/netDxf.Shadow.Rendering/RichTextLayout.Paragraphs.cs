using System.Collections.Immutable;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering;

public static partial class RichTextLayout
{
    private sealed record ParagraphToken(List<Box> Boxes, double Width, bool Space = false, bool Tab = false);
    private sealed record Paragraph(List<ParagraphToken> Tokens, RichTextParagraphStyle Style);
    private sealed record Placement(Box Box, double X);
    private sealed class ParagraphRow(int paragraph, int number, RichTextParagraphStyle style, double left, double right, double height)
    {
        public readonly int Paragraph = paragraph;
        public readonly int Number = number;
        public readonly RichTextParagraphStyle Style = style;
        public readonly double Left = left;
        public readonly double Right = right;
        public readonly List<Placement> Boxes = new();
        public double Cursor = left;
        public double Ascent = height * 0.8;
        public double Descent = height * 0.2;
        public double NominalHeight = height;
        public double Baseline;
        public bool HasContent;
        public bool HasTab;
        public bool End;
    }

    private static RichTextLayoutResult ComposeParagraphs(CadText text, RichTextDocument document,
        Func<string, RichTextStyle, Box> plain, Func<RichTextInline, Box> stack, int maximumRuns)
    {
        if (text.LineSpacing is { } spacing &&
            (!double.IsFinite(spacing.Factor) || spacing.Factor < 0.25 || spacing.Factor > 4))
            throw new ArgumentOutOfRangeException(nameof(text), "Entity line spacing factor must be finite and between 0.25 and 4.");
        var paragraphs = new List<Paragraph>();
        var tokens = new List<ParagraphToken>();
        var word = new List<Box>();
        var wordWidth = 0.0;
        var format = RichTextParagraphStyle.Default;
        var assigned = false;
        var tokenCount = 0;
        foreach (var inline in document.Inlines)
        {
            if (inline.IsParagraphControl)
            {
                format = inline.Style.Paragraph; assigned = true; continue;
            }
            if (inline.Lower is not null)
            {
                AssignStyle(inline.Style.Paragraph); AddWord(stack(inline)); continue;
            }
            var start = 0;
            for (var i = 0; i <= inline.Text.Length; i++)
            {
                if (i < inline.Text.Length && inline.Text[i] is not (' ' or '\t' or '\n')) continue;
                if (i > start)
                {
                    AssignStyle(inline.Style.Paragraph);
                    AddWord(plain(inline.Text[start..i], inline.Style));
                }
                if (i == inline.Text.Length) break;
                AssignStyle(inline.Style.Paragraph);
                CommitWord();
                switch (inline.Text[i])
                {
                    case '\n':
                        FinishParagraph();
                        // Retain the style for a trailing empty paragraph, but let the
                        // next actual inline establish its context after a scope exit.
                        format = inline.Style.Paragraph;
                        break;
                    case '\t': AddToken(new([], 0, Tab: true)); break;
                    default:
                        var space = plain(" ", inline.Style) with { Space = true };
                        AddToken(new([space], space.Width, Space: true));
                        break;
                }
                start = i + 1;
            }
        }
        CommitWord(); FinishParagraph();

        var rows = new List<ParagraphRow>();
        var issues = document.Diagnostics.ToBuilder();
        var definedWidth = text.Multiline && text.Width > 0;
        for (var p = 0; p < paragraphs.Count; p++)
        {
            var paragraph = paragraphs[p];
            var style = paragraph.Style;
            var source = paragraph.Tokens;
            var tabStops = new ParagraphTabIndex(style.Tabs, text.Height);
            // Every field is measured once in this reverse pass, independent of how
            // many tab stops a wide right/center field skips during binary lookup.
            var fields = new double[source.Count];
            var fieldWidth = 0.0;
            for (var i = source.Count - 1; i >= 0; i--)
            {
                if (source[i].Tab) { fields[i] = fieldWidth; fieldWidth = 0; }
                else fieldWidth += source[i].Width;
                if (!double.IsFinite(fieldWidth)) throw new ArgumentException("Tab field width overflowed.");
            }
            var number = 0;
            var row = NewRow();
            if (definedWidth && text.Width <= (style.LeftIndent + style.RightIndent) * text.Height)
                issues.Add(new(0, "text.paragraph.width", "Paragraph indents exhaust the column; intact words overflow rather than disappearing."));
            for (var i = 0; i < source.Count; i++)
            {
                var token = source[i];
                if (token.Tab)
                {
                    var tab = tabStops.Next(row.Cursor, fields[i]);
                    row.Cursor = tab.X;
                    row.HasTab = true;
                    if (tab.Alignment != ParagraphTabAlignment.Left)
                    {
                        // Aligned fields are atomic shaping cells. Overflow is explicit;
                        // wrapping only their suffix would destroy the tab alignment.
                        while (i + 1 < source.Count && !source[i + 1].Tab) Append(source[++i]);
                    }
                    continue;
                }
                if (!token.Space && row.HasContent && row.Cursor + token.Width > row.Right)
                {
                    FinishRow(false); row = NewRow();
                }
                Append(token);
            }
            FinishRow(true);

            ParagraphRow NewRow()
            {
                var left = text.Height * Math.Max(0, style.LeftIndent + (number == 0 ? style.FirstLineIndent : 0));
                var right = definedWidth ? Math.Max(left, text.Width - style.RightIndent * text.Height) : double.PositiveInfinity;
                return new(p, number++, style, left, right, text.Height);
            }
            void Append(ParagraphToken token)
            {
                foreach (var box in token.Boxes)
                {
                    row.Boxes.Add(new(box, row.Cursor)); row.Cursor += box.Width;
                    row.Ascent = Math.Max(row.Ascent, box.Ascent); row.Descent = Math.Max(row.Descent, box.Descent);
                    foreach (var run in box.Runs) row.NominalHeight = Math.Max(row.NominalHeight, run.Style.Height);
                }
                row.HasContent |= !token.Space;
            }
            void FinishRow(bool end)
            {
                // Consume separator spaces at a soft wrap only. Authored terminal
                // spaces remain part of a paragraph's center/right alignment width.
                while (!end && row.Boxes.Count > 0 && row.Boxes[^1].Box.Space)
                {
                    row.Cursor = row.Boxes[^1].X; row.Boxes.RemoveAt(row.Boxes.Count - 1);
                }
                if (!double.IsFinite(row.Cursor) || !double.IsFinite(row.Ascent + row.Descent))
                    throw new ArgumentException("Paragraph layout overflowed.");
                if (rows.Count >= maximumRuns) throw new InvalidOperationException("Text row budget exceeded.");
                row.End = end; rows.Add(row);
            }
        }

        var totalHeight = 0.0;
        ParagraphRow? previous = null;
        foreach (var row in rows)
        {
            if (previous is null)
                row.Baseline = row.Style.SpaceBefore * text.Height + row.Ascent;
            else
            {
                row.Baseline = previous.Baseline + BaselineDistance(previous, row);
                if (previous.Paragraph != row.Paragraph)
                    row.Baseline += (previous.Style.SpaceAfter + row.Style.SpaceBefore) * text.Height;
            }
            var bottom = row.Baseline + row.Descent + (row.End ? row.Style.SpaceAfter * text.Height : 0);
            if (!double.IsFinite(bottom)) throw new ArgumentException("Paragraph line positions overflowed.");
            totalHeight = Math.Max(totalHeight, bottom); previous = row;
        }
        var contentWidth = rows.Max(row => row.Cursor + row.Style.RightIndent * text.Height);
        var boxWidth = definedWidth ? text.Width : contentWidth;
        var yOrigin = text.Alignment.StartsWith("Top", StringComparison.Ordinal) ? 0 :
            text.Alignment.StartsWith("Middle", StringComparison.Ordinal) || text.Alignment == "Middle" ? -totalHeight / 2 :
            text.Alignment.StartsWith("Bottom", StringComparison.Ordinal) ? -totalHeight : -rows[0].Baseline;
        var runs = ImmutableArray.CreateBuilder<PositionedTextRun>();
        var rules = ImmutableArray.CreateBuilder<TextRule>();
        var lines = ImmutableArray.CreateBuilder<RichTextLine>(rows.Count);
        var bounds = Bounds2.Empty;
        foreach (var row in rows)
        {
            var xOrigin = HorizontalOrigin(text.Multiline ? boxWidth : row.Cursor);
            var right = definedWidth ? row.Right : boxWidth - row.Style.RightIndent * text.Height;
            var slack = Math.Max(0, right - row.Cursor);
            var shift = row.Style.Alignment switch { ParagraphAlignment.Center => slack / 2, ParagraphAlignment.Right => slack, _ => 0 };
            var justify = !row.HasTab && (row.Style.Alignment == ParagraphAlignment.Distributed || row.Style.Alignment == ParagraphAlignment.Justified && !row.End);
            var firstWord = row.Boxes.FindIndex(p => !p.Box.Space);
            var lastWord = row.Boxes.FindLastIndex(p => !p.Box.Space);
            var gaps = 0;
            if (justify) for (var i = firstWord + 1; i < lastWord; i++) if (row.Boxes[i].Box.Space) gaps++;
            var extra = gaps == 0 ? 0 : slack / gaps;
            var distributed = 0.0;
            for (var i = 0; i < row.Boxes.Count; i++)
            {
                var placed = row.Boxes[i]; var box = placed.Box;
                var stretch = justify && i > firstWord && i < lastWord && box.Space ? extra : 0;
                var x = xOrigin + placed.X + shift + distributed;
                var rowTop = row.Baseline - row.Ascent;
                var baseline = yOrigin + (box.Alignment switch
                {
                    2 => rowTop + box.Ascent,
                    1 => rowTop + (row.Ascent + row.Descent - box.Ascent - box.Descent) / 2 + box.Ascent,
                    _ => row.Baseline
                });
                foreach (var run in box.Runs)
                {
                    if (runs.Count >= maximumRuns) throw new InvalidOperationException("Text layout run budget exceeded.");
                    runs.Add(run with { X = x + run.X, Y = baseline + run.Y, Advance = run.Advance + stretch });
                }
                foreach (var rule in box.Rules)
                {
                    var placedRule = rule with { X1 = x + rule.X1, X2 = x + rule.X2 + stretch, Y1 = baseline + rule.Y1, Y2 = baseline + rule.Y2 };
                    rules.Add(placedRule); bounds = bounds.Union(RuleBounds(placedRule));
                }
                var ink = box.Bounds;
                if (stretch > 0 && !ink.IsEmpty) ink = new(ink.MinX, ink.MinY, ink.MaxX + stretch, ink.MaxY);
                bounds = bounds.Union(Translate(ink, x, baseline)); distributed += stretch;
            }
            lines.Add(new(row.Paragraph, row.Number, xOrigin + row.Left + shift,
                xOrigin + row.Cursor + shift + distributed, yOrigin + row.Baseline, row.End));
        }
        var layoutX = HorizontalOrigin(boxWidth);
        return new(runs.ToImmutable(), rules.ToImmutable(), bounds, issues.ToImmutable())
        {
            Lines = lines.MoveToImmutable(),
            LayoutBounds = new(layoutX, yOrigin, layoutX + boxWidth, yOrigin + totalHeight)
        };

        double HorizontalOrigin(double width) =>
            text.Alignment.EndsWith("Center", StringComparison.Ordinal) || text.Alignment == "Middle" ? -width / 2 :
            text.Alignment.EndsWith("Right", StringComparison.Ordinal) ? -width : 0;

        double BaselineDistance(ParagraphRow before, ParagraphRow after)
        {
            var natural = Math.Max(5.0 / 3.0 * Math.Max(before.NominalHeight, after.NominalHeight), before.Descent + after.Ascent);
            return after.Style.LineSpacing switch
            {
                ParagraphLineSpacing.Exact => after.Style.LineSpacingValue,
                ParagraphLineSpacing.AtLeast => Math.Max(natural, after.Style.LineSpacingValue),
                ParagraphLineSpacing.Multiple => natural * after.Style.LineSpacingValue,
                _ when text.Multiline && text.LineSpacing is { } authored => authored.Exact
                    ? text.Height * (5.0 / 3.0) * authored.Factor
                    : Math.Max(natural, text.Height * (5.0 / 3.0) * authored.Factor),
                _ => before.Descent + after.Ascent + text.Height * 0.2
            };
        }
        void AssignStyle(RichTextParagraphStyle style)
        {
            if (assigned) return;
            format = style; assigned = true;
        }
        void AddWord(Box box) { word.Add(box); wordWidth += box.Width; }
        void AddToken(ParagraphToken token)
        {
            if (++tokenCount > maximumRuns) throw new InvalidOperationException("Paragraph token budget exceeded.");
            tokens.Add(token);
        }
        void CommitWord()
        {
            if (word.Count == 0) return;
            AddToken(new(word, wordWidth)); word = new(); wordWidth = 0;
        }
        void FinishParagraph()
        {
            if (paragraphs.Count >= maximumRuns) throw new InvalidOperationException("Text paragraph budget exceeded.");
            paragraphs.Add(new(tokens, format)); tokens = new(); assigned = false;
        }
    }

    /// <summary>Three sorted indexes make aligned tab lookup logarithmic without a
    /// repeated scan of all skipped stops for wide center/right-aligned fields.</summary>
    private sealed class ParagraphTabIndex
    {
        private readonly double[][] positions;
        private readonly double interval;
        private readonly double last;
        public ParagraphTabIndex(ImmutableArray<ParagraphTabStop> tabs, double height)
        {
            interval = 4 * height;
            positions = new double[3][];
            for (var i = 0; i < 3; i++) positions[i] = tabs.Where(t => (int)t.Alignment == i).Select(t => t.Position * height).ToArray();
            last = tabs.IsEmpty ? 0 : tabs[^1].Position * height;
        }
        public (double X, ParagraphTabAlignment Alignment) Next(double cursor, double width)
        {
            var position = double.PositiveInfinity;
            var x = 0.0;
            var alignment = ParagraphTabAlignment.Left;
            for (var i = 0; i < 3; i++)
            {
                var adjustment = i == 1 ? width / 2 : i == 2 ? width : 0;
                var minimum = Math.Max(Math.BitIncrement(cursor), cursor + adjustment);
                var values = positions[i]; var low = 0; var high = values.Length;
                while (low < high)
                {
                    var middle = low + (high - low) / 2;
                    if (values[middle] < minimum) low = middle + 1; else high = middle;
                }
                if (low < values.Length && values[low] < position)
                {
                    position = values[low]; x = position - adjustment; alignment = (ParagraphTabAlignment)i;
                }
            }
            if (double.IsPositiveInfinity(position)) x = (Math.Floor(Math.Max(cursor, last) / interval) + 1) * interval;
            if (!double.IsFinite(x)) throw new ArgumentException("Tab positions overflowed.");
            return (x, alignment);
        }
    }
}
