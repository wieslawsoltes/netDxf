using System.Globalization;
using System.Text;
using System.Text.Json;

namespace netDxf.Shadow.Rendering.Shx;

/// <summary>Portable, explicit Unicode interpretation for a host-registered BIGFONT alias.
/// This is mapping metadata, not a font or permission to resolve a file path.</summary>
public sealed record ShxBigFontMapFile(string Resource, ShxBigFontMap Map)
{
    public static ShxBigFontMapFile Read(ReadOnlyMemory<byte> utf8, int maximumBytes = 4 * 1024 * 1024,
        int maximumCharacters = 65_536, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBytes < 1 || maximumCharacters is < 1 or > 1_114_112) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if (utf8.Length > maximumBytes) throw new InvalidOperationException("BIGFONT map byte budget exceeded.");
        try
        {
            using var document = JsonDocument.Parse(utf8, new JsonDocumentOptions { MaxDepth = 4 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("A BIGFONT map must be a JSON object.");
            var fields = new HashSet<string>(StringComparer.Ordinal);
            string? resource = null; JsonElement characters = default; var version = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!fields.Add(property.Name)) throw new FormatException("Duplicate BIGFONT map field.");
                switch (property.Name)
                {
                    case "schemaVersion":
                        if (property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out version) || version != 1) throw new FormatException("Unsupported BIGFONT map schema."); break;
                    case "resource":
                        if (property.Value.ValueKind != JsonValueKind.String) throw new FormatException("Expected a resource alias.");
                        resource = property.Value.GetString(); break;
                    case "characters": characters = property.Value; break;
                    default: throw new FormatException("Unknown BIGFONT map field: " + property.Name);
                }
            }
            if (version != 1 || characters.ValueKind != JsonValueKind.Object || string.IsNullOrWhiteSpace(resource) ||
                resource.Length > 2048 || resource.Any(char.IsControl)) throw new FormatException("Incomplete or invalid BIGFONT map.");
            var values = new List<KeyValuePair<int, int>>();
            foreach (var property in characters.EnumerateObject())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (values.Count >= maximumCharacters) throw new InvalidOperationException("BIGFONT map character budget exceeded.");
                var name = property.Name;
                if (name.Length is < 3 or > 8 || !name.StartsWith("U+", StringComparison.Ordinal) ||
                    !int.TryParse(name.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var unicode) ||
                    !Rune.IsValid(unicode) || property.Value.ValueKind != JsonValueKind.Number || !property.Value.TryGetInt32(out var packed) ||
                    packed is < 1 or > 65535) throw new FormatException("Expected U+HEX scalar keys and integer packed glyph words in 1..65535.");
                values.Add(new(unicode, packed));
            }
            return new(resource, new ShxBigFontMap(values, maximumCharacters));
        }
        catch (JsonException error) { throw new FormatException("Malformed BIGFONT mapping JSON.", error); }
    }

    /// <summary>Canonical UTF-8 output with deterministic Unicode-key order.</summary>
    public byte[] Write()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource); ArgumentNullException.ThrowIfNull(Map);
        if (Resource.Length > 2048 || Resource.Any(char.IsControl)) throw new ArgumentException("Invalid resource alias.");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject(); writer.WriteNumber("schemaVersion", 1); writer.WriteString("resource", Resource);
            writer.WriteStartObject("characters");
            foreach (var pair in Map.Characters.OrderBy(p => p.Key)) writer.WriteNumber("U+" + pair.Key.ToString("X4", CultureInfo.InvariantCulture), pair.Value);
            writer.WriteEndObject(); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
