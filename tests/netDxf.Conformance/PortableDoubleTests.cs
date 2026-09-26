// Copyright (c) netDxf contributors. Licensed under the MIT License.
using NetDxf.Qualification;
using System.Text.Json;
using netDxf;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterPortableDoubleTests()
    {
        RegisterNumericConsumerTests();
        var assembly = typeof(DxfDocument).Assembly;
        var portable = PortableDoubleCases.GetParser(assembly, "TryParsePortable");
        var selected = PortableDoubleCases.GetParser(assembly, "TryParse");
        var observations = new List<object>();
        foreach (var item in PortableDoubleCases.All())
            Run("portable-double/" + item.Id, () =>
            {
                string? expected = PortableDoubleCases.Expected(item);
                string? portableBits = PortableDoubleCases.ParseBits(portable, item.Token);
                string? selectedBits = PortableDoubleCases.ParseBits(selected, item.Token);
                string? codecBits = PortableDoubleCases.CodecBits(assembly, item);
                // Keep failing observations too: exported evidence must not
                // silently omit exactly the rows needed to diagnose a failure.
                observations.Add(new { id = item.Id, token = item.Token, portableBits, selectedBits, codecBits });
                Check(portableBits == expected, "Incorrect portable rounding/admission: " + item.Id
                    + " expected=" + expected + " actual=" + portableBits);
                Check(selectedBits == expected, "Incorrect selected rounding/admission: " + item.Id
                    + " expected=" + expected + " actual=" + selectedBits);
                Check(codecBits == expected, "Incorrect shared DXF codec: " + item.Id
                    + " expected=" + expected + " actual=" + codecBits);
            });
        if (observations.Count != 0)
            File.WriteAllText(Path.Combine(ArtifactDirectory, "portable-double.json"),
                JsonSerializer.Serialize(new { schema = 1, observations }));
        foreach (var version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"portable-double/wire/{version}/{binary}", () =>
                PortableDoubleCases.VerifyWire(version, binary, ArtifactDirectory));
        Run("portable-double/all-double-groups", () =>
        {
            foreach (var group in ExpectedTagTypes().Where(pair => pair.Value == netDxf.IO.DxfTagValueType.Double))
            foreach (string token in new[] { "9007199254740993", "4.9406564584124654e-324", "-1e-9999" })
            {
                using var stream = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(group.Key + "\n" + token + "\n0\nEOF\n"));
                object reader = NewCodeReader(stream, false); Invoke(reader, "Next");
                Check(portable(token, out double value), "Expected finite number");
                SameDoubleBits(value, (double)Invoke(reader, "ReadDouble")!, "Double group " + group.Key);
                Invoke(reader, "Next"); Check((string)Invoke(reader, "ReadString")! == "EOF", "Following group");
            }
        });
        Run("portable-double/culture-and-parser-whitespace", () =>
        {
            PortableDoubleCases.VerifyDispatch(assembly);
            var before = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("pl-PL");
                foreach (var parser in new[] { portable, selected })
                {
                    Check(parser("\r\n\v\f\t-1.5 \r\n", out double value) && value == -1.5, "Float edge whitespace");
                    Check(!parser("1,5", out _), "Do not use current-culture decimal separator");
                    Check(!parser("1\n2", out _), "Do not accept internal whitespace");
                }
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = before; }
        });
    }
}
