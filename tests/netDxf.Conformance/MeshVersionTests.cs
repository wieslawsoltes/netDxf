using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshVersionTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int placement in Enumerable.Range(0, 4))
                    foreach (byte level in new byte[] { 0, 2 })
                    {
                        int p = placement; byte l = level;
                        Run($"mesh/export-profile/{v}/{b}/{p}/{l}", () => MeshExportProfile(v, b, p, l));
                    }
                Run($"mesh/legacy-polyline-controls/{v}/{b}", () => MeshLegacyControls(v, b));
                Run($"mesh/empty-document/{v}/{b}", () => MeshEmptyDocument(v, b));
                if (version < DxfVersion.AutoCad2010)
                    Run($"mesh/explicit-upgrade/{v}/{b}", () => MeshExplicitUpgrade(v, b));
            }
    }

    private static Mesh ProfileMesh(byte level) => new(
        new[] { new Vector3(1e-20, 0, 0), new Vector3(2, 0, 0), new Vector3(0, 3, 1) },
        new[] { new[] { 0, 1, 2 } },
        new[] { new MeshEdge(0, 1, 0.0), new MeshEdge(1, 2, 1.5), new MeshEdge(2, 0, -1.0) })
    { SubdivisionLevel = level };

    private static DxfDocument ProfileMeshDocument(DxfVersion version, int placement, byte level)
    {
        var document = new DxfDocument(version);
        document.Comments.Clear();
        Mesh mesh = ProfileMesh(level);
        var metadata = new XData(new ApplicationRegistry("MESH_PROFILE_TEST"));
        metadata.XDataRecord.Add(new XDataRecord(XDataCode.String, "mesh metadata"));
        mesh.XData.Add(metadata);
        switch (placement)
        {
            case 0: document.Entities.Add(mesh); break;
            case 1:
                document.Layouts.Add(new Layout("Paper")); document.Entities.ActiveLayout = "Paper";
                document.Entities.Add(mesh); document.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("MeshInner"); inner.Entities.Add(mesh);
                var outer = new Block("MeshOuter"); outer.Entities.Add(new Insert(inner));
                document.Entities.Add(new Insert(outer)); break;
            default:
                var unused = new Block("MeshUnused"); unused.Entities.Add(mesh); document.Blocks.Add(unused); break;
        }
        return document;
    }

    private static Mesh OnlyProfileMesh(DxfDocument document) => document.Blocks
        .SelectMany(block => block.Entities).OfType<Mesh>().Single();

    private static void SameProfileMesh(Mesh expected, Mesh actual)
    {
        Equal(expected.SubdivisionLevel, actual.SubdivisionLevel, "Mesh subdivision level");
        Check(expected.Vertexes.SequenceEqual(actual.Vertexes), "Mesh vertices changed.");
        SameDoubleBits(expected.Vertexes[0].X, actual.Vertexes[0].X, "Mesh coordinate precision");
        Equal(expected.Faces.Count, actual.Faces.Count, "Mesh face count");
        for (int i = 0; i < expected.Faces.Count; i++)
            Check(expected.Faces[i].SequenceEqual(actual.Faces[i]), "Mesh face topology changed.");
        Equal(expected.Edges.Count, actual.Edges.Count, "Mesh edge count");
        for (int i = 0; i < expected.Edges.Count; i++)
        {
            Equal(expected.Edges[i].StartVertexIndex, actual.Edges[i].StartVertexIndex, "Mesh edge start");
            Equal(expected.Edges[i].EndVertexIndex, actual.Edges[i].EndVertexIndex, "Mesh edge end");
            SameDoubleBits(expected.Edges[i].Crease, actual.Edges[i].Crease, "Mesh crease");
        }
        Equal("mesh metadata", (string)actual.XData["MESH_PROFILE_TEST"].XDataRecord.Single().Value, "Mesh XData");
    }

    private static void MeshExportProfile(DxfVersion version, bool binary, int placement, byte level)
    {
        DxfDocument document = ProfileMeshDocument(version, placement, level);
        Mesh original = OnlyProfileMesh(document);
        using var output = new MemoryStream();
        byte[] prefix = { 12, 34, 56, 78, 90 }; output.Write(prefix); output.Position = 2;
        string handles = document.DrawingVariables.HandleSeed;
        string active = document.Entities.ActiveLayout;
        int layouts = document.Layouts.Count, apps = document.ApplicationRegistries.Count;
        var entities = document.Blocks.SelectMany(b => b.Entities).ToArray();
        string[] identities = entities.Select(e => e.Handle).ToArray();
        if (version < DxfVersion.AutoCad2010)
        {
#if DEBUG
            try
            {
                document.Save(output, binary);
                throw new InvalidOperationException("Old target accepted a modern MESH record.");
            }
            catch (NotSupportedException exception)
            {
                Check(exception.Message.Contains("MESH", StringComparison.Ordinal) &&
                      exception.Message.Contains("2010", StringComparison.Ordinal), "Missing feature/version diagnostic.");
            }
#else
            Check(!document.Save(output, binary), "Old target accepted a modern MESH record.");
#endif
            Check(prefix.SequenceEqual(output.ToArray()), "Rejected mesh export wrote destination bytes.");
            Equal(2L, output.Position, "Rejected mesh export advanced destination");
            Equal(handles, document.DrawingVariables.HandleSeed, "Preflight allocated handles");
            Equal(layouts, document.Layouts.Count, "Preflight added layouts");
            Equal(apps, document.ApplicationRegistries.Count, "Preflight added application registrations");
            Equal(active, document.Entities.ActiveLayout, "Preflight changed active layout");
            Check(entities.SequenceEqual(document.Blocks.SelectMany(b => b.Entities)), "Preflight replaced entities.");
            Check(identities.SequenceEqual(entities.Select(e => e.Handle)), "Preflight changed entity identities.");
        }
        else
        {
            output.SetLength(0); output.Position = 0;
            Check(document.Save(output, binary), "Supported MESH export was rejected.");
            byte[] bytes = output.ToArray(); output.Position = 0;
            DxfDocument loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Supported MESH reload failed.");
            SameProfileMesh(original, OnlyProfileMesh(loaded));
            output.Position = 0; var raw = DxfRawDocument.Load(output);
            Equal(version, raw.Version, "Mesh output version");
            Equal(1, raw.Sections.SelectMany(s => s.Records).Count(r => r.Name == "MESH"), "Mesh wire record count");
            using var second = new MemoryStream(); Check(loaded.Save(second, !binary), "Mesh cross-transport export failed.");
            second.Position = 0; SameProfileMesh(original, OnlyProfileMesh(DxfDocument.Load(second)!));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-profile-{version}-{binary}-{placement}-{level}.dxf"), bytes);
        }
        Check(output.CanWrite, "Mesh export closed caller-owned stream.");
    }

    private static void MeshLegacyControls(DxfVersion version, bool binary)
    {
        var vertices = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, new Vector3(1, 1, 0) };
        var polygon = new PolygonMesh(2, 2, vertices);
        var polyface = new PolyfaceMesh(vertices, new[] { new short[] { 1, 2, 4, 3 } });
        var document = new DxfDocument(version); document.Entities.Add(polygon); document.Entities.Add(polyface);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Legacy POLYLINE mesh was blocked.");
        output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Legacy mesh reload failed.");
        Check(vertices.SequenceEqual(loaded.Entities.PolygonMeshes.Single().Vertexes), "Polygon mesh vertices changed.");
        Check(vertices.SequenceEqual(loaded.Entities.PolyfaceMeshes.Single().Vertexes), "Polyface mesh vertices changed.");
        Equal(0, loaded.Entities.Meshes.Count(), "Legacy mesh converted to modern MESH.");
        output.Position = 0; var raw = DxfRawDocument.Load(output);
        Equal(0, raw.Sections.SelectMany(s => s.Records).Count(r => r.Name == "MESH"), "Legacy output contains modern MESH.");
    }

    private static void MeshEmptyDocument(DxfVersion version, bool binary)
    {
        using var stream = new MemoryStream();
        Check(new DxfDocument(version).Save(stream, binary), "Mesh gate rejected a mesh-free document.");
    }

    private static void MeshExplicitUpgrade(DxfVersion version, bool binary)
    {
        DxfDocument document = ProfileMeshDocument(version, 2, 2);
        document.DrawingVariables.AcadVer = DxfVersion.AutoCad2010;
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Explicit upgrade was rejected.");
        stream.Position = 0; var raw = DxfRawDocument.Load(stream);
        // The preservation layer may retain out-of-profile records; it does not certify legality.
        var declared = raw.Tags.ToArray();
        int index = Array.FindIndex(declared, t => t.Code == 9 && Equals(t.Value, "$ACADVER")) + 1;
        declared[index] = new DxfTag(1, HeaderVersion(version));
        var oldProfile = DxfRawDocument.Create(declared, binary);
        using var retained = new MemoryStream(); oldProfile.Save(retained); retained.Position = 0;
        Equal(version, DxfRawDocument.Load(retained).Version, "Raw retention unexpectedly changed the profile.");
        retained.Position = 0;
        var loaded = DxfDocument.Load(retained) ?? throw new InvalidOperationException("Read-side retention changed.");
        SameProfileMesh(OnlyProfileMesh(document), OnlyProfileMesh(loaded));
        loaded.DrawingVariables.AcadVer = DxfVersion.AutoCad2010;
        using var promoted = new MemoryStream(); Check(loaded.Save(promoted, !binary), "Explicit promotion failed.");
    }
}
