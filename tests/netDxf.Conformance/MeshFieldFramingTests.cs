using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterMeshFieldFramingTests()
    {
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2010))
        foreach (bool binary in new[] { false, true })
        {
            var v = version; bool b = binary;
            foreach (short code in new short[] { 71, 72, 91, 92, 93, 94, 95 })
            foreach (bool changed in new[] { false, true })
            foreach (bool after in new[] { false, true })
            {
                short c = code; bool change = changed, a = after;
                Run($"mesh/framing/duplicate/{v}/{b}/{c}/{change}/{a}", () => MeshFramingDuplicate(v, b, c, change, a));
            }
            foreach (bool after in new[] { false, true })
            {
                bool a = after;
                Run($"mesh/framing/duplicate/{v}/{b}/90/{a}", () => MeshFramingDuplicate(v, b, 90, false, a));
                foreach (short code in new short[] { 92, 93, 94, 95 })
                {
                    short c = code;
                    Run($"mesh/framing/empty-duplicate/{v}/{b}/{c}/{a}", () => MeshFramingDuplicate(v, b, c, false, a, true));
                }
            }
            foreach (short code in new short[] { 10, 20, 30, 140 })
            foreach (int position in new[] { 0, 1, 2 })
            {
                short c = code; int p = position;
                Run($"mesh/framing/orphan/{v}/{b}/{c}/{p}", () => MeshFramingOrphan(v, b, c, p));
            }
            foreach (short before in new short[] { 71, 92, 95 })
            foreach (int count in new[] { -1, 1, int.MaxValue })
            {
                short p = before; int c = count;
                Run($"mesh/framing/early-invalid-override/{v}/{b}/{p}/{c}", () => MeshFramingEarlyInvalidOverride(v, b, p, c));
            }
            foreach (short code in new short[] { 92, 93, 94, 95 })
            foreach (bool after in new[] { false, true })
            {
                short c = code; bool a = after;
                Run($"mesh/framing/large-duplicate/{v}/{b}/{c}/{a}", () => MeshFramingLargeDuplicate(v, b, c, a));
            }
            foreach (string scope in new[] { "private", "subclass" })
            {
                string s = scope;
                Run($"mesh/framing/reentry/{v}/{b}/{s}", () => MeshFramingReentry(v, b, s));
            }
            foreach (bool optional in new[] { false, true })
            foreach (string scope in new[] { "private", "nested", "later-subclass", "private-subclass", "xdata", "comments" })
            {
                bool o = optional; string s = scope;
                Run($"mesh/framing/scoped/{v}/{b}/{o}/{s}", () => MeshFramingScoped(v, b, o, s));
            }
            foreach (string scenario in new[] { "late-version", "late-blend", "late-subdivision", "late-vertices", "late-faces", "late-edges-creases", "late-creases", "early-zero" })
            {
                string s = scenario;
                Run($"mesh/framing/unique/{v}/{b}/{s}", () => MeshFramingUnique(v, b, s));
            }
        }
    }

    private static (int Start, int Count) MeshFramingPacket(List<DxfTag> tags, short code)
    {
        int end = tags.FindIndex(t => t.Code == 1001), start = code == 90 ? end - 1 : tags.FindIndex(t => t.Code == code);
        int next = code switch
        {
            92 => tags.FindIndex(t => t.Code == 93),
            93 => tags.FindIndex(t => t.Code == 94),
            94 => tags.FindIndex(t => t.Code == 95),
            95 => end - 1,
            _ => start + 1
        };
        return (start, next - start);
    }

    private static void MeshFramingDuplicate(DxfVersion version, bool binary, short code, bool changed, bool after, bool empty = false)
    {
        var tags = MeshReadTags(version);
        if (empty)
        {
            int start = tags.FindIndex(t => t.Code == 92), end = tags.FindIndex(t => t.Code == 1001);
            tags.RemoveRange(start, end - start);
            tags.InsertRange(start, new DxfTag[] { new(92, 0), new(93, 0), new(94, 0), new(95, 0), new(90, 0) });
        }
        var span = MeshFramingPacket(tags, code);
        var duplicate = tags.GetRange(span.Start, span.Count);
        if (changed)
        {
            switch (code)
            {
                case 71: duplicate[0] = new(71, (short)3); break;
                case 72: duplicate[0] = new(72, (short)0); break;
                case 91: duplicate[0] = new(91, 7); break;
                case 92: duplicate[0] = new(92, 4); duplicate.AddRange(new DxfTag[] { new(10, 9.0), new(20, 8.0), new(30, 7.0) }); break;
                case 93: duplicate[0] = new(93, 8); duplicate.AddRange(new DxfTag[] { new(90, 3), new(90, 2), new(90, 1), new(90, 0) }); break;
                case 94: duplicate[0] = new(94, 2); duplicate.AddRange(new DxfTag[] { new(90, 1), new(90, 2) }); break;
                case 95: duplicate[1] = new(140, 9.0); break;
            }
        }
        int at = tags.FindIndex(t => t.Code == 1001) - (after ? 0 : 1);
        tags.InsertRange(at, duplicate);
        MeshFramingReject(tags, binary, $"duplicate-{version}-{binary}-{code}-{changed}-{after}-{empty}", "more than once", code);
    }

    private static void MeshFramingOrphan(DxfVersion version, bool binary, short code, int position)
    {
        var tags = MeshReadTags(version);
        int at = position == 0 ? tags.FindIndex(t => t.Code == 92) : tags.FindIndex(t => t.Code == 1001) - (position == 1 ? 1 : 0);
        tags.Insert(at, new DxfTag(code, 99.0));
        MeshFramingReject(tags, binary, $"orphan-{version}-{binary}-{code}-{position}", "counted list", code);
    }

    private static void MeshFramingReentry(DxfVersion version, bool binary, string scope)
    {
        var tags = MeshReadTags(version);
        var privateTags = scope == "private" ? MeshOverridePrivate(true) : new DxfTag[] { new(100, "FutureMeshSubclass"), new(91, 8), new(100, "AcDbSubDMesh") };
        tags.InsertRange(tags.FindIndex(t => t.Code == 1001), privateTags.Concat(new DxfTag[] { new(91, 7) }));
        MeshFramingReject(tags, binary, $"reentry-{version}-{binary}-{scope}", "more than once", 91);
    }

    private static void MeshFramingEarlyInvalidOverride(DxfVersion version, bool binary, short before, int count)
    {
        var tags = MeshReadTags(version);
        tags.RemoveAt(tags.FindIndex(t => t.Code == 1001) - 1);
        tags.Insert(tags.FindIndex(t => t.Code == before), new DxfTag(90, count));
        MeshFramingReject(tags, binary, $"early-override-{version}-{binary}-{before}-{count}", count < 0 ? "negative" : "not supported", 90);
    }

    private static void MeshFramingLargeDuplicate(DxfVersion version, bool binary, short code, bool after)
    {
        var tags = MeshReadTags(version);
        // No payload follows the forged count: rejection must identify the
        // repeated declaration before attempting to consume or allocate a list.
        tags.Insert(tags.FindIndex(t => t.Code == 1001) - (after ? 0 : 1), new DxfTag(code, int.MaxValue));
        MeshFramingReject(tags, binary, $"large-duplicate-{version}-{binary}-{code}-{after}", "more than once", code);
    }

    private static void MeshFramingReject(List<DxfTag> tags, bool binary, string name, string diagnostic, short group)
    {
        byte[] bytes = RawFixtureBytes(tags, binary);
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-framing-input-{name}.dxf"), bytes);
        using var input = new MemoryStream(bytes);
        long before = GC.GetAllocatedBytesForCurrentThread();
#if DEBUG
        try { DxfDocument.Load(input); throw new Exception("Invalid public MESH field framing was admitted."); }
        catch (InvalidDataException error)
        {
            Check(error.Message.Contains("MESH group " + group + " at position ", StringComparison.Ordinal) && error.Message.Contains(diagnostic, StringComparison.Ordinal), "Framing diagnostic lost entity/group/position context: " + error.Message);
        }
#else
        Check(DxfDocument.Load(input) == null, "Invalid public MESH field framing was admitted.");
#endif
        Check(GC.GetAllocatedBytesForCurrentThread() - before < 4 * 1024 * 1024, "Framing rejection allocated from a trailing count.");
        Check(input.CanRead, "Framing rejection closed the caller stream.");
    }

    private static void MeshFramingScoped(DxfVersion version, bool binary, bool optionalEdges, string scenario)
    {
        var tags = MeshReadTags(version);
        if (optionalEdges)
        {
            int start = tags.FindIndex(t => t.Code == 94), end = tags.FindIndex(t => t.Code == 1001) - 1;
            tags.RemoveRange(start, end - start);
        }
        int after = tags.FindIndex(t => t.Code == 1001);
        var lookalikes = new DxfTag[] { new(71, (short)3), new(72, (short)0), new(91, 7), new(92, 0), new(93, 0), new(94, 0), new(95, 0), new(90, 1), new(10, 99.0), new(20, 98.0), new(30, 97.0), new(140, 9.0) };
        switch (scenario)
        {
            case "private":
            case "nested":
            case "private-subclass":
                var payload = new List<DxfTag> { new(102, "{PRIVATE_MESH_FRAMING") };
                if (scenario == "nested") payload.Add(new(102, "{INNER"));
                if (scenario == "private-subclass") payload.Add(new(100, "PrivateMeshSubclass"));
                payload.AddRange(lookalikes);
                if (scenario == "nested") payload.Add(new(102, "}"));
                payload.Add(new(102, "}"));
                tags.InsertRange(after, payload); break;
            case "later-subclass": tags.InsertRange(after, new DxfTag[] { new(100, "FutureMeshSubclass") }.Concat(lookalikes)); break;
            case "xdata":
                // Existing non-XData trailer tolerance is a scoping control,
                // not a claim that these tails are valid or preserved.
                int following = tags.FindIndex(t => t.Code == 0 && (string)t.Value == "LINE");
                tags.InsertRange(following, new DxfTag[] { new(100, "AcDbSubDMesh") }.Concat(lookalikes)); break;
            case "comments": if (!binary) tags.Insert(after, new DxfTag(999, "91 7 outside a real field")); break;
        }
        MeshFramingSave(tags, binary, $"scoped-{version}-{binary}-{optionalEdges}-{scenario}", !optionalEdges);
    }

    private static void MeshFramingUnique(DxfVersion version, bool binary, string scenario)
    {
        var tags = MeshReadTags(version);
        short code = scenario switch { "late-version" => 71, "late-blend" => 72, "late-subdivision" => 91, "late-vertices" => 92, "late-faces" => 93, "late-edges-creases" => 94, "late-creases" => 95, _ => 90 };
        var span = MeshFramingPacket(tags, code);
        if (scenario == "late-edges-creases") span.Count = tags.FindIndex(t => t.Code == 1001) - 1 - span.Start;
        var packet = tags.GetRange(span.Start, span.Count);
        tags.RemoveRange(span.Start, span.Count);
        int destination = scenario == "early-zero" ? tags.FindIndex(t => t.Code == 71) : tags.FindIndex(t => t.Code == 1001);
        tags.InsertRange(destination, packet);
        MeshFramingSave(tags, binary, $"unique-{version}-{binary}-{scenario}", true);
    }

    private static void MeshFramingSave(List<DxfTag> tags, bool binary, string name, bool hasEdges)
    {
        using var input = new MemoryStream(RawFixtureBytes(tags, binary));
        var document = DxfDocument.Load(input) ?? throw new Exception("A unique/scoped MESH field packet was rejected.");
        MeshOverrideCheckGeometry(document, hasEdges);
        using var output = new MemoryStream(); Check(document.Save(output, binary), "MESH framing control did not save.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"mesh-framing-{name}.dxf"), output.ToArray());
        output.Position = 0;
        MeshOverrideCheckGeometry(DxfDocument.Load(output) ?? throw new Exception("MESH framing control did not reload."), hasEdges);
    }
}
