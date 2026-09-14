using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshWriteValidationTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
            foreach (bool binary in new[] { false, true })
                foreach (int placement in Enumerable.Range(0, 4))
                {
                    DxfVersion v = version; bool b = binary; int p = placement;
                    foreach (int defect in Enumerable.Range(0, 13))
                    {
                        int d = defect;
                        Run($"mesh/write-validation/{v}/{b}/{p}/{d}", () => MeshWriteInvalid(v, b, p, d));
                    }
                    Run($"mesh/write-validation/valid/{v}/{b}/{p}", () => MeshWriteValid(v, b, p));
                }
        // Shared array references forge an overflowing serialized count cheaply.
        // The red run deliberately omits this case until preflight is implemented:
        // the old writer would attempt to emit 2.2 billion list entries.
        Run("mesh/write-validation/empty", MeshWriteEmpty);
        Run("mesh/write-validation/count-overflow", MeshWriteCountOverflow);
    }

    private static void MeshWriteInvalid(DxfVersion version, bool binary, int placement, int defect)
    {
        DxfDocument doc = ProfileMeshDocument(version, placement, 2);
        Mesh mesh = OnlyProfileMesh(doc);
        switch (defect)
        {
            case 0: mesh.Faces[0] = null!; break;
            case 1: mesh.Faces[0] = Array.Empty<int>(); break;
            case 2: mesh.Faces[0] = new[] { 0, 1 }; break;
            case 3: mesh.Faces[0][1] = -1; break;
            case 4: mesh.Faces[0][1] = mesh.Vertexes.Count; break;
            case 5: mesh.Vertexes.RemoveAt(2); break;
            case 6: mesh.Edges[1] = null!; break;
            case 7: mesh.Edges[1].StartVertexIndex = mesh.Vertexes.Count; break;
            case 8: mesh.Edges[1].EndVertexIndex = int.MaxValue; break;
            case 9: mesh.Edges[1].Crease = double.NaN; break;
            case 10: mesh.Edges[1].Crease = double.PositiveInfinity; break;
            case 11: mesh.Vertexes[0] = new(double.NaN, 0, 0); break;
            case 12: mesh.Vertexes[0] = new(0, double.NegativeInfinity, double.PositiveInfinity); break;
        }
        MeshExpectPreflight(doc, binary);
    }

    private static void MeshExpectPreflight(DxfDocument doc, bool binary)
    {
        using var output = new MemoryStream(); byte[] initial = { 10, 20, 30, 40, 50 }; output.Write(initial); output.Position = 2;
        string seed = doc.DrawingVariables.HandleSeed; string active = doc.Entities.ActiveLayout;
        var registrations = doc.ApplicationRegistries.ToArray(); var layouts = doc.Layouts.ToArray();
        var entities = doc.Blocks.SelectMany(b => b.Entities).ToArray(); var handles = entities.Select(e => e.Handle).ToArray();
#if DEBUG
        try { doc.Save(output, binary); throw new InvalidOperationException("Invalid mutable MESH was exported."); }
        catch (InvalidOperationException exception)
        {
            Check(exception.Message.Contains("MESH", StringComparison.Ordinal) && exception.Message.Contains("group", StringComparison.Ordinal),
                "Missing contextual MESH preflight error: " + exception.Message);
        }
#else
        Check(!doc.Save(output, binary), "Invalid mutable MESH was exported.");
#endif
        Check(initial.SequenceEqual(output.ToArray()), "Invalid MESH changed destination bytes.");
        Equal(2L, output.Position, "Invalid MESH moved destination position");
        Equal(seed, doc.DrawingVariables.HandleSeed, "Invalid MESH allocated handles");
        Equal(active, doc.Entities.ActiveLayout, "Invalid MESH changed active layout");
        Check(registrations.SequenceEqual(doc.ApplicationRegistries), "Invalid MESH registered APPIDs.");
        Check(layouts.SequenceEqual(doc.Layouts), "Invalid MESH created layouts.");
        Check(entities.SequenceEqual(doc.Blocks.SelectMany(b => b.Entities)) && handles.SequenceEqual(entities.Select(e => e.Handle)), "Invalid MESH mutated entity identities.");
        Check(output.CanWrite, "Preflight closed caller-owned stream.");
    }

    private static void MeshWriteValid(DxfVersion version, bool binary, int placement)
    {
        DxfDocument doc = ProfileMeshDocument(version, placement, 255); Mesh mesh = OnlyProfileMesh(doc);
        mesh.BlendCrease = true;
        mesh.Vertexes.Add(new(5, 7, 9));
        mesh.Faces.Add(new[] { 0, 1, 3, 2 });
        // Neither an unreferenced vertex nor duplicate face indices imply bad lexical topology.
        mesh.Vertexes.Add(new(10, 11, 12)); mesh.Faces.Add(new[] { 0, 1, 1 });
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Valid mutable MESH rejected.");
        output.Position = 0; DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Validated output cannot reload.");
        SameProfileMesh(mesh, OnlyProfileMesh(loaded));
        Equal(true, OnlyProfileMesh(loaded).BlendCrease, "Validation changed blend crease");
        output.Position = 0;
        var raw = DxfRawDocument.Load(output);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "MESH");
        Equal(13, (int)record.Tags.Single(t => t.Code == 93).Value, "Serialized total face-list size");
        if (placement == 0)
        {
            // Retained independent fixtures use a nondegenerate face corpus.
            mesh.Faces.RemoveAt(2); output.SetLength(0); output.Position = 0; Check(doc.Save(output, binary), "Fixture save failed.");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-write-validation-{version}-{binary}.dxf"), output.ToArray());
        }
    }

    private static void MeshWriteEmpty()
    {
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(new Mesh(Array.Empty<Vector3>(), Array.Empty<int[]>()));
        using var output = new MemoryStream(); Check(doc.Save(output, true), "Empty mesh policy changed.");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Empty mesh reload failed.");
        Equal(0, loaded.Entities.Meshes.Single().Vertexes.Count, "Empty mesh changed");
    }

    private static void MeshWriteCountOverflow()
    {
        var shared = new int[100000];
        var mesh = new Mesh(new[] { Vector3.Zero }, Enumerable.Repeat(shared, 22000));
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(mesh);
        MeshExpectPreflight(doc, false);
        MeshExpectPreflight(doc, true);
    }
}
