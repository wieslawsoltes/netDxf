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
    private static readonly string[] MrOperations = { "keep", "first", "middle", "last", "remove", "forward", "backward", "same" };
    private static Vector3[] MrPoints() => new[] { new Vector3(-0.0, 2, 3), new Vector3(11, 12, 13), new Vector3(4, 2, 3), new Vector3(1, 6, 3), new Vector3(4, 6, 3) };
    private static Vector3 MrAdded => new(99, -7, 5);
    private static Mesh MrSeed() => new(MrPoints(), new[] { new[] { 0, 2, 3 }, new[] { 3, 2, 4, 0 } },
        new[] { new MeshEdge(0, 2, -0.0), new MeshEdge(4, 3, -1) }) { SubdivisionLevel = 2, BlendCrease = true };
    private static byte[]? MrCache(int state) => state == 0 ? null : state == 1 ? Array.Empty<byte>() : new byte[] { 77, 82, 0, 255 };
    private static string MrPoint(Vector3 point) => string.Join(",", BitConverter.DoubleToInt64Bits(point.X), BitConverter.DoubleToInt64Bits(point.Y), BitConverter.DoubleToInt64Bits(point.Z));
    private static void MrProxy(Mesh mesh, byte[]? expected) => Check(expected == null ? mesh.ProxyGraphics == null
        : mesh.ProxyGraphics != null && mesh.ProxyGraphics.SequenceEqual(expected), "MESH reindex graphics mismatch");
    private static string MrState(Mesh mesh) => string.Join(";", mesh.Vertexes.Select(MrPoint)) + "|"
        + string.Join(";", mesh.Faces.Select(f => f == null ? "null" : string.Join(",", f))) + "|"
        + string.Join(";", mesh.Edges.Select(e => e == null ? "null" : $"{e.StartVertexIndex},{e.EndVertexIndex},{BitConverter.DoubleToInt64Bits(e.Crease)}"));
    private static void MrReject(Mesh mesh, Action operation, int cache)
    {
        string before = MrState(mesh); mesh.ProxyGraphics = MrCache(cache);
        int capacity = mesh.Vertexes.Capacity;
        using var vertices = mesh.Vertexes.GetEnumerator();
        MteReject(operation);
        Check(before == MrState(mesh) && capacity == mesh.Vertexes.Capacity, "Rejected reindex partially changed storage");
        MrProxy(mesh, MrCache(cache)); vertices.MoveNext();
    }
    private static void RegisterMeshReindexingTests()
    {
        for (int cache = 0; cache < 3; cache++)
        {
            int c = cache;
            foreach (bool attached in new[] { false, true })
            {
                bool a = attached;
                for (int slot = 0; slot <= 5; slot++)
                { int s = slot; Run($"mesh-reindex/insert/{c}/{a}/{s}", () => MrEdit(c, a, "insert", s, 0)); }
                for (int from = 0; from < 5; from++) for (int to = 0; to < 5; to++)
                { int f = from, t = to; Run($"mesh-reindex/move/{c}/{a}/{f}/{t}", () => MrEdit(c, a, "move", f, t)); }
                Run($"mesh-reindex/remove/{c}/{a}", () => MrEdit(c, a, "remove", 1, 0));
            }
            Run("mesh-reindex/refused/" + c, () => MrRefused(c));
            Run("mesh-reindex/aliases/" + c, () => MrAliases(c));
            Run("mesh-reindex/clone-and-empty/" + c, () => MrCloneAndEmpty(c));
            Run("mesh-reindex/scalar-bits/" + c, () => MrScalarBits(c));
            Run("mesh-reindex/all-removal-slots/" + c, () => MrRemovalSlots(c));
        }
        Run("mesh-reindex/insertion-budget", MrBudget);
        foreach (var version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
            Run($"mesh-reindex/wire/{version}/{binary}", () => MrWire(version, binary));
    }
    private static void MrEdit(int cache, bool attached, string operation, int index, int target)
    {
        var mesh = MrSeed(); var doc = new DxfDocument(); if (attached) doc.Entities.Add(mesh);
        var vertices = mesh.Vertexes; var faces = mesh.Faces; var arrays = mesh.Faces.ToArray();
        var edges = mesh.Edges; var objects = mesh.Edges.ToArray(); var owner = mesh.Owner; string? handle = mesh.Handle;
        var layer = mesh.Layer; var normal = mesh.Normal; var xdata = mesh.XData;
        var order = Enumerable.Range(0, 5).ToList();
        using var fv = faces.GetEnumerator(); using var ev = edges.GetEnumerator(); using var vv = vertices.GetEnumerator();
        Check(fv.MoveNext() && ev.MoveNext() && vv.MoveNext(), "Reindex fixture empty");
        mesh.ProxyGraphics = MrCache(cache);
        if (operation == "insert") { mesh.InsertVertex(index, MrAdded); order.Insert(index, -1); }
        else if (operation == "remove") { mesh.RemoveVertexAt(index); order.RemoveAt(index); }
        else { mesh.MoveVertex(index, target); int old = order[index]; order.RemoveAt(index); order.Insert(target, old); }
        MrVerifyOrder(mesh, order);
        bool changed = operation != "move" || index != target;
        MrProxy(mesh, changed ? null : MrCache(cache));
        if (!changed) Check(vv.MoveNext(), "Equal-index move invalidated coordinate enumeration");
        Check(fv.MoveNext() && ev.MoveNext(), "Reindex invalidated topology list enumerators");
        Check(ReferenceEquals(vertices, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces) && ReferenceEquals(edges, mesh.Edges)
            && arrays.Zip(mesh.Faces).All(p => ReferenceEquals(p.First, p.Second))
            && objects.Zip(mesh.Edges).All(p => ReferenceEquals(p.First, p.Second))
            && ReferenceEquals(owner, mesh.Owner) && handle == mesh.Handle && ReferenceEquals(layer, mesh.Layer)
            && ReferenceEquals(xdata, mesh.XData) && mesh.Normal == normal && mesh.SubdivisionLevel == 2 && mesh.BlendCrease,
            "Reindex changed unselected objects or header state");
        if (attached) Check(ReferenceEquals(mesh, doc.GetObjectByHandle(handle)), "Parent handle lost");
        Check(doc.Objects.Validate().Count == 0, "Reindex graph invalid");
    }
    private static void MrVerifyOrder(Mesh mesh, List<int> order)
    {
        var source = MrPoints();
        Check(mesh.Vertexes.Select(MrPoint).SequenceEqual(order.Select(i => MrPoint(i < 0 ? MrAdded : source[i]))), "Reindex coordinate bits/order");
        int[][] original = { new[] { 0, 2, 3 }, new[] { 3, 2, 4, 0 } };
        Check(mesh.Faces.Count == 2 && mesh.Edges.Count == 2, "Unexpected topology count");
        for (int i = 0; i < original.Length; i++)
            Check(mesh.Faces[i].SequenceEqual(original[i].Select(v => order.IndexOf(v))), "Face reindex mismatch");
        Check(mesh.Edges[0].StartVertexIndex == order.IndexOf(0) && mesh.Edges[0].EndVertexIndex == order.IndexOf(2)
            && mesh.Edges[1].StartVertexIndex == order.IndexOf(4) && mesh.Edges[1].EndVertexIndex == order.IndexOf(3), "Edge reindex mismatch");
        Check(BitConverter.DoubleToInt64Bits(mesh.Edges[0].Crease) == long.MinValue && mesh.Edges[1].Crease == -1, "Crease bits changed");
        for (int i = 0; i < original.Length; i++)
            Check(mesh.Faces[i].Select(v => MrPoint(mesh.Vertexes[v])).SequenceEqual(original[i].Select(v => MrPoint(source[v]))), "Reindex changed face geometry");
    }
    private static void MrRefused(int cache)
    {
        var mesh = MrSeed();
        foreach (int index in new[] { -1, 6 }) MrReject(mesh, () => mesh.InsertVertex(index, MrAdded), cache);
        foreach (int index in new[] { -1, 5 })
        {
            MrReject(mesh, () => mesh.RemoveVertexAt(index), cache);
            MrReject(mesh, () => mesh.MoveVertex(index, 0), cache);
            MrReject(mesh, () => mesh.MoveVertex(0, index), cache);
        }
        foreach (int index in new[] { 0, 2, 3, 4 }) MrReject(mesh, () => mesh.RemoveVertexAt(index), cache);
        foreach (double value in new[] { double.NaN, double.NegativeInfinity, double.PositiveInfinity })
        for (int axis = 0; axis < 3; axis++)
        {
            var point = new Vector3(axis == 0 ? value : 0, axis == 1 ? value : 0, axis == 2 ? value : 0);
            MrReject(mesh, () => mesh.InsertVertex(1, point), cache);
        }
        // A reference discovered in the final edge must not partially publish already staged faces.
        mesh.Edges.Add(new MeshEdge(4, 1)); MrReject(mesh, () => mesh.RemoveVertexAt(1), cache);
        for (int fault = 0; fault < 8; fault++)
        {
            mesh = MrSeed();
            switch (fault)
            {
                case 0: mesh.Vertexes[4] = new Vector3(1, 2, double.NaN); break;
                case 1: mesh.Faces[1] = null!; break;
                case 2: mesh.Faces[1] = new[] { 0, 1 }; break;
                case 3: mesh.Faces[1][3] = -1; break;
                case 4: mesh.Faces[1][3] = 5; break;
                case 5: mesh.Edges[1] = null!; break;
                case 6: mesh.Edges[1].EndVertexIndex = 5; break;
                case 7: mesh.Edges[1].Crease = double.NaN; break;
            }
            MrReject(mesh, () => mesh.InsertVertex(1, MrAdded), cache);
            MrReject(mesh, () => mesh.RemoveVertexAt(1), cache);
            MrReject(mesh, () => mesh.MoveVertex(1, 1), cache);
        }
    }
    private sealed class MrHostileEdge : MeshEdge
    {
        internal MrHostileEdge() : base(0, 2, -0.0) { }
        public override bool Equals(object? obj) => throw new InvalidOperationException("Caller equality invoked");
        public override int GetHashCode() => throw new InvalidOperationException("Caller hash invoked");
    }
    private static void MrAliases(int cache)
    {
        var mesh = MrSeed(); int[] face = mesh.Faces[0]; mesh.Faces.Add(face);
        var edge = new MrHostileEdge(); mesh.Edges[0] = edge; mesh.Edges.Add(edge); mesh.ProxyGraphics = MrCache(cache);
        mesh.InsertVertex(0, MrAdded);
        Check(face.SequenceEqual(new[] { 1, 3, 4 }) && edge.StartVertexIndex == 1 && edge.EndVertexIndex == 3, "Aliased storage remapped twice");
        Check(ReferenceEquals(mesh.Faces[0], mesh.Faces[2]) && ReferenceEquals(mesh.Edges[0], mesh.Edges[2]), "Alias identity lost");
        MrProxy(mesh, null); mesh.RemoveVertexAt(0);
        Check(face.SequenceEqual(new[] { 0, 2, 3 }) && edge.StartVertexIndex == 0 && edge.EndVertexIndex == 2, "Alias removal mismatch");
        mesh.MoveVertex(0, 4);
        Check(face.SequenceEqual(new[] { 4, 1, 2 }) && edge.StartVertexIndex == 4 && edge.EndVertexIndex == 1, "Alias move mismatch");
    }
    private static void MrCloneAndEmpty(int cache)
    {
        var original = MrSeed(); original.ProxyGraphics = MrCache(cache); string state = MrState(original);
        var clone = (Mesh)original.Clone(); clone.InsertVertex(1, MrAdded); clone.MoveVertex(0, 5); clone.RemoveVertexAt(0);
        Check(MrState(original) == state, "Clone edits changed source"); MrProxy(original, MrCache(cache)); MrProxy(clone, null);
        var empty = new Mesh(Array.Empty<Vector3>(), Array.Empty<int[]>()); empty.ProxyGraphics = MrCache(cache);
        empty.InsertVertex(0, MrAdded); Check(empty.Vertexes.Count == 1, "Empty insertion failed"); MrProxy(empty, null);
        empty.ProxyGraphics = MrCache(cache); empty.MoveVertex(0, 0); MrProxy(empty, MrCache(cache));
        empty.RemoveVertexAt(0); Check(empty.Vertexes.Count == 0 && empty.Faces.Count == 0 && empty.Edges.Count == 0, "Singleton removal failed"); MrProxy(empty, null);
        MrReject(empty, () => empty.RemoveVertexAt(0), cache);
    }
    private static void MrScalarBits(int cache)
    {
        foreach (double value in new[] { -0.0, double.Epsilon, -double.Epsilon, double.MaxValue, -double.MaxValue })
        for (int axis = 0; axis < 3; axis++)
        {
            var mesh = MrSeed(); var p = new Vector3(axis == 0 ? value : 0, axis == 1 ? value : 0, axis == 2 ? value : 0);
            mesh.ProxyGraphics = MrCache(cache); mesh.InsertVertex(1, p); mesh.MoveVertex(1, 5);
            Check(MrPoint(mesh.Vertexes[5]) == MrPoint(p), "Reindex changed inserted binary64 bits"); MrProxy(mesh, null);
            mesh.RemoveVertexAt(5); Check(mesh.Vertexes.Select(MrPoint).SequenceEqual(MrPoints().Select(MrPoint)), "Insert/move/remove lost original bits");
        }
    }
    private static void MrRemovalSlots(int cache)
    {
        for (int index = 0; index < 5; index++)
        {
            var mesh = new Mesh(MrPoints(), Array.Empty<int[]>()); mesh.ProxyGraphics = MrCache(cache);
            var expected = MrPoints().ToList(); expected.RemoveAt(index); mesh.RemoveVertexAt(index);
            Check(mesh.Vertexes.Select(MrPoint).SequenceEqual(expected.Select(MrPoint)), "Unreferenced removal order"); MrProxy(mesh, null);
        }
    }
    private static void MrBudget()
    {
        var mesh = new Mesh(new Vector3[4000000], Array.Empty<int[]>());
        mesh.ProxyGraphics = MrCache(2); int capacity = mesh.Vertexes.Capacity;
        Throws<NotSupportedException>(() => mesh.InsertVertex(0, Vector3.Zero));
        Check(mesh.Vertexes.Count == 4000000 && mesh.Vertexes.Capacity == capacity, "Insertion budget changed count/capacity"); MrProxy(mesh, MrCache(2));
    }
    private static List<int> MrApply(Mesh mesh, string operation)
    {
        var order = Enumerable.Range(0, 5).ToList();
        switch (operation)
        {
            case "first": mesh.InsertVertex(0, MrAdded); order.Insert(0, -1); break;
            case "middle": mesh.InsertVertex(2, MrAdded); order.Insert(2, -1); break;
            case "last": mesh.InsertVertex(5, MrAdded); order.Add(-1); break;
            case "remove": mesh.RemoveVertexAt(1); order.RemoveAt(1); break;
            case "forward": mesh.MoveVertex(0, 4); order.RemoveAt(0); order.Add(0); break;
            case "backward": mesh.MoveVertex(4, 0); order.RemoveAt(4); order.Insert(0, 4); break;
            case "same": mesh.MoveVertex(2, 2); break;
        }
        return order;
    }
    private static void MrWire(DxfVersion version, bool binary)
    {
        var seed = new DxfDocument(version); var subjects = new Dictionary<string, (int Cache, string Operation)>();
        for (int place = 0; place < 4; place++)
        {
            var items = new List<Mesh>();
            for (int cache = 0; cache < 3; cache++) foreach (string operation in MrOperations)
            {
                var mesh = MrSeed(); var data = new XData(new ApplicationRegistry("MESH_REINDEX"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"MR_{place}_{cache}_{operation}")); mesh.XData.Add(data); mesh.ProxyGraphics = MrCache(cache); items.Add(mesh);
            }
            if (place == 0) foreach (var mesh in items) seed.Entities.Add(mesh);
            else if (place == 1) { var paper = new Layout("MR_PAPER"); seed.Layouts.Add(paper); foreach (var mesh in items) paper.AssociatedBlock.Entities.Add(mesh); }
            else { var block = new Block("MR_CONTAINER_" + place); foreach (var mesh in items) block.Entities.Add(mesh); if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block); }
            foreach (var mesh in items)
            {
                var label = ((string)mesh.XData["MESH_REINDEX"].XDataRecord.Single().Value).Split('_');
                subjects.Add(mesh.Handle, (int.Parse(label[2], System.Globalization.CultureInfo.InvariantCulture), label[3]));
            }
        }
        var line = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(line);
        string prefix = "mesh-reindex-" + version + "-" + (binary ? "binary" : "text");
        byte[] bytes = MeshHeaderSave(seed, binary); File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), bytes);
        var doc = MeshHeaderLoad(bytes); var expected = new Dictionary<string, List<int>>();
        foreach (var pair in subjects)
        {
            var mesh = MeshHeaderFind(doc, pair.Key); MrVerifyOrder(mesh, Enumerable.Range(0, 5).ToList()); MrProxy(mesh, MrCache(pair.Value.Cache));
            expected.Add(pair.Key, MrApply(mesh, pair.Value.Operation)); MrVerifyOrder(mesh, expected[pair.Key]);
        }
        for (int stage = 0; stage < 2; stage++)
        {
            bytes = MeshHeaderSave(doc, stage == 0 ? binary : !binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes); doc = MeshHeaderLoad(bytes);
            foreach (var pair in subjects)
            {
                var mesh = MeshHeaderFind(doc, pair.Key); MrVerifyOrder(mesh, expected[pair.Key]);
                MrProxy(mesh, pair.Value.Operation == "keep" || pair.Value.Operation == "same" ? MrCache(pair.Value.Cache) : null);
                Check(mesh.SubdivisionLevel == 2 && mesh.BlendCrease, "Roundtrip header changed");
            }
            var following = (Line)doc.GetObjectByHandle(line.Handle);
            Check(following.StartPoint == line.StartPoint && following.EndPoint == line.EndPoint && doc.Objects.Validate().Count == 0, "Following LINE or graph changed");
        }
    }
}
