using System.Globalization;
using System.Text;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawDocumentTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"raw-document/exact-source/{v}/{b}", () => RawDocumentExact(v, b));
                Run($"raw-document/normalized-tags/{v}/{b}", () => RawDocumentNormalize(v, b));
                Run($"raw-document/unknown-edit/{v}/{b}", () => RawDocumentEdit(v, b));
                Run($"raw-document/section-indexes/{v}/{b}", () => RawDocumentSections(v, b));
                Run($"raw-document/nonseekable/{v}/{b}", () => RawDocumentNonseekable(v, b));
                Run($"raw-document/limits/{v}/{b}", () => RawDocumentLimits(v, b));
                Run($"raw-document/known-semantics/{v}/{b}", () => RawDocumentKnown(v, b));
                Run($"raw-document/profile-edits/{v}/{b}", () => RawDocumentProfile(v, b));
            }
        foreach (DxfVersion version in SupportedVersions)
        {
            DxfVersion v = version;
            Run($"raw-document/comments/{v}", () => RawDocumentComments(v));
            Run($"raw-document/binary-multiline-string/{v}", () => RawDocumentMultiline(v));
            Run($"raw-document/bom-and-newlines/{v}", () => RawDocumentLexical(v));
        }
        foreach (int cp in new[] { 1250, 1251, 1252, 932 })
            foreach (bool binary in new[] { false, true })
            {
                int c = cp; bool b = binary;
                Run($"raw-document/code-page/{cp}/{b}", () => RawDocumentCodePage(c, b));
            }
        foreach (int scenario in Enumerable.Range(0, 20))
        {
            int s = scenario;
            Run($"raw-document/malformed-structure/{s}", () => RawDocumentMalformed(s));
        }
        Run("raw-document/encoding-and-binary-rejection", RawDocumentInvalidEncoding);
        Run("raw-document/immutable-snapshots", RawDocumentIsolation);
        Run("raw-document/cancellation-and-stream-errors", RawDocumentCancellation);
        Run("raw-document/options", RawDocumentOptions);
        Run("raw-document/normalized-byte-budgets", RawDocumentOutputBudgets);
        Run("raw-document/bootstrap-unicode-budget", RawDocumentBootstrapBudget);
    }

    private static List<DxfTag> RawFixtureTags(DxfVersion version, bool comments = false)
    {
        var tags = new List<DxfTag>();
        void T(short code, object value) => tags.Add(new DxfTag(code, value));
        void Section(string name) { T(0, "SECTION"); T(2, name); }
        if (comments) T(999, "leading comment");
        Section("HEADER"); T(9, "$ACADVER"); T(1, HeaderVersion(version));
        T(9, "$DWGCODEPAGE"); T(3, "ANSI_1252"); T(9, "$HANDSEED"); T(5, "ABCD");
        T(9, "$PROJECTNAME"); T(1, "ENDSEC"); T(0, "ENDSEC");
        Section("CLASSES"); T(0, "CLASS"); T(1, "FUTURE_ENTITY"); T(2, "AcDbFutureEntity");
        T(3, "Future application"); T(90, 19); T(91, 1); T(280, (short)0); T(281, (short)1); T(0, "ENDSEC");
        Section("TABLES"); T(0, "TABLE"); T(2, "FUTURE_TABLE"); T(5, "10");
        T(0, "FUTURE_ENTRY"); T(5, "11"); T(2, "entry"); T(49, 1.25); T(49, 2.5); T(0, "ENDTAB"); T(0, "ENDSEC");
        Section("BLOCKS"); T(0, "BLOCK"); T(5, "20"); T(2, "CustomBlock");
        T(0, "FUTURE_ENTITY"); T(5, "21"); T(330, "20"); T(1, "block payload"); T(0, "ENDBLK"); T(5, "22"); T(0, "ENDSEC");
        Section("ENTITIES"); T(0, "LINE"); T(5, "100"); T(100, "AcDbEntity"); T(8, "0");
        T(100, "AcDbLine"); T(10, 1e-20); T(20, -0.0); T(30, 0.0); T(11, 1.0); T(21, 2.0); T(31, 3.0);
        // The SECTION entity must not be confused with a nested file section.
        T(0, "SECTION"); T(5, "101"); T(100, "AcDbEntity"); T(100, "AcDbSection"); T(2, "Cut plane");
        T(0, "FUTURE_ENTITY"); T(5, "102"); T(102, "{ACAD_REACTORS"); T(330, "201"); T(102, "}");
        T(330, "200"); T(100, "AcDbFutureEntity"); T(320, "FEDCBA9876543210"); T(340, "202"); T(350, "203"); T(360, "204");
        T(160, long.MinValue); T(90, int.MaxValue); T(70, short.MinValue); T(290, true);
        T(310, new byte[] { 0, 255, 128, 127 }); T(310, Array.Empty<byte>());
        T(1, version >= DxfVersion.AutoCad2007 ? "Zażółć 東京" : "Za\\U+017C\\U+00F3\\U+0142\\U+0107 \\U+6771\\U+4EAC");
        if (comments) { T(999, "EOF"); T(999, "ENDSEC"); }
        T(1001, "APP"); T(1002, "{"); T(1004, new byte[] { 0, 255 }); T(1005, "FFFFFFFFFFFFFFFF");
        T(1040, double.Epsilon); T(1070, (short)-1); T(1071, int.MinValue); T(1002, "}"); T(0, "ENDSEC");
        Section("OBJECTS"); T(0, "FUTURE_OBJECT"); T(5, "200"); T(102, "{ACAD_XDICTIONARY"); T(360, "204"); T(102, "}");
        T(100, "FutureObjectClass"); T(1, "SECTION"); T(3, "ENDSEC"); T(300, "EOF"); T(0, "ENDSEC");
        Section("ACDSDATA"); T(0, "ACDSRECORD"); T(90, 2); T(101, "opaque schema"); T(310, new byte[] { 1, 2, 3 }); T(0, "ENDSEC");
        Section("VENDOR_SECTION"); T(0, "VENDOR_RECORD"); T(1, "untouched"); T(460, double.MaxValue); T(0, "ENDSEC");
        T(0, "EOF"); return tags;
    }

    // This fixture encoder is independent of the production code-value writer.
    private static byte[] RawFixtureBytes(IEnumerable<DxfTag> tags, bool binary, Encoding? encoding = null, string newline = "\n")
    {
        encoding ??= new UTF8Encoding(false, true);
        using var stream = new MemoryStream();
        if (binary)
        {
            using var writer = new BinaryWriter(stream, encoding, true);
            writer.Write(BinarySentinel);
            foreach (DxfTag tag in tags)
            {
                writer.Write(tag.Code);
                switch (tag.Value)
                {
                    case string text: writer.Write(encoding.GetBytes(text)); writer.Write((byte)0); break;
                    case byte[] data: writer.Write(checked((byte)data.Length)); writer.Write(data); break;
                    case double d: writer.Write(d); break;
                    case short s: writer.Write(s); break;
                    case int i: writer.Write(i); break;
                    case long l: writer.Write(l); break;
                    case bool b: writer.Write((byte)(b ? 1 : 0)); break;
                    default: throw new InvalidOperationException("Unexpected fixture type.");
                }
            }
        }
        else
        {
            var builder = new StringBuilder();
            foreach (DxfTag tag in tags)
            {
                string value = tag.Value switch
                {
                    byte[] data => Convert.ToHexString(data),
                    double d => d == 0 && BitConverter.DoubleToInt64Bits(d) < 0 ? "-0.0" : d.ToString("G17", CultureInfo.InvariantCulture),
                    bool b => b ? "1" : "0",
                    _ => Convert.ToString(tag.Value, CultureInfo.InvariantCulture)!
                };
                builder.Append(tag.Code.ToString(CultureInfo.InvariantCulture)).Append(newline).Append(value).Append(newline);
            }
            stream.Write(encoding.GetBytes(builder.ToString()));
        }
        return stream.ToArray();
    }

    private static DxfRawDocument LoadRaw(byte[] data, DxfRawOptions? options = null)
    {
        using var input = new MemoryStream(data);
        var raw = DxfRawDocument.Load(input, options);
        Check(input.CanRead, "Raw loader closed the caller stream.");
        return raw;
    }

    private static byte[] SaveRaw(DxfRawDocument raw, bool? binary = null)
    {
        using var stream = new MemoryStream();
        if (binary.HasValue) raw.Save(stream, binary.Value); else raw.Save(stream);
        Check(stream.CanWrite, "Raw saver closed the caller stream."); return stream.ToArray();
    }

    private static void SameRawTags(IReadOnlyList<DxfTag> expected, IReadOnlyList<DxfTag> actual)
    {
        Equal(expected.Count, actual.Count, "Raw tag count");
        for (int i = 0; i < expected.Count; i++)
        {
            Equal(expected[i].Code, actual[i].Code, "Raw tag order/code " + i);
            Equal(expected[i].ValueType, actual[i].ValueType, "Raw value type " + i);
            object value = expected[i].Value, other = actual[i].Value;
            if (value is byte[] bytes) Check(bytes.SequenceEqual((byte[])other), "Raw binary payload changed at " + i);
            else if (value is double d) SameDoubleBits(d, (double)other, "Raw numeric bits " + i);
            else Equal(value, other, "Raw tag value " + i);
        }
    }

    private static void RawDocumentExact(DxfVersion version, bool binary)
    {
        byte[] input = RawFixtureBytes(RawFixtureTags(version, !binary), binary);
        byte[] expected = (byte[])input.Clone();
        var raw = LoadRaw(input); Equal(version, raw.Version, "Raw declared version"); Equal(binary, raw.IsBinary, "Raw detected transport");
        Check(raw.HasOriginalBytes, "Loaded raw document lost its original bytes.");
        Array.Fill(input, (byte)99);
        Check(expected.SequenceEqual(SaveRaw(raw)), "Same-format save was not byte-identical or retained caller storage.");
        Check(expected.SequenceEqual(SaveRaw(raw)), "Repeated raw save mutated the source.");
    }

    private static void RawDocumentNormalize(DxfVersion version, bool binary)
    {
        var tags = RawFixtureTags(version);
        var raw = LoadRaw(RawFixtureBytes(tags, binary)); SameRawTags(tags, raw.Tags);
        var edited = raw.WithTags(raw.Tags); Check(!edited.HasOriginalBytes, "Edited snapshot incorrectly promises original bytes.");
        foreach (bool transport in new[] { false, true })
        {
            var reloaded = LoadRaw(SaveRaw(edited, transport)); SameRawTags(raw.Tags, reloaded.Tags);
            SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, transport)).Tags);
        }
    }

    private static void RawDocumentEdit(DxfVersion version, bool binary)
    {
        var raw = LoadRaw(RawFixtureBytes(RawFixtureTags(version), binary));
        var values = raw.Tags.ToArray(); int index = Array.FindIndex(values, t => t.Code == 10);
        values[index] = new DxfTag(10, -1.2345678901234567);
        var edited = raw.WithTags(values); SameDoubleBits(1e-20, (double)raw.Tags[index].Value, "Editing changed original");
        SameRawTags(values, LoadRaw(SaveRaw(edited, !binary)).Tags);
    }

    private static void RawDocumentSections(DxfVersion version, bool binary)
    {
        var raw = LoadRaw(RawFixtureBytes(RawFixtureTags(version), binary));
        string[] names = { "HEADER", "CLASSES", "TABLES", "BLOCKS", "ENTITIES", "OBJECTS", "ACDSDATA", "VENDOR_SECTION" };
        Check(names.SequenceEqual(raw.Sections.Select(s => s.Name)), "Unknown or named section disappeared.");
        foreach (var section in raw.Sections)
        {
            Equal("SECTION", (string)raw.Tags[section.StartTagIndex].Value, "Section start index");
            Equal("ENDSEC", (string)raw.Tags[section.EndTagIndex - 1].Value, "Section end index");
            SameRawTags(raw.Tags.Skip(section.StartTagIndex + 2).Take(section.EndTagIndex - section.StartTagIndex - 3).ToArray(), section.Content);
            Throws<ArgumentOutOfRangeException>(() => _ = section.Content[-1]);
            Throws<ArgumentOutOfRangeException>(() => _ = section.Content[section.Content.Count]);
        }
        Check(raw.Sections.Single(s => s.Name == "ENTITIES").Content.Any(t => t.Code == 0 && Equals(t.Value, "SECTION")),
            "SECTION entity was confused with section grammar.");
    }

    private static void RawDocumentNonseekable(DxfVersion version, bool binary)
    {
        byte[] bytes = RawFixtureBytes(RawFixtureTags(version), binary);
        using var input = new RawNonseekableStream(bytes, false, 3);
        var raw = DxfRawDocument.Load(input); Check(!input.WasDisposed, "Nonseekable input was closed.");
        using var output = new RawNonseekableStream(Array.Empty<byte>(), true, 3);
        raw.Save(output); Check(!output.WasDisposed, "Nonseekable output was closed.");
        Check(bytes.SequenceEqual(output.Bytes), "Nonseekable raw identity failed.");
        using var offset = new MemoryStream(new byte[] { 1, 2, 3, 4 }.Concat(bytes).ToArray()); offset.Position = 4;
        Check(bytes.SequenceEqual(SaveRaw(DxfRawDocument.Load(offset))), "Nonzero input offset was included as DXF data.");
    }

    private static void RawDocumentLimits(DxfVersion version, bool binary)
    {
        var tags = RawFixtureTags(version); byte[] bytes = RawFixtureBytes(tags, binary);
        var raw = LoadRaw(bytes, new DxfRawOptions(bytes.Length, tags.Count));
        Check(bytes.SequenceEqual(SaveRaw(raw)), "Inclusive input/tag budget boundary failed.");
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(bytes.Length - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(bytes.Length, tags.Count - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(bytes.Length, tags.Count, 2)));
        using var limited = new RawNonseekableStream(bytes, false, 3);
        Throws<InvalidDataException>(() => DxfRawDocument.Load(limited, new DxfRawOptions(10)));
        Check(limited.ReadCount <= 11 && !limited.WasDisposed, "Input quota read unbounded extra data or closed the source.");
    }

    private static void RawDocumentKnown(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        document.Comments.Clear(); // This control is deliberately comment-free for both output transports.
        document.Entities.Add(new Line(new Vector3(1e-20, 2, 3), new Vector3(4, 5, 6)));
        using var input = new MemoryStream(); Check(document.Save(input, binary), "Known raw control failed to save."); input.Position = 0;
        var raw = DxfRawDocument.Load(input); var tags = raw.Tags.ToArray();
        bool line = false; int changed = 0;
        for (int i = 0; i < tags.Length; i++)
        {
            if (tags[i].Code == 0) line = Equals(tags[i].Value, "LINE");
            if (line && tags[i].Code == 11) { tags[i] = new DxfTag(11, 7.123456789012345); changed++; }
        }
        Equal(1, changed, "Known raw control edit count");
        byte[] output = SaveRaw(raw.WithTags(tags), !binary);
        using var stream = new MemoryStream(output);
        var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Raw-edited known file failed semantic loading.");
        var actual = loaded.Entities.Lines.Single();
        SameDoubleBits(1e-20, actual.StartPoint.X, "Raw edit changed untouched coordinate");
        SameDoubleBits(7.123456789012345, actual.EndPoint.X, "Raw edit did not persist");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raw-known-{version}-{!binary}.dxf"), output);
    }

    private static void RawDocumentProfile(DxfVersion version, bool binary)
    {
        var raw = LoadRaw(RawFixtureBytes(RawFixtureTags(version), binary));
        var tags = raw.Tags.ToArray(); int v = Array.FindIndex(tags, t => t.Code == 9 && Equals(t.Value, "$ACADVER")) + 1;
        tags[v] = new DxfTag(1, version == DxfVersion.AutoCad2000 ? "AC1032" : "AC1015");
        Throws<NotSupportedException>(() => raw.WithTags(tags));
        tags = raw.Tags.ToArray(); int cp = Array.FindIndex(tags, t => t.Code == 9 && Equals(t.Value, "$DWGCODEPAGE")) + 1;
        tags[cp] = new DxfTag(3, "ANSI_1250"); Throws<NotSupportedException>(() => raw.WithTags(tags));
        Equal(version, raw.Version, "Rejected profile edit mutated source");
    }

    private static void RawDocumentComments(DxfVersion version)
    {
        var tags = RawFixtureTags(version, true); tags.Insert(2, new DxfTag(999, "before section name"));
        var raw = LoadRaw(RawFixtureBytes(tags, false)); SameRawTags(tags, raw.Tags);
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags))).Tags);
        using var output = new MemoryStream(); output.Write(new byte[] { 1, 2, 3 });
        Throws<NotSupportedException>(() => raw.Save(output, true));
        Check(output.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }), "Binary comment rejection wrote partial output.");
        var stripped = raw.WithTags(raw.Tags.Where(t => t.Code != 999));
        SameRawTags(stripped.Tags, LoadRaw(SaveRaw(stripped, true)).Tags);
    }

    private static void RawDocumentMultiline(DxfVersion version)
    {
        var tags = RawFixtureTags(version);
        const string value = "a\r\nb\nc\rd";
        tags.Insert(tags.Count - 2, new DxfTag(300, value));
        byte[] input = RawFixtureBytes(tags, true);
        var raw = LoadRaw(input); SameRawTags(tags, raw.Tags);
        Check(input.SequenceEqual(SaveRaw(raw)), "Multiline binary string lost exact input bytes.");
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags))).Tags);
        using var output = new MemoryStream(); output.WriteByte(42);
        Throws<NotSupportedException>(() => raw.Save(output, false));
        Equal(1L, output.Length, "Multiline text-conversion rejection wrote partial data");
    }

    private static void RawDocumentLexical(DxfVersion version)
    {
        foreach (string newline in new[] { "\r", "\n", "\r\n" })
        {
            byte[] bytes = RawFixtureBytes(RawFixtureTags(version), false, newline: newline);
            string text = Encoding.UTF8.GetString(bytes).Replace("5" + newline + "ABCD", "  5" + newline + "00abcd").TrimEnd('\r', '\n');
            bytes = Encoding.UTF8.GetBytes(text + newline + " \t" + newline);
            if (version >= DxfVersion.AutoCad2007) bytes = new byte[] { 239, 187, 191 }.Concat(bytes).ToArray();
            var raw = LoadRaw(bytes); Check(bytes.SequenceEqual(SaveRaw(raw)), "BOM/padding/line endings/handle spelling changed.");
            SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);
        }
    }

    private static void RawDocumentCodePage(int cp, bool binary)
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2004);
        int c = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$DWGCODEPAGE")) + 1;
        tags[c] = new DxfTag(3, "ANSI_" + cp);
        string text = cp switch { 1250 => "Zażółć", 1251 => "Привет", 932 => "東京", _ => "Résumé €" };
        int p = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$PROJECTNAME")) + 1; tags[p] = new DxfTag(1, text);
        var raw = LoadRaw(RawFixtureBytes(tags, binary, Encoding.GetEncoding(cp)));
        Equal(cp, raw.EncodingCodePage, "Raw code page"); Equal(text, raw.Tags[p].Value, "Raw legacy decoding");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !binary)).Tags);
    }

    private static void RawDocumentMalformed(int scenario)
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018); bool load = false;
        switch (scenario)
        {
            case 0: tags.RemoveAt(tags.Count - 1); break;
            case 1: tags.RemoveAt(tags.Count - 2); break;
            case 2: tags.Add(new DxfTag(1, "after EOF")); break;
            case 3: tags[1] = new DxfTag(1, "HEADER"); break;
            case 4: tags[1] = new DxfTag(2, ""); break;
            case 5: tags[0] = new DxfTag(0, "ENDSEC"); break;
            case 6: tags.Insert(2, new DxfTag(0, "EOF")); break;
            case 7: tags[2] = new DxfTag(9, "$MISSING_VERSION"); break;
            case 8: tags[3] = new DxfTag(3, "AC1032"); break;
            case 9: tags.InsertRange(4, new[] { new DxfTag(9, "$ACADVER"), new DxfTag(1, "AC1032") }); break;
            case 10: tags.InsertRange(6, new[] { new DxfTag(9, "$DWGCODEPAGE"), new DxfTag(3, "ANSI_1252") }); break;
            case 11: tags[3] = new DxfTag(1, "AC1006"); break;
            case 12: tags[3] = new DxfTag(1, "AC9999"); break;
            case 13: tags[5] = new DxfTag(1, "ANSI_1252"); break;
            case 14: tags.InsertRange(tags.Count - 1, tags.Take(11).ToArray()); break;
            case 15: load = true; break;
            case 16: load = true; break;
            case 17: load = true; break;
            case 18: tags.Insert(4, new DxfTag(1, "AC1015")); break;
            case 19: tags.Insert(6, new DxfTag(3, "ANSI_1250")); break;
        }
        if (load)
        {
            byte[] bytes = RawFixtureBytes(tags, scenario == 17);
            if (scenario == 15) bytes = bytes.Take(bytes.Length - 5).ToArray();
            else bytes = bytes.Concat(scenario == 17 ? new byte[] { 0 } : Encoding.UTF8.GetBytes("999\ntrailing tag\n")).ToArray();
            if (scenario == 15) Throws<EndOfStreamException>(() => LoadRaw(bytes));
            else Throws<FormatException>(() => LoadRaw(bytes));
        }
        else if (scenario == 0) Throws<EndOfStreamException>(() => DxfRawDocument.Create(tags));
        else if (scenario == 11 || scenario == 12) Throws<DxfVersionNotSupportedException>(() => DxfRawDocument.Create(tags));
        else Throws<FormatException>(() => DxfRawDocument.Create(tags));
    }

    private static void RawDocumentInvalidEncoding()
    {
        byte[] bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), false);
        int value = Array.IndexOf(bytes, (byte)'u', Array.IndexOf(bytes, (byte)'F'));
        bytes[value] = 255; Throws<DecoderFallbackException>(() => LoadRaw(bytes));
        var legacy = DxfRawDocument.Create(RawFixtureTags(DxfVersion.AutoCad2000));
        var tags = legacy.Tags.ToList(); tags.Insert(tags.Count - 2, new DxfTag(1, "東京"));
        var invalid = legacy.WithTags(tags);
        using var output = new MemoryStream(); output.WriteByte(42);
        Throws<EncoderFallbackException>(() => invalid.Save(output)); Equal(1L, output.Length, "Invalid encoding wrote output");
        tags = RawFixtureTags(DxfVersion.AutoCad2018); tags.Insert(tags.Count - 2, new DxfTag(310, new byte[256]));
        invalid = DxfRawDocument.Create(tags);
        Throws<NotSupportedException>(() => invalid.Save(output, true)); Equal(1L, output.Length, "Unrepresentable chunk wrote output");
        foreach (byte[] bom in new[] { new byte[] { 255, 254 }, new byte[] { 254, 255 }, new byte[] { 0, 0, 254, 255 } })
            Throws<NotSupportedException>(() => LoadRaw(bom.Concat(bytes).ToArray()));
        bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2000), false);
        Throws<NotSupportedException>(() => LoadRaw(new byte[] { 239, 187, 191 }.Concat(bytes).ToArray()));
        tags = RawFixtureTags(DxfVersion.AutoCad2000); tags[5] = new DxfTag(3, "ANSI_1200");
        Throws<NotSupportedException>(() => DxfRawDocument.Create(tags));
        tags[5] = new DxfTag(3, "not-a-codepage"); Throws<NotSupportedException>(() => DxfRawDocument.Create(tags));
    }

    private static void RawDocumentIsolation()
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018); var raw = DxfRawDocument.Create(tags);
        var snapshot = raw.Tags.ToArray(); tags.Clear(); SameRawTags(snapshot, raw.Tags);
        var binary = raw.Tags.First(t => t.ValueType == DxfTagValueType.BinaryData);
        var bytes = (byte[])binary.Value; bytes[0] = 99; Equal((byte)0, ((byte[])binary.Value)[0], "Raw document exposed bytes");
        Throws<NotSupportedException>(() => ((IList<DxfTag>)raw.Tags).Add(new DxfTag(1, "mutate")));
        Throws<NotSupportedException>(() => ((IList<DxfRawSection>)raw.Sections).Clear());
        Throws<ArgumentNullException>(() => DxfRawDocument.Create(null!));
        Throws<ArgumentException>(() => raw.WithTags(new DxfTag[] { null! }));
    }

    private static void RawDocumentCancellation()
    {
        byte[] bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), false);
        using var input = new RawNonseekableStream(bytes, false, 3);
        Throws<OperationCanceledException>(() => DxfRawDocument.Load(input, cancellationToken: new CancellationToken(true)));
        Equal(0, input.ReadCount, "Cancelled load consumed input"); Check(!input.WasDisposed, "Cancelled load closed input");
        var raw = LoadRaw(bytes); using var output = new MemoryStream(); output.WriteByte(42);
        Throws<OperationCanceledException>(() => raw.Save(output, true, new CancellationToken(true)));
        Equal(1L, output.Length, "Cancelled save wrote output");
        using var failing = new RawNonseekableStream(bytes, false, 3) { FailAfter = 12 };
        Throws<IOException>(() => DxfRawDocument.Load(failing)); Check(!failing.WasDisposed, "Failed load closed caller input");
        using var badOutput = new RawNonseekableStream(Array.Empty<byte>(), true, 3) { FailAfter = 0 };
        Throws<IOException>(() => raw.Save(badOutput)); Check(!badOutput.WasDisposed, "Failed save closed caller output");
        Throws<ArgumentNullException>(() => DxfRawDocument.Load(null!)); Throws<ArgumentNullException>(() => raw.Save(null!));
        using var closed = new MemoryStream(); closed.Dispose();
        Throws<ArgumentException>(() => DxfRawDocument.Load(closed)); Throws<ArgumentException>(() => raw.Save(closed));
    }

    private static void RawDocumentOptions()
    {
        Throws<ArgumentOutOfRangeException>(() => new DxfRawOptions(0));
        Throws<ArgumentOutOfRangeException>(() => new DxfRawOptions(maximumTags: 0));
        Throws<ArgumentOutOfRangeException>(() => new DxfRawOptions(maximumStringLength: 0));
        var raw = RawFixtureTags(DxfVersion.AutoCad2018);
        Throws<InvalidDataException>(() => DxfRawDocument.Create(raw, options: new DxfRawOptions(maximumTags: raw.Count - 1)));
        IEnumerable<DxfTag> Infinite() { while (true) yield return new DxfTag(999, "infinite"); }
        Throws<InvalidDataException>(() => DxfRawDocument.Create(Infinite(), options: new DxfRawOptions(maximumTags: 3)));
    }

    private static void RawDocumentOutputBudgets()
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018);
        foreach (bool binary in new[] { false, true })
        {
            byte[] bytes = SaveRaw(DxfRawDocument.Create(tags), binary);
            foreach (int limit in new[] { 22, 100, bytes.Length - 1 })
            {
                var raw = DxfRawDocument.Create(tags, options: new DxfRawOptions(limit));
                using var output = new MemoryStream(); output.WriteByte(42);
                Throws<InvalidDataException>(() => raw.Save(output, binary));
                Equal(1L, output.Length, "Output quota wrote partial destination data");
            }
            var allowed = DxfRawDocument.Create(tags, options: new DxfRawOptions(bytes.Length));
            Equal(bytes.Length, SaveRaw(allowed, binary).Length, "Inclusive normalized byte limit");
        }
    }

    private static void RawDocumentBootstrapBudget()
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018);
        int index = tags.FindIndex(t => t.Code == 9 && Equals(t.Value, "$PROJECTNAME")) + 1;
        tags[index] = new DxfTag(1, new string('東', 40));
        var raw = LoadRaw(RawFixtureBytes(tags, false), new DxfRawOptions(maximumStringLength: 50));
        Equal(new string('東', 40), raw.Tags[index].Value, "Bootstrap imposed byte count as the decoded character budget");
    }

    private sealed class RawNonseekableStream : Stream
    {
        private readonly MemoryStream stream;
        private readonly bool writable;
        private readonly int fragment;
        public int ReadCount { get; private set; }
        public int FailAfter { get; set; } = int.MaxValue;
        public bool WasDisposed { get; private set; }
        public byte[] Bytes => this.stream.ToArray();
        public RawNonseekableStream(byte[] bytes, bool writable, int fragment)
        { this.stream = writable ? new MemoryStream() : new MemoryStream(bytes, false); this.writable = writable; this.fragment = fragment; }
        public override bool CanRead => !this.writable;
        public override bool CanWrite => this.writable;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.ReadCount >= this.FailAfter) throw new IOException("Injected input failure.");
            int n = this.stream.Read(buffer, offset, Math.Min(count, this.fragment)); this.ReadCount += n; return n;
        }
        public override void Write(byte[] buffer, int offset, int count)
        { if (this.stream.Length >= this.FailAfter) throw new IOException("Injected output failure."); this.stream.Write(buffer, offset, count); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { this.WasDisposed = true; if (disposing) this.stream.Dispose(); base.Dispose(disposing); }
    }
}
