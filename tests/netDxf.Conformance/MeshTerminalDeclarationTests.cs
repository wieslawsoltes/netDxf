using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshTerminalDeclarationTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
        foreach (bool binary in new[] { false, true })
        foreach (bool optionalEdges in new[] { false, true })
        {
            var v = version; bool b = binary, optional = optionalEdges;
            foreach (string scenario in new[] { "version", "blend", "subdivision", "vertices", "faces", "edges", "creases", "point-x", "point-y", "point-z", "crease-value", "duplicate-zero", "private-return", "subclass-return" })
            {
                string s = scenario;
                Run($"mesh/terminal/reject/{v}/{b}/{optional}/{s}", () => MeshTerminalReject(v, b, optional, s));
            }
            foreach (string scenario in new[] { "private", "nested", "later-subclass", "private-subclass", "xdata", "comments" })
            {
                string s = scenario;
                Run($"mesh/terminal/accept/{v}/{b}/{optional}/{s}", () => MeshTerminalAccept(v, b, optional, s));
            }
        }
    }

    private static List<DxfTag> MeshTerminalTags(DxfVersion version, bool optionalEdges)
    {
        var tags = MeshReadTags(version);
        if (optionalEdges)
        {
            int edge = tags.FindIndex(t => t.Code == 94);
            int terminal = tags.FindIndex(t => t.Code == 1001) - 1;
            tags.RemoveRange(edge, terminal - edge);
        }
        return tags;
    }

    private static DxfTag[] MeshTerminalTail(string scenario) => scenario switch
    {
        "version" => new DxfTag[] { new(71, (short)3) },
        "blend" => new DxfTag[] { new(72, (short)0) },
        "subdivision" => new DxfTag[] { new(91, 7) },
        "vertices" => new DxfTag[] { new(92, 3), new(10, 10.0), new(20, 20.0), new(30, 30.0), new(10, 1.0), new(20, 0.0), new(30, 0.0), new(10, 0.0), new(20, 1.0), new(30, 0.0) },
        "faces" => new DxfTag[] { new(93, 4), new(90, 3), new(90, 2), new(90, 1), new(90, 0) },
        "edges" => new DxfTag[] { new(94, 1), new(90, 0), new(90, 1) },
        "creases" => new DxfTag[] { new(95, 1), new(140, 9.0) },
        "point-x" => new DxfTag[] { new(10, 100.0) },
        "point-y" => new DxfTag[] { new(20, 100.0) },
        "point-z" => new DxfTag[] { new(30, 100.0) },
        "crease-value" => new DxfTag[] { new(140, 9.0) },
        "duplicate-zero" => new DxfTag[] { new(90, 0) },
        "private-return" => MeshOverridePrivate(true).Concat(new DxfTag[] { new(91, 7) }).ToArray(),
        "subclass-return" => new DxfTag[] { new(100, "FutureMeshSubclass"), new(91, 8), new(100, "AcDbSubDMesh"), new(91, 7) },
        _ => throw new ArgumentOutOfRangeException(nameof(scenario))
    };

    private static void MeshTerminalReject(DxfVersion version, bool binary, bool optionalEdges, string scenario)
    {
        var tags = MeshTerminalTags(version, optionalEdges);
        tags.InsertRange(tags.FindIndex(t => t.Code == 1001), MeshTerminalTail(scenario));
        byte[] bytes = RawFixtureBytes(tags, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-terminal-input-{version}-{binary}-{optionalEdges}-{scenario}.dxf"), bytes);
        using var input = new MemoryStream(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        try { DxfDocument.Load(input); throw new Exception("Trailing public MESH field was admitted after the terminal zero declaration."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("MESH", StringComparison.Ordinal) && error.Message.Contains("terminal", StringComparison.Ordinal), "Terminal-state diagnostic lost its entity/phase context: " + error.Message);
        }
#else
        Check(DxfDocument.Load(input) == null, "Trailing public MESH field was admitted after the terminal zero declaration.");
#endif
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Terminal rejection allocated from a trailing count.");
        Check(input.CanRead, "Terminal rejection closed the caller stream.");
    }

    private static void MeshTerminalAccept(DxfVersion version, bool binary, bool optionalEdges, string scenario)
    {
        var tags = MeshTerminalTags(version, optionalEdges);
        int after = tags.FindIndex(t => t.Code == 1001);
        var publicLookalikes = new DxfTag[] { new(71, (short)3), new(72, (short)0), new(91, 7), new(92, 0), new(93, 0), new(94, 0), new(95, 0), new(90, 1), new(10, 99.0), new(20, 98.0), new(30, 97.0), new(140, 9.0) };
        switch (scenario)
        {
            case "private":
            case "nested":
            case "private-subclass":
                var privateTags = new List<DxfTag> { new(102, "{PRIVATE_MESH_TERMINAL") };
                if (scenario == "nested") privateTags.Add(new(102, "{INNER"));
                if (scenario == "private-subclass") privateTags.Add(new(100, "PrivateMeshSubclass"));
                privateTags.AddRange(publicLookalikes);
                if (scenario == "nested") privateTags.Add(new(102, "}"));
                privateTags.Add(new(102, "}"));
                tags.InsertRange(after, privateTags);
                break;
            case "later-subclass":
                tags.InsertRange(after, new DxfTag[] { new(100, "FutureMeshSubclass") }.Concat(publicLookalikes));
                break;
            case "xdata":
                // Retain the existing reader boundary: a non-XData trailer after
                // XData starts cannot become public mesh geometry, even if it
                // repeats the subclass marker. This is not a valid-tail claim.
                int following = tags.FindIndex(t => t.Code == 0 && (string)t.Value == "LINE");
                tags.InsertRange(following, new DxfTag[] { new(100, "AcDbSubDMesh") }.Concat(publicLookalikes));
                break;
            case "comments":
                if (!binary) tags.Insert(after, new DxfTag(999, "91 7 after terminal zero"));
                break;
        }
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new Exception("Scoped non-public terminal payload was rejected.");
        MeshOverrideCheckGeometry(document, !optionalEdges);
        using var output = new MemoryStream();
        Check(document.Save(output, binary), "Terminal-context control did not save.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-terminal-{version}-{binary}-{optionalEdges}-{scenario}.dxf"), output.ToArray());
        output.Position = 0;
        MeshOverrideCheckGeometry(DxfDocument.Load(output) ?? throw new Exception("Terminal-context control did not reload."), !optionalEdges);
    }
}
