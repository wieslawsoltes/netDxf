using System.Globalization;
using System.Text;
using System.Text.Json;

namespace netDxf.Shadow.Rendering.Shx;

public sealed record ShxBigFontMapReadOptions(int MaximumBytes = 4 * 1024 * 1024, int MaximumCharacters = 65_536)
{
    internal void Validate()
    {
        if (MaximumBytes < 1 || MaximumCharacters is < 1 or > 1_114_112)
            throw new ArgumentOutOfRangeException(nameof(ShxBigFontMapReadOptions));
    }
}

/// <summary>Explicit character interpretation for one host-registered resource. This object contains
/// no font bytes, code-page inference or permission to open a path.</summary>
public sealed record ShxBigFontMapFile(string Resource, ShxBigFontMap Map)
{
    /// <summary>Read bounded UTF-8 JSON. Fields are case-sensitive; duplicate fields, duplicate Unicode
    /// aliases, unknown fields and trailing values are errors. A leading UTF-8 BOM is accepted.</summary>
    public static ShxBigFontMapFile Read(ReadOnlySpan<byte> utf8, ShxBigFontMapReadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new(); options.Validate(); cancellationToken.ThrowIfCancellationRequested();
        if (utf8.Length > options.MaximumBytes) throw new InvalidOperationException("BIGFONT map byte budget exceeded.");
        if (utf8.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) utf8 = utf8[3..];
        try
        {
            // Do not accept replacement-decoded resource aliases or JSON property names.
            _ = new UTF8Encoding(false, true).GetCharCount(utf8);
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 3 });
            Expect(ref reader, JsonTokenType.StartObject);
            string? resource = null; ShxBigFontMap? map = null; var seen = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.TokenType != JsonTokenType.PropertyName) throw new FormatException("Expected a BIGFONT map field.");
                var field = reader.ValueTextEquals("schemaVersion"u8) ? 1 : reader.ValueTextEquals("resource"u8) ? 2 :
                    reader.ValueTextEquals("characters"u8) ? 4 : 0;
                if (field == 0 || (seen & field) != 0) throw new FormatException("Unknown or duplicate BIGFONT map field.");
                seen |= field;
                if (field == 1)
                {
                    Expect(ref reader, JsonTokenType.Number);
                    if (!reader.TryGetInt32(out var version) || version != 1) throw new FormatException("Unsupported BIGFONT map schema.");
                }
                else if (field == 2)
                {
                    Expect(ref reader, JsonTokenType.String);
                    resource = reader.GetString()!; ValidateResource(resource);
                }
                else
                {
                    Expect(ref reader, JsonTokenType.StartObject);
                    var characters = new Dictionary<int, int>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.TokenType != JsonTokenType.PropertyName) throw new FormatException("Expected a Unicode mapping key.");
                        if (characters.Count >= options.MaximumCharacters) throw new InvalidOperationException("BIGFONT mapping budget exceeded.");
                        var key = reader.GetString()!;
                        if (key.Length is < 6 or > 8 || !key.StartsWith("U+", StringComparison.Ordinal) ||
                            !int.TryParse(key.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unicode) || !Rune.IsValid(unicode))
                            throw new FormatException("Use U+ followed by four to six hexadecimal digits naming one Unicode scalar.");
                        Expect(ref reader, JsonTokenType.Number);
                        if (!reader.TryGetInt32(out var packed) || packed is < 1 or > 65535)
                            throw new FormatException("BIGFONT glyph identifiers must be integral words in 1..65535.");
                        if (!characters.TryAdd(unicode, packed)) throw new FormatException("Duplicate BIGFONT Unicode mapping, including alternate key spellings.");
                    }
                    if (reader.TokenType != JsonTokenType.EndObject) throw new FormatException("Truncated BIGFONT character table.");
                    map = new(characters, options.MaximumCharacters);
                }
            }
            if (reader.TokenType != JsonTokenType.EndObject || seen != 7 || reader.Read())
                throw new FormatException("Incomplete BIGFONT map or trailing JSON value.");
            return new(resource!, map!);
        }
        catch (JsonException error) { throw new FormatException("Malformed BIGFONT mapping JSON.", error); }
        catch (DecoderFallbackException error) { throw new FormatException("BIGFONT maps must be valid UTF-8.", error); }
    }

    /// <summary>Canonical field order and ascending Unicode keys. Equivalent mappings serialize identically.</summary>
    public byte[] Write(CancellationToken cancellationToken = default)
    {
        ValidateResource(Resource); ArgumentNullException.ThrowIfNull(Map); cancellationToken.ThrowIfCancellationRequested();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("resource", Resource);
            writer.WriteStartObject("characters");
            foreach (var pair in Map.Characters.OrderBy(p => p.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteNumber("U+" + pair.Key.ToString("X4", CultureInfo.InvariantCulture), pair.Value);
            }
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static bool Equivalent(ShxBigFontMap left, ShxBigFontMap right)
    {
        ArgumentNullException.ThrowIfNull(left); ArgumentNullException.ThrowIfNull(right);
        return ReferenceEquals(left, right) || left.Characters.Count == right.Characters.Count &&
            left.Characters.All(pair => right.Characters.TryGetValue(pair.Key, out var value) && value == pair.Value);
    }

    internal static void ValidateResource(string resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (resource.Length > 2048 || resource.Any(c => char.IsControl(c) || c is '/' or '\\' or ':') ||
            !resource.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) || resource.Length == 4)
            throw new ArgumentException("The BIGFONT map must name a registered .shx basename, not a path.", nameof(resource));
        _ = new UTF8Encoding(false, true).GetByteCount(resource);
    }

    private static void Expect(ref Utf8JsonReader reader, JsonTokenType token)
    {
        if (!reader.Read() || reader.TokenType != token) throw new FormatException($"Expected {token} in BIGFONT mapping JSON.");
    }
}
