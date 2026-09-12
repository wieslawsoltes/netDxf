using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunTextHexTests()
    {
        foreach (int code in Enumerable.Range(310, 10).Append(1004))
        {
            int captured = code;
            string prefix = $"text/binary-chunk/{code}";
            Run(prefix + "/mixed-case", () => TextHexValue(captured, "00aF10Ff", new byte[] { 0, 175, 16, 255 }));
            Run(prefix + "/empty", () => TextHexValue(captured, "", Array.Empty<byte>()));
            Run(prefix + "/all-byte-values", () => TextHexAllBytes(captured));
            Run(prefix + "/odd-length", () =>
            {
                foreach (string value in new[] { "0", "ABC", "0011223" })
                    TextHexRejected<FormatException>(captured, value, 2);
            });
            Run(prefix + "/missing-value", () => TextHexRejected<EndOfStreamException>(captured, null, 2));
            Run(prefix + "/invalid-digit", () =>
            {
                foreach (string value in new[] { "0G", "G0", "FFZZ", " 0", "0 ", "\t0", "0\t", "-1", "0x", "Ｆ0", "00\0F" })
                    TextHexRejected<FormatException>(captured, value, 2);
            });
            Run(prefix + "/line-diagnostic", () => TextHexRejected<FormatException>(captured, "Z0", 4));
        }

        foreach (DxfVersion version in SupportedVersions)
        {
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion capturedVersion = version;
                bool capturedBinary = binary;
                Run($"xdata/binary-chunks/{version}/{(binary ? "binary" : "text")}",
                    () => XDataBinaryChunkRoundTrip(capturedVersion, capturedBinary));
            }
        }
    }

    private static object CreateTextHexReader(TextReader reader)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.TextCodeValueReader", true)!;
        return Activator.CreateInstance(type, reader)!;
    }

    private static void TextHexValue(int code, string value, byte[] expected)
    {
        using var source = new StringReader(code.ToString(CultureInfo.InvariantCulture) + "\n" + value + "\n0\nEOF\n");
        object reader = CreateTextHexReader(source);
        Invoke(reader, "Next");
        byte[] actual = (byte[])Invoke(reader, "ReadBytes")!;
        Check(expected.SequenceEqual(actual), "Hex decoding changed the binary payload.");
        Invoke(reader, "Next");
        Equal("EOF", (string)Invoke(reader, "ReadString")!, "record after binary chunk");
    }

    private static void TextHexAllBytes(int code)
    {
        byte[] all = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        for (int start = 0; start < all.Length; start += 127)
        {
            byte[] part = all.Skip(start).Take(127).ToArray();
            string upper = Convert.ToHexString(part);
            TextHexValue(code, upper, part);
            TextHexValue(code, upper.ToLowerInvariant(), part);
        }
    }

    private static void TextHexRejected<T>(int code, string? value, int expectedLine) where T : Exception
    {
        string prefix = expectedLine == 4 ? "999\ncomment\n" : "";
        string input = prefix + code.ToString(CultureInfo.InvariantCulture) + "\n" + (value == null ? "" : value + "\n");
        using var source = new StringReader(input);
        object reader = CreateTextHexReader(source);
        if (expectedLine == 4) Invoke(reader, "Next");
        try
        {
            Invoke(reader, "Next");
        }
        catch (T exception)
        {
            Check(exception.Message.Contains("group code " + code.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                "Malformed binary chunk diagnostic must identify its group code.");
            Check(exception.Message.Contains("line " + expectedLine.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
                "Malformed binary chunk diagnostic must identify the value line.");
            return;
        }
        throw new InvalidOperationException($"Expected {typeof(T).Name}; malformed binary data must not become an empty payload.");
    }

    private static void XDataBinaryChunkRoundTrip(DxfVersion version, bool binary)
    {
        const string application = "DXF_HEX_CONFORMANCE";
        byte[] expected = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        var xdata = new XData(new ApplicationRegistry(application));
        for (int start = 0; start < expected.Length; start += 127)
            xdata.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, expected.Skip(start).Take(127).ToArray()));
        var line = new Line(Vector3.Zero, new Vector3(1, 2, 3));
        line.XData.Add(xdata);
        var document = new DxfDocument(version);
        document.Entities.Add(line);
        using var stream = new MemoryStream();
        Check(document.Save(stream, binary), "XData fixture failed to save.");
        stream.Position = 0;
        DxfDocument loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("XData fixture failed to load.");
        XData actual = loaded.Entities.Lines.Single().XData[application];
        Equal(3, actual.XDataRecord.Count, "binary XData chunk count");
        Check(actual.XDataRecord.All(record => record.Code == XDataCode.BinaryData), "XData record types changed.");
        Check(expected.SequenceEqual(actual.XDataRecord.SelectMany(record => (byte[])record.Value)),
            "Binary XData payload did not round trip exactly.");
    }
}
