// Binary record layouts were cross-checked against ezdxf's MIT-licensed shapefile.py.
// See ThirdPartyNotices.md. No font data is distributed by this project.
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace netDxf.Shadow.Rendering.Shx;

public static partial class ShxFontReader
{
    public static ShxFont ReadBinary(ReadOnlySpan<byte> data, ShxReadOptions? options = null)
    {
        options ??= new(); Validate(options, data.Length);
        if (data.StartsWith("AutoCAD-86 bigfont 1.0\r\n"u8)) return ReadBigFont(data, options);
        var old = data.StartsWith("AutoCAD-86 shapes 1.0\r\n"u8) || data.StartsWith("AutoCAD-86 shapes 1.1\r\n"u8);
        var unicode = data.StartsWith("AutoCAD-86 unifont 1.0\r\n"u8);
        if (!old && !unicode) throw new NotSupportedException("Expected SHAPES 1.0/1.1, UNIFONT 1.0 or BIGFONT 1.0.");
        var reader = new ByteCursor(data[(old ? 23 : 24)..]);
        if (reader.Byte() != 0x1a) throw new FormatException("Missing SHX signature terminator.");
        var symbols = ImmutableDictionary.CreateBuilder<int, ShxSymbol>();
        string name = ""; int above = 0, below = 0, mode = 0, instructions = 0;
        var encoding = old ? ShxEncoding.Shapes : ShxEncoding.Unicode; var embedding = ShxEmbedding.Allowed;
        if (old)
        {
            var first = reader.Word(); var last = reader.Word(); var count = reader.Word();
            if (count < 1 || count > options.MaximumSymbols) throw new FormatException("Invalid SHX symbol count.");
            var entries = new (int Id, int Size)[count];
            for (var i = 0; i < count; i++) entries[i] = (reader.Word(), reader.Word());
            if (entries[0].Id != first || entries[^1].Id != last) throw new FormatException("SHX directory range does not match its entries.");
            foreach (var (id, size) in entries)
            {
                var record = new ByteCursor(reader.Take(size));
                var symbolName = record.Name();
                if (id == 0)
                {
                    if (encoding != ShxEncoding.Shapes) throw new FormatException("Duplicate SHX font definition.");
                    name = symbolName; above = record.Byte(); below = record.Byte(); mode = record.Byte();
                    if (record.Remaining < 1 || record.Take(record.Remaining)[^1] != 0) throw new FormatException("Unterminated SHX font definition.");
                    encoding = ShxEncoding.Legacy;
                }
                else Add(id, symbolName, record.Remaining, Decode(ref record, false, options, ref instructions));
            }
            if (!reader.Take(3).SequenceEqual("EOF"u8) || reader.Remaining != 0) throw new FormatException("Invalid SHX end marker.");
        }
        else
        {
            var count = reader.Word(); var definitionId = reader.Word(); var size = reader.Word();
            if (definitionId != 0 || count < 1 || count > options.MaximumSymbols + 1) throw new FormatException("Invalid UNIFONT directory.");
            var definition = new ByteCursor(reader.Take(size));
            name = definition.Name(); above = definition.Byte(); below = definition.Byte(); mode = definition.Byte();
            encoding = (ShxEncoding)definition.Byte(); embedding = (ShxEmbedding)definition.Byte();
            if (encoding == ShxEncoding.Legacy || definition.Byte() != 0) throw new FormatException("Invalid UNIFONT metadata terminator.");
            while (definition.Remaining > 0) if (definition.Byte() != 0) throw new FormatException("Unexpected UNIFONT metadata padding.");
            for (var i = 1; i < count; i++)
            {
                var id = reader.Word(); var bytes = reader.Word(); var record = new ByteCursor(reader.Take(bytes));
                var symbolName = record.Name();
                Add(id, symbolName, record.Remaining, Decode(ref record, true, options, ref instructions));
            }
            if (reader.Remaining != 0) throw new FormatException("UNIFONT record count does not match its payload.");
        }
        return new(name, above, below, mode, encoding, embedding, symbols.ToImmutable());

        void Add(int id, string symbolName, int size, ImmutableArray<ShxInstruction> code)
        {
            if (!symbols.TryAdd(id, new(id, symbolName, size, code))) throw new FormatException($"Duplicate SHX symbol {id}.");
        }
    }

    private static void Validate(ShxReadOptions options, int size)
    {
        if (options.MaximumBytes < 1 || options.MaximumSymbols < 1 || options.MaximumSymbols > 65_536 || options.MaximumInstructions < 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (size > options.MaximumBytes) throw new InvalidOperationException("SHX input byte budget exceeded.");
    }

    private static ImmutableArray<ShxInstruction> Decode(ref ByteCursor reader, bool unicode, ShxReadOptions options, ref int total)
    {
        var result = ImmutableArray.CreateBuilder<ShxInstruction>();
        while (true)
        {
            if (++total > options.MaximumInstructions) throw new InvalidOperationException("SHX instruction budget exceeded.");
            var code = reader.Byte();
            var args = ImmutableArray.CreateBuilder<int>();
            switch (code)
            {
                case 0:
                    while (reader.Remaining > 0) if (reader.Byte() != 0) throw new FormatException("Nonzero bytes after SHX symbol terminator.");
                    return result.ToImmutable();
                case 1: case 2: case 5: case 6: case 14: break;
                case 3: case 4:
                    var factor = reader.Byte(); if (factor == 0) throw new FormatException("A SHX scale factor cannot be zero.");
                    args.Add(factor); break;
                case 7: args.Add(unicode ? reader.Word() : reader.Byte()); break;
                case 8: args.Add(reader.Signed()); args.Add(reader.Signed()); break;
                case 9: case 13:
                    while (true)
                    {
                        var x = reader.Signed(); var y = reader.Signed();
                        if (x == 0 && y == 0) break;
                        if (++total > options.MaximumInstructions) throw new InvalidOperationException("SHX displacement budget exceeded.");
                        args.Add(x); args.Add(y); if (code == 13) args.Add(reader.Signed());
                    }
                    break;
                case 10: args.Add(reader.Byte()); args.Add(reader.Byte()); break;
                case 11: for (var i = 0; i < 5; i++) args.Add(reader.Byte()); break;
                case 12: for (var i = 0; i < 3; i++) args.Add(reader.Signed()); break;
                case 15: throw new NotSupportedException("Reserved SHX opcode 15 is not a vector instruction.");
            }
            result.Add(new(code, args.ToImmutable()));
        }
    }

    private ref struct ByteCursor
    {
        private ReadOnlySpan<byte> data;
        public int Remaining => data.Length;
        public ByteCursor(ReadOnlySpan<byte> data) => this.data = data;
        public ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > data.Length) throw new FormatException("Truncated SHX record.");
            var value = data[..count]; data = data[count..]; return value;
        }
        public byte Byte() => Take(1)[0];
        public int Signed() => unchecked((sbyte)Byte());
        public int Word() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
        public string Name()
        {
            var end = data.IndexOf((byte)0);
            if (end < 0) throw new FormatException("Unterminated SHX symbol name.");
            var result = Encoding.Latin1.GetString(Take(end)); Take(1); return result;
        }
    }
}
