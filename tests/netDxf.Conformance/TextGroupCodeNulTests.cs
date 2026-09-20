// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text;
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static object GroupCodeReader(string text)
    {
        Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.TextCodeValueReader", true)!;
        return Activator.CreateInstance(type, new StringReader(text))!;
    }

    private static void RegisterTextGroupCodeNulTests()
    {
        foreach (short code in new short[] { 0, 5, 10, 70, 90, 100, 160, 290, 310, 999, 1000, 1071 })
            foreach (string suffix in new[] { "\0", "\0\0", " \0", "\t\0" })
                Run($"text-code-nul/codec/{code}/{Convert.ToHexString(Encoding.UTF8.GetBytes(suffix))}", () =>
                {
                    object reader = GroupCodeReader(code.ToString(System.Globalization.CultureInfo.InvariantCulture) + suffix + "\n00\n");
                    Throws<FormatException>(() => Invoke(reader, "Next"));
                });
        foreach (DxfVersion version in HandleProfiles)
            foreach (bool crlf in new[] { false, true })
                Run($"text-code-nul/raw/{version}/{crlf}", () =>
                {
                    string nl = crlf ? "\r\n" : "\n";
                    string text = string.Join(nl, new[] { "0\0", "SECTION", "2", "HEADER", "9", "$ACADVER",
                        "1", HandleProfileName(version), "0", "ENDSEC", "0", "EOF", "" });
                    using var input = new MemoryStream(Encoding.ASCII.GetBytes(text));
                    Throws<FormatException>(() => DxfRawDocument.Load(input));
                    Check(input.CanRead, "Failure closed the caller-owned stream");
                });
        foreach (string code in new[] { "0", " 0 ", "\t0\t", "+0", "000" })
            Run("text-code-nul/valid/" + Convert.ToHexString(Encoding.ASCII.GetBytes(code)), () =>
            {
                object reader = GroupCodeReader(code + "\nEOF\n");
                Invoke(reader, "Next"); Equal("EOF", (string)Invoke(reader, "ReadString")!, "Valid integer spelling rejected");
            });
        Run("text-code-nul/value-not-consumed", () =>
        {
            var input = new StringReader("10\0\n7.5\n");
            Type type = typeof(DxfDocument).Assembly.GetType("netDxf.IO.TextCodeValueReader", true)!;
            object reader = Activator.CreateInstance(type, input)!;
            Throws<FormatException>(() => Invoke(reader, "Next"));
            Equal("7.5", input.ReadLine(), "Malformed code consumed a value line");
        });
    }
}
