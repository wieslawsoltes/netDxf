using System.Globalization;
using System.Text;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawDimensionStyleNameTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (string name in new[] { "", "OPEN_ARROW", "00aB", "  a0  ", "flèche" })
                {
                    DxfVersion v = version; bool b = binary; string n = name;
                    Run($"raw-dimblk/name/{v}/{b}/{n}", () => RawDimensionStyleName(v, b, n));
                }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"raw-dimblk/context-boundaries/{b}", () => RawDimStyleContexts(b));
            Run($"raw-dimblk/strict-handles/{b}", () => RawDimStyleStrictHandles(b));
            Run($"raw-dimblk/scoped-edits/{b}", () => RawDimStyleEdits(b));
        }
        Run("raw-dimblk/factory-and-placement", RawDimStyleFactory);
        Run("raw-dimblk/binary-multiline-preflight", RawDimStyleMultiline);
    }

    private static List<(short Code, object Value)> RawDimStylePairs(DxfVersion version, string arrowName)
        => new()
        {
            (0, "SECTION"), (2, "HEADER"), (9, "$ACADVER"), (1, HeaderVersion(version)),
            (9, "$DWGCODEPAGE"), (3, "ANSI_1252"), (0, "ENDSEC"),
            (0, "SECTION"), (2, "TABLES"), (0, "TABLE"), (2, "DIMSTYLE"),
            (5, "10"), (330, "0"), (100, "AcDbSymbolTable"), (70, (short)1),
            (0, "DIMSTYLE"), (105, "11"), (330, "10"), (100, "AcDbSymbolTableRecord"),
            (100, "AcDbDimStyleTableRecord"), (2, "Style"), (70, (short)0),
            (5, arrowName), (6, "OPEN_ARROW_1"), (7, "OPEN_ARROW_2"),
            (0, "ENDTAB"), (0, "ENDSEC"), (0, "SECTION"), (2, "ENTITIES"),
            (0, "LINE"), (5, "000aF"), (100, "AcDbEntity"), (8, "0"), (100, "AcDbLine"),
            (10, 1.0), (20, 2.0), (30, 3.0), (11, 4.0), (21, 5.0), (31, 6.0),
            (0, "ENDSEC"), (0, "EOF")
        };

    // Direct transport encoding, deliberately independent of DxfTag's context validation
    // and both production code-value writers. This also builds malformed-context cases.
    private static byte[] RawDimStyleBytes(IEnumerable<(short Code, object Value)> pairs,
        DxfVersion version, bool binary)
    {
        Encoding encoding = version >= DxfVersion.AutoCad2007 ? new UTF8Encoding(false, true) :
            Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        using var stream = new MemoryStream();
        if (binary)
        {
            using var writer = new BinaryWriter(stream, encoding, true);
            writer.Write(BinarySentinel);
            foreach (var (code, value) in pairs)
            {
                writer.Write(code);
                switch (value)
                {
                    case string text: writer.Write(encoding.GetBytes(text)); writer.Write((byte)0); break;
                    case short integer: writer.Write(integer); break;
                    case double real: writer.Write(real); break;
                    default: throw new InvalidOperationException("Unexpected DIMSTYLE fixture value.");
                }
            }
        }
        else
        {
            var text = new StringBuilder();
            foreach (var (code, value) in pairs)
                text.Append(code.ToString(CultureInfo.InvariantCulture)).Append('\n')
                    .Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
            stream.Write(encoding.GetBytes(text.ToString()));
        }
        return stream.ToArray();
    }

    private static void RawDimensionStyleName(DxfVersion version, bool binary, string name)
    {
        byte[] bytes = RawDimStyleBytes(RawDimStylePairs(version, name), version, binary);
        var raw = LoadRaw(bytes);
        var style = FindRawRecord(raw, "TABLES", "DIMSTYLE");
        DxfTag arrow = style.Content.Single(t => t.Code == 5);
        Equal(name, (string)arrow.Value, "DIMBLK block name was normalized as a handle");
        Equal(DxfTagValueType.String, arrow.ValueType, "DIMBLK primitive type");
        Equal(DxfHandleKind.None, arrow.HandleKind, "DIMBLK incorrectly classified as identity");
        Equal(DxfHandleKind.ObjectIdentity, style.Content.Single(t => t.Code == 105).HandleKind, "DIMSTYLE identity");
        Equal("AF", (string)FindRawRecord(raw, "ENTITIES", "LINE").Content.Single(t => t.Code == 5).Value, "LINE handle normalization regressed");
        Check(bytes.SequenceEqual(SaveRaw(raw)), "Original DIMSTYLE bytes changed");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, !binary)).Tags);
        var rewritten = raw.WithTags(raw.Tags);
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(rewritten, binary)).Tags);
        CheckRawRecordPartition(raw);
        if (name == "00aB")
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raw-dimblk-{version}-{binary}.dxf"), SaveRaw(rewritten, binary));
    }

    private static void RawDimStyleContexts(bool binary)
    {
        var pairs = RawDimStylePairs(DxfVersion.AutoCad2018, "00aB");
        int arrow = pairs.FindIndex(t => t.Code == 5 && Equals(t.Value, "00aB"));
        pairs.InsertRange(arrow, new (short, object)[]
        {
            (102, "{VENDOR"), (102, "{NESTED"), (5, "00ab"), (102, "}"),
            (5, "00cd"), (102, "}"), (5, ""), (1001, "APP"), (1005, "00ef")
        });
        // XData consumes the rest of this record: the following code 5 must be a handle.
        var raw = LoadRaw(RawDimStyleBytes(pairs, DxfVersion.AutoCad2018, binary));
        var values = FindRawRecord(raw, "TABLES", "DIMSTYLE").Content.Where(t => t.Code == 5).ToArray();
        Equal("AB", (string)values[0].Value, "Nested application data must not become DIMBLK");
        Equal("CD", (string)values[1].Value, "Outer application data must remain a handle");
        Equal("", (string)values[2].Value, "Control-group close did not restore DIMBLK context");
        Equal(DxfTagValueType.String, values[2].ValueType, "Restored DIMBLK type");
        Equal("AB", (string)values[3].Value, "XData did not end the DIMBLK exception");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, !binary)).Tags);
        pairs = RawDimStylePairs(DxfVersion.AutoCad2018, "OPEN_ARROW");
        // Case-insensitive structural matching, but names remain exact.
        for (int i = 0; i < pairs.Count; i++)
            if (pairs[i].Code is 0 or 2 && pairs[i].Value is string text)
                pairs[i] = (pairs[i].Code, text.ToLowerInvariant());
        var lower = LoadRaw(RawDimStyleBytes(pairs, DxfVersion.AutoCad2018, binary));
        Equal("OPEN_ARROW", (string)lower.Tags.Single(t => t.Code == 5 && t.ValueType == DxfTagValueType.String).Value, "Structural casing changed DIMBLK");
        // Comments are not structural boundaries. Binary fixtures omit comments by design.
        if (!binary)
        {
            pairs.Insert(2, (999, "DIMSTYLE"));
            pairs.Insert(pairs.FindIndex(t => Equals(t.Value, "OPEN_ARROW")), (999, "ENDTAB"));
            LoadRaw(RawDimStyleBytes(pairs, DxfVersion.AutoCad2018, false));
        }
    }

    private static void RawDimStyleStrictHandles(bool binary)
    {
        void Reject(List<(short Code, object Value)> tags)
        {
            byte[] bytes = RawDimStyleBytes(tags, DxfVersion.AutoCad2018, binary);
            if (binary) Throws<InvalidDataException>(() => LoadRaw(bytes));
            else Throws<FormatException>(() => LoadRaw(bytes));
        }
        foreach (string value in new[] { "", "NOT_A_HANDLE" })
        {
            var tags = RawDimStylePairs(DxfVersion.AutoCad2018, "OPEN_ARROW");
            int tableHandle = tags.FindIndex(t => t.Code == 5);
            tags[tableHandle] = (5, value); Reject(tags);
            tags = RawDimStylePairs(DxfVersion.AutoCad2018, "OPEN_ARROW");
            int lineHandle = tags.FindLastIndex(t => t.Code == 5);
            tags[lineHandle] = (5, value); Reject(tags);
            tags = RawDimStylePairs(DxfVersion.AutoCad2018, "OPEN_ARROW");
            int identity = tags.FindIndex(t => t.Code == 105);
            tags[identity] = (105, value); Reject(tags);
            foreach (var prefix in new[]
            {
                new (short, object)[] { (102, "{VENDOR") },
                new (short, object)[] { (1001, "APP") }
            })
            {
                tags = RawDimStylePairs(DxfVersion.AutoCad2018, value);
                int arrow = tags.FindLastIndex(t => t.Code == 5 && !Equals(t.Value, "000aF"));
                tags.InsertRange(arrow, prefix); Reject(tags);
            }
        }
        foreach (string context in new[] { "wrong-table", "wrong-record", "no-table", "after-endtab", "wrong-section" })
        {
            var tags = RawDimStylePairs(DxfVersion.AutoCad2018, "NOT_A_HANDLE");
            if (context == "wrong-table") tags[10] = (2, "LAYER");
            if (context == "wrong-record") tags[15] = (0, "LAYER");
            if (context == "no-table") tags.RemoveRange(9, 6);
            if (context == "after-endtab") tags.Insert(15, (0, "ENDTAB"));
            if (context == "wrong-section") tags[8] = (2, "ENTITIES");
            Reject(tags);
        }
    }

    private static void RawDimStyleEdits(bool binary)
    {
        var raw = LoadRaw(RawDimStyleBytes(RawDimStylePairs(DxfVersion.AutoCad2018, ""), DxfVersion.AutoCad2018, binary));
        var record = FindRawRecord(raw, "TABLES", "DIMSTYLE");
        var replacement = record.Tags.Select(t => t.Code == 5 ? DxfTag.CreateDimensionStyleArrowName("00aB") : t).ToArray();
        var edited = raw.WithRecord(record, replacement);
        AssertOutsideRecordUnchanged(raw, record, edited, replacement.Length);
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !binary)).Tags);
        Throws<ArgumentException>(() => raw.WithRecord(record,
            record.Tags.Select(t => t.Code == 5 ? new DxfTag(5, "AB") : t)));
        // Moving a DIMBLK string into LINE or an application control group cannot bypass validation.
        var line = FindRawRecord(raw, "ENTITIES", "LINE");
        Throws<ArgumentException>(() => raw.WithRecord(line,
            line.Tags.Select(t => t.Code == 5 ? DxfTag.CreateDimensionStyleArrowName("") : t)));
        var tags = record.Tags.ToList();
        tags.Insert(tags.FindIndex(t => t.Code == 5), new DxfTag(102, "{VENDOR"));
        Throws<ArgumentException>(() => raw.WithRecord(record, tags));
        Check(raw.HasOriginalBytes, "Failed or successful edits modified source snapshot");
    }

    private static void RawDimStyleFactory()
    {
        Throws<ArgumentNullException>(() => DxfTag.CreateDimensionStyleArrowName(null!));
        Throws<ArgumentException>(() => DxfTag.CreateDimensionStyleArrowName("x\0y"));
        Throws<ArgumentException>(() => new DxfTag(5, ""));
        Throws<ArgumentException>(() => new DxfTag(5, "OPEN_ARROW"));
        Equal(DxfTagValueType.Handle, DxfGroupCode.GetValueType(5), "Context-free classifier changed");
        var name = DxfTag.CreateDimensionStyleArrowName("000aB");
        Equal((short)5, name.Code, "Factory code");
        Equal("000aB", (string)name.Value, "Factory normalized a name");
        Equal(DxfTagValueType.String, name.ValueType, "Factory type");
        Equal(DxfHandleKind.None, name.HandleKind, "Factory identity category");
    }

    private static void RawDimStyleMultiline()
    {
        var raw = LoadRaw(RawDimStyleBytes(RawDimStylePairs(DxfVersion.AutoCad2018, "line1\nline2"), DxfVersion.AutoCad2018, true));
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), true)).Tags);
        using var destination = new MemoryStream(new byte[] { 1, 2, 3 }, true);
        Throws<NotSupportedException>(() => raw.Save(destination, false));
        Check(destination.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Text preflight partially wrote destination");
    }
}
