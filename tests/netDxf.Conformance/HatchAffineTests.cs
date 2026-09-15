using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterHatchAffineTests()
    {
        foreach (var v in SupportedVersions) foreach (bool b in new[] { false, true }) foreach (int op in Enumerable.Range(0, 5)) foreach (int plane in Enumerable.Range(0, 2))
            Run($"hatch-affine/geometry/{v}/{b}/{op}/{plane}", () => HatchAffineGeometry(v, b, op, plane));
        foreach (bool associative in new[] { false, true }) foreach (string defect in new[] { "nan-matrix", "infinite-matrix", "nan-translation", "collapsed-plane", "overflow", "invalid-spline", "invalid-mixed-line" })
            Run($"hatch-affine/atomic/{associative}/{defect}", () => HatchAffineAtomic(associative, defect));
        Run("hatch-affine/association/success", HatchAffineAssociation);
        Run("hatch-affine/exact-identity", HatchAffineIdentity);
        foreach (bool binary in new[] { false, true }) foreach (int op in Enumerable.Range(0, 5)) foreach (int plane in Enumerable.Range(0, 2))
            Run($"hatch-affine/closed-polyline/{binary}/{op}/{plane}", () => HatchAffinePolyline(binary, op, plane));
        foreach (string kind in new[] { "pattern", "gradient" })
            Run($"hatch-affine/unsupported/{kind}", () => HatchAffineUnsupported(kind));
        foreach (bool binary in new[] { false, true }) Run($"hatch-affine/mixed-spline/{binary}", () => HatchAffineMixedSpline(binary));
    }
    private static Matrix3 HatchAffineMatrix(int op) => op switch
    {
        0 => Matrix3.Identity, 1 => Matrix3.RotationX(0.4) * Matrix3.RotationY(-0.3), 2 => Matrix3.Scale(2, 3, 0.5),
        3 => new Matrix3(1, 0.75, -0.2, 0, 1, 0.5, 0.3, 0, 1), _ => Matrix3.Reflection(Vector3.UnitX)
    };
    private static Vector3 HatchAffineWorld(Hatch h, Vector2 p, bool vector = false) => MathHelper.ArbitraryAxis(h.Normal) * new Vector3(p.X, p.Y, vector ? 0 : h.Elevation);
    private static void HatchAffineNear(Vector3 expected, Vector3 actual, string message)
    {
        double scale = Math.Max(1, Math.Max(expected.Modulus(), actual.Modulus()));
        Check((expected - actual).Modulus() <= 2e-10 * scale, message + $" expected {expected}, actual {actual}");
    }
    private static void HatchAffineGeometry(DxfVersion version, bool binary, int operation, int plane)
    {
        var document = HatchRelationsLoad(HatchRelationsRaw(version, binary)); var matrix = HatchAffineMatrix(operation); var translation = new Vector3(7, -11, 13);
        foreach (Hatch h in document.Entities.Hatches)
        {
            h.Normal = plane == 0 ? Vector3.UnitZ : new Vector3(1, 2, 3); h.Elevation = 4;
            var edge = HatchRelationEdge(h); if (!edge.IsRational) edge.ControlPoints[0] = new Vector3(edge.ControlPoints[0].X, edge.ControlPoints[0].Y, -2.5);
            var snapshot = (HatchBoundaryPath.Spline)edge.Clone();
            var controls = edge.ControlPoints.Select(p => HatchAffineWorld(h, new Vector2(p.X, p.Y))).ToArray(); var fits = edge.FitPoints.Select(p => HatchAffineWorld(h, p)).ToArray();
            Vector3? start = edge.StartTangent.HasValue ? HatchAffineWorld(h, edge.StartTangent.Value, true) : null, end = edge.EndTangent.HasValue ? HatchAffineWorld(h, edge.EndTangent.Value, true) : null;
            var line = h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Line>().Single(); Vector3 ls = HatchAffineWorld(h, line.Start), le = HatchAffineWorld(h, line.End);
            var seeds = h.SeedPoints.Select(p => HatchAffineWorld(h, p)).ToArray(); var flags = h.BoundaryPaths.Single().PathType;
            h.TransformBy(matrix, translation); var actual = HatchRelationEdge(h);
            Equal(snapshot.Degree, actual.Degree, "Affine degree"); Equal(snapshot.IsRational, actual.IsRational, "Affine rational flag"); Equal(snapshot.IsPeriodic, actual.IsPeriodic, "Affine periodic flag");
            Equal(snapshot.Knots.Length, actual.Knots.Length, "Affine knot count"); Equal(snapshot.ControlPoints.Length, actual.ControlPoints.Length, "Affine control count");
            for (int i = 0; i < snapshot.Knots.Length; i++) SameDoubleBits(snapshot.Knots[i], actual.Knots[i], "Affine knot value");
            for (int i = 0; i < controls.Length; i++) { SameDoubleBits(snapshot.ControlPoints[i].Z, actual.ControlPoints[i].Z, "Affine stored weight"); HatchAffineNear(matrix * controls[i] + translation, HatchAffineWorld(h, new Vector2(actual.ControlPoints[i].X, actual.ControlPoints[i].Y)), "Affine world control"); }
            for (int i = 0; i < fits.Length; i++) HatchAffineNear(matrix * fits[i] + translation, HatchAffineWorld(h, actual.FitPoints[i]), "Affine world fit");
            Equal(start.HasValue, actual.StartTangent.HasValue, "Affine start presence"); Equal(end.HasValue, actual.EndTangent.HasValue, "Affine end presence");
            if (start.HasValue) HatchAffineNear(matrix * start.Value, HatchAffineWorld(h, actual.StartTangent!.Value, true), "Affine start tangent");
            if (end.HasValue) HatchAffineNear(matrix * end.Value, HatchAffineWorld(h, actual.EndTangent!.Value, true), "Affine end tangent");
            var al = h.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Line>().Single(); HatchAffineNear(matrix * ls + translation, HatchAffineWorld(h, al.Start), "Affine line start"); HatchAffineNear(matrix * le + translation, HatchAffineWorld(h, al.End), "Affine line end");
            for (int i = 0; i < seeds.Length; i++) HatchAffineNear(matrix * seeds[i] + translation, HatchAffineWorld(h, h.SeedPoints[i]), "Affine seed");
            Equal(flags, h.BoundaryPaths.Single().PathType, "Affine path classification"); HatchRelationsEqual(snapshot, edge);
        }
        var expected = document.Entities.Hatches.ToDictionary(HatchRelationName, h => (HatchBoundaryPath.Spline)HatchRelationEdge(h).Clone());
        for (int cycle = 0; cycle < 3; cycle++) { document = HatchRelationsRoundTrip(document, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-affine-{version}-{binary}-{operation}-{plane}.dxf" : null); foreach (var h in document.Entities.Hatches) HatchRelationsEqual(expected[HatchRelationName(h)], HatchRelationEdge(h)); }
    }
    private static void HatchAffineAtomic(bool associative, string defect)
    {
        var document = new DxfDocument(); var source = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, associative); document.Entities.Add(hatch);
        var path = hatch.BoundaryPaths.Single(); var edge = path.Edges.Single(); var matrix = Matrix3.Identity; var translation = Vector3.Zero;
        switch (defect)
        {
            case "nan-matrix": matrix.M11 = double.NaN; break; case "infinite-matrix": matrix.M22 = double.PositiveInfinity; break; case "nan-translation": translation.X = double.NaN; break;
            case "collapsed-plane": matrix = Matrix3.Scale(0, 1, 1); break; case "overflow": translation = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue); matrix = Matrix3.Scale(double.MaxValue); break;
            case "invalid-mixed-line":
                ((HatchBoundaryPath.Line)edge).End = new Vector2(double.NaN, 0);
                hatch.BoundaryPaths.Add(new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Arc { Center = Vector2.Zero, Radius = 1, StartAngle = 0, EndAngle = 180 } })); break;
            default: hatch.BoundaryPaths.Add(new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Spline { Degree = 2, Knots = new double[] { 0, 0, 0, 1, 1, 1 }, ControlPoints = new[] { new Vector3(0, 0, 1), new Vector3(1, 1, 1) } } })); break;
        }
        var paths = hatch.BoundaryPaths.ToArray(); var sources = path.Entities.ToArray(); var reactors = source.Reactors.ToArray(); long seed = OwnershipSeed(document); int members = document.Entities.All.Count(), events = 0;
        hatch.HatchBoundaryPathAdded += (_, _) => events++; hatch.HatchBoundaryPathRemoved += (_, _) => events++;
        bool rejected = false; try { hatch.TransformBy(matrix, translation); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Invalid affine transform rejected"); Equal(associative, hatch.Associative, "Failed transform retains associativity"); Check(paths.SequenceEqual(hatch.BoundaryPaths) && ReferenceEquals(edge, path.Edges.Single()), "Failed transform preserves path and edge identities");
        Check(sources.SequenceEqual(path.Entities) && reactors.SequenceEqual(source.Reactors), "Failed transform retains source occurrences/reactors"); Equal(seed, OwnershipSeed(document), "Failed transform allocates no handles"); Equal(members, document.Entities.All.Count(), "Failed transform retains membership"); Equal(0, events, "Failed transform raises no path events"); Equal(Vector3.UnitZ, hatch.Normal, "Failed transform retains normal"); SameDoubleBits(0, hatch.Elevation, "Failed transform retains elevation");
    }
    private static void HatchAffineIdentity()
    {
        var doc = HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018, false));
        foreach (var hatch in doc.Entities.Hatches)
        {
            hatch.Normal = new Vector3(1, 2, 3); hatch.Elevation = 4; hatch.PixelSize = 0.0625;
            var edge = HatchRelationEdge(hatch); var expected = (HatchBoundaryPath.Spline)edge.Clone(); var path = hatch.BoundaryPaths.Single();
            hatch.TransformBy(Matrix3.Identity, Vector3.Zero);
            Check(ReferenceEquals(path, hatch.BoundaryPaths.Single()) && ReferenceEquals(edge, HatchRelationEdge(hatch)), "Identity keeps boundary object identity");
            HatchRelationsEqual(expected, HatchRelationEdge(hatch)); SameDoubleBits(0.0625, hatch.PixelSize!.Value, "Identity preserves pixel size");
        }
    }
    private static void HatchAffinePolyline(bool binary, int operation, int plane)
    {
        var points = new[] { new Vector3(0, 0, 0), new Vector3(10, 0, -0.0), new Vector3(10, 5, 0), new Vector3(0, 5, 0) };
        var edge = new HatchBoundaryPath.Polyline { IsClosed = true, Vertexes = points.ToArray() };
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false) { Normal = plane == 0 ? Vector3.UnitZ : new Vector3(1, 2, 3), Elevation = 4, PixelSize = 0.0625 };
        var matrix = HatchAffineMatrix(operation); var translation = new Vector3(7, -11, 13); var world = points.Select(p => HatchAffineWorld(hatch, new Vector2(p.X, p.Y))).ToArray(); var flags = hatch.BoundaryPaths.Single().PathType;
        hatch.TransformBy(matrix, translation); var actual = (HatchBoundaryPath.Polyline)hatch.BoundaryPaths.Single().Edges.Single();
        Check(actual.IsClosed, "Affine straight polyline closure"); Equal(flags, hatch.BoundaryPaths.Single().PathType, "Affine polyline flags");
        for (int i = 0; i < points.Length; i++) { HatchAffineNear(matrix * world[i] + translation, HatchAffineWorld(hatch, new Vector2(actual.Vertexes[i].X, actual.Vertexes[i].Y)), "Affine polyline world vertex"); SameDoubleBits(points[i].Z, actual.Vertexes[i].Z, "Affine stored zero bulge"); }
        var doc = new DxfDocument(); doc.Entities.Add(hatch); doc = HatchRelationsRoundTrip(doc, binary, $"hatch-affine-polyline-{binary}-{operation}-{plane}.dxf");
        SameDoubleBits(0.0625, doc.Entities.Hatches.Single().PixelSize!.Value, "Affine stored pixel size");
    }
    private static void HatchAffineUnsupported(string kind)
    {
        HatchBoundaryPath.Edge edge = new HatchBoundaryPath.Line { Start = Vector2.Zero, End = Vector2.UnitX };
        if (kind == "arc") edge = new HatchBoundaryPath.Arc { Center = Vector2.Zero, Radius = 1, StartAngle = 0, EndAngle = 180, IsCounterclockwise = true };
        if (kind == "ellipse") edge = new HatchBoundaryPath.Ellipse { Center = Vector2.Zero, EndMajorAxis = Vector2.UnitX, MinorRatio = 0.5, StartAngle = 0, EndAngle = 180, IsCounterclockwise = true };
        if (kind == "bulge") edge = new HatchBoundaryPath.Polyline { IsClosed = true, Vertexes = new[] { new Vector3(0, 0, 1), new Vector3(2, 0, 0), new Vector3(0, 2, 0) } };
        HatchPattern pattern = kind == "pattern" ? HatchPattern.Line : kind == "gradient" ? new HatchGradientPattern() : HatchPattern.Solid;
        var hatch = new Hatch(pattern, new[] { new HatchBoundaryPath(new[] { edge }) }, false); var path = hatch.BoundaryPaths.Single();
        Throws<NotSupportedException>(() => hatch.TransformBy(HatchAffineMatrix(3), new Vector3(7, -11, 13)));
        Check(ReferenceEquals(path, hatch.BoundaryPaths.Single()) && ReferenceEquals(edge, path.Edges.Single()), "Unsupported transform preserves boundary objects");
        SameDoubleBits(1, hatch.Pattern.Scale, "Unsupported pattern scale unchanged"); SameDoubleBits(0, hatch.Pattern.Angle, "Unsupported pattern angle unchanged");
    }
    private static void HatchAffineMixedSpline(bool binary)
    {
        var source = HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018, binary));
        var spline = (HatchBoundaryPath.Spline)HatchRelationEdge(source.Entities.Hatches.Single(h => HatchRelationName(h) == "PERIODIC")).Clone();
        spline.IsRational = false; spline.ControlPoints[0] = new Vector3(spline.ControlPoints[0].X, spline.ControlPoints[0].Y, -2.5);
        var hatch = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { spline, new HatchBoundaryPath.Arc { Center = Vector2.Zero, Radius = 3, StartAngle = 0, EndAngle = 180, IsCounterclockwise = true } }) }, false);
        var expected = (HatchBoundaryPath.Spline)spline.Clone(); var doc = new DxfDocument(); doc.Entities.Add(hatch);
        hatch.TransformBy(Matrix3.Identity, Vector3.Zero); HatchRelationsEqual(expected, hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
        doc = HatchRelationsRoundTrip(doc, binary); HatchRelationsEqual(expected, doc.Entities.Hatches.Single().BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single());
    }
    private static void HatchAffineAssociation()
    {
        var document = new DxfDocument(); var source = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, true); document.Entities.Add(h);
        var handle = source.Handle; h.TransformBy(HatchAffineMatrix(3), new Vector3(7, -11, 13)); Check(!h.Associative && h.BoundaryPaths.Single().Entities.Count == 0, "Successful transform follows explicit unlink behavior");
        Check(source.Owner != null && ReferenceEquals(document.GetObjectByHandle(handle), source), "Unlink retains source entity identity"); Equal(Vector3.Zero, source.StartPoint, "Source geometry remains unchanged"); Check(!source.Reactors.Contains(h), "Successful unlink releases source use");
    }
}
