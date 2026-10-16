using System.Collections.Immutable;
using System.Text;

namespace netDxf.Shadow.Rendering.Shx;

public readonly record struct ShxLeadByteRange(byte First, byte Last);
public readonly record struct ShxEncodedCharacter(int ByteOffset, int ByteLength, int Number, bool BigFont);

/// <summary>BIGFONT declares packed character lead bytes, not a Unicode encoding.
/// CharacterWidth is positive only for the extended primitive-placement dialect.</summary>
public sealed class ShxBigFontInfo
{
    public int ApproximateCharacters { get; }
    public int CharacterWidth { get; }
    public ImmutableArray<ShxLeadByteRange> LeadByteRanges { get; }
    private readonly ImmutableArray<bool> leadBytes;
    public ShxBigFontInfo(int approximateCharacters, IEnumerable<ShxLeadByteRange> ranges, int characterWidth = 0)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        if (approximateCharacters is < 0 or > 65535 || characterWidth is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(approximateCharacters));
        var result = new List<ShxLeadByteRange>(); var table = new bool[256];
        foreach (var range in ranges)
        {
            if (result.Count >= 256 || range.First > range.Last) throw new FormatException("Invalid BIGFONT lead-byte range.");
            for (var i = (int)range.First; i <= range.Last; i++)
            { if (table[i]) throw new FormatException("Overlapping BIGFONT lead-byte ranges."); table[i] = true; }
            result.Add(range);
        }
        ApproximateCharacters = approximateCharacters; CharacterWidth = characterWidth;
        LeadByteRanges = result.OrderBy(r => r.First).ToImmutableArray(); leadBytes = table.ToImmutableArray();
    }
    public bool IsLeadByte(byte value) => leadBytes[value];

    /// <summary>One-pass packed-byte tokenization. A lead byte consumes exactly one following byte,
    /// even when that byte would normally be a delimiter. Unicode text uses a separately supplied map.</summary>
    public ImmutableArray<ShxEncodedCharacter> Decode(ReadOnlySpan<byte> bytes, int maximumCharacters = 100_000,
        CancellationToken cancellationToken = default)
    {
        if (maximumCharacters < 1) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        cancellationToken.ThrowIfCancellationRequested();
        var result = ImmutableArray.CreateBuilder<ShxEncodedCharacter>();
        for (var i = 0; i < bytes.Length;)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Count >= maximumCharacters) throw new InvalidOperationException("BIGFONT character budget exceeded.");
            var start = i; var first = bytes[i++];
            if (!IsLeadByte(first)) { result.Add(new(start, 1, first, false)); continue; }
            if (i == bytes.Length) throw new FormatException("Truncated BIGFONT double-byte character.");
            result.Add(new(start, 2, first << 8 | bytes[i++], true));
        }
        return result.ToImmutable();
    }
}

/// <summary>Explicit, immutable Unicode-to-packed-word mapping. No filename/code-page guessing or
/// process-global encoding registration is performed. Mapped missing glyphs remain diagnosable.</summary>
public sealed class ShxBigFontMap : IEquatable<ShxBigFontMap>
{
    public ImmutableDictionary<int, int> Characters { get; }
    public ShxBigFontMap(IEnumerable<KeyValuePair<int, int>> characters, int maximumCharacters = 65_536)
    {
        ArgumentNullException.ThrowIfNull(characters);
        if (maximumCharacters < 1 || maximumCharacters > 1_114_112) throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        var result = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var (unicode, packed) in characters)
        {
            if (!Rune.IsValid(unicode) || packed is < 1 or > 65535) throw new ArgumentException("Invalid BIGFONT character mapping.", nameof(characters));
            if (result.Count >= maximumCharacters) throw new InvalidOperationException("BIGFONT mapping budget exceeded.");
            if (!result.TryAdd(unicode, packed)) throw new ArgumentException("Duplicate BIGFONT Unicode mapping.", nameof(characters));
        }
        Characters = result.ToImmutable();
    }
    public bool Equals(ShxBigFontMap? other) => ReferenceEquals(this, other) || other is not null &&
        Characters.Count == other.Characters.Count && Characters.All(pair => other.Characters.TryGetValue(pair.Key, out var value) && value == pair.Value);
    public override bool Equals(object? obj) => obj is ShxBigFontMap other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var pair in Characters.OrderBy(p => p.Key)) { hash.Add(pair.Key); hash.Add(pair.Value); }
        return hash.ToHashCode();
    }

    /// <summary>Builds mappings only for declared double-byte glyphs using a host-selected encoding.
    /// Invalid byte sequences and non-roundtripping aliases are excluded; ambiguous Unicode aliases fail.</summary>
    public static ShxBigFontMap FromEncoding(ShxFont font, Encoding encoding, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(font); ArgumentNullException.ThrowIfNull(encoding);
        cancellationToken.ThrowIfCancellationRequested();
        var info = font.BigFont ?? throw new ArgumentException("Expected a BIGFONT resource.", nameof(font));
        var strict = (Encoding)encoding.Clone(); strict.DecoderFallback = DecoderFallback.ExceptionFallback; strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        var values = new List<KeyValuePair<int, int>>();
        foreach (var number in font.Symbols.Keys.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (number < 256 || !info.IsLeadByte((byte)(number >> 8))) continue;
            var bytes = new byte[] { (byte)(number >> 8), (byte)number };
            string text;
            try { text = strict.GetString(bytes); }
            catch (DecoderFallbackException) { continue; }
            var runes = text.EnumerateRunes().ToArray();
            if (runes.Length != 1) continue;
            try { if (!strict.GetBytes(text).AsSpan().SequenceEqual(bytes)) continue; }
            catch (EncoderFallbackException) { continue; }
            values.Add(new(runes[0].Value, number));
        }
        return new(values);
    }
}
