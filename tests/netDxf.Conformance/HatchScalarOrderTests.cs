using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchScalarOrderTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (short kind in new short[] { 1, 2, 3 })
                {
                    DxfVersion v = version; bool b = binary; short k = kind;
                    Run($"hatch/scalar-order/permutations/{v}/{b}/{k}", () => HatchScalarPermutations(v, b, k));
                    Run($"hatch/scalar-order/missing-duplicate/{v}/{b}/{k}", () => HatchScalarInvalid(v, b, k));
                    Run($"hatch/scalar-order/next-edge/{v}/{b}/{k}", () => HatchScalarNextEdge(v, b, k));
                }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (bool spline in new[] { false, true })
                {
                    DxfVersion v = version; bool b = binary, s = spline;
                    Run($"hatch/scalar-order/header/{v}/{b}/{s}", () => HatchUnorderedHeader(v, b, s));
                }
    }

    private static List<DxfTag> HatchScalarTags(DxfVersion version, short kind, int shift, bool reverse)
    {
        var tags = HatchEdgeDispatchTags(version, kind);
        int start = tags.FindIndex(t => t.Code == 72) + 1, end = tags.FindIndex(t => t.Code == 97);
        var packet = tags.GetRange(start, end - start);
        if (kind == 1)
        {
            packet[0] = new(10, -0.125); packet[1] = new(20, 2.0000000000000004);
        }
        if (reverse) packet.Reverse();
        var reordered = packet.Skip(shift).Concat(packet.Take(shift)).ToList();
        tags.RemoveRange(start, end - start); tags.InsertRange(start, reordered);
        return tags;
    }

    private static void AssertHatchScalar(HatchBoundaryPath.Edge edge, short kind)
    {
        Equal((HatchBoundaryPath.EdgeType)kind, edge.Type, "Scalar edge kind");
        switch (edge)
        {
            case HatchBoundaryPath.Line line:
                SameDoubleBits(-0.125, line.Start.X, "Line start X"); SameDoubleBits(2.0000000000000004, line.Start.Y, "Line start Y");
                Equal(new Vector2(10, 10), line.End, "Line end"); break;
            case HatchBoundaryPath.Arc arc:
                Equal(new Vector2(1, 2), arc.Center, "Arc center"); Equal(3.0, arc.Radius, "Arc radius");
                Equal(0.0, arc.StartAngle, "Arc start"); Equal(360.0, arc.EndAngle, "Arc end"); Check(arc.IsCounterclockwise, "Arc direction"); break;
            case HatchBoundaryPath.Ellipse ellipse:
                Equal(new Vector2(1, 2), ellipse.Center, "Ellipse center"); Equal(new Vector2(3, 0), ellipse.EndMajorAxis, "Ellipse major vector");
                Equal(0.5, ellipse.MinorRatio, "Ellipse ratio"); Equal(0.0, ellipse.StartAngle, "Ellipse start");
                Equal(360.0, ellipse.EndAngle, "Ellipse end"); Check(ellipse.IsCounterclockwise, "Ellipse direction"); break;
            default: throw new InvalidOperationException("Unexpected scalar edge.");
        }
    }

    private static void HatchScalarPermutations(DxfVersion version, bool binary, short kind)
    {
        int count = kind == 1 ? 4 : kind == 2 ? 6 : 8;
        foreach (bool reverse in new[] { false, true })
            for (int shift = 0; shift < count; shift++)
            {
                var tags = HatchScalarTags(version, kind, shift, reverse);
                if (!binary && reverse)
                {
                    int start = tags.FindIndex(t => t.Code == 72), end = tags.FindIndex(t => t.Code == 97);
                    for (int i = end; i > start; i--) tags.Insert(i, new(999, "72 97 0 ENDSEC"));
                }
                using var input = new MemoryStream(RawFixtureBytes(tags, binary));
                var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Reordered HATCH scalar input rejected.");
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    Hatch hatch = doc.Entities.Hatches.Single();
                    AssertHatchScalar(hatch.BoundaryPaths.Single().Edges.Single(), kind);
                    AssertHatchScalar(((Hatch)hatch.Clone()).BoundaryPaths.Single().Edges.Single(), kind);
                    Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Following XData");
                    Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "Seed");
                    using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Scalar edge save failed.");
                    if (cycle == 0 && shift == 0 && reverse)
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-scalar-order-{version}-{binary}-{kind}.dxf"), output.ToArray());
                    output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Scalar edge reload failed.");
                }
                Check(input.CanRead, "Scalar reader closed input.");
            }
    }

    private static void HatchScalarInvalid(DxfVersion version, bool binary, short kind)
    {
        var original = HatchScalarTags(version, kind, 1, true);
        int start = original.FindIndex(t => t.Code == 72) + 1, end = original.FindIndex(t => t.Code == 97);
        for (int i = start; i < end; i++)
        {
            var missing = original.ToList(); missing.RemoveAt(i); ExpectEdgePacketInvalid(missing, binary);
            var duplicate = original.ToList(); duplicate.Insert(end, original[i]); ExpectEdgePacketInvalid(duplicate, binary);
        }
        var unframed = original.ToList(); unframed.Insert(end, new(75, (short)0)); ExpectEdgePacketInvalid(unframed, binary);
        if (kind != 1)
        {
            var flag = original.ToList(); flag[flag.FindIndex(start, t => t.Code == 73)] = new(73, (short)2);
            ExpectEdgePacketInvalid(flag, binary);
        }
        var overrun = original.ToList(); overrun.RemoveAt(end - 1);
        // A following entity cannot satisfy the missing field even when its group codes match.
        overrun.InsertRange(end - 1, new DxfTag[] { new(0, "LINE"), new(5, "ABCD"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine") });
        ExpectEdgePacketInvalid(overrun, binary);
    }

    private static void HatchScalarNextEdge(DxfVersion version, bool binary, short kind)
    {
        var tags = HatchScalarTags(version, kind, 1, true);
        int refs = tags.FindIndex(t => t.Code == 97);
        tags[tags.FindIndex(t => t.Code == 93)] = new(93, 2);
        tags.InsertRange(refs, new DxfTag[] { new(72, (short)1), new(21, -7.0), new(11, 8.0), new(20, 5.0), new(10, -6.0) });
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Two reordered edges rejected.");
        var edges = doc.Entities.Hatches.Single().BoundaryPaths.Single().Edges;
        Equal(2, edges.Count, "Counted edges changed"); AssertHatchScalar(edges[0], kind);
        var line = (HatchBoundaryPath.Line)edges[1];
        Equal(new Vector2(-6, 5), line.Start, "Second edge start"); Equal(new Vector2(8, -7), line.End, "Second edge end");
    }
    private static void HatchUnorderedHeader(DxfVersion version, bool binary, bool spline)
    {
        var source = spline ? HatchEdgeDispatchTags(version, 4) : HatchDoubleTags(version, HatchType.UserDefined, 0);
        int start = spline ? source.FindIndex(t => t.Code == 94) : source.FindIndex(t => t.Code == 92) + 1;
        int count = spline ? 5 : 3;
        var header = source.GetRange(start, count); header.Reverse();
        for (int shift = 0; shift < count; shift++)
        {
            var tags = source.ToList(); tags.RemoveRange(start, count);
            tags.InsertRange(start, header.Skip(shift).Concat(header.Take(shift)));
            if (!binary) tags.Insert(start + 1, new(999, "95 96 93 73"));
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Reordered list header rejected.");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                var hatch = doc.Entities.Hatches.Single(); var edge = hatch.BoundaryPaths.Single().Edges.Single();
                if (spline)
                {
                    var e = (HatchBoundaryPath.Spline)edge;
                    Equal((short)2, e.Degree, "Reordered degree"); Check(!e.IsRational && !e.IsPeriodic, "Reordered spline flags");
                    Check(e.Knots.SequenceEqual(new double[] { 0, 0, 0, 1, 1, 1 }), "Knot list changed");
                    Equal(new Vector3(5, 10, 1), e.ControlPoints[1], "Control list changed");
                }
                else
                {
                    var e = (HatchBoundaryPath.Polyline)edge;
                    Check(e.IsClosed && e.Vertexes.Length == 4, "Polyline header changed");
                    Equal(new Vector3(10, 10, 0), e.Vertexes[2], "Polyline list changed");
                }
                Equal(new Vector2(2, 3), hatch.SeedPoints.Single(), "Following header seed");
                using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Reordered header save failed");
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Reordered header reload failed");
            }
        }
        foreach (int i in Enumerable.Range(0, count))
            foreach (bool duplicate in new[] { false, true })
            {
                var tags = source.ToList();
                if (duplicate) tags.Insert(start + count, source[start + i]); else tags.RemoveAt(start + i);
                using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
                Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
                Check(DxfDocument.Load(input) == null, "Malformed list header accepted.");
#endif
            }
    }

}
