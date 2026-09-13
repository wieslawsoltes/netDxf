using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPatternValidationTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (string mode in new[] { "canonical", "reordered", "empty", "no-dashes", "comments" })
                {
                    string m = mode;
                    if (b && m == "comments") continue; // 999 is a text-only DXF comment.
                    Run($"hatch/pattern-lists/valid/{v}/{b}/{m}", () => HatchPatternValid(v, b, m));
                }
                foreach (string defect in new[] { "negative-lines", "too-many-lines", "too-few-lines", "huge-lines",
                    "duplicate-list", "negative-dashes", "too-many-dashes", "too-few-dashes", "huge-dashes",
                    "wrong-angle", "wrong-x", "wrong-y", "wrong-dx", "wrong-dy", "wrong-dash-count", "wrong-dash",
                    "missing-x", "missing-y", "missing-dx", "missing-dy", "missing-dash-count", "duplicate-x", "duplicate-dash-count" })
                {
                    string d = defect;
                    Run($"hatch/pattern-lists/invalid/{v}/{b}/{d}", () => HatchPatternInvalid(v, b, d));
                }
                for (int component = 0; component < 10; component++)
                {
                    int c = component;
                    Run($"hatch/pattern-lists/eof/{v}/{b}/{c}", () => HatchPatternEof(v, b, c));
                }
            }
    }

    private static List<DxfTag> HatchPatternValidationTags(DxfVersion version)
    {
        var tags = HatchDoubleTags(version, HatchType.Custom, 1);
        int start = tags.FindIndex(t => t.Code == 78);
        int end = tags.FindIndex(start, t => t.Code == 98);
        tags.RemoveRange(start, end - start);
        tags.InsertRange(start, new DxfTag[]
        {
            new(78, (short)2),
            new(53, 0.0), new(43, 1.5), new(44, -2.25), new(45, 0.5), new(46, 2.0),
            new(79, (short)3), new(49, 1.25), new(49, -0.75), new(49, 0.0),
            new(53, 0.0), new(43, -3.0), new(44, 4.5), new(45, -0.25), new(46, 0.125),
            new(79, (short)1), new(49, 2.0)
        });
        int terminal = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        tags.InsertRange(terminal, new DxfTag[]
        {
            new(0, "LINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 20.0), new(20, 30.0), new(30, 40.0), new(11, 21.0), new(21, 31.0), new(31, 41.0)
        });
        return tags;
    }

    private static void HatchPatternValid(DxfVersion version, bool binary, string mode)
    {
        var tags = HatchPatternValidationTags(version);
        int start = tags.FindIndex(t => t.Code == 78);
        int end = tags.FindIndex(start, t => t.Code == 98);
        if (mode == "empty")
        {
            tags.RemoveRange(start + 1, end - start - 1);
            tags[start] = new(78, (short)0);
        }
        else if (mode == "no-dashes")
        {
            for (int i = end - 1; i > start; i--)
            {
                if (tags[i].Code == 49) tags.RemoveAt(i);
                else if (tags[i].Code == 79) tags[i] = new(79, (short)0);
            }
        }
        else if (mode == "reordered")
        {
            // Each group-53 starts a line. Scalar fields within that line are keyed, not positional.
            for (int i = end - 1; i > start; i--)
                if (tags[i].Code == 53)
                {
                    var scalars = tags.GetRange(i + 1, 4);
                    tags.RemoveRange(i + 1, 4);
                    scalars.Reverse();
                    int next = tags.FindIndex(i + 1, t => t.Code == 53 || t.Code == 98);
                    tags.InsertRange(next, scalars); // dash-count/list may precede those scalars
                }
        }
        else if (mode == "comments")
        {
            for (int i = end; i > start; i--) tags.Insert(i, new(999, "53 79 49 ENDSEC are not tags"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid HATCH pattern lists rejected.");
        Check(input.CanRead, "Pattern parser closed caller input.");
        HatchPatternCheck(doc, mode, 1);
        doc.Entities.Add((Hatch)doc.Entities.Hatches.Single().Clone());
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream();
            bool format = cycle == 0 ? !binary : binary;
            Check(doc.Save(output, format), "Pattern-list fixture save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH"))
            {
                Equal((short)(mode == "empty" ? 0 : 2), (short)record.Tags.Single(t => t.Code == 78).Value, "Emitted pattern line count");
                Equal(mode == "empty" ? 0 : 2, record.Tags.Count(t => t.Code == 79), "Emitted dash count groups");
                Equal(mode is "empty" or "no-dashes" ? 0 : 4, record.Tags.Count(t => t.Code == 49), "Emitted dash lengths");
            }
            if (mode == "canonical" && cycle == 1)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-pattern-lists-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Pattern-list fixture reload failed.");
            HatchPatternCheck(doc, mode, 2);
            Check(output.CanRead, "Pattern round trip closed caller stream.");
        }
    }

    private static void HatchPatternCheck(DxfDocument doc, string mode, int count)
    {
        Equal(count, doc.Entities.Hatches.Count(), "Hatch count");
        foreach (var hatch in doc.Entities.Hatches)
        {
            Equal(HatchType.Custom, hatch.Pattern.Type, "Pattern type");
            Check(hatch.Pattern.IsDouble, "Pattern double flag changed.");
            Equal(2.5, hatch.Elevation, "Hatch elevation");
            Equal(1, hatch.BoundaryPaths.Count, "Boundary path count");
            EqualHatchSeeds(new[] { new Vector2(2.0, 3.0) }, hatch.SeedPoints);
            Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData");
            Equal(mode == "empty" ? 0 : 2, hatch.Pattern.LineDefinitions.Count, "Pattern line count");
            if (mode == "empty") continue;
            var first = hatch.Pattern.LineDefinitions[0];
            var second = hatch.Pattern.LineDefinitions[1];
            Equal(new Vector2(1.5, -2.25), first.Origin, "First line origin");
            Equal(new Vector2(0.5, 2.0), first.Delta, "First line delta");
            Equal(new Vector2(-3.0, 4.5), second.Origin, "Second line origin");
            Equal(new Vector2(-0.25, 0.125), second.Delta, "Second line delta");
            Check(first.DashPattern.SequenceEqual(mode == "no-dashes" ? Array.Empty<double>() : new[] { 1.25, -0.75, 0.0 }), "First dash list changed.");
            Check(second.DashPattern.SequenceEqual(mode == "no-dashes" ? Array.Empty<double>() : new[] { 2.0 }), "Second dash list changed.");
        }
        var line = doc.Entities.Lines.Single();
        Equal(new Vector3(20, 30, 40), line.StartPoint, "Following LINE start");
        Equal(new Vector3(21, 31, 41), line.EndPoint, "Following LINE end");
    }

    private static void HatchPatternInvalid(DxfVersion version, bool binary, string defect)
    {
        var tags = HatchPatternValidationTags(version);
        int lines = tags.FindIndex(t => t.Code == 78);
        int dashes = tags.FindIndex(lines, t => t.Code == 79);
        int end = tags.FindIndex(lines, t => t.Code == 98);
        switch (defect)
        {
            case "negative-lines": tags[lines] = new(78, (short)-1); break;
            case "too-many-lines": tags[lines] = new(78, (short)3); break;
            case "too-few-lines": tags[lines] = new(78, (short)1); break;
            case "huge-lines": tags[lines] = new(78, short.MaxValue); break;
            case "duplicate-list": tags.Insert(end, new(78, (short)0)); break;
            case "negative-dashes": tags[dashes] = new(79, (short)-1); break;
            case "too-many-dashes": tags[dashes] = new(79, (short)4); break;
            case "too-few-dashes": tags[dashes] = new(79, (short)2); break;
            case "huge-dashes": tags[dashes] = new(79, short.MaxValue); break;
            case "wrong-angle": tags[lines + 1] = new(54, 0.0); break;
            case "wrong-x": tags[lines + 2] = new(48, 1.5); break;
            case "wrong-y": tags[lines + 3] = new(48, -2.25); break;
            case "wrong-dx": tags[lines + 4] = new(48, 0.5); break;
            case "wrong-dy": tags[lines + 5] = new(48, 2.0); break;
            case "wrong-dash-count": tags[dashes] = new(73, (short)3); break;
            case "wrong-dash": tags[dashes + 1] = new(48, 1.25); break;
            case "missing-x": tags.RemoveAt(lines + 2); break;
            case "missing-y": tags.RemoveAt(lines + 3); break;
            case "missing-dx": tags.RemoveAt(lines + 4); break;
            case "missing-dy": tags.RemoveAt(lines + 5); break;
            case "missing-dash-count": tags.RemoveAt(dashes); break;
            case "duplicate-x": tags.Insert(lines + 3, new(43, 5.0)); break;
            case "duplicate-dash-count": tags.Insert(end, new(79, (short)0)); break;
            default: throw new InvalidOperationException("Unknown defect.");
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Invalid HATCH pattern list accepted."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH pattern", StringComparison.Ordinal), "Missing pattern context: " + error.Message);
            Check(error.Message.Contains("group code", StringComparison.Ordinal), "Missing group-code context.");
        }
#else
        Check(DxfDocument.Load(input) == null, "Invalid HATCH pattern list accepted.");
#endif
        Check(input.CanRead, "Rejected pattern list closed caller input.");
    }

    private static void HatchPatternEof(DxfVersion version, bool binary, int component)
    {
        var tags = HatchPatternValidationTags(version);
        int start = tags.FindIndex(t => t.Code == 78);
        // A physical EOF after a complete tag is still inside the declared first line/list.
        byte[] bytes = RawFixtureBytes(tags.Take(start + component + 1), binary);
        using var input = new MemoryStream(bytes);
#if DEBUG
        Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Truncated pattern list accepted.");
#endif
        Check(input.CanRead, "Truncated pattern input was closed.");
    }
}
