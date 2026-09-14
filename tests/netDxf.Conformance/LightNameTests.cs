using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLightNameTests()
    {
        Run("light/name/nonmutating-validation", () =>
        {
            var light = new Light { Name = "original" };
            Throws<ArgumentNullException>(() => light.Name = null!);
            foreach (string name in new[] { "\r", "\n", "\0", "A\rB", "A\nB", "A\0B", "\r\n" })
            {
                Throws<ArgumentException>(() => light.Name = name);
                Equal("original", light.Name, "Rejected name mutated state");
            }
        });
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"light/name/encoded-delimiters/{v}/{b}", () =>
                {
                    foreach (string name in new[] { "A\\U+000DB", "A\\U+000AB", "A\\U+0000B" })
                    {
                        var tags = LightTags(v, 1, false);
                        int at = tags.FindIndex(t => t.Code == 1 && ((string)t.Value).StartsWith("Lamp", StringComparison.Ordinal));
                        tags[at] = new(1, name);
                        using var stream = new MemoryStream(RawFixtureBytes(tags, b));
#if DEBUG
                        Throws<InvalidDataException>(() => DxfDocument.Load(stream));
#else
                        Check(DxfDocument.Load(stream) == null, "Invalid decoded name accepted.");
#endif
                        Check(stream.CanRead, "Invalid name closed caller stream.");
                    }
                });
                if (v < DxfVersion.AutoCad2007) continue;
                Run($"light/name/valid-persistence/{v}/{b}", () =>
                {
                    foreach (string name in new[] { "", "A\tB", "Żółć 灯", "A\\U+1234B", "A\\U+000AB", "C:\\tmp", "\\u+0041", new string('x', 300) })
                    {
                        var light = new Light { Name = name };
                        var clone = (Light)light.Clone(); Equal(name, clone.Name, "Cloned name");
                        var doc = new DxfDocument(v); doc.Entities.Add(light);
                        using var stream = new MemoryStream(); Check(doc.Save(stream, b), "Valid name save failed.");
                        stream.Position = 0; var restored = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Valid name reload failed.");
                        Equal(name, restored.Entities.Lights.Single().Name, "Round-trip name");
                    }
                });
            }
    }
}
