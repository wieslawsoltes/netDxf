// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] MteOperations = { "keep", "face", "edge", "crease", "negative", "zero", "same" };
    private static Vector3[] MtePoints() => new[] { new Vector3(1, 2, 3), new Vector3(4, 2, 3), new Vector3(1, 6, 3), new Vector3(4, 6, 3) };
    private static byte[]? MteCache(int kind) => kind == 0 ? null : kind == 1 ? Array.Empty<byte>() : new byte[] { 77, 84, 69, 0, 255 };
    private static Mesh MteSeed() => new(MtePoints(), new[] { new[] { 0, 1, 2 } }, new[] { new MeshEdge(0, 1, 0.5) })
    { SubdivisionLevel = 2, BlendCrease = true };
    private static long MteBits(double value) => BitConverter.DoubleToInt64Bits(value);
    private static void MteProxy(Mesh mesh, byte[]? expected) =>
        Check(expected == null ? mesh.ProxyGraphics == null : mesh.ProxyGraphics != null && expected.SequenceEqual(mesh.ProxyGraphics), "Topology edit proxy state");
    private static void MteReject(Action action)
    {
        bool rejected = false;
        try { action(); }
        catch (ArgumentException) { rejected = true; }
        catch (InvalidOperationException) { rejected = true; }
        catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid topology edit was accepted");
    }

    private static void RegisterMeshTopologyEditTests()
    {
        for (int cache = 0; cache < 3; cache++)
        {
            int c = cache;
            foreach (bool attached in new[] { false, true })
            {
                bool a = attached;
                Run($"mesh-topology-edit/operations/{cache}/{attached}", () => MteOperationsTest(c, a));
            }
            Run($"mesh-topology-edit/clone/{cache}", () => MteClone(c));
            Run($"mesh-topology-edit/aliases/{cache}", () => MteAliases(c));
            Run($"mesh-topology-edit/crease-bits/{cache}", () => MteCreaseBits(c));
            for (int fault = 0; fault < 14; fault++)
            {
                int f = fault;
                Run($"mesh-topology-edit/source-refusal/{cache}/{fault}", () => MteInvalidSource(c, f));
            }
        }
        Run("mesh-topology-edit/budget", MteBudget);
        foreach (var version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
            Run($"mesh-topology-edit/wire/{version}/{binary}", () => MteWire(version, binary));
    }

    private static void MteOperationsTest(int cache, bool attached)
    {
        var mesh = MteSeed(); var document = new DxfDocument(); if (attached) document.Entities.Add(mesh);
        var points = mesh.Vertexes; var faces = mesh.Faces; var face = faces[0]; var edges = mesh.Edges; var edge = edges[0];
        var owner = mesh.Owner; var layer = mesh.Layer; string? handle = mesh.Handle;
        using var pv = points.GetEnumerator(); using var fv = faces.GetEnumerator(); using var ev = edges.GetEnumerator();
        Check(pv.MoveNext() && fv.MoveNext() && ev.MoveNext(), "Fixture enumeration");
        mesh.ProxyGraphics = MteCache(cache);
        mesh.SetFaceVertexIndex(0, 2, 2); mesh.SetEdgeVertexIndices(0, 0, 1); mesh.SetEdgeCrease(0, 0.5);
        MteProxy(mesh, MteCache(cache));
        Action[] invalid = {
            () => mesh.SetFaceVertexIndex(-1, 0, 0), () => mesh.SetFaceVertexIndex(1, 0, 0),
            () => mesh.SetFaceVertexIndex(0, -1, 0), () => mesh.SetFaceVertexIndex(0, 3, 0),
            () => mesh.SetFaceVertexIndex(0, 1, -1), () => mesh.SetFaceVertexIndex(0, 1, 4),
            () => mesh.SetEdgeVertexIndices(-1, 0, 1), () => mesh.SetEdgeVertexIndices(1, 0, 1),
            () => mesh.SetEdgeVertexIndices(0, -1, 1), () => mesh.SetEdgeVertexIndices(0, 4, 1),
            () => mesh.SetEdgeVertexIndices(0, 2, -1), () => mesh.SetEdgeVertexIndices(0, 2, 4),
            () => mesh.SetEdgeCrease(-1, 1), () => mesh.SetEdgeCrease(1, 1),
            () => mesh.SetEdgeCrease(0, double.NaN), () => mesh.SetEdgeCrease(0, double.PositiveInfinity),
            () => mesh.SetEdgeCrease(0, double.NegativeInfinity)
        };
        foreach (var action in invalid)
        {
            MteReject(action); MteProxy(mesh, MteCache(cache));
            Check(face.SequenceEqual(new[] { 0, 1, 2 }) && edge.StartVertexIndex == 0 && edge.EndVertexIndex == 1 && edge.Crease == 0.5, "Refusal partially changed topology");
        }
        mesh.SetFaceVertexIndex(0, 2, 3); MteProxy(mesh, null);
        mesh.ProxyGraphics = MteCache(cache); mesh.SetEdgeVertexIndices(0, 2, 3); MteProxy(mesh, null);
        mesh.ProxyGraphics = MteCache(cache); mesh.SetEdgeCrease(0, 2.5); MteProxy(mesh, null);
        Check(face.SequenceEqual(new[] { 0, 1, 3 }) && edge.StartVertexIndex == 2 && edge.EndVertexIndex == 3 && edge.Crease == 2.5, "Incorrect changed topology");
        Check(ReferenceEquals(points, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces) && ReferenceEquals(face, mesh.Faces[0])
            && ReferenceEquals(edges, mesh.Edges) && ReferenceEquals(edge, mesh.Edges[0]) && ReferenceEquals(owner, mesh.Owner)
            && ReferenceEquals(layer, mesh.Layer) && handle == mesh.Handle, "Topology edit replaced identities");
        Check(points.SequenceEqual(MtePoints()) && mesh.SubdivisionLevel == 2 && mesh.BlendCrease && mesh.Normal == Vector3.UnitZ, "Unselected geometry/header changed");
        Check(pv.MoveNext() && !fv.MoveNext() && !ev.MoveNext(), "Topology edits invalidated collection enumerators");
        Check(document.Objects.Validate().Count == 0, "Graph invalid after topology edit");
    }

    private static void MteClone(int cache)
    {
        var source = MteSeed(); source.ProxyGraphics = MteCache(cache); var clone = (Mesh)source.Clone();
        MteProxy(clone, MteCache(cache));
        clone.SetFaceVertexIndex(0, 2, 3); clone.SetEdgeVertexIndices(0, 2, 3); clone.SetEdgeCrease(0, 2.5);
        MteProxy(clone, null); MteProxy(source, MteCache(cache));
        Check(source.Faces[0].SequenceEqual(new[] { 0, 1, 2 }) && source.Edges[0].StartVertexIndex == 0 && source.Edges[0].EndVertexIndex == 1 && source.Edges[0].Crease == 0.5, "Clone changed original");
    }

    private static void MteAliases(int cache)
    {
        var mesh = MteSeed(); mesh.Faces.Add(mesh.Faces[0]); mesh.Edges.Add(mesh.Edges[0]); mesh.ProxyGraphics = MteCache(cache);
        mesh.SetFaceVertexIndex(0, 2, 2); mesh.SetEdgeVertexIndices(0, 0, 1); mesh.SetEdgeCrease(0, 0.5);
        MteReject(() => mesh.SetFaceVertexIndex(0, 2, 3)); MteReject(() => mesh.SetEdgeVertexIndices(0, 2, 3));
        MteReject(() => mesh.SetEdgeCrease(0, 2.5)); MteProxy(mesh, MteCache(cache));
        Check(mesh.Faces[1][2] == 2 && mesh.Edges[1].StartVertexIndex == 0 && mesh.Edges[1].EndVertexIndex == 1 && mesh.Edges[1].Crease == 0.5, "Aliased record changed");
    }

    private static void MteCreaseBits(int cache)
    {
        var mesh = MteSeed();
        foreach (double value in new[] { 0.0, BitConverter.Int64BitsToDouble(long.MinValue), double.Epsilon, double.MaxValue, -double.Epsilon, -2.0 })
        {
            mesh.ProxyGraphics = MteCache(cache); long before = MteBits(mesh.Edges[0].Crease);
            double expected = value < 0 ? -1 : value; mesh.SetEdgeCrease(0, value);
            Check(MteBits(mesh.Edges[0].Crease) == MteBits(expected), "Crease scalar bits");
            MteProxy(mesh, before == MteBits(expected) ? MteCache(cache) : null);
            mesh.ProxyGraphics = MteCache(cache); mesh.SetEdgeCrease(0, value); MteProxy(mesh, MteCache(cache));
        }
    }

    private static void MteInvalidSource(int cache, int fault)
    {
        var mesh = MteSeed(); var original = mesh.Edges[0]; int[] face = mesh.Faces[0];
        switch (fault)
        {
            case 0: mesh.Vertexes[3] = new Vector3(double.NaN, 0, 0); break;
            case 1: mesh.Vertexes[3] = new Vector3(0, double.PositiveInfinity, 0); break;
            case 2: mesh.Vertexes[3] = new Vector3(0, 0, double.NegativeInfinity); break;
            case 3: mesh.Faces.Add(null!); break;
            case 4: mesh.Faces.Add(new[] { 0, 1 }); break;
            case 5: mesh.Faces.Add(new[] { 0, -1, 2 }); break;
            case 6: mesh.Faces.Add(new[] { 0, 4, 2 }); break;
            case 7: mesh.Edges.Add(null!); break;
            case 8: mesh.Edges.Add(new MeshEdge(4, 1)); break;
            case 9: mesh.Edges.Add(new MeshEdge(0, 4)); break;
            case 10: mesh.Edges.Add(new MeshEdge(0, 1, double.NaN)); break;
            case 11: mesh.Edges.Add(new MeshEdge(0, 1, double.PositiveInfinity)); break;
            case 12: mesh.Vertexes.RemoveAt(2); mesh.Vertexes.RemoveAt(2); break;
            case 13: mesh.Faces.Add(Array.Empty<int>()); break;
        }
        mesh.ProxyGraphics = MteCache(cache);
        // Even valid-looking or identical assignments may not bypass corrupt current state.
        MteReject(() => mesh.SetFaceVertexIndex(0, 1, 1));
        MteReject(() => mesh.SetEdgeVertexIndices(0, 0, 1));
        MteReject(() => mesh.SetEdgeCrease(0, 0.5)); MteProxy(mesh, MteCache(cache));
        Check(face.SequenceEqual(new[] { 0, 1, 2 }) && original.StartVertexIndex == 0 && original.EndVertexIndex == 1 && original.Crease == 0.5, "Source refusal changed selected record");
    }

    private static void MteBudget()
    {
        var mesh = MteSeed(); var large = new int[2000000]; mesh.Faces.Add(large); mesh.Faces.Add(large);
        mesh.ProxyGraphics = MteCache(2); MteReject(() => mesh.SetEdgeCrease(0, 2)); MteProxy(mesh, MteCache(2));
        mesh.Faces.RemoveRange(1, 2);
        mesh.Vertexes.AddRange(new Vector3[4000001]); MteReject(() => mesh.SetFaceVertexIndex(0, 1, 1)); MteProxy(mesh, MteCache(2));
    }

    private static void MteApply(Mesh mesh, string operation)
    {
        if (operation == "face") mesh.SetFaceVertexIndex(0, 2, 3);
        else if (operation == "edge") mesh.SetEdgeVertexIndices(0, 2, 3);
        else if (operation == "crease") mesh.SetEdgeCrease(0, 2.5);
        else if (operation == "negative") mesh.SetEdgeCrease(0, -7);
        else if (operation == "zero") mesh.SetEdgeCrease(0, BitConverter.Int64BitsToDouble(long.MinValue));
        else if (operation == "same") { mesh.SetFaceVertexIndex(0, 2, 2); mesh.SetEdgeVertexIndices(0, 0, 1); mesh.SetEdgeCrease(0, 0.5); }
    }

    private static void MteVerify(Mesh mesh, int cache, string operation, bool source)
    {
        Check(mesh.Vertexes.SequenceEqual(MtePoints()) && mesh.Faces.Count == 1 && mesh.Edges.Count == 1, "Wire geometry/count");
        Check(mesh.Faces[0].SequenceEqual(new[] { 0, 1, !source && operation == "face" ? 3 : 2 }), "Wire face");
        bool edge = !source && operation == "edge";
        Check(mesh.Edges[0].StartVertexIndex == (edge ? 2 : 0) && mesh.Edges[0].EndVertexIndex == (edge ? 3 : 1), "Wire edge");
        double crease = source ? 0.5 : operation == "crease" ? 2.5 : operation == "negative" ? -1 : operation == "zero" ? BitConverter.Int64BitsToDouble(long.MinValue) : 0.5;
        Check(MteBits(mesh.Edges[0].Crease) == MteBits(crease), "Wire crease bits");
        Check(mesh.BlendCrease && mesh.SubdivisionLevel == 2 && mesh.Normal == Vector3.UnitZ, "Wire unselected header");
        MteProxy(mesh, source || operation == "keep" || operation == "same" ? MteCache(cache) : null);
    }

    private static void MteWire(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var subjects = new List<(string Handle, string Name, int Cache, string Operation)>();
        for (int place = 0; place < 4; place++)
        {
            var block = place == 0 ? document.Layouts["Model"].AssociatedBlock : new Block("MT_CONTAINER_" + place);
            if (place == 1) { var layout = new Layout("MT_PAPER"); document.Layouts.Add(layout); block = layout.AssociatedBlock; }
            else if (place == 2) document.Entities.Add(new Insert(block));
            else if (place == 3) document.Blocks.Add(block);
            for (int cache = 0; cache < 3; cache++)
            foreach (string operation in MteOperations)
            {
                var mesh = MteSeed(); string name = $"MT_{place}_{cache}_{operation}";
                var data = new XData(new ApplicationRegistry("MESH_TOPOLOGY_EDIT")); data.XDataRecord.Add(new XDataRecord(XDataCode.String, name)); mesh.XData.Add(data);
                block.Entities.Add(mesh); mesh.ProxyGraphics = MteCache(cache);
                subjects.Add((mesh.Handle, name, cache, operation));
            }
        }
        var line = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); document.Entities.Add(line);
        string prefix = $"mesh-topology-edit-{version}-{(binary ? "binary" : "text")}";
        for (int stage = 0; stage < 3; stage++)
        {
            using var stream = new MemoryStream(); Check(document.Save(stream, stage == 2 ? !binary : binary) && stream.CanWrite, "Wire save/lifetime");
            byte[] bytes = stream.ToArray(); File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + new[] { "-source.dxf", "-output.dxf", "-resave.dxf" }[stage]), bytes);
            byte[] before = (byte[])bytes.Clone(); using var input = new MemoryStream(bytes);
            document = DxfDocument.Load(input) ?? throw new InvalidOperationException("Wire load");
            Check(input.CanRead && before.SequenceEqual(bytes), "Wire load/lifetime/source");
            foreach (var spec in subjects)
            {
                var mesh = document.GetObjectByHandle(spec.Handle) as Mesh ?? throw new InvalidOperationException("Wire identity");
                Equal(spec.Name, (string)mesh.XData["MESH_TOPOLOGY_EDIT"].XDataRecord.Single().Value, "Wire XData");
                MteVerify(mesh, spec.Cache, spec.Operation, stage == 0);
                if (stage == 0) MteApply(mesh, spec.Operation);
            }
            var following = document.GetObjectByHandle(line.Handle) as Line;
            Check(following != null && following.StartPoint == line.StartPoint && following.EndPoint == line.EndPoint, "Wire following line");
            Check(document.Objects.Validate().Count == 0, "Wire graph");
        }
    }
}
