// Copyright (c) netDxf contributors. Licensed under the MIT License.
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
    private static readonly string[] MeshHeaderOperations = { "keep", "level", "blend", "same-level", "same-blend" };

    private static void RegisterMeshHeaderMutationTests()
    {
        for (int cache = 0; cache < 3; cache++)
        {
            int capturedCache = cache;
            foreach (bool blend in new[] { false, true })
            {
                bool capturedBlend = blend;
                Run($"mesh-header/edit/{cache}/{blend}", () => MeshHeaderEdit(capturedCache, capturedBlend));
                Run($"mesh-header/clone/{cache}/{blend}", () => MeshHeaderClone(capturedCache, capturedBlend));
            }
            Run($"mesh-header/byte-domain/{cache}", () => MeshHeaderByteDomain(capturedCache));
        }
        foreach (DxfVersion version in new[] { DxfVersion.AutoCad2010, DxfVersion.AutoCad2013, DxfVersion.AutoCad2018 })
        foreach (bool binary in new[] { false, true })
            Run($"mesh-header/wire/{version}/{binary}", () => MeshHeaderWire(version, binary));
    }

    private static byte[]? MeshHeaderCache(int state) => state == 0 ? null : state == 1
        ? Array.Empty<byte>() : new byte[] { 77, 72, 67, 0, 255 };

    private static void MeshHeaderCheckCache(byte[]? expected, byte[]? actual)
    {
        Check(expected == null ? actual == null : actual != null && expected.SequenceEqual(actual),
            "MESH absent/empty/nonempty graphics changed unexpectedly");
    }

    private static Mesh MeshHeaderSubject()
    {
        return new Mesh(new[] { new Vector3(1, 2, 3), new Vector3(4, 2, 3), new Vector3(1, 6, 3) },
            new[] { new[] { 0, 1, 2 } }, new[] { new MeshEdge(0, 1, 0.5) })
        { SubdivisionLevel = 2, BlendCrease = false };
    }

    private static void MeshHeaderGeometry(Mesh mesh)
    {
        Check(mesh.Vertexes.SequenceEqual(new[] { new Vector3(1, 2, 3), new Vector3(4, 2, 3), new Vector3(1, 6, 3) }), "MESH coordinates changed");
        Check(mesh.Faces.Count == 1 && mesh.Faces[0].SequenceEqual(new[] { 0, 1, 2 }), "MESH face changed");
        Check(mesh.Edges.Count == 1 && mesh.Edges[0].StartVertexIndex == 0
            && mesh.Edges[0].EndVertexIndex == 1 && mesh.Edges[0].Crease == 0.5, "MESH crease topology changed");
    }

    private static void MeshHeaderEdit(int cache, bool blend)
    {
        var document = new DxfDocument(DxfVersion.AutoCad2018);
        var mesh = MeshHeaderSubject(); document.Entities.Add(mesh);
        var vertices = mesh.Vertexes; var faces = mesh.Faces; var face = faces[0];
        var edges = mesh.Edges; var edge = edges[0]; var owner = mesh.Owner;
        var layer = mesh.Layer; string handle = mesh.Handle;
        using var enumerator = vertices.GetEnumerator();
        Check(enumerator.MoveNext(), "Empty test mesh");
        mesh.ProxyGraphics = MeshHeaderCache(cache);
        if (blend) mesh.BlendCrease = false; else mesh.SubdivisionLevel = 2;
        MeshHeaderCheckCache(MeshHeaderCache(cache), mesh.ProxyGraphics);
        Check(enumerator.MoveNext(), "No-op invalidated the coordinate enumerator");
        if (blend) mesh.BlendCrease = true; else mesh.SubdivisionLevel = 5;
        MeshHeaderCheckCache(null, mesh.ProxyGraphics);
        Check(mesh.BlendCrease == blend && mesh.SubdivisionLevel == (blend ? 2 : 5), "Wrong header field changed");
        Check(ReferenceEquals(vertices, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces)
            && ReferenceEquals(face, mesh.Faces[0]) && ReferenceEquals(edges, mesh.Edges)
            && ReferenceEquals(edge, mesh.Edges[0]) && ReferenceEquals(owner, mesh.Owner)
            && ReferenceEquals(layer, mesh.Layer) && handle == mesh.Handle
            && ReferenceEquals(document.GetObjectByHandle(handle), mesh), "Header edit replaced graph/topology identities");
        MeshHeaderGeometry(mesh);
        mesh.ProxyGraphics = MeshHeaderCache(cache);
        if (blend) mesh.BlendCrease = true; else mesh.SubdivisionLevel = 5;
        MeshHeaderCheckCache(MeshHeaderCache(cache), mesh.ProxyGraphics);
        if (blend) mesh.BlendCrease = false; else mesh.SubdivisionLevel = 2;
        MeshHeaderCheckCache(null, mesh.ProxyGraphics);
        Check(document.Objects.Validate().Count == 0, "Header edit damaged the graph");
    }

    private static void MeshHeaderClone(int cache, bool blend)
    {
        var source = MeshHeaderSubject(); source.ProxyGraphics = MeshHeaderCache(cache);
        var clone = (Mesh)source.Clone();
        MeshHeaderCheckCache(MeshHeaderCache(cache), clone.ProxyGraphics);
        Check(clone.SubdivisionLevel == source.SubdivisionLevel && clone.BlendCrease == source.BlendCrease, "Clone lost header state");
        Check(!ReferenceEquals(clone.Vertexes, source.Vertexes) && !ReferenceEquals(clone.Faces[0], source.Faces[0])
            && !ReferenceEquals(clone.Edges[0], source.Edges[0]), "Clone aliases geometry");
        if (blend) clone.BlendCrease = true; else clone.SubdivisionLevel = 255;
        MeshHeaderCheckCache(null, clone.ProxyGraphics);
        MeshHeaderCheckCache(MeshHeaderCache(cache), source.ProxyGraphics);
        Check(source.SubdivisionLevel == 2 && !source.BlendCrease, "Clone edit changed source header");
        MeshHeaderGeometry(source); MeshHeaderGeometry(clone);
    }

    private static void MeshHeaderByteDomain(int cache)
    {
        var mesh = MeshHeaderSubject();
        for (int value = 0; value <= byte.MaxValue; value++)
        {
            byte before = mesh.SubdivisionLevel;
            mesh.ProxyGraphics = MeshHeaderCache(cache); mesh.SubdivisionLevel = (byte)value;
            Equal((byte)value, mesh.SubdivisionLevel, "Subdivision byte range");
            MeshHeaderCheckCache(before == value ? MeshHeaderCache(cache) : null, mesh.ProxyGraphics);
            mesh.ProxyGraphics = MeshHeaderCache(cache); mesh.SubdivisionLevel = (byte)value;
            MeshHeaderCheckCache(MeshHeaderCache(cache), mesh.ProxyGraphics);
            mesh.BlendCrease = !mesh.BlendCrease; MeshHeaderCheckCache(null, mesh.ProxyGraphics);
            mesh.ProxyGraphics = MeshHeaderCache(cache); mesh.BlendCrease = mesh.BlendCrease;
            MeshHeaderCheckCache(MeshHeaderCache(cache), mesh.ProxyGraphics);
        }
        MeshHeaderGeometry(mesh);
    }

    private static byte[] MeshHeaderSave(DxfDocument document, bool binary)
    {
        using var stream = new MemoryStream();
        Check(document.Save(stream, binary) && stream.CanWrite, "MESH save/stream lifetime");
        return stream.ToArray();
    }

    private static DxfDocument MeshHeaderLoad(byte[] bytes)
    {
        byte[] before = (byte[])bytes.Clone();
        using var stream = new MemoryStream(bytes);
        var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("MESH load failed");
        Check(stream.CanRead && bytes.SequenceEqual(before), "MESH load changed caller bytes/stream lifetime");
        return document;
    }

    private static Mesh MeshHeaderFind(DxfDocument document, string handle) =>
        document.GetObjectByHandle(handle) as Mesh ?? throw new InvalidOperationException("Missing MESH identity: " + handle);

    private static void MeshHeaderWire(DxfVersion version, bool binary)
    {
        var seed = new DxfDocument(version);
        var subjects = new Dictionary<string, (string Name, int Cache, string Operation)>();
        for (int place = 0; place < 4; place++)
        {
            var meshes = new List<Mesh>();
            for (int cache = 0; cache < 3; cache++)
            foreach (string operation in MeshHeaderOperations)
            {
                var mesh = MeshHeaderSubject();
                var data = new XData(new ApplicationRegistry("MESH_HEADER"));
                data.XDataRecord.Add(new XDataRecord(XDataCode.String, $"MH_{place}_{cache}_{operation}"));
                mesh.XData.Add(data); mesh.ProxyGraphics = MeshHeaderCache(cache); meshes.Add(mesh);
            }
            if (place == 0) foreach (var mesh in meshes) seed.Entities.Add(mesh);
            else if (place == 1)
            {
                var layout = new Layout("MH_PAPER"); seed.Layouts.Add(layout);
                foreach (var mesh in meshes) layout.AssociatedBlock.Entities.Add(mesh);
            }
            else
            {
                var block = new Block("MH_CONTAINER_" + place);
                foreach (var mesh in meshes) block.Entities.Add(mesh);
                if (place == 2) seed.Entities.Add(new Insert(block)); else seed.Blocks.Add(block);
            }
            foreach (var mesh in meshes)
            {
                string name = (string)mesh.XData["MESH_HEADER"].XDataRecord.Single().Value;
                string[] fields = name.Split('_');
                subjects.Add(mesh.Handle, (name, int.Parse(fields[2], CultureInfo.InvariantCulture), fields[3]));
            }
        }
        var following = new Line(new Vector3(101, 102, 103), new Vector3(104, 105, 106)); seed.Entities.Add(following);
        string prefix = "mesh-header-" + version + "-" + (binary ? "binary" : "text");
        byte[] source = MeshHeaderSave(seed, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + "-source.dxf"), source);
        var document = MeshHeaderLoad(source);
        foreach (var pair in subjects)
        {
            var mesh = MeshHeaderFind(document, pair.Key);
            MeshHeaderGeometry(mesh); MeshHeaderCheckCache(MeshHeaderCache(pair.Value.Cache), mesh.ProxyGraphics);
            Check(mesh.SubdivisionLevel == 2 && !mesh.BlendCrease, "Hydration header mismatch");
            var vertices = mesh.Vertexes; var faces = mesh.Faces; var edges = mesh.Edges; var owner = mesh.Owner;
            switch (pair.Value.Operation)
            {
                case "level": mesh.SubdivisionLevel = 5; break;
                case "blend": mesh.BlendCrease = true; break;
                case "same-level": mesh.SubdivisionLevel = 2; break;
                case "same-blend": mesh.BlendCrease = false; break;
            }
            Check(ReferenceEquals(vertices, mesh.Vertexes) && ReferenceEquals(faces, mesh.Faces)
                && ReferenceEquals(edges, mesh.Edges) && ReferenceEquals(owner, mesh.Owner), "Hydrated edit replaced identities");
        }
        for (int stage = 0; stage < 2; stage++)
        {
            byte[] bytes = MeshHeaderSave(document, stage == 0 ? binary : !binary);
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, prefix + (stage == 0 ? "-output.dxf" : "-resave.dxf")), bytes);
            document = MeshHeaderLoad(bytes);
            foreach (var pair in subjects)
            {
                var mesh = MeshHeaderFind(document, pair.Key); var specification = pair.Value;
                bool changed = specification.Operation == "level" || specification.Operation == "blend";
                Equal((byte)(specification.Operation == "level" ? 5 : 2), mesh.SubdivisionLevel, "Saved subdivision");
                Equal(specification.Operation == "blend", mesh.BlendCrease, "Saved blend crease");
                MeshHeaderCheckCache(changed ? null : MeshHeaderCache(specification.Cache), mesh.ProxyGraphics);
                MeshHeaderGeometry(mesh);
                Equal(specification.Name, (string)mesh.XData["MESH_HEADER"].XDataRecord.Single().Value, "Saved XData");
            }
            var line = document.GetObjectByHandle(following.Handle) as Line ?? throw new InvalidOperationException("Missing following LINE");
            Check(line.StartPoint == following.StartPoint && line.EndPoint == following.EndPoint, "Following geometry changed");
            Check(document.Objects.Validate().Count == 0, "Saved MESH graph invalid");
        }
    }
}
