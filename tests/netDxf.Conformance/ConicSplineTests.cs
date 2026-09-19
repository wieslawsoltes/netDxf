// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Vector3[] ConicNormals = { Vector3.UnitZ, -Vector3.UnitZ, new Vector3(2, -3, 6) };

    private static EntityObject ConicSubject(int kind, int normal)
    {
        var center = new Vector3(7, -11, 3);
        EntityObject result = kind switch
        {
            0 => new Circle(center, 4),
            1 => new Arc(center, 4, 0, 90),
            2 => new Arc(center, 4, 270, 90),
            3 => new Arc(center, 4, 22, 297),
            4 => new Ellipse(center, 12, 4) { Rotation = 31 },
            5 => new Ellipse(center, 12, 4) { Rotation = 37, StartAngle = 0, EndAngle = 90 },
            6 => new Ellipse(center, 12, 4) { Rotation = 37, StartAngle = 270, EndAngle = 90 },
            7 => new Ellipse(center, 12, 4) { Rotation = 37, StartAngle = 25, EndAngle = 215 },
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        result.Normal = ConicNormals[normal]; ProjectionAppearance(result); return result;
    }

    private static Spline ConvertConic(EntityObject source)
    {
        Type? extension = typeof(Spline).Assembly.GetType("netDxf.Entities.ConicSplineExtensions");
        if (extension == null) throw new MissingMethodException("Conic-to-SPLINE API is absent");
        var method = extension!.GetMethod("ToSpline", new[] { source.GetType() });
        if (method == null) throw new MissingMethodException("Typed conic overload is absent");
        try { return (Spline)method!.Invoke(null, new object[] { source })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        { ExceptionDispatchInfo.Capture(ex.InnerException).Throw(); throw; }
    }

    private static void CheckConicGeometry(EntityObject source, Spline spline)
    {
        bool closed = source is Circle || source is Ellipse full && full.IsFullEllipse;
        Equal((short)2, spline.Degree, "rational conic degree");
        Equal(closed, spline.IsClosed, "conic closure"); Check(!spline.IsClosedPeriodic, "clamped conic is not periodic");
        int spans = (spline.ControlPoints.Length - 1) / 2;
        Check(spans >= 1 && spans <= 4 && spline.ControlPoints.Length == 2 * spans + 1, "bounded conic control count");
        Equal(spline.ControlPoints.Length + 3, spline.Knots.Length, "conic knot count");
        Check(spline.Weights.All(w => double.IsFinite(w) && w > 0), "conic positive weights");
        if (closed) Check(DirectionBits(spline.ControlPoints[0]).SequenceEqual(DirectionBits(spline.ControlPoints[^1])), "exact closed seam");
        Vector3 center; double a, b, rotation, start, end;
        if (source is Ellipse e) { center = e.Center; a = e.MajorAxis * .5; b = e.MinorAxis * .5; rotation = e.Rotation; start = e.StartAngle; end = e.EndAngle; }
        else if (source is Circle c) { center = c.Center; a = b = c.Radius; rotation = start = end = 0; }
        else { var arc = (Arc)source; center = arc.Center; a = b = arc.Radius; rotation = 0; start = arc.StartAngle; end = arc.EndAngle; }
        Matrix3 inverse = MathHelper.ArbitraryAxis(source.Normal).Transpose();
        double cr = Math.Cos(rotation * MathHelper.DegToRad), sr = Math.Sin(rotation * MathHelper.DegToRad);
        double previous = double.NaN;
        var samples = spline.PolygonalVertexes(65);
        foreach (Vector3 point in samples)
        {
            Vector3 local = inverse * (point - center);
            double x = (local.X * cr + local.Y * sr) / a, y = (local.Y * cr - local.X * sr) / b;
            Check(Math.Abs(x * x + y * y - 1) <= 2e-12 && Math.Abs(local.Z) <= 2e-12, "spline left analytic conic plane/locus");
            double parameter = Math.Atan2(y, x);
            if (!double.IsNaN(previous))
            {
                double advance = parameter - previous;
                if (advance < -Math.PI) advance += MathHelper.TwoPI;
                Check(advance > 0 && advance < Math.PI, "spline traversal changed or stalled");
            }
            previous = parameter;
        }
        Vector3 Endpoint(double polar)
        {
            Vector2 local = source is Ellipse ellipse ? ellipse.PolarCoordinateRelativeToCenter(polar) :
                new Vector2(a * Math.Cos(polar * MathHelper.DegToRad), a * Math.Sin(polar * MathHelper.DegToRad));
            return center + inverse.Transpose() * new Vector3(local.X * cr - local.Y * sr, local.X * sr + local.Y * cr, 0);
        }
        Check(Vector3.Distance(Endpoint(closed ? 0 : start), samples[0]) < 3e-12, "conic start point");
        if (!closed) Check(Vector3.Distance(Endpoint(end), samples[^1]) < 3e-12, "conic end point");
    }

    private static void ConicModel(int kind, int normal, int owner)
    {
        var source = ConicSubject(kind, normal); var doc = new DxfDocument();
        if (owner == 1) new Block("CONIC_DETACHED").Entities.Add(source);
        if (owner == 2) doc.Entities.Add(source);
        var handle = source.Handle; var oldOwner = source.Owner; var proxy = source.ProxyGraphics!.ToArray();
        var target = ConvertConic(source); CheckConicGeometry(source, target);
        ProjectionAppearanceCheck(source, target);
        var sibling = ConvertConic(source);
        Check(!ReferenceEquals(sibling.Layer, target.Layer) && !ReferenceEquals(sibling.XData["CURVE_PROJECTION"], target.XData["CURVE_PROJECTION"]), "conic sibling aliases metadata");
        Check(proxy.SequenceEqual(source.ProxyGraphics!) && ReferenceEquals(oldOwner, source.Owner), "conic source state changed");
        Equal(handle, source.Handle, "source handle changed");
        var cloned = (Spline)sibling.Clone(); CheckConicGeometry(source, cloned);
    }

    private static void ConicWire(int kind, int normal, DxfVersion version, bool binary)
    {
        var source = ConicSubject(kind, normal);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        var target = ConvertConic(source); var doc = new DxfDocument(version); doc.Entities.Add(target);
        using var stream = new MemoryStream(); Check(doc.Save(stream, binary), "conic spline save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"conic-spline-{kind}-{normal}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var result = loaded.Entities.Splines.Single();
        CheckConicGeometry(source, result);
        Check(!result.IsVisible && result.XData.ContainsAppId("CURVE_PROJECTION"), "conic wire appearance");
        Check(target.Knots.SequenceEqual(result.Knots) && target.Weights.SequenceEqual(result.Weights), "conic knots/weights changed on wire");
        Equal(0, loaded.Objects.Validate().Count, "conic graph");
    }

    private static void RegisterConicSplineTests()
    {
        for (int k = 0; k < 8; k++) for (int n = 0; n < 3; n++)
        {
            int kind = k, normal = n;
            for (int o = 0; o < 3; o++) { int owner = o; Run($"conic-spline/model/{k}/{n}/{o}", () => ConicModel(kind, normal, owner)); }
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
                Run($"conic-spline/wire/{k}/{n}/{version}/{binary}", () => ConicWire(kind, normal, version, binary));
        }
        for (int k = 0; k < 3; k++) for (int fault = 0; fault < 6; fault++)
        {
            int kind = k, f = fault;
            Run($"conic-spline/reject/{k}/{fault}", () =>
            {
                EntityObject source = ConicSubject(kind == 2 ? 4 : kind, 0);
                if (f == 0) source.PersistentReactors.Add(new Line());
                if (f == 1) source.XData["CURVE_PROJECTION"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "ABC"));
                if (f == 2) typeof(EntityObject).GetField("normal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, Vector3.Zero);
                if (f >= 3)
                {
                    string name = f == 3 ? "center" : "thickness";
                    object value = f == 3 ? new Vector3(double.NaN, 0, 0) : f == 4 ? 1.0 : double.PositiveInfinity;
                    source.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(source, value);
                }
                var proxy = source.ProxyGraphics!.ToArray(); bool rejected = false;
                try { ConvertConic(source); }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { rejected = true; }
                Check(rejected && proxy.SequenceEqual(source.ProxyGraphics!), "invalid conic converted or changed source");
            });
        }
        foreach (Type type in new[] { typeof(Circle), typeof(Arc), typeof(Ellipse) })
            Run($"conic-spline/null/{type.Name}", () =>
            {
                Type extension = typeof(Spline).Assembly.GetType("netDxf.Entities.ConicSplineExtensions", true)!;
                try { extension.GetMethod("ToSpline", new[] { type })!.Invoke(null, new object?[] { null }); }
                catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException) { return; }
                throw new InvalidOperationException("Null conic must reject");
            });
        foreach (double radius in new[] { 1e-300, 1e-100, 1e100, 1e300 })
            Run($"conic-spline/scale/{radius:R}", () =>
            {
                var result = ConvertConic(new Circle(Vector3.Zero, radius));
                foreach (Vector3 p in result.PolygonalVertexes(65))
                    Check(Math.Abs((p.X / radius) * (p.X / radius) + (p.Y / radius) * (p.Y / radius) - 1) < 2e-13,
                        "scaled conic left unit locus");
            });
        Run("conic-spline/zero-sweep", () => Throws<NotSupportedException>(() => ConvertConic(new Arc(Vector3.Zero, 4, 25, 25))));
        Run("conic-spline/closure-ambiguity", () => Throws<NotSupportedException>(() => ConvertConic(new Arc(Vector3.Zero, 1e-20, 0, 90))));
        Run("conic-spline/nonfinite-radius", () => Throws<ArgumentException>(() => ConvertConic(new Circle(Vector3.Zero, double.NaN))));
        Run("conic-spline/unrepresentable-control", () => Throws<NotSupportedException>(() => ConvertConic(new Arc(Vector3.Zero, double.MaxValue, 45, 135))));
    }
}
