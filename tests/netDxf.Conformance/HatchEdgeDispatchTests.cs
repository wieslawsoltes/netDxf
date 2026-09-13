using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchEdgeDispatchTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                for (short kind = 1; kind <= 4; ++kind)
                {
                    short k = kind;
                    Run($"hatch/edge-dispatch/valid/{v}/{b}/{k}", () => HatchEdgeDispatchValid(v, b, k, false));
                    if (!b) Run($"hatch/edge-dispatch/comments/{v}/{k}", () => HatchEdgeDispatchValid(v, false, k, true));
                }
                for (int failure = 0; failure < 12; ++failure)
                {
                    int f = failure;
                    Run($"hatch/edge-dispatch/invalid/{v}/{b}/{f}", () => HatchEdgeDispatchInvalid(v, b, f));
                }
                for (int failure = 0; failure < 24; ++failure)
                {
                    int f = failure;
                    Run($"hatch/edge-packets/invalid/{v}/{b}/{f}", () => HatchEdgePacketInvalid(v, b, f));
                }
                foreach (short code in new short[] { 93, 95, 96 })
                {
                    short c = code;
                    Run($"hatch/edge-packets/allocation/{v}/{b}/{c}", () => HatchSplineAllocation(v, b, c));
                }
                Run($"hatch/edge-packets/sparse-weights/{v}/{b}", () => HatchSplineWeights(v, b));
                if (v >= DxfVersion.AutoCad2010)
                    for (int failure = 0; failure < 7; ++failure)
                    {
                        int f = failure;
                        Run($"hatch/edge-packets/fit-grammar/{v}/{b}/{f}", () => HatchSplineFitGrammar(v, b, f));
                    }
                Run($"hatch/edge-dispatch/reference-allocation/{v}/{b}", () => HatchEdgeReferenceAllocation(v, b));
                Run($"hatch/edge-dispatch/physical-eof/{v}/{b}", () => HatchEdgeDispatchEof(v, b));
            }
    }

    private static List<DxfTag> HatchEdgeDispatchTags(DxfVersion version, short kind, bool comments = false)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
        int start = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 97);
        tags.RemoveRange(start, end - start);
        var edge = new List<DxfTag> { new(92, 0), new(93, 1), new(72, kind) };
        if (kind == 1)
            edge.AddRange(new DxfTag[] { new(10, 0.0), new(20, 0.0), new(11, 10.0), new(21, 10.0) });
        else if (kind == 2)
            edge.AddRange(new DxfTag[] { new(10, 1.0), new(20, 2.0), new(40, 3.0), new(50, 0.0), new(51, 360.0), new(73, (short)1) });
        else if (kind == 3)
            edge.AddRange(new DxfTag[] { new(10, 1.0), new(20, 2.0), new(11, 3.0), new(21, 0.0), new(40, 0.5), new(50, 0.0), new(51, 360.0), new(73, (short)1) });
        else if (kind == 4)
        {
            edge.AddRange(new DxfTag[] {
                new(94, 2), new(73, (short)0), new(74, (short)0), new(95, 6), new(96, 3),
                new(40, 0.0), new(40, 0.0), new(40, 0.0), new(40, 1.0), new(40, 1.0), new(40, 1.0),
                new(10, 0.0), new(20, 0.0), new(10, 5.0), new(20, 10.0), new(10, 10.0), new(20, 0.0)
            });
            if (version >= DxfVersion.AutoCad2010) edge.Add(new(97, 0));
        }
        tags.InsertRange(start, edge);
        if (comments)
        {
            end = tags.FindIndex(t => t.Code == 75);
            for (int i = end; i > start + 1; --i) tags.Insert(i, new(999, "72 97 ENDSEC"));
        }
        return tags;
    }

    private static void HatchEdgeDispatchValid(DxfVersion version, bool binary, short kind, bool comments)
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchEdgeDispatchTags(version, kind, comments), binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Supported edge kind rejected.");
        for (int cycle = 0; cycle < 2; ++cycle)
        {
            var hatch = doc.Entities.Hatches.Single();
            var edge = hatch.BoundaryPaths.Single().Edges.Single();
            Equal((HatchBoundaryPath.EdgeType)kind, edge.Type, "Edge kind changed");
            switch (edge)
            {
                case HatchBoundaryPath.Line line: Equal(new Vector2(10, 10), line.End, "Line endpoint"); break;
                case HatchBoundaryPath.Arc arc: Equal(3.0, arc.Radius, "Arc radius"); break;
                case HatchBoundaryPath.Ellipse ellipse: Equal(0.5, ellipse.MinorRatio, "Ellipse ratio"); break;
                case HatchBoundaryPath.Spline spline:
                    Check(new[] { 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 }.SequenceEqual(spline.Knots), "Spline knots changed.");
                    Equal(new Vector3(5, 10, 1), spline.ControlPoints[1], "Spline control data"); break;
            }
            EqualHatchSeeds(new[] { new Vector2(2, 3) }, hatch.SeedPoints);
            Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData changed");
            using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Supported edge save failed.");
            if (cycle == 0 && !comments)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-edge-dispatch-{version}-{binary}-{kind}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Supported edge reload failed.");
        }
        Check(input.CanRead, "Edge parser closed caller stream.");
        // These are primitive/dispatch controls, not a certification of closed area topology.
    }

    private static void HatchEdgeDispatchInvalid(DxfVersion version, bool binary, int failure)
    {
        var tags = HatchEdgeDispatchTags(version, 1);
        int count = tags.FindIndex(t => t.Code == 93), type = count + 1, refs = tags.FindIndex(t => t.Code == 97);
        switch (failure)
        {
            case 0: tags[type] = new(72, (short)-1); break;
            case 1: tags[type] = new(72, (short)0); break;
            case 2: tags[type] = new(72, (short)5); break;
            case 3: tags[type] = new(72, short.MaxValue); break;
            case 4: tags[type] = new(74, (short)1); break;
            case 5: tags[count] = new(93, -1); break;
            case 6: tags[refs] = new(96, 0); break;
            case 7: tags[refs] = new(97, -1); break;
            case 8: tags[refs] = new(97, 1); break;
            case 9: tags[refs] = new(97, 1); tags.Insert(refs + 1, new(340, "201")); break;
            case 10: tags.Insert(refs + 1, new(330, "201")); break;
            case 11: tags[count] = new(93, 2); tags.Insert(refs, new(72, (short)5)); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH edge boundary", StringComparison.Ordinal) &&
                error.Message.Contains("group code", StringComparison.Ordinal) && error.Message.Contains("position", StringComparison.Ordinal),
                "Missing edge-dispatch diagnostic context.");
            Check(input.CanRead, "Malformed edge closed caller stream.");
            return;
        }
        throw new InvalidOperationException("Invalid edge input was accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Invalid edge input was accepted.");
        Check(input.CanRead, "Malformed edge closed caller stream.");
#endif
    }

    private static void ExpectEdgePacketInvalid(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("HATCH edge boundary", StringComparison.Ordinal) &&
                error.Message.Contains("group code", StringComparison.Ordinal) && error.Message.Contains("position", StringComparison.Ordinal),
                "Missing edge-packet diagnostic context.");
            Check(input.CanRead, "Invalid edge packet closed its input.");
            return;
        }
        throw new InvalidOperationException("Invalid edge packet was accepted.");
#else
        Check(DxfDocument.Load(input) == null, "Invalid edge packet was accepted.");
        Check(input.CanRead, "Invalid edge packet closed its input.");
#endif
    }

    private static void HatchEdgePacketInvalid(DxfVersion version, bool binary, int failure)
    {
        short kind = failure < 3 ? (short)(failure + 1) : (short)4;
        var tags = HatchEdgeDispatchTags(version, kind);
        int start = tags.FindIndex(t => t.Code == 72) + 1;
        int Find(short code) => tags.FindIndex(start, t => t.Code == code);
        if (failure < 3)
        {
            int scalar = Find(20); tags[scalar] = new(21, tags[scalar].Value);
        }
        else switch (failure)
        {
            case 3: tags[Find(94)] = new(94, 0); break;
            case 4: tags[Find(94)] = new(94, -1); break;
            case 5: tags[Find(94)] = new(94, (int)short.MaxValue + 1); break;
            case 6: tags[Find(94)] = new(94, int.MaxValue); break;
            case 7: tags[Find(94)] = new(90, 2); break;
            case 8: tags[Find(73)] = new(73, (short)2); break;
            case 9: tags[Find(74)] = new(74, (short)-1); break;
            case 10: tags[Find(95)] = new(95, -1); break;
            case 11: tags[Find(96)] = new(96, -1); break;
            case 12: tags[Find(95)] = new(95, 5); break;
            case 13: tags[Find(95)] = new(95, 7); break;
            case 14: tags[Find(96)] = new(96, 2); break;
            case 15: tags[Find(96)] = new(96, 4); break;
            case 16: tags[Find(40)] = new(41, 0.0); break;
            case 17: tags[Find(10)] = new(11, 0.0); break;
            case 18: tags[Find(20)] = new(21, 0.0); break;
            case 19:
                tags.InsertRange(Find(20) + 1, new DxfTag[] { new(42, 1.0), new(42, 2.0) }); break;
            case 20: tags.RemoveAt(Find(20)); break;
            case 21: tags.RemoveAt(Find(96)); break;
            case 22:
            case 23:
                tags = HatchEdgeDispatchTags(version, failure == 22 ? (short)2 : (short)3);
                tags[tags.FindIndex(t => t.Code == 73)] = new(73, (short)2); break;
        }
        ExpectEdgePacketInvalid(tags, binary);
    }

    private static void HatchSplineAllocation(DxfVersion version, bool binary, short code)
    {
        var tags = HatchEdgeDispatchTags(version, 4);
        tags[tags.FindIndex(t => t.Code == code)] = new(code, int.MaxValue);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Huge edge/spline count accepted.");
#endif
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024,
            "Tiny malformed edge input allocated from a declared count.");
    }

    private static void HatchSplineWeights(DxfVersion version, bool binary)
    {
        var tags = HatchEdgeDispatchTags(version, 4);
        int start = tags.FindIndex(t => t.Code == 94);
        tags[tags.FindIndex(start, t => t.Code == 73)] = new(73, (short)1);
        int first = tags.FindIndex(start, t => t.Code == 20);
        tags.Insert(first + 1, new(42, 0.5));
        int last = tags.FindIndex(first + 2, t => t.Code == 97) - 1;
        tags.Insert(last + 1, new(42, 2.0));
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Sparse spline weights rejected.");
        var expected = new[] { new Vector3(0, 0, 0.5), new Vector3(5, 10, 1), new Vector3(10, 0, 2) };
        for (int i = 0; i < 2; ++i)
        {
            var spline = (HatchBoundaryPath.Spline)document.Entities.Hatches.Single().BoundaryPaths.Single().Edges.Single();
            Check(spline.IsRational && expected.SequenceEqual(spline.ControlPoints), "Spline weight defaults changed.");
            using var output = new MemoryStream(); Check(document.Save(output, !binary), "Weighted spline output failed.");
            output.Position = 0; document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Weighted spline reload failed.");
        }
    }

    private static void HatchSplineFitGrammar(DxfVersion version, bool binary, int failure)
    {
        var tags = HatchEdgeDispatchTags(version, 4);
        int fit = tags.FindIndex(t => t.Code == 97);
        switch (failure)
        {
            case 0: tags[fit] = new(97, -1); break;
            case 1: tags[fit] = new(97, 1); break;
            case 2: tags[fit] = new(97, 1); tags.InsertRange(fit + 1, new DxfTag[] { new(11, 0.0), new(21, 0.0) }); break;
            case 3: tags.InsertRange(fit + 1, new DxfTag[] { new(12, 1.0), new(23, 0.0) }); break;
            case 4: tags.Insert(fit + 1, new(13, 1.0)); break;
            case 5: tags[fit] = new(98, 0); break;
            default:
                tags[fit] = new(97, 1);
                tags.InsertRange(fit + 1, new DxfTag[] { new(11, 0.0), new(21, 0.0), new(12, 1.0), new(22, 0.0), new(13, 1.0), new(23, 0.0) });
                using (var input = new MemoryStream(RawFixtureBytes(tags, binary)))
                {
                    var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid fit/tangent packet rejected.");
                    Equal(3, ((HatchBoundaryPath.Spline)doc.Entities.Hatches.Single().BoundaryPaths.Single().Edges.Single()).ControlPoints.Length,
                        "Fit data disrupted spline control data");
                    EqualHatchSeeds(new[] { new Vector2(2, 3) }, doc.Entities.Hatches.Single().SeedPoints);
                    // The existing typed model discards fit/tangent metadata. This only verifies packet framing.
                }
                return;
        }
        ExpectEdgePacketInvalid(tags, binary);
    }

    private static void HatchEdgeReferenceAllocation(DxfVersion version, bool binary)
    {
        var tags = HatchEdgeDispatchTags(version, 1);
        tags[tags.FindIndex(t => t.Code == 97)] = new(97, int.MaxValue);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Huge source-reference count accepted.");
#endif
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Source-reference count controlled preallocation.");
    }

    private static void HatchEdgeDispatchEof(DxfVersion version, bool binary)
    {
        var tags = HatchEdgeDispatchTags(version, 1);
        int count = tags.FindIndex(t => t.Code == 93);
        using var input = new MemoryStream(RawFixtureBytes(tags.Take(count + 1), binary));
#if DEBUG
        Throws<EndOfStreamException>(() => DxfDocument.Load(input));
#else
        Check(DxfDocument.Load(input) == null, "Truncated edge declaration accepted.");
#endif
        Check(input.CanRead, "Truncated edge closed caller stream.");
    }
}
