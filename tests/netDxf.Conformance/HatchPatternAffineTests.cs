using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterHatchPatternAffineTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            foreach (int operation in Enumerable.Range(0, 7)) foreach (int plane in Enumerable.Range(0, 2))
                Run($"hatch-pattern-affine/geometry/{version}/{binary}/{operation}/{plane}", () => HatchPatternAffineGeometry(version, binary, operation, plane));
        foreach (bool association in new[] { false, true }) foreach (string defect in new[] {
            "null-line", "angle", "base", "delta", "dash-nan", "dash-infinite", "origin", "line-count", "dash-count", "underflow",
            "dash-overflow", "base-overflow", "offset-overflow", "empty", "subclass", "user-defined", "double", "gradient", "origin-z", "translation-z", "tiny-z" })
            Run($"hatch-pattern-affine/atomic/{association}/{defect}", () => HatchPatternAffineAtomic(association, defect));
        foreach (bool binary in new[] { false, true }) foreach (int operation in Enumerable.Range(0, 3))
            Run($"hatch-pattern-affine/origin/{binary}/{operation}", () => HatchPatternAffineOrigin(binary, operation));
        foreach (bool binary in new[] { false, true }) Run($"hatch-pattern-affine/mixed/{binary}", () => HatchPatternAffineMixed(binary));
        Run("hatch-pattern-affine/shared", HatchPatternAffineShared);
        Run("hatch-pattern-affine/identity", HatchPatternAffineIdentity);
        Run("hatch-pattern-affine/repeated", HatchPatternAffineRepeated);
    }
    private static Matrix3 HatchPatternAffineMatrix(int operation) => operation switch
    {
        0 => Matrix3.Identity, 1 => Matrix3.RotationZ(.43), 2 => Matrix3.Scale(2, 3, .5),
        3 => new Matrix3(1, .75, -.2, 0, 1, .5, 0, 0, 1), 4 => Matrix3.Reflection(Vector3.UnitX),
        5 => Matrix3.Scale(2, 3, 0), _ => Matrix3.Scale(100, .1, 2)
    };
    private static Vector2 HatchPatternRotate(Vector2 p, double angle)
    { double a = angle * Math.PI / 180; return new Vector2(p.X * Math.Cos(a) - p.Y * Math.Sin(a), p.X * Math.Sin(a) + p.Y * Math.Cos(a)); }
    private static Vector2[] HatchPatternSamples(HatchPattern pattern, HatchPatternLineDefinition line)
    {
        var b = pattern.Scale * HatchPatternRotate(line.Origin, pattern.Angle);
        var offset = pattern.Scale * HatchPatternRotate(line.Delta, pattern.Angle + line.Angle);
        var u = HatchPatternRotate(Vector2.UnitX, pattern.Angle + line.Angle);
        var breaks = new List<double> { 0 }; double length = 0;
        foreach (double dash in line.DashPattern) { breaks.Add(length + Math.Abs(dash) * pattern.Scale / 2); length += Math.Abs(dash) * pattern.Scale; breaks.Add(length); }
        var result = new List<Vector2>();
        foreach (int n in new[] { -3, -1, 0, 2, 5 }) foreach (int m in new[] { -2, 0, 3 }) foreach (double at in breaks)
            result.Add(b + n * offset + (m * length + at) * u);
        return result.ToArray();
    }
    private static HatchPattern HatchPatternAffineExample()
    {
        var pattern = new HatchPattern("AFFINE_EXPLICIT", "Preserved description") { Type = HatchType.Custom, Angle = 27, Scale = .75, Origin = new Vector2(3, 4) };
        var line = new HatchPatternLineDefinition { Angle = 15, Origin = new Vector2(1, 2), Delta = new Vector2(.5, 3) };
        line.DashPattern.AddRange(new[] { 2.0, -1.0, 0.0, -0.0, -.5 }); pattern.LineDefinitions.Add(line); return pattern;
    }
    private static Hatch HatchPatternAffineHatch(HatchPattern pattern) => new Hatch(pattern,
        new[] { new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Line { Start = Vector2.Zero, End = new Vector2(2, 3) } }) }, false);
    private static void HatchPatternAffineGeometry(DxfVersion version, bool binary, int operation, int plane)
    {
        string year = version.ToString().Replace("AutoCad", "");
        var doc = DxfDocument.Load(Path.Combine("tests", "fixtures", "hatch-pattern-affine", $"ezdxf-hatch-pattern-R{year}-{(binary ? "binary" : "ascii")}.dxf")) ?? throw new Exception("Pattern fixture load failed");
        var matrix = HatchPatternAffineMatrix(operation); var translation = new Vector3(7, -11, 0);
        var expected = new Dictionary<string, Vector3[][]>(); var origins = new Dictionary<string, Vector2>(); var snapshots = new Dictionary<string, HatchPattern>(); var directions = new Dictionary<string, Vector3[]>();
        foreach (var h in doc.Entities.Hatches)
        {
            h.Normal = plane == 0 ? Vector3.UnitZ : new Vector3(1, 2, 3); h.Elevation = 4;
            expected[h.Layer.Name] = h.Pattern.LineDefinitions.Select(l => HatchPatternSamples(h.Pattern, l).Select(p => matrix * HatchAffineWorld(h, p) + translation).ToArray()).ToArray();
            var origin = matrix * new Vector3(h.Pattern.Origin.X, h.Pattern.Origin.Y, 0) + translation; origins[h.Layer.Name] = new Vector2(origin.X, origin.Y);
            directions[h.Layer.Name] = h.Pattern.LineDefinitions.Select(l => Vector3.Normalize(matrix * HatchAffineWorld(h, HatchPatternRotate(Vector2.UnitX, h.Pattern.Angle + l.Angle), true))).ToArray();
            snapshots[h.Layer.Name] = (HatchPattern)h.Pattern.Clone(); var original = h.Pattern;
            h.TransformBy(matrix, translation); Check(!ReferenceEquals(original, h.Pattern), "Explicit pattern receives an independent snapshot");
            HatchPatternAffineEqual(snapshots[h.Layer.Name], original);
        }
        void Verify(DxfDocument drawing)
        {
            foreach (var h in drawing.Entities.Hatches)
            {
                var snapshot = snapshots[h.Layer.Name]; Equal(snapshot.Name, h.Pattern.Name, "Pattern name"); Equal(snapshot.Type, h.Pattern.Type, "Pattern type"); Equal(snapshot.Style, h.Pattern.Style, "Pattern style"); Equal(snapshot.IsDouble, h.Pattern.IsDouble, "Pattern double");
                HatchAffineNear(new Vector3(origins[h.Layer.Name].X, origins[h.Layer.Name].Y, 0), new Vector3(h.Pattern.Origin.X, h.Pattern.Origin.Y, 0), "Separate WCS pattern Origin");
                Equal(expected[h.Layer.Name].Length, h.Pattern.LineDefinitions.Count, "Pattern family count");
                for (int f = 0; f < h.Pattern.LineDefinitions.Count; f++)
                {
                    var line = h.Pattern.LineDefinitions[f]; var old = snapshot.LineDefinitions[f]; Equal(old.DashPattern.Count, line.DashPattern.Count, "Pattern dash count");
                    for (int d = 0; d < old.DashPattern.Count; d++)
                    { Equal(Math.Sign(old.DashPattern[d]), Math.Sign(line.DashPattern[d]), "Dash/dot/gap sign"); if (old.DashPattern[d] == 0) SameDoubleBits(old.DashPattern[d], line.DashPattern[d], "Stored signed-zero dot"); }
                    HatchAffineNear(directions[h.Layer.Name][f], Vector3.Normalize(HatchAffineWorld(h, HatchPatternRotate(Vector2.UnitX, h.Pattern.Angle + line.Angle), true)), "Pattern family direction");
                    var points = HatchPatternSamples(h.Pattern, line); Equal(expected[h.Layer.Name][f].Length, points.Length, "Pattern probe count");
                    for (int i = 0; i < points.Length; i++) HatchAffineNear(expected[h.Layer.Name][f][i], HatchAffineWorld(h, points[i]), "Pattern world phase/family/dash point");
                }
            }
        }
        Verify(doc);
        for (int cycle = 0; cycle < 3; cycle++) { doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-pattern-affine-{version}-{binary}-{operation}-{plane}.dxf" : null); Verify(doc); }
    }
    private static void HatchPatternAffineEqual(HatchPattern expected, HatchPattern actual)
    {
        Equal(expected.Name, actual.Name, "Unchanged pattern name"); Equal(expected.Description, actual.Description, "Unchanged pattern description"); Equal(expected.Type, actual.Type, "Unchanged pattern type"); Equal(expected.IsDouble, actual.IsDouble, "Unchanged pattern double");
        Equal(expected.Origin, actual.Origin, "Unchanged pattern Origin"); SameDoubleBits(expected.Scale, actual.Scale, "Unchanged pattern scale"); SameDoubleBits(expected.Angle, actual.Angle, "Unchanged pattern angle");
        Equal(expected.LineDefinitions.Count, actual.LineDefinitions.Count, "Unchanged family count");
        for (int i = 0; i < expected.LineDefinitions.Count; i++)
        {
            var a = expected.LineDefinitions[i]; var b = actual.LineDefinitions[i]; SameDoubleBits(a.Angle, b.Angle, "Unchanged line angle"); Equal(a.Origin, b.Origin, "Unchanged line base"); Equal(a.Delta, b.Delta, "Unchanged line offset"); Equal(a.DashPattern.Count, b.DashPattern.Count, "Unchanged dash count");
            for (int d = 0; d < a.DashPattern.Count; d++) SameDoubleBits(a.DashPattern[d], b.DashPattern[d], "Unchanged signed dash");
        }
    }
    private sealed class HatchPatternAffineSubclass : HatchPattern
    {
        public int CloneCalls;
        public HatchPatternAffineSubclass() : base("SUBCLASS") { Type = HatchType.Custom; LineDefinitions.Add(new HatchPatternLineDefinition()); }
        public override object Clone() { CloneCalls++; throw new Exception("Virtual Clone must not run"); }
    }
    private static void HatchPatternAffineAtomic(bool association, string defect)
    {
        var pattern = HatchPatternAffineExample(); var line = pattern.LineDefinitions.Single(); var matrix = HatchPatternAffineMatrix(3); var translation = new Vector3(7, -11, 0);
        switch (defect)
        {
            case "null-line": pattern.LineDefinitions.Add(null!); break;
            case "angle": line.Angle = double.NaN; break;
            case "base": line.Origin = new Vector2(double.PositiveInfinity, 0); break;
            case "delta": line.Delta = new Vector2(0, double.NaN); break;
            case "dash-nan": line.DashPattern.Add(double.NaN); break;
            case "dash-infinite": line.DashPattern.Add(double.PositiveInfinity); break;
            case "origin": pattern.Origin = new Vector2(double.NaN, 0); break;
            case "line-count": pattern.LineDefinitions.AddRange(Enumerable.Repeat(line, short.MaxValue)); break;
            case "dash-count": line.DashPattern.AddRange(Enumerable.Repeat(1.0, short.MaxValue)); break;
            case "underflow": pattern.Angle = 0; pattern.Scale = 1; line.Angle = 0; line.DashPattern.Add(double.Epsilon); matrix = Matrix3.Scale(.1, .2, 1); break;
            case "dash-overflow": line.DashPattern.Add(double.MaxValue); matrix = Matrix3.Scale(8, 3, 1); break;
            case "base-overflow": line.Origin = new Vector2(double.MaxValue, double.MaxValue); matrix = Matrix3.Scale(8, 3, 1); break;
            case "offset-overflow": line.Delta = new Vector2(double.MaxValue, double.MaxValue); matrix = Matrix3.Scale(8, 3, 1); break;
            case "empty": pattern.LineDefinitions.Clear(); break;
            case "subclass": pattern = new HatchPatternAffineSubclass(); break;
            case "user-defined": pattern.Type = HatchType.UserDefined; break;
            case "double": pattern.IsDouble = true; break;
            case "gradient": pattern = new HatchGradientPattern(); break;
            case "origin-z": pattern.Origin = new Vector2(3, 5); matrix = new Matrix3(1, .3, 0, 0, 1, 0, 4, -3, 1); break;
            case "translation-z": translation.Z = 1; break;
            case "tiny-z": translation.Z = 1e-200; break;
        }
        var source = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var h = new Hatch(pattern, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, association); var doc = new DxfDocument(); doc.Entities.Add(h);
        var path = h.BoundaryPaths.Single(); var edge = path.Edges.Single(); var sources = path.Entities.ToArray(); var reactors = source.Reactors.ToArray(); var definitions = pattern.LineDefinitions.ToArray();
        var oldOrigin = pattern.Origin; double oldAngle = pattern.Angle, oldScale = pattern.Scale; var dashes = line.DashPattern.ToArray(); long seed = OwnershipSeed(doc); int members = doc.Entities.All.Count(), events = 0;
        h.HatchBoundaryPathAdded += (_, _) => events++; h.HatchBoundaryPathRemoved += (_, _) => events++;
        bool rejected = false; try { h.TransformBy(matrix, translation); } catch (ArgumentException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Unrepresentable explicit pattern rejected: " + defect); Check(ReferenceEquals(pattern, h.Pattern) && definitions.SequenceEqual(pattern.LineDefinitions), "Refused pattern retains original objects");
        SameDoubleBits(oldOrigin.X, pattern.Origin.X, "Refused origin X"); SameDoubleBits(oldOrigin.Y, pattern.Origin.Y, "Refused origin Y"); SameDoubleBits(oldAngle, pattern.Angle, "Refused angle"); SameDoubleBits(oldScale, pattern.Scale, "Refused scale");
        for (int i = 0; i < dashes.Length; i++) SameDoubleBits(dashes[i], line.DashPattern[i], "Refused dash");
        Check(ReferenceEquals(path, h.BoundaryPaths.Single()) && ReferenceEquals(edge, path.Edges.Single()), "Refused boundary identities"); Equal(association, h.Associative, "Refused association");
        Check(sources.SequenceEqual(path.Entities) && reactors.SequenceEqual(source.Reactors), "Refused source occurrences/reactors"); Equal(seed, OwnershipSeed(doc), "Refused handle seed"); Equal(members, doc.Entities.All.Count(), "Refused membership"); Equal(0, events, "Refused callbacks");
        Equal(Vector3.UnitZ, h.Normal, "Refused normal"); SameDoubleBits(0, h.Elevation, "Refused elevation"); if (pattern is HatchPatternAffineSubclass subclass) Equal(0, subclass.CloneCalls, "No user virtual callback in validation");
    }
    private static void HatchPatternAffineOrigin(bool binary, int operation)
    {
        var h = HatchPatternAffineHatch(HatchPatternAffineExample()); h.Normal = new Vector3(1, 2, 3); h.Elevation = 4;
        var matrix = new Matrix3(1, .3, 0, 0, 1, 0, 4, -3, 1); var translation = Vector3.Zero;
        if (operation == 1) { h.Pattern.Origin = new Vector2(11, 0); matrix = Matrix3.RotationX(.4); }
        if (operation == 2) { h.Pattern.Origin = new Vector2(.1, .2); matrix = new Matrix3(1, .3, 0, 0, 1, 0, 3, -1.5, 1); }
        var old = h.Pattern; var expected = matrix * new Vector3(old.Origin.X, old.Origin.Y, 0) + translation;
        h.TransformBy(matrix, translation); HatchAffineNear(new Vector3(expected.X, expected.Y, 0), new Vector3(h.Pattern.Origin.X, h.Pattern.Origin.Y, 0), "Origin-specific representable map");
        var doc = new DxfDocument(); doc.Entities.Add(h); doc = HatchRelationsRoundTrip(doc, binary, $"hatch-pattern-affine-origin-{binary}-{operation}.dxf");
        HatchAffineNear(new Vector3(expected.X, expected.Y, 0), new Vector3(doc.Entities.Hatches.Single().Pattern.Origin.X, doc.Entities.Hatches.Single().Pattern.Origin.Y, 0), "Origin packet survives reload");
    }
    private static void HatchPatternAffineMixed(bool binary)
    {
        var doc = HatchRelationsLoad(HatchRelationsRaw(DxfVersion.AutoCad2018, binary)); var expected = new Dictionary<string, HatchBoundaryPath.Spline>();
        foreach (var h in doc.Entities.Hatches)
        {
            var spline = HatchRelationEdge(h); if (!spline.IsRational) spline.ControlPoints[0] = new Vector3(spline.ControlPoints[0].X, spline.ControlPoints[0].Y, -2.5);
            expected[HatchRelationName(h)] = (HatchBoundaryPath.Spline)spline.Clone(); h.Pattern = HatchPatternAffineExample();
            h.TransformBy(HatchPatternAffineMatrix(3), new Vector3(7, -11, 0));
        }
        void Verify(DxfDocument drawing)
        {
            foreach (var h in drawing.Entities.Hatches)
            {
                var before = expected[HatchRelationName(h)]; var after = HatchRelationEdge(h);
                Equal(before.Degree, after.Degree, "Pattern mixed spline degree"); Equal(before.IsRational, after.IsRational, "Pattern mixed rational flag"); Equal(before.IsPeriodic, after.IsPeriodic, "Pattern mixed periodic flag");
                Check(before.Knots.SequenceEqual(after.Knots), "Pattern mixed stored knots"); Check(before.ControlPoints.Select(p => p.Z).SequenceEqual(after.ControlPoints.Select(p => p.Z)), "Pattern mixed stored weights");
                for (int i = 0; i < before.ControlPoints.Length; i++) HatchAffineNear(HatchPatternAffineMatrix(3) * new Vector3(before.ControlPoints[i].X, before.ControlPoints[i].Y, 0) + new Vector3(7, -11, 0), HatchAffineWorld(h, new Vector2(after.ControlPoints[i].X, after.ControlPoints[i].Y)), "Pattern mixed control");
                for (int i = 0; i < before.FitPoints.Count; i++) HatchAffineNear(HatchPatternAffineMatrix(3) * new Vector3(before.FitPoints[i].X, before.FitPoints[i].Y, 0) + new Vector3(7, -11, 0), HatchAffineWorld(h, after.FitPoints[i]), "Pattern mixed fit");
            }
        }
        Verify(doc); for (int cycle = 0; cycle < 3; cycle++) { doc = HatchRelationsRoundTrip(doc, cycle == 1 ? !binary : binary, cycle == 2 ? $"hatch-pattern-affine-mixed-{binary}.dxf" : null); Verify(doc); }
    }
    private static void HatchPatternAffineShared()
    {
        var pattern = HatchPatternAffineExample(); var snapshot = (HatchPattern)pattern.Clone(); var other = HatchPatternAffineHatch(pattern);
        var source = new Line(Vector3.Zero, new Vector3(2, 3, 0)); var h = new Hatch(pattern, new[] { new HatchBoundaryPath(new EntityObject[] { source }) }, true); var doc = new DxfDocument(); doc.Entities.Add(h); doc.Entities.Add(other);
        var definitions = pattern.LineDefinitions.ToArray(); h.TransformBy(HatchPatternAffineMatrix(3), new Vector3(7, -11, 0));
        Check(ReferenceEquals(pattern, other.Pattern) && !ReferenceEquals(pattern, h.Pattern), "Shared pattern ownership separated"); Check(definitions.SequenceEqual(pattern.LineDefinitions), "Shared line objects retained"); HatchPatternAffineEqual(snapshot, pattern);
        Check(!h.Associative && !source.Reactors.Contains(h), "Success unlinks source association"); Equal(Vector3.Zero, source.StartPoint, "Source geometry retained"); Check(source.Owner != null, "Source entity remains in document");
        var clone = (Hatch)h.Clone(); clone.Pattern.LineDefinitions[0].DashPattern[0] = 101; Check(h.Pattern.LineDefinitions[0].DashPattern[0] != 101, "Cloned transformed pattern remains independent");
    }
    private static void HatchPatternAffineIdentity()
    {
        var pattern = HatchPatternAffineExample(); var snapshot = (HatchPattern)pattern.Clone(); var h = HatchPatternAffineHatch(pattern); var path = h.BoundaryPaths.Single(); var definitions = pattern.LineDefinitions.ToArray();
        h.TransformBy(Matrix3.Identity, Vector3.Zero); Check(ReferenceEquals(pattern, h.Pattern) && ReferenceEquals(path, h.BoundaryPaths.Single()) && definitions.SequenceEqual(pattern.LineDefinitions), "Exact identity retains stored objects"); HatchPatternAffineEqual(snapshot, pattern);
    }
    private static void HatchPatternAffineRepeated()
    {
        var h = HatchPatternAffineHatch(HatchPatternAffineExample()); h.Normal = new Vector3(1, 2, 3); h.Elevation = 4;
        var expected = HatchPatternSamples(h.Pattern, h.Pattern.LineDefinitions.Single()).Select(p => HatchAffineWorld(h, p)).ToArray();
        foreach (int operation in new[] { 3, 4, 2, 1 })
        {
            var matrix = HatchPatternAffineMatrix(operation); var translation = new Vector3(7, -11, 0); expected = expected.Select(p => matrix * p + translation).ToArray(); h.TransformBy(matrix, translation);
            var observed = HatchPatternSamples(h.Pattern, h.Pattern.LineDefinitions.Single()); for (int i = 0; i < observed.Length; i++) HatchAffineNear(expected[i], HatchAffineWorld(h, observed[i]), "Repeated phase/dash mapping");
        }
    }
}
