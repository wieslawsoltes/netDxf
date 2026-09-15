using System.IO.Compression;
using System.Security.Cryptography;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshOverrideDeclarationTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (string scenario in new[] { "nonzero", "marker", "property-count", "negative", "large", "private-then-public", "subclass-then-public" })
            {
                string s = scenario;
                Run($"mesh/override-declaration/reject/{v}/{b}/{s}", () => MeshOverrideReject(v, b, s));
            }
            foreach (string scenario in new[] { "zero", "absent", "private-before", "private-after", "private-nested", "later-subclass", "xdata-trailer", "comments" })
            {
                string s = scenario;
                Run($"mesh/override-declaration/accept/{v}/{b}/{s}", () => MeshOverrideAccept(v, b, s));
            }
            foreach (string scenario in new[] { "zero", "absent", "nonzero", "negative" })
            {
                string s = scenario;
                Run($"mesh/override-declaration/optional-edges/{v}/{b}/{s}", () => MeshOverrideOptionalEdges(v, b, s));
            }
        }
        foreach (bool binary in new[] { false, true })
        {
            bool b = binary;
            Run($"mesh/override-declaration/native-zero/{b}", () => MeshOverrideNative(b));
        }
    }

    private static DxfTag[] MeshOverridePrivate(bool nested = false)
    {
        var tags = new List<DxfTag> { new(102, "{PRIVATE_MESH") };
        if (nested) tags.Add(new(102, "{INNER"));
        tags.AddRange(new DxfTag[] { new(90, 1), new(91, 7), new(92, 0), new(100, "PrivateMeshSubclass") });
        if (nested) tags.Add(new(102, "}"));
        tags.Add(new(102, "}"));
        return tags.ToArray();
    }

    private static void MeshOverrideReject(DxfVersion version, bool binary, string scenario)
    {
        // These are unsupported-declaration controls. They do not claim to encode
        // a valid native property value packet, whose grammar is not qualified.
        var tags = MeshReadTags(version);
        int suffix = tags.FindIndex(t => t.Code == 1001) - 1;
        tags[suffix] = new(90, scenario == "negative" ? -1 : scenario == "large" ? int.MaxValue : 1);
        if (scenario is "marker" or "property-count") tags.Insert(suffix + 1, new(91, 7));
        if (scenario == "property-count") tags.Insert(suffix + 2, new(92, 0));
        if (scenario == "private-then-public") tags.InsertRange(suffix, MeshOverridePrivate(true));
        if (scenario == "subclass-then-public") tags.InsertRange(suffix,
            new DxfTag[] { new(100, "FutureMeshSubclass"), new(90, 0), new(100, "AcDbSubDMesh") });
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        try { DxfDocument.Load(input); throw new Exception("Unsupported MESH override declaration was accepted."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("MESH", StringComparison.Ordinal) && error.Message.Contains("90", StringComparison.Ordinal), "Override diagnostic lost entity/count context.");
            Check(error.Message.Contains(scenario == "negative" ? "negative" : "not supported", StringComparison.Ordinal), "Override diagnostic lost the unsupported/malformed distinction.");
        }
#else
        Check(DxfDocument.Load(input) == null, "Unsupported MESH override declaration was accepted.");
#endif
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Override declaration allocated in proportion to its untrusted count.");
        Check(input.CanRead, "Override rejection closed the caller stream.");
    }

    private static void MeshOverrideAccept(DxfVersion version, bool binary, string scenario)
    {
        var tags = MeshReadTags(version);
        int suffix = tags.FindIndex(t => t.Code == 1001) - 1;
        switch (scenario)
        {
            case "absent": tags.RemoveAt(suffix); break;
            case "private-before": tags.InsertRange(suffix, MeshOverridePrivate()); break;
            case "private-after": tags.InsertRange(suffix + 1, MeshOverridePrivate()); break;
            case "private-nested": tags.InsertRange(suffix, MeshOverridePrivate(true)); break;
            case "later-subclass": tags.InsertRange(suffix + 1,
                new DxfTag[] { new(100, "FutureMeshSubclass"), new(90, 1), new(91, 7), new(92, 0) }); break;
            case "xdata-trailer": tags.InsertRange(tags.FindIndex(t => t.Code == 0 && (string)t.Value == "LINE"),
                new DxfTag[] { new(90, 1), new(91, 7), new(92, 0), new(100, "AcDbSubDMesh"), new(90, 1) }); break;
            case "comments": if (!binary) { tags.Insert(suffix, new(999, "90 1 91 7 92 0")); tags.Insert(suffix + 2, new(999, "after override count")); } break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        DxfDocument document = DxfDocument.Load(input) ?? throw new Exception("Zero/absent public MESH override declaration was rejected.");
        MeshOverrideCheckGeometry(document);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "Zero-override MESH save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-override-{version}-{binary}-{scenario}.dxf"), output.ToArray());
        output.Position = 0; MeshOverrideCheckGeometry(DxfDocument.Load(output) ?? throw new Exception("Zero-override MESH reload failed."));
    }

    private static void MeshOverrideCheckGeometry(DxfDocument document, bool hasEdge = true)
    {
        Mesh mesh = document.Entities.Meshes.Single();
        Equal((byte)3, mesh.SubdivisionLevel, "Private/suffix marker overwrote subdivision");
        Equal(3, mesh.Vertexes.Count, "Private/suffix property count overwrote vertices");
        SameDoubleBits(1e-20, mesh.Vertexes[0].X, "Vertex bits changed");
        Equal(new Vector3(1, 0, 0), mesh.Vertexes[1], "Second vertex changed");
        Equal(new Vector3(0, 1, 0), mesh.Vertexes[2], "Third vertex changed");
        Check(mesh.Faces.Single().SequenceEqual(new[] { 0, 1, 2 }), "Face topology changed");
        if (hasEdge)
        {
            Equal(0, mesh.Edges.Single().StartVertexIndex, "Edge start changed");
            Equal(2, mesh.Edges.Single().EndVertexIndex, "Edge end changed");
            Equal(1.25, mesh.Edges.Single().Crease, "Crease changed");
        }
        else Equal(0, mesh.Edges.Count, "Optional edge list acquired edges");
        Check(mesh.BlendCrease, "Blend flag changed");
        Equal("following XData", (string)mesh.XData["MESH_READ"].XDataRecord.Single().Value, "XData boundary changed");
        Equal(new Vector3(7, 8, 9), document.Entities.Lines.Single().StartPoint, "Following entity changed");
    }

    private static void MeshOverrideOptionalEdges(DxfVersion version, bool binary, string scenario)
    {
        using var seed = new MemoryStream(RawFixtureBytes(MeshReadTags(version), binary));
        var authored = DxfDocument.Load(seed) ?? throw new Exception("Optional-edge seed load failed.");
        authored.Entities.Meshes.Single().Edges.Clear();
        using var saved = new MemoryStream(); Check(authored.Save(saved, binary), "Empty-edge authored save failed.");
        saved.Position = 0; MeshOverrideCheckGeometry(DxfDocument.Load(saved) ?? throw new Exception("Empty-edge authored reload failed."), false);
        saved.Position = 0; var raw = DxfRawDocument.Load(saved);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "MESH");
        var tags = record.Tags.ToList();
        Equal(0, (int)tags.Single(t => t.Code == 94).Value, "Authored empty edge count");
        Equal(0, (int)tags.Single(t => t.Code == 95).Value, "Authored empty crease count");
        // The current public model canonicalizes null edges to an empty list.
        // Explicitly omit its two authored zero-count packets to exercise the
        // existing accepted representation with optional edge data absent.
        tags.RemoveAll(t => t.Code is 94 or 95);
        int suffix = tags.FindIndex(t => t.Code == 1001) - 1;
        Equal((short)90, tags[suffix].Code, "Authored override declaration boundary");
        if (scenario == "absent") tags.RemoveAt(suffix);
        else tags[suffix] = new(90, scenario == "nonzero" ? 1 : scenario == "negative" ? -1 : 0);
        raw = raw.WithRecord(record, tags);
        using var input = new MemoryStream(); raw.Save(input, binary); input.Position = 0;
        if (scenario is "nonzero" or "negative")
        {
#if DEBUG
            try { DxfDocument.Load(input); throw new Exception("Optional-edge override declaration accepted."); }
            catch (InvalidDataException error)
            {
                Check(error.Message.Contains("90", StringComparison.Ordinal) && error.Message.Contains(scenario == "negative" ? "negative" : "not supported", StringComparison.Ordinal), "Optional-edge diagnostic changed.");
            }
#else
            Check(DxfDocument.Load(input) == null, "Optional-edge override declaration accepted.");
#endif
            Check(input.CanRead, "Optional-edge rejection closed caller stream.");
            return;
        }
        var loaded = DxfDocument.Load(input) ?? throw new Exception("Optional-edge zero/absent declaration rejected.");
        MeshOverrideCheckGeometry(loaded, false);
        using var output = new MemoryStream(); Check(loaded.Save(output, binary), "Optional-edge save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-override-optional-{version}-{binary}-{scenario}.dxf"), output.ToArray());
        output.Position = 0; MeshOverrideCheckGeometry(DxfDocument.Load(output) ?? throw new Exception("Optional-edge reload failed."), false);
    }

    private static void MeshOverrideNative(bool binary)
    {
        using var compressed = File.OpenRead(Path.Combine("tests", "fixtures", "table-oracle", CommonProxyNativeFile + ".gz"));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var original = new MemoryStream(); gzip.CopyTo(original);
        Equal(CommonProxyNativeSha256, Convert.ToHexString(SHA256.HashData(original.ToArray())).ToLowerInvariant(), "Native MESH source hash");
        original.Position = 0;
        var packets = DxfRawDocument.Load(original).Sections.SelectMany(s => s.Records).Where(r => r.Name == "MESH").ToArray();
        Equal(2, packets.Length, "Native zero-override MESH inventory");
        foreach (var packet in packets)
        {
            string handle = (string)packet.Tags.Single(t => t.Code == 5).Value;
            var body = packet.Tags.SkipWhile(t => t.Code != 100 || (string)t.Value != "AcDbSubDMesh").ToArray();
            Equal((short)90, body.Last().Code, "Native override declaration code"); Equal(0, (int)body.Last().Value, "Native override declaration value");
            // Reuse the exact native subclass in a minimal R2010 carrier. The
            // source common proxy/owner packet and complete drawing are outside this test.
            var tags = MeshReadTags(DxfVersion.AutoCad2010);
            int start = tags.FindIndex(t => t.Code == 100 && (string)t.Value == "AcDbSubDMesh");
            int end = tags.FindIndex(start, t => t.Code == 1001);
            tags.RemoveRange(start, end - start); tags.InsertRange(start, body);
            using var input = new MemoryStream(RawFixtureBytes(tags, binary));
            var document = DxfDocument.Load(input) ?? throw new Exception("Native zero-override subclass failed to load.");
            using var output = new MemoryStream(); Check(document.Save(output, binary), "Native zero-override subclass failed to save.");
            File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-override-native-{handle}-{binary}.dxf"), output.ToArray());
            output.Position = 0;
            var written = DxfRawDocument.Load(output).Sections.SelectMany(s => s.Records).Single(r => r.Name == "MESH").Tags;
            var actual = written.SkipWhile(t => t.Code != 100 || (string)t.Value != "AcDbSubDMesh").TakeWhile(t => t.Code != 1001).ToArray();
            SameRawTags(body, actual);
        }
    }
}
