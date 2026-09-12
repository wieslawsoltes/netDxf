using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterStrictBinaryValueTests()
    {
        foreach (short code in StrictRanges((10,59), (110,149), (210,239), (460,469), (1010,1059)))
        {
            short c = code;
            Run($"binary/strict/double/{c}/finite-bits", () =>
            {
                foreach (double value in new[] { 0.0, -0.0, -1.25e12, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue })
                {
                    double actual = (double)StrictBinaryRead(c, w => w.Write(value), "ReadDouble", true);
                    Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits(actual), "finite IEEE bits");
                }
            });
            Run($"binary/strict/double/{c}/nonfinite", () =>
            {
                foreach (long bits in new[] { 0x7ff0000000000000L, unchecked((long)0xfff0000000000000UL),
                    0x7ff0000000000001L, 0x7ff8000000000001L, unchecked((long)0xfff8000000001234UL) })
                    StrictBinaryReject(c, w => w.Write(bits));
            });
        }
        foreach (short code in StrictRanges((290,299)))
        {
            short c = code;
            Run($"binary/strict/bool/{c}/valid", () =>
            {
                Equal(false, (bool)StrictBinaryRead(c, w => w.Write((byte)0), "ReadBool"), "false byte");
                Equal(true, (bool)StrictBinaryRead(c, w => w.Write((byte)1), "ReadBool", true), "true byte");
            });
            Run($"binary/strict/bool/{c}/all-invalid-bytes", () =>
            {
                for (int b = 2; b <= 255; ++b)
                {
                    byte value = (byte)b;
                    StrictBinaryReject(c, w => w.Write(value));
                }
            });
        }
        foreach (short code in StrictRanges((5,5), (105,105), (320,369), (390,399), (480,481), (1005,1005)))
        {
            short c = code;
            Run($"binary/strict/handle/{c}/valid", () =>
            {
                foreach (var pair in new[] { ("0", "0"), (" \t000af \t", "AF"), ("7fffffffffffffff", "7FFFFFFFFFFFFFFF"),
                    ("8000000000000000", "8000000000000000"), ("ffffffffffffffff", "FFFFFFFFFFFFFFFF") })
                    Equal(pair.Item2, (string)StrictBinaryRead(c, w => WriteBinaryText(w, pair.Item1), "ReadHex", true), "unsigned handle");
            });
            Run($"binary/strict/handle/{c}/invalid", () =>
            {
                foreach (string value in new[] { "", " ", "G", "1G", "-1", "+1", "0xFF", "1 2", "ＦF", "10000000000000000", "00000000000000000" })
                    StrictBinaryReject(c, w => WriteBinaryText(w, value));
            });
        }
        RegisterBinaryIntegerEndpoints("int16", "ReadShort", StrictRanges((60,79), (170,179), (270,289), (370,389), (400,409), (1060,1070)),
            new object[] { short.MinValue, (short)0, short.MaxValue });
        RegisterBinaryIntegerEndpoints("int32", "ReadInt", StrictRanges((90,99), (420,429), (440,459), (1071,1071)),
            new object[] { int.MinValue, 0, int.MaxValue });
        RegisterBinaryIntegerEndpoints("int64", "ReadLong", StrictRanges((160,169)), new object[] { long.MinValue, 0L, long.MaxValue });
        Run("binary/strict/scalar-truncation", () =>
        {
            foreach (var pair in new[] { ((short)10, 8), ((short)70, 2), ((short)90, 4), ((short)160, 8), ((short)290, 1) })
            {
                for (int count = 0; count < pair.Item2; count++)
                {
                    byte[] bytes = StrictBinaryBytes(pair.Item1, w => w.Write(new byte[count]), false);
                    using var stream = new FragmentedReadStream(bytes);
                    using var source = new BinaryReader(stream, Encoding.UTF8, true);
                    object reader = CreateBinaryReader(source);
                    Throws<EndOfStreamException>(() => Invoke(reader, "Next"));
                }
            }
            using var unterminated = new MemoryStream(StrictBinaryBytes(5, w => w.Write(Encoding.ASCII.GetBytes("ABCD")), false));
            using var binary = new BinaryReader(unterminated, Encoding.UTF8, true);
            object decoder = CreateBinaryReader(binary);
            Throws<EndOfStreamException>(() => Invoke(decoder, "Next"));
        });
        Run("binary/strict/nonseekable-codec", () =>
        {
            using var stream = new StrictNonSeekableStream(StrictBinaryBytes(290, w => w.Write((byte)2), true));
            using var source = new BinaryReader(stream, Encoding.UTF8, true);
            object reader = CreateBinaryReader(source);
            try { Invoke(reader, "Next"); }
            catch (InvalidDataException exception)
            {
                Check(exception.Message.Contains("byte address unknown", StringComparison.Ordinal), "Nonseekable diagnostic requested an unsupported position.");
                return;
            }
            throw new InvalidOperationException("Invalid flag accepted on nonseekable codec input.");
        });
        Run("binary/strict/ordinary-strings", () =>
        {
            foreach (short code in new short[] { 0,1,2,3,4,6,7,8,9,100,101,102,300,410,430,470,1000,1001,1002,1003,1006,1009 })
                Equal("  Zażółć λ  ", (string)StrictBinaryRead(code, w => WriteBinaryText(w, "  Zażółć λ  "), "ReadString"), "UTF-8 strings unchanged");
        });
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            Run($"binary/strict/document/{v}/nonfinite", () => StrictBinaryDocument(v, true));
            Run($"binary/strict/document/{v}/invalid-handle", () => StrictBinaryDocument(v, false));
        }
    }

    private static void RegisterBinaryIntegerEndpoints(string kind, string getter, IEnumerable<short> codes, object[] values)
    {
        foreach (short code in codes)
        {
            short c = code;
            Run($"binary/strict/{kind}/{c}/endpoints", () =>
            {
                foreach (object value in values)
                    Equal(value, StrictBinaryRead(c, w =>
                    {
                        if (value is short s) w.Write(s);
                        else if (value is int i) w.Write(i);
                        else w.Write((long)value);
                    }, getter, true), "signed integer endpoint and runtime type");
            });
        }
    }

    private static void WriteBinaryText(BinaryWriter writer, string text) => writer.Write(Encoding.UTF8.GetBytes(text + "\0"));

    private static byte[] StrictBinaryBytes(short code, Action<BinaryWriter> payload, bool eof)
    {
        using var stream = new MemoryStream();
        stream.Write(BinarySentinel);
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write(code); payload(writer);
            if (eof) { writer.Write((short)0); WriteBinaryText(writer, "EOF"); }
        }
        return stream.ToArray();
    }

    private static object StrictBinaryRead(short code, Action<BinaryWriter> payload, string getter, bool fragmented = false)
    {
        byte[] bytes = StrictBinaryBytes(code, payload, true);
        using MemoryStream stream = fragmented ? new FragmentedReadStream(bytes) : new MemoryStream(bytes);
        using var source = new BinaryReader(stream, Encoding.UTF8, true);
        object reader = CreateBinaryReader(source);
        Invoke(reader, "Next");
        object result = Invoke(reader, getter)!;
        Invoke(reader, "Next");
        Equal("EOF", (string)Invoke(reader, "ReadString")!, "following record");
        Equal(stream.Length, stream.Position, "exact consumption");
        return result;
    }

    private static void StrictBinaryReject(short code, Action<BinaryWriter> payload)
    {
        // Start at a nonzero absolute offset to detect relative/character-count diagnostics.
        byte[] bytes = new byte[7].Concat(StrictBinaryBytes(code, payload, false)).ToArray();
        using var stream = new FragmentedReadStream(bytes);
        stream.Position = 7;
        using var source = new BinaryReader(stream, Encoding.UTF8, true);
        object reader = CreateBinaryReader(source);
        try { Invoke(reader, "Next"); }
        catch (InvalidDataException exception)
        {
            Check(exception.Message.Contains("group code " + code.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal), "Missing binary group diagnostic.");
            Check(exception.Message.Contains("byte address 31", StringComparison.Ordinal), "Wrong absolute value-byte address.");
            return;
        }
        throw new InvalidOperationException("Malformed binary value silently accepted.");
    }

    private static void StrictBinaryDocument(DxfVersion version, bool nonfinite)
    {
        using var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, true);
        void Tag(short code, object value) => Invoke(writer, "Write", code, value);
        string acadver = version switch
        {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018", DxfVersion.AutoCad2007 => "AC1021",
            DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        Tag(0, "SECTION"); Tag(2, "HEADER"); Tag(9, "$ACADVER"); Tag(1, acadver);
        Tag(9, "$DWGCODEPAGE"); Tag(3, "ANSI_1252"); Tag(9, "$HANDSEED"); Tag(5, "FFFF"); Tag(0, "ENDSEC");
        Tag(0, "SECTION"); Tag(2, "ENTITIES"); Tag(0, "LINE"); Tag(5, nonfinite ? "AB" : "INVALID");
        Tag(100, "AcDbEntity"); Tag(8, "0"); Tag(100, "AcDbLine");
        Tag(10, nonfinite ? double.NaN : 1.0); Tag(20, 2.0); Tag(30, 3.0); Tag(11, 4.0); Tag(21, 5.0); Tag(31, 6.0);
        Tag(0, "ENDSEC"); Tag(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0;
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(stream));
#else
        Check(DxfDocument.Load(stream) == null, "Invalid binary document loaded instead of failing.");
#endif
        Check(stream.CanRead, "Caller stream was closed.");
    }

    private sealed class StrictNonSeekableStream : MemoryStream
    {
        public StrictNonSeekableStream(byte[] buffer) : base(buffer) { }
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
