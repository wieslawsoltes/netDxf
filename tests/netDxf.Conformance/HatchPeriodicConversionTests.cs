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
        foreach (double weight in new[] { double.Epsilon, 1e-310 }) foreach (bool extreme in new[] { false, true })
            Run($"hatch-periodic/local-weight-scale/{weight:R}/{extreme}", () => HatchPeriodicLocalWeightScale(weight, extreme));
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"hatch-periodic/reverse-clone/{version}/{binary}", () => HatchPeriodicReverseClone(version, binary));
        foreach (int degree in new[] { 1, 2, 3, 5, 10 }) foreach (double coordinate in new[] { double.Epsilon, 1e-320, 1e-100, 1e100, double.MaxValue }) foreach (bool mixed in new[] { false, true })
            Run($"hatch-periodic/constant-coordinate/{degree}/{coordinate:R}/{mixed}", () => HatchPeriodicConstantCoordinate(degree, coordinate, mixed));
        foreach (int degree in new[] { 1, 2, 3, 5, 10 }) foreach (double scale in new[] { double.Epsilon, 1e-320 }) foreach (bool mixed in new[] { false, true })
            Run($"hatch-periodic/subnormal-shape/{degree}/{scale:R}/{mixed}", () => HatchPeriodicSubnormalShape(degree, scale, mixed));
        foreach (double magnitude in new[] { 1e100, 1e200, 1e308 }) foreach (double middle in new[] { 1.0, 1e-100, 1e100 })
            Run($"hatch-periodic/cancellation/{magnitude:R}/{middle:R}", () => HatchPeriodicCancellation(magnitude, middle));
        foreach (var pair in new[] { (small: 1e-310, large: 1e13), (small: 5e-300, large: 3e22), (small: 3e-307, large: 7e14) })
            Run($"hatch-periodic/weight-compensation/{pair.small:R}/{pair.large:R}", () => HatchPeriodicWeightCompensation(pair.small, pair.large));
        for (int index = 0; index < 9; index++)
        {
            int captured = index;
            Run($"hatch-periodic/weighted-cancellation/{index}", () => HatchPeriodicWeightedCancellation(captured));
        }
        foreach (double offset in new[] { -1e-15, -1e-14 })
            Run($"hatch-periodic/subnormal-basis/{offset:R}", () => HatchPeriodicSubnormalBasis(offset));
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

    private static void HatchPeriodicLocalWeightScale(double weight, bool extreme)
    {
        var points = Enumerable.Range(0, 6).Select(i => new Vector3(2 * i + 1, 3 * i - 1, 0)).ToArray();
        var weights = Enumerable.Repeat(weight, points.Length).ToArray(); weights[3] = 1;
        if (extreme)
        {
            Array.Fill(points, Vector3.Zero); points[0] = new Vector3(1e308, -1e308, 0);
            Array.Fill(weights, 1); weights[0] = weight;
        }
        var source = new Spline(points, weights, (short)1, true);
        var converted = (Spline)new HatchBoundaryPath.Spline(source).ConvertTo();
        var samples = converted.PolygonalVertexes(12);
        // The first interval only sees the final and first controls. A remote
        // large weight has no support here; the curve is their exact midpoint.
        if (extreme)
        {
            double expected = weight * 1e308 / (1 + weight);
            Check(Math.Abs(samples[1].X - expected) <= expected * 1e-12 && Math.Abs(samples[1].Y + expected) <= expected * 1e-12,
                "Subnormal basis coefficient retains its representable coordinate contribution");
        }
        else HatchAffineNear((points[^1] + points[0]) * .5, samples[1], "Local subnormal weights retain their relative scale");
        HatchAffineNear(points[^1], samples[0], "Local subnormal endpoint remains finite");
        Check(source.Weights.SequenceEqual(converted.Weights), "Evaluation leaves mixed-scale stored weights unchanged");
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"hatch-periodic-local-weight-{weight:R}-{extreme}.json"),
            System.Text.Json.JsonSerializer.Serialize(new { weight, extreme, controls = points.Select(p => new[] { p.X, p.Y, p.Z }),
                weights, knots = source.Knots, samples = samples.Select(p => new[] { p.X, p.Y, p.Z }) }));
    }

    private static void HatchPeriodicReverseClone(DxfVersion version, bool binary)
    {
        string year = version.ToString().Replace("AutoCad", "");
        var document = DxfDocument.Load(Path.Combine("tests", "fixtures", "hatch-periodic-conversion", $"ezdxf-hatch-periodic-R{year}-{(binary ? "binary" : "ascii")}.dxf"))!;
        foreach (var hatch in document.Entities.Hatches.ToArray())
        {
            var sourceEdge = hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();
            var source = (Spline)sourceEdge.ConvertTo(); var clone = (Spline)source.Clone();
            var before = source.PolygonalVertexes(64); clone.Reverse();
            var reversedEdge = new HatchBoundaryPath.Spline(clone); var reverse = (Spline)reversedEdge.ConvertTo();
            var samples = reverse.PolygonalVertexes(64);
            for (int i = 0; i < samples.Count; i++) HatchAffineNear(before[(64 - i) % 64], samples[i], "Periodic reversed active-domain samples");
            Check(!ReferenceEquals(source.ControlPoints, clone.ControlPoints) && !ReferenceEquals(source.Knots, clone.Knots) && !ReferenceEquals(source.Weights, clone.Weights), "Clone isolates periodic arrays");
            clone.Reverse();
            Check(source.ControlPoints.SequenceEqual(clone.ControlPoints) && source.Weights.SequenceEqual(clone.Weights) && source.Knots.SequenceEqual(clone.Knots), "Double reversal restores periodic packet");
            Equal(source.StartTangent, clone.StartTangent, "Double reversal restores start tangent"); Equal(source.EndTangent, clone.EndTangent, "Double reversal restores end tangent");
            var output = new DxfDocument(version); output.Entities.Add(reverse);
            output = HatchRelationsRoundTrip(output, binary);
            var reloaded = output.Entities.Splines.Single().PolygonalVertexes(64);
            for (int i = 0; i < reloaded.Count; i++) HatchAffineNear(samples[i], reloaded[i], "Reversed periodic export/reload samples");
        }
    }

    private static void HatchPeriodicConstantCoordinate(int degree, double coordinate, bool mixed)
    {
        var controls = Enumerable.Repeat(new Vector3(coordinate, -coordinate, 0), degree + 3).ToArray();
        var weights = Enumerable.Repeat(1.0, controls.Length).ToArray();
        if (mixed) for (int i = 0; i < weights.Length - 1; i++) weights[i] = 1e-310;
        var source = new Spline(controls, weights, (short)degree, true);
        var converted = (Spline)new HatchBoundaryPath.Spline(source).ConvertTo();
        var samples = converted.PolygonalVertexes(17);
        foreach (var sample in samples)
        {
            SameDoubleBits(coordinate, sample.X, "A constant rational curve retains its positive coordinate exactly");
            SameDoubleBits(-coordinate, sample.Y, "A constant rational curve retains its negative coordinate exactly");
            Equal(0.0, sample.Z, "A constant zero coordinate remains zero");
        }
        var reverse = (Spline)converted.Clone(); reverse.Reverse();
        foreach (var sample in reverse.PolygonalVertexes(17))
        {
            SameDoubleBits(coordinate, sample.X, "Reversal retains the constant positive coordinate");
            SameDoubleBits(-coordinate, sample.Y, "Reversal retains the constant negative coordinate");
        }
        Check(source.ControlPoints.SequenceEqual(converted.ControlPoints) && source.Weights.SequenceEqual(converted.Weights) && source.Knots.SequenceEqual(converted.Knots), "Constant evaluation leaves the packet unchanged");
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"hatch-periodic-constant-{degree}-{coordinate:R}-{mixed}.json"),
            System.Text.Json.JsonSerializer.Serialize(new { degree, coordinate, mixed, samples = samples.Select(p => new[] { p.X, p.Y, p.Z }) }));
    }

    private static void HatchPeriodicNumericOutput(Spline source, string name, int count)
    {
        File.WriteAllText(Path.Combine(ArtifactDirectory, $"hatch-periodic-numeric-{name}.json"),
            System.Text.Json.JsonSerializer.Serialize(new { degree = source.Degree,
                controls = source.ControlPoints.Select(p => new[] { p.X, p.Y, p.Z }), weights = source.Weights, knots = source.Knots,
                samples = source.PolygonalVertexes(count).Select(p => new[] { p.X, p.Y, p.Z }) }));
    }

    private static void HatchPeriodicSubnormalShape(int degree, double scale, bool mixed)
    {
        var controls = Enumerable.Range(0, degree + 3).Select(i => new Vector3(i * 3 - 5, i % 4 * 2 - 3, 0)).ToArray();
        var weights = Enumerable.Repeat(1.0, controls.Length).ToArray();
        if (mixed) for (int i = 0; i < weights.Length - 1; i++) weights[i] = 1e-310;
        var unit = new Spline(controls, weights, (short)degree, true);
        var source = new Spline(controls.Select(p => p * scale), weights, (short)degree, true);
        var actual = source.PolygonalVertexes(17); var expected = unit.PolygonalVertexes(17);
        for (int i = 0; i < actual.Count; i++)
        {
            Check(Math.Abs(actual[i].X - expected[i].X * scale) <= 4 * double.Epsilon, "Nonconstant subnormal X respects curve scaling");
            Check(Math.Abs(actual[i].Y - expected[i].Y * scale) <= 4 * double.Epsilon, "Nonconstant subnormal Y respects curve scaling");
        }
        HatchPeriodicNumericOutput(source, $"subnormal-{degree}-{scale:R}-{mixed}", 17);
    }

    private static void HatchPeriodicCancellation(double magnitude, double middle)
    {
        var controls = new[] { new Vector3(-magnitude, 0, 0), Vector3.Zero, Vector3.Zero, new Vector3(magnitude, 0, 0), new Vector3(middle, 0, 0) };
        var source = new Spline(controls, Enumerable.Repeat(1.0, 5), Enumerable.Range(0, 10).Select(i => (double)i), (short)2, true);
        double actual = source.PolygonalVertexes(10)[1].X, expected = .75 * middle;
        Check(Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-12, "Opposite large contributions retain the smaller middle contribution");
        HatchPeriodicNumericOutput(source, $"cancellation-{magnitude:R}-{middle:R}", 10);
    }

    private static void HatchPeriodicWeightCompensation(double small, double large)
    {
        var source = new Spline(new[] { new Vector3(1e308, -1e308, 0), Vector3.Zero, Vector3.Zero, Vector3.Zero },
            new[] { small, large, large, large }, Enumerable.Range(0, 7).Select(i => (double)i), (short)1, true);
        double expected = small * 1e308 / large;
        var actual = source.PolygonalVertexes(8)[1];
        Check(Math.Abs(actual.X - expected) <= expected * 1e-12 && Math.Abs(actual.Y + expected) <= expected * 1e-12,
            "Weight division does not quantize a later representable coordinate contribution");
        HatchPeriodicNumericOutput(source, $"weight-compensation-{small:R}-{large:R}", 8);
    }

    private static void HatchPeriodicWeightedCancellation(int index)
    {
        using var fixture = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine("tests", "fixtures", "hatch-periodic-conversion", "weighted-cancellation.json")));
        var item = fixture.RootElement.GetProperty("cases")[index];
        var controls = item.GetProperty("controls").EnumerateArray().Select(p => new Vector3(p[0].GetDouble(), p[1].GetDouble(), p[2].GetDouble())).ToArray();
        var source = new Spline(controls, item.GetProperty("weights").EnumerateArray().Select(v => v.GetDouble()),
            item.GetProperty("knots").EnumerateArray().Select(v => v.GetDouble()), (short)item.GetProperty("degree").GetInt32(), true);
        int count = item.GetProperty("precision").GetInt32(); var samples = source.PolygonalVertexes(count);
        var expected = item.GetProperty("expected");
        for (int i = 0; i < count; i++)
        {
            double value = expected[i][0].GetDouble();
            Check(Math.Abs(samples[i].X - value) <= Math.Abs(value) * 2e-12 + 4 * double.Epsilon,
                "Weighted cancellation matches independent exact-rational input fixture");
        }
        HatchPeriodicNumericOutput(source, $"weighted-cancellation-{index}", count);
    }

    private static void HatchPeriodicSubnormalBasis(double offset)
    {
        var source = new Spline(new[] { Vector3.Zero, Vector3.Zero, new Vector3(1e308, 0, 0), Vector3.Zero },
            Enumerable.Repeat(1.0, 4), new[] { -1.2e308, -8e307, -4e307, offset, 4e307, 8e307, 1.2e308 }, (short)1, true);
        double expected = -offset * 2.5;
        double actual = source.PolygonalVertexes(4)[2].X;
        Check(Math.Abs(actual - expected) <= expected * 2e-12, "Positive subnormal basis retains a representable coordinate contribution");
        var reverse = (Spline)source.Clone(); reverse.Reverse();
        SameDoubleBits(-offset, reverse.Knots[3], "Opposite-sign active endpoints retain the reflected small knot");
        Check(Math.Abs(reverse.PolygonalVertexes(4)[2].X - expected) <= expected * 2e-12, "Reversal preserves the same exactly reflected parameter");
        reverse.Reverse();
        for (int i = 0; i < source.Knots.Length; i++) SameDoubleBits(source.Knots[i], reverse.Knots[i], "Double reversal preserves large-domain knots");
        HatchPeriodicNumericOutput(source, $"subnormal-basis-{offset:R}", 4);
    }
}
