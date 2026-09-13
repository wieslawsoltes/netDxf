using System.Security.Cryptography;
using System.Text;
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (string File, DxfVersion Version, int CodePage, int Tags, string Sha)[] LegacyFixtures =
    {
        ("small_r13.dxf", DxfVersion.AutoCad13, 932, 1502, "a1007452f8ada2e71fdef60f79923924045cb371f37eeab896a61f5628573d0b"),
        ("small_r14.dxf", DxfVersion.AutoCad14, 1252, 789, "92e792f8f9169e4226c9b7dbe848ba9afb96d3e388e6297c55481e1941c65fb0"),
        ("bin_dxf_r13.dxf", DxfVersion.AutoCad13, 1252, 2075, "c65782683815ff0f1680d26207da1637a486f96b30bc215685e26cf7adee8464"),
        ("bin_dxf_r14.dxf", DxfVersion.AutoCad14, 1252, 2083, "177a97e5dc86ca106d0dd6365d139122fc54ff78df87670847eb7174a8bbd5eb")
    };

    private static void RegisterRawLegacyProfileTests()
    {
        foreach (var fixture in LegacyFixtures)
        {
            var f = fixture;
            Run($"raw-legacy/external/{f.File}", () => RawLegacyExternal(f));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad13, DxfVersion.AutoCad14 })
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"raw-legacy/authored/{v}/{b}", () => RawLegacyAuthored(v, b));
                Run($"raw-legacy/invalid-and-bounded/{v}/{b}", () => RawLegacyBoundaries(v, b));
            }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad13, DxfVersion.AutoCad14, DxfVersion.AutoCad2000, DxfVersion.AutoCad2004 })
            foreach (bool binary in new[] { false, true })
                foreach (var pair in new[] { ("dos437", 437, "é"), ("DoS850", 850, "é"), ("dos932", 932, "東京") })
                {
                    DxfVersion v = version; bool b = binary; var p = pair;
                    Run($"raw-legacy/dos-codepage/{v}/{b}/{p.Item1}", () => RawLegacyEncoding(v, b, p.Item1, p.Item2, p.Item3));
                }
        Run("raw-legacy/typed-admission-unchanged", RawLegacyTypedBoundary);
        Run("raw-legacy/invalid-codepage-aliases", RawLegacyBadAliases);
    }

    private static List<DxfTag> RawLegacyTags(DxfVersion version, string codePage = "ANSI_1252", string text = "legacy text")
        => new()
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"),
            new(1, version switch { DxfVersion.AutoCad13 => "AC1012", DxfVersion.AutoCad14 => "AC1014", _ => HeaderVersion(version) }),
            new(9, "$DWGCODEPAGE"), new(3, codePage), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, "LINE"), new(5, "A"),
            new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 1e-20), new(20, -0.0), new(30, 3.0), new(11, 4.0), new(21, 5.0), new(31, 6.0),
            new(1001, "RAW_TEST"), new(1000, text), new(1004, new byte[] { 0, 128, 255 }),
            new(1005, "B"), new(1070, (short)-1), new(1071, int.MinValue), new(0, "ENDSEC"), new(0, "EOF")
        };

    private static void RawLegacyExternal((string File, DxfVersion Version, int CodePage, int Tags, string Sha) fixture)
    {
        byte[] bytes = File.ReadAllBytes(Path.Combine("tests", "fixtures", "legacy", fixture.File));
        Equal(fixture.Sha, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Pinned external fixture hash");
        var raw = LoadRaw(bytes);
        Equal(fixture.Version, raw.Version, "External declared version");
        Equal(fixture.CodePage, raw.EncodingCodePage, "External encoding");
        Equal(fixture.Tags, raw.Tags.Count, "External ordered tag count");
        Equal(fixture.File.StartsWith("bin_", StringComparison.Ordinal), raw.IsBinary, "External transport");
        Check(bytes.SequenceEqual(SaveRaw(raw)), "External original bytes changed");
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);
        SameRawTags(raw.Tags, LoadRaw(SaveRaw(raw, !raw.IsBinary)).Tags);
        CheckRawRecordPartition(raw);
        var layer = raw.Sections.Single(s => s.Name == "TABLES").Records.First(r => r.Name == "LAYER");
        Check(layer.Content.Any(t => t.Code == 62), "Expected an authored layer color");
        var replacement = layer.Tags.Select(t => t.Code == 62 ? new DxfTag(62, (short)3) : t).ToArray();
        var edited = raw.WithRecord(layer, replacement);
        AssertOutsideRecordUnchanged(raw, layer, edited, replacement.Length);
        SameRawTags(edited.Tags, LoadRaw(SaveRaw(edited, !raw.IsBinary)).Tags);
        // Independent tooling gets normalized text with the declared encoding unchanged.
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, "legacy-normalized-" + fixture.File), SaveRaw(raw.WithTags(raw.Tags), false));
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, "legacy-edited-" + fixture.File), SaveRaw(edited, false));
    }

    private static void RawLegacyAuthored(DxfVersion version, bool binary)
    {
        var tags = RawLegacyTags(version);
        var raw = LoadRaw(RawFixtureBytes(tags, binary));
        Equal(version, raw.Version, "Authored legacy profile");
        SameRawTags(tags, raw.Tags);
        SameRawTags(tags, LoadRaw(SaveRaw(DxfRawDocument.Create(tags, binary), !binary)).Tags);
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags))).Tags);
        CheckRawRecordPartition(raw);
    }

    private static void RawLegacyBoundaries(DxfVersion version, bool binary)
    {
        var tags = RawLegacyTags(version); byte[] bytes = RawFixtureBytes(tags, binary);
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumBytes: bytes.Length - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumTags: tags.Count - 1)));
        Throws<InvalidDataException>(() => LoadRaw(bytes, new DxfRawOptions(maximumStringLength: 9)));
        var raw = LoadRaw(bytes);
        Throws<NotSupportedException>(() => raw.WithTags(tags.Select(t => t.Code == 1 ? new DxfTag(1, "AC1015") : t)));
        using var cancelled = new MemoryStream(bytes);
        Throws<OperationCanceledException>(() => DxfRawDocument.Load(cancelled, cancellationToken: new CancellationToken(true)));
        Check(cancelled.CanRead, "Legacy cancellation closed input");
        foreach (string unsupported in new[] { "AC1009", "AC1034", "VENDOR_UNKNOWN" })
        {
            var changed = tags.Select(t => t.Code == 1 ? new DxfTag(1, unsupported) : t);
            Throws<DxfVersionNotSupportedException>(() => LoadRaw(RawFixtureBytes(changed, binary)));
        }
        byte[] truncated = RawFixtureBytes(tags.Take(tags.Count - 1), binary);
        Throws<EndOfStreamException>(() => LoadRaw(truncated));
    }

    private static void RawLegacyEncoding(DxfVersion version, bool binary, string name, int codePage, string text)
    {
        var tags = RawLegacyTags(version, name, text);
        var encoding = Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        var raw = LoadRaw(RawFixtureBytes(tags, binary, encoding));
        Equal(codePage, raw.EncodingCodePage, "DOS alias resolution");
        SameRawTags(tags, raw.Tags);
        SameRawTags(tags, LoadRaw(SaveRaw(raw.WithTags(raw.Tags), !binary)).Tags);
        Equal(name, (string)raw.Tags.Single(t => t.Code == 3).Value, "Alias spelling changed");
    }

    private static void RawLegacyTypedBoundary()
    {
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad13, DxfVersion.AutoCad14 })
        {
            Throws<NotSupportedException>(() => new DxfDocument(version));
            foreach (bool binary in new[] { false, true })
            {
                using var input = new MemoryStream(RawFixtureBytes(RawLegacyTags(version), binary));
                Throws<DxfVersionNotSupportedException>(() => DxfDocument.Load(input));
                Check(input.CanRead, "Unsupported typed load closed input");
            }
        }
    }

    private static void RawLegacyBadAliases()
    {
        foreach (string name in new[] { "dos", "dos_932", "dos+932", "dos 932", "dos932 ", "dos99999999999", "UTF-16", "ANSI_" })
            Throws<NotSupportedException>(() => DxfRawDocument.Create(RawLegacyTags(DxfVersion.AutoCad13, name)));
        foreach (string name in new[] { "dos1200", "dos1201", "dos12000", "dos12001" })
            Throws<NotSupportedException>(() => DxfRawDocument.Create(RawLegacyTags(DxfVersion.AutoCad13, name)));
    }
}
