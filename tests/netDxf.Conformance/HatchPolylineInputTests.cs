using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPolylineInputTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int mask in new[] { 0, 1, 5, 15 })
                {
                    int m = mask;
                    Run($"hatch/polyline-input/sparse/{v}/{b}/{m}", () => HatchPolylineSparse(v, b, m, false));
                }
                if (!binary) Run($"hatch/polyline-input/comments/{v}", () => HatchPolylineSparse(v, false, 15, true));
                for (int scenario = 0; scenario < 24; ++scenario)
                {
                    int c = scenario;
                    Run($"hatch/polyline-input/malformed/{v}/{b}/{c}", () => HatchPolylineMalformed(v, b, c));
                }
                Run($"hatch/polyline-input/zero-flag-bulges/{v}/{b}", () => HatchPolylineZeroFlag(v, b));
                Run($"hatch/polyline-input/empty/{v}/{b}", () => HatchPolylineEmpty(v, b));
                Run($"hatch/polyline-input/source-reference/{v}/{b}", () => HatchPolylineReference(v, b));
                Run($"hatch/polyline-input/multiple-paths/{v}/{b}", () => HatchPolylineMultiple(v, b));
                foreach (bool references in new[] { false, true })
                {
                    bool r = references;
                    Run($"hatch/polyline-input/forged-count/{v}/{b}/{r}", () => HatchPolylineHugeCount(v, b, r));
                }
                for (int at = 0; at < 3; ++at)
                {
                    int a = at;
                    Run($"hatch/polyline-input/physical-eof/{v}/{b}/{a}", () => HatchPolylinePhysicalEof(v, b, a));
                }
            }
    }

    private static List<DxfTag> HatchPolylineInputTags(DxfVersion version, int mask, bool comments = false)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
        tags[tags.FindIndex(t => t.Code == 72)] = new DxfTag(72, (short)1);
        int start = tags.FindIndex(t => t.Code == 93) + 1;
        int index = 0;
        for (int at = start; tags[at].Code != 97; ++at)
        {
            if (tags[at].Code != 20) continue;
            if ((mask & (1 << index)) != 0) tags.Insert(++at, new DxfTag(42, index % 2 == 0 ? 0.5 : -0.25));
            ++index;
        }
        if (comments)
        {
            int end = tags.FindIndex(t => t.Code == 75);
            for (int at = end; at > tags.FindIndex(t => t.Code == 92); --at)
                tags.Insert(at, new DxfTag(999, "ENDSEC 97 42 comment"));
        }
        return tags;
    }

    private static void HatchPolylineSparse(DxfVersion version, bool binary, int mask, bool comments)
    {
        var tags = HatchPolylineInputTags(version, mask, comments);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid sparse polyline input rejected.");
        Hatch hatch = doc.Entities.Hatches.Single();
        var expected = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 10, 0), new Vector3(0, 10, 0) };
        for (int i = 0; i < expected.Length; ++i)
            if ((mask & (1 << i)) != 0) expected[i].Z = i % 2 == 0 ? 0.5 : -0.25;
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            var poly = ClosureEdge(hatch);
            Check(poly.IsClosed, "Sparse bulges lost closure.");
            Check(expected.SequenceEqual(poly.Vertexes), "Omitted bulges misaligned vertex data.");
            EqualHatchSeeds(new[] { new Vector2(2, 3) }, hatch.SeedPoints);
            Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Boundary consumed following XData");
            using var output = new MemoryStream();
            bool transport = cycle == 1 ? binary : !binary;
            Check(doc.Save(output, transport), "Sparse-bulge save failed.");
            if (cycle == 0 && mask == 5 && !comments)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-sparse-bulges-{version}-{transport}.dxf"), output.ToArray());
            output.Position = 0;
            doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Sparse-bulge reload failed.");
            hatch = doc.Entities.Hatches.Single();
        }
        Check(input.CanRead, "Polyline parser closed caller input.");
    }

    private static void ExpectPolylineInvalid(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH polyline boundary", StringComparison.Ordinal), "Missing polyline context.");
            Check(error.Message.Contains("group code", StringComparison.Ordinal) && error.Message.Contains("position", StringComparison.Ordinal),
                "Missing tag/position diagnostic.");
            Check(input.CanRead, "Malformed polyline closed caller input.");
            return;
        }
        throw new InvalidOperationException("Malformed polyline was accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Malformed polyline was accepted.");
        Check(input.CanRead, "Malformed polyline closed caller input.");
#endif
    }

    private static void HatchPolylineMalformed(DxfVersion version, bool binary, int scenario)
    {
        var tags = HatchPolylineInputTags(version, 5);
        int flag = tags.FindIndex(t => t.Code == 72), closed = flag + 1;
        int count = tags.FindIndex(t => t.Code == 93), vertex = count + 1;
        int refs = tags.FindIndex(t => t.Code == 97);
        switch (scenario)
        {
            case 0: tags.RemoveAt(flag); break;
            case 1: tags[flag] = new(74, (short)1); break;
            case 2: tags[flag] = new(72, (short)-1); break;
            case 3: tags[flag] = new(72, (short)2); break;
            case 4: tags.RemoveAt(closed); break;
            case 5: tags[closed] = new(73, (short)-1); break;
            case 6: tags[closed] = new(73, (short)2); break;
            case 7: tags[count] = new(94, 4); break;
            case 8: tags[count] = new(93, -1); break;
            case 9: tags[count] = new(93, 3); break;
            case 10: tags[count] = new(93, 5); break;
            case 11: tags[vertex] = new(11, 0.0); break;
            case 12: tags[vertex + 1] = new(21, 0.0); break;
            case 13: tags.Insert(vertex + 3, new(42, 0.25)); break;
            case 14: tags.RemoveAt(refs); break;
            case 15: tags[refs] = new(96, 0); break;
            case 16: tags[refs] = new(97, -1); break;
            case 17: tags[refs] = new(97, 1); break;
            case 18: tags[refs] = new(97, 1); tags.Insert(refs + 1, new(340, "201")); break;
            case 19: tags.Insert(refs + 1, new(330, "201")); break;
            case 20: tags.Insert(refs + 1, new(10, 123.0)); break;
            case 21: tags.Insert(refs + 1, new(97, 0)); break;
            case 22: tags[vertex] = new(0, "ENDSEC"); break;
            case 23: tags[refs] = new(97, 2); tags.Insert(refs + 1, new(330, "201")); break;
        }
        ExpectPolylineInvalid(tags, binary);
    }

    private static void HatchPolylineZeroFlag(DxfVersion version, bool binary)
    {
        var tags = HatchPolylineInputTags(version, 5);
        tags[tags.FindIndex(t => t.Code == 72)] = new(72, (short)0);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Contradictory flag lost valid bulges.");
        var vertices = ClosureEdge(doc.Entities.Hatches.Single()).Vertexes;
        Equal(0.5, vertices[0].Z, "Actual bulge discarded under zero flag");
        Equal(0.0, vertices[1].Z, "Missing bulge did not default to zero");
        Equal(0.5, vertices[2].Z, "Sparse bulge misalignment");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Tolerant bulge output failed.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Tolerant output failed to reload.");
        Check(vertices.SequenceEqual(ClosureEdge(loaded.Entities.Hatches.Single()).Vertexes), "Output lost retained bulges.");
    }

    private static void HatchPolylineEmpty(DxfVersion version, bool binary)
    {
        var tags = HatchPolylineInputTags(version, 0);
        int start = tags.FindIndex(t => t.Code == 93), end = tags.FindIndex(t => t.Code == 97);
        tags[start] = new(93, 0); tags.RemoveRange(start + 1, end - start - 1);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Zero-sized polyline list rejected.");
        Equal(0, ClosureEdge(doc.Entities.Hatches.Single()).Vertexes.Length, "Empty list invented vertices");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Empty polyline list save failed.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Empty polyline list reload failed.");
        Equal(0, ClosureEdge(loaded.Entities.Hatches.Single()).Vertexes.Length, "Empty-list output changed");
        // Count-grammar tolerance is not a claim of valid filled-area topology.
    }

    private static void HatchPolylineReference(DxfVersion version, bool binary)
    {
        var tags = HatchPolylineInputTags(version, 0);
        tags[tags.FindIndex(t => t.Code == 71)] = new(71, (short)1);
        int refs = tags.FindIndex(t => t.Code == 97); tags[refs] = new(97, 1);
        tags.Insert(refs + 1, new(330, "201"));
        int end = tags.FindLastIndex(t => t.Code == 0 && Equals(t.Value, "ENDSEC"));
        tags.InsertRange(end, new DxfTag[] {
            new(0, "LWPOLYLINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"),
            new(100, "AcDbPolyline"), new(90, 4), new(70, (short)1), new(38, 2.5),
            new(10, 0.0), new(20, 0.0), new(10, 10.0), new(20, 0.0),
            new(10, 10.0), new(20, 10.0), new(10, 0.0), new(20, 10.0)
        });
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Referenced polyline failed to load.");
        for (int cycle = 0; cycle < 2; ++cycle)
        {
            var hatch = doc.Entities.Hatches.Single();
            Check(hatch.Associative, "Associativity changed.");
            var contour = hatch.BoundaryPaths.Single().Entities.Single();
            Check(ReferenceEquals(doc.GetObjectByHandle("201"), contour), "Boundary reference not resolved to source entity.");
            using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Referenced boundary save failed.");
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Referenced boundary reload failed.");
        }
    }

    private static void HatchPolylineMultiple(DxfVersion version, bool binary)
    {
        var tags = HatchPolylineInputTags(version, 5);
        int start = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 75);
        tags.InsertRange(end, tags.GetRange(start, end - start));
        tags[tags.FindIndex(t => t.Code == 91)] = new(91, 2);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Adjacent polyline boundaries rejected.");
        var paths = doc.Entities.Hatches.Single().BoundaryPaths;
        Equal(2, paths.Count, "Polyline parser consumed the following path");
        foreach (var path in paths)
        {
            var poly = (HatchBoundaryPath.Polyline)path.Edges.Single();
            Equal(4, poly.Vertexes.Length, "Following path vertex count");
            Equal(0.5, poly.Vertexes[2].Z, "Following path bulge");
        }
    }

    private static void HatchPolylineHugeCount(DxfVersion version, bool binary, bool references)
    {
        var tags = HatchPolylineInputTags(version, 0);
        int index = tags.FindIndex(t => t.Code == (references ? 97 : 93));
        tags[index] = new(tags[index].Code, int.MaxValue);
        byte[] bytes = RawFixtureBytes(tags, binary);
        using var input = new MemoryStream(bytes);
        long start = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Forged polyline count was accepted.");
#endif
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Check(allocated < 4 * 1024 * 1024, "A tiny malformed polyline input allocated from its declared count: " + allocated);
        Check(input.CanRead, "Forged count closed caller input.");
    }

    private static void HatchPolylinePhysicalEof(DxfVersion version, bool binary, int at)
    {
        var tags = HatchPolylineInputTags(version, 5);
        int count = tags.FindIndex(t => t.Code == 93);
        int refs = tags.FindIndex(t => t.Code == 97);
        if (at == 2) tags[refs] = new(97, 1);
        int length = at == 0 ? count + 1 : at == 1 ? count + 2 : refs + 1;
        using var input = new MemoryStream(RawFixtureBytes(tags.Take(length), binary));
#if DEBUG
        Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Truncated polyline input was accepted.");
#endif
        Check(input.CanRead, "Truncated input closed caller stream.");
    }
}
