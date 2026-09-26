// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections;
using System.Globalization;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] MveOperations = { "keep", "single", "replace", "batch", "same", "alias" };
    private static Vector3[] MveOriginal() => new[] { new Vector3(1, 2, 3), new Vector3(4, 2, 3), new Vector3(1, 6, 3) };
    private static byte[]? MveCache(int kind) => kind == 0 ? null : kind == 1 ? Array.Empty<byte>() : new byte[] { 77, 86, 69, 0, 255 };
    private static KeyValuePair<int, Vector3> MveEntry(int index, Vector3 value) => new(index, value);

    private static void RegisterMeshVertexEditTests()
    {
        for (int cache = 0; cache < 3; cache++)
        {
            int captured = cache;
            foreach (bool attached in new[] { false, true })
            {
                bool placed = attached;
                Run($"mesh-vertex-edit/setters/{cache}/{attached}", () => MveSetters(captured, placed));
                Run($"mesh-vertex-edit/reject/{cache}/{attached}", () => MveReject(captured, placed));
            }
            Run($"mesh-vertex-edit/bits/{cache}", () => MveBits(captured));
            Run($"mesh-vertex-edit/enumeration/{cache}", () => MveEnumeration(captured));
            Run($"mesh-vertex-edit/clone/{cache}", () => MveClone(captured));
        }
        Run("mesh-vertex-edit/empty", MveEmpty);
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
            Run($"mesh-vertex-edit/wire/{version}/{binary}", () => MveWire(version, binary));
    }

    private static Mesh MveSeed(bool attached = false)
    {
        var mesh = new Mesh(MveOriginal(), new[] { new[] { 0, 1, 2 } }, new[] { new MeshEdge(0, 1, 0.5) })
        { SubdivisionLevel = 2, BlendCrease = true };
        if (attached) new DxfDocument(DxfVersion.AutoCad2018).Entities.Add(mesh);
        return mesh;
    }

    private static bool MveSame(Vector3 a, Vector3 b) =>
        BitConverter.DoubleToInt64Bits(a.X) == BitConverter.DoubleToInt64Bits(b.X)
        && BitConverter.DoubleToInt64Bits(a.Y) == BitConverter.DoubleToInt64Bits(b.Y)
        && BitConverter.DoubleToInt64Bits(a.Z) == BitConverter.DoubleToInt64Bits(b.Z);

    private static void MvePoints(IReadOnlyList<Vector3> expected, IReadOnlyList<Vector3> actual)
    {
        Equal(expected.Count, actual.Count, "MESH vertex count");
        for (int i = 0; i < expected.Count; i++) Check(MveSame(expected[i], actual[i]), "MESH coordinate bits at " + i);
    }

    private static void MveProxy(byte[]? expected, byte[]? actual)
    {
        Check(expected == null ? actual == null : actual != null && expected.SequenceEqual(actual), "MESH proxy state");
    }

    private static Vector3[] MveExpected(string operation)
    {
        var points = MveOriginal();
        if (operation == "single") points[1] = new Vector3(14, -2, 9);
        if (operation == "replace") for (int i = 0; i < points.Length; i++) points[i] += new Vector3(10, -4, 8);
        if (operation == "batch") { points[0] = new Vector3(-4, -5, -6); points[2] = new Vector3(30, 31, 32); }
        return points;
    }

    private static void MveApply(Mesh mesh, string operation)
    {
        var points = MveExpected(operation);
        switch (operation)
        {
            case "single": mesh.SetVertex(1, points[1]); break;
            case "replace": mesh.SetVertexes(points); break;
            case "batch": mesh.SetVertexPositions(new[] { MveEntry(2, points[2]), MveEntry(0, points[0]) }); break;
            case "same":
                mesh.SetVertex(1, mesh.Vertexes[1]); mesh.SetVertexes(mesh.Vertexes.ToArray());
                mesh.SetVertexPositions(new[] { MveEntry(0, mesh.Vertexes[0]), MveEntry(2, mesh.Vertexes[2]) }); break;
            case "alias": mesh.SetVertexes(mesh.Vertexes); break;
            case "keep": break;
            default: throw new ArgumentException("Unknown MESH edit");
        }
    }

    private static void MveSetters(int cache, bool attached)
    {
        foreach (string operation in MveOperations)
        {
            var mesh = MveSeed(attached); mesh.Normal = Vector3.UnitY; mesh.ProxyGraphics = MveCache(cache);
            var vertices = mesh.Vertexes; var faces = mesh.Faces; var face = faces[0];
            var edges = mesh.Edges; var edge = edges[0]; var owner = mesh.Owner; var layer = mesh.Layer;
            string handle = mesh.Handle; var normal = mesh.Normal;
            var iterator = vertices.GetEnumerator(); Check(iterator.MoveNext(), "Missing first vertex");
            MveApply(mesh, operation);
            bool changed = operation is "single" or "replace" or "batch";
            MvePoints(MveExpected(operation), mesh.Vertexes);
            MveProxy(changed ? null : MveCache(cache), mesh.ProxyGraphics);
            if (!changed) Check(iterator.MoveNext(), "No-op invalidated a List enumerator");
            iterator.Dispose();
            Check(ReferenceEquals(vertices, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces)
                && ReferenceEquals(face, mesh.Faces[0]) && ReferenceEquals(edges, mesh.Edges)
                && ReferenceEquals(edge, mesh.Edges[0]) && ReferenceEquals(owner, mesh.Owner)
                && ReferenceEquals(layer, mesh.Layer) && handle == mesh.Handle, "MESH identity replacement");
            Check(face.SequenceEqual(new[] { 0, 1, 2 }) && edge.StartVertexIndex == 0
                && edge.EndVertexIndex == 1 && edge.Crease == 0.5, "MESH topology/crease edit");
            Check(MveSame(normal, mesh.Normal) && mesh.SubdivisionLevel == 2 && mesh.BlendCrease, "MESH unrelated header edit");
            mesh.ProxyGraphics = MveCache(cache);
            mesh.SetVertexes(mesh.Vertexes); mesh.SetVertexPositions(Array.Empty<KeyValuePair<int, Vector3>>());
            MveProxy(MveCache(cache), mesh.ProxyGraphics);
        }
    }

    private static void MveRefuses<T>(Mesh mesh, int cache, Action edit) where T : Exception
    {
        var before = mesh.Vertexes.ToArray(); mesh.ProxyGraphics = MveCache(cache);
        Throws<T>(edit); MvePoints(before, mesh.Vertexes); MveProxy(MveCache(cache), mesh.ProxyGraphics);
    }

    private static void MveReject(int cache, bool attached)
    {
        var mesh = MveSeed(attached);
        MveRefuses<ArgumentOutOfRangeException>(mesh, cache, () => mesh.SetVertex(-1, Vector3.Zero));
        MveRefuses<ArgumentOutOfRangeException>(mesh, cache, () => mesh.SetVertex(3, Vector3.Zero));
        MveRefuses<ArgumentNullException>(mesh, cache, () => mesh.SetVertexes(null!));
        MveRefuses<ArgumentNullException>(mesh, cache, () => mesh.SetVertexPositions(null!));
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexes(new[] { Vector3.Zero }));
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexes(new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero }));
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        for (int slot = 0; slot < 3; slot++)
        {
            int index = slot;
            foreach (Vector3 point in new[] { new Vector3(bad, 1, 2), new Vector3(1, bad, 2), new Vector3(1, 2, bad) })
            {
                var values = MveExpected("replace"); values[index] = point;
                MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertex(index, point));
                MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexes(values));
                MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexPositions(new[] { MveEntry(index, point) }));
            }
        }
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexPositions(new[] { MveEntry(1, Vector3.Zero), MveEntry(1, Vector3.Zero) }));
        MveRefuses<ArgumentOutOfRangeException>(mesh, cache, () => mesh.SetVertexPositions(new[] { MveEntry(1, Vector3.Zero), MveEntry(3, Vector3.Zero) }));
        MveRefuses<ArgumentOutOfRangeException>(mesh, cache, () => mesh.SetVertexPositions(new[] { MveEntry(-1, Vector3.Zero) }));
        mesh.Vertexes[2] = new Vector3(double.NaN, 6, 3);
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertex(0, mesh.Vertexes[0]));
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexes(MveOriginal()));
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexPositions(Array.Empty<KeyValuePair<int, Vector3>>()));
    }

    private static void MveOneEdit(Mesh mesh, int method, Vector3 point)
    {
        if (method == 0) mesh.SetVertex(0, point);
        else if (method == 1) { var points = mesh.Vertexes.ToArray(); points[0] = point; mesh.SetVertexes(points); }
        else mesh.SetVertexPositions(new[] { MveEntry(0, point) });
    }

    private static void MveBits(int cache)
    {
        double minusZero = BitConverter.Int64BitsToDouble(long.MinValue);
        double nextOne = BitConverter.Int64BitsToDouble(BitConverter.DoubleToInt64Bits(1.0) + 1);
        for (int method = 0; method < 3; method++)
        foreach (var point in new[] { new Vector3(minusZero, 0, 0), new Vector3(0, minusZero, 0), new Vector3(0, 0, minusZero), new Vector3(nextOne, 2, 3) })
        {
            var mesh = MveSeed();
            mesh.Vertexes[0] = point.X == nextOne ? new Vector3(1, 2, 3) : Vector3.Zero;
            mesh.ProxyGraphics = MveCache(cache); MveOneEdit(mesh, method, point);
            Check(MveSame(point, mesh.Vertexes[0]), "Scalar bits lost"); MveProxy(null, mesh.ProxyGraphics);
            mesh.ProxyGraphics = MveCache(cache); MveOneEdit(mesh, method, point);
            MveProxy(MveCache(cache), mesh.ProxyGraphics);
        }
    }

    private sealed class MveProbe<T> : IEnumerable<T>, IEnumerator<T>
    {
        private readonly T[] values;
        private int index = -1;
        public int Enumerations, Moves, Disposals;
        public int ThrowMove = -1;
        public Action? OnDispose;
        public MveProbe(T[] values) { this.values = values; }
        public IEnumerator<T> GetEnumerator() { if (++Enumerations != 1) throw new InvalidOperationException("Repeated enumeration"); return this; }
        IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
        public T Current => this.values[this.index];
        object IEnumerator.Current => this.Current!;
        public bool MoveNext() { if (++Moves == ThrowMove) throw new InvalidOperationException("Late enumeration failure"); return ++this.index < this.values.Length; }
        public void Reset() => throw new NotSupportedException();
        public void Dispose() { Disposals++; OnDispose?.Invoke(); }
    }

    private static void MveEnumeration(int cache)
    {
        var mesh = MveSeed(); mesh.ProxyGraphics = MveCache(cache);
        var noOp = new MveProbe<Vector3>(MveOriginal()); mesh.SetVertexes(noOp);
        Check(noOp.Enumerations == 1 && noOp.Moves == 4 && noOp.Disposals == 1, "Replacement consumption/disposal");
        MveProxy(MveCache(cache), mesh.ProxyGraphics);
        foreach (int failure in new[] { 2, 4 })
        {
            var throwing = new MveProbe<Vector3>(MveExpected("replace")) { ThrowMove = failure };
            MveRefuses<InvalidOperationException>(mesh, cache, () => mesh.SetVertexes(throwing));
            Equal(1, throwing.Disposals, "Failed enumeration disposal");
        }
        var tooLong = new MveProbe<Vector3>(MveOriginal().Concat(new[] { Vector3.Zero, Vector3.Zero }).ToArray());
        MveRefuses<ArgumentException>(mesh, cache, () => mesh.SetVertexes(tooLong));
        Check(tooLong.Moves == 4 && tooLong.Disposals == 1, "Overlong replacement was not bounded");
        var disposing = new MveProbe<Vector3>(MveExpected("replace")) { OnDispose = () => throw new InvalidOperationException("Dispose failed") };
        MveRefuses<InvalidOperationException>(mesh, cache, () => mesh.SetVertexes(disposing));
        var batch = new MveProbe<KeyValuePair<int, Vector3>>(new[] { MveEntry(0, Vector3.Zero), MveEntry(2, Vector3.Zero) }) { ThrowMove = 3 };
        MveRefuses<InvalidOperationException>(mesh, cache, () => mesh.SetVertexPositions(batch));
        Equal(1, batch.Disposals, "Batch disposal");
        var batchDispose = new MveProbe<KeyValuePair<int, Vector3>>(new[] { MveEntry(0, Vector3.Zero) }) { OnDispose = () => throw new InvalidOperationException("Batch dispose failed") };
        MveRefuses<InvalidOperationException>(mesh, cache, () => mesh.SetVertexPositions(batchDispose));

        // The API must not overwrite caller-side edits made during enumeration. Such
        // callback mutations are intentionally retained, not rolled back or concealed.
        var external = new Vector3(70, 80, 90); mesh.ProxyGraphics = MveCache(cache);
        var drifting = new MveProbe<Vector3>(MveExpected("replace")) { OnDispose = () => mesh.Vertexes.Add(external) };
        Throws<InvalidOperationException>(() => mesh.SetVertexes(drifting));
        MvePoints(MveOriginal().Concat(new[] { external }).ToArray(), mesh.Vertexes);
        MveProxy(MveCache(cache), mesh.ProxyGraphics); mesh.Vertexes.RemoveAt(3);
        var driftingBatch = new MveProbe<KeyValuePair<int, Vector3>>(new[] { MveEntry(0, Vector3.Zero) }) { OnDispose = () => mesh.Vertexes[2] = external };
        Throws<InvalidOperationException>(() => mesh.SetVertexPositions(driftingBatch));
        var expected = MveOriginal(); expected[2] = external; MvePoints(expected, mesh.Vertexes);
        MveProxy(MveCache(cache), mesh.ProxyGraphics);
    }

    private static void MveClone(int cache)
    {
        var mesh = MveSeed(); mesh.ProxyGraphics = MveCache(cache);
        var clone = (Mesh)mesh.Clone(); MveProxy(MveCache(cache), clone.ProxyGraphics);
        Check(!ReferenceEquals(mesh.Vertexes, clone.Vertexes) && !ReferenceEquals(mesh.Faces[0], clone.Faces[0])
            && !ReferenceEquals(mesh.Edges[0], clone.Edges[0]), "Clone aliases mesh topology");
        clone.SetVertexPositions(new[] { MveEntry(0, Vector3.Zero), MveEntry(2, Vector3.Zero) });
        MveProxy(null, clone.ProxyGraphics); MveProxy(MveCache(cache), mesh.ProxyGraphics); MvePoints(MveOriginal(), mesh.Vertexes);
    }

    private static void MveEmpty()
    {
        var mesh = new Mesh(Array.Empty<Vector3>(), Array.Empty<int[]>());
        mesh.ProxyGraphics = MveCache(2); mesh.SetVertexes(mesh.Vertexes);
        mesh.SetVertexPositions(Array.Empty<KeyValuePair<int, Vector3>>()); MveProxy(MveCache(2), mesh.ProxyGraphics);
        MveRefuses<ArgumentOutOfRangeException>(mesh, 2, () => mesh.SetVertex(0, Vector3.Zero));
        MveRefuses<ArgumentException>(mesh, 2, () => mesh.SetVertexes(new[] { Vector3.Zero }));
        MveRefuses<ArgumentOutOfRangeException>(mesh, 2, () => mesh.SetVertexPositions(new[] { MveEntry(0, Vector3.Zero) }));
    }

    private static byte[] MveSave(DxfDocument document, bool binary)
    {
        using var stream = new MemoryStream(); Check(document.Save(stream, binary) && stream.CanWrite, "MESH save/stream ownership"); return stream.ToArray();
    }

    private static DxfDocument MveLoad(byte[] bytes)
    {
        var before = (byte[])bytes.Clone(); using var stream = new MemoryStream(bytes);
        var result = DxfDocument.Load(stream) ?? throw new InvalidOperationException("MESH load failed");
        Check(stream.CanRead && before.SequenceEqual(bytes), "Load changed caller bytes or stream lifetime"); return result;
    }

    private static void MveWire(DxfVersion version, bool binary)
    {
        var document = new DxfDocument(version);
        var subjects = new Dictionary<string, (string Name, int Cache, string Operation)>();
        for (int place = 0; place < 4; place++)
        {
            var meshes = new List<Mesh>();
            for (int cache = 0; cache < 3; cache++)
            foreach (string operation in MveOperations)
            {
                var mesh = MveSeed(); var data = new XData(new ApplicationRegistry("MESH_VERTEX_EDIT"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"MV_{place}_{cache}_{operation}"));
                mesh.XData.Add(data); mesh.ProxyGraphics = MveCache(cache); meshes.Add(mesh);
            }
            if (place == 0) foreach (var mesh in meshes) document.Entities.Add(mesh);
            else if (place == 1)
            {
                var layout = new Layout("MV_PAPER"); document.Layouts.Add(layout);
                foreach (var mesh in meshes) layout.AssociatedBlock.Entities.Add(mesh);
            }
            else
            {
                var block = new Block("MV_CONTAINER_" + place); foreach (var mesh in meshes) block.Entities.Add(mesh);
                if (place == 2) document.Entities.Add(new Insert(block)); else document.Blocks.Add(block);
            }
            foreach (var mesh in meshes)
            {
                string name = (string)mesh.XData["MESH_VERTEX_EDIT"].XDataRecord.Single().Value; string[] pieces = name.Split('_');
                subjects.Add(mesh.Handle, (name, int.Parse(pieces[2], CultureInfo.InvariantCulture), pieces[3]));
            }
        }
        var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); document.Entities.Add(following);
        string lineHandle = following.Handle;
        string prefix = "mesh-vertex-edit-" + version + "-" + (binary ? "binary" : "text");
        for (int stage = 0; stage < 3; stage++)
        {
            bool format = stage == 2 ? !binary : binary;
            byte[] bytes = MveSave(document, format);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-" + new[] { "source", "output", "resave" }[stage] + ".dxf"), bytes);
            document = MveLoad(bytes);
            Equal(version, document.DrawingVariables.AcadVer, "MESH profile changed");
            foreach (var pair in subjects)
            {
                var mesh = document.GetObjectByHandle(pair.Key) as Mesh ?? throw new InvalidOperationException("MESH identity missing");
                var spec = pair.Value; bool changed = stage != 0 && (spec.Operation is "single" or "replace" or "batch");
                MvePoints(MveExpected(stage == 0 ? "keep" : spec.Operation), mesh.Vertexes);
                MveProxy(changed ? null : MveCache(spec.Cache), mesh.ProxyGraphics);
                Equal(spec.Name, (string)mesh.XData["MESH_VERTEX_EDIT"].XDataRecord.Single().Value, "MESH XData changed");
                Check(mesh.SubdivisionLevel == 2 && mesh.BlendCrease && mesh.Faces.Count == 1
                    && mesh.Faces[0].SequenceEqual(new[] { 0, 1, 2 }) && mesh.Edges.Count == 1
                    && mesh.Edges[0].StartVertexIndex == 0 && mesh.Edges[0].EndVertexIndex == 1
                    && mesh.Edges[0].Crease == 0.5, "MESH header/topology changed");
                if (stage == 0) MveApply(mesh, spec.Operation);
            }
            var line = document.GetObjectByHandle(lineHandle) as Line ?? throw new InvalidOperationException("Following LINE missing");
            Check(MveSame(line.StartPoint, following.StartPoint) && MveSame(line.EndPoint, following.EndPoint), "Following LINE changed");
            Check(document.Objects.Validate().Count == 0, "Invalid MESH object graph");
        }
    }
}
