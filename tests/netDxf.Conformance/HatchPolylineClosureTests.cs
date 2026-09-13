using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchPolylineClosureTests()
    {
        foreach (bool closed in new[] { false, true })
        {
            bool c = closed;
            Run($"hatch/polyline-closure/direct-clone/{c}", () => HatchPolylineClosureClone(c));
            foreach (DxfVersion version in SupportedVersions)
                foreach (bool binary in new[] { false, true })
                {
                    DxfVersion v = version; bool b = binary;
                    Run($"hatch/polyline-closure/wire/{v}/{b}/{c}", () => HatchPolylineClosureWire(v, b, c));
                    Run($"hatch/polyline-closure/nested/{v}/{b}/{c}", () => HatchPolylineClosureNested(v, b, c));
                }
        }
    }

    private static HatchBoundaryPath.Polyline ClosurePolyline(bool closed) => new()
    {
        IsClosed = closed,
        Vertexes = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(10, 10, 0), new Vector3(0, 10, 0) }
    };

    private static HatchBoundaryPath.Polyline ClosureEdge(Hatch hatch) =>
        (HatchBoundaryPath.Polyline)hatch.BoundaryPaths.Single().Edges.Single();

    private static void CheckClosure(HatchBoundaryPath.Polyline edge, bool closed)
    {
        Equal(closed, edge.IsClosed, "HATCH polyline closure flag");
        Equal(closed, ((Polyline2D)edge.ConvertTo()).IsClosed, "Closure changed in entity conversion");
        var segments = edge.Explode();
        Equal(closed ? 4 : 3, segments.Count, "Closing segment was added or lost");
        if (closed)
        {
            var last = (HatchBoundaryPath.Line)segments[^1];
            Equal(new Vector2(edge.Vertexes[^1].X, edge.Vertexes[^1].Y), last.Start, "Closing segment start");
            Equal(new Vector2(edge.Vertexes[0].X, edge.Vertexes[0].Y), last.End, "Closing segment end");
        }
    }

    private static void HatchPolylineClosureClone(bool closed)
    {
        var original = ClosurePolyline(closed);
        var copy = (HatchBoundaryPath.Polyline)original.Clone();
        CheckClosure(copy, closed);
        Check(original.Vertexes.SequenceEqual(copy.Vertexes), "Cloning changed vertices.");
        Check(!ReferenceEquals(original.Vertexes, copy.Vertexes), "Cloning aliases vertex storage.");
        copy.IsClosed = !closed; copy.Vertexes[0] = new Vector3(99, 100, 0);
        CheckClosure(original, closed);
        Equal(Vector3.Zero, original.Vertexes[0], "Editing clone changed source geometry");
    }

    private static void HatchPolylineClosureWire(DxfVersion version, bool binary, bool closed)
    {
        var tags = HatchDoubleTags(version, HatchType.UserDefined, 0);
        tags[tags.FindIndex(t => t.Code == 73)] = new DxfTag(73, closed ? (short)1 : (short)0);
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Closure fixture load failed.");
        Hatch original = document.Entities.Hatches.Single();
        CheckClosure(ClosureEdge(original), closed);
        document.Entities.Add((Hatch)original.Clone());
        for (int cycle = 0; cycle < 3; ++cycle)
        {
            using var output = new MemoryStream();
            bool transport = cycle == 1 ? binary : !binary;
            Check(document.Save(output, transport), "Closure round-trip save failed.");
            output.Position = 0;
            var raw = DxfRawDocument.Load(output);
            foreach (var record in raw.Sections.Single(s => s.Name == "ENTITIES").Records.Where(r => r.Name == "HATCH"))
                Equal(closed ? (short)1 : (short)0, (short)record.Tags.First(t => t.Code == 73).Value, "Emitted group 73 closure");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Closure round-trip load failed.");
            Equal(2, document.Entities.Hatches.Count(), "HATCH count after clone");
            foreach (Hatch hatch in document.Entities.Hatches)
            {
                CheckClosure(ClosureEdge(hatch), closed);
                Equal(2.5, hatch.Elevation, "Closure changed elevation");
                Equal("after pattern", (string)hatch.XData["DOUBLE_TEST"].XDataRecord.Single().Value, "Closure disrupted XData");
            }
            if (cycle == 0 && closed)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"hatch-closure-{version}-{transport}.dxf"), output.ToArray());
            Check(output.CanRead, "Closure round trip closed caller output.");
        }
        Check(input.CanRead, "Closure load closed caller input.");
    }

    private static void HatchPolylineClosureNested(DxfVersion version, bool binary, bool closed)
    {
        var original = new Hatch(HatchPattern.Solid, new[]
        {
            new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { ClosurePolyline(closed) })
        }, false);
        var block = new Block("ClosureBlock"); block.Entities.Add(original);
        var insert = new Insert(block);
        var clonedInsert = (Insert)insert.Clone();
        CheckClosure(ClosureEdge(clonedInsert.Block.Entities.OfType<Hatch>().Single()), closed);
        var exploded = insert.Explode().OfType<Hatch>().Single();
        if (closed) CheckClosure(ClosureEdge(exploded), true);
        else
        {
            // Existing TransformBy intentionally expands open polylines to edge segments.
            var edges = exploded.BoundaryPaths.Single().Edges;
            Equal(3, edges.Count, "Open exploded path acquired a closing segment");
            Check(edges.All(e => e is HatchBoundaryPath.Line), "Open polyline transformation changed straight edges.");
            Equal(new Vector2(0, 10), ((HatchBoundaryPath.Line)edges[^1]).End, "Open path endpoint");
        }
        var doc = new DxfDocument(version); doc.Entities.Add((Hatch)original.Clone());
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Authored closure save failed.");
        output.Position = 0;
        var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Authored closure load failed.");
        CheckClosure(ClosureEdge(loaded.Entities.Hatches.Single()), closed);
        CheckClosure(ClosureEdge(original), closed);
    }
}
