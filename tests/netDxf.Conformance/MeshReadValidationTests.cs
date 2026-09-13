using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshReadValidationTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                foreach (int scenario in Enumerable.Range(0, 13))
                {
                    int s = scenario;
                    Run($"mesh/read-validation/invalid/{v}/{b}/{s}", () => MeshReadInvalid(v, b, s));
                }
                Run($"mesh/read-validation/large-declarations/{v}/{b}", () => MeshReadLargeDeclarations(v, b));
                Run($"mesh/read-validation/empty-lists/{v}/{b}", () => MeshReadEmpty(v, b));
                Run($"mesh/read-validation/valid/{v}/{b}", () => MeshReadValid(v, b, false));
                if (!b) Run($"mesh/read-validation/comments/{v}", () => MeshReadValid(v, false, true));
            }
    }

    private static List<DxfTag> MeshReadTags(DxfVersion version) => new()
    {
        new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"), new(1, HeaderVersion(version)), new(0, "ENDSEC"),
        new(0, "SECTION"), new(2, "ENTITIES"), new(0, "MESH"), new(5, "200"),
        new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbSubDMesh"), new(71, (short)2), new(72, (short)1),
        new(91, 3), new(92, 3),
        new(10, 1e-20), new(20, 0.0), new(30, 0.0),
        new(10, 1.0), new(20, 0.0), new(30, 0.0),
        new(10, 0.0), new(20, 1.0), new(30, 0.0),
        new(93, 4), new(90, 3), new(90, 0), new(90, 1), new(90, 2),
        new(94, 1), new(90, 0), new(90, 2), new(95, 1), new(140, 1.25), new(90, 0),
        new(1001, "MESH_READ"), new(1000, "following XData"),
        new(0, "LINE"), new(5, "201"), new(100, "AcDbEntity"), new(8, "0"), new(100, "AcDbLine"),
        new(10, 7.0), new(20, 8.0), new(30, 9.0), new(11, 10.0), new(21, 11.0), new(31, 12.0),
        new(0, "ENDSEC"), new(0, "EOF")
    };

    private static void MeshReadInvalid(DxfVersion version, bool binary, int scenario)
    {
        var tags = MeshReadTags(version);
        int At(short code) => tags.FindIndex(t => t.Code == code);
        int group;
        switch (scenario)
        {
            case 0: group = 92; tags[At(92)] = new(92, -1); break;
            case 1: group = 93; tags[At(93)] = new(93, 3); break; // face overruns declared list size
            case 2: group = 93; tags[At(93) + 1] = new(90, 2); tags.RemoveAt(At(93) + 4); tags[At(93)] = new(93, 3); break;
            case 3: group = 90; tags[At(93) + 2] = new(90, -1); break;
            case 4: group = 90; tags[At(93) + 4] = new(90, 3); break;
            case 5: group = 94; tags[At(94)] = new(94, -1); break;
            case 6: group = 90; tags[At(94) + 2] = new(90, 3); break;
            case 7: group = 10; tags[At(10)] = new(40, 1e-20); break;
            case 8: group = 20; tags[At(20)] = new(21, 0.0); break;
            case 9: group = 90; tags[At(94) + 1] = new(91, 0); break;
            case 10: group = 140; tags[At(140)] = new(40, 1.25); break;
            case 11: group = 95; tags[At(95)] = new(95, 0); break;
            default: group = 91; tags[At(91)] = new(91, 256); break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
#if DEBUG
        try { DxfDocument.Load(input); throw new InvalidOperationException("Malformed MESH was accepted."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("MESH", StringComparison.Ordinal), "Diagnostic lost the entity type.");
            Check(error.Message.Contains(group.ToString(), StringComparison.Ordinal), "Diagnostic lost the responsible group code.");
        }
#else
        Check(DxfDocument.Load(input) == null, "Malformed MESH group " + group + " was accepted.");
#endif
        Check(input.CanRead, "Invalid MESH closed the caller's stream.");
    }

    private static void MeshReadLargeDeclarations(DxfVersion version, bool binary)
    {
        foreach (short group in new short[] { 92, 93, 94, 95 })
            foreach (int count in new[] { -1, int.MaxValue })
            {
                var tags = MeshReadTags(version);
                int at = tags.FindIndex(t => t.Code == group);
                tags[at] = new(group, count);
                if (group == 93 && count == int.MaxValue) tags[at + 1] = new(90, int.MaxValue - 1);
                using var input = new MemoryStream(RawFixtureBytes(tags, binary));
                long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
                Throws<InvalidDataException>(() => DxfDocument.Load(input));
#else
                Check(DxfDocument.Load(input) == null, "Forged MESH list declaration was accepted.");
#endif
                Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024,
                    "A short malformed record allocated memory proportional to its forged count.");
                Check(input.CanRead, "Forged MESH closed the caller stream.");
            }
    }

    private static void MeshReadEmpty(DxfVersion version, bool binary)
    {
        var tags = MeshReadTags(version);
        int start = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 1001);
        tags.RemoveRange(start, end - start);
        tags.InsertRange(start, new DxfTag[] { new(92, 0), new(93, 0), new(94, 0), new(95, 0), new(90, 0) });
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Empty counted lists failed to load.");
        Mesh mesh = doc.Entities.Meshes.Single();
        Equal(0, mesh.Vertexes.Count, "Empty vertices");
        Equal(0, mesh.Faces.Count, "Empty faces");
        Equal(0, mesh.Edges.Count, "Empty edges");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Empty counted mesh failed to save.");
        output.Position = 0; Check(DxfDocument.Load(output) != null, "Empty counted mesh failed to reload.");
    }

    private static void MeshReadValid(DxfVersion version, bool binary, bool comments)
    {
        var tags = MeshReadTags(version);
        if (comments)
        {
            int begin = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 1001);
            for (int i = end - 1; i >= begin; i--) tags.Insert(i + 1, new(999, "ENDSEC 90 140"));
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var doc = DxfDocument.Load(input) ?? throw new InvalidOperationException("Valid counted MESH failed.");
        Mesh mesh = doc.Entities.Meshes.Single();
        SameDoubleBits(1e-20, mesh.Vertexes[0].X, "Counted mesh coordinate");
        Check(new[] { 0, 1, 2 }.SequenceEqual(mesh.Faces.Single()), "Counted mesh face");
        Equal(2, mesh.Edges.Single().EndVertexIndex, "Counted mesh edge");
        Equal(1.25, mesh.Edges.Single().Crease, "Counted mesh crease");
        Equal((byte)3, mesh.SubdivisionLevel, "Counted mesh subdivision");
        Check(mesh.BlendCrease, "Counted mesh blend flag");
        Equal("following XData", (string)mesh.XData["MESH_READ"].XDataRecord.Single().Value, "Counted mesh XData boundary");
        Equal(new Vector3(7, 8, 9), doc.Entities.Lines.Single().StartPoint, "Following entity boundary");
        using var output = new MemoryStream(); Check(doc.Save(output, !binary), "Counted mesh save failed.");
        output.Position = 0;
        Check(DxfDocument.Load(output) != null, "Counted mesh reload failed.");
        if (!comments) File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-validated-{version}-{!binary}.dxf"), output.ToArray());
    }
}
