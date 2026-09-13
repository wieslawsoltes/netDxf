using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] HatchPatternOrders =
    {
        "canonical", "late-angle", "late-scale", "late-both", "early-metadata", "late-style",
        "omitted-style", "pattern-before-boundary", "seeds-before-pattern", "late-name-fill", "late-geometry", "comments"
    };

    private static void RegisterHatchPatternOrderTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (var settings in new[] { (37.0, 0.25), (90.0, 2.0), (-45.0, 4.0), (450.0, 0.5) })
                    foreach (string order in HatchPatternOrders)
                    {
                        if (binary && order == "comments") continue;
                        DxfVersion v = version; bool b = binary; string o = order; var s = settings;
                        Run($"hatch/pattern-order/{v}/{b}/{s}/{o}", () => HatchPatternOrder(v, b, s.Item1, s.Item2, o));
                    }
    }

    // Produce wire data directly from explicit PAT-local lines, without calling the library writer.
    private static List<DxfTag> HatchPatternOrderTags(DxfVersion version, double angle, double scale, string order)
    {
        var tags = HatchPatternValidationTags(version);
        int begin = tags.FindIndex(t => t.Code == 78);
        int end = tags.FindIndex(begin, t => t.Code == 98);
        int line = 0;
        for (int i = begin + 1; i < end; i++)
        {
            if (tags[i].Code != 53) continue;
            double localAngle = line++ == 0 ? 11.5 : 173.25;
            double a = (angle + localAngle) * Math.PI / 180.0;
            double g = angle * Math.PI / 180.0;
            double x = (double)tags[i + 1].Value, y = (double)tags[i + 2].Value;
            double dx = (double)tags[i + 3].Value, dy = (double)tags[i + 4].Value;
            tags[i] = new(53, angle + localAngle);
            tags[i + 1] = new(43, scale * (x * Math.Cos(g) - y * Math.Sin(g)));
            tags[i + 2] = new(44, scale * (x * Math.Sin(g) + y * Math.Cos(g)));
            tags[i + 3] = new(45, scale * (dx * Math.Cos(a) - dy * Math.Sin(a)));
            tags[i + 4] = new(46, scale * (dx * Math.Sin(a) + dy * Math.Cos(a)));
        }
        for (int i = 0; i < tags.Count; i++)
        {
            if (tags[i].Code == 52) tags[i] = new(52, angle);
            else if (tags[i].Code == 41) tags[i] = new(41, scale);
            else if (tags[i].Code == 49) tags[i] = new(49, (double)tags[i].Value * scale);
        }
        // Group 47 is independent of whether a pattern-style field was already encountered.
        tags.Insert(tags.FindIndex(t => t.Code == 98), new(47, 0.125));

        void MoveBefore(short code, short before)
        {
            int at = tags.FindIndex(t => t.Code == code);
            DxfTag tag = tags[at]; tags.RemoveAt(at);
            tags.Insert(tags.FindIndex(t => t.Code == before), tag);
        }
        switch (order)
        {
            case "late-angle": MoveBefore(52, 98); break;
            case "late-scale": MoveBefore(41, 98); break;
            case "late-both": MoveBefore(52, 98); MoveBefore(41, 98); break;
            case "early-metadata": foreach (short code in new short[] { 76, 52, 41, 77 }) MoveBefore(code, 75); break;
            case "late-style": MoveBefore(75, 1001); break;
            case "omitted-style": tags.RemoveAt(tags.FindIndex(t => t.Code == 75)); break;
            case "pattern-before-boundary":
                begin = tags.FindIndex(t => t.Code == 75); end = tags.FindIndex(t => t.Code == 98);
                var packet = tags.GetRange(begin, end - begin); tags.RemoveRange(begin, end - begin);
                tags.InsertRange(tags.FindIndex(t => t.Code == 91), packet);
                break;
            case "seeds-before-pattern":
                begin = tags.FindIndex(t => t.Code == 98);
                var seeds = tags.GetRange(begin, 3); tags.RemoveRange(begin, 3);
                tags.InsertRange(tags.FindIndex(t => t.Code == 75), seeds);
                break;
            case "late-name-fill":
                int name = tags.FindIndex(t => t.Code == 2 && Equals(t.Value, "U"));
                var nameTag = tags[name]; tags.RemoveAt(name); tags.Insert(tags.FindIndex(t => t.Code == 98), nameTag);
                MoveBefore(70, 98);
                break;
            case "late-geometry": foreach (short code in new short[] { 30, 210, 220, 230 }) MoveBefore(code, 98); break;
            case "comments":
                MoveBefore(52, 98); MoveBefore(41, 98);
                begin = tags.FindIndex(t => t.Code == 75); end = tags.FindIndex(t => t.Code == 1001);
                for (int i = end; i > begin; i--) tags.Insert(i, new(999, "52 41 75 are comment text"));
                break;
        }
        return tags;
    }

    private static void HatchPatternOrder(DxfVersion version, bool binary, double angle, double scale, string order)
    {
        var tags = HatchPatternOrderTags(version, angle, scale, order);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Reordered pattern rejected.");
        Check(input.CanRead, "Pattern-order read closed input.");
        CheckHatchPatternOrder(doc, angle, scale, 1);
        var original = doc.Entities.Hatches.Single();
        var copy = (Hatch)original.Clone();
        Check(!ReferenceEquals(copy.Pattern.LineDefinitions[0], original.Pattern.LineDefinitions[0]), "Line clone aliases source.");
        doc.Entities.Add(copy);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            using var output = new MemoryStream();
            bool transport = cycle % 2 == 0 ? !binary : binary;
            Check(doc.Save(output, transport), "Reordered pattern save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var hatch in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH"))
            {
                foreach (short code in new short[] { 43, 44, 45, 46, 49 })
                {
                    double[] actual = hatch.Tags.Where(t => t.Code == code).Select(t => (double)t.Value).ToArray();
                    double[] expected = tags.Where(t => t.Code == code).Select(t => (double)t.Value).ToArray();
                    Equal(expected.Length, actual.Length, "Wire component count");
                    for (int i = 0; i < expected.Length; i++) Near(expected[i], actual[i], "Wire geometry/dash " + code);
                }
            }
            if (order == "late-both" && angle == 37.0 && cycle == 1)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-pattern-order-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Pattern-order round trip failed.");
            CheckHatchPatternOrder(doc, angle, scale, 2);
            Check(output.CanRead, "Pattern-order round trip closed stream.");
        }
    }

    private static void CheckHatchPatternOrder(DxfDocument doc, double angle, double scale, int count)
    {
        Equal(count, doc.Entities.Hatches.Count(), "Ordered hatch count");
        foreach (var hatch in doc.Entities.Hatches)
        {
            var pattern = hatch.Pattern;
            Equal("U", pattern.Name, "Pattern name"); Equal(HatchFillType.PatternFill, pattern.Fill, "Pattern fill");
            Equal(HatchType.Custom, pattern.Type, "Pattern type"); Equal(HatchStyle.Normal, pattern.Style, "Pattern style");
            Check(pattern.IsDouble, "Late metadata lost double flag.");
            Near(((angle % 360.0) + 360.0) % 360.0, pattern.Angle, "Global angle"); Near(scale, pattern.Scale, "Global scale");
            Near(2.5, hatch.Elevation, "Late elevation"); Equal(Vector3.UnitZ, hatch.Normal, "Late normal");
            Equal((double?)0.125, hatch.PixelSize, "Pixel size");
            Equal(1, hatch.BoundaryPaths.Count, "Boundary packet preserved");
            Check(((HatchBoundaryPath.Polyline)hatch.BoundaryPaths[0].Edges.Single()).IsClosed, "Boundary closure lost.");
            EqualHatchSeeds(new[] { new Vector2(2.0, 3.0) }, hatch.SeedPoints);
            Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Pattern following XData");
            Equal(2, pattern.LineDefinitions.Count, "Ordered pattern line count");
            double[][] expected = { new[] { 11.5, 1.5, -2.25, 0.5, 2.0, 1.25, -0.75, 0.0 }, new[] { 173.25, -3.0, 4.5, -0.25, 0.125, 2.0 } };
            for (int i = 0; i < expected.Length; i++)
            {
                var line = pattern.LineDefinitions[i];
                double[] actual = new[] { line.Angle, line.Origin.X, line.Origin.Y, line.Delta.X, line.Delta.Y }.Concat(line.DashPattern).ToArray();
                Equal(expected[i].Length, actual.Length, "PAT-local component count");
                for (int j = 0; j < actual.Length; j++) Near(expected[i][j], actual[j], $"PAT-local line {i} component {j}");
            }
        }
        Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following entity");
    }
}
