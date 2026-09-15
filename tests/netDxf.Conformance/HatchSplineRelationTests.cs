using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchSplineRelationTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
        {
            Run($"hatch-spline-relations/producer/{version}/{binary}", () => HatchRelationsProducer(version, binary));
            Run($"hatch-spline-relations/weights/{version}/{binary}", () => HatchRelationsWeights(version, binary));
            Run($"hatch-spline-relations/rational-sparse-weights/{version}/{binary}", () => HatchRelationsRationalSparseWeights(version, binary));
            foreach (string defect in new[] { "short-knots", "long-knots", "descending", "degree-controls", "empty-controls", "empty-knots", "zero-degree", "periodic-descending" })
                Run($"hatch-spline-relations/read/{version}/{binary}/{defect}", () => HatchRelationsMalformed(version, binary, defect));
            foreach (int placement in Enumerable.Range(0, 4))
                Run($"hatch-spline-relations/preflight/{version}/{binary}/{placement}", () => HatchRelationsPreflight(version, binary, placement, "short-knots"));
        }
        foreach (bool binary in new[] { false, true })
            foreach (string defect in new[] { "zero-degree", "negative-degree", "null-controls", "null-knots", "short-controls", "short-knots", "long-knots", "descending", "nan-knot", "infinite-knot", "nan-control", "infinite-weight" })
                Run($"hatch-spline-relations/api/{binary}/{defect}", () => HatchRelationsPreflight(DxfVersion.AutoCad2018, binary, 0, defect));
    }

    private static DxfRawDocument HatchRelationsRaw(DxfVersion version, bool binary)
    {
        string file = $"ezdxf-hatch-spline-R{version.ToString().Replace("AutoCad", "")}-{(binary ? "binary" : "ascii")}.dxf";
        string directory = Path.Combine("tests", "fixtures", "hatch-spline-relations"); byte[] bytes = File.ReadAllBytes(Path.Combine(directory, file));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
        string expected = manifest.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("file").GetString() == file).GetProperty("sha256").GetString()!;
        Equal(expected, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), "Pinned producer source");
        HatchRelationsAssertTransport(bytes, binary);
        return DxfRawDocument.Load(new MemoryStream(bytes));
    }
    private static void HatchRelationsAssertTransport(byte[] bytes, bool binary)
    {
        byte[] sentinel = System.Text.Encoding.ASCII.GetBytes("AutoCAD Binary DXF\r\n\u001a\0");
        Equal(binary, bytes.AsSpan().StartsWith(sentinel), "Actual input bytes use the named DXF transport");
    }
    private static DxfDocument HatchRelationsLoad(DxfRawDocument raw)
    { using var bytes = new MemoryStream(); raw.Save(bytes, raw.IsBinary); HatchRelationsAssertTransport(bytes.ToArray(), raw.IsBinary); bytes.Position = 0; return DxfDocument.Load(bytes) ?? throw new Exception("Producer HATCH load failed."); }
    private static DxfDocument HatchRelationsRoundTrip(DxfDocument doc, bool binary, string? output = null)
    {
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Spline relation export"); HatchRelationsAssertTransport(bytes.ToArray(), binary); if (output != null) File.WriteAllBytes(Path.Combine(ArtifactDirectory, output), bytes.ToArray()); bytes.Position = 0;
        return DxfDocument.Load(bytes) ?? throw new Exception("Spline relation reload failed.");
    }
    private static string HatchRelationName(Hatch hatch) => (string)hatch.XData["HATCH_SPLINE_REL"].XDataRecord.Single().Value;
    private static HatchBoundaryPath.Spline HatchRelationEdge(Hatch hatch) => hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
    private static void HatchRelationsEqual(HatchBoundaryPath.Spline expected, HatchBoundaryPath.Spline actual)
    {
        Equal(expected.Degree, actual.Degree, "Stored degree"); Equal(expected.IsRational, actual.IsRational, "Stored rational flag"); Equal(expected.IsPeriodic, actual.IsPeriodic, "Stored periodic flag");
        Equal(expected.Knots.Length, actual.Knots.Length, "Knot count"); Equal(expected.ControlPoints.Length, actual.ControlPoints.Length, "Control count");
        for (int i = 0; i < expected.Knots.Length; i++) SameDoubleBits(expected.Knots[i], actual.Knots[i], "Stored knot order/value");
        for (int i = 0; i < expected.ControlPoints.Length; i++)
        {
            SameDoubleBits(expected.ControlPoints[i].X, actual.ControlPoints[i].X, "Control X"); SameDoubleBits(expected.ControlPoints[i].Y, actual.ControlPoints[i].Y, "Control Y"); SameDoubleBits(expected.ControlPoints[i].Z, actual.ControlPoints[i].Z, "Stored weight, including omitted defaults");
        }
        Check(expected.FitPoints.SequenceEqual(actual.FitPoints), "Fit data independent of control metadata"); Equal(expected.StartTangent, actual.StartTangent, "Start tangent"); Equal(expected.EndTangent, actual.EndTangent, "End tangent");
    }
    private static void HatchRelationsProducer(DxfVersion version, bool binary)
    {
        var doc = HatchRelationsLoad(HatchRelationsRaw(version, binary));
        var expected = doc.Entities.Hatches.ToDictionary(HatchRelationName, hatch => (HatchBoundaryPath.Spline)HatchRelationEdge(hatch).Clone()); Equal(4, expected.Count, "Four independent producer spline packets");
        for (int cycle = 0; cycle < 3; cycle++)
        {
            doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-spline-relations-{version}-{binary}.dxf" : null);
            foreach (var hatch in doc.Entities.Hatches) { HatchRelationsEqual(expected[HatchRelationName(hatch)], HatchRelationEdge(hatch)); Equal(2, hatch.BoundaryPaths.Single().Edges.Count, "Following closing edge remains intact"); }
            Equal(1, doc.Entities.Lines.Count(), "Following LINE survives counted lists"); Equal(0, doc.Objects.Validate().Count, "No database damage");
        }
    }
    private static void HatchRelationsWeights(DxfVersion version, bool binary)
    {
        var raw = HatchRelationsRaw(version, binary); var record = raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "HATCH"); var tags = record.Tags.ToList(); int start = tags.FindIndex(t => t.Code == 94); int firstY = tags.FindIndex(start, t => t.Code == 20); tags.Insert(firstY + 1, new DxfTag(42, 2.5)); raw = raw.WithRecord(record, tags);
        var doc = HatchRelationsLoad(raw); var hatch = doc.Entities.Hatches.Single(h => HatchRelationName(h) == "QUADRATIC"); var edge = HatchRelationEdge(hatch); Check(!edge.IsRational, "Weight presence does not change rational flag"); SameDoubleBits(2.5, edge.ControlPoints[0].Z, "Accepted source nondefault weight");
        doc = HatchRelationsRoundTrip(doc, !binary); hatch = doc.Entities.Hatches.Single(h => HatchRelationName(h) == "QUADRATIC"); edge = HatchRelationEdge(hatch); SameDoubleBits(2.5, edge.ControlPoints[0].Z, "Accepted weight must survive output even with rational flag zero");
        edge.ControlPoints[0] = new Vector3(0, 0, 1); edge.ControlPoints[1] = new Vector3(5, 10, 2.5); edge.ControlPoints[2] = new Vector3(10, 0, -0.0);
        var expected = (HatchBoundaryPath.Spline)edge.Clone(); var copied = HatchRelationEdge((Hatch)hatch.Clone()); HatchRelationsEqual(expected, copied); copied.Knots[0] = -10; copied.ControlPoints[0] = new Vector3(0, 0, 9);
        SameDoubleBits(0, edge.Knots[0], "Clone knot storage is independent"); SameDoubleBits(1, edge.ControlPoints[0].Z, "Clone control storage is independent");
        doc = HatchRelationsRoundTrip(doc, binary, $"hatch-spline-relations-weights-{version}-{binary}.dxf"); HatchRelationsEqual(expected, HatchRelationEdge(doc.Entities.Hatches.Single(h => HatchRelationName(h) == "QUADRATIC")));
    }
    private static void HatchRelationsRationalSparseWeights(DxfVersion version, bool binary)
    {
        var raw = HatchRelationsRaw(version, binary);
        var record = raw.Sections.SelectMany(s => s.Records).Single(r => r.Name == "HATCH" && r.Tags.Any(t => t.Code == 1000 && (string)t.Value == "RATIONAL"));
        var tags = record.Tags.ToList();
        var weightIndexes = tags.Select((tag, index) => (tag, index)).Where(item => item.tag.Code == 42).Select(item => item.index).ToArray();
        Equal(4, weightIndexes.Length, "Independent rational producer supplies four indexed weights");
        // Omitted group 42 defaults to one at its own control; it must not shift
        // later weights or infer a different rational flag from their values.
        tags[weightIndexes[3]] = new DxfTag(42, -0.0);
        tags[weightIndexes[1]] = new DxfTag(42, -2.5);
        tags.RemoveAt(weightIndexes[2]); tags.RemoveAt(weightIndexes[0]);
        var doc = HatchRelationsLoad(raw.WithRecord(record, tags));
        var edge = HatchRelationEdge(doc.Entities.Hatches.Single(h => HatchRelationName(h) == "RATIONAL"));
        Check(edge.IsRational, "Sparse optional weights retain the explicit rational flag");
        double[] weights = { 1.0, -2.5, 1.0, -0.0 };
        for (int i = 0; i < weights.Length; i++) SameDoubleBits(weights[i], edge.ControlPoints[i].Z, "Sparse rational weight defaults remain indexed");
        var expected = doc.Entities.Hatches.ToDictionary(HatchRelationName, hatch => (HatchBoundaryPath.Spline)HatchRelationEdge(hatch).Clone());
        for (int cycle = 0; cycle < 3; cycle++)
        {
            doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-spline-relations-rational-weights-{version}-{binary}.dxf" : null);
            foreach (var hatch in doc.Entities.Hatches) HatchRelationsEqual(expected[HatchRelationName(hatch)], HatchRelationEdge(hatch));
            Equal(1, doc.Entities.Lines.Count(), "Following LINE survives sparse rational weights");
        }
    }
    private static void HatchRelationsMalformed(DxfVersion version, bool binary, string defect)
    {
        var raw = HatchRelationsRaw(version, binary); var records = raw.Sections.SelectMany(s => s.Records).Where(r => r.Name == "HATCH").ToArray(); var record = records[defect == "periodic-descending" ? 3 : 0]; var tags = record.Tags.ToList(); int begin = tags.FindIndex(t => t.Code == 94);
        int Index(short code) => tags.FindIndex(begin, t => t.Code == code);
        void Set(short code, object value) { int at = Index(code); tags[at] = new DxfTag(code, value); }
        switch (defect)
        {
            case "short-knots": Set(95, 5); tags.RemoveAt(Index(40)); break;
            case "long-knots": Set(95, 7); tags.Insert(Index(40), new DxfTag(40, 0.0)); break;
            case "descending": case "periodic-descending": tags[Index(40) + 1] = new DxfTag(40, 100.0); break;
            case "degree-controls": Set(94, 3); Set(95, 7); tags.Insert(Index(40), new DxfTag(40, 0.0)); break;
            case "empty-controls":
                Set(96, 0); int at = Index(10); int end = tags.FindIndex(at, t => t.Code == 97 || t.Code == 72); tags.RemoveRange(at, end - at); break;
            case "empty-knots": Set(95, 0); int knot = Index(40); tags.RemoveRange(knot, 6); break;
            case "zero-degree": Set(94, 0); break;
        }
        raw = raw.WithRecord(record, tags); using var bytes = new MemoryStream(); raw.Save(bytes, binary); HatchRelationsAssertTransport(bytes.ToArray(), binary); bytes.Position = 0;
#if DEBUG
        try { DxfDocument.Load(bytes); throw new Exception("Malformed HATCH spline relation accepted: " + defect); }
        catch (InvalidDataException error) { Check(error.Message.Contains("HATCH") && error.Message.Contains("group code"), "Contextual spline rejection"); }
#else
        Check(DxfDocument.Load(bytes) == null, "Release malformed spline input must return null");
#endif
    }
    private static void HatchRelationsPreflight(DxfVersion version, bool binary, int placement, string defect)
    {
        var edge = new HatchBoundaryPath.Spline { Degree = 2, Knots = new double[] { 0, 0, 0, 1, 1, 1 }, ControlPoints = new[] { new Vector3(0, 0, 1), new Vector3(5, 10, 1), new Vector3(10, 0, 1) } };
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { edge, new HatchBoundaryPath.Line { Start = new Vector2(10, 0), End = Vector2.Zero } }) }, false); var doc = new DxfDocument(version);
        switch (placement)
        {
            case 0: doc.Entities.Add(hatch); break;
            case 1: doc.Layouts.Add(new Layout("SplinePaper")); doc.Entities.ActiveLayout = "SplinePaper"; doc.Entities.Add(hatch); doc.Entities.ActiveLayout = "Model"; break;
            case 2: var inner = new Block("SplineInner"); inner.Entities.Add(hatch); var outer = new Block("SplineOuter"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default: var unused = new Block("SplineUnused"); unused.Entities.Add(hatch); doc.Blocks.Add(unused); break;
        }
        switch (defect)
        {
            case "zero-degree": edge.Degree = 0; break;
            case "negative-degree": edge.Degree = -1; break;
            case "null-controls": edge.ControlPoints = null!; break;
            case "null-knots": edge.Knots = null!; break;
            case "short-controls": edge.ControlPoints = edge.ControlPoints.Take(2).ToArray(); break;
            case "short-knots": edge.Knots = edge.Knots.Take(5).ToArray(); break;
            case "long-knots": edge.Knots = edge.Knots.Concat(new[] { 1.0 }).ToArray(); break;
            case "descending": edge.Knots[1] = 2; break;
            case "nan-knot": edge.Knots[1] = double.NaN; break;
            case "infinite-knot": edge.Knots[1] = double.PositiveInfinity; break;
            case "nan-control": edge.ControlPoints[1] = new Vector3(double.NaN, 10, 1); break;
            case "infinite-weight": edge.ControlPoints[1] = new Vector3(5, 10, double.NegativeInfinity); break;
        }
        long seed = OwnershipSeed(doc); string? handle = hatch.Handle; int apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count, blocks = doc.Blocks.Count;
        var databaseField = typeof(DxfDocument).GetField("objectDatabase", BindingFlags.Instance | BindingFlags.NonPublic)!; object? database = databaseField.GetValue(doc);
        using var bytes = new MemoryStream(); byte[] original = { 10, 20, 30, 40 }; bytes.Write(original); bytes.Position = 2;
#if DEBUG
        Throws<ArgumentException>(() => doc.Save(bytes, binary));
#else
        Check(!doc.Save(bytes, binary), "Release invalid spline save returns false");
#endif
        Check(original.SequenceEqual(bytes.ToArray()) && bytes.Position == 2, "Invalid spline preflight preserves destination bytes and position"); Equal(seed, OwnershipSeed(doc), "Invalid spline preflight allocates no handles"); Equal(handle, hatch.Handle, "Invalid spline preflight preserves entity identity"); Equal(apps, doc.ApplicationRegistries.Count, "No APPID registration"); Equal(layouts, doc.Layouts.Count, "No layout mutation"); Equal(blocks, doc.Blocks.Count, "No block mutation"); Check(ReferenceEquals(database, databaseField.GetValue(doc)), "No lazy OBJECTS creation");
        edge.Degree = 2; edge.Knots = new double[] { 0, 0, 0, 1, 1, 1 }; edge.ControlPoints = new[] { new Vector3(0, 0, 1), new Vector3(5, 10, 1), new Vector3(10, 0, 1) };
        using var repaired = new MemoryStream(); Check(doc.Save(repaired, binary), "Explicitly repaired spline can save after rejection");
    }
}
