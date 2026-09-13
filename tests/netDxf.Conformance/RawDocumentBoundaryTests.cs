using System.Text;
using System.Globalization;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterRawDocumentBoundaryTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"raw-document/duplicate-vendor-sections/{v}/{b}", () => RawDocumentDuplicateSections(v, b));
                Run($"raw-document/header-not-first/{v}/{b}", () => RawDocumentReorderedHeader(v, b));
                Run($"raw-document/case-preservation/{v}/{b}", () => RawDocumentCase(v, b));
                Run($"raw-document/duplicate-case-header/{v}/{b}", () => RawDocumentAmbiguousHeader(v, b));
                Run($"raw-document/destination-offset/{v}/{b}", () => RawDocumentDestination(v, b));
                Run($"raw-document/independent-raw-fixtures/{v}/{b}", () => RawDocumentRetainFixtures(v, b));
            }
        Run("raw-document/cancellation-during-copy", RawDocumentMidCancellation);
        Run("raw-document/unsupported-value-types", RawDocumentBadTypedInput);
        Run("raw-document/strict-utf8-binary", RawDocumentBinaryUtf8);
    }

    private static void RawDocumentDuplicateSections(DxfVersion v, bool b)
    {
        var tags = RawFixtureTags(v);
        var section = tags.Skip(tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "VENDOR_SECTION")) - 1).SkipLast(1).ToArray();
        tags.InsertRange(tags.Count - 1, section);
        var raw = LoadRaw(RawFixtureBytes(tags, b));
        Equal(2, raw.Sections.Count(s => s.Name == "VENDOR_SECTION"), "Repeated section was collapsed");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !b)).Tags);
    }

    private static void RawDocumentReorderedHeader(DxfVersion v, bool b)
    {
        var tags = RawFixtureTags(v);
        int end = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        var header = tags.Take(end + 1).ToArray(); tags.RemoveRange(0, end + 1);
        tags.InsertRange(tags.Count - 1, header);
        var raw = LoadRaw(RawFixtureBytes(tags, b));
        Equal("CLASSES", raw.Sections[0].Name, "Section order was normalized in memory");
        Equal("HEADER", raw.Sections.Last().Name, "Header was moved");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !b)).Tags);
    }

    private static void RawDocumentCase(DxfVersion v, bool b)
    {
        var tags = RawFixtureTags(v);
        for (int i = 0; i < tags.Count; i++)
        {
            var t = tags[i];
            if (t.Code is 0 or 2 or 9 || (t.Code == 1 && Equals(t.Value, HeaderVersion(v))))
                tags[i] = new DxfTag(t.Code, ((string)t.Value).ToLowerInvariant());
        }
        var raw = LoadRaw(RawFixtureBytes(tags, b));
        Equal(v, raw.Version, "Case-insensitive version detection failed");
        Equal("header", raw.Sections[0].Name, "Section-name spelling was lost");
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !b)).Tags);
    }

    private static void RawDocumentAmbiguousHeader(DxfVersion v, bool b)
    {
        var tags = RawFixtureTags(v);
        var header = new[] { new DxfTag(0, "SECTION"), new DxfTag(2, "header"),
            new DxfTag(9, "$acadver"), new DxfTag(1, HeaderVersion(v)), new DxfTag(0, "ENDSEC") };
        tags.InsertRange(tags.Count - 1, header);
        Throws<FormatException>(() => LoadRaw(RawFixtureBytes(tags, b)));
        tags = RawFixtureTags(v);
        tags.InsertRange(4, new[] { new DxfTag(9, "$acadver"), new DxfTag(1, HeaderVersion(v)) });
        Throws<FormatException>(() => LoadRaw(RawFixtureBytes(tags, b)));
    }

    private static void RawDocumentDestination(DxfVersion v, bool b)
    {
        byte[] source = RawFixtureBytes(RawFixtureTags(v), b);
        var raw = LoadRaw(source);
        byte[] target = Enumerable.Repeat((byte)42, source.Length + 15).ToArray();
        using var output = new MemoryStream(target, true); output.Position = 5;
        raw.Save(output);
        Equal((long)source.Length + 5, output.Position, "Destination position");
        Equal((long)source.Length + 15, output.Length, "Destination suffix was truncated");
        Check(target.Take(5).All(x => x == 42) && target.Skip(source.Length + 5).All(x => x == 42), "Destination prefix/suffix changed");
        Check(source.SequenceEqual(target.Skip(5).Take(source.Length)), "Exact copy at an offset failed");
    }

    private static void RawDocumentRetainFixtures(DxfVersion v, bool b)
    {
        var tags = RawFixtureTags(v);
        var raw = LoadRaw(RawFixtureBytes(tags, b));
        var manifest = tags.Select(tag => new
        {
            code = tag.Code, kind = tag.ValueType.ToString(),
            value = tag.Value switch
            {
                double d => BitConverter.DoubleToInt64Bits(d).ToString("X16", CultureInfo.InvariantCulture),
                byte[] bytes => Convert.ToHexString(bytes),
                bool flag => flag ? "1" : "0",
                _ => Convert.ToString(tag.Value, CultureInfo.InvariantCulture)!
            }
        });
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"raw-preservation-{v}.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        foreach (bool transport in new[] { false, true })
        {
            byte[] bytes = SaveRaw(raw.WithTags(raw.Tags), transport);
            SameRawTags(tags, LoadRaw(bytes).Tags);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"raw-preservation-{v}-{transport}.dxf"), bytes);
        }
    }

    private static void RawDocumentMidCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        byte[] bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), false);
        using var input = new RawCancelAfterReadStream(bytes, cancellation);
        Throws<OperationCanceledException>(() => DxfRawDocument.Load(input, cancellationToken: cancellation.Token));
        Equal(3L, input.Position, "Cancelled load kept consuming input");
        Check(input.CanRead, "Mid-copy cancellation disposed caller-owned input");
    }

    private static void RawDocumentBadTypedInput()
    {
        var tags = RawFixtureTags(DxfVersion.AutoCad2018);
        byte[] input = RawFixtureBytes(tags, false);
        string text = Encoding.UTF8.GetString(input).Replace("290\n1\n", "290\n2\n");
        Throws<FormatException>(() => LoadRaw(Encoding.UTF8.GetBytes(text)));
        text = Encoding.UTF8.GetString(input).Replace("1040\n4.9406564584124654E-324\n", "1040\nNaN\n");
        Check(text.Contains("NaN", StringComparison.Ordinal), "Malformed numeric fixture was not changed");
        Throws<FormatException>(() => LoadRaw(Encoding.UTF8.GetBytes(text)));
        text = Encoding.UTF8.GetString(input).Replace("310\n00FF807F\n", "310\n00G1\n");
        Throws<FormatException>(() => LoadRaw(Encoding.UTF8.GetBytes(text)));
    }

    private static void RawDocumentBinaryUtf8()
    {
        byte[] bytes = RawFixtureBytes(RawFixtureTags(DxfVersion.AutoCad2018), true);
        byte[] needle = Encoding.UTF8.GetBytes("Zażółć 東京");
        int index = -1;
        for (int i = 0; i <= bytes.Length - needle.Length; i++)
            if (bytes.AsSpan(i, needle.Length).SequenceEqual(needle)) { index = i; break; }
        Check(index >= 0, "Invalid UTF-8 fixture did not find its payload");
        bytes[index] = 255;
        Throws<DecoderFallbackException>(() => LoadRaw(bytes));
    }

    private sealed class RawCancelAfterReadStream : MemoryStream
    {
        private readonly CancellationTokenSource cancellation;
        public RawCancelAfterReadStream(byte[] bytes, CancellationTokenSource cancellation) : base(bytes, false)
        { this.cancellation = cancellation; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, Math.Min(count, 3));
            this.cancellation.Cancel(); return read;
        }
    }
}
