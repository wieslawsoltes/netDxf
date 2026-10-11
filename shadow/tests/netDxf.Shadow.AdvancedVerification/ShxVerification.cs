using System.Collections.Immutable;
using System.Text;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering.Shx;

internal static partial class ShxVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("shx/ASCII metadata, comments, hex numbers and case-sensitive names", () =>
        {
            var font = ShxFontReader.ReadAscii("; comment\n*0,4,Test\n10,2,2,0\n*65,4,A\n8,5,10,0\n*061,2,a\n020,0");
            Require(font.CapHeight == 10 && font.Descender == 2 && font.SupportsVerticalText && font.IsFont);
            Require(font.Symbols.Count == 2 && font.Names["A"] == 65 && font.Names["a"] == 97);
            Near(ShxGlyphCompiler.Compile(font, 97).Advance.X, 2);
        }));
        tests.Add(("shx/SHAPES binary detachment and numeric directory", () =>
        {
            var data = Binary(false, (65, "A", new byte[] { 8, 12, 0, 0 }));
            var font = ShxFontReader.ReadBinary(data); Array.Fill(data, (byte)0);
            Require(font.IsFont && font.CapHeight == 10); Near(ShxGlyphCompiler.Compile(font, 65).Advance.X, 12);
        }));
        tests.Add(("shx/UNIFONT metadata and sixteen-bit subshape calls", () =>
        {
            var font = ShxFontReader.ReadBinary(Binary(true,
                (65, "A", new byte[] { 8, 7, 3, 0 }), (0x3a9, "OMEGA", new byte[] { 7, 65, 0, 0 })));
            Require(font.Encoding == ShxEncoding.Unicode && font.Embedding == ShxEmbedding.ReadOnly);
            var glyph = ShxGlyphCompiler.Compile(font, 0x3a9); Near(glyph.Advance.X, 7); Near(glyph.Advance.Y, 3);
        }));
        tests.Add(("shx/packed vector directions and rational scale factors", () =>
        {
            var glyph = Glyph("4,3,3,2,021,0"); Near(glyph.Advance.X, 3); Near(glyph.Advance.Y, 1.5);
            Near(glyph.Bounds.MaxX, 3);
        }));
        tests.Add(("shx/subshapes inherit pen state and position stack restores without strokes", () =>
        {
            var font = ShxFontReader.ReadAscii("*1,12,MAIN\n2,7,2,1,5,8,0,4,6,8,3,0,0\n*2,4,SUB\n8,9,0,0");
            var glyph = ShxGlyphCompiler.Compile(font, 1);
            Near(glyph.Advance.X, 12); Near(glyph.Advance.Y, 0);
            Require(glyph.Contours.Length == 2 && glyph.Contours.Sum(c => c.Curves.Length) == 2);
            Near(glyph.Bounds.MinX, 9);
        }));
        tests.Add(("shx/vertical conditional skips the complete displacement instruction", () =>
        {
            var font = ShxFontReader.ReadAscii("*1,14,TEST\n14,9,3,0,0,3,0,0,8,10,0,0");
            var horizontal = ShxGlyphCompiler.Compile(font, 1); var vertical = ShxGlyphCompiler.Compile(font, 1, true);
            Near(horizontal.Advance.X, 10); Near(horizontal.Advance.Y, 0);
            Near(vertical.Advance.X, 13); Near(vertical.Advance.Y, 3);
            Require(horizontal.Contours[0].Curves.Length == 1 && vertical.Contours[0].Curves.Length == 3);
        }));
        tests.Add(("shx/octant circles and clockwise signed-magnitude encoding", () =>
        {
            var circle = Glyph("10,5,0,0"); var arc = (EllipseCurve)circle.Contours.Single().Curves.Single();
            Near(arc.Sweep, Math.Tau); Near(circle.Bounds.MinX, -10); Near(circle.Bounds.MinY, -5); Near(circle.Bounds.MaxY, 5);
            var clockwise = Glyph("10,5,-002,0");
            Near(((EllipseCurve)clockwise.Contours[0].Curves[0]).Sweep, -Math.PI / 2);
            var binary = ShxGlyphCompiler.Compile(ShxFontReader.ReadBinary(Binary(false, (1, "arc", new byte[] { 10, 5, 0x82, 0 }))), 1);
            Near(Vec3.Distance(binary.Advance, clockwise.Advance), 0);
        }));
        tests.Add(("shx/fractional arcs retain analytic start and sweep", () =>
        {
            var arc = (EllipseCurve)Glyph("11,64,128,0,8,012,0").Contours.Single().Curves.Single();
            Near(arc.StartAngle, 56.25 * Math.PI / 180); Near(arc.Sweep, 56.25 * Math.PI / 180);
            var reverse = (EllipseCurve)Glyph("11,64,128,0,8,-012,0").Contours.Single().Curves.Single();
            Near(reverse.StartAngle, 33.75 * Math.PI / 180); Near(reverse.Sweep, -56.25 * Math.PI / 180);
        }));
        tests.Add(("shx/single and repeated bulge instructions", () =>
        {
            var glyph = Glyph("13,10,0,127,0,10,-127,0,0,0");
            Require(glyph.Contours[0].Curves.Length == 2);
            Near(((EllipseCurve)glyph.Contours[0].Curves[0]).Sweep, Math.PI);
            Near(((EllipseCurve)glyph.Contours[0].Curves[1]).Sweep, -Math.PI);
            Near(glyph.Advance.X, 10); Near(glyph.Advance.Y, 10);
        }));
        tests.Add(("shx/malformed instructions, duplicate IDs and stack underflow are explicit", () =>
        {
            Throws<FormatException>(() => ShxFontReader.ReadAscii("*1,4,X\n8,1,0"));
            Throws<FormatException>(() => ShxFontReader.ReadAscii("*1,3,X\n3,0,0"));
            Throws<FormatException>(() => ShxFontReader.ReadAscii("*1,1,X\n0\n*1,1,Y\n0"));
            Throws<NotSupportedException>(() => ShxFontReader.ReadAscii("*1,2,X\n15,0"));
            Throws<FormatException>(() => Glyph("6,0"));
        }));
        tests.Add(("shx/all binary truncations fail without out-of-range reads", () =>
        {
            foreach (var unicode in new[] { false, true })
            {
                var bytes = Binary(unicode, (65, "A", new byte[] { 9, 10, 0, 0, 10, 0, 0, 0 }));
                for (var size = 0; size < bytes.Length; size++)
                {
                    var rejected = false;
                    try { ShxFontReader.ReadBinary(bytes.AsSpan(0, size)); }
                    catch (Exception error) when (error is FormatException or NotSupportedException) { rejected = true; }
                    Require(rejected, $"Accepted truncated SHX input of length {size}.");
                }
            }
        }));
        tests.Add(("shx/execution, recursion, input and cancellation budgets", () =>
        {
            var cyclic = ShxFontReader.ReadAscii("*1,3,CYCLE\n7,1,0");
            Throws<InvalidOperationException>(() => ShxGlyphCompiler.Compile(cyclic, 1));
            var font = ShxFontReader.ReadAscii("*1,4,LINES\n010,010,010,0");
            Throws<InvalidOperationException>(() => ShxGlyphCompiler.Compile(font, 1, options: new(MaximumCurves: 2)));
            Throws<InvalidOperationException>(() => ShxFontReader.ReadAscii("*1,2,X\n010,0", new(MaximumBytes: 1)));
            Throws<OperationCanceledException>(() => ShxGlyphCompiler.Compile(font, 1, cancellationToken: new(true)));
            Throws<KeyNotFoundException>(() => ShxGlyphCompiler.Compile(font, 99));
        }));
    }

    private static ShxGlyph Glyph(string codes) => ShxGlyphCompiler.Compile(ShxFontReader.ReadAscii("*1,0,TEST\n" + codes), 1);
    // Synthetic test records are generated in memory; tests never copy/install or distribute font files.
    private static byte[] Binary(bool unicode, params (int Number, string Name, byte[] Codes)[] symbols)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.Latin1, true);
        writer.Write(Encoding.ASCII.GetBytes(unicode ? "AutoCAD-86 unifont 1.0\r\n" : "AutoCAD-86 shapes 1.0\r\n")); writer.Write((byte)0x1a);
        var definition = Encoding.Latin1.GetBytes("TEST\0").Concat(unicode ? new byte[] { 10, 2, 2, 0, 2, 0 } : new byte[] { 10, 2, 2, 0 }).ToArray();
        var records = symbols.Select(s => Encoding.Latin1.GetBytes(s.Name + "\0").Concat(s.Codes).ToArray()).ToArray();
        if (unicode)
        {
            writer.Write((ushort)(symbols.Length + 1)); writer.Write((ushort)0); writer.Write((ushort)definition.Length); writer.Write(definition);
            for (var i = 0; i < symbols.Length; i++) { writer.Write((ushort)symbols[i].Number); writer.Write((ushort)records[i].Length); writer.Write(records[i]); }
        }
        else
        {
            writer.Write((ushort)0); writer.Write((ushort)symbols[^1].Number); writer.Write((ushort)(symbols.Length + 1));
            writer.Write((ushort)0); writer.Write((ushort)definition.Length);
            for (var i = 0; i < symbols.Length; i++) { writer.Write((ushort)symbols[i].Number); writer.Write((ushort)records[i].Length); }
            writer.Write(definition); foreach (var record in records) writer.Write(record); writer.Write("EOF"u8);
        }
        return output.ToArray();
    }
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Require(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-8, $"Expected {expected:R}, got {actual:R}.");
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
