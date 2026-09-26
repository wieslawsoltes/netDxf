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
    private static readonly string[] MtcOperations = { "keep", "face-first", "face-last", "face-remove",
        "face-forward", "face-backward", "face-same", "edge-first", "edge-last", "edge-remove",
        "edge-forward", "edge-backward", "edge-same", "sequence" };
    private static Vector3[] MtcPoints() => new[] { new Vector3(-0.0, 2, 3), new Vector3(4, 2, 3),
        new Vector3(1, 6, 3), new Vector3(4, 6, 3), new Vector3(10, 11, 12), new Vector3(14, 15, 16) };
    private static Mesh MtcSeed() => new(MtcPoints(), new[] { new[] { 0, 1, 2 }, new[] { 3, 2, 1 }, new[] { 0, 1, 3, 2 } },
        new[] { new MeshEdge(0, 1, -0.0), new MeshEdge(2, 3, .5), new MeshEdge(0, 3, -1) })
        { SubdivisionLevel = 2, BlendCrease = true };
    private static byte[]? MtcCache(int state) => state == 0 ? null : state == 1 ? Array.Empty<byte>() : new byte[] { 77, 67, 0, 255 };
    private static string MtcEdge(MeshEdge? edge) => edge == null ? "null" : $"{edge.StartVertexIndex},{edge.EndVertexIndex},{BitConverter.DoubleToInt64Bits(edge.Crease)}";
    private static string MtcState(Mesh mesh) => string.Join(";", mesh.Vertexes.Select(MrPoint)) + "|"
        + string.Join(";", mesh.Faces.Select(f => f == null ? "null" : string.Join(",", f))) + "|"
        + string.Join(";", mesh.Edges.Select(MtcEdge));
    private static bool MtcChanges(string op) => op != "keep" && !op.EndsWith("same", StringComparison.Ordinal);

    private static void RegisterMeshTopologyCollectionTests()
    {
        for (int cache = 0; cache < 3; cache++)
        {
            int c = cache;
            foreach (bool attached in new[] { false, true }) foreach (bool face in new[] { false, true })
            {
                bool a = attached, f = face;
                for (int i = 0; i <= 3; i++) { int slot = i; Run($"mesh-collections/insert/{c}/{a}/{f}/{i}", () => MtcEdit(c, a, f, 0, slot, 0)); }
                for (int i = 0; i < 3; i++)
                {
                    int slot = i; Run($"mesh-collections/remove/{c}/{a}/{f}/{i}", () => MtcEdit(c, a, f, 1, slot, 0));
                    for (int j = 0; j < 3; j++) { int to = j; Run($"mesh-collections/move/{c}/{a}/{f}/{i}/{j}", () => MtcEdit(c, a, f, 2, slot, to)); }
                }
            }
            Run("mesh-collections/candidates/" + c, () => MtcCandidates(c));
            Run("mesh-collections/aliases/" + c, () => MtcAliases(c));
            Run("mesh-collections/empty-and-clone/" + c, () => MtcEmptyAndClone(c));
            Run("mesh-collections/crease-bits/" + c, () => MtcCreaseBits(c));
            for (int fault = 0; fault < 9; fault++) { int f = fault; Run($"mesh-collections/source/{c}/{f}", () => MtcBadSource(c, f)); }
        }
        Run("mesh-collections/face-budget", MtcFaceBudget);
        Run("mesh-collections/edge-budget", MtcEdgeBudget);
        Run("mesh-collections/vertex-budget", MtcVertexBudget);
        foreach (var version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
            Run($"mesh-collections/wire/{version}/{binary}", () => MtcWire(version, binary));
    }

    private static void MtcEdit(int cache, bool attached, bool face, int operation, int index, int to)
    {
        var mesh = MtcSeed(); var document = new DxfDocument(); if (attached) document.Entities.Add(mesh);
        var points = mesh.Vertexes; var faces = mesh.Faces; var edges = mesh.Edges;
        var expectedFaces = faces.ToList(); var expectedEdges = edges.ToList();
        var faceValues = faces.Select(a => (int[])a.Clone()).ToArray(); var edgeValues = edges.Select(MtcEdge).ToArray();
        var originalFaces = faces.ToArray(); var originalEdges = edges.ToArray();
        var owner = mesh.Owner; var layer = mesh.Layer; var data = mesh.XData; string? handle = mesh.Handle;
        int pc = points.Capacity, fc = faces.Capacity, ec = edges.Capacity;
        using var pv = points.GetEnumerator(); using var fv = faces.GetEnumerator(); using var ev = edges.GetEnumerator();
        Check(pv.MoveNext() && fv.MoveNext() && ev.MoveNext(), "Collection fixture empty");
        mesh.ProxyGraphics = MtcCache(cache);
        int[] supplied = { 4, 5, 0 };
        if (operation == 0)
        {
            if (face)
            {
                mesh.InsertFace(index, supplied);
                Check(!ReferenceEquals(mesh.Faces[index], supplied) && mesh.Faces[index].SequenceEqual(supplied), "Inserted face must be an independent value copy");
                supplied[0] = 3; Check(mesh.Faces[index][0] == 4, "Input array aliases inserted face");
                expectedFaces.Insert(index, mesh.Faces[index]);
            }
            else
            {
                mesh.InsertEdge(index, 4, 5, 2.5);
                Check(mesh.Edges[index].StartVertexIndex == 4 && mesh.Edges[index].EndVertexIndex == 5
                    && mesh.Edges[index].Crease == 2.5 && !originalEdges.Any(e => ReferenceEquals(e, mesh.Edges[index])), "Incorrect inserted edge");
                expectedEdges.Insert(index, mesh.Edges[index]);
            }
        }
        else if (operation == 1)
        {
            if (face) { mesh.RemoveFaceAt(index); expectedFaces.RemoveAt(index); }
            else { mesh.RemoveEdgeAt(index); expectedEdges.RemoveAt(index); }
        }
        else if (face)
        {
            mesh.MoveFace(index, to); var item = expectedFaces[index]; expectedFaces.RemoveAt(index); expectedFaces.Insert(to, item);
        }
        else
        {
            mesh.MoveEdge(index, to); var item = expectedEdges[index]; expectedEdges.RemoveAt(index); expectedEdges.Insert(to, item);
        }
        bool changed = operation != 2 || index != to;
        MrProxy(mesh, changed ? null : MtcCache(cache));
        Check(faces.Count == expectedFaces.Count && faces.Zip(expectedFaces).All(p => ReferenceEquals(p.First, p.Second)), "Wrong face slot/order/identity");
        Check(edges.Count == expectedEdges.Count && edges.Zip(expectedEdges).All(p => ReferenceEquals(p.First, p.Second)), "Wrong edge slot/order/identity");
        for (int i = 0; i < originalFaces.Length; i++) Check(originalFaces[i].SequenceEqual(faceValues[i]), "Existing face contents changed");
        Check(originalEdges.Select(MtcEdge).SequenceEqual(edgeValues), "Existing edge data or crease bits changed");
        Check(ReferenceEquals(points, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces) && ReferenceEquals(edges, mesh.Edges)
            && ReferenceEquals(owner, mesh.Owner) && ReferenceEquals(layer, mesh.Layer) && ReferenceEquals(data, mesh.XData)
            && handle == mesh.Handle && mesh.SubdivisionLevel == 2 && mesh.BlendCrease && mesh.Normal == Vector3.UnitZ,
            "Collection edit changed common identities/headers");
        Check(points.Select(MrPoint).SequenceEqual(MtcPoints().Select(MrPoint)) && points.Capacity == pc && pv.MoveNext(), "Coordinate storage changed");
        if (!face || !changed) Check(fv.MoveNext(), "Unchanged face enumerator invalidated");
        if (face || !changed) Check(ev.MoveNext(), "Unchanged edge enumerator invalidated");
        if (operation != 0) Check(faces.Capacity == fc && edges.Capacity == ec, "Removal/move changed capacity");
        if (attached) Check(ReferenceEquals(mesh, document.GetObjectByHandle(handle)), "Parent handle lost");
        Check(document.Objects.Validate().Count == 0, "Collection edit damaged database graph");
    }

    private static void MtcRefuse(Mesh mesh, Action action, int cache)
    {
        mesh.ProxyGraphics = MtcCache(cache); string before = MtcState(mesh);
        int vc = mesh.Vertexes.Capacity, fc = mesh.Faces.Capacity, ec = mesh.Edges.Capacity;
        var faces = mesh.Faces.ToArray(); var edges = mesh.Edges.ToArray();
        using var vv = mesh.Vertexes.GetEnumerator(); using var fv = mesh.Faces.GetEnumerator(); using var ev = mesh.Edges.GetEnumerator();
        MteReject(action);
        Check(before == MtcState(mesh) && vc == mesh.Vertexes.Capacity && fc == mesh.Faces.Capacity && ec == mesh.Edges.Capacity,
            "Rejected collection edit changed state/capacity");
        Check(faces.Zip(mesh.Faces).All(p => ReferenceEquals(p.First, p.Second)) && edges.Zip(mesh.Edges).All(p => ReferenceEquals(p.First, p.Second)), "Rejected edit replaced identities");
        MrProxy(mesh, MtcCache(cache)); vv.MoveNext(); fv.MoveNext(); ev.MoveNext();
    }

    private static void MtcCandidates(int cache)
    {
        var mesh = MtcSeed();
        foreach (int slot in new[] { -1, 4 })
        {
            MtcRefuse(mesh, () => mesh.InsertFace(slot, 0, 1, 2), cache);
            MtcRefuse(mesh, () => mesh.InsertEdge(slot, 0, 1), cache);
        }
        foreach (int slot in new[] { -1, 3 })
        {
            MtcRefuse(mesh, () => mesh.RemoveFaceAt(slot), cache); MtcRefuse(mesh, () => mesh.RemoveEdgeAt(slot), cache);
            MtcRefuse(mesh, () => mesh.MoveFace(slot, 0), cache); MtcRefuse(mesh, () => mesh.MoveFace(0, slot), cache);
            MtcRefuse(mesh, () => mesh.MoveEdge(slot, 0), cache); MtcRefuse(mesh, () => mesh.MoveEdge(0, slot), cache);
        }
        MtcRefuse(mesh, () => mesh.InsertFace(0, null!), cache);
        foreach (int[] indices in new[] { Array.Empty<int>(), new[] { 0 }, new[] { 0, 1 }, new[] { 0, 1, -1 }, new[] { 0, 1, 6 } })
            MtcRefuse(mesh, () => mesh.InsertFace(0, indices), cache);
        foreach (int i in new[] { -1, 6 })
        {
            MtcRefuse(mesh, () => mesh.InsertEdge(0, i, 1), cache); MtcRefuse(mesh, () => mesh.InsertEdge(0, 1, i), cache);
        }
        foreach (double crease in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            MtcRefuse(mesh, () => mesh.InsertEdge(1, 4, 5, crease), cache);
        // Existing API admission does not pretend to validate manifoldness or repair duplicates.
        mesh.InsertFace(0, 0, 0, 0); mesh.InsertEdge(0, 0, 0);
        Check(mesh.Faces[0].SequenceEqual(new[] { 0, 0, 0 }) && mesh.Edges[0].StartVertexIndex == mesh.Edges[0].EndVertexIndex, "Unexpected degeneracy policy change");
    }

    private static void MtcBadSource(int cache, int fault)
    {
        var mesh = MtcSeed();
        switch (fault)
        {
            case 0: mesh.Vertexes[5] = new Vector3(1, double.NaN, 3); break;
            case 1: mesh.Faces[2] = null!; break;
            case 2: mesh.Faces[2] = new[] { 0, 1 }; break;
            case 3: mesh.Faces[2][3] = -1; break;
            case 4: mesh.Faces[2][3] = 6; break;
            case 5: mesh.Edges[2] = null!; break;
            case 6: mesh.Edges[2].EndVertexIndex = 6; break;
            case 7: mesh.Edges[2].Crease = double.NaN; break;
            case 8: mesh.Edges[2].Crease = double.PositiveInfinity; break;
        }
        MtcRefuse(mesh, () => mesh.InsertFace(0, 0, 1, 2), cache); MtcRefuse(mesh, () => mesh.InsertEdge(0, 0, 1), cache);
        MtcRefuse(mesh, () => mesh.RemoveFaceAt(2), cache); MtcRefuse(mesh, () => mesh.RemoveEdgeAt(2), cache);
        MtcRefuse(mesh, () => mesh.MoveFace(0, 0), cache); MtcRefuse(mesh, () => mesh.MoveEdge(0, 0), cache);
        MtcRefuse(mesh, () => mesh.MoveFace(0, 2), cache); MtcRefuse(mesh, () => mesh.MoveEdge(0, 2), cache);
    }

    private static void MtcAliases(int cache)
    {
        var mesh = MtcSeed(); var face = mesh.Faces[0]; var edge = new MrHostileEdge();
        mesh.Faces.Add(face); mesh.Edges[0] = edge; mesh.Edges.Add(edge); mesh.ProxyGraphics = MtcCache(cache);
        mesh.InsertFace(1, face);
        Check(!ReferenceEquals(face, mesh.Faces[1]) && face.SequenceEqual(mesh.Faces[1]), "Borrowed rather than copied existing face input");
        mesh.MoveFace(0, 4); mesh.RemoveFaceAt(3);
        Check(ReferenceEquals(face, mesh.Faces[3]) && face.SequenceEqual(new[] { 0, 1, 2 }), "Face alias removal mutated retained alias");
        mesh.MoveEdge(0, 3); mesh.RemoveEdgeAt(2);
        Check(ReferenceEquals(edge, mesh.Edges[2]) && edge.StartVertexIndex == 0 && edge.EndVertexIndex == 2, "Edge alias removal changed shared object");
        mesh.InsertEdge(0, 0, 2, -0.0); Check(!ReferenceEquals(mesh.Edges[0], edge), "Inserted edge aliases an existing object"); MrProxy(mesh, null);
    }

    private static void MtcEmptyAndClone(int cache)
    {
        var original = MtcSeed(); original.ProxyGraphics = MtcCache(cache); string before = MtcState(original);
        var clone = (Mesh)original.Clone(); clone.InsertFace(1, 4, 5, 0); clone.RemoveEdgeAt(1); clone.MoveFace(0, 3);
        Check(before == MtcState(original), "Clone topology edit mutated source"); MrProxy(original, MtcCache(cache)); MrProxy(clone, null);
        var mesh = new Mesh(MtcPoints(), Array.Empty<int[]>()); mesh.ProxyGraphics = MtcCache(cache);
        MtcRefuse(mesh, () => mesh.RemoveFaceAt(0), cache); MtcRefuse(mesh, () => mesh.RemoveEdgeAt(0), cache);
        mesh.InsertFace(0, 0, 1, 2); mesh.InsertEdge(0, 0, 1); MrProxy(mesh, null);
        mesh.ProxyGraphics = MtcCache(cache); mesh.MoveFace(0, 0); mesh.MoveEdge(0, 0); MrProxy(mesh, MtcCache(cache));
        mesh.RemoveFaceAt(0); Check(mesh.Edges.Count == 1 && mesh.Vertexes.Count == 6, "Face removal implicitly deleted topology");
        mesh.RemoveEdgeAt(0); Check(mesh.Faces.Count == 0 && mesh.Edges.Count == 0 && mesh.Vertexes.Count == 6, "Empty topology mismatch"); MrProxy(mesh, null);
        mesh.Vertexes.Clear(); MtcRefuse(mesh, () => mesh.InsertFace(0, 0, 1, 2), cache); MtcRefuse(mesh, () => mesh.InsertEdge(0, 0, 0), cache);
    }

    private static void MtcCreaseBits(int cache)
    {
        foreach (double value in new[] { 0.0, -0.0, double.Epsilon, double.MaxValue, -double.Epsilon, -2.0, -double.MaxValue })
        {
            var mesh = MtcSeed(); mesh.ProxyGraphics = MtcCache(cache); mesh.InsertEdge(1, 4, 5, value);
            var added = mesh.Edges[1]; mesh.MoveEdge(1, 3);
            Check(ReferenceEquals(added, mesh.Edges[3]) && BitConverter.DoubleToInt64Bits(added.Crease) == BitConverter.DoubleToInt64Bits(value < 0 ? -1 : value), "Crease normalization/bits/order");
            mesh.RemoveEdgeAt(3); Check(mesh.Edges.Select(MtcEdge).SequenceEqual(MtcSeed().Edges.Select(MtcEdge)), "Insert/move/remove corrupted old creases"); MrProxy(mesh, null);
        }
    }

    private static void MtcFaceBudget()
    {
        // Count words as well as corners: 1 + 3,999,995 + 1 + 3 == 4,000,000.
        var mesh = new Mesh(new[] { Vector3.Zero }, new[] { new int[3999995] }); mesh.ProxyGraphics = MtcCache(2);
        mesh.InsertFace(1, 0, 0, 0); MrProxy(mesh, null);
        Check(mesh.Faces.Sum(f => (long)f.Length + 1) == 4000000, "Exact face-list limit rejected");
        mesh.ProxyGraphics = MtcCache(2); int capacity = mesh.Faces.Capacity; var iterator = mesh.Faces.GetEnumerator();
        Throws<NotSupportedException>(() => mesh.InsertFace(0, 0, 0, 0));
        Check(mesh.Faces.Count == 2 && mesh.Faces.Capacity == capacity && iterator.MoveNext(), "Overflow changed face count/capacity/version"); MrProxy(mesh, MtcCache(2));
        mesh.RemoveFaceAt(1); Check(mesh.Faces.Count == 1, "Removal at admitted limit failed");
        var small = new Mesh(new[] { Vector3.Zero }, Array.Empty<int[]>());
        Throws<NotSupportedException>(() => small.InsertFace(0, new int[4000000])); Check(small.Faces.Count == 0 && small.Faces.Capacity == 0, "Oversized candidate modified capacity");
    }

    private static void MtcEdgeBudget()
    {
        var edge = new MeshEdge(0, 0);
        var mesh = new Mesh(new[] { Vector3.Zero }, Array.Empty<int[]>(), Enumerable.Repeat(edge, 3999999));
        mesh.InsertEdge(mesh.Edges.Count, 0, 0, .5); Check(mesh.Edges.Count == 4000000, "Exact edge limit rejected");
        mesh.ProxyGraphics = MtcCache(2); int capacity = mesh.Edges.Capacity; var iterator = mesh.Edges.GetEnumerator();
        Throws<NotSupportedException>(() => mesh.InsertEdge(0, 0, 0));
        Check(mesh.Edges.Count == 4000000 && mesh.Edges.Capacity == capacity && iterator.MoveNext(), "Overflow changed edge state"); MrProxy(mesh, MtcCache(2));
        mesh.RemoveEdgeAt(0); Check(mesh.Edges.Count == 3999999 && ReferenceEquals(mesh.Edges[0], edge), "Alias removal at edge limit changed remaining object");
    }

    private static void MtcVertexBudget()
    {
        var mesh = new Mesh(new Vector3[4000001], new[] { new[] { 0, 0, 0 } }, new[] { new MeshEdge(0, 0) });
        mesh.ProxyGraphics = MtcCache(2);
        Action[] edits = { () => mesh.InsertFace(0, 0, 0, 0), () => mesh.RemoveFaceAt(0), () => mesh.MoveFace(0, 0),
            () => mesh.InsertEdge(0, 0, 0), () => mesh.RemoveEdgeAt(0), () => mesh.MoveEdge(0, 0) };
        foreach (var edit in edits) { Throws<NotSupportedException>(edit); MrProxy(mesh, MtcCache(2)); }
        Check(mesh.Faces.Count == 1 && mesh.Edges.Count == 1, "Vertex budget refusal changed topology");
    }

    private static void MtcApply(Mesh mesh, string op)
    {
        switch (op)
        {
            case "face-first": mesh.InsertFace(0, 4, 5, 0); break;
            case "face-last": mesh.InsertFace(mesh.Faces.Count, 4, 5, 0); break;
            case "face-remove": mesh.RemoveFaceAt(1); break;
            case "face-forward": mesh.MoveFace(0, 2); break;
            case "face-backward": mesh.MoveFace(2, 0); break;
            case "face-same": mesh.MoveFace(1, 1); break;
            case "edge-first": mesh.InsertEdge(0, 4, 5, 2.5); break;
            case "edge-last": mesh.InsertEdge(mesh.Edges.Count, 4, 5, 2.5); break;
            case "edge-remove": mesh.RemoveEdgeAt(1); break;
            case "edge-forward": mesh.MoveEdge(0, 2); break;
            case "edge-backward": mesh.MoveEdge(2, 0); break;
            case "edge-same": mesh.MoveEdge(1, 1); break;
            case "sequence":
                mesh.InsertFace(1, 4, 5, 0); mesh.MoveFace(0, 3); mesh.RemoveFaceAt(2);
                mesh.InsertEdge(1, 4, 5, 2.5); mesh.MoveEdge(3, 0); mesh.RemoveEdgeAt(2); break;
        }
    }

    private static void MtcExpected(Mesh mesh, string op)
    {
        int[] faceOrder = op switch { "face-first" => new[] { 3, 0, 1, 2 }, "face-last" => new[] { 0, 1, 2, 3 },
            "face-remove" => new[] { 0, 2 }, "face-forward" => new[] { 1, 2, 0 }, "face-backward" => new[] { 2, 0, 1 },
            "sequence" => new[] { 3, 1, 0 }, _ => new[] { 0, 1, 2 } };
        int[] edgeOrder = op switch { "edge-first" => new[] { 3, 0, 1, 2 }, "edge-last" => new[] { 0, 1, 2, 3 },
            "edge-remove" => new[] { 0, 2 }, "edge-forward" => new[] { 1, 2, 0 }, "edge-backward" => new[] { 2, 0, 1 },
            "sequence" => new[] { 2, 0, 1 }, _ => new[] { 0, 1, 2 } };
        var seed = MtcSeed(); var allFaces = seed.Faces.Concat(new[] { new[] { 4, 5, 0 } }).ToArray();
        var allEdges = seed.Edges.Concat(new[] { new MeshEdge(4, 5, 2.5) }).ToArray();
        Check(mesh.Vertexes.Select(MrPoint).SequenceEqual(MtcPoints().Select(MrPoint)), "Wire coordinate bits changed");
        Check(mesh.Faces.Count == faceOrder.Length && mesh.Faces.Select(f => string.Join(",", f)).SequenceEqual(faceOrder.Select(i => string.Join(",", allFaces[i]))), "Wire face order/arity/winding");
        Check(mesh.Edges.Select(MtcEdge).SequenceEqual(edgeOrder.Select(i => MtcEdge(allEdges[i]))), "Wire edge/crease association");
        Check(mesh.SubdivisionLevel == 2 && mesh.BlendCrease, "Wire subdivision header changed");
    }

    private static void MtcWire(DxfVersion version, bool binary)
    {
        var seed = new DxfDocument(version); var specs = new Dictionary<string, (string Name, int Cache, string Op)>();
        for (int place = 0; place < 4; place++)
        {
            var meshes = new List<Mesh>();
            for (int cache = 0; cache < 3; cache++) foreach (string op in MtcOperations)
            {
                var mesh = MtcSeed(); var data = new XData(new ApplicationRegistry("MESH_COLLECTIONS"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"MC_{place}_{cache}_{op}")); mesh.XData.Add(data); mesh.ProxyGraphics = MtcCache(cache); meshes.Add(mesh);
            }
            if (place == 0) foreach (var mesh in meshes) seed.Entities.Add(mesh);
            else if (place == 1) { var paper = new Layout("MC_PAPER"); seed.Layouts.Add(paper); foreach (var mesh in meshes) paper.AssociatedBlock.Entities.Add(mesh); }
            else { var block = new Block("MC_CONTAINER_" + place); foreach (var mesh in meshes) block.Entities.Add(mesh); if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block); }
            foreach (var mesh in meshes)
            {
                string name = (string)mesh.XData["MESH_COLLECTIONS"].XDataRecord.Single().Value; var fields = name.Split('_');
                specs.Add(mesh.Handle, (name, int.Parse(fields[2], System.Globalization.CultureInfo.InvariantCulture), fields[3]));
            }
        }
        var line = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(line);
        string prefix = "mesh-collections-" + version + "-" + (binary ? "binary" : "text");
        byte[] bytes = MeshHeaderSave(seed, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), bytes);
        var doc = MeshHeaderLoad(bytes);
        foreach (var pair in specs)
        {
            var mesh = MeshHeaderFind(doc, pair.Key); MtcExpected(mesh, "keep"); MrProxy(mesh, MtcCache(pair.Value.Cache));
            MtcApply(mesh, pair.Value.Op); MtcExpected(mesh, pair.Value.Op); MrProxy(mesh, MtcChanges(pair.Value.Op) ? null : MtcCache(pair.Value.Cache));
        }
        for (int stage = 0; stage < 2; stage++)
        {
            bytes = MeshHeaderSave(doc, stage == 0 ? binary : !binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes); doc = MeshHeaderLoad(bytes);
            foreach (var pair in specs)
            {
                var mesh = MeshHeaderFind(doc, pair.Key); MtcExpected(mesh, pair.Value.Op); MrProxy(mesh, MtcChanges(pair.Value.Op) ? null : MtcCache(pair.Value.Cache));
                Check((string)mesh.XData["MESH_COLLECTIONS"].XDataRecord.Single().Value == pair.Value.Name, "Wire parent XData changed");
            }
            var following = (Line)doc.GetObjectByHandle(line.Handle);
            Check(following.StartPoint == line.StartPoint && following.EndPoint == line.EndPoint && doc.Objects.Validate().Count == 0, "Following LINE or database graph changed");
        }
    }
}
