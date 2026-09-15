using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] HatchPeriodicDefects = { "overlap-x", "overlap-y", "overlap-weight", "flag-weight", "zero-weight", "negative-weight", "count", "spans", "repeated", "domain", "empty", "degree", "weight-range" };
    private static void RegisterHatchPeriodicConversionTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true }) foreach (bool compact in new[] { false, true }) foreach (int plane in new[] { 0, 1 }) foreach (bool link in new[] { false, true })
            Run($"hatch-periodic/geometry/{version}/{binary}/{compact}/{plane}/{link}", () => HatchPeriodicGeometry(version, binary, compact, plane, link));
        foreach (bool associated in new[] { false, true }) foreach (bool link in new[] { false, true }) foreach (string defect in HatchPeriodicDefects)
            Run($"hatch-periodic/atomic/{associated}/{link}/{defect}", () => HatchPeriodicAtomic(associated, link, defect));
        foreach (bool binary in new[] { false, true }) foreach (string defect in HatchPeriodicDefects)
            Run($"hatch-periodic/storage/{binary}/{defect}", () => HatchPeriodicStorage(binary, defect));
        foreach (int degree in Enumerable.Range(1, 10)) foreach (bool compact in new[] { false, true }) foreach (bool coincident in new[] { false, true })
            Run($"hatch-periodic/degree/{degree}/{compact}/{coincident}", () => HatchPeriodicDegree(degree, compact, coincident));
        foreach (bool offset in new[] { false, true }) Run($"hatch-periodic/parameter-grid/{offset}", () => HatchPeriodicParameterGrid(offset));
        foreach (double weight in new[] { double.Epsilon, 1e-200, 1e200 }) foreach (bool coincident in new[] { false, true })
            Run($"hatch-periodic/weight-scale/{weight:R}/{coincident}", () => HatchPeriodicWeightScale(weight, coincident));
    }
    private static HatchBoundaryPath.Spline HatchPeriodicPacket()
    {
        var points = new[] { new Vector3(0, 0, 1), new Vector3(4, 7, 1), new Vector3(10, 1, 1), new Vector3(7, -5, 1), new Vector3(-3, -2, 1), new Vector3(0, 0, 1), new Vector3(4, 7, 1) };
        return new HatchBoundaryPath.Spline { Degree = 2, IsPeriodic = true, IsRational = true, ControlPoints = points, Knots = Enumerable.Range(0, 10).Select(i => i * .25 - 3).ToArray() };
    }
    private static HatchBoundaryPath.Spline HatchPeriodicInvalid(string defect)
    {
        var edge = HatchPeriodicPacket();
        switch (defect)
        {
            case "overlap-x": edge.ControlPoints[0].X += .125; break;
            case "overlap-y": edge.ControlPoints[0].Y += .125; break;
            case "overlap-weight": edge.ControlPoints[0].Z = 2; break;
            case "flag-weight": edge.IsRational = false; edge.ControlPoints[2].Z = 2; break;
            case "zero-weight": edge.ControlPoints[2].Z = 0; break;
            case "negative-weight": edge.ControlPoints[2].Z = -1; break;
            case "count": edge.Knots = edge.Knots.Concat(new[] { 1.0 }).ToArray(); break;
            case "spans": edge.Knots[0] -= .125; break;
            case "repeated": edge.Knots[1] = edge.Knots[0]; break;
            case "domain": edge.Knots = edge.Knots.Select(_ => 0.0).ToArray(); break;
            case "empty": edge.Knots = Array.Empty<double>(); edge.ControlPoints = Array.Empty<Vector3>(); break;
            case "degree": edge.Degree = 11; break;
            case "weight-range": edge.ControlPoints[2].Z = double.Epsilon; edge.ControlPoints[3].Z = 1e308; break;
        }
        return edge;
    }
    private static void HatchPeriodicGeometry(DxfVersion version, bool binary, bool compact, int plane, bool link)
    {
        string year = version.ToString().Replace("AutoCad", "");
        var doc = DxfDocument.Load(Path.Combine("tests", "fixtures", "hatch-periodic-conversion", $"ezdxf-hatch-periodic-R{year}-{(binary ? "binary" : "ascii")}.dxf")) ?? throw new Exception("Periodic fixture load failed");
        var snapshots = new Dictionary<string, HatchBoundaryPath.Spline>(); var expected = new Dictionary<string, Vector3[]>();
        foreach (var h in doc.Entities.Hatches.ToArray())
        {
            h.Normal = plane == 0 ? Vector3.UnitZ : new Vector3(1, 2, 3); h.Elevation = 4;
            var edge = h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single(); var raw = (HatchBoundaryPath.Spline)edge.Clone();
            if (compact) edge.ControlPoints = edge.ControlPoints.Skip(edge.Degree).ToArray();
            snapshots[h.Layer.Name] = (HatchBoundaryPath.Spline)edge.Clone();
            var converted = (Spline)edge.ConvertTo(); Equal(raw.ControlPoints.Length - edge.Degree, converted.ControlPoints.Length, "Compact entity count");
            Check(raw.Knots.SequenceEqual(converted.Knots), "Periodic knots retained exactly");
            var back = new HatchBoundaryPath.Spline(converted); Equal(raw.ControlPoints.Length, back.ControlPoints.Length, "Canonical expanded edge count");
            for (int i = 0; i < raw.ControlPoints.Length; i++) { SameDoubleBits(raw.ControlPoints[i].X, back.ControlPoints[i].X, "Expanded X"); SameDoubleBits(raw.ControlPoints[i].Y, back.ControlPoints[i].Y, "Expanded Y"); SameDoubleBits(raw.ControlPoints[i].Z, back.ControlPoints[i].Z, "Expanded weight"); }
            double first = converted.Knots[converted.Degree], last = converted.Knots[converted.Knots.Length - converted.Degree - 1];
            expected[h.Layer.Name] = Enumerable.Range(0, 64).Select(i => HatchAffineWorld(h, new Vector2(ReversalPoint(converted, first + (last - first) * i / 64.0).X, ReversalPoint(converted, first + (last - first) * i / 64.0).Y))).ToArray();
            var boundary = h.CreateBoundary(link); var spline = boundary.OfType<Spline>().Single(); spline.Layer = h.Layer; if (!link) doc.Entities.Add(spline);
            Equal(link, h.Associative, "Created boundary association"); HatchRelationsEqual(snapshots[h.Layer.Name], edge);
        }
        void Verify(DxfDocument drawing)
        {
            foreach (var h in drawing.Entities.Hatches) HatchRelationsEqual(snapshots[h.Layer.Name], h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
            foreach (var s in drawing.Entities.Splines)
            { var samples = s.PolygonalVertexes(64); Equal(64, samples.Count, "Periodic sample count"); for (int i = 0; i < samples.Count; i++) HatchAffineNear(expected[s.Layer.Name][i], samples[i], "Periodic world curve / active parameter domain"); }
        }
        Verify(doc); string stem = $"hatch-periodic-{version}-{binary}-{compact}-{plane}-{link}";
        for (int cycle = 0; cycle < 3; cycle++) { doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? stem + ".dxf" : null); Verify(doc); }
        if (compact)
        {
            // Preserve the compatibility HATCH packet separately; qualify its
            // converted canonical SPLINE entities in an independently valid drawing.
            var curves = new DxfDocument(version);
            foreach (var spline in doc.Entities.Splines) curves.Entities.Add((Spline)spline.Clone());
            curves.Entities.Add((Line)doc.Entities.Lines.Single().Clone());
            HatchRelationsRoundTrip(curves, binary, stem + "-curves.dxf");
        }
        var output = doc.Entities.Splines.ToDictionary(s => s.Layer.Name, s => s.PolygonalVertexes(64).Select(p => new[] { p.X, p.Y, p.Z }).ToArray());
        File.WriteAllText(Path.Combine(ArtifactDirectory, stem + ".json"), System.Text.Json.JsonSerializer.Serialize(output));
    }
    private static void HatchPeriodicAtomic(bool associated, bool link, string defect)
    {
        var line = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { line }) }, associated); var doc = new DxfDocument(); doc.Entities.Add(h);
        var edge = HatchPeriodicInvalid(defect); h.BoundaryPaths.Add(new HatchBoundaryPath(new[] { edge })); var snapshot = (HatchBoundaryPath.Spline)edge.Clone(); var paths = h.BoundaryPaths.ToArray(); var sources = paths[0].Entities.ToArray(); var reactors = line.Reactors.ToArray();
        long seed = OwnershipSeed(doc); int members = doc.Entities.All.Count(), events = 0; h.HatchBoundaryPathAdded += (_, _) => events++; h.HatchBoundaryPathRemoved += (_, _) => events++;
        bool rejected = false; try { h.CreateBoundary(link); } catch (ArgumentException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Unsupported periodic conversion rejected " + defect); Equal(associated, h.Associative, "Refused boundary association unchanged"); Check(paths.SequenceEqual(h.BoundaryPaths), "Refused path identity");
        Check(sources.SequenceEqual(paths[0].Entities) && reactors.SequenceEqual(line.Reactors), "Refused source occurrences/reactors"); Check(ReferenceEquals(edge, paths[1].Edges.Single()), "Refused edge identity"); HatchRelationsEqual(snapshot, edge);
        Equal(seed, OwnershipSeed(doc), "No handles allocated before refusal"); Equal(members, doc.Entities.All.Count(), "No partial boundary registration"); Equal(0, events, "No callbacks before refusal");
    }
    private static void HatchPeriodicStorage(bool binary, string defect)
    {
        var edge = HatchPeriodicInvalid(defect); var snapshot = (HatchBoundaryPath.Spline)edge.Clone(); var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false);
        var clone = (Hatch)h.Clone(); HatchRelationsEqual(snapshot, clone.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
        h.TransformBy(Matrix3.Identity, Vector3.Zero); HatchRelationsEqual(snapshot, edge); var doc = new DxfDocument(); doc.Entities.Add(h);
        for (int cycle = 0; cycle < 3; cycle++) { doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-periodic-storage-{binary}-{defect}.dxf" : null); HatchRelationsEqual(snapshot, doc.Entities.Hatches.Single().BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single()); }
    }
    private static void HatchPeriodicDegree(int degree, bool compact, bool coincident)
    {
        var controls = Enumerable.Range(0, degree + 3).Select(i => new Vector3(i * 2, i % 3 * 4, 0)).ToArray(); if (coincident) controls[^1] = controls[0];
        var source = new Spline(controls, Enumerable.Repeat(1.0, controls.Length), (short)degree, true); var edge = new HatchBoundaryPath.Spline(source); if (compact) edge.ControlPoints = edge.ControlPoints.Skip(degree).ToArray(); var result = (Spline)edge.ConvertTo();
        double first = source.Knots[degree], last = source.Knots[source.Knots.Length - degree - 1]; var samples = result.PolygonalVertexes(65);
        for (int i = 0; i < samples.Count; i++) HatchAffineNear(ReversalPoint(source, first + (last - first) * i / 65.0), samples[i], "High-degree/coincident periodic active domain");
        Check(source.Knots.SequenceEqual(result.Knots) && source.Weights.SequenceEqual(result.Weights) && source.ControlPoints.SequenceEqual(result.ControlPoints), "Adapter preserves compact source exactly");
    }
    private static void HatchPeriodicParameterGrid(bool offset)
    {
        var raw = HatchPeriodicPacket(); var controls = raw.ControlPoints.Skip(2).Select(p => new Vector3(p.X, p.Y, 0)).ToArray();
        var knots = Enumerable.Range(0, 10).Select(i => offset ? 1e16 + i * 2.0 : i * double.Epsilon).ToArray();
        var spline = new Spline(controls, Enumerable.Repeat(1.0, controls.Length), knots, (short)2, true); var snapshot = spline.Knots.ToArray();
        Throws<ArgumentException>(() => spline.PolygonalVertexes(64)); Check(snapshot.SequenceEqual(spline.Knots), "Unrepresentable sample request retains stored knots");
    }
    private static void HatchPeriodicWeightScale(double weight, bool coincident)
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(4, 7, 0), new Vector3(10, 1, 0), new Vector3(7, -5, 0), new Vector3(-3, -2, 0) }; if (coincident) points[^1] = points[0];
        var unit = new Spline(points, Enumerable.Repeat(1.0, points.Length), (short)2, true); var source = new Spline(points, Enumerable.Repeat(weight, points.Length), (short)2, true); var result = (Spline)new HatchBoundaryPath.Spline(source).ConvertTo();
        var expected = unit.PolygonalVertexes(65); var actual = result.PolygonalVertexes(65); for (int i = 0; i < actual.Count; i++) HatchAffineNear(expected[i], actual[i], "Temporary weight normalization");
        foreach (double actualWeight in result.Weights) SameDoubleBits(weight, actualWeight, "Stored weight remains unchanged by evaluation");
    }
}
