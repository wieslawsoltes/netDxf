using System.Collections.Immutable;
using System.Text;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

public sealed partial class ShxEntityProjector
{
    private CadGeometry ProjectText(CadText text, ShxFontSet fonts, ShxFont fallback, out string? diagnostic, ShxBigFontBinding? bigFont = null)
    {
        var issues = new SortedSet<string>(StringComparer.Ordinal);
        var work = 0L; var curveCount = 0L; var instructionBudget = options.MaximumGlyphInstructions;
        var metrics = new Dictionary<(RichTextStyle, string), TextRunMetrics>();
        var layout = RichTextLayout.Layout(text, Measure, options.MaximumCharacters);
        foreach (var issue in layout.Diagnostics.Take(20)) Issue(issue.Message);
        var strokes = ImmutableArray.CreateBuilder<CadStrokeGroup>();
        var regions = ImmutableArray.CreateBuilder<CadRegion>();
        var frame = new Affine3(text.XAxis, -text.YAxis, Vec3.Cross(text.XAxis, text.YAxis), text.Origin);
        foreach (var run in layout.Runs)
        {
            var font = Font(run.Style); var factor = run.Style.Height / font.CapHeight;
            var skew = Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180); var cursor = 0.0;
            var contours = ImmutableArray.CreateBuilder<CadContour>();
            foreach (var rune in run.Text.EnumerateRunes())
            {
                var glyph = Glyph(font, rune.Value);
                var matrix = frame * new Affine3(new(factor * run.Style.WidthFactor, 0), new(skew * factor, -factor), Vec3.UnitZ,
                    new(run.X + cursor * factor * run.Style.WidthFactor, run.Y));
                foreach (var contour in glyph.Contours)
                {
                    curveCount += contour.Curves.Length;
                    if (curveCount > options.MaximumCurves) throw new InvalidOperationException("SHX text curve budget exceeded.");
                    contours.Add(contour with { Curves = contour.Curves.Select(c => c.Transform(matrix)).ToImmutableArray() });
                }
                cursor += glyph.Advance.X;
            }
            if (contours.Count > 0) strokes.Add(new(contours.ToImmutable(), run.Style.Color));
        }
        foreach (var rule in layout.Rules)
        {
            var a = new Vec3(rule.X1, rule.Y1); var b = new Vec3(rule.X2, rule.Y2); var d = b - a;
            if (d.Length == 0) continue;
            var n = new Vec3(-d.Y, d.X) * (rule.Thickness / (2 * d.Length));
            var points = new[] { frame.Point(a + n), frame.Point(b + n), frame.Point(b - n), frame.Point(a - n) };
            if ((curveCount += 4) > options.MaximumCurves) throw new InvalidOperationException("SHX decoration budget exceeded.");
            regions.Add(new([new(points.Select((p, i) => (Curve3)new LineCurve(p, points[(i + 1) % 4])).ToImmutableArray(), true)]) { Argb = rule.Color });
        }
        diagnostic = issues.Count > 0 ? string.Join(" ", issues) : null;
        return new([]) { Strokes = strokes.ToImmutable(), Regions = regions.ToImmutable() };

        void Issue(string message) { if (issues.Count < 20) issues.Add(message); }
        ShxFont Font(RichTextStyle style)
        {
            var requested = fonts.Resolve(style.FontFamily);
            if (requested is null)
            {
                Issue($"SHX inline font '{style.FontFamily}' is not registered; the entity font is used."); requested = fallback;
            }
            if (style.Bold || style.Italic)
            {
                var variant = fonts.Resolve(style.FontFamily + (style.Bold ? "|bold" : "") + (style.Italic ? "|italic" : ""));
                if (variant is not null) requested = variant;
                else Issue("No authored SHX bold/italic variant is registered; original strokes are retained.");
            }
            if (!requested.IsFont || requested.Encoding == ShxEncoding.PackedMultibyte)
                throw new NotSupportedException("SHX inline resource has unsupported character mapping or metrics.");
            return requested;
        }
        ShxGlyph Glyph(ShxFont font, int number)
        {
            if (++work > options.MaximumCharacters * 8L) throw new InvalidOperationException("SHX text measurement work budget exceeded.");
            if (MappedGlyph(font, bigFont, number, false, ref instructionBudget, Issue) is { } mapped) return mapped;
            var authored = number;
            if (font.Encoding == ShxEncoding.Legacy)
                number = number switch { 0xb0 => 256, 0xb1 => 257, 0x2205 => 258, _ => number };
            if (number == 0xa0 && !font.Symbols.ContainsKey(number) && font.Symbols.ContainsKey(32)) number = 32;
            if (font.Symbols.ContainsKey(number))
            {
                var glyph = GetGlyph(font, number, ref instructionBudget);
                if (Math.Abs(glyph.ExitScale - 1) > 1e-12)
                    throw new NotSupportedException("SHX text glyphs must restore their vector scale; cumulative cross-glyph scaling needs a stateful layout provider.");
                return glyph;
            }
            if (number is 32 or 0xa0) return new([], new(font.CapHeight * .5, 0), Bounds2.Empty);
            Issue($"Missing SHX glyph U+{authored:X4}; an explicit missing-glyph box is drawn."); return Missing(font);
        }
        TextRunMetrics Measure(RichTextStyle style, string content)
        {
            if (metrics.TryGetValue((style, content), out var cached)) return cached;
            var font = Font(style); var factor = style.Height / font.CapHeight; var cursor = 0.0; var bounds = Bounds2.Empty;
            foreach (var rune in content.EnumerateRunes())
            {
                var glyph = Glyph(font, rune.Value); var box = glyph.Bounds;
                if (!box.IsEmpty)
                {
                    bounds = bounds.Include(new((cursor + box.MinX) * factor, -box.MaxY * factor));
                    bounds = bounds.Include(new((cursor + box.MaxX) * factor, -box.MinY * factor));
                }
                cursor += glyph.Advance.X;
            }
            var value = new TextRunMetrics(cursor * factor, style.Height, font.Descender * factor, bounds);
            metrics[(style, content)] = value; return value;
        }
    }
}
