using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchExplicitPeriodicTests()
    {
        foreach (DxfVersion version in SupportedVersions)
        foreach (bool binary in new[] { false, true })
            Run($"hatch-periodic-explicit/roundtrip/{version}/{binary}", () => HatchExplicitRoundTrip(version, binary));
        foreach (Vector3 normal in new[] { Vector3.UnitZ, -Vector3.UnitZ, new Vector3(1, -2, 3) })
        foreach (bool link in new[] { false, true })
            Run($"hatch-periodic-explicit/world-boundary/{normal}/{link}", () => HatchExplicitWorldBoundary(normal, link));
        foreach (string form in new[] { "overlap-x", "overlap-y", "overlap-weight", "spans", "repeated", "zero-weight", "negative-weight", "weight-range" })
            Run($"hatch-periodic-explicit/retained-basis/{form}", () => HatchExplicitRetainedBasis(form));
        foreach (string defect in new[] { "zero-weight", "negative-weight", "weight-range", "spans", "repeated", "count", "domain", "multiplicity" })
            Run($"hatch-periodic-explicit/compact-boundary/{defect}", () => HatchExplicitCompactBoundary(defect));
        Run("hatch-periodic-explicit/invalid-source", HatchExplicitInvalidSource);
    }

    private static HatchBoundaryPath.Spline HatchExplicitPacket()
    {
        var edge = new HatchBoundaryPath.Spline
        {
            Degree = 3, IsPeriodic = true, IsRational = true,
            ControlPoints = new[]
            {
                new Vector3(0, 1, 1), new Vector3(4, 7, 2), new Vector3(8, 3, 1),
                new Vector3(6, -2, 1.5), new Vector3(2, -4, 1), new Vector3(-1, -1, .75),
                new Vector3(0, 1, 1)
            },
            Knots = new double[] { 0, 0, 0, 0, 1, 2, 3, 4, 4, 4, 4 },
            StartTangent = new Vector2(2, 1), EndTangent = new Vector2(-1, 2)
        };
        edge.FitPoints.Add(new Vector2(1, 2)); edge.FitPoints.Add(new Vector2(4, 1));
        return edge;
    }

    private static Spline HatchExplicitReference(HatchBoundaryPath.Spline edge)
    {
        return new Spline(edge.ControlPoints.Select(p => new Vector3(p.X, p.Y, 0)),
            edge.ControlPoints.Select(p => p.Z), edge.Knots, edge.Degree, false);
    }

    private static void HatchExplicitCheck(HatchBoundaryPath.Spline edge, Spline spline, bool evaluate)
    {
        Check(spline.IsClosedPeriodic && spline.HasExplicitPeriodicControlPoints, "Complete periodic basis was compacted or lost.");
        Equal(edge.Degree, spline.Degree, "Explicit hatch degree.");
        Check(edge.ControlPoints.Select(p => new Vector3(p.X, p.Y, 0)).SequenceEqual(spline.ControlPoints), "Explicit hatch controls changed.");
        Check(edge.ControlPoints.Select(p => p.Z).SequenceEqual(spline.Weights), "Explicit hatch weights changed.");
        Check(edge.Knots.SequenceEqual(spline.Knots), "Explicit hatch knots changed.");
        Check(edge.FitPoints.Select(p => new Vector3(p.X, p.Y, 0)).SequenceEqual(spline.FitPoints), "Explicit hatch fit metadata changed.");
        Equal(edge.StartTangent.HasValue ? new Vector3(edge.StartTangent.Value.X, edge.StartTangent.Value.Y, 0) : (Vector3?)null,
            spline.StartTangent, "Explicit hatch start tangent.");
        Equal(edge.EndTangent.HasValue ? new Vector3(edge.EndTangent.Value.X, edge.EndTangent.Value.Y, 0) : (Vector3?)null,
            spline.EndTangent, "Explicit hatch end tangent.");
        if (!evaluate) return;
        var reference = HatchExplicitReference(edge);
        double first = edge.Knots[edge.Degree], last = edge.Knots[edge.ControlPoints.Length];
        for (int i = 0; i <= 16; i++)
        {
            double parameter = first + (last - first) * i / 16;
            Check(reference.EvaluateDerivatives(parameter, 2).SequenceEqual(spline.EvaluateDerivatives(parameter, 2)),
                "Explicit hatch conversion changed the rational curve or its derivatives.");
        }
    }

    private static void HatchExplicitRoundTrip(DxfVersion version, bool binary)
    {
        var expected = HatchExplicitPacket();
        if (version < DxfVersion.AutoCad2010)
        {
            expected.FitPoints.Clear(); expected.StartTangent = null; expected.EndTangent = null;
        }
        var edge = (HatchBoundaryPath.Spline)expected.Clone();
        var spline = (Spline)edge.ConvertTo();
        HatchExplicitCheck(expected, spline, true);
        HatchRelationsEqual(expected, edge);
        HatchRelationsEqual(expected, new HatchBoundaryPath.Spline(spline));
        var document = new DxfDocument(version);
        document.Entities.Add(new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false));
        document.Entities.Add(spline);
        for (int cycle = 0; cycle < 3; cycle++)
        {
            var hatch = document.Entities.Hatches.Single();
            HatchRelationsEqual(expected, hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
            HatchExplicitCheck(expected, document.Entities.Splines.Single(), true);
            var boundary = hatch.CreateBoundary(false).OfType<Spline>().Single();
            HatchExplicitCheck(expected, boundary, true);
            Check(!hatch.Associative && hatch.BoundaryPaths.Single().Entities.Count == 0, "Detached boundary unexpectedly linked.");
            using var output = new MemoryStream(); Check(document.Save(output, binary ^ (cycle % 2 != 0)), "Explicit hatch save failed.");
            output.Position = 0;
            document = DxfDocument.Load(output) ?? throw new InvalidOperationException("Explicit hatch reload failed.");
        }
    }

    private static void HatchExplicitWorldBoundary(Vector3 normal, bool link)
    {
        var edge = HatchExplicitPacket(); var snapshot = (HatchBoundaryPath.Spline)edge.Clone();
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false)
        { Normal = normal, Elevation = 7 };
        var document = new DxfDocument(DxfVersion.AutoCad2018); document.Entities.Add(hatch);
        var result = hatch.CreateBoundary(link).OfType<Spline>().Single();
        var reference = HatchExplicitReference(edge); Matrix3 transform = MathHelper.ArbitraryAxis(hatch.Normal);
        Vector3 offset = transform * new Vector3(0, 0, hatch.Elevation);
        for (int i = 0; i <= 16; i++)
            HatchAffineNear(transform * reference.PointAt(i * .25) + offset, result.PointAt(i * .25), "Explicit world boundary curve.");
        Check(result.HasExplicitPeriodicControlPoints && result.IsClosedPeriodic, "World transform changed periodic representation.");
        Check(edge.Knots.SequenceEqual(result.Knots) && edge.ControlPoints.Select(p => p.Z).SequenceEqual(result.Weights), "World transform changed the stored basis.");
        for (int i = 0; i < edge.FitPoints.Count; i++)
            HatchAffineNear(transform * new Vector3(edge.FitPoints[i].X, edge.FitPoints[i].Y, 0) + offset, result.FitPoints[i], "World fit metadata.");
        HatchAffineNear(transform * new Vector3(2, 1, 0), result.StartTangent!.Value, "World start tangent.");
        HatchAffineNear(transform * new Vector3(-1, 2, 0), result.EndTangent!.Value, "World end tangent.");
        Equal(link, hatch.Associative, "World boundary association flag.");
        Equal(link ? 1 : 0, document.Entities.Splines.Count(), "World boundary registration.");
        Check(!link || ReferenceEquals(result, hatch.BoundaryPaths.Single().Entities.Single()), "Linked boundary identity changed.");
        HatchRelationsEqual(snapshot, edge);
        if (link)
        {
            using var output = new MemoryStream(); Check(document.Save(output, true), "Linked explicit boundary save.");
            output.Position = 0; document = DxfDocument.Load(output)!;
            var loaded = document.Entities.Hatches.Single();
            Check(loaded.Associative && loaded.BoundaryPaths.Single().Entities.Single() is Spline stored && stored.HasExplicitPeriodicControlPoints,
                "Linked explicit boundary reload.");
        }
    }

    private static void HatchExplicitRetainedBasis(string form)
    {
        // These full packets used to require cyclic compaction even though the
        // ordinary rational basis can represent them without changing any value.
        var edge = HatchPeriodicInvalid(form); var snapshot = (HatchBoundaryPath.Spline)edge.Clone();
        var spline = (Spline)edge.ConvertTo();
        HatchExplicitCheck(snapshot, spline, form != "negative-weight" && form != "weight-range");
        HatchRelationsEqual(snapshot, edge);
        HatchRelationsEqual(snapshot, new HatchBoundaryPath.Spline(spline));
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false);
        HatchExplicitCheck(snapshot, hatch.CreateBoundary(false).OfType<Spline>().Single(), false);
    }

    private static void HatchExplicitCompactBoundary(string defect)
    {
        var edge = HatchPeriodicInvalid(defect);
        edge.ControlPoints = edge.ControlPoints.Skip(edge.Degree).ToArray();
        var snapshot = (HatchBoundaryPath.Spline)edge.Clone();
        bool rejected = false;
        try { edge.ConvertTo(); } catch (ArgumentException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Malformed compact periodic data was reinterpreted as an explicit basis: " + defect);
        HatchRelationsEqual(snapshot, edge);
    }

    private static void HatchExplicitInvalidSource()
    {
        var source = (Spline)HatchExplicitPacket().ConvertTo();
        source.Knots[4] = source.Knots[0];
        double[] before = source.Knots.ToArray();
        Throws<NotSupportedException>(() => new HatchBoundaryPath.Spline(source));
        Check(before.SequenceEqual(source.Knots), "Refused explicit source conversion changed the basis.");
    }
}
