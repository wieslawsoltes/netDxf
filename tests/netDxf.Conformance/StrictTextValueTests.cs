using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterStrictTextValueTests()
    {
        RegisterStrictIntegers("int16", "ReadShort", StrictRanges((60,79), (170,179), (270,289), (370,389), (400,409), (1060,1070)),
            new (string, object)[] { ("-32768", short.MinValue), ("32767", short.MaxValue), (" \t+0012 \t", (short)12) },
            "32768", "-32769");
        RegisterStrictIntegers("int32", "ReadInt", StrictRanges((90,99), (420,429), (440,459), (1071,1071)),
            new (string, object)[] { ("-2147483648", int.MinValue), ("2147483647", int.MaxValue), (" +0012 ", 12) },
            "2147483648", "-2147483649");
        RegisterStrictIntegers("int64", "ReadLong", StrictRanges((160,169)),
            new (string, object)[] { ("-9223372036854775808", long.MinValue), ("9223372036854775807", long.MaxValue), (" +0012 ", 12L) },
            "9223372036854775808", "-9223372036854775809");

        foreach (short code in StrictRanges((10,59), (110,149), (210,239), (460,469), (1010,1059)))
        {
            short c = code;
            Run($"text/strict/double/{c}/valid", () =>
            {
                foreach (var pair in new[] { ("0", 0.0), (" -1.25e+12 ", -1.25e12), (".5", 0.5), ("1.", 1.0),
                    ("5e-324", double.Epsilon), ("1.7976931348623157E+308", double.MaxValue) })
                    Equal(pair.Item2, (double)StrictTextRead(c, pair.Item1, "ReadDouble"), "floating-point value");
            });
            Run($"text/strict/double/{c}/invalid", () =>
            {
                foreach (string bad in new[] { "", " ", "garbage", "1,25", "1.2.3", "1e", "--1", "1\0", "NaN", "nan", "+NaN", "Infinity", "-Infinity", "1e9999", "-1e9999" })
                    StrictTextReject(c, bad);
            });
        }
        foreach (short code in StrictRanges((290,299)))
        {
            short c = code;
            Run($"text/strict/bool/{c}/valid", () =>
            {
                Equal(false, (bool)StrictTextRead(c, " 0 ", "ReadBool"), "false");
                Equal(true, (bool)StrictTextRead(c, " +01 ", "ReadBool"), "true");
            });
            Run($"text/strict/bool/{c}/invalid", () =>
            {
                foreach (string bad in new[] { "", "true", "false", "-1", "2", "255", "256", "1.0", "1\0", "garbage" })
                    StrictTextReject(c, bad);
            });
        }
        foreach (short code in StrictRanges((5,5), (105,105), (320,369), (390,399), (480,481), (1005,1005)))
        {
            short c = code;
            Run($"text/strict/handle/{c}/valid", () =>
            {
                foreach (var pair in new[] { ("0", "0"), (" \t000aF \t", "AF"), ("7fffffffffffffff", "7FFFFFFFFFFFFFFF"),
                    ("8000000000000000", "8000000000000000"), ("ffffffffffffffff", "FFFFFFFFFFFFFFFF") })
                    Equal(pair.Item2, (string)StrictTextRead(c, pair.Item1, "ReadHex"), "normalized unsigned handle");
            });
            Run($"text/strict/handle/{c}/invalid", () =>
            {
                foreach (string bad in new[] { "", " ", "G", "1G", "-1", "+1", "0xFF", "1 2", "1\0", "ＦF", "10000000000000000", "00000000000000000" })
                    StrictTextReject(c, bad);
            });
        }
        Run("text/strict/culture-invariance", () =>
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                foreach (string culture in new[] { "pl-PL", "fr-FR", "ar-SA", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                    Equal(1.25, (double)StrictTextRead(10, "1.25", "ReadDouble"), "invariant decimal");
                    Equal((short)-17, (short)StrictTextRead(70, "-17", "ReadShort"), "invariant integer");
                    Equal("ABCDEF", (string)StrictTextRead(330, "abcdef", "ReadHex"), "invariant handle");
                    StrictTextReject(10, "1,25");
                }
            }
            finally { CultureInfo.CurrentCulture = previous; }
        });
        Run("text/strict/ordinary-strings-unchanged", () =>
        {
            foreach (short code in new short[] { 0,1,2,3,4,6,7,8,9,100,101,102,300,410,430,470,999,1000,1001,1002,1003,1006,1009 })
                Equal("  literal text  ", (string)StrictTextRead(code, "  literal text  ", "ReadString"), "string whitespace");
        });
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            Run($"text/strict/document/{v}/bad-coordinate", () => StrictTextDocument(v, "10\ninvalid", false));
            Run($"text/strict/document/{v}/nonfinite-coordinate", () => StrictTextDocument(v, "10\nNaN", false));
            Run($"text/strict/document/{v}/bad-handle", () => StrictTextDocument(v, "5\nGARBAGE", false));
            Run($"text/strict/document/{v}/bad-int16", () => StrictTextDocument(v, "60\n32768", false));
            Run($"text/strict/document/{v}/valid", () => StrictTextDocument(v, "", true));
        }
    }

    private static IEnumerable<short> StrictRanges(params (int First, int Last)[] ranges)
    {
        foreach (var range in ranges)
            for (int code = range.First; code <= range.Last; ++code) yield return (short)code;
    }

    private static void RegisterStrictIntegers(string type, string getter, IEnumerable<short> codes,
        (string Text, object Value)[] valid, params string[] overflow)
    {
        foreach (short code in codes)
        {
            short c = code;
            Run($"text/strict/{type}/{c}/valid", () =>
            {
                foreach (var pair in valid) Equal(pair.Value, StrictTextRead(c, pair.Text, getter), "exact integer and runtime type");
            });
            Run($"text/strict/{type}/{c}/invalid", () =>
            {
                foreach (string bad in overflow.Concat(new[] { "", " ", "garbage", "1.0", "1e2", "1,000", "--1", "1\0" }))
                    StrictTextReject(c, bad);
            });
        }
    }

    private static object StrictTextRead(short code, string text, string getter)
    {
        using var source = new StringReader(code.ToString(CultureInfo.InvariantCulture) + "\n" + text + "\n0\nEOF\n");
        object reader = CreateTextHexReader(source);
        Invoke(reader, "Next");
        object result = Invoke(reader, getter)!;
        Invoke(reader, "Next");
        Equal("EOF", (string)Invoke(reader, "ReadString")!, "numeric record boundary");
        return result;
    }

    private static void StrictTextReject(short code, string text)
    {
        using var source = new StringReader("999\ncomment\n" + code.ToString(CultureInfo.InvariantCulture) + "\n" + text + "\n");
        object reader = CreateTextHexReader(source);
        Invoke(reader, "Next");
        try { Invoke(reader, "Next"); }
        catch (FormatException exception)
        {
            Check(exception.Message.Contains($"group code {code}", StringComparison.Ordinal), "Missing group-code diagnostic.");
            Check(exception.Message.Contains("line 4", StringComparison.Ordinal), "Missing physical value-line diagnostic.");
            return;
        }
        throw new InvalidOperationException("Malformed numeric/handle data was silently accepted.");
    }

    private static void StrictTextDocument(DxfVersion version, string replacement, bool valid)
    {
        string acadver = version switch
        {
            DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018",
            DxfVersion.AutoCad2007 => "AC1021", DxfVersion.AutoCad2010 => "AC1024",
            DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
            _ => throw new ArgumentOutOfRangeException(nameof(version))
        };
        string entity = "0\nLINE\n5\nAB\n100\nAcDbEntity\n8\n0\n60\n0\n100\nAcDbLine\n10\n1.25\n20\n-2.5\n30\n3.75\n11\n4\n21\n5\n31\n6\n";
        if (replacement.StartsWith("10\n", StringComparison.Ordinal)) entity = entity.Replace("10\n1.25", replacement, StringComparison.Ordinal);
        if (replacement.StartsWith("5\n", StringComparison.Ordinal)) entity = entity.Replace("5\nAB", replacement, StringComparison.Ordinal);
        if (replacement.StartsWith("60\n", StringComparison.Ordinal)) entity = entity.Replace("60\n0", replacement, StringComparison.Ordinal);
        string file = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n" + acadver + "\n9\n$DWGCODEPAGE\n3\nANSI_1252\n9\n$HANDSEED\n5\nFFFF\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n" + entity + "0\nENDSEC\n0\nEOF\n";
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes(file));
        if (valid)
        {
            DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Valid fixture rejected.");
            Equal(new Vector3(1.25, -2.5, 3.75), loaded.Entities.Lines.Single().StartPoint, "fixture coordinates");
        }
        else
        {
#if DEBUG
            Throws<FormatException>(() => DxfDocument.Load(stream));
#else
            Check(DxfDocument.Load(stream) == null, "Malformed document returned a corrupted drawing instead of failing.");
#endif
        }
        Check(stream.CanRead, "Load closed a caller-owned stream.");
    }
}
