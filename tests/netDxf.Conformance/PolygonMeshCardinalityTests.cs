using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterPolygonMeshCardinalityTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (short smooth in new short[] { 0, 5, 6 })
                {
                    short s = smooth;
                    Run($"polygonmesh/cardinality/valid/{v}/{b}/{s}", () => PolygonGridValid(v, b, s));
                    foreach (int delta in new[] { -16, -1, 1 })
                    {
                        int d = delta;
                        Run($"polygonmesh/cardinality/count/{v}/{b}/{s}/{d}", () => PolygonGridInvalid(v, b, s, d));
                    }
                }
                foreach (short count in new short[] { short.MinValue, -1, 0, 1, 257, short.MaxValue })
                    foreach (bool u in new[] { false, true })
                    {
                        short n = count; bool axis = u;
                        Run($"polygonmesh/cardinality/range/{v}/{b}/{n}/{axis}", () => PolygonGridRange(v, b, n, axis));
                    }
                foreach (int invalid in Enumerable.Range(0, 9))
                {
                    int i = invalid;
                    Run($"polygonmesh/cardinality/type/{v}/{b}/{i}", () => PolygonGridType(v, b, i));
                }
                foreach (int placement in Enumerable.Range(0, 4))
                    foreach (int invalid in Enumerable.Range(0, 5))
                    {
                        int p = placement, i = invalid;
                        Run($"polygonmesh/output-preflight/{v}/{b}/{p}/{i}", () => PolygonGridOutput(v, b, p, i));
                    }
                Run($"polygonmesh/cardinality/density-default/{v}/{b}", () => PolygonGridDensity(v, b));
                Run($"polygonmesh/output-preflight/periodic-control/{v}/{b}", () => PolygonGridPeriodic(v, b));
                Run($"polygonmesh/cardinality/polyface-advisory/{v}/{b}", () => PolygonGridPolyfaceControl(v, b));
                Run($"polygonmesh/cardinality/bounds/{v}/{b}", () => PolygonGridBounds(v, b, false));
                foreach (string mode in new[] { "plain", "quadratic", "cubic" })
                {
                    string m = mode;
                    Run($"polygonmesh/producer/{v}/{b}/{m}", () => PolygonGridProducer(v, b, m));
                }
            }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"polygonmesh/cardinality/maximum-product/{b}", () => PolygonGridBounds(DxfVersion.AutoCad2018, b, true));
            foreach (string native in new[] { "R2000-native-20F.dxf", "R2018-native-20F.dxf" })
            {
                string n = native;
                Run($"polygonmesh/native/{n}/{b}", () => PolygonGridNative(n, b));
            }
        }
        Run("polygonmesh/model/invalid-smooth-type", () =>
        {
            var mesh = new PolygonMesh(2, 2, new Vector3[4]);
            Throws<ArgumentOutOfRangeException>(() => mesh.SmoothType = (PolylineSmoothType)8);
            Equal(PolylineSmoothType.NoSmooth, mesh.SmoothType, "Rejected enum mutated surface");
            Throws<ArgumentException>(() => new PolygonMesh(2, 2, new Vector3[3]));
            Throws<ArgumentException>(() => new PolygonMesh(2, 2, new Vector3[5]));
        });
    }

    private static List<DxfTag> PolygonGridTags(DxfVersion version, short smooth = 0, int gridCount = 16, int generated = 20)
    {
        var tags = new List<DxfTag>
        {
            new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)), new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, "ENTITIES"), new(0, "POLYLINE"), new(5, "200"), new(100, "AcDbEntity"),
            new(8, "0"), new(100, "AcDbPolygonMesh"), new(66, (short)1), new(10, 0.0), new(20, 0.0), new(30, 0.0),
            new(70, (short)(smooth == 0 ? 16 : 20)), new(71, (short)4), new(72, (short)4),
            new(73, (short)4), new(74, (short)5), new(75, smooth), new(1001, "GRID_TEST"), new(1000, "grid metadata")
        };
        int handle = 0x201;
        void Vertex(short flags, double x, double y, double z)
        {
            tags.AddRange(new DxfTag[] { new(0, "VERTEX"), new(5, (handle++).ToString("X")), new(330, "200"),
                new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbVertex"), new(100, "AcDbPolygonMeshVertex"),
                new(10, x), new(20, y), new(30, z), new(70, flags) });
        }
        for (int i = 0; i < gridCount; i++) Vertex((short)(smooth == 0 ? 64 : 80), i / 4 + 1e-20, i % 4, i * 0.125);
        if (smooth != 0)
            for (int i = 0; i < generated; i++) Vertex(72, i / 5, i % 5, -100.0 - i);
        tags.AddRange(new DxfTag[] { new(0, "SEQEND"), new(5, (handle++).ToString("X")), new(330, "200"), new(100, "AcDbEntity"), new(8, "0"),
            new(0, "LINE"), new(5, "300"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
            new(10, 7.0), new(20, 8.0), new(30, 9.0), new(11, 10.0), new(21, 11.0), new(31, 12.0),
            new(0, "ENDSEC"), new(0, "EOF") });
        return tags;
    }

    private static void PolygonGridReject(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Malformed polygon grid was accepted."); }
        catch (InvalidDataException error) { Check(error.Message.Contains("POLYGONMESH", StringComparison.Ordinal), "Missing polygon mesh diagnostic."); }
#else
        Check(DxfDocument.Load(input) == null, "Malformed polygon grid was accepted.");
#endif
        Check(input.CanRead, "Grid rejection closed the caller's stream.");
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Invalid grid declaration caused a large allocation.");
    }

    private static void PolygonGridInvalid(DxfVersion version, bool binary, short smooth, int delta) =>
        PolygonGridReject(PolygonGridTags(version, smooth, 16 + delta), binary);

    private static void PolygonGridRange(DxfVersion version, bool binary, short count, bool u)
    {
        var tags = PolygonGridTags(version); short code = (short)(u ? 71 : 72);
        tags[tags.FindIndex(t => t.Code == code)] = new(code, count); PolygonGridReject(tags, binary);
    }

    private static void PolygonGridType(DxfVersion version, bool binary, int invalid)
    {
        var tags = PolygonGridTags(version, invalid < 2 ? (short)0 : (short)5);
        if (invalid == 0) tags[tags.FindIndex(t => t.Code == 75)] = new(75, (short)5);
        else if (invalid == 1) tags[tags.FindIndex(t => t.Code == 70)] = new(70, (short)20);
        else if (invalid < 4) tags[tags.FindIndex(t => t.Code == 75)] = new(75, (short)(invalid == 2 ? 8 : 42));
        else
        {
            int firstVertex = tags.FindIndex(t => t.Code == 0 && Equals(t.Value, "VERTEX"));
            int flag = tags.FindIndex(firstVertex, t => t.Code == 70);
            tags[flag] = new(70, new short[] { 64, 16, 88, 192, 0 }[invalid - 4]);
        }
        PolygonGridReject(tags, binary);
    }

    private static DxfDocument PolygonGridLoad(List<DxfTag> tags, bool binary)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        return DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid polygon grid failed to load.");
    }

    private static void PolygonGridValid(DxfVersion version, bool binary, short smooth)
    {
        foreach (int samples in smooth == 0 ? new[] { 0 } : new[] { 0, 7, 20, 23 })
        {
            var doc = PolygonGridLoad(PolygonGridTags(version, smooth, 16, samples), binary);
            var mesh = doc.Entities.PolygonMeshes.Single();
            Equal((short)4, mesh.U, "Grid U"); Equal((short)4, mesh.V, "Grid V");
            Equal((PolylineSmoothType)smooth, mesh.SmoothType, "Grid surface type");
            for (int i = 0; i < 16; i++) Equal(new Vector3(i / 4 + 1e-20, i % 4, i * 0.125), mesh.GetVertex(i / 4, i % 4), "Grid wire ordering");
            Equal("grid metadata", (string)mesh.XData["GRID_TEST"].XDataRecord.Single().Value, "Grid XData");
            Equal(new Vector3(7, 8, 9), doc.Entities.Lines.Single().StartPoint, "Following entity boundary");
            for (int cycle = 0; cycle < 2; cycle++)
            {
                using var output = new MemoryStream(); Check(doc.Save(output, cycle == 0 ? binary : !binary), "Grid save failed.");
                if (samples == (smooth == 0 ? 0 : 20))
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"polygonmesh-grid-{version}-{binary}-{smooth}-{cycle}.dxf"), output.ToArray());
                output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Grid reload failed.");
                Check(mesh.Vertexes.SequenceEqual(doc.Entities.PolygonMeshes.Single().Vertexes), "Grid control coordinates changed.");
            }
        }
    }

    private static void PolygonGridDensity(DxfVersion version, bool binary)
    {
        var tags = PolygonGridTags(version, 5); tags.RemoveAll(t => t.Code is 73 or 74);
        var doc = PolygonGridLoad(tags, binary); var mesh = doc.Entities.PolygonMeshes.Single();
        Equal((short)0, mesh.DensityU, "Omitted U density"); Equal((short)0, mesh.DensityV, "Omitted V density");
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Default density save failed.");
    }

    private static void PolygonGridOutput(DxfVersion version, bool binary, int placement, int invalid)
    {
        var doc = new DxfDocument(version);
        var mesh = new PolygonMesh(2, 2, new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ });
        if (invalid < 3) mesh.Vertexes[1] = new Vector3(invalid == 0 ? double.NaN : invalid == 1 ? double.PositiveInfinity : double.NegativeInfinity, 0, 0);
        else mesh.SmoothType = invalid == 3 ? PolylineSmoothType.Quadratic : PolylineSmoothType.Cubic;
        if (placement == 0) doc.Entities.Add(mesh);
        else if (placement == 1) { doc.Layouts.Add(new netDxf.Objects.Layout("Paper")); doc.Entities.ActiveLayout = "Paper"; doc.Entities.Add(mesh); }
        else { var block = new Block("GridBlock"); block.Entities.Add(mesh); if (placement == 2) doc.Entities.Add(new Insert(block)); else doc.Blocks.Add(block); }
        byte[] bytes = { 12, 34, 56 }; using var output = new MemoryStream(); output.Write(bytes); output.Position = 1;
        string handles = doc.DrawingVariables.HandleSeed; int layouts = doc.Layouts.Count, apps = doc.ApplicationRegistries.Count;
#if DEBUG
        Throws<InvalidOperationException>(() => doc.Save(output, binary));
#else
        Check(!doc.Save(output, binary), "Invalid polygon mesh export succeeded.");
#endif
        Check(output.ToArray().SequenceEqual(bytes), "Rejected polygon mesh changed destination bytes."); Equal(1L, output.Position, "Rejected polygon mesh changed stream position");
        Equal(handles, doc.DrawingVariables.HandleSeed, "Rejected polygon mesh allocated handles"); Equal(layouts, doc.Layouts.Count, "Rejected polygon mesh added layouts"); Equal(apps, doc.ApplicationRegistries.Count, "Rejected polygon mesh added registrations");
        Throws<InvalidOperationException>(() => mesh.MeshVertexes(4, 4));
    }

    private static void PolygonGridBounds(DxfVersion version, bool binary, bool maximum)
    {
        foreach (var size in maximum ? new[] { (U: (short)256, V: (short)256) } : new[] { (U: (short)2, V: (short)256), (U: (short)256, V: (short)2) })
        {
            var vertices = Enumerable.Range(0, size.U * size.V).Select(i => new Vector3(i, i * .25, -i)).ToArray();
            var doc = new DxfDocument(version); doc.Entities.Add(new PolygonMesh(size.U, size.V, vertices));
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Boundary grid failed to save."); output.Position = 0;
            var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Boundary grid failed to load.");
            Check(vertices.SequenceEqual(loaded.Entities.PolygonMeshes.Single().Vertexes), "Boundary grid changed vertex coordinates.");
        }
    }

    private static void PolygonGridPolyfaceControl(DxfVersion version, bool binary)
    {
        var doc = new DxfDocument(version); doc.Entities.Add(new PolyfaceMesh(new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY }, new[] { new short[] { 1, 2, 3 } }));
        using var saved = new MemoryStream(); Check(doc.Save(saved, binary), "Polyface setup save failed."); saved.Position = 0;
        var tags = DxfRawDocument.Load(saved).Tags.ToList(); int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbPolyFaceMesh"));
        int end = tags.FindIndex(start, t => t.Code == 0); tags.InsertRange(end, new[] { new DxfTag(71, (short)0), new DxfTag(72, short.MaxValue) });
        var loaded = PolygonGridLoad(tags, binary); Equal(3, loaded.Entities.PolyfaceMeshes.Single().Vertexes.Count(), "Advisory POLYFACE coordinate count");
        Equal(1, loaded.Entities.PolyfaceMeshes.Single().Faces.Count(), "Advisory POLYFACE face count");
    }

    private static void PolygonGridProducer(DxfVersion version, bool binary, string mode)
    {
        string file = $"{HeaderVersion(version)}-{(binary ? "binary" : "ascii")}-{mode}.dxf";
        string path = Path.Combine("tests", "fixtures", "polygonmesh-cardinality", file);
        var doc = DxfDocument.Load(path) ?? throw new InvalidOperationException("Producer grid failed to load: " + file);
        var mesh = doc.Entities.PolygonMeshes.Single(); Equal((short)4, mesh.U, "Producer U"); Equal((short)4, mesh.V, "Producer V");
        for (int i = 0; i < 16; i++) Equal(new Vector3(i / 4, i % 4, (i / 4) * (i % 4) * .125), mesh.GetVertex(i / 4, i % 4), "Producer grid coordinates");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Producer grid cross-transport save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, "polygonmesh-producer-" + file), output.ToArray());
        output.Position = 0; var reload = DxfDocument.Load(output) ?? throw new InvalidOperationException("Producer grid reload failed.");
        Check(mesh.Vertexes.SequenceEqual(reload.Entities.PolygonMeshes.Single().Vertexes), "Producer grid coordinates changed.");
    }
    private static void PolygonGridNative(string name, bool binary)
    {
        const string fixtureDirectory = "tests/fixtures/polygonmesh-cardinality";
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtureDirectory, "native-manifest.json")));
        var row = manifest.RootElement.EnumerateArray().Single(r => r.GetProperty("carrier").GetString() == name);
        var expected = row.GetProperty("points").EnumerateArray().Select(v => new Vector3(v[0].GetDouble(), v[1].GetDouble(), v[2].GetDouble())).ToArray();
        var doc = DxfDocument.Load(Path.Combine(fixtureDirectory, name)) ?? throw new InvalidOperationException("Native grid carrier failed to load.");
        for (int cycle = 0; cycle < 2; cycle++)
        {
            var mesh = doc.Entities.PolygonMeshes.Single(); Equal((short)3, mesh.U, "Native M"); Equal((short)4, mesh.V, "Native N");
            Equal(PolylineSmoothType.NoSmooth, mesh.SmoothType, "Native smooth type");
            for (int i = 0; i < expected.Length; i++) Equal(expected[i], mesh.GetVertex(i / 4, i % 4), "Native physical coordinate order");
            using var output = new MemoryStream(); Check(doc.Save(output, cycle == 0 ? binary : !binary), "Native grid export failed.");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"polygonmesh-native-{name}-{binary}-{cycle}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Native grid reload failed.");
        }
    }

    private static void PolygonGridPeriodic(DxfVersion version, bool binary)
    {
        foreach (short degree in new short[] { 2, 3 })
        {
            var mesh = new PolygonMesh(degree, degree, Enumerable.Range(0, degree * degree).Select(i => new Vector3(i / degree, i % degree, i * .125)))
            { SmoothType = degree == 2 ? PolylineSmoothType.Quadratic : PolylineSmoothType.Cubic, IsClosedInU = true, IsClosedInV = true, DensityU = 4, DensityV = 5 };
            var doc = new DxfDocument(version); doc.Entities.Add(mesh);
            var samples = mesh.MeshVertexes(4, 5); Equal(20, samples.Count, "Periodic sample count");
            Check(samples.All(p => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z)), "Periodic samples are not finite.");
            using var output = new MemoryStream(); Check(doc.Save(output, binary), "Admitted periodic control grid failed to save."); output.Position = 0;
            var reload = DxfDocument.Load(output) ?? throw new InvalidOperationException("Periodic grid reload failed.");
            Check(mesh.Vertexes.SequenceEqual(reload.Entities.PolygonMeshes.Single().Vertexes), "Periodic controls changed.");
        }
    }

}
