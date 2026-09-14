using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterLwPolylineIntegrityTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
        {
            DxfVersion v = version; bool b = binary;
            for (int order = 0; order < 8; order++)
            {
                int o = order;
                Run($"lwpolyline/vertex-packet/{v}/{b}/{o}", () => LwPolylinePacket(v, b, o));
            }
            for (int malformed = 0; malformed < 14; malformed++)
            {
                int m = malformed;
                Run($"lwpolyline/malformed/{v}/{b}/{m}", () => LwPolylineMalformed(v, b, m));
            }
            Run($"lwpolyline/declared-count-allocation/{v}/{b}", () => LwPolylineCountAllocation(v, b));
            Run($"lwpolyline/empty-count/{v}/{b}", () => LwPolylineEmpty(v, b));
            foreach (bool closed in new[] { false, true })
            {
                bool c = closed;
                Run($"lwpolyline/reverse-taper/{v}/{b}/{c}", () => LwPolylineReverse(v, b, c));
            }
        }
    }

    private static List<DxfTag> LwPolylineIntegrityTags(DxfVersion version, int order)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)),
            new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, "LWPOLYLINE"), new(5, "200"),
            new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbPolyline"),
            new(90, 3), new(70, (short)1), new(38, 2.5), new(39, -0.75)
        };
        for (int i = 0; i < 3; i++)
        {
            tags.Add(new(10, i * 3.0 - 0.125));
            var optional = new[] { new DxfTag(40, i + 0.25), new DxfTag(41, i + 1.5), new DxfTag(42, (i - 1) * 0.5) };
            for (int j = 0; j < 3; j++) if ((order & (1 << j)) != 0) tags.Add(optional[j]);
            tags.Add(new(20, i * i + 0.375));
            for (int j = 2; j >= 0; j--) if ((order & (1 << j)) == 0) tags.Add(optional[j]);
        }
        tags.AddRange(new DxfTag[]
        {
            new(210, 0.0), new(220, 0.0), new(230, 1.0),
            new(1001, "LW_PACKET"), new(1000, "after vertices"), new(1070, (short)73),
            new(0, "LINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 91.0), new(20, -37.0), new(30, 0.0), new(11, 12.0), new(21, 14.0), new(31, 0.0),
            new(0, "ENDSEC"), new(0, "EOF")
        });
        return tags;
    }

    private static void LwPolylineAssertPacket(DxfDocument document)
    {
        Polyline2D polyline = document.Entities.Polylines2D.Single();
        Equal(3, polyline.Vertexes.Count, "LWPOLYLINE vertex count");
        Check(polyline.IsClosed, "LWPOLYLINE closed flag lost");
        Equal(2.5, polyline.Elevation, "LWPOLYLINE elevation");
        Equal(-0.75, polyline.Thickness, "LWPOLYLINE thickness");
        for (int i = 0; i < 3; i++)
        {
            var vertex = polyline.Vertexes[i];
            Equal(new Vector2(i * 3.0 - 0.125, i * i + 0.375), vertex.Position, "Packet point");
            Equal(i + 0.25, vertex.StartWidth, "Packet start width");
            Equal(i + 1.5, vertex.EndWidth, "Packet end width");
            Equal((i - 1) * 0.5, vertex.Bulge, "Packet bulge");
        }
        Equal("after vertices", (string)polyline.XData["LW_PACKET"].XDataRecord[0].Value, "Following XData string");
        Equal((short)73, (short)polyline.XData["LW_PACKET"].XDataRecord[1].Value, "Following XData integer");
        Equal(new Vector3(91, -37, 0), document.Entities.Lines.Single().StartPoint, "Following entity was consumed");
    }

    private static void LwPolylinePacket(DxfVersion version, bool binary, int order)
    {
        var tags = LwPolylineIntegrityTags(version, order);
        if (!binary)
        {
            int first = tags.FindIndex(t => t.Code == 10), last = tags.FindIndex(t => t.Code == 1001);
            for (int i = last; i > first; i--) tags.Insert(i, new(999, "10 20 90 ENDSEC"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("LWPOLYLINE packet rejected");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            LwPolylineAssertPacket(document);
            var original = document.Entities.Polylines2D.Single();
            var clone = (Polyline2D)original.Clone();
            Equal(original.Vertexes[0].StartWidth, clone.Vertexes[0].StartWidth, "Clone taper");
            clone.Vertexes[0].StartWidth += 10;
            Equal(0.25, original.Vertexes[0].StartWidth, "Clone aliases vertex data");
            using var output = new MemoryStream();
            Check(document.Save(output, binary), "LWPOLYLINE packet save failed");
            if (cycle == 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"lwpolyline-packet-{version}-{binary}-{order}.dxf"), output.ToArray());
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("LWPOLYLINE packet reload failed");
        }
        Check(input.CanRead, "LWPOLYLINE reader closed input");
    }

    private static void LwPolylineReject(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("LWPOLYLINE", StringComparison.Ordinal), "Missing LWPOLYLINE diagnostic");
            Check(input.CanRead, "Rejected LWPOLYLINE closed input");
            return;
        }
        throw new InvalidOperationException("Malformed LWPOLYLINE accepted");
#else
        Check(DxfDocument.Load(input) == null, "Malformed LWPOLYLINE accepted");
        Check(input.CanRead, "Rejected LWPOLYLINE closed input");
#endif
    }

    private static void LwPolylineMalformed(DxfVersion version, bool binary, int malformed)
    {
        var tags = LwPolylineIntegrityTags(version, 0);
        int count = tags.FindIndex(t => t.Code == 90), x = tags.FindIndex(t => t.Code == 10), y = x + 1;
        int finalY = tags.FindIndex(tags.FindIndex(x + 1, t => t.Code == 10) + 1, t => t.Code == 10) + 1;
        switch (malformed)
        {
            case 0: tags.RemoveAt(count); break;
            case 1: tags[count] = new(90, -1); break;
            case 2: tags[count] = new(90, 2); break;
            case 3: tags[count] = new(90, 4); break;
            case 4: tags.Insert(count + 1, new(90, 3)); break;
            case 5: tags.RemoveAt(x); break;
            case 6: tags.RemoveAt(y); break;
            case 7: tags.Insert(y + 1, new(20, 123.0)); break;
            case 8: tags.Insert(x, new(40, 1.0)); break;
            case 9: tags.Insert(x, new(41, 1.0)); break;
            case 10: tags.Insert(x, new(42, 0.5)); break;
            case 11: tags.RemoveAt(finalY); break;
            case 12: tags.Insert(y, new(10, 123.0)); break;
            case 13: tags[count] = new(90, 0); break;
        }
        LwPolylineReject(tags, binary);
    }

    private static void LwPolylineCountAllocation(DxfVersion version, bool binary)
    {
        var tags = LwPolylineIntegrityTags(version, 0);
        int count = tags.FindIndex(t => t.Code == 90);
        tags[count] = new(90, 4);
        LwPolylineReject(tags, binary); // Warm up loader and exception metadata.
        tags[count] = new(90, int.MaxValue);
        long before = GC.GetAllocatedBytesForCurrentThread();
        LwPolylineReject(tags, binary);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(allocated < 2_000_000, $"Declared vertex count caused disproportionate allocation: {allocated}");
    }

    private static void LwPolylineEmpty(DxfVersion version, bool binary)
    {
        var tags = LwPolylineIntegrityTags(version, 0);
        tags[tags.FindIndex(t => t.Code == 90)] = new(90, 0);
        int start = tags.FindIndex(t => t.Code == 10), end = tags.FindIndex(t => t.Code == 210);
        tags.RemoveRange(start, end - start);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Exact zero vertex count rejected");
        Equal(0, document.Entities.Polylines2D.Single().Vertexes.Count, "Empty vertex count");
        Equal(1, document.Entities.Lines.Count(), "Empty vertex entity consumed following line");
    }

    private static Vector2 LwPolylineEdgePoint(Polyline2DVertex a, Polyline2DVertex b, double fraction)
    {
        Vector2 chord = b.Position - a.Position;
        if (a.Bulge == 0) return a.Position + chord * fraction;
        Vector2 center = (a.Position + b.Position) * 0.5 + new Vector2(-chord.Y, chord.X) * ((1 - a.Bulge * a.Bulge) / (4 * a.Bulge));
        Vector2 offset = a.Position - center;
        double angle = 4 * Math.Atan(a.Bulge) * fraction;
        return center + new Vector2(offset.X * Math.Cos(angle) - offset.Y * Math.Sin(angle), offset.X * Math.Sin(angle) + offset.Y * Math.Cos(angle));
    }

    private static void LwPolylineAssertReverse(Polyline2D original, Polyline2D reversed)
    {
        int count = original.Vertexes.Count, edges = original.IsClosed ? count : count - 1;
        for (int i = 0; i < edges; i++)
        {
            var a = reversed.Vertexes[i]; var b = reversed.Vertexes[(i + 1) % count];
            int source = Enumerable.Range(0, edges).Single(j => original.Vertexes[j].Position == b.Position && original.Vertexes[(j + 1) % count].Position == a.Position);
            var oldA = original.Vertexes[source]; var oldB = original.Vertexes[(source + 1) % count];
            Equal(oldA.EndWidth, a.StartWidth, "Reversed taper start");
            Equal(oldA.StartWidth, a.EndWidth, "Reversed taper end");
            for (int sample = 0; sample <= 8; sample++)
            {
                double t = sample / 8.0;
                Check(Vector2.Distance(LwPolylineEdgePoint(oldA, oldB, 1 - t), LwPolylineEdgePoint(a, b, t)) < 1e-11, "Reversal changed edge locus");
                double oldWidth = oldA.StartWidth * t + oldA.EndWidth * (1 - t);
                double newWidth = a.StartWidth * (1 - t) + a.EndWidth * t;
                Check(Math.Abs(oldWidth - newWidth) < 1e-12, "Reversal changed taper geometry");
            }
        }
    }

    private static void LwPolylineReverse(DxfVersion version, bool binary, bool closed)
    {
        var original = new Polyline2D(new[] {
            new Polyline2DVertex(-3, 2) { StartWidth = 1, EndWidth = 2, Bulge = 0 },
            new Polyline2DVertex(1, -1) { StartWidth = 3, EndWidth = 5, Bulge = 0.25 },
            new Polyline2DVertex(6, 4) { StartWidth = 0.5, EndWidth = 1.5, Bulge = -0.5 },
            new Polyline2DVertex(9, -2) { StartWidth = 7, EndWidth = 9, Bulge = 0.75 }
        }, closed) { Elevation = 2.5 };
        var reversed = (Polyline2D)original.Clone(); reversed.Reverse();
        Equal(1.0, original.Vertexes[0].StartWidth, "Reverse clone changed source");
        LwPolylineAssertReverse(original, reversed);
        var twice = (Polyline2D)reversed.Clone(); twice.Reverse();
        for (int i = 0; i < original.Vertexes.Count; i++)
        {
            Equal(original.Vertexes[i].Position, twice.Vertexes[i].Position, "Double reverse position");
            Equal(original.Vertexes[i].StartWidth, twice.Vertexes[i].StartWidth, "Double reverse start width");
            Equal(original.Vertexes[i].EndWidth, twice.Vertexes[i].EndWidth, "Double reverse end width");
            Equal(original.Vertexes[i].Bulge, twice.Vertexes[i].Bulge, "Double reverse bulge");
        }
        var document = new DxfDocument(version); document.Entities.Add(original); document.Entities.Add(reversed);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Reversal save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"lwpolyline-reverse-{version}-{binary}-{closed}.dxf"), output.ToArray());
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Reversal reload failed");
        var pair = loaded.Entities.Polylines2D.ToArray();
        LwPolylineAssertReverse(pair[0], pair[1]);
    }
}
