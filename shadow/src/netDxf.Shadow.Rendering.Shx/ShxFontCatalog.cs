using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering.Shx;

/// <summary>An immutable resource snapshot. Names are host aliases, never permission to read a path.</summary>
public sealed record ShxFontSet(long Revision, ImmutableDictionary<string, ShxFont> Fonts)
{
    public ShxFont? Resolve(string reference)
        => Fonts.TryGetValue(reference, out var font) || Fonts.TryGetValue(FileName(reference), out font) ? font : null;

    public static string FileName(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var index = Math.Max(reference.LastIndexOf('/'), reference.LastIndexOf('\\'));
        return reference[(index + 1)..];
    }
}

/// <summary>Bounded, atomically published host font resources. Serialize updates with scene compilation
/// for frame-wide resource consistency; previously acquired font sets remain immutable.</summary>
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
        ArgumentException.ThrowIfNullOrWhiteSpace(alias); ArgumentNullException.ThrowIfNull(font);
        if (alias.Length > 2048 || alias.Any(char.IsControl)) throw new ArgumentException("Invalid SHX font alias.", nameof(alias));
        lock (gate)
        {
            if (current.Fonts.TryGetValue(alias, out var previous) && ReferenceEquals(previous, font)) return current;
            if (!current.Fonts.ContainsKey(alias) && current.Fonts.Count >= maximumFonts) throw new InvalidOperationException("SHX font catalog budget exceeded.");
            var next = new ShxFontSet(checked(current.Revision + 1), current.Fonts.SetItem(alias, font));
            Volatile.Write(ref current, next); return next;
        }
    }
    public ShxFontSet Remove(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        lock (gate)
        {
            if (!current.Fonts.ContainsKey(alias)) return current;
            var next = new ShxFontSet(checked(current.Revision + 1), current.Fonts.Remove(alias));
            Volatile.Write(ref current, next); return next;
        }
    }
}
