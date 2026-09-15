using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterHatchSourceRelationTests()
    {
        foreach (DxfVersion version in SupportedVersions) foreach (bool inputBinary in new[] { false, true }) foreach (string kind in new[] { "native", "producer" })
        {
            foreach (bool outputBinary in new[] { false, true })
                Run($"hatch-source/retained/{kind}/{version}/{inputBinary}/{outputBinary}", () => HatchSourceRetained(kind, version, inputBinary, outputBinary));
            Run($"hatch-source/normalized/{kind}/{version}/{inputBinary}", () => HatchSourceNormalized(kind, version, inputBinary));
            foreach (string defect in new[] { "missing", "zero", "wrong-owner", "self", "unsupported-proxy", "unsupported-aggregate", "missing-identity", "private-identity", "nonassociative", "short-count", "long-count", "duplicate-physical", "duplicate-common-equal", "duplicate-common-different" })
                Run($"hatch-source/reject/{kind}/{version}/{inputBinary}/{defect}", () => HatchSourceReject(kind, version, inputBinary, defect));
        }
        foreach (bool binary in new[] { false, true }) foreach (int placement in Enumerable.Range(0, 3))
            Run($"hatch-source/shared/{binary}/{placement}", () => HatchSourceShared(binary, placement));
        foreach (int scenario in Enumerable.Range(0, 15))
            Run($"hatch-source/api/{scenario}", () => HatchSourceApi(scenario));
    }
    private static byte[] HatchSourceInput(string kind, DxfVersion version, bool binary)
    {
        string name = $"{kind}-R{version.ToString()[7..]}-{(binary ? "binary" : "ascii")}.dxf";
        string directory = "tests/fixtures/hatch-source-relations"; byte[] input = File.ReadAllBytes(Path.Combine(directory, name));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        var item = manifest.RootElement.GetProperty("fixtures").EnumerateArray().Single(f => f.GetProperty("file").GetString() == name);
        Equal(item.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant(), "Pinned HATCH producer/native fixture");
        Equal(binary, input.AsSpan().StartsWith("AutoCAD Binary DXF"u8), "Actual HATCH source input transport"); return input;
    }
    private static DxfDocument HatchSourceLoad(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return DxfDocument.Load(stream) ?? throw new Exception("HATCH source load returned null"); }
    private static byte[] HatchSourceSave(DxfDocument doc, bool binary, string? artifact = null)
    {
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "HATCH source save"); byte[] bytes = stream.ToArray();
        Equal(binary, bytes.AsSpan().StartsWith("AutoCAD Binary DXF"u8), "Actual HATCH source output transport");
        if (artifact != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, artifact), bytes); return bytes;
    }
    private static DxfRawDocument HatchSourceRaw(byte[] bytes)
    { using var stream = new MemoryStream(bytes); return DxfRawDocument.Load(stream); }
    private static byte[] HatchSourceRawBytes(DxfRawDocument raw, bool binary)
    { using var stream = new MemoryStream(); raw.Save(stream, binary); return stream.ToArray(); }
    private static string HatchSourceSnapshot(DxfDocument doc) => string.Join("|", doc.Entities.Hatches.Select(h => h.Handle + ":" + h.Associative + ":" + string.Join("/", h.BoundaryPaths.Select(p => (int)p.PathType + ":" + p.Edges.Count + ":" + string.Join(",", p.Entities.Select(e => e.Handle))))));
    private static void HatchSourceRetained(string kind, DxfVersion version, bool inputBinary, bool outputBinary)
    {
        var doc = HatchSourceLoad(HatchSourceInput(kind, version, inputBinary)); string expected = HatchSourceSnapshot(doc);
        Equal(kind == "native" ? 1 : 10, doc.Entities.Hatches.SelectMany(h => h.BoundaryPaths).Sum(p => p.Entities.Count), "Source counts and duplicates");
        foreach (var hatch in doc.Entities.Hatches) foreach (var source in hatch.BoundaryPaths.SelectMany(p => p.Entities))
        {
            Check(ReferenceEquals(source.Owner, hatch.Owner) && ReferenceEquals(doc.GetObjectByHandle(source.Handle), source), "Source identity and containing block");
            Check(!doc.Entities.Remove(source), "Live HATCH source removal must be refused");
        }
        for (int cycle = 0; cycle < 3; cycle++)
        {
            doc = HatchSourceLoad(HatchSourceSave(doc, cycle == 1 ? !outputBinary : outputBinary,
                cycle == 2 ? $"hatch-source-{kind}-{version}-{inputBinary}-{outputBinary}.dxf" : null));
            Equal(expected, HatchSourceSnapshot(doc), "Stored source order/count/identity and associativity"); Equal(0, doc.Objects.Validate().Count, "HATCH source graph validation");
        }
    }
    private static DxfRawRecord HatchSourceRecord(DxfRawDocument raw, string handle) => raw.Sections.SelectMany(s => s.Records).Single(r => r.Tags.Any(t => t.Code == 5 && Equals(t.Value, handle)));
    private static void HatchSourceNormalized(string kind, DxfVersion version, bool binary)
    {
        var input = HatchSourceInput(kind, version, binary); var raw = HatchSourceRaw(input);
        foreach (var record in raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray())
        {
            var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbHatch"));
            for (int i = start; i < tags.Count; i++) if (tags[i].Code == 330) tags[i] = new DxfTag(330, "000" + ((string)tags[i].Value).ToLowerInvariant());
            var current = HatchSourceRecord(raw, (string)record.Tags.First(t => t.Code == 5).Value); raw = raw.WithRecord(current, tags);
        }
        Equal(HatchSourceSnapshot(HatchSourceLoad(input)), HatchSourceSnapshot(HatchSourceLoad(HatchSourceRawBytes(raw, binary))), "Numeric source identity ignores spelling");
        var targetHandle = kind == "native" ? "8E" : "3A0";
        var source = HatchSourceRecord(raw, targetHandle); var sourceTags = source.Tags.ToList();
        sourceTags.InsertRange(sourceTags.FindIndex(t => t.Code == 100), new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(102, "{NESTED"), new DxfTag(102, "}"), new DxfTag(5, "FFFF"), new DxfTag(102, "}") });
        raw = raw.WithRecord(source, sourceTags);
        Equal(HatchSourceSnapshot(HatchSourceLoad(input)), HatchSourceSnapshot(HatchSourceLoad(HatchSourceRawBytes(raw, binary))), "Nested private handle does not replace physical source identity");
    }
    private static void HatchSourceReject(string kind, DxfVersion version, bool binary, string defect)
    {
        var raw = HatchSourceRaw(HatchSourceInput(kind, version, binary)); var hatch = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "HATCH"); var tags = hatch.Tags.ToList();
        int start = tags.FindIndex(t => t.Code == 100 && Equals(t.Value, "AcDbHatch")); int reference = tags.FindIndex(start, t => t.Code == 330); string target = (string)tags[reference].Value;
        if (defect.StartsWith("duplicate-", StringComparison.Ordinal))
        {
            var source = HatchSourceRecord(raw, target); var sourceTags = source.Tags.ToList();
            if (defect == "duplicate-physical")
                raw = DxfRawDocument.Create(raw.Tags.Take(source.StartTagIndex).Concat(new[] { new DxfTag(0, "FUTURE_BOUNDARY_CURVE"), new DxfTag(5, target), new DxfTag(330, ((EntityObject)HatchSourceLoad(HatchSourceInput(kind, version, binary)).GetObjectByHandle(target)).Owner.Record.Handle), new DxfTag(100, "AcDbEntity"), new DxfTag(8, "0"), new DxfTag(100, "AcDbFutureBoundaryCurve") }).Concat(raw.Tags.Skip(source.StartTagIndex)));
            else
            {
                sourceTags.Insert(sourceTags.FindIndex(t => t.Code == 5), new DxfTag(5, defect == "duplicate-common-equal" ? target : "FFFF"));
                raw = raw.WithRecord(source, sourceTags);
            }
        }
        else if (defect is "unsupported-proxy" or "unsupported-aggregate" or "missing-identity" or "private-identity")
        {
            var source = HatchSourceRecord(raw, target); var sourceTags = source.Tags.ToList();
            if (defect == "unsupported-proxy") sourceTags[0] = new DxfTag(0, "ACAD_PROXY_ENTITY");
            else if (defect == "unsupported-aggregate") { sourceTags[0] = new DxfTag(0, "FUTURE_BOUNDARY_CURVE"); int xdata = sourceTags.FindIndex(t => t.Code == 1001); sourceTags.Insert(xdata < 0 ? sourceTags.Count : xdata, new DxfTag(66, (short)1)); }
            else
            {
                sourceTags.RemoveAt(sourceTags.FindIndex(t => t.Code == 5));
                if (defect == "private-identity") sourceTags.InsertRange(1, new[] { new DxfTag(102, "{PRIVATE"), new DxfTag(5, target), new DxfTag(102, "}") });
            }
            raw = raw.WithRecord(source, sourceTags);
        }
        else
        {
            switch (defect)
            {
                case "missing": tags[reference] = new DxfTag(330, "FFFF"); break;
                case "zero": tags[reference] = new DxfTag(330, "000"); break;
                case "wrong-owner":
                    var foreign = raw.Sections.Single(s => s.Name == "BLOCKS").Records.First(r => r.Name == "LINE");
                    tags[reference] = new DxfTag(330, foreign.Tags.First(t => t.Code == 5).Value); break;
                case "self": tags[reference] = new DxfTag(330, tags.First(t => t.Code == 5).Value); break;
                case "nonassociative": int associative = tags.FindIndex(start, t => t.Code == 71); tags[associative] = new DxfTag(71, (short)0); break;
                case "short-count": case "long-count":
                    int count = tags.FindIndex(start, t => t.Code == 97); tags[count] = new DxfTag(97, defect == "short-count" ? 0 : (int)tags[count].Value + 1); break;
            }
            raw = raw.WithRecord(hatch, tags);
        }
        byte[] bytes = HatchSourceRawBytes(raw, binary); using var stream = new MemoryStream(bytes);
#if DEBUG
        bool rejected = false;
        try { DxfDocument.Load(stream); }
        catch (InvalidDataException error) { rejected = error.Message.Contains("HATCH") || ((defect is "missing-identity" or "private-identity") && error.Message.Contains("source handle identity")); }
        catch (NotSupportedException error)
        {
            rejected = defect == "unsupported-proxy" && error.Message == "Unsupported standalone, aggregate or proxy entity: ACAD_PROXY_ENTITY"
                || defect == "unsupported-aggregate" && error.Message == "Unknown aggregate or embedded entity framing is unsupported.";
        }
        catch (FormatException error)
        {
            // The common entity reader can reject these identities before HATCH
            // reference binding. Other defects still require the HATCH-specific path.
            rejected = (defect is "missing-identity" or "private-identity" or "duplicate-common-equal" or "duplicate-common-different")
                && error.Message.StartsWith("A retained DXF entity ", StringComparison.Ordinal)
                && error.Message.Contains("common handle");
            if (defect == "duplicate-physical") rejected = error.Message == "A retained DXF object has an ambiguous physical source identity: " + target;
        }
        Check(rejected, "Unretained or unsupported HATCH source did not reject contextually: " + defect);
#else
        Check(DxfDocument.Load(stream) == null, "Release HATCH source rejection returns null: " + defect);
#endif
    }
    private static void HatchSourceShared(bool binary, int placement)
    {
        var doc = new DxfDocument(); Block block = doc.Blocks["*Model_Space"];
        if (placement == 1) { var layout = new netDxf.Objects.Layout("HatchPaper"); doc.Layouts.Add(layout); block = layout.AssociatedBlock; }
        if (placement == 2) { block = new Block("HatchNested"); doc.Blocks.Add(block); }
        var source = new Circle(Vector2.Zero, 3); block.Entities.Add(source);
        var first = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { source, source }), new HatchBoundaryPath(new EntityObject[] { source }) }, true);
        var second = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, true);
        block.Entities.Add(first); block.Entities.Add(second); Equal(4, source.Reactors.Count, "Shared sources count each list occurrence");
        var clone = (Hatch)first.Clone(); Check(!clone.Associative && clone.BoundaryPaths.All(p => p.Entities.Count == 0) && source.Reactors.Count == 4, "Clone detaches association without changing source");
        first.BoundaryPaths.Remove(first.BoundaryPaths[0]); Equal(2, source.Reactors.Count, "Removing one path releases precisely its duplicate uses"); Check(ReferenceEquals(source.Owner, block), "Source shared by remaining paths is retained");
        Check(block.Entities.Remove(second), "Second hatch can be removed"); Equal(1, source.Reactors.Count, "Removing second hatch releases its source use"); Check(!block.Entities.Remove(source), "Final source association prevents removal");
        HatchSourceLoad(HatchSourceSave(doc, binary));
        first.BoundaryPaths.Remove(first.BoundaryPaths[0]); Check(source.Owner == null && !block.Entities.Contains(source), "Last source path removal uses its owning block rather than active layout");
        Check(block.Entities.Remove(first), "Remove emptied hatch before export");
        HatchSourceLoad(HatchSourceSave(doc, binary));
    }
    private static void HatchSourceApi(int scenario)
    {
        var doc = new DxfDocument(); var block = new Block("SourceOwner"); doc.Blocks.Add(block); var foreign = new Circle(Vector2.Zero, 2); block.Entities.Add(foreign);
        var hatch = new Hatch(HatchPattern.Solid, true); doc.Entities.Add(hatch);
        if (scenario == 0)
        {
            int count = hatch.BoundaryPaths.Count, reactors = foreign.Reactors.Count; long seed = OwnershipSeed(doc);
            Throws<ArgumentException>(() => hatch.BoundaryPaths.Add(new HatchBoundaryPath(new EntityObject[] { foreign })));
            Check(hatch.BoundaryPaths.Count == count && foreign.Reactors.Count == reactors && seed == OwnershipSeed(doc), "Rejected wrong-block path mutated collection/reactors/handles");
        }
        else if (scenario == 1)
        {
            var incoming = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { foreign }) }, true); long seed = OwnershipSeed(doc); int count = doc.Entities.All.Count();
            Throws<ArgumentException>(() => doc.Entities.Add(incoming)); Check(incoming.Owner == null && incoming.Handle == null && count == doc.Entities.All.Count() && seed == OwnershipSeed(doc), "Rejected wrong-block hatch adoption mutated destination");
        }
        else if (scenario == 2)
        {
            var nested = new Hatch(HatchPattern.Solid, true); block.Entities.Add(nested); var circle = new Circle(Vector2.Zero, 4);
            nested.BoundaryPaths.Add(new HatchBoundaryPath(new EntityObject[] { circle })); Check(ReferenceEquals(circle.Owner, block), "Added nested-block source did not use actual owner"); Check(doc.Entities.Remove(hatch), "Remove unused empty control hatch"); HatchSourceLoad(HatchSourceSave(doc, false));
        }
        else if (scenario == 3)
        {
            var circle = new Circle(Vector2.Zero, 4); var path = new HatchBoundaryPath(new EntityObject[] { circle }); hatch.BoundaryPaths.Add(path);
            var another = new Hatch(HatchPattern.Solid, true); doc.Entities.Add(another); int reactors = circle.Reactors.Count;
            Throws<ArgumentException>(() => another.BoundaryPaths.Add(path)); Check(another.BoundaryPaths.Count == 0 && circle.Reactors.Count == reactors, "Shared path-instance rejection changed either hatch");
            hatch.UnLinkBoundary(); Check(path.Entities.Count == 0 && circle.Reactors.Count == 0 && circle.Owner != null, "Explicit unlink clears association but retains source");
        }
        else if (scenario == 4)
        {
            var circle = new Circle(Vector2.Zero, 4); var path = new HatchBoundaryPath(new EntityObject[] { circle }); hatch.BoundaryPaths.Add(path);
            Throws<ArgumentException>(() => hatch.BoundaryPaths.Add(path)); Equal(1, circle.Reactors.Count, "Repeated path rejection preserves the original source use");
            Equal(1, hatch.UnLinkBoundary().Count, "Unlink returns the unique attached path source"); Equal(0, circle.Reactors.Count, "Unlink releases the original source use");
            var detached = new HatchBoundaryPath(new EntityObject[] { circle });
            Throws<ArgumentException>(() => new Hatch(HatchPattern.Solid, new[] { detached, detached }, true)); Equal(0, circle.Reactors.Count, "Constructor repeated path rejection precedes mutations");
        }
        else if (scenario == 5)
        {
            int count = hatch.BoundaryPaths.Count; Throws<ArgumentException>(() => hatch.BoundaryPaths.Add(null!)); Equal(count, hatch.BoundaryPaths.Count, "Null path is not inserted");
        }
        else if (scenario == 6)
        {
            var path = new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Line { Start = Vector2.Zero, End = Vector2.UnitX } }); hatch.BoundaryPaths.Add(path);
            var loaded = HatchSourceLoad(HatchSourceSave(doc, true)); Check(loaded.Entities.Hatches.Single().Associative && loaded.Entities.Hatches.Single().BoundaryPaths.Single().Entities.Count == 0, "Associative path with zero sources remains supported");
        }
        else if (scenario is 7 or 8)
        {
            HatchBoundaryPath path;
            if (scenario == 7) path = new HatchBoundaryPath(new EntityObject[] { new Circle(Vector2.Zero, 3) });
            else path = new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Line { Start = Vector2.Zero, End = Vector2.UnitX } });
            hatch.BoundaryPaths.Add(path); int count = path.Entities.Count, reactors = path.Entities.Sum(e => e.Reactors.Count);
            Throws<ArgumentException>(() => new Hatch(HatchPattern.Solid, new[] { path }, scenario == 8));
            Check(path.Entities.Count == count && path.Entities.Sum(e => e.Reactors.Count) == reactors && hatch.BoundaryPaths.Contains(path), "Rejected constructor mutated already attached source path");
        }
        else if (scenario is 9 or 10 or 11)
        {
            var sourceDoc = DxfDocument.Load("tests/fixtures/polyline3d-records/producer-R2018-ascii.dxf");
            var retained = (Polyline3D)sourceDoc.GetObjectByHandle("3B"); Check(sourceDoc.Entities.Remove(retained), "Plain retained source can detach");
            var line = new Line(Vector2.Zero, Vector2.UnitX); var path = new HatchBoundaryPath(new EntityObject[] { line, retained });
            int count = doc.Entities.All.Count(), blocks = doc.Blocks.Count; long seed = OwnershipSeed(doc); int paths = hatch.BoundaryPaths.Count;
            if (scenario == 9)
            {
                var incoming = new Hatch(HatchPattern.Solid, new[] { path }, true);
                Throws<NotSupportedException>(() => doc.Entities.Add(incoming)); Check(incoming.Owner == null && incoming.Handle == null, "Rejected retained-source hatch acquired identity");
            }
            else if (scenario == 10) Throws<NotSupportedException>(() => hatch.BoundaryPaths.Add(path));
            else
            {
                var incoming = new Block("ForeignSourceContainer"); incoming.Entities.Add(new Hatch(HatchPattern.Solid, new[] { path }, true));
                Throws<NotSupportedException>(() => doc.Blocks.Add(incoming)); Check(incoming.Record.Owner == null && incoming.Handle == null, "Rejected retained-source block acquired identity");
            }
            Check(count == doc.Entities.All.Count() && blocks == doc.Blocks.Count && seed == OwnershipSeed(doc) && paths == hatch.BoundaryPaths.Count && line.Handle == null,
                "Rejected source adoption mutated destination membership/handles/paths");
        }
        else
        {
            doc = HatchSourceLoad(HatchSourceInput("producer", DxfVersion.AutoCad2018, false));
            hatch = (Hatch)doc.GetObjectByHandle("3A2"); var other = (Hatch)doc.GetObjectByHandle("3A3"); var sources = hatch.BoundaryPaths.SelectMany(p => p.Entities).Distinct().ToArray();
            if (scenario == 12) hatch.UnLinkBoundary();
            else if (scenario == 13) Check(doc.Entities.Remove(hatch), "Loaded hatch removal");
            else
            {
                hatch.BoundaryPaths.Remove(hatch.BoundaryPaths[0]);
                Check(sources.All(e => e.PersistentReactors.Contains(hatch)), "Loaded backlink must remain for surviving path uses");
                hatch.BoundaryPaths.Remove(hatch.BoundaryPaths[0]);
            }
            Check(sources.All(e => !e.PersistentReactors.Contains(hatch) && e.PersistentReactors.Contains(other) && e.Owner != null), "Loaded backlink cleanup must retain other hatch/source relationships");
            if (scenario == 14) Check(doc.Entities.Remove(hatch), "Remove emptied loaded hatch before export");
            HatchSourceLoad(HatchSourceSave(doc, false)); HatchSourceLoad(HatchSourceSave(doc, true));
        }
    }
}
