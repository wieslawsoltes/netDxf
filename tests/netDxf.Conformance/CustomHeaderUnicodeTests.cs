using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (string Name, short Code, string Value)[] HeaderUnicodeValues =
    {
        ("$PROJECTNAME", 1, "Zażółć 東京 Ω"),
        ("$HYPERLINKBASE", 1, "C:\\Próby\\東京\\rysunki"),
        ("$MENU", 1, "Меню"),
        ("$UCSBASE", 2, "工程_ó"),
        ("$PUCSBASE", 2, "papier_Ł"),
        ("$PUCSNAME", 2, ""),
        ("$UCSORTHOREF", 2, "ordinary ASCII \\ path")
    };

    private static void RegisterCustomHeaderUnicodeTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"header/custom-unicode/escaped-input/{v}/{b}", () => CustomHeaderRead(v, b, true));
                Run($"header/custom-unicode/ordinary-input/{v}/{b}", () => CustomHeaderRead(v, b, false));
                Run($"header/custom-unicode/exact-export/{v}/{b}", () => CustomHeaderExport(v, b));
                Run($"header/custom-unicode/repeated-roundtrip/{v}/{b}", () => CustomHeaderRoundTrip(v, b));
                Run($"header/custom-unicode/non-string-controls/{v}/{b}", () => CustomHeaderNonStrings(v, b));
            }
    }

    private static string HeaderEscape(string text) => string.Concat(text.Select(c => c > 127
        ? "\\U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture) : c.ToString()));

    private static string HeaderVersion(DxfVersion version) => version switch
    {
        DxfVersion.AutoCad2000 => "AC1015", DxfVersion.AutoCad2004 => "AC1018", DxfVersion.AutoCad2007 => "AC1021",
        DxfVersion.AutoCad2010 => "AC1024", DxfVersion.AutoCad2013 => "AC1027", DxfVersion.AutoCad2018 => "AC1032",
        _ => throw new ArgumentOutOfRangeException(nameof(version))
    };

    private static MemoryStream CustomHeaderFixture(DxfVersion version, bool binary, bool escaped)
    {
        var stream = new MemoryStream();
        object writer = NewCodeWriter(stream, binary);
        void T(short code, object value) => Invoke(writer, "Write", code, value);
        T(0, "SECTION"); T(2, "HEADER"); T(9, "$ACADVER"); T(1, HeaderVersion(version));
        T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252");
        foreach (var (name, code, value) in HeaderUnicodeValues)
        {
            T(9, name);
            // Ordinary legacy input stays ASCII. UTF-8 fixtures use actual Unicode;
            // escaped fixtures test independently authored U+ sequences in every family.
            T(code, escaped ? HeaderEscape(value) : version < DxfVersion.AutoCad2007 ? "ASCII " + name : value);
        }
        T(0, "ENDSEC"); T(0, "EOF"); Invoke(writer, "Flush"); stream.Position = 0;
        return stream;
    }

    private static HeaderVariable CustomHeaderValue(DxfDocument document, string name)
    {
        Check(document.DrawingVariables.TryGetCustomVariable(name, out HeaderVariable variable), "Missing custom header " + name);
        return variable;
    }

    private static void CustomHeaderRead(DxfVersion version, bool binary, bool escaped)
    {
        using var input = CustomHeaderFixture(version, binary, escaped);
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Custom header input failed.");
        foreach (var (name, code, text) in HeaderUnicodeValues)
        {
            HeaderVariable value = CustomHeaderValue(document, name);
            Equal(code, value.GroupCode, "Custom string group code");
            Equal(escaped || version >= DxfVersion.AutoCad2007 ? text : "ASCII " + name, (string)value.Value,
                "Custom header Unicode decoding " + name);
        }
        Check(input.CanRead, "Header reader closed caller stream.");
    }

    private static DxfDocument NewCustomHeaderDocument(DxfVersion version)
    {
        var doc = new DxfDocument(version);
        foreach (var (name, code, value) in HeaderUnicodeValues)
            doc.DrawingVariables.AddCustomVariable(new HeaderVariable(name, code, value));
        return doc;
    }

    private static Dictionary<string, List<(short Code, object Value)>> ReadRawHeader(byte[] bytes, bool binary)
    {
        using var input = new MemoryStream(bytes); object reader = NewCodeReader(input, binary);
        var result = new Dictionary<string, List<(short, object)>>(StringComparer.OrdinalIgnoreCase);
        bool header = false; string? name = null;
        while (true)
        {
            Invoke(reader, "Next"); short code = TagCode(reader);
            object value = reader.GetType().GetProperty("Value")!.GetValue(reader)!;
            if (code == 0 && Equals(value, "EOF")) break;
            if (code == 2 && Equals(value, "HEADER")) { header = true; continue; }
            if (header && code == 0 && Equals(value, "ENDSEC")) break;
            if (!header || code == 999) continue;
            if (code == 9) { name = (string)value; result.Add(name, new List<(short, object)>()); }
            else if (name != null) result[name].Add((code, value));
        }
        return result;
    }

    private static void CustomHeaderExport(DxfVersion version, bool binary)
    {
        DxfDocument document = NewCustomHeaderDocument(version);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Custom header save failed.");
        var raw = ReadRawHeader(output.ToArray(), binary);
        foreach (var (name, code, text) in HeaderUnicodeValues)
        {
            var tag = raw[name].Single(); Equal(code, tag.Code, "Export changed custom group code");
            Equal(version < DxfVersion.AutoCad2007 ? HeaderEscape(text) : text, (string)tag.Value, "Custom string wire encoding " + name);
            Equal(text, (string)CustomHeaderValue(document, name).Value, "Encoding mutated the source header");
        }
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"header-unicode-{version}-{binary}.dxf"), output.ToArray());
    }

    private static void CustomHeaderRoundTrip(DxfVersion version, bool binary)
    {
        DxfDocument doc = NewCustomHeaderDocument(version);
        for (int cycle = 0; cycle < 4; cycle++)
        {
            bool transport = (cycle & 1) == 0 ? binary : !binary;
            using var stream = new MemoryStream(); Check(doc.Save(stream, transport), "Repeated custom header save failed.");
            stream.Position = 0; doc = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Repeated custom header load failed.");
            foreach (var (name, _, text) in HeaderUnicodeValues)
                Equal(text, (string)CustomHeaderValue(doc, name).Value, "Repeated custom Unicode round trip " + name);
            Check(stream.CanRead, "Custom header round trip closed caller stream.");
        }
        CustomHeaderValue(doc, "$PROJECTNAME").Value = "changed Żółć 東京";
        using var edited = new MemoryStream(); Check(doc.Save(edited, binary), "Edited custom header save failed.");
        edited.Position = 0;
        DxfDocument updated = DxfDocument.Load(edited) ?? throw new InvalidOperationException("Edited custom header load failed.");
        Equal("changed Żółć 東京", (string)CustomHeaderValue(updated, "$PROJECTNAME").Value, "Custom header edit lost Unicode");
    }

    private static void CustomHeaderNonStrings(DxfVersion version, bool binary)
    {
        var controls = new[]
        {
            new HeaderVariable("$USERI1", 70, (short)-123), new HeaderVariable("$USERR1", 40, 1.23456789),
            new HeaderVariable("$LIMMIN", 20, new Vector2(-1.25, 2.5)),
            new HeaderVariable("$EXTMIN", 30, new Vector3(-10, -20, -30))
        };
        var document = new DxfDocument(version);
        foreach (HeaderVariable variable in controls) document.DrawingVariables.AddCustomVariable(variable);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Non-string custom header save failed.");
        output.Position = 0; DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Non-string custom header load failed.");
        foreach (HeaderVariable expected in controls)
        {
            HeaderVariable actual = CustomHeaderValue(loaded, expected.Name);
            Equal(expected.GroupCode, actual.GroupCode, "Non-string group changed");
            Equal(expected.Value.GetType(), actual.Value.GetType(), "Non-string runtime type changed");
            Equal(expected.Value, actual.Value, "Non-string custom value changed");
        }
    }
}
