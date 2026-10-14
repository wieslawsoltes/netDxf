using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering.Shx;

public sealed record ShxBigFontBinding(ShxFont Font, ShxBigFontMap Map);

/// <summary>An immutable resource snapshot. Names are host aliases, never permission to read a path.</summary>
public sealed record ShxFontSet(long Revision, ImmutableDictionary<string, ShxFont> Fonts)
{
    public ImmutableDictionary<string, ShxBigFontMap> BigFontMaps { get; init; }
        = ImmutableDictionary.Create<string, ShxBigFontMap>(StringComparer.OrdinalIgnoreCase);
    public ShxFont? Resolve(string reference)
        => Fonts.TryGetValue(reference, out var font) || Fonts.TryGetValue(FileName(reference), out font) ? font : null;
    /// <summary>Resolve font and mapping through the same alias, never combine an exact-path font
    /// with a different basename mapping. Incomplete bindings are explicit failures at the consumer.</summary>
    public ShxBigFontBinding? ResolveBigFont(string reference)
    {
        var alias = Fonts.ContainsKey(reference) ? reference : FileName(reference);
        return Fonts.TryGetValue(alias, out var font) && BigFontMaps.TryGetValue(alias, out var map) ? new(font, map) : null;
    }
    public static string FileName(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var index = Math.Max(reference.LastIndexOf('/'), reference.LastIndexOf('\\'));
        return reference[(index + 1)..];
    }
}

/// <summary>Bounded, atomically published host resources. Font and BIGFONT mapping replacements are
/// one revision. Previously acquired sets retain both the old font and old character interpretation.</summary>
public sealed class ShxFontCatalog
{
    private readonly object gate = new();
    private readonly int maximumFonts;
    private ShxFontSet current = new(0, ImmutableDictionary.Create<string, ShxFont>(StringComparer.OrdinalIgnoreCase));
    public ShxFontCatalog(int maximumFonts = 64)
    {
        if (maximumFonts < 1) throw new ArgumentOutOfRangeException(nameof(maximumFonts));
        this.maximumFonts = maximumFonts;
    }
    public ShxFontSet Current => Volatile.Read(ref current);
    public ShxFontSet Register(string alias, ShxFont font)
    {
        ValidateAlias(alias); ArgumentNullException.ThrowIfNull(font);
        lock (gate)
        {
            if (current.Fonts.TryGetValue(alias, out var previous) && ReferenceEquals(previous, font)) return current;
            CheckCapacity(alias);
            var next = current with { Revision = checked(current.Revision + 1), Fonts = current.Fonts.SetItem(alias, font),
                BigFontMaps = font.BigFont is null ? current.BigFontMaps.Remove(alias) : current.BigFontMaps };
            Volatile.Write(ref current, next); return next;
        }
    }
    public ShxFontSet RegisterBigFont(string alias, ShxFont font, ShxBigFontMap map)
    {
        ValidateAlias(alias); ArgumentNullException.ThrowIfNull(font); ArgumentNullException.ThrowIfNull(map);
        if (font.BigFont is null) throw new ArgumentException("Expected a decoded BIGFONT resource.", nameof(font));
        lock (gate)
        {
            if (current.Fonts.TryGetValue(alias, out var previous) && ReferenceEquals(previous, font) &&
                current.BigFontMaps.TryGetValue(alias, out var previousMap) && ReferenceEquals(previousMap, map)) return current;
            CheckCapacity(alias);
            var next = current with { Revision = checked(current.Revision + 1), Fonts = current.Fonts.SetItem(alias, font),
                BigFontMaps = current.BigFontMaps.SetItem(alias, map) };
            Volatile.Write(ref current, next); return next;
        }
    }
    public ShxFontSet Remove(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        lock (gate)
        {
            if (!current.Fonts.ContainsKey(alias)) return current;
            var next = current with { Revision = checked(current.Revision + 1), Fonts = current.Fonts.Remove(alias), BigFontMaps = current.BigFontMaps.Remove(alias) };
            Volatile.Write(ref current, next); return next;
        }
    }
    private void CheckCapacity(string alias)
    {
        if (!current.Fonts.ContainsKey(alias) && current.Fonts.Count >= maximumFonts) throw new InvalidOperationException("SHX font catalog budget exceeded.");
    }
    private static void ValidateAlias(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        if (alias.Length > 2048 || alias.Any(char.IsControl)) throw new ArgumentException("Invalid SHX font alias.", nameof(alias));
    }
}
