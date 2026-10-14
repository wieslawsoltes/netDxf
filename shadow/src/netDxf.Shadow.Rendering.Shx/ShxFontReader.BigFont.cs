using System.Buffers.Binary;
using System.Collections.Immutable;

namespace netDxf.Shadow.Rendering.Shx;

public static partial class ShxFontReader
{
    private readonly record struct BigEntry(int Number, int Length, int Offset);

    // Indexed sparse BIGFONT 1.0 layout cross-check: OpenCADStudio src/scene/text/shx.rs,
    // revision 843cbcd6. This independent parser rejects malformed/truncated records and does
    // not use filename-based encoding guesses, silent recovery or external font files.
    private static ShxFont ReadBigFont(ReadOnlySpan<byte> data, ShxReadOptions options)
    {
        var cursor = new ByteCursor(data[24..]);
        if (cursor.Byte() != 0x1a) throw new FormatException("Missing BIGFONT signature terminator.");
        var entrySize = cursor.Word(); var slots = cursor.Word(); var rangeCount = cursor.Word();
        if (entrySize != 8) throw new NotSupportedException("Only the eight-byte BIGFONT 1.0 directory entry is supported.");
        // Slot count includes unused hash buckets; MaximumSymbols bounds actual glyphs, not empty buckets.
        if (slots < 1 || rangeCount > 256) throw new FormatException("Invalid BIGFONT directory cardinality.");
        var ranges = ImmutableArray.CreateBuilder<ShxLeadByteRange>(rangeCount);
        for (var i = 0; i < rangeCount; i++)
        {
            var first = cursor.Word(); var last = cursor.Word();
            if (first > 255 || last > 255) throw new FormatException("BIGFONT lead bytes must be in 0..255.");
            ranges.Add(new((byte)first, (byte)last));
        }
        var info = new ShxBigFontInfo(slots, ranges);
        var directory = cursor.Take(checked(slots * 8));
        var payloadStart = data.Length - cursor.Remaining;
        var entries = new List<BigEntry>(); var ids = new HashSet<int>();
        for (var i = 0; i < slots; i++)
        {
            var entry = directory.Slice(i * 8, 8);
            var id = BinaryPrimitives.ReadUInt16LittleEndian(entry);
            var length = BinaryPrimitives.ReadUInt16LittleEndian(entry[2..]);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[4..]);
            if (id == 0 && length == 0 && offset == 0) continue;
            if (length == 0 || offset < payloadStart || offset > data.Length || length > (long)data.Length - offset)
                throw new FormatException("BIGFONT record points outside its payload.");
            if (!ids.Add(id)) throw new FormatException("Duplicate BIGFONT shape number.");
            entries.Add(new(id, length, (int)offset));
            if (entries.Count > options.MaximumSymbols + 1) throw new InvalidOperationException("BIGFONT glyph budget exceeded.");
        }
        if (!ids.Contains(0)) throw new FormatException("Missing BIGFONT metrics.");
        var previousEnd = payloadStart;
        foreach (var entry in entries.OrderBy(e => e.Offset))
        {
            if (entry.Offset < previousEnd) throw new FormatException("Overlapping BIGFONT records.");
            previousEnd = checked(entry.Offset + entry.Length);
        }
        var metrics = entries.Single(e => e.Number == 0);
        var definition = new ByteCursor(data.Slice(metrics.Offset, metrics.Length));
        var name = definition.Name(); var meta = definition.Take(definition.Remaining);
        if (meta.Length is not (4 or 5) || meta[^1] != 0 || meta[0] == 0 || meta[2] is not (0 or 2))
            throw new FormatException("Invalid BIGFONT metrics.");
        var width = meta.Length == 5 ? meta[3] : 0;
        if (meta.Length == 5 && (meta[1] != 0 || width == 0)) throw new FormatException("Invalid extended BIGFONT metrics.");
        info = new(slots, ranges, width);
        var symbols = ImmutableDictionary.CreateBuilder<int, ShxSymbol>(); var work = 0;
        foreach (var entry in entries)
        {
            if (entry.Number == 0) continue;
            if (symbols.Count >= options.MaximumSymbols) throw new InvalidOperationException("BIGFONT glyph budget exceeded.");
            var record = new ByteCursor(data.Slice(entry.Offset, entry.Length));
            var symbolName = record.Name(); var size = record.Remaining;
            symbols.Add(entry.Number, new(entry.Number, symbolName, size, DecodeBigInstructions(ref record, info, options, ref work)));
        }
        return new(name, meta[0], meta[1], meta[2], ShxEncoding.PackedMultibyte, ShxEmbedding.Allowed, symbols.ToImmutable(), info);
    }

    private static ImmutableArray<ShxInstruction> DecodeBigInstructions(ref ByteCursor reader, ShxBigFontInfo info,
        ShxReadOptions options, ref int total)
    {
        var result = ImmutableArray.CreateBuilder<ShxInstruction>();
        while (true)
        {
            if (++total > options.MaximumInstructions) throw new InvalidOperationException("BIGFONT instruction budget exceeded.");
            var code = reader.Byte(); var args = ImmutableArray.CreateBuilder<int>();
            switch (code)
            {
                case 0:
                    while (reader.Remaining > 0) if (reader.Byte() != 0) throw new FormatException("Data follows BIGFONT glyph terminator.");
                    return result.ToImmutable();
                case 1: case 2: case 5: case 6: case 14: break;
                case 3: case 4:
                    var factor = reader.Byte(); if (factor == 0) throw new FormatException("Zero BIGFONT vector factor."); args.Add(factor); break;
                case 7:
                    int number = reader.Byte();
                    if (number != 0) { args.Add(number); break; }
                    number = reader.Byte() << 8 | reader.Byte();
                    if (number == 0) throw new FormatException("BIGFONT subshape zero is metadata, not executable geometry.");
                    if (info.CharacterWidth == 0) args.Add(number);
                    else
                    {
                        args.Add(0); args.Add(number); args.Add(reader.Byte()); args.Add(reader.Byte());
                        var width = reader.Byte(); var height = reader.Byte();
                        if (width == 0 || height == 0) throw new FormatException("Extended BIGFONT primitive dimensions must be positive.");
                        args.Add(width); args.Add(height);
                    }
                    break;
                case 8: args.Add(reader.Signed()); args.Add(reader.Signed()); break;
                case 9: case 13:
                    while (true)
                    {
                        var x = reader.Signed(); var y = reader.Signed(); if (x == 0 && y == 0) break;
                        if (++total > options.MaximumInstructions) throw new InvalidOperationException("BIGFONT displacement budget exceeded.");
                        args.Add(x); args.Add(y); if (code == 13) args.Add(reader.Signed());
                    }
                    break;
                case 10: args.Add(reader.Byte()); args.Add(reader.Byte()); break;
                case 11: for (var i = 0; i < 5; i++) args.Add(reader.Byte()); break;
                case 12: for (var i = 0; i < 3; i++) args.Add(reader.Signed()); break;
                case 15: throw new NotSupportedException("Reserved BIGFONT opcode 15.");
            }
            result.Add(new(code, args.ToImmutable()));
        }
    }
}
