using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

/// <summary>Shares the same explicit live/pinned font set and glyph cache with entity projection.</summary>
public sealed partial class ShxEntityProjector : ILinetypeDecorationProvider
{
    public bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic)
    {
        ArgumentNullException.ThrowIfNull(decoration);
        geometry = CadGeometry.Empty; diagnostic = null;
        if (decoration.Kind == CadLinetypeDecorationKind.Text && !decoration.Resource.EndsWith(".shx", StringComparison.OrdinalIgnoreCase)) return false;
        var fonts = readFonts();
        var font = fonts.Resolve(decoration.Resource) ?? throw new NotSupportedException($"SHX linetype resource '{decoration.Resource}' is not registered by the host.");
        if (decoration.Kind == CadLinetypeDecorationKind.Shape)
        {
            int number;
            if (decoration.ShapeNumber is { } authored) number = authored < 0 ? unchecked((ushort)(short)authored) : authored;
            else if (!font.Names.TryGetValue(decoration.Content, out number)) throw new NotSupportedException($"SHX linetype shape '{decoration.Content}' is absent from its registered resource.");
            if (!font.Symbols.ContainsKey(number)) throw new NotSupportedException($"SHX linetype shape number {number} is absent from its registered resource.");
            var budget = options.MaximumGlyphInstructions; var glyph = GetGlyph(font, number, ref budget);
            var skew = Math.Tan(decoration.ObliqueAngle * Math.PI / 180);
            var transform = new Affine3(new(decoration.WidthFactor * (decoration.IsBackward ? -1 : 1), 0),
                new Vec3(skew, 1) * (decoration.IsUpsideDown ? -1 : 1), Vec3.UnitZ, Vec3.Zero);
            geometry = new CadGeometry(glyph.Contours).Transform(transform); return true;
        }
        if (!font.IsFont || font.Encoding == ShxEncoding.PackedMultibyte)
            throw new NotSupportedException("The primary linetype font must provide Unicode/legacy metrics.");
        if (decoration.Content.Length > options.MaximumCharacters) throw new InvalidOperationException("SHX linetype text character budget exceeded.");
        var bigFont = BigFont(fonts, decoration.BigFont, decoration.IsVertical);
        var text = decoration.CreateText() with { FontFamily = decoration.Resource };
        geometry = decoration.IsVertical ? ProjectVerticalText(text, fonts, font, out diagnostic, bigFont)
            : ProjectText(text, fonts, font, out diagnostic, bigFont);
        return true;
    }
}
