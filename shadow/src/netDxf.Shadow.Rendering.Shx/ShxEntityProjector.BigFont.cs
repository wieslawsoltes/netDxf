using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

public sealed partial class ShxEntityProjector
{
    private sealed class NormalizedBigGlyphs
    { internal readonly Dictionary<(int Number, int Height, bool Vertical), ShxGlyph> Values = new(); }
    private readonly ConditionalWeakTable<ShxFont, NormalizedBigGlyphs> bigGlyphs = new();

    private static ShxBigFontBinding? BigFont(ShxFontSet fonts, string reference, bool vertical)
    {
        if (reference.Length == 0) return null;
        var binding = fonts.ResolveBigFont(reference) ?? throw new NotSupportedException($"BIGFONT '{reference}' requires an explicitly registered font and Unicode mapping.");
        if (binding.Font.BigFont is null || !binding.Font.IsFont || vertical && !binding.Font.SupportsVerticalText)
            throw new NotSupportedException("The registered BIGFONT does not support the requested text orientation.");
        return binding;
    }

    /// <summary>Returns a mapped glyph in PRIMARY-font units so layout and painting share the same
    /// advance/ink coordinate system even when the companion font has different nominal dimensions.</summary>
    private ShxGlyph? MappedGlyph(ShxFont primary, ShxBigFontBinding? binding, int unicode, bool vertical,
        ref int instructions, Action<string> issue)
    {
        if (binding is null || !binding.Map.Characters.TryGetValue(unicode, out var number)) return null;
        var font = binding.Font;
        if (!font.Symbols.ContainsKey(number))
        {
            issue($"Mapped BIGFONT glyph {number} for U+{unicode:X4} is missing; an explicit box is drawn.");
            var missing = Missing(primary);
            if (!vertical) return missing;
            var shift = Affine3.Move(new(-primary.CapHeight * .3, -primary.CapHeight));
            return new(new CadGeometry(missing.Contours).Transform(shift).Contours, new(0, -primary.CapHeight * 1.2),
                new(-primary.CapHeight * .3, -primary.CapHeight, primary.CapHeight * .3, 0));
        }
        var cache = bigGlyphs.GetValue(font, _ => new());
        lock (cache)
        {
            var key = (number, primary.CapHeight, vertical);
            if (cache.Values.TryGetValue(key, out var retained)) return retained;
            var glyph = GetGlyph(font, number, ref instructions, vertical);
            if (Math.Abs(glyph.ExitScale - 1) > 1e-12) throw new NotSupportedException("BIGFONT text glyphs must restore their vector scale.");
            if (vertical && (glyph.Advance.Y > 0 || Math.Abs(glyph.Advance.X) > 1e-10))
                throw new NotSupportedException("Vertical BIGFONT glyphs must advance downwards and return to the column baseline.");
            var factor = (double)primary.CapHeight / font.CapHeight;
            var matrix = Affine3.Scale(new(factor, factor, 1));
            var box = glyph.Bounds;
            var normalized = factor == 1 ? glyph : glyph with
            {
                Contours = new CadGeometry(glyph.Contours).Transform(matrix).Contours,
                Advance = glyph.Advance * factor,
                Bounds = box.IsEmpty ? box : new(box.MinX * factor, box.MinY * factor, box.MaxX * factor, box.MaxY * factor)
            };
            if (cache.Values.Count >= options.MaximumCachedGlyphsPerFont) cache.Values.Clear();
            cache.Values[key] = normalized; return normalized;
        }
    }
}
