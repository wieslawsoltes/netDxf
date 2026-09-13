using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly double[] ExactDoubles =
    {
        1e-20, -1e-20, double.Epsilon, -double.Epsilon,
        1.2345678901234567, -1.2345678901234567, double.MaxValue, double.MinValue,
        0.0, BitConverter.Int64BitsToDouble(long.MinValue),
        BitConverter.Int64BitsToDouble(0x3FF0000000000001),
        BitConverter.Int64BitsToDouble(0x3FEFFFFFFFFFFFFF),
        BitConverter.Int64BitsToDouble(0x0010000000000000),
        BitConverter.Int64BitsToDouble(0x000FFFFFFFFFFFFF),
        1e300, -1e300, 1e-300, -1e-300, Math.PI, Math.E,
        9007199254740991.0, 9007199254740992.0, 0.1, 1.0
    };

    private static void RegisterDoublePrecisionWriterTests()
    {
        foreach (var entry in ExpectedTagTypes().Where(t => t.Value == DxfTagValueType.Double))
            foreach (bool binary in new[] { false, true })
            {
                short code = entry.Key; bool b = binary;
                Run($"double-output/all-groups/{code}/{b}", () => ExactDoubleCodec(code, b, ExactDoubles));
            }
        foreach (string culture in new[] { "en-US", "pl-PL", "tr-TR", "ar-SA" })
        {
            string c = culture;
            Run($"double-output/random-corpus/{c}", () => ExactDoubleRandom(c));
        }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"double-output/document/{v}/{b}", () => ExactDoubleDocument(v, b));
            }
        Run("double-output/canonical-zero", ExactDoubleZero);
    }

    private static void SameDoubleBits(double expected, double actual, string message)
    {
        Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual), message);
    }

    private static void ExactDoubleCodec(short code, bool binary, IReadOnlyList<double> values)
    {
        using var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        foreach (double value in values) Invoke(writer, "Write", code, value);
        Invoke(writer, "Write", (short)0, "EOF"); Invoke(writer, "Flush");
        stream.Position = 0; object reader = NewCodeReader(stream, binary);
        foreach (double expected in values)
        {
            Invoke(reader, "Next"); Equal(code, TagCode(reader), "Double group boundary");
            SameDoubleBits(expected, (double)Invoke(reader, "ReadDouble")!, "Lost finite-double bits at group " + code);
        }
        Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Double following-record boundary");
        Check(stream.CanRead, "Double codec closed its caller's stream.");
    }

    private static void ExactDoubleRandom(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var values = new List<double>(4096);
            ulong state = 0x9E3779B97F4A7C15UL;
            while (values.Count < 4096)
            {
                // An explicit bit generator, not Random's runtime-dependent sequence.
                state ^= state >> 12; state ^= state << 25; state ^= state >> 27;
                ulong bits = unchecked(state * 0x2545F4914F6CDD1DUL);
                if (((bits >> 52) & 2047) != 2047)
                    values.Add(BitConverter.Int64BitsToDouble(unchecked((long)bits)));
            }
            ExactDoubleCodec(40, false, values);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    private static void ExactDoubleZero()
    {
        using var stream = new MemoryStream(); object writer = NewCodeWriter(stream, false);
        foreach (double value in new[] { 0.0, BitConverter.Int64BitsToDouble(long.MinValue), 1.0 })
            Invoke(writer, "WriteDouble", value);
        Invoke(writer, "Flush");
        string text = Encoding.UTF8.GetString(stream.ToArray()).TrimStart('\uFEFF').Replace("\r\n", "\n");
        Equal("0.0\n-0.0\n1.0\n", text, "Explicit real/negative-zero spelling");
    }

    private static void ExactDoubleDocument(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        for (int i = 0; i < ExactDoubles.Length; ++i)
        {
            var point = new Point(new Vector3(ExactDoubles[i], i, 0));
            var xdata = new XData(new ApplicationRegistry("DOUBLE_BITS"));
            xdata.XDataRecord.Add(new XDataRecord(XDataCode.Real, ExactDoubles[i]));
            point.XData.Add(xdata); document.Entities.Add(point);
        }
        document.DrawingVariables.AddCustomVariable(new HeaderVariable("$USERR1", 40, 1e-20));
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            bool b = cycle == 1 ? !binary : binary;
            using var output = new MemoryStream(); Check(document.Save(output, b), "Double fixture save failed.");
            if (cycle == 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"double-precision-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Finite double fixture failed to reload.");
            Point[] points = document.Entities.Points.OrderBy(p => p.Position.Y).ToArray();
            Equal(ExactDoubles.Length, points.Length, "Point count");
            for (int i = 0; i < points.Length; ++i)
            {
                SameDoubleBits(ExactDoubles[i], points[i].Position.X, "Point geometry bits");
                SameDoubleBits(ExactDoubles[i], (double)points[i].XData["DOUBLE_BITS"].XDataRecord.Single().Value, "XData real bits");
            }
            SameDoubleBits(1e-20, (double)CustomHeaderValue(document, "$USERR1").Value, "Custom header real bits");
            Check(output.CanRead, "Double document closed its caller's stream.");
        }
    }
}
