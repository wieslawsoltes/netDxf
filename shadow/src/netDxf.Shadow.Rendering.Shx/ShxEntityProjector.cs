using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using netDxf.Shadow.Geometry;

namespace netDxf.Shadow.Rendering.Shx;

public sealed record ShxProjectionOptions(int MaximumCharacters = 100_000, int MaximumCurves = 500_000,
    int MaximumCachedGlyphsPerFont = 4096, int MaximumGlyphInstructions = 2_000_000);

/// <summary>Projects explicit host-provided SHX resources to neutral geometry. Text strokes are
/// presentation-only and do not become graph edges. No filenames are resolved outside the catalog.</summary>
public sealed partial class ShxEntityProjector : ICacheableEntityProjector
{
    private sealed class GlyphCache { internal readonly Dictionary<(int Number, bool Vertical), ShxGlyph> Values = new(); }
    private readonly Func<ShxFontSet> readFonts;
    private readonly ShxProjectionOptions options;
    private readonly ConditionalWeakTable<ShxFont, GlyphCache> glyphs = new();
    public long Revision => readFonts().Revision;

    public ShxEntityProjector(ShxFontCatalog catalog, ShxProjectionOptions? options = null) : this(Live(catalog), options) { }
    /// <summary>Pins external resources for immutable retained views and deterministic exports.</summary>
    public ShxEntityProjector(ShxFontSet fonts, ShxProjectionOptions? options = null) : this(Frozen(fonts), options) { }
    private ShxEntityProjector(Func<ShxFontSet> readFonts, ShxProjectionOptions? options)
    {
        this.readFonts = readFonts; this.options = options ?? new();
        if (this.options.MaximumCharacters < 1 || this.options.MaximumCurves < 1 ||
            this.options.MaximumCachedGlyphsPerFont < 1 || this.options.MaximumGlyphInstructions < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
    }
    private static Func<ShxFontSet> Live(ShxFontCatalog catalog)
    { ArgumentNullException.ThrowIfNull(catalog); return () => catalog.Current; }
    private static Func<ShxFontSet> Frozen(ShxFontSet fonts)
    { ArgumentNullException.ThrowIfNull(fonts); return () => fonts; }

    public bool TryProject(ShadowReader entity, out CadGeometry geometry, out string? diagnostic)
    {
        geometry = CadGeometry.Empty; diagnostic = null;
        var fonts = readFonts();
        if (entity.Type == "Shape")
        {
            var reference = entity["Style"]["File"].Text;
            var font = fonts.Resolve(reference) ?? throw new NotSupportedException($"SHX shape resource '{reference}' is not registered by the host.");
            if (!font.Names.TryGetValue(entity["Name"].Text, out var number)) throw new NotSupportedException("The named shape is absent from its registered resource.");
            var budget = options.MaximumGlyphInstructions; var glyph = GetGlyph(font, number, ref budget); var size = entity["Size"].Number(1); var width = entity["WidthFactor"].Number(1);
            var skew = Math.Tan(entity["ObliqueAngle"].Number() * Math.PI / 180);
            var basis = Affine3.ObjectCoordinates(entity["Normal"].Vector(Vec3.UnitZ));
            var matrix = Affine3.Move(entity["Position"].Vector()) * basis * Affine3.RotateZ(entity["Rotation"].Number() * Math.PI / 180) *
                new Affine3(new(size * width, 0), new(skew * size, size), Vec3.UnitZ, Vec3.Zero);
            if (!(size > 0 && width > 0) || !double.IsFinite(size + width + skew)) throw new ArgumentException("Invalid SHX shape dimensions.");
            geometry = new(glyph.Contours); geometry = geometry.Transform(matrix); return true;
        }
        if (entity.Type is not ("Text" or "MText" or "Attribute" or "AttributeDefinition")) return false;
        var source = CadTextProjection.Read(entity);
        var file = source.Style["FontFile"].Text;
        var resource = fonts.Resolve(file);
        if (resource is null) return false;
        if (!resource.IsFont || resource.Encoding == ShxEncoding.PackedMultibyte)
            throw new NotSupportedException("The primary SHX resource must provide Unicode/legacy metrics; BIGFONT is a companion resource.");
        var vertical = source.DrawingDirection == 3 || source.DrawingDirection == 5 && source.Style["IsVertical"].Boolean();
        var bigFont = BigFont(fonts, source.Style["BigFont"].Text, vertical);
        var text = source.Text;
        if (text.Content.Length > options.MaximumCharacters) throw new InvalidOperationException("SHX text character budget exceeded.");
        geometry = vertical ? ProjectVerticalText(text with { FontFamily = file }, fonts, resource, out diagnostic, bigFont)
            : ProjectText(text with { FontFamily = file }, fonts, resource, out diagnostic, bigFont);
        if (source.HasBackground) diagnostic = string.Join(" ", new[] { diagnostic, "The stored text background is not rendered by this projector." }.Where(s => s is not null));
        return true;
    }

    private ShxGlyph GetGlyph(ShxFont font, int number, ref int remainingWork, bool vertical = false)
    {
        var cache = glyphs.GetValue(font, _ => new());
        lock (cache)
        {
            if (cache.Values.TryGetValue((number, vertical), out var glyph)) return glyph;
            if (remainingWork < 1) throw new InvalidOperationException("SHX glyph compilation work budget exceeded.");
            glyph = ShxGlyphCompiler.Compile(font, number, vertical, options: new(MaximumInstructions: Math.Min(1_000_000, remainingWork), MaximumCurves: options.MaximumCurves));
            remainingWork -= glyph.ExecutedInstructions;
            if (cache.Values.Count >= options.MaximumCachedGlyphsPerFont) cache.Values.Clear();
            cache.Values[(number, vertical)] = glyph; return glyph;
        }
    }

    private static ShxGlyph Missing(ShxFont font)
    {
        var width = font.CapHeight * 0.6; var height = font.CapHeight;
        var points = new Vec3[] { new(0, 0), new(width, 0), new(width, height), new(0, height) };
        return new([new(points.Select((p, i) => (Curve3)new LineCurve(p, points[(i + 1) % 4])).ToImmutableArray(), true)],
            new(font.CapHeight * 0.8, 0), new(0, 0, width, height));
    }
}
