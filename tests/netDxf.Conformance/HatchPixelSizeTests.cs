using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPixelSizeTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                for (int location = 0; location < 3; location++)
                {
                    int l = location;
                    Run($"hatch/pixel-size/wire/{v}/{b}/{l}", () => HatchPixelWire(v, b, l));
                }
                for (int failure = 0; failure < 3; failure++)
                {
                    int f = failure;
                    Run($"hatch/pixel-size/invalid/{v}/{b}/{f}", () => HatchPixelInvalid(v, b, f));
                }
            }
    }

    private static List<DxfTag> HatchPixelTags(DxfVersion version, double? pixel, int location)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 1);
        if (pixel.HasValue)
        {
            int at = location == 0 ? tags.FindIndex(t => t.Code == 75) :
                location == 1 ? tags.FindIndex(t => t.Code == 98) :
                tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
            tags.Insert(at, new DxfTag(47, pixel.Value));
        }
        return tags;
    }

    private static void HatchPixelWire(DxfVersion version, bool binary, int location)
    {
        foreach (double? expected in new double?[] { null, 0.0, BitConverter.Int64BitsToDouble(long.MinValue),
            0.125, 1e-20, double.Epsilon, double.MaxValue })
        {
            var tags = HatchPixelTags(version, expected, location);
            if (!binary && expected.HasValue) tags.Insert(tags.FindIndex(t => t.Code == 47), new(999, "47 ENDSEC"));
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Pixel-size fixture failed to load.");
            var original = doc.Entities.Hatches.Single();
            Equal(2.5, original.Elevation, "Pixel size changed elevation");
            Equal(1, original.BoundaryPaths.Count, "Pixel size changed boundaries");
            EqualHatchSeeds(new[] { new Vector2(2, 3) }, original.SeedPoints);
            Equal("after pattern", (string)original.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData changed");
            doc.Entities.Add((Hatch)original.Clone());
            for (int cycle = 0; cycle < 2; cycle++)
            {
                using var output = new MemoryStream();
                Check(doc.Save(output, cycle == 0 ? !binary : binary), "Pixel-size fixture failed to save.");
                output.Position = 0; var raw = DxfRawDocument.Load(output);
                var hatches = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray();
                Equal(2, hatches.Length, "Pixel clone count");
                foreach (var hatch in hatches)
                {
                    DxfTag[] values = hatch.Tags.Where(t => t.Code == 47).ToArray();
                    Equal(expected.HasValue ? 1 : 0, values.Length, "Pixel-size optional-field presence");
                    if (expected.HasValue) SameDoubleBits(expected.Value, (double)values[0].Value, "Pixel-size bits changed");
                }
                if (cycle == 0 && location == 1 && expected == 0.125)
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-pixel-{version}-{!binary}.dxf"), output.ToArray());
                output.Position = 0;
                doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Pixel-size reload failed.");
                Check(output.CanRead, "Pixel round trip closed caller stream.");
            }
            Check(input.CanRead, "Pixel input closed caller stream.");
        }
    }

    private static void HatchPixelInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = HatchPixelTags(version, failure == 0 ? -0.125 : 0.125, 1);
        if (failure == 1) tags.Insert(tags.FindIndex(t => t.Code == 75), new DxfTag(47, 0.25));
        byte[] bytes = RawFixtureBytes(failure == 2 ? tags.Take(tags.FindIndex(t => t.Code == 47)) : tags, binary);
        if (failure == 2)
            bytes = bytes.Concat(binary ? new byte[] { 47, 0 } : System.Text.Encoding.ASCII.GetBytes("47\n")).ToArray();
        using var input = new MemoryStream(bytes);
#if DEBUG
        if (failure == 2) Throws<EndOfStreamException>(() => DxfDocument.Load(input));
        else
        {
            try { DxfDocument.Load(input); throw new InvalidOperationException("Invalid pixel-size data was accepted."); }
            catch (InvalidDataException error)
            { Check(error.Message.Contains("47", StringComparison.Ordinal), "Pixel diagnostic lost group 47."); }
        }
#else
        Check(DxfDocument.Load(input) == null, "Invalid pixel-size data was accepted.");
#endif
        Check(input.CanRead, "Invalid pixel data closed input.");
    }
}
