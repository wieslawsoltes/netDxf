using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPathCountTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int count in new[] { 0, 1, 2, 4 })
                    foreach (bool late in new[] { false, true })
                        foreach (bool comments in binary ? new[] { false } : new[] { false, true })
                        {
                            int c = count; bool l = late, co = comments;
                            Run($"hatch/path-count/valid/{v}/{b}/{c}/{l}/{co}", () => HatchPathCountValid(v, b, c, l, co));
                        }
                foreach (int failure in Enumerable.Range(0, 18))
                {
                    int f = failure;
                    Run($"hatch/path-count/invalid/{v}/{b}/{f}", () => HatchPathCountInvalid(v, b, f));
                }
                foreach (int cut in Enumerable.Range(0, 3))
                {
                    int c = cut;
                    Run($"hatch/path-count/eof/{v}/{b}/{c}", () => HatchPathCountEof(v, b, c));
                }
                Run($"hatch/path-count/absent/{v}/{b}", () => HatchPathCountAbsent(v, b));
            }
    }

    private static List<DxfTag> HatchPathCountTags(DxfVersion version, int count, bool late = false, bool comments = false)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
        int start = tags.FindIndex(t => t.Code == 91), end = tags.FindIndex(t => t.Code == 75);
        var outer = tags.GetRange(start + 1, end - start - 1);
        outer[0] = new(92, 3); // external polyline, with its representation flag
        var inner = new DxfTag[]
        {
            new(92, 0), new(93, 1), new(72, (short)2), new(10, 5.0), new(20, 5.0),
            new(40, 1.0), new(50, 0.0), new(51, 360.0), new(73, (short)1), new(97, 0)
        };
        var packet = new List<DxfTag> { new(91, count) };
        for (int i = 0; i < count; ++i) packet.AddRange(i == 0 ? outer : inner);
        if (comments)
            for (int i = packet.Count; i > 0; --i) packet.Insert(i, new(999, "91 92 93 ENDSEC"));
        tags.RemoveRange(start, end - start);
        if (late) start = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        tags.InsertRange(start, packet);
        int tail = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        tags.InsertRange(tail, new DxfTag[]
        {
            new(0, "LINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 20.0), new(20, 30.0), new(30, 40.0), new(11, 21.0), new(21, 31.0), new(31, 41.0)
        });
        return tags;
    }

    private static void HatchPathCountValid(DxfVersion version, bool binary, int count, bool late, bool comments)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchPathCountTags(version, count, late, comments), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid boundary packet rejected.");
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            Equal(count == 0 ? 0 : 1, doc.Entities.Hatches.Count(), "Existing empty-boundary discard policy changed");
            Equal(new Vector3(20, 30, 40), doc.Entities.Lines.Single().StartPoint, "Following entity consumed by boundary list");
            foreach (var hatch in doc.Entities.Hatches)
            {
                Equal(count, hatch.BoundaryPaths.Count, "Boundary path count");
                Equal((HatchBoundaryPathTypeFlags)3, hatch.BoundaryPaths[0].PathType, "External polyline flag");
                Equal(4, ((HatchBoundaryPath.Polyline)hatch.BoundaryPaths[0].Edges.Single()).Vertexes.Length, "Outer vertices lost");
                foreach (var path in hatch.BoundaryPaths.Skip(1))
                {
                    Equal((HatchBoundaryPathTypeFlags)0, path.PathType, "Flags inherited from preceding path");
                    var arc = (HatchBoundaryPath.Arc)path.Edges.Single();
                    Equal(new Vector2(5, 5), arc.Center, "Inner arc center"); Equal(1.0, arc.Radius, "Inner arc radius");
                }
                Equal(2.5, hatch.Elevation, "Boundary parser changed elevation");
                Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "Boundary parser changed seed");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Boundary parser changed XData");
                Equal(count, ((Hatch)hatch.Clone()).BoundaryPaths.Count, "Boundary clone count");
            }
            using var output = new MemoryStream(); bool format = cycle % 2 == 0 ? !binary : binary;
            Check(doc.Save(output, format), "Boundary packet save failed.");
            if (count == 2 && cycle == 1 && !late && !comments)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-path-count-{version}-{binary}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Boundary packet reload failed.");
        }
        Check(input.CanRead, "Boundary reader closed caller stream.");
    }

    private static void HatchPathCountInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = HatchPathCountTags(version, 2);
        int count = tags.FindIndex(t => t.Code == 91), first = tags.FindIndex(t => t.Code == 92);
        int second = tags.FindIndex(first + 1, t => t.Code == 92), tail = tags.FindIndex(t => t.Code == 75);
        int xdataEnd = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "LINE"));
        switch (failure)
        {
            case 0: tags[count] = new(91, -1); break;
            case 1: tags[count] = new(91, 0); break;
            case 2: tags[count] = new(91, 1); break;
            case 3: tags[count] = new(91, 3); break;
            case 4: tags.Insert(count, new(91, 0)); break;
            case 5: tags.Insert(tail, new(91, 0)); break;
            case 6: tags.RemoveAt(first); break;
            case 7: tags.RemoveAt(second); break;
            case 8: tags.Insert(first, new(92, 0)); break;
            case 9: tags.RemoveAt(second + 1); break;
            case 10: tags.RemoveAt(count); break;
            case 11: tags.Insert(xdataEnd, new(92, 0)); break;
            case 12: tags.Insert(xdataEnd, new(93, 0)); break;
            case 13: tags[count] = new(91, int.MaxValue); break;
            case 14: tags.InsertRange(second, new DxfTag[] { new(0, "LINE"), new(5, "202"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine") }); break;
            case 15: tags.Insert(second, new(0, "ENDSEC")); break;
            case 16: tags[count] = new(91, int.MinValue); break;
            case 17: tags.Insert(count, new(93, 0)); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH", StringComparison.Ordinal) && error.Message.Contains("group code", StringComparison.Ordinal) &&
                error.Message.Contains("position", StringComparison.Ordinal), "Missing boundary diagnostic context.");
            Check(input.CanRead, "Rejected boundary closed caller stream."); return;
        }
        throw new InvalidOperationException("Invalid boundary count/framing was accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Invalid boundary count/framing was accepted.");
        Check(input.CanRead, "Rejected boundary closed caller stream.");
#endif
    }

    private static void HatchPathCountEof(DxfVersion version, bool binary, int cut)
    {
        var tags = HatchPathCountTags(version, 2);
        int count = tags.FindIndex(t => t.Code == 91), first = tags.FindIndex(t => t.Code == 92);
        int second = tags.FindIndex(first + 1, t => t.Code == 92);
        int end = cut == 0 ? count + 1 : cut == 1 ? first + 1 : second;
        using var input = new MemoryStream(RawFixtureBytes(tags.Take(end), binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (IOException) { Check(input.CanRead, "Truncated boundary closed stream."); return; }
        throw new InvalidOperationException("Physical boundary truncation accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Physical boundary truncation accepted.");
#endif
    }

    private static void HatchPathCountAbsent(DxfVersion version, bool binary)
    {
        var tags = HatchPathCountTags(version, 0); tags.RemoveAll(t => t.Code == 91);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Existing absent-boundary policy changed.");
        Equal(0, doc.Entities.Hatches.Count(), "Absent boundary invented a HATCH");
        Equal(1, doc.Entities.Lines.Count(), "Absent boundary consumed following LINE");
    }
}
