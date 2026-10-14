using System.Collections.Immutable;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

public sealed partial class ShxEntityProjector
{
    /// <summary>Vertical glyph execution is not a rotated horizontal string. Glyph origins and
    /// advances are authored by the SHX VM; paragraph flow uses downwards runs and leftwards columns.</summary>
    private CadGeometry ProjectVerticalText(CadText text, ShxFontSet fonts, ShxFont fallback, out string? diagnostic, ShxBigFontBinding? bigFont = null)
    {
        if (!fallback.SupportsVerticalText) throw new NotSupportedException("The selected SHX resource does not declare vertical text support.");
        if (text.Content.Length > options.MaximumCharacters) throw new InvalidOperationException("Vertical SHX character budget exceeded.");
        var issues = new SortedSet<string>(StringComparer.Ordinal);
        var instructionBudget = options.MaximumGlyphInstructions; long work = 0, curves = 0;
        var measurements = new Dictionary<(RichTextStyle, string), TextRunMetrics>();
        var flow = text with { Alignment = FlowAlignment(text.Alignment) };
        var layout = RichTextLayout.Layout(flow, Measure, verticalFlow: true, options.MaximumCharacters);
        foreach (var issue in layout.Diagnostics.Take(20)) Issue(issue.Message);
        var strokes = ImmutableArray.CreateBuilder<CadStrokeGroup>();
        var regions = ImmutableArray.CreateBuilder<CadRegion>();
        var frame = new Affine3(text.XAxis, text.YAxis, Vec3.Cross(text.XAxis, text.YAxis), text.Origin);
        foreach (var run in layout.Runs)
        {
            var font = Font(run.Style); var factor = run.Style.Height / font.CapHeight;
            var skew = Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180); var cursor = Vec3.Zero;
            var contours = ImmutableArray.CreateBuilder<CadContour>();
            foreach (var rune in run.Text.EnumerateRunes())
            {
                var glyph = Glyph(font, rune.Value);
                var local = new Affine3(new(factor * run.Style.WidthFactor, 0), new(skew * factor, factor), Vec3.UnitZ,
                    new(-run.Y + cursor.X * factor * run.Style.WidthFactor + skew * cursor.Y * factor,
                        -run.X + cursor.Y * factor));
                foreach (var contour in glyph.Contours)
                {
                    if ((curves += contour.Curves.Length) > options.MaximumCurves) throw new InvalidOperationException("Vertical SHX curve budget exceeded.");
                    contours.Add(contour with { Curves = contour.Curves.Select(c => c.Transform(frame * local)).ToImmutableArray() });
                }
                cursor += glyph.Advance;
            }
            if (contours.Count > 0) strokes.Add(new(contours.ToImmutable(), run.Style.Color));
        }
        foreach (var rule in layout.Rules)
        {
            var a = new Vec3(-rule.Y1, -rule.X1); var b = new Vec3(-rule.Y2, -rule.X2); var d = b - a;
            if (!(d.Length > 0)) continue;
            var n = new Vec3(-d.Y, d.X) * (rule.Thickness / (2 * d.Length));
            var vertices = new[] { frame.Point(a + n), frame.Point(b + n), frame.Point(b - n), frame.Point(a - n) };
            if ((curves += 4) > options.MaximumCurves) throw new InvalidOperationException("Vertical SHX decoration budget exceeded.");
            regions.Add(new([new(vertices.Select((v, i) => (Curve3)new LineCurve(v, vertices[(i + 1) % 4])).ToImmutableArray(), true)]) { Argb = rule.Color });
        }
        diagnostic = issues.Count == 0 ? null : string.Join(" ", issues);
        return new([]) { Strokes = strokes.ToImmutable(), Regions = regions.ToImmutable() };

        void Issue(string message) { if (issues.Count < 20) issues.Add(message); }
        ShxFont Font(RichTextStyle style)
        {
            var font = fonts.Resolve(style.FontFamily);
            if (font is null) { Issue($"Vertical SHX inline resource '{style.FontFamily}' is absent; entity resource retained."); font = fallback; }
            if (style.Bold || style.Italic)
            {
                var variant = fonts.Resolve(style.FontFamily + (style.Bold ? "|bold" : "") + (style.Italic ? "|italic" : ""));
                if (variant is not null) font = variant; else Issue("Vertical SHX bold/italic variant is absent; original strokes retained.");
            }
            if (!font.IsFont || !font.SupportsVerticalText || font.Encoding == ShxEncoding.PackedMultibyte)
                throw new NotSupportedException("Vertical text requires a resource with vertical Unicode/legacy metrics.");
            return font;
        }
        ShxGlyph Glyph(ShxFont font, int number)
        {
            if (++work > options.MaximumCharacters * 8L) throw new InvalidOperationException("Vertical SHX layout work budget exceeded.");
            if (MappedGlyph(font, bigFont, number, true, ref instructionBudget, Issue) is { } mapped) return mapped;
            var authored = number;
            if (font.Encoding == ShxEncoding.Legacy) number = number switch { 0xb0 => 256, 0xb1 => 257, 0x2205 => 258, _ => number };
            if (number == 0xa0 && !font.Symbols.ContainsKey(number)) number = 32;
            if (font.Symbols.ContainsKey(number))
            {
                var glyph = GetGlyph(font, number, ref instructionBudget, vertical: true);
                if (Math.Abs(glyph.ExitScale - 1) > 1e-12) throw new NotSupportedException("Vertical glyphs must restore vector scale between layout runs.");
                if (glyph.Advance.Y > 0) throw new NotSupportedException("A vertical glyph advances upward; monotone column composition cannot infer a forward text advance.");
                if (Math.Abs(glyph.Advance.X) > 1e-10) throw new NotSupportedException("Vertical paragraph glyphs must return to the column baseline before advancing.");
                return glyph;
            }
            if (number == 32) return new([], new(0, -font.CapHeight * .5), Bounds2.Empty);
            Issue($"Missing vertical SHX glyph U+{authored:X4}; an explicit box is drawn.");
            var missing = Missing(font); var shift = Affine3.Move(new(-font.CapHeight * .3, -font.CapHeight));
            return new(new CadGeometry(missing.Contours).Transform(shift).Contours, new(0, -font.CapHeight * 1.2),
                new(-font.CapHeight * .3, -font.CapHeight, font.CapHeight * .3, 0));
        }
        TextRunMetrics Measure(RichTextStyle style, string content)
        {
            if (measurements.TryGetValue((style, content), out var retained)) return retained;
            var font = Font(style); var factor = style.Height / font.CapHeight;
            var skew = Math.Tan(style.ObliqueDegrees * Math.PI / 180); var cursor = Vec3.Zero; var bounds = Bounds2.Empty;
            foreach (var rune in content.EnumerateRunes())
            {
                var glyph = Glyph(font, rune.Value); var box = glyph.Bounds;
                if (!box.IsEmpty)
                    foreach (var x in new[] { box.MinX, box.MaxX })
                        foreach (var y in new[] { box.MinY, box.MaxY })
                            bounds = bounds.Include(new(-(cursor.Y + y) * factor,
                                -((cursor.X + x) * style.WidthFactor + skew * (cursor.Y + y)) * factor));
                cursor += glyph.Advance;
            }
            var ascent = bounds.IsEmpty ? style.Height * .5 : Math.Max(0, -bounds.MinY);
            var descent = bounds.IsEmpty ? style.Height * .5 : Math.Max(0, bounds.MaxY);
            var result = new TextRunMetrics(-cursor.Y * factor, ascent, descent, bounds);
            measurements[(style, content)] = result; return result;
        }
    }
    private static string FlowAlignment(string alignment) => alignment switch
    {
        "TopLeft" => "BottomLeft", "TopCenter" => "MiddleLeft", "TopRight" => "TopLeft",
        "MiddleLeft" => "BottomCenter", "MiddleCenter" or "Middle" => "MiddleCenter", "MiddleRight" => "TopCenter",
        "BottomLeft" => "BottomRight", "BottomCenter" => "MiddleRight", "BottomRight" => "TopRight", _ => alignment
    };
}
