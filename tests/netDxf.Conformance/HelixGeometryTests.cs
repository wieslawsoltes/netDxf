using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHelixGeometryTests()
    {
        foreach (int shape in Enumerable.Range(0, 6))
            foreach (bool right in new[] { false, true })
                foreach (int pose in Enumerable.Range(0, 3))
                {
                    int s = shape, p = pose; bool r = right;
                    Run($"helix/authoring/{s}/{r}/{p}", () => HelixAuthoring(s, r, p));
                }
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
            foreach (bool binary in new[] { false, true })
                foreach (int shape in Enumerable.Range(0, 6))
                {
                    DxfVersion v = version; bool b = binary; int s = shape;
                    Run($"helix/authoring/wire/{v}/{b}/{s}", () => HelixAuthoringWire(v, b, s));
                }
        foreach (DxfVersion version in SupportedVersions.Where(v => v >= DxfVersion.AutoCad2007))
            foreach (bool binary in new[] { false, true })
            {
                DxfVersion v = version; bool b = binary;
                Run($"helix/authoring/zero-radius-phase/{v}/{b}", () => HelixZeroRadiusPhase(v, b));
            }
        Run("helix/authoring/budgets-and-invalid", HelixAuthoringInvalid);
        Run("helix/authoring/regenerate-isolation", HelixAuthoringRegenerate);
        Run("helix/authoring/error-bound-extremes", HelixBoundExtremes);
    }

    private static Helix AuthoredHelix(int shape, bool right, int pose, double tolerance = 1.0e-5)
    {
        Matrix3 matrix = pose == 0 ? Matrix3.Identity : Matrix3.RotationY(0.45) * Matrix3.RotationX(-0.3);
        Vector3 translation = pose == 2 ? new Vector3(100, -200, 30) : Vector3.Zero;
        double initial = shape == 4 ? 0 : 5, final = shape == 0 ? 5 : shape == 5 ? 0 : 2;
        double pitch = shape == 2 ? 0 : shape == 3 ? -1.5 : 1.5;
        return Helix.Create(translation, matrix * new Vector3(initial, 0, 0) + translation,
            matrix * new Vector3(0, 0, 3), final, 2.25, pitch, right, tolerance);
    }

    private static Vector3 CubicSample(Helix helix, double parameter)
    {
        int segments = helix.ControlPoints.Length / 4;
        int span = Math.Min(segments - 1, (int)(parameter * segments));
        double t = parameter * segments - span, c = 1 - t;
        int i = span * 4; var p = helix.ControlPoints;
        return c*c*c*p[i] + 3*c*c*t*p[i+1] + 3*c*t*t*p[i+2] + t*t*t*p[i+3];
    }

    private static void HelixAuthoring(int shape, bool right, int pose)
    {
        var helix = AuthoredHelix(shape, right, pose);
        int segments = helix.ControlPoints.Length / 4;
        Check(segments >= 9 && segments <= 65536, "Authoring segment budget");
        Equal(4 * (segments + 1), helix.Knots.Length, "Authored cubic knot count");
        double bound = helix.GetApproximationErrorBound(segments);
        Check(bound > 0 && bound <= 1e-5, "Selected approximation bound exceeds tolerance.");
        for (int i = 0; i < helix.Knots.Length; ++i) Near((i / 4) / (double)segments, helix.Knots[i], "Hermite span knots");
        for (int i = 0; i <= 250; ++i)
        {
            double t = i / 250.0;
            Vector3 expected = helix.EvaluateDefinition(t), actual = CubicSample(helix, t);
            Check((expected - actual).Modulus() <= bound + 1e-10, "Authored spline violates analytic error bound.");
            if (i > 0 && i < 250)
            {
                double delta = 1e-6;
                Vector3 derivative = (helix.EvaluateDefinition(t + delta) - helix.EvaluateDefinition(t - delta)) / (2 * delta);
                Check((derivative - helix.EvaluateDefinitionDerivative(t)).Modulus() < 2e-7, "Analytic derivative differs from independent centered difference.");
            }
        }
        NearFitVector(helix.StartPoint, helix.EvaluateDefinition(0), "Analytic start");
        NearFitVector(helix.EvaluateDefinitionDerivative(0), helix.StartTangent!.Value, "Stored start tangent");
        NearFitVector(helix.EvaluateDefinitionDerivative(1), helix.EndTangent!.Value, "Stored end tangent");
        Vector3 axis = Vector3.Normalize(helix.AxisVector), endOffset = helix.EvaluateDefinition(1) - helix.AxisBasePoint;
        Near(helix.Turns * helix.TurnHeight, Vector3.DotProduct(axis, endOffset), "Analytic signed total height");
        Near(helix.Radius, (endOffset - axis * Vector3.DotProduct(axis, endOffset)).Modulus(), "Analytic terminal radius");
        var transformed = (Helix)helix.Clone();
        Matrix3 reflection = Matrix3.Reflection(Vector3.UnitX) * Matrix3.RotationZ(0.5) * Matrix3.Scale(2);
        Vector3 move = new(7, 8, 9); transformed.TransformBy(reflection, move);
        // A zero-radius start derives its otherwise unencoded phase from
        // the retained spline tangent, including after reflections.
        foreach (double t in new[] { 0.0, 0.123, 0.875, 1.0 })
        {
            NearFitVector(reflection * helix.EvaluateDefinition(t) + move, transformed.EvaluateDefinition(t), "Transformed analytic definition");
            NearFitVector(reflection * helix.EvaluateDefinitionDerivative(t), transformed.EvaluateDefinitionDerivative(t), "Transformed analytic derivative");
        }
    }

    private static void HelixAuthoringWire(DxfVersion version, bool binary, int shape)
    {
        var helix = AuthoredHelix(shape, shape != 1, 0); var doc = new DxfDocument(version);
        doc.Entities.Add(helix); doc.Entities.Add((Helix)helix.Clone());
        using var output = new MemoryStream(); Check(doc.Save(output, binary), "Authored HELIX save failed.");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"helix-authored-{version}-{binary}-{shape}.dxf"), output.ToArray());
        output.Position = 0; var read = DxfDocument.Load(output)!;
        foreach (Helix actual in read.Entities.Helices)
        {
            Check(helix.ControlPoints.SequenceEqual(actual.ControlPoints) && helix.Knots.SequenceEqual(actual.Knots), "Authored HELIX changed in transport.");
            for (int i = 0; i <= 100; ++i) NearFitVector(helix.EvaluateDefinition(i / 100.0), actual.EvaluateDefinition(i / 100.0), "Transported analytic definition");
        }
    }

    private static void HelixAuthoringInvalid()
    {
        foreach (double t in new[] { -1.0, 2.0, double.NaN, double.PositiveInfinity })
        {
            var h = AuthoredHelix(1, true, 0);
            Throws<ArgumentOutOfRangeException>(() => h.EvaluateDefinition(t));
            Throws<ArgumentOutOfRangeException>(() => h.EvaluateDefinitionDerivative(t));
        }
        foreach (double tolerance in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity })
            Throws<ArgumentOutOfRangeException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 1, 1, 1, true, tolerance));
        foreach (int budget in new[] { -1, 0, 1048577, int.MaxValue })
            Throws<ArgumentOutOfRangeException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 1, 1, 1, true, 1e-5, budget));
        Throws<ArgumentException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 1, 100, 1, true, 1e-5, 8));
        Throws<ArgumentException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 1, 1, 1, true, 1e-20, 8));
        Throws<ArgumentException>(() => Helix.Create(Vector3.Zero, new Vector3(1, 0, 1), Vector3.UnitZ, 1, 1, 1));
        Throws<ArgumentOutOfRangeException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.Zero, 1, 1, 1));
        Throws<ArgumentOutOfRangeException>(() => Helix.Create(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 1, double.MaxValue, 1));
        var h2 = AuthoredHelix(0, true, 0); h2.StartPoint += Vector3.UnitZ;
        Throws<ArgumentException>(() => h2.WithRegeneratedSpline());
        Throws<ArgumentOutOfRangeException>(() => h2.GetApproximationErrorBound(0));
    }

    private static void HelixAuthoringRegenerate()
    {
        var h = NewHelixFixture(); var original = h.ControlPoints.ToArray(); h.Layer = new Layer("Coil");
        var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(h);
        var regenerated = h.WithRegeneratedSpline(1e-6);
        Check(h.ControlPoints.SequenceEqual(original), "Regeneration mutated source curve.");
        Check(!regenerated.ControlPoints.SequenceEqual(original), "Regeneration did not replace edited source curve.");
        Check(regenerated.Handle == null && regenerated.Owner == null, "Regeneration retained database identity.");
        Equal(h.Radius, regenerated.Radius, "Regenerated radius"); Equal(h.Constraint, regenerated.Constraint, "Regenerated constraint");
        Equal(h.AxisVector, regenerated.AxisVector, "Regenerated axis magnitude"); Equal(h.MajorReleaseNumber, regenerated.MajorReleaseNumber, "Regenerated class version");
        Equal(h.Layer.Name, regenerated.Layer.Name, "Regenerated layer");
        regenerated.XData["HELIX_TEST"].XDataRecord.Clear(); Check(h.XData["HELIX_TEST"].XDataRecord.Count == 1, "Regeneration aliases XData.");
        Check(regenerated.GetApproximationErrorBound(regenerated.ControlPoints.Length / 4) <= 1e-6, "Regeneration error bound");
        var straight = Helix.Create(Vector3.Zero, Vector3.Zero, Vector3.UnitZ, 0, 3, 2);
        Equal(4, straight.ControlPoints.Length, "Zero-radius linear definition should require one cubic");
        NearFitVector(new Vector3(0, 0, 3), straight.EvaluateDefinition(0.5), "Zero-radius definition");
    }

    private static void HelixZeroRadiusPhase(DxfVersion version, bool binary)
    {
        var source = AuthoredHelix(4, false, 2);
        source.TransformBy(Matrix3.Reflection(Vector3.UnitX) * Matrix3.RotationZ(0.8), new Vector3(11, 19, -23));
        var regenerated = source.WithRegeneratedSpline(1e-6);
        for (int i = 0; i <= 100; ++i)
        {
            double t = i / 100.0;
            NearFitVector(source.EvaluateDefinition(t), regenerated.EvaluateDefinition(t), "Zero-radius regeneration phase");
            Check((CubicSample(regenerated, t) - source.EvaluateDefinition(t)).Modulus() < 1e-6 + 1e-10, "Zero-radius regenerated curve lost phase.");
        }
        var doc = new DxfDocument(version); doc.Entities.Add(source); doc.Entities.Add(regenerated);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "Zero-radius phase save failed.");
        stream.Position = 0; var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Zero-radius phase load failed.");
        Equal(2, loaded.Entities.Helices.Count(), "Zero-radius phase entities lost");
        foreach (var h in loaded.Entities.Helices)
            foreach (double t in new[] { 0.0, 0.125, 0.375, 0.75, 1.0 })
                NearFitVector(source.EvaluateDefinition(t), h.EvaluateDefinition(t), "Transported zero-radius phase");
        var absent = (Helix)source.Clone(); absent.StartTangent = null;
        // An absent phase has a deterministic local convention, not an
        // invented promise about an application's hidden zero-radius azimuth.
        NearFitVector(absent.EvaluateDefinition(0.2), absent.WithRegeneratedSpline().EvaluateDefinition(0.2), "Absent phase convention");
    }

    private static void HelixBoundExtremes()
    {
        var h = NewHelixFixture(); h.AxisBasePoint = Vector3.Zero; h.StartPoint = new Vector3(1e308, 0, 0);
        h.AxisVector = Vector3.UnitZ; h.Radius = 1e308; h.Turns = 1e-100;
        double tiny = h.GetApproximationErrorBound(1);
        Check(double.IsFinite(tiny) && tiny > 0 && tiny < 1e-80, "Large-radius small-angle bound overflowed or underflowed.");
        // A changing radius has a third-order angular contribution to the
        // fourth derivative. It must not be mistaken for the cylindrical bound.
        h.StartPoint = Vector3.Zero;
        double taper = h.GetApproximationErrorBound(1);
        Check(double.IsFinite(taper) && taper > 1e7 && taper < 1e10, "Taper term was lost or overflowed.");
        h.Turns = 1e100;
        Check(double.IsPositiveInfinity(h.GetApproximationErrorBound(1)), "Unrepresentable error bound should be positive infinity, not NaN.");
        foreach (double axisScale in new[] { double.Epsilon, 1e-300, 1e308 })
        {
            var scaled = Helix.Create(Vector3.Zero, Vector3.UnitX, new Vector3(0, 0, axisScale), 1, 1, 1);
            NearFitVector(new Vector3(-1, 0, 0.5), scaled.EvaluateDefinition(0.5), "Finite extreme axis scale");
            SameDoubleBits(axisScale, scaled.AxisVector.Z, "Extreme axis magnitude retained");
        }
        var usual = AuthoredHelix(1, true, 0);
        Near(usual.GetApproximationErrorBound(10) / 16, usual.GetApproximationErrorBound(20), "Fourth-order error bound scaling");
    }
}
