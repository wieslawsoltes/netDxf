using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace netDxf.Shadow.Rendering.Shx;

public static partial class ShxFontReader
{
    /// <summary>Parses Autodesk SHP definitions (not GIS shapefiles), including regular/extended BIGFONT.</summary>
    public static ShxFont ReadAscii(string source, ShxReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source); options ??= new(); Validate(options, source.Length);
        var symbols = ImmutableDictionary.CreateBuilder<int, ShxSymbol>();
        var body = new StringBuilder(); string? header = null; string name = "";
        int above = 0, below = 0, mode = 0, total = 0, records = 0;
        var encoding = ShxEncoding.Shapes; var embedding = ShxEmbedding.Allowed; var fontDefinition = false;
        ShxBigFontInfo? big = null;
        using var lines = new StringReader(source);
        while (lines.ReadLine() is { } raw)
        {
            var comment = raw.IndexOf(';'); var line = (comment < 0 ? raw : raw[..comment]).Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("*BIGFONT", StringComparison.Ordinal))
            {
                if (header is not null || records != 0 || big is not null) throw new FormatException("BIGFONT must be the first definition.");
                var values = Tokens(line[8..]);
                if (values.Length < 2 || values[0] is < 0 or > 65535 || values[1] is < 0 or > 256 || values.Length != 2 + 2 * values[1])
                    throw new FormatException("BIGFONT needs a character-count hint, range count and complete lead-byte pairs.");
                var ranges = new List<ShxLeadByteRange>();
                for (var i = 2; i < values.Length; i += 2)
                {
                    if (values[i] is < 0 or > 255 || values[i + 1] is < 0 or > 255) throw new FormatException("BIGFONT lead bytes must be in 0..255.");
                    ranges.Add(new((byte)values[i], (byte)values[i + 1]));
                }
                big = new(values[0], ranges); continue;
            }
            if (line.StartsWith('*')) { Commit(); header = line; body.Clear(); }
            else if (header is null) throw new FormatException("SHP data precedes a record header.");
            else if (header.Count(c => c == ',') < 2) header += line;
            else { if (body.Length > 0) body.Append(','); body.Append(line); }
        }
        Commit();
        if (records == 0 || big is not null && !fontDefinition) throw new FormatException("SHP file has no records or BIGFONT metrics.");
        return new(name, above, below, mode, encoding, embedding, symbols.ToImmutable(), big);

        void Commit()
        {
            if (header is null) return;
            if (++records > options.MaximumSymbols + 1) throw new InvalidOperationException("SHP record budget exceeded.");
            var fields = header.Split(',', 3);
            if (fields.Length != 3) throw new FormatException("A SHP header needs number, byte count and name.");
            var spec = fields[0].Trim();
            if (!int.TryParse(fields[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var declared) || declared > options.MaximumBytes)
                throw new FormatException("Invalid SHP byte count.");
            var numbers = Tokens(body.ToString());
            var id = spec == "*UNIFONT" ? 0 : ParseInteger(spec.AsSpan(1));
            if (id < 0 || id > 65535) throw new FormatException("SHP symbol number is outside 0..65535.");
            if (id == 0)
            {
                if (fontDefinition || numbers.Length < 4 || numbers[^1] != 0) throw new FormatException("Invalid or duplicate SHP font definition.");
                fontDefinition = true; name = fields[2].Trim(); above = numbers[0]; below = numbers[1]; mode = numbers[2];
                encoding = ShxEncoding.Legacy;
                if (big is not null)
                {
                    if (spec == "*UNIFONT" || numbers.Length is not (4 or 5) || above is < 1 or > 255 || below is < 0 or > 255)
                        throw new FormatException("Invalid BIGFONT metric definition.");
                    var width = numbers.Length == 5 ? numbers[3] : 0;
                    if (numbers.Length == 5 && (below != 0 || width is < 1 or > 255)) throw new FormatException("Invalid extended BIGFONT dimensions.");
                    big = new(big.ApproximateCharacters, big.LeadByteRanges, width);
                    encoding = ShxEncoding.PackedMultibyte;
                }
                else if (spec == "*UNIFONT")
                {
                    if (numbers.Length != 6) throw new FormatException("A UNIFONT definition needs six metadata values.");
                    encoding = (ShxEncoding)numbers[3]; embedding = (ShxEmbedding)numbers[4];
                    if (encoding == ShxEncoding.Legacy) throw new FormatException("Invalid UNIFONT encoding.");
                }
            }
            else
            {
                if (big is not null && !fontDefinition) throw new FormatException("BIGFONT glyphs precede its metrics.");
                var instructions = DecodeLogical(numbers, options, ref total, big?.CharacterWidth ?? 0);
                if (symbols.Count >= options.MaximumSymbols) throw new InvalidOperationException("SHP symbol budget exceeded.");
                if (!symbols.TryAdd(id, new(id, fields[2].Trim(), declared, instructions))) throw new FormatException($"Duplicate SHP symbol {id}.");
            }
        }
    }

    private static int[] Tokens(string source)
    {
        var result = new List<int>(); var start = -1;
        for (var i = 0; i <= source.Length; i++)
        {
            if (i < source.Length && (char.IsAsciiHexDigit(source[i]) || source[i] is '-' or '+'))
            { if (start < 0) start = i; continue; }
            if (start >= 0) { result.Add(ParseInteger(source.AsSpan(start, i - start))); start = -1; }
            if (i < source.Length && source[i] is not (',' or '(' or ')') && !char.IsWhiteSpace(source[i]))
                throw new FormatException($"Invalid character in SHP code list: {source[i]}.");
        }
        return result.ToArray();
    }
    private static int ParseInteger(ReadOnlySpan<char> token)
    {
        if (token.IsEmpty) throw new FormatException("Empty SHP integer.");
        var negative = token[0] == '-'; var signed = token[0] is '-' or '+';
        var digits = signed ? token[1..] : token;
        if (digits.IsEmpty) throw new FormatException("Invalid SHP integer.");
        var style = digits[0] == '0' && digits.Length > 1 ? NumberStyles.AllowHexSpecifier : NumberStyles.None;
        if (!int.TryParse(digits, style, CultureInfo.InvariantCulture, out var value) || value < 0)
            throw new FormatException("SHP integer is outside the supported range.");
        return negative ? -value : value;
    }

    private static ImmutableArray<ShxInstruction> DecodeLogical(int[] values, ShxReadOptions options, ref int total, int extendedWidth = 0)
    {
        var result = ImmutableArray.CreateBuilder<ShxInstruction>(); var index = 0;
        while (true)
        {
            if (++total > options.MaximumInstructions) throw new InvalidOperationException("SHP instruction budget exceeded.");
            var code = Next(0, 255); var args = ImmutableArray.CreateBuilder<int>();
            switch (code)
            {
                case 0:
                    while (index < values.Length) if (values[index++] != 0) throw new FormatException("SHP codes follow a symbol terminator.");
                    return result.ToImmutable();
                case 1: case 2: case 5: case 6: case 14: break;
                case 3: case 4: args.Add(Next(1, 255)); break;
                case 7:
                    var number = Next(0, 65535); args.Add(number);
                    if (number == 0 && extendedWidth > 0)
                    {
                        args.Add(Next(1, 65535)); args.Add(Next(0, 255)); args.Add(Next(0, 255));
                        args.Add(Next(1, 255)); args.Add(Next(1, 255));
                    }
                    break;
                case 8: args.Add(Next(-128, 127)); args.Add(Next(-128, 127)); break;
                case 9: case 13:
                    while (true)
                    {
                        var x = Next(-128, 127); var y = Next(-128, 127);
                        if (x == 0 && y == 0) break;
                        if (++total > options.MaximumInstructions) throw new InvalidOperationException("SHP displacement budget exceeded.");
                        args.Add(x); args.Add(y); if (code == 13) args.Add(Next(-128, 127));
                    }
                    break;
                case 10: args.Add(Next(1, 255)); args.Add(Octant()); break;
                case 11: for (var i = 0; i < 4; i++) args.Add(Next(0, 255)); args.Add(Octant()); break;
                case 12: for (var i = 0; i < 3; i++) args.Add(Next(-128, 127)); break;
                case 15: throw new NotSupportedException("Reserved SHP opcode 15 is not a vector instruction.");
            }
            result.Add(new((byte)code, args.ToImmutable()));
        }
        int Next(int min, int max)
        {
            if (index >= values.Length) throw new FormatException("Unterminated SHP symbol or truncated instruction.");
            var value = values[index++];
            if (value < min || value > max) throw new FormatException($"SHP operand {value} is outside {min}..{max}.");
            return value;
        }
        int Octant() { var value = Next(-127, 255); return value < 0 ? 128 | -value : value; }
    }
}
