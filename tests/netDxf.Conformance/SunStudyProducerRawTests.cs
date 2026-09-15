using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RunSunStudyProducerRawTests()
    {
        foreach (int year in new[] { 2013, 2018 })
        foreach (bool sourceBinary in new[] { false, true })
        foreach (bool hours in new[] { false, true })
        foreach (string mode in sourceBinary && hours ? new[] { "source-rejection" } : new[] { "source-rejection", "exact", "ascii", "binary" })
        {
            int y = year; bool s = sourceBinary, h = hours; string m = mode;
            Run($"sunstudy/producer-raw/{year}/{sourceBinary}/{hours}/{mode}",
                () => SunStudyProducerRaw(y, s, h, m));
        }
    }

    private static void SunStudyProducerRaw(int year, bool sourceBinary, bool hours, string mode)
    {
        string name = $"ixmilia-sunstudy-R{year}-{(sourceBinary ? "binary" : "ascii")}-no-dates-{(hours ? "hours" : "no-hours")}.dxf";
        string folder = Path.Combine("tests", "fixtures", "sunstudy-producer");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, "source-manifest.json")));
        var entry = manifest.RootElement.GetProperty("files").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == name);
        Equal(sourceBinary && hours ? "failed-reload" : "original", entry.GetProperty("kind").GetString()!, "Producer output classification");
        byte[] compressed = File.ReadAllBytes(Path.Combine(folder, entry.GetProperty("storedFile").GetString()!));
        Equal(entry.GetProperty("gzipSha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(compressed)).ToLowerInvariant(), "Pinned compressed original");
        using var gzip = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress);
        using var decompressed = new MemoryStream();
        gzip.CopyTo(decompressed);
        byte[] source = decompressed.ToArray();
        Equal(entry.GetProperty("bytes").GetInt32(), source.Length, "Pinned original byte count");
        Equal(entry.GetProperty("sha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant(), "Pinned exact original");

        if (mode == "source-rejection")
        {
            using var input = new MemoryStream(source);
            bool rejected = false;
            try { _ = DxfRawDocument.Load(input); }
            catch (Exception error) when ((sourceBinary ? error is InvalidDataException : error is FormatException)
                && error.Message.Contains("Invalid hexadecimal handle", StringComparison.Ordinal)
                && error.Message.Contains("group code 340", StringComparison.Ordinal))
            {
                rejected = true;
            }
            Check(rejected, "Unchanged producer scaffolding did not reject its empty DIMSTYLE pointer.");
            Check(input.CanRead, "Malformed producer input closed the caller stream.");
            return;
        }
        var carrier = entry.GetProperty("carrier");
        byte[] carrierBytes = File.ReadAllBytes(Path.Combine(folder, carrier.GetProperty("file").GetString()!));
        Equal(carrier.GetProperty("sha256").GetString()!,
            Convert.ToHexString(SHA256.HashData(carrierBytes)).ToLowerInvariant(), "Pinned disclosed carrier");
        Equal(10, carrier.GetProperty("transformations").GetArrayLength(), "Disclosed unrelated DIMSTYLE null pointers");
        var raw = LoadRaw(carrierBytes);
        Equal(year == 2013 ? DxfVersion.AutoCad2013 : DxfVersion.AutoCad2018, raw.Version, "Producer profile");
        Equal(sourceBinary, raw.IsBinary, "Producer source transport");
        Equal(1, raw.Sections.Single(section => section.Name == "OBJECTS").Content
            .Count(tag => tag.Code == 0 && Equals(tag.Value, "SUNSTUDY")), "Actual SUNSTUDY object count");
        byte[] output;
        if (mode == "exact")
        {
            output = SaveRaw(raw);
            Check(carrierBytes.SequenceEqual(output), "Exact carrier save changed bytes.");
        }
        else
        {
            output = SaveRaw(raw.WithTags(raw.Tags), mode == "binary");
            var copy = LoadRaw(output);
            Equal(mode == "binary", copy.IsBinary, "Requested normalized transport");
            SameRawTags(raw.Tags, copy.Tags);
        }
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"sunstudy-producer-{name[..^4]}-{mode}.dxf"), output);
    }
}
