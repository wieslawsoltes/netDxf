using System.Security.Cryptography;
using System.Text;
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawR12Tests()
    {
        foreach (var fixture in new[]
        {
            ("ASCII_R12.dxf", "b476d3e53fe24c1db3c701d20b2bebd774f7bd7966b12d81891505b9b29e4d21"),
            ("bin_dxf_r12.dxf", "1e7a904d67bc7036bd3f78a6273cdf28124ba7e8b731604c0593288182f2aa0e")
        })
        {
            var f = fixture;
            Run("raw-r12/external/" + f.Item1, () => RawR12External(f.Item1, f.Item2));
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"raw-r12/authored/{b}", () => RawR12Authored(b));
            Run($"raw-r12/budgets-cancellation-offsets/{b}", () => RawR12Limits(b));
            Run($"raw-r12/dimstyle-names/{b}", () => RawR12DimensionStyle(b));
            Run($"raw-r12/header-not-first/{b}", () => RawR12HeaderNotFirst(b));
            foreach (var cp in new[] { ("ANSI_1250", 1250, "Zażółć"), ("dos932", 932, "東京") })
            {
                var c = cp;
                Run($"raw-r12/codepage/{b}/{c.Item1}", () => RawR12Encoding(b, c.Item1, c.Item2, c.Item3));
            }
        }
        foreach (short code in new short[] { 1000, 1001, 1002, 1003, 1004, 1005, 1010, 1020, 1030, 1040, 1041, 1042, 1070, 1071 })
        {
            short c = code;
            Run($"raw-r12/escaped-code/{c}", () => RawR12EscapedCode(c));
        }
        foreach (int length in new[] { 0, 1, 2, 126, 127, 128, 255 })
        {
            int n = length;
            Run($"raw-r12/chunk-framing/{n}", () => RawR12Chunks(n));
        }
        foreach (int length in new[] { 0, 1, 2 })
        {
            int n = length;
            Run($"raw-r12/truncated-escape/{n}", () => RawR12TruncatedEscape(n));
        }
        foreach (short code in new short[] { -1, 0, 1, 254 })
        {
            short c = code;
            Run($"raw-r12/noncanonical-escape/{c}", () => RawR12BadEscape(c));
        }
        Run("raw-r12/version-framing-consistency", RawR12FramingMismatch);
        Run("raw-r12/comments-explicit-removal", RawR12Comments);
        Run("raw-r12/trailing-data", RawR12Trailing);
        Run("raw-r12/typed-admission-unchanged", RawR12TypedBoundary);
    }

    private static List<DxfTag> RawR12Tags() => new()
    {
        new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, "AC1009"),
        new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
        new(0, "SECTION"), new(2, "ENTITIES"), new(0, "LINE"), new(5, "A"), new(8, "0"),
        new(10, 1e-20), new(20, -0.0), new(30, 0.0), new(11, 1.0), new(21, 2.0), new(31, 3.0),
        new(1001, "R12_TEST"), new(1000, "AC1032 $ACADVER SECTION EOF"), new(1002, "{"),
        new(1004, new byte[] { 0, 255, 1, 255, 0 }), new(1005, "FFFFFFFFFFFFFFFF"),
        new(1040, double.Epsilon), new(1070, short.MinValue), new(1071, int.MinValue), new(1002, "}"),
        new(0, "ENDSEC"), new(0, "EOF")
    };

    // A fixture encoder independent of production framing, including explicit little-endian escapes.
    private static byte[] RawR12Bytes(IEnumerable<DxfTag> tags, bool binary = true, Encoding? encoding = null)
    {
        encoding ??= new UTF8Encoding(false, true);
        if (!binary) return RawFixtureBytes(tags, false, encoding);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, encoding, true);
        writer.Write(BinarySentinel);
        foreach (DxfTag tag in tags)
        {
            if (tag.Code >= 255) { writer.Write((byte)255); writer.Write(tag.Code); }
            else writer.Write(checked((byte)tag.Code));
            switch (tag.Value)
            {
                case string text: writer.Write(encoding.GetBytes(text)); writer.Write((byte)0); break;
                case byte[] bytes: writer.Write(checked((byte)bytes.Length)); writer.Write(bytes); break;
                case double d: writer.Write(d); break;
                case short s: writer.Write(s); break;
                case int i: writer.Write(i); break;
                case long l: writer.Write(l); break;
                case bool b: writer.Write(b); break;
                default: throw new InvalidOperationException("Unexpected R12 fixture value.");
            }
        }
        writer.Flush(); return stream.ToArray();
    }

    private static void RawR12External(string name, string hash)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("tests", "fixtures", "legacy", name));
        Equal(hash, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Pinned R12 input hash");
        var raw = LoadRaw(bytes);
        Equal(DxfVersion.AutoCad12, raw.Version, "R12 declared profile");
        Equal(name.StartsWith("bin_", StringComparison.Ordinal), raw.IsBinary, "R12 detected transport");
        Check(bytes.SequenceEqual(SaveRaw(raw)), "R12 exact source bytes changed.");
        foreach (bool binary in new[] { false, true })
        {
            byte[] normalized = SaveRaw(raw.WithTags(raw.Tags), binary);
            SameRawTags(raw.Tags, LoadRaw(normalized).Tags);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-normalized-{binary}-{name}"), normalized);
        }
        CheckRawRecordPartition(raw);
        var line = raw.Sections.Single(s => s.Name == "ENTITIES").Records.First(r => r.Name == "LINE");
        var replacement = line.Tags.Select(t => t.Code == 10 ? new DxfTag(10, 12.345678901234567) : t).ToArray();
        var edited = raw.WithRecord(line, replacement);
        AssertOutsideRecordUnchanged(raw, line, edited, replacement.Length);
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !raw.IsBinary)).Tags);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, "r12-edited-" + name), SaveRaw(edited, true));
    }

    private static void RawR12Authored(bool binary)
    {
        var tags = RawR12Tags(); byte[] bytes = RawR12Bytes(tags, binary);
        var raw = LoadRaw(bytes); Equal(DxfVersion.AutoCad12, raw.Version, "Authored R12 profile");
        SameRawTags(tags, raw.Tags); Check(bytes.SequenceEqual(SaveRaw(raw)), "Authored exact bytes");
        foreach (bool format in new[] { false, true })
        {
            SameRawTags(tags, LoadRaw(SaveRaw(raw, format)).Tags);
            SameRawTags(tags, LoadRaw(SaveRaw(DxfRawDocument.Create(tags), format)).Tags);
        }
        byte[] actual = SaveRaw(raw.WithTags(raw.Tags), true);
        Check(RawR12Bytes(tags).SequenceEqual(actual), "R12 output disagrees with independently encoded bytes.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"r12-xdata-{binary}.dxf"), SaveRaw(raw.WithTags(tags), binary));
    }

    private static void RawR12EscapedCode(short code)
    {
        var tags = RawR12Tags();
        object sample = RawTagSample(DxfGroupCode.GetValueType(code));
        if (sample is string && code != 1005) sample = "ASCII";
        tags.Insert(tags.Count - 2, new DxfTag(code, sample));
        byte[] expected = RawR12Bytes(tags);
        var raw = LoadRaw(expected); SameRawTags(tags, raw.Tags);
        Check(expected.SequenceEqual(SaveRaw(raw.WithTags(tags), true)), "Escaped group width/value mismatch.");
    }

    private static void RawR12Chunks(int length)
    {
        var tags = RawR12Tags();
        tags.Insert(tags.Count - 2, new DxfTag(1004, Enumerable.Range(0, length).Select(i => (byte)i).ToArray()));
        byte[] expected = RawR12Bytes(tags);
        var raw = LoadRaw(expected); SameRawTags(tags, raw.Tags);
        Check(expected.SequenceEqual(SaveRaw(raw.WithTags(tags), true)), "Chunk length/payload/next record mismatch.");
        // Lengths above 127 test the existing tolerant transport, not standard XData authoring limits.
    }

    private static void RawR12DimensionStyle(bool binary)
    {
        foreach (string name in new[] { "", "_CLOSED", "00aB" })
        {
            var tags = RawR12Tags();
            tags.InsertRange(7, new DxfTag[] { new(0, "SECTION"), new(2, "TABLES"), new(0, "TABLE"), new(2, "DIMSTYLE"),
                new(70, (short)1), new(0, "DIMSTYLE"), new(2, "STANDARD"), new(70, (short)0),
                DxfTag.CreateDimensionStyleArrowName(name), new(0, "ENDTAB"), new(0, "ENDSEC") });
            var raw = LoadRaw(RawR12Bytes(tags, binary)); SameRawTags(tags, raw.Tags);
            SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags), !binary)).Tags);
        }
    }

    private static void RawR12HeaderNotFirst(bool binary)
    {
        var tags = RawR12Tags();
        tags.InsertRange(0, new DxfTag[] { new(0, "SECTION"), new(2, "VENDOR"), new(0, "RECORD"), new(1, "AC1032"), new(0, "ENDSEC") });
        var raw = LoadRaw(RawR12Bytes(tags, binary)); SameRawTags(tags, raw.Tags);
        Equal(DxfVersion.AutoCad12, raw.Version, "Payload strings overrode the actual profile");
    }

    private static void RawR12Encoding(bool binary, string alias, int codePage, string value)
    {
        var tags = RawR12Tags(); tags[5] = new DxfTag(3, alias);
        tags.Insert(tags.Count - 2, new DxfTag(1000, value));
        var raw = LoadRaw(RawR12Bytes(tags, binary, Encoding.GetEncoding(codePage)));
        Equal(codePage, raw.EncodingCodePage, "R12 code page"); SameRawTags(tags, raw.Tags);
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(tags), !binary)).Tags);
    }

    private static void RawR12Limits(bool binary)
    {
        byte[] bytes = RawR12Bytes(RawR12Tags(), binary);
        var raw = LoadRaw(bytes);
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumBytes: bytes.Length - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumTags: raw.Tags.Count - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumStringLength: 5)));
        using var fragmented = new RawNonseekableStream(bytes, false, 1);
        SameRawTags(raw.Tags, DxfRawDocument.Load(fragmented).Tags);
        Check(!fragmented.WasDisposed, "R12 reader closed caller stream.");
        using var cancelled = new MemoryStream(bytes);
        Throws<OperationCanceledException>(() => DxfRawDocument.Load(cancelled, cancellationToken: new CancellationToken(true)));
        Equal(0L, cancelled.Position, "Cancelled R12 read consumed input");
        using var offset = new MemoryStream(new byte[] { 1, 2, 3 }.Concat(bytes).ToArray()); offset.Position = 3;
        SameRawTags(raw.Tags, DxfRawDocument.Load(offset).Tags);
        using var output = new MemoryStream(); output.WriteByte(42);
        Throws<OperationCanceledException>(() => raw.Save(output, !binary, new CancellationToken(true)));
        Equal(1L, output.Length, "Cancelled R12 output changed destination");
    }

    private static byte[] RawR12Partial(byte[] tail) => RawR12Bytes(RawR12Tags().Take(10)).Concat(tail).ToArray();

    private static void RawR12TruncatedEscape(int length)
    {
        byte[] prefix = { 255, 0xEF, 0x03 };
        Throws<EndOfStreamException>(() => LoadRaw(RawR12Partial(prefix.Take(length).ToArray())));
    }

    private static void RawR12BadEscape(short code)
    {
        byte[] tail = { 255, (byte)code, (byte)(code >> 8) };
        Throws<InvalidDataException>(() => LoadRaw(RawR12Partial(tail)));
    }

    private static void RawR12FramingMismatch()
    {
        Throws<FormatException>(() => LoadRaw(RawFixtureBytes(RawR12Tags(), true)));
        foreach (string profile in new[] { "AC1012", "AC1014", "AC1015", "AC1032" })
        {
            var tags = RawR12Tags(); tags[3] = new DxfTag(1, profile);
            Throws<FormatException>(() => LoadRaw(RawR12Bytes(tags)));
        }
    }

    private static void RawR12Comments()
    {
        var tags = RawR12Tags(); tags.Insert(3, new DxfTag(999, "$ACADVER AC1032"));
        var raw = LoadRaw(RawR12Bytes(tags, false)); SameRawTags(tags, raw.Tags);
        using var output = new MemoryStream(); output.WriteByte(42);
        Throws<NotSupportedException>(() => raw.Save(output, true)); Equal(1L, output.Length, "R12 comment rejection wrote output");
        var stripped = raw.WithTags(tags.Where(t => t.Code != 999));
        SameRawTags(stripped.Tags, LoadRaw(SaveRaw(stripped, true)).Tags);
    }

    private static void RawR12Trailing()
    {
        Throws<FormatException>(() => LoadRaw(RawR12Bytes(RawR12Tags()).Concat(new byte[] { 0 }).ToArray()));
        Throws<EndOfStreamException>(() => LoadRaw(RawR12Bytes(RawR12Tags().SkipLast(1))));
    }

    private static void RawR12TypedBoundary()
    {
        Throws<NotSupportedException>(() => new DxfDocument(DxfVersion.AutoCad12));
        using var input = new MemoryStream(RawR12Bytes(RawR12Tags(), false));
        Throws<DxfVersionNotSupportedException>(() => DxfDocument.Load(input));
        Check(input.CanRead, "Unsupported typed load closed caller stream.");
    }
}
