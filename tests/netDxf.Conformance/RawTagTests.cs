using System.Globalization;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawTagTests()
    {
        Run("raw-tags/exhaustive-code-space", RawTagCodeSpace);
        Run("raw-tags/reference-kinds", RawTagReferenceKinds);
        Run("raw-tags/binary-storage-isolation", RawTagBinaryIsolation);
        Run("raw-tags/invalid-values", RawTagInvalidValues);
        Run("raw-tags/negative-zero-and-extremes", RawTagExtremes);
        Run("raw-tags/culture-independent-handles", RawTagCultures);
        foreach (var entry in ExpectedTagTypes())
        {
            short code = entry.Key;
            DxfTagValueType type = entry.Value;
            foreach (bool binary in code == 999 ? new[] { false } : new[] { false, true })
            {
                bool b = binary;
                Run($"raw-tags/codecs/{code}/{b}", () => RawTagCodec(code, type, b));
            }
        }
    }

    private static Dictionary<short, DxfTagValueType> ExpectedTagTypes()
    {
        var types = new Dictionary<short, DxfTagValueType>();
        void Add(int first, int last, DxfTagValueType type)
        {
            for (int i = first; i <= last; ++i) types.Add((short)i, type);
        }
        Add(0, 9, DxfTagValueType.String); Add(10, 59, DxfTagValueType.Double);
        Add(60, 79, DxfTagValueType.Int16); Add(90, 99, DxfTagValueType.Int32);
        Add(100, 102, DxfTagValueType.String); Add(105, 105, DxfTagValueType.Handle);
        Add(110, 149, DxfTagValueType.Double); Add(160, 169, DxfTagValueType.Int64);
        Add(170, 179, DxfTagValueType.Int16); Add(210, 239, DxfTagValueType.Double);
        Add(270, 289, DxfTagValueType.Int16); Add(290, 299, DxfTagValueType.Boolean);
        Add(300, 309, DxfTagValueType.String); Add(310, 319, DxfTagValueType.BinaryData);
        Add(320, 369, DxfTagValueType.Handle); Add(370, 389, DxfTagValueType.Int16);
        Add(390, 399, DxfTagValueType.Handle); Add(400, 409, DxfTagValueType.Int16);
        Add(410, 419, DxfTagValueType.String); Add(420, 429, DxfTagValueType.Int32);
        Add(430, 439, DxfTagValueType.String); Add(440, 459, DxfTagValueType.Int32);
        Add(460, 469, DxfTagValueType.Double); Add(470, 479, DxfTagValueType.String);
        Add(480, 481, DxfTagValueType.Handle); Add(999, 1003, DxfTagValueType.String);
        Add(1004, 1004, DxfTagValueType.BinaryData); Add(1005, 1005, DxfTagValueType.Handle);
        Add(1006, 1009, DxfTagValueType.String); Add(1010, 1059, DxfTagValueType.Double);
        Add(1060, 1070, DxfTagValueType.Int16); Add(1071, 1071, DxfTagValueType.Int32);
        types[5] = DxfTagValueType.Handle;
        return types;
    }

    private static void RawTagCodeSpace()
    {
        foreach (short code in new short[] { short.MinValue, -1, 80, 89, 103, 104, 106, 159, 180, 209, 240, 269, 482, 998, 1072, short.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => DxfGroupCode.GetValueType(code));
        var expected = ExpectedTagTypes();
        for (int value = short.MinValue; value <= short.MaxValue; ++value)
        {
            short code = (short)value;
            bool known = expected.TryGetValue(code, out var wanted);
            Equal(known, DxfGroupCode.TryGetValueType(code, out var actual), "Code admission " + code);
            if (known)
            {
                Equal(wanted, actual, "Type classification " + code);
                Equal(wanted, DxfGroupCode.GetValueType(code), "Throwing classifier " + code);
            }
            else
            {
                Equal(DxfHandleKind.None, DxfGroupCode.GetHandleKind(code), "Unknown handle code");
            }
        }
    }

    private static void RawTagReferenceKinds()
    {
        Equal(DxfHandleKind.ObjectIdentity, DxfGroupCode.GetHandleKind(5), "Entity identity");
        Equal(DxfHandleKind.ObjectIdentity, DxfGroupCode.GetHandleKind(105), "DIMSTYLE identity");
        for (short code = 320; code <= 399; ++code)
        {
            DxfHandleKind expected = code < 330 ? DxfHandleKind.Arbitrary : code < 340 ? DxfHandleKind.SoftPointer :
                code < 350 ? DxfHandleKind.HardPointer : code < 360 ? DxfHandleKind.SoftOwner :
                code < 370 ? DxfHandleKind.HardOwner : code < 390 ? DxfHandleKind.None : DxfHandleKind.HardPointer;
            Equal(expected, DxfGroupCode.GetHandleKind(code), "Reference class " + code);
        }
        Equal(DxfHandleKind.HardPointer, DxfGroupCode.GetHandleKind(480), "480");
        Equal(DxfHandleKind.HardPointer, DxfGroupCode.GetHandleKind(481), "481");
        Equal(DxfHandleKind.XData, DxfGroupCode.GetHandleKind(1005), "XData handle");
        Equal(DxfHandleKind.None, new DxfTag(1, "FFFFFFFF").HandleKind, "Ordinary text became a reference");
    }

    private static object RawTagSample(DxfTagValueType type) => type switch
    {
        DxfTagValueType.String => "Zażółć 東京 \\U+0041",
        DxfTagValueType.Handle => "FFFFFFFFFFFFFFFF",
        DxfTagValueType.Double => -1234.56789012345,
        DxfTagValueType.Int16 => (short)-32768,
        DxfTagValueType.Int32 => int.MinValue,
        DxfTagValueType.Int64 => long.MinValue,
        DxfTagValueType.Boolean => true,
        _ => new byte[] { 0, 255, 127, 128, 1 }
    };

    private static void RawTagCodec(short code, DxfTagValueType type, bool binary)
    {
        object value = RawTagSample(type);
        var original = new DxfTag(code, value);
        Equal(type, original.ValueType, "Tag value type");
        using var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        Invoke(writer, "Write", original.Code, original.Value);
        Invoke(writer, "Write", (short)0, "EOF"); Invoke(writer, "Flush");
        stream.Position = 0;
        object reader = NewCodeReader(stream, binary); Invoke(reader, "Next");
        var loaded = new DxfTag(TagCode(reader), reader.GetType().GetProperty("Value")!.GetValue(reader)!);
        Equal(code, loaded.Code, "Codec changed group code");
        Equal(value.GetType(), loaded.Value.GetType(), "Codec changed CLR type");
        if (value is byte[] bytes) Check(bytes.SequenceEqual((byte[])loaded.Value), "Codec changed bytes.");
        else Equal(value, loaded.Value, "Codec changed typed value");
        Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Following tag boundary");
    }

    private static void RawTagBinaryIsolation()
    {
        foreach (int length in new[] { 0, 1, 127, 128, 255, 256, 4096 })
        {
            byte[] source = Enumerable.Range(0, length).Select(i => (byte)i).ToArray();
            byte[] expected = (byte[])source.Clone();
            var tag = new DxfTag(310, source);
            Array.Fill(source, (byte)99);
            byte[] first = (byte[])tag.Value;
            Check(expected.SequenceEqual(first), "Tag retained source byte storage.");
            Array.Fill(first, (byte)88);
            Check(expected.SequenceEqual((byte[])tag.Value), "Tag exposed backing byte storage.");
            Check(!ReferenceEquals(first, tag.Value), "Binary Value is not a defensive copy.");
        }
    }

    private static void RawTagInvalidValues()
    {
        foreach (var entry in ExpectedTagTypes())
        {
            short code = entry.Key;
            Throws<ArgumentNullException>(() => new DxfTag(code, null!));
            Throws<ArgumentException>(() => new DxfTag(code, new object()));
            Throws<ArgumentException>(() => new DxfTag(code, 1m));
        }
        foreach (string bad in new[] { "", " 1", "1 ", "+1", "-1", "0x1", "G", "FFFFFFFFFFFFFFFFF", "Ｆ", "1\0" })
            foreach (short code in new short[] { 5, 105, 320, 330, 340, 350, 360, 390, 480, 1005 })
                Throws<ArgumentException>(() => new DxfTag(code, bad));
        foreach (string bad in new[] { "a\0b", "a\rb", "a\nb" })
            Throws<ArgumentException>(() => new DxfTag(1, bad));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Throws<ArgumentOutOfRangeException>(() => new DxfTag(10, bad));
        Throws<ArgumentException>(() => new DxfTag(70, 1));
        Throws<ArgumentException>(() => new DxfTag(90, (short)1));
        Throws<ArgumentException>(() => new DxfTag(160, 1));
        Throws<ArgumentException>(() => new DxfTag(290, (byte)1));
        Throws<ArgumentException>(() => new DxfTag(10, 1f));
    }

    private static void RawTagExtremes()
    {
        foreach (double value in new[] { 0.0, -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, double.MinValue })
            Equal(BitConverter.DoubleToInt64Bits(value), BitConverter.DoubleToInt64Bits((double)new DxfTag(10, value).Value), "Double bits");
        Equal(long.MaxValue, (long)new DxfTag(160, long.MaxValue).Value, "Int64 endpoint");
        Equal(int.MaxValue, (int)new DxfTag(450, int.MaxValue).Value, "DXF long is Int32");
        Equal((short)short.MaxValue, (short)new DxfTag(70, short.MaxValue).Value, "Int16 endpoint");
        Equal("", (string)new DxfTag(1, "").Value, "Empty text");
        Equal("00abcdef", (string)new DxfTag(5, "00abcdef").Value, "Tag constructor rewrote handle spelling");
    }

    private static void RawTagCultures()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "pl-PL", "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Equal("abcdef", (string)new DxfTag(5, "abcdef").Value, "Culture changed handle");
                Equal("Zażółć 東京", (string)new DxfTag(1, "Zażółć 東京").Value, "Culture changed text");
                Throws<ArgumentException>(() => new DxfTag(5, "١"));
            }
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
