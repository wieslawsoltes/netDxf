using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering.Shx;

internal static partial class BigFontVerification
{
    internal const string Regular = "*BIGFONT 999,2,128,129,224,224\n*0,4,BigTest\n20,0,2,0\n*32832,0,STEM\n8,0,20,2,8,20,-20,14,8,-20,-20,0\n*32833,0,SECOND\n8,10,20,2,8,10,-20,14,8,-20,-20,0";
    internal const string Extended = "*BIGFONT 1,1,128,128\n*0,5,Extended\n20,0,2,20,0\n*1,0,PRIMITIVE\n8,0,20,0\n*32832,0,COMPOSITE\n7,0,1,3,4,10,12,2,8,20,0,14,8,-20,-20,0";
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action action) => tests.Add(("bigfont/" + name, action));
        Test("SHP count is a hint and lead ranges remain detached", () =>
        {
            var font = ShxFontReader.ReadAscii(Regular);
            Check(font.BigFont is not null && font.BigFont.ApproximateCharacters == 999 && font.Symbols.Count == 2);
            Check(font.IsFont && font.Encoding == ShxEncoding.PackedMultibyte && font.CapHeight == 20 && font.SupportsVerticalText);
            Check(font.BigFont!.IsLeadByte(128) && font.BigFont.IsLeadByte(129) && font.BigFont.IsLeadByte(224) && !font.BigFont.IsLeadByte(130));
            Near(ShxGlyphCompiler.Compile(font, 32832).Advance.X, 20);
        });
        Test("packed decoding consumes a trail delimiter atomically", () =>
        {
            var info = ShxFontReader.ReadAscii(Regular).BigFont!;
            var decoded = info.Decode([65, 128, 92, 129, 128, 66]);
            Check(decoded.SequenceEqual(new[] { new ShxEncodedCharacter(0, 1, 65, false),
                new ShxEncodedCharacter(1, 2, 0x805c, true), new ShxEncodedCharacter(3, 2, 0x8180, true), new ShxEncodedCharacter(5, 1, 66, false) }));
        });
        Test("packed decoding rejects incomplete characters and enforces limits", () =>
        {
            var info = ShxFontReader.ReadAscii(Regular).BigFont!;
            Throws<FormatException>(() => info.Decode([65, 128]));
            Throws<InvalidOperationException>(() => info.Decode([65, 66], 1));
            Throws<OperationCanceledException>(() => info.Decode([], cancellationToken: new(true)));
        });
        Test("lead range input is copied and canonicalized", () =>
        {
            var ranges = new[] { new ShxLeadByteRange(224, 224), new ShxLeadByteRange(128, 129) };
            var info = new ShxBigFontInfo(0, ranges); ranges[0] = new(0, 1);
            Check(info.LeadByteRanges[0].First == 128 && info.IsLeadByte(224) && !info.IsLeadByte(0));
        });
        foreach (var source in new[] { "*BIGFONT 1,1,128\n", "*BIGFONT 1,1,256,256\n", "*BIGFONT 1,2,128,129,129,130\n",
            "*BIGFONT 1,1,130,128\n", "*BIGFONT 1,0\n*1,0,X\n0", "*BIGFONT 1,0\n*0,4,X\n0,0,2,0",
            "*BIGFONT 1,0\n*0,5,X\n20,0,2,0,0", "*BIGFONT 1,0\n*0,5,X\n20,1,2,20,0",
            "*BIGFONT 1,0\n*0,4,X\n20,0,9,0" })
            Test("malformed SHP header/" + tests.Count, () => Throws<FormatException>(() => ShxFontReader.ReadAscii(source)));
        Test("extended primitive dimensions and cursor state", () =>
        {
            var font = ShxFontReader.ReadAscii(Extended); var glyph = ShxGlyphCompiler.Compile(font, 32832);
            var curve = glyph.Contours.Single().Curves.Single();
            Vector(curve.Start, new(3, 4)); Vector(curve.End, new(3, 16)); Vector(glyph.Advance, new(20, 0));
            Check(font.BigFont!.CharacterWidth == 20); Near(glyph.ExitScale, 1);
        });
        Test("extended primitives retain vertical conditionals and caller advance", () =>
        {
            var glyph = ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(Extended), 32832, true);
            Vector(glyph.Advance, new(0, -20)); Vector(glyph.Contours.Single().Curves.Single().End, new(3, 16));
        });
        Test("extended nonuniform arcs retain analytic extrema", () =>
        {
            var source = Extended.Replace("8,0,20,0\n", "10,5,0,0\n", StringComparison.Ordinal);
            var glyph = ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(source), 32832);
            Check(glyph.Contours.Single().Curves.Single() is EllipseCurve);
            Near(glyph.Bounds.MinX, -2); Near(glyph.Bounds.MaxX, 3); Near(glyph.Bounds.MinY, 1); Near(glyph.Bounds.MaxY, 7);
        });
        Test("extended recursion and shared execution quota", () =>
        {
            var cyclic = Extended.Replace("7,0,1,3,4,10,12", "7,0,32832,3,4,10,12", StringComparison.Ordinal);
            Throws<InvalidOperationException>(() => ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(cyclic), 32832));
            Throws<InvalidOperationException>(() => ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(Extended), 32832, options: new(MaximumInstructions: 3)));
        });
        Test("extended local stacks cannot consume caller stack entries", () =>
        {
            var source = Extended.Replace("8,0,20,0\n", "6,0\n", StringComparison.Ordinal);
            Throws<FormatException>(() => ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(source), 32832));
        });
        foreach (var tail in new[] { "7,0,1,3,4,10,0,0", "7,0,1,3,4,10", "7,0,1,256,4,10,12,0" })
            Test("invalid extended operand/" + tests.Count, () => Throws<FormatException>(() => ShxFontReader.ReadAscii(
                "*BIGFONT 1,0\n*0,5,X\n20,0,2,20,0\n*1,0,P\n8,0,20,0\n*32832,0,C\n" + tail)));
        Test("indexed binary directory accepts sparse slots and arbitrary record order", () =>
        {
            var bytes = Binary(false, (32833, "SECOND", new byte[] { 8, 2, 0, 0 }), (32832, "FIRST", new byte[] { 8, 0, 20, 0 }));
            var font = ShxFontReader.ReadBinary(bytes); Array.Fill(bytes, (byte)0);
            Check(font.Symbols.Count == 2 && font.BigFont!.IsLeadByte(128));
            Vector(ShxGlyphCompiler.Compile(font, 32832).Advance, new(0, 20));
        });
        Test("binary escaped subshape numbers use big-endian packed words", () =>
        {
            var font = ShxFontReader.ReadBinary(Binary(false, (32832, "P", new byte[] { 8, 3, 4, 0 }),
                (32833, "C", new byte[] { 7, 0, 128, 64, 0 })));
            Vector(ShxGlyphCompiler.Compile(font, 32833).Advance, new(3, 4));
        });
        Test("extended binary and SHP definitions produce equivalent geometry", () =>
        {
            var binary = Binary(true, (1, "PRIMITIVE", new byte[] { 8, 0, 20, 0 }),
                (32832, "COMPOSITE", new byte[] { 7, 0, 0, 1, 3, 4, 10, 12, 2, 8, 20, 0, 14, 8, 236, 236, 0 }));
            foreach (var vertical in new[] { false, true })
            {
                var a = ShxGlyphCompiler.Compile(ShxFontReader.ReadBinary(binary), 32832, vertical);
                var b = ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii(Extended), 32832, vertical);
                Vector(a.Advance, b.Advance); Check(a.Bounds == b.Bounds);
                Check(a.Contours.Single().Curves.SequenceEqual(b.Contours.Single().Curves));
            }
        });
        Test("all binary truncations fail with controlled format errors", () =>
        {
            var bytes = Binary(false, (32832, "G", new byte[] { 8, 1, 0, 0 }));
            for (var length = 0; length < bytes.Length; length++)
            {
                var rejected = false;
                try { ShxFontReader.ReadBinary(bytes.AsSpan(0, length)); }
                catch (Exception error) when (error is FormatException or NotSupportedException) { rejected = true; }
                Check(rejected, "Accepted binary truncation at " + length);
            }
        });
        Test("binary offsets cannot alias directory or overlap payloads", () =>
        {
            var bytes = Binary(false, (32832, "G", new byte[] { 8, 1, 0, 0 }));
            var invalid = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(35 + 16 + 4), 35);
            Throws<FormatException>(() => ShxFontReader.ReadBinary(invalid));
            invalid = (byte[])bytes.Clone(); var offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(39));
            BinaryPrimitives.WriteUInt32LittleEndian(invalid.AsSpan(55), offset);
            Throws<FormatException>(() => ShxFontReader.ReadBinary(invalid));
        });
        Test("binary duplicate IDs missing metrics and unsupported entries are explicit", () =>
        {
            var bytes = Binary(false, (32832, "G", new byte[] { 8, 1, 0, 0 }));
            var invalid = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(51), 0);
            Throws<FormatException>(() => ShxFontReader.ReadBinary(invalid));
            invalid = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(35), 5);
            Throws<FormatException>(() => ShxFontReader.ReadBinary(invalid));
            invalid = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt16LittleEndian(invalid.AsSpan(25), 9);
            Throws<NotSupportedException>(() => ShxFontReader.ReadBinary(invalid));
        });
        Test("parser input symbol and instruction budgets remain active", () =>
        {
            Throws<InvalidOperationException>(() => ShxFontReader.ReadAscii(Regular, new(MaximumBytes: 10)));
            Throws<InvalidOperationException>(() => ShxFontReader.ReadAscii(Regular, new(MaximumInstructions: 1)));
            Throws<InvalidOperationException>(() => ShxFontReader.ReadAscii(Regular, new(MaximumSymbols: 1)));
            Throws<InvalidOperationException>(() => ShxFontReader.ReadBinary(Binary(false, (32832, "G", new byte[] { 8, 1, 0, 0 })), new(MaximumInstructions: 1)));
        });
        RegisterLayout(tests);
    }
    // Original synthetic instruction records only; no external font bytes are copied or installed.
    internal static byte[] Binary(bool extended, params (int Number, string Name, byte[] Codes)[] glyphs)
    {
        var metadata = extended ? new byte[] { 20, 0, 2, 20, 0 } : new byte[] { 20, 0, 2, 0 };
        var records = new List<(int Number, byte[] Data)> { (0, "Test\0"u8.ToArray().Concat(metadata).ToArray()) };
        records.AddRange(glyphs.Select(g => (g.Number, Encoding.Latin1.GetBytes(g.Name + "\0").Concat(g.Codes).ToArray())));
        var slots = records.Count + 1; var payload = 35 + slots * 8;
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("AutoCAD-86 bigfont 1.0\r\n"u8); writer.Write((byte)0x1a);
        writer.Write((ushort)8); writer.Write((ushort)slots); writer.Write((ushort)1); writer.Write((ushort)128); writer.Write((ushort)129);
        for (var i = 0; i < slots; i++)
        {
            if (i == 1) { writer.Write(new byte[8]); continue; }
            var record = records[i == 0 ? 0 : i - 1]; writer.Write((ushort)record.Number); writer.Write((ushort)record.Data.Length); writer.Write((uint)payload);
            payload += record.Data.Length;
        }
        foreach (var record in records) writer.Write(record.Data);
        return stream.ToArray();
    }
    internal static void Check(bool condition, string message = "BIGFONT assertion failed.") { if (!condition) throw new Exception(message); }
    internal static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-9, $"Expected {expected}, got {actual}.");
    internal static void Vector(Vec3 actual, Vec3 expected) => Near(Vec3.Distance(actual, expected), 0);
    internal static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
