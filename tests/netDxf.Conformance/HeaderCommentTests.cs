using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHeaderCommentTests()
    {
        int gaps = HeaderCommentPairs(DxfVersion.AutoCad2018).Count - 1;
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            for (int gap = 1; gap <= gaps; gap++)
            {
                int g = gap;
                Run($"header/comments/single-gap/{v}/{g}", () => HeaderCommentsLoad(v, g, false));
            }
            Run($"header/comments/all-gaps/{v}", () => HeaderCommentsLoad(v, -1, false));
            Run($"header/comments/crlf/{v}", () => HeaderCommentsLoad(v, -1, true));
            Run($"header/comments/none/{v}", () => HeaderCommentsLoad(v, -2, false));
            foreach (int malformed in Enumerable.Range(0, 4))
            {
                int m = malformed;
                Run($"header/comments/truncated/{v}/{m}", () => HeaderCommentsTruncated(v, m));
            }
        }
    }

    private static List<(short Code, object Value)> HeaderCommentPairs(DxfVersion version) => new()
    {
        (0, "SECTION"), (2, "HEADER"),
        (9, "$ACADVER"), (1, HeaderVersion(version)), (9, "$DWGCODEPAGE"), (3, "ANSI_1252"),
        (9, "$ANGBASE"), (50, 15.0), (9, "$INSBASE"), (10, 1.25), (20, -2.5), (30, 3.75),
        (9, "$UCSORG"), (10, 10.0), (20, 20.0), (30, 30.0),
        (9, "$UCSXDIR"), (10, 1.0), (20, 0.0), (30, 0.0),
        (9, "$UCSYDIR"), (10, 0.0), (20, 1.0), (30, 0.0),
        // Structural-looking text is legal as a value, not a record marker.
        (9, "$PROJECTNAME"), (1, "ENDSEC"), (9, "$HYPERLINKBASE"), (1, "EOF"),
        (9, "$USERI1"), (70, (short)-12), (9, "$USERR1"), (40, 1.25),
        (9, "$LIMMIN"), (10, -1.25), (20, -2.5),
        (9, "$EXTMIN"), (10, -10.0), (20, -20.0), (30, -30.0),
        (9, "$DIMDLI"), (40, 0.25), (9, "$DIMPOST"), (1, "ignored dimension override"),
        (9, "$LASTSAVEDBY"), (1, "comments test"), (9, "$INSUNITS"), (70, (short)4)
    };

    private static string HeaderCommentsText(DxfVersion version, int gap, bool crlf)
    {
        var text = new StringBuilder();
        string newline = crlf ? "\r\n" : "\n";
        void T(short code, object value) => text.Append(code.ToString(CultureInfo.InvariantCulture)).Append(newline)
            .Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append(newline);
        var pairs = HeaderCommentPairs(version);
        for (int i = 0; i < pairs.Count; i++)
        {
            T(pairs[i].Code, pairs[i].Value);
            if (i >= 1 && (i == gap || gap == -1))
            {
                // None of these strings may be treated as variable names or structure.
                foreach (string comment in new[] { "", "ENDSEC", "EOF", "$ACADVER", "AC1009", "999" })
                    T(999, comment);
            }
        }
        T(0, "ENDSEC"); T(0, "SECTION"); T(2, "ENTITIES");
        T(0, "LINE"); T(5, "200"); T(100, "AcDbEntity"); T(8, "0"); T(100, "AcDbLine");
        T(10, 1.0); T(20, 2.0); T(30, 3.0); T(11, 4.0); T(21, 5.0); T(31, 6.0);
        T(0, "ENDSEC"); T(0, "EOF");
        return text.ToString();
    }

    private static void CheckHeaderCommentsDocument(DxfDocument doc, DxfVersion version, bool roundTripped = false)
    {
        Equal(version, doc.DrawingVariables.AcadVer, "Comments changed the actual version");
        Near(15, doc.DrawingVariables.Angbase, "Comments changed a modeled scalar");
        Equal(new Vector3(1.25, -2.5, 3.75), doc.DrawingVariables.InsBase, "Comments changed a modeled vector");
        Equal(new Vector3(10, 20, 30), doc.DrawingVariables.CurrentUCS.Origin, "Comments changed UCS origin");
        Equal(Vector3.UnitX, doc.DrawingVariables.CurrentUCS.XAxis, "Comments changed UCS X axis");
        Equal(Vector3.UnitY, doc.DrawingVariables.CurrentUCS.YAxis, "Comments changed UCS Y axis");
        Equal("ENDSEC", (string)CustomHeaderValue(doc, "$PROJECTNAME").Value, "Literal ENDSEC value changed");
        Equal("EOF", (string)CustomHeaderValue(doc, "$HYPERLINKBASE").Value, "Literal EOF value changed");
        Equal((short)-12, (short)CustomHeaderValue(doc, "$USERI1").Value, "Custom integer changed");
        Equal(1.25, (double)CustomHeaderValue(doc, "$USERR1").Value, "Custom real changed");
        Equal(new Vector2(-1.25, -2.5), (Vector2)CustomHeaderValue(doc, "$LIMMIN").Value, "Custom 2D vector changed");
        Equal(new Vector3(-10, -20, -30), (Vector3)CustomHeaderValue(doc, "$EXTMIN").Value, "Custom 3D vector changed");
        Check(!doc.DrawingVariables.ContainsCustomVariable("$DIMDLI") && !doc.DrawingVariables.ContainsCustomVariable("$DIMPOST"),
            "Comments altered the existing dimension-override policy.");
        // Existing AC1015 writer policy omits LASTSAVEDBY; a reload uses its normal default.
        Equal(roundTripped && version == DxfVersion.AutoCad2000 ? Environment.UserName : "comments test",
            doc.DrawingVariables.LastSavedBy, "Comments changed last-saved-by");
        Equal((short)4, (short)doc.DrawingVariables.InsUnits, "Comments changed units");
        var line = doc.Entities.Lines.Single();
        Equal(new Vector3(1, 2, 3), line.StartPoint, "Comments disrupted the following section");
        Equal(new Vector3(4, 5, 6), line.EndPoint, "Comments disrupted the following entity");
    }

    private static void HeaderCommentsLoad(DxfVersion version, int gap, bool crlf)
    {
        string fixture = HeaderCommentsText(version, gap, crlf);
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(fixture));
        Equal(version, DxfDocument.CheckDxfFileVersion(input, out bool binary), "Commented version probe");
        Check(!binary, "Commented text was detected as binary."); input.Position = 0;
        DxfDocument doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid HEADER comments prevented loading.");
        CheckHeaderCommentsDocument(doc, version); Check(input.CanRead, "Comments closed caller stream.");
        foreach (bool transport in new[] { false, true })
        {
            using var output = new MemoryStream(); Check(doc.Save(output, transport), "Commented document failed to save.");
            output.Position = 0; DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Commented document reload failed.");
            CheckHeaderCommentsDocument(loaded, version, true);
        }
        if (gap == -1 && !crlf)
            File.WriteAllText(Path.Combine(ArtifactDirectory, $"header-comments-{version}.dxf"), fixture, new UTF8Encoding(false));
    }

    private static void HeaderCommentsTruncated(DxfVersion version, int scenario)
    {
        string prefix = "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\n" + HeaderVersion(version) + "\n";
        string tail = scenario switch
        {
            0 => "999\n", // Missing comment value, not an empty string value.
            1 => "999\ncomplete comment\n", // No ENDSEC/EOF.
            2 => "9\n$USERI1\n999\ncomment instead of a required value\n",
            _ => "9\n$INSBASE\n10\n1\n999\n" // Truncated comment within vector.
        };
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(prefix + tail));
#if DEBUG
        Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Truncated HEADER/comment was accepted.");
#endif
        Check(input.CanRead, "Malformed comment closed caller stream.");
    }
}
