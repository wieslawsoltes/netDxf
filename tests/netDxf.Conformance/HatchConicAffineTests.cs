using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterHatchConicAffineTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            foreach (int operation in Enumerable.Range(0, 7)) foreach (int plane in Enumerable.Range(0, 2))
                Run($"hatch-conic/geometry/{version}/{binary}/{operation}/{plane}", () => HatchConicGeometry(version, binary, operation, plane));
        foreach (bool association in new[] { false, true })
            foreach (string defect in new[] { "zero-radius", "negative-ratio", "zero-axis", "multiple-turns", "tiny-bulge", "terminal-bulge", "coincident-bulge", "extreme-anisotropy" })
                Run($"hatch-conic/atomic/{association}/{defect}", () => HatchConicAtomic(association, defect));
        Run("hatch-conic/identity", HatchConicIdentity);
        foreach (bool binary in new[] { false, true }) Run($"hatch-conic/mixed-stored-spline/{binary}", () => HatchConicMixed(binary));
        Run("hatch-conic/open-dormant-bulge", HatchConicDormant);
        foreach (bool ellipse in new[] { false, true }) foreach (bool ccw in new[] { false, true }) foreach (double span in new[] { 0.0, 360.0, -360.0, 359.99999999 })
            Run($"hatch-conic/repeated-turns/{ellipse}/{ccw}/{span:R}", () => HatchConicTurns(ellipse, ccw, span));
        Run("hatch-conic/bulge-conditioning", HatchConicConditioning);
    }
    private static Matrix3 HatchConicMatrix(int operation) => operation < 5 ? HatchAffineMatrix(operation)
        : operation == 5 ? Matrix3.Scale(.2, 7, 1) : Matrix3.Scale(100, .1, 2);
    private static double HatchConicMod(double angle) { angle %= 2 * Math.PI; return angle < 0 ? angle + 2 * Math.PI : angle; }
    private static Vector2 HatchConicPoint(HatchBoundaryPath.Edge edge, double fraction)
    {
        if (edge is HatchBoundaryPath.Line line) return line.Start + (line.End - line.Start) * fraction;
        Vector2 center, major; double ratio, start, end; bool ccw;
        if (edge is HatchBoundaryPath.Arc arc)
        { center = arc.Center; major = new Vector2(arc.Radius, 0); ratio = 1; start = arc.StartAngle; end = arc.EndAngle; ccw = arc.IsCounterclockwise; }
        else
        { var ellipse = (HatchBoundaryPath.Ellipse)edge; center = ellipse.Center; major = ellipse.EndMajorAxis; ratio = ellipse.MinorRatio; start = ellipse.StartAngle; end = ellipse.EndAngle; ccw = ellipse.IsCounterclockwise; }
        double sign = ccw ? 1 : -1;
        double Parameter(double value) { double a = sign * value * Math.PI / 180; return Math.Atan2(Math.Sin(a) / ratio, Math.Cos(a)); }
        double first = Parameter(start), last = Parameter(end);
        double sweep = start == end ? 0 : Math.Abs(end - start) == 360 ? sign * 2 * Math.PI : sign * HatchConicMod(sign * (last - first));
        double t = first + sweep * fraction;
        return center + major * Math.Cos(t) + new Vector2(-major.Y, major.X) * (ratio * Math.Sin(t));
    }
    private static HatchBoundaryPath.Edge HatchConicSegment(HatchBoundaryPath.Polyline polyline, int index)
    {
        Vector3 a = polyline.Vertexes[index], b = polyline.Vertexes[(index + 1) % polyline.Vertexes.Length];
        Vector2 first = new Vector2(a.X, a.Y), last = new Vector2(b.X, b.Y);
        if (a.Z == 0) return new HatchBoundaryPath.Line { Start = first, End = last };
        Vector2 chord = last - first;
        Vector2 center = (first + last) / 2 + new Vector2(-chord.Y, chord.X) * ((1 / a.Z - a.Z) / 4);
        double radius = (first - center).Modulus(); bool ccw = a.Z > 0;
        double Angle(Vector2 p) { double v = (ccw ? 1 : -1) * Math.Atan2(p.Y - center.Y, p.X - center.X); return HatchConicMod(v) * 180 / Math.PI; }
        return new HatchBoundaryPath.Arc { Center = center, Radius = radius, StartAngle = Angle(first), EndAngle = Angle(last), IsCounterclockwise = ccw };
    }
    private static List<HatchBoundaryPath.Edge> HatchConicEdges(Hatch hatch)
    {
        var result = new List<HatchBoundaryPath.Edge>();
        foreach (var edge in hatch.BoundaryPaths.Single().Edges)
        {
            if (edge is not HatchBoundaryPath.Polyline polyline) result.Add(edge);
            else for (int i = 0; i < polyline.Vertexes.Length - (polyline.IsClosed ? 0 : 1); i++) result.Add(HatchConicSegment(polyline, i));
        }
        return result;
    }
    private static void HatchConicGeometry(DxfVersion version, bool binary, int operation, int plane)
    {
        string year = version.ToString().Replace("AutoCad", "");
        var doc = DxfDocument.Load(Path.Combine("tests", "fixtures", "hatch-conic-affine", $"ezdxf-hatch-conic-R{year}-{(binary ? "binary" : "ascii")}.dxf")) ?? throw new Exception("Conic fixture load failed");
        var matrix = HatchConicMatrix(operation); var translation = new Vector3(7, -11, 13);
        var expected = new Dictionary<string, Vector3[][]>();
        foreach (var h in doc.Entities.Hatches)
        {
            h.Normal = plane == 0 ? Vector3.UnitZ : new Vector3(1, 2, 3); h.Elevation = 4; h.PixelSize = .0625;
            h.SeedPoints.Add(new Vector2(1, 2));
            expected[h.Layer.Name] = HatchConicEdges(h).Select(e => Enumerable.Range(0, 33).Select(i => matrix * HatchAffineWorld(h, HatchConicPoint(e, i / 32.0)) + translation).ToArray()).ToArray();
            var oldPath = h.BoundaryPaths.Single(); var oldFlags = oldPath.PathType; var sourceEdges = oldPath.Edges.ToArray();
            h.TransformBy(matrix, translation);
            Equal(oldFlags & ~HatchBoundaryPathTypeFlags.Polyline, h.BoundaryPaths.Single().PathType & ~HatchBoundaryPathTypeFlags.Polyline, "Conic classification flags");
            Check(sourceEdges.SequenceEqual(oldPath.Edges), "Conic transform changed original edge objects");
        }
        void Verify(DxfDocument drawing)
        {
            foreach (var h in drawing.Entities.Hatches)
            {
                var edges = HatchConicEdges(h); Equal(expected[h.Layer.Name].Length, edges.Count, "Conic segment count / open closure");
                for (int e = 0; e < edges.Count; e++) for (int i = 0; i < 33; i++)
                    HatchAffineNear(expected[h.Layer.Name][e][i], HatchAffineWorld(h, HatchConicPoint(edges[e], i / 32.0)), "Conic world curve " + h.Layer.Name);
                SameDoubleBits(.0625, h.PixelSize!.Value, "Conic pixel size");
            }
        }
        Verify(doc);
        for (int cycle = 0; cycle < 3; cycle++) { doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-conic-{version}-{binary}-{operation}-{plane}.dxf" : null); Verify(doc); }
    }
    private static void HatchConicAtomic(bool association, string defect)
    {
        var source = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, association);
        var doc = new DxfDocument(); doc.Entities.Add(h);
        HatchBoundaryPath.Edge edge = new HatchBoundaryPath.Ellipse { Center = new Vector2(2, 3), EndMajorAxis = new Vector2(3, 4), MinorRatio = .4, StartAngle = 30, EndAngle = 150, IsCounterclockwise = true };
        var matrix = HatchConicMatrix(3);
        switch (defect)
        {
            case "zero-radius": edge = new HatchBoundaryPath.Arc { Radius = 0, StartAngle = 0, EndAngle = 180 }; break;
            case "negative-ratio": ((HatchBoundaryPath.Ellipse)edge).MinorRatio = -.4; break;
            case "zero-axis": ((HatchBoundaryPath.Ellipse)edge).EndMajorAxis = Vector2.Zero; break;
            case "multiple-turns": ((HatchBoundaryPath.Ellipse)edge).EndAngle = 750; break;
            case "extreme-anisotropy": matrix = Matrix3.Scale(1e12, 1e-12, 1); break;
            default: edge = new HatchBoundaryPath.Polyline { IsClosed = false, Vertexes = new[] { new Vector3(0, 0, defect == "tiny-bulge" ? 1e-15 : .25), new Vector3(defect == "coincident-bulge" ? 0 : 10, 0, defect == "terminal-bulge" ? .5 : 0) } }; break;
        }
        h.BoundaryPaths.Add(new HatchBoundaryPath(new[] { edge })); var paths = h.BoundaryPaths.ToArray(); var reactors = source.Reactors.ToArray(); var entities = paths[0].Entities.ToArray(); long seed = OwnershipSeed(doc); int members = doc.Entities.All.Count(), events = 0;
        h.HatchBoundaryPathAdded += (_, _) => events++; h.HatchBoundaryPathRemoved += (_, _) => events++;
        bool rejected = false; try { h.TransformBy(matrix, new Vector3(7, -11, 13)); } catch (ArgumentException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Unrepresentable conic rejected: " + defect); Check(paths.SequenceEqual(h.BoundaryPaths) && ReferenceEquals(edge, paths[1].Edges.Single()), "Rejected conic preserves object identities");
        Equal(association, h.Associative, "Rejected conic preserves association"); Check(reactors.SequenceEqual(source.Reactors) && entities.SequenceEqual(paths[0].Entities), "Rejected conic preserves source occurrences/reactors");
        Equal(seed, OwnershipSeed(doc), "Rejected conic allocates no handles"); Equal(members, doc.Entities.All.Count(), "Rejected conic retains membership"); Equal(0, events, "Rejected conic raises no callbacks");
        Equal(Vector3.UnitZ, h.Normal, "Rejected conic normal unchanged"); SameDoubleBits(0, h.Elevation, "Rejected conic elevation unchanged"); SameDoubleBits(1, h.Pattern.Scale, "Rejected conic pattern scale unchanged");
    }
    private static void HatchConicIdentity()
    {
        var ellipse = new HatchBoundaryPath.Ellipse { Center = new Vector2(2, 3), EndMajorAxis = new Vector2(3, 4), MinorRatio = .4, StartAngle = -30, EndAngle = 330, IsCounterclockwise = false };
        var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { ellipse }) }, false); var path = h.BoundaryPaths.Single();
        h.TransformBy(Matrix3.Identity, Vector3.Zero); Check(ReferenceEquals(path, h.BoundaryPaths.Single()) && ReferenceEquals(ellipse, path.Edges.Single()), "Conic identity preserves objects");
        SameDoubleBits(-30, ellipse.StartAngle, "Conic identity preserves stored start"); SameDoubleBits(330, ellipse.EndAngle, "Conic identity preserves stored full turn");
    }
    private static void HatchConicDormant()
    {
        var p = new HatchBoundaryPath.Polyline { IsClosed = false, Vertexes = new[] { new Vector3(0, 0, .25), new Vector3(10, 0, .75) } };
        var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { p }) }, false);
        h.TransformBy(Matrix3.Reflection(Vector3.UnitX), Vector3.Zero); var result = (HatchBoundaryPath.Polyline)h.BoundaryPaths.Single().Edges.Single();
        Check(!result.IsClosed, "Similarity preserves open representation"); SameDoubleBits(-.25, result.Vertexes[0].Z, "Active reflected bulge"); SameDoubleBits(.75, result.Vertexes[1].Z, "Dormant terminal bulge remains stored");
    }
    private static void HatchConicTurns(bool ellipse, bool ccw, double span)
    {
        HatchBoundaryPath.Edge edge = ellipse
            ? new HatchBoundaryPath.Ellipse { Center = new Vector2(2, 3), EndMajorAxis = new Vector2(3, 4), MinorRatio = .4, StartAngle = 30, EndAngle = 30 + span, IsCounterclockwise = ccw }
            : new HatchBoundaryPath.Arc { Center = new Vector2(2, 3), Radius = 5, StartAngle = 30, EndAngle = 30 + span, IsCounterclockwise = ccw };
        var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { edge }) }, false) { Normal = new Vector3(1, 2, 3), Elevation = 4 };
        var expected = Enumerable.Range(0, 33).Select(i => HatchAffineWorld(h, HatchConicPoint(edge, i / 32.0))).ToArray();
        foreach (int operation in new[] { 3, 4, 2, 1 })
        {
            var matrix = HatchConicMatrix(operation); var translation = new Vector3(7, -11, 13);
            expected = expected.Select(p => matrix * p + translation).ToArray(); h.TransformBy(matrix, translation);
            var result = (HatchBoundaryPath.Ellipse)h.BoundaryPaths.Single().Edges.Single();
            Equal(span == 0, result.StartAngle == result.EndAngle, "Repeated transform zero interval");
            Equal(Math.Abs(span) == 360, Math.Abs(result.EndAngle - result.StartAngle) == 360, "Repeated transform exact stored full turn");
            for (int i = 0; i < 33; i++) HatchAffineNear(expected[i], HatchAffineWorld(h, HatchConicPoint(result, i / 32.0)), "Repeated conic transform world curve");
        }
    }
    private static void HatchConicConditioning()
    {
        var records = new List<object>(); int accepted = 0, rejected = 0;
        foreach (int operation in new[] { 2, 3 }) foreach (int sign in new[] { -1, 1 }) foreach (int exponent in Enumerable.Range(3, 14))
        {
            double bulge = sign * Math.Pow(10, -exponent);
            var p = new HatchBoundaryPath.Polyline { IsClosed = false, Vertexes = new[] { new Vector3(0, 0, bulge), new Vector3(10, 0, 0) } };
            var h = new Hatch(HatchPattern.Solid, new[] { new HatchBoundaryPath(new[] { p }) }, false); var path = h.BoundaryPaths.Single();
            string? file = null; bool success;
            try { h.TransformBy(HatchConicMatrix(operation), new Vector3(7, -11, 13)); success = true; }
            catch (ArgumentException) { success = false; }
            if (success)
            {
                Check(h.BoundaryPaths.Single().Edges.Single() is HatchBoundaryPath.Ellipse, "A tiny nonzero bulge was flattened");
                var doc = new DxfDocument(); doc.Entities.Add(h); file = $"hatch-conic-conditioning-{operation}-{sign}-{exponent}.dxf"; HatchRelationsRoundTrip(doc, false, file); accepted++;
            }
            else { Check(ReferenceEquals(path, h.BoundaryPaths.Single()) && ReferenceEquals(p, path.Edges.Single()), "Rejected tiny bulge mutated geometry"); rejected++; }
            if (exponent == 3) Check(success, "Well-conditioned small bulge should transform");
            if (exponent == 16) Check(!success, "Unrepresentable tiny bulge should reject");
            records.Add(new { operation, sign, exponent, bulge, accepted = success, file });
        }
        Check(accepted > 0 && rejected > 0, "Conditioning probe covers both outcomes");
        File.WriteAllText(Path.Combine(ArtifactDirectory, "hatch-conic-conditioning.json"), System.Text.Json.JsonSerializer.Serialize(records, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    private static void HatchConicMixed(bool binary)
    {
        var doc = HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018, binary));
        foreach (var h in doc.Entities.Hatches)
        {
            var spline = HatchRelationEdge(h); if (!spline.IsRational) spline.ControlPoints[0] = new Vector3(spline.ControlPoints[0].X, spline.ControlPoints[0].Y, -2.5);
            var snapshot = (HatchBoundaryPath.Spline)spline.Clone();
            h.BoundaryPaths.Add(new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Arc { Center = new Vector2(2, 3), Radius = 5, StartAngle = 30, EndAngle = 150, IsCounterclockwise = false } }));
            h.TransformBy(HatchConicMatrix(3), new Vector3(7, -11, 13)); var result = h.BoundaryPaths[0].Edges.OfType<HatchBoundaryPath.Spline>().Single();
            Equal(snapshot.IsRational, result.IsRational, "Conic mixed rational flag"); Equal(snapshot.IsPeriodic, result.IsPeriodic, "Conic mixed periodic flag"); Check(snapshot.Knots.SequenceEqual(result.Knots), "Conic mixed knots");
            Check(snapshot.ControlPoints.Select(p => p.Z).SequenceEqual(result.ControlPoints.Select(p => p.Z)), "Conic mixed weights");
        }
        HatchRelationsRoundTrip(doc, binary, $"hatch-conic-mixed-{binary}.dxf");
    }
}
