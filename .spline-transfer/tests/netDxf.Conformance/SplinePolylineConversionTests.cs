// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Spline SplineConversionSubject(int kind, int normal)
    {
        var controls = ProjectionControls.ToArray();
        if (kind == 2) controls[^1] = controls[0];
        Spline spline = kind == 4 ? new Spline(controls) :
            new Spline(controls, new[] { 1.0, .5, 2.0, 1.0, 1.5 }, (short)(kind == 0 ? 1 : kind == 1 ? 2 : 3), kind == 3);
        spline.Normal = ProjectionNormals[normal]; ProjectionAppearance(spline); return spline;
    }

    private static EntityObject SplineConversionOutput(Spline source, int mode, int count) =>
        mode == 0 ? source.ToPolyline3D(count) : ProjectCurve(source, count, mode == 1 ? null : -7.5);

    private static void SplineConversionModel(int kind, int normal, int mode, int owner)
    {
        var source = SplineConversionSubject(kind, normal); var document = new DxfDocument();
        if (owner == 1) new Block("DETACHED_SPLINE_CONVERSION").Entities.Add(source);
        if (owner == 2) document.Entities.Add(source);
        var controls = source.ControlPoints.ToArray(); var weights = source.Weights.ToArray(); var knots = source.Knots.ToArray();
        var fit = source.FitPoints.ToArray(); var handle = source.Handle; var oldOwner = source.Owner; var proxy = source.ProxyGraphics!;
        var samples = source.PolygonalVertexes(17); var result = SplineConversionOutput(source, mode, 17);
        if (result is Polyline2D p2)
        {
            ProjectionPointsCheck(samples, source.Normal, p2, mode == 1 ? 0 : -7.5);
            Equal(source.IsClosed || source.IsClosedPeriodic, p2.IsClosed, "Projected closure");
        }
        else
        {
            var p3 = (Polyline3D)result;
            Equal(samples.Count, p3.Vertexes.Count, "WCS sample count");
            for (int i = 0; i < samples.Count; i++)
                Check(DirectionBits(samples[i]).SequenceEqual(DirectionBits(p3.Vertexes[i])), "WCS sample components changed");
            Equal(source.IsClosed || source.IsClosedPeriodic, p3.IsClosed, "3D closure");
            Check(p3.SmoothType == PolylineSmoothType.NoSmooth, "Converted spline sampled twice");
            Check(DirectionBits(source.Normal).SequenceEqual(DirectionBits(p3.Normal)), "3D normal changed");
        }
        ProjectionAppearanceCheck(source, result);
        Check(controls.SequenceEqual(source.ControlPoints) && weights.SequenceEqual(source.Weights)
            && knots.SequenceEqual(source.Knots) && fit.SequenceEqual(source.FitPoints), "Conversion changed source definition");
        Check(proxy.SequenceEqual(source.ProxyGraphics!) && ReferenceEquals(oldOwner, source.Owner), "Conversion changed source graph/cache");
        Equal(handle, source.Handle, "Source handle changed");
        var sibling = SplineConversionOutput(source, mode, 17);
        Check(!ReferenceEquals(result.Layer, sibling.Layer) && !ReferenceEquals(result.Color, sibling.Color)
            && !ReferenceEquals(result.XData["CURVE_PROJECTION"], sibling.XData["CURVE_PROJECTION"]), "Sibling conversion aliases metadata");
    }

    private static void SplineConversionReject(int mode, int fault)
    {
        var source = SplineConversionSubject(1, 4);
        if (fault == 0) source.ControlPoints[2] = new Vector3(double.NaN, 1, 2);
        if (fault == 1) source.ControlPoints[4] = new Vector3(1, double.PositiveInfinity, 2);
        if (fault == 2) source.Weights[1] = double.NaN;
        if (fault == 3) source.Weights[3] = double.PositiveInfinity;
        if (fault == 4) source.Knots[2] = double.NaN;
        if (fault == 5) source.Knots[^1] = double.PositiveInfinity;
        if (fault == 6) source.Knots[3] = -1;
        if (fault == 7) source.StartTangent = new Vector3(1, 2, double.NaN);
        if (fault == 8) source.EndTangent = new Vector3(1, double.NegativeInfinity, 2);
        if (fault == 9) typeof(EntityObject).GetField("normal", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(source, Vector3.Zero);
        if (fault == 10) source.PersistentReactors.Add(new Line());
        if (fault == 11) source.XData["CURVE_PROJECTION"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "ABC"));
        string before = string.Join("|", source.ControlPoints.SelectMany(DirectionBits)) + ":" +
            string.Join("|", source.Weights.Concat(source.Knots).Select(BitConverter.DoubleToInt64Bits));
        var proxy = source.ProxyGraphics!; bool rejected = false;
        try { SplineConversionOutput(source, mode, 17); }
        catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NotSupportedException) { rejected = true; }
        Check(rejected, "Malformed or associated spline was silently converted");
        Equal(before, string.Join("|", source.ControlPoints.SelectMany(DirectionBits)) + ":" +
            string.Join("|", source.Weights.Concat(source.Knots).Select(BitConverter.DoubleToInt64Bits)), "Rejected conversion mutated definition");
        Check(proxy.SequenceEqual(source.ProxyGraphics!), "Rejected conversion mutated proxy");
    }

    private static void SplineConversionPrecision(int mode, int precision)
    {
        var source = SplineConversionSubject(1, 0);
        if (precision > 1000000)
        {
            var limit = typeof(Spline).GetField("MaximumConvertedVertices", BindingFlags.Public | BindingFlags.Static);
            Check(limit != null && (int)limit.GetRawConstantValue()! == 1000000, "Conversion must bound generated allocation");
        }
        Throws<ArgumentOutOfRangeException>(() => SplineConversionOutput(source, mode, precision));
    }

    private static void SplineConversionWire(int normal, int plane, DxfVersion version, bool binary)
    {
        // A rational quadratic Bezier segment with exact analytic expectations.
        var source = new Spline(new[] { new Vector3(2, 3, 5), new Vector3(-4, 6, 7), new Vector3(8, -2, 9) },
            new[] { 1.0, .5, 2.0 }, (short)2, false) { Normal = ProjectionNormals[normal] };
        ProjectionAppearance(source);
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        EntityObject target = plane == 2 ? source.ToPolyline3D(9) : ProjectCurve(source, 9, plane == 0 ? null : -7.5);
        var document = new DxfDocument(version); document.Entities.Add(target);
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Spline conversion save failed");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-conversion-{normal}-{plane}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!;
        EntityObject result = plane == 2 ? loaded.Entities.Polylines3D.Single() : loaded.Entities.Polylines2D.Single();
        Check(!result.IsVisible && result.XData.ContainsAppId("CURVE_PROJECTION"), "Spline wire metadata lost");
        if (result is Polyline2D p2) ProjectionPointsCheck(source.PolygonalVertexes(9), source.Normal, p2, plane == 0 ? 0 : -7.5);
        else
        {
            var p3 = (Polyline3D)result; var expected = source.PolygonalVertexes(9);
            Check(p3.Vertexes.Count == expected.Count && !p3.IsClosed, "3D wire topology");
            for (int i = 0; i < expected.Count; i++) Check(Vector3.Distance(p3.Vertexes[i], expected[i]) < 2e-12, "3D wire geometry");
        }
    }

    private static void RegisterSplinePolylineConversionTests()
    {
        for (int kind = 0; kind < 5; kind++) for (int normal = 0; normal < ProjectionNormals.Length; normal++)
            for (int mode = 0; mode < 3; mode++) for (int owner = 0; owner < 3; owner++)
            {
                int k=kind,n=normal,m=mode,o=owner;
                Run($"spline-conversion/model/{k}/{n}/{m}/{o}", () => SplineConversionModel(k,n,m,o));
            }
        for (int mode=0; mode<3; mode++)
        {
            int m=mode;
            for (int fault=0; fault<12; fault++)
            { int f=fault; Run($"spline-conversion/reject/{m}/{f}", () => SplineConversionReject(m,f)); }
            foreach (int precision in new[] { -1, 0, 1, 1000001, int.MaxValue })
                Run($"spline-conversion/precision/{m}/{precision}", () => SplineConversionPrecision(m,precision));
        }
        for (int i=0; i<3; i++)
        {
            int n=i; double bad=new[] { double.NaN,double.PositiveInfinity,double.NegativeInfinity }[i];
            Run($"spline-conversion/elevation/{n}", () => Throws<ArgumentOutOfRangeException>(() => ProjectCurve(SplineConversionSubject(1,0),9,bad)));
        }
        for (int normal=0;normal<ProjectionNormals.Length;normal++) for (int plane=0;plane<3;plane++)
            foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false,true })
            {
                int n=normal,p=plane;
                Run($"spline-conversion/wire/{n}/{p}/{version}/{binary}", () => SplineConversionWire(n,p,version,binary));
            }
    }
}
