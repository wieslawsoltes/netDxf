// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly Vector3[] ProjectionNormals = { Vector3.UnitZ, -Vector3.UnitZ,
        Vector3.UnitX, Vector3.UnitY, new(1, 2, 3), new(1e-13, -2e-13, 1) };
    private static readonly Vector3[] ProjectionControls = { new(2, 3, 5), new(-4, 6, 7),
        new(8, -2, 9), new(3, 5, -1), new(1, 7, 4) };

    private static void ProjectionAppearance(EntityObject source)
    {
        source.Layer = new Layer("PROJECTION"); source.Linetype = (Linetype)Linetype.Dashed.Clone();
        source.Color = new AciColor(32, 64, 96); source.Transparency = new Transparency(35);
        source.Lineweight = Lineweight.W35; source.LinetypeScale = 2.5; source.IsVisible = false;
        source.ColorName = "Book$Ink"; source.ShadowMode = EntityShadowMode.Ignore;
        source.ProxyGraphics = new byte[] { 3, 1, 4, 1, 5 };
        var data = new XData(new ApplicationRegistry("CURVE_PROJECTION"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "retained"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData, new byte[] { 9, 2, 6 }));
        source.XData.Add(data);
    }
    private static void ProjectionAppearanceCheck(EntityObject source, EntityObject target)
    {
        Check(!target.IsVisible, "Conversion lost visibility"); Equal(source.ColorName, target.ColorName, "Named color");
        Equal(source.ShadowMode, target.ShadowMode, "Shadow mode"); Equal(source.Layer.Name, target.Layer.Name, "Layer");
        Equal(source.Linetype.Name, target.Linetype.Name, "Linetype"); Equal(source.Lineweight, target.Lineweight, "Lineweight");
        Equal(source.LinetypeScale, target.LinetypeScale, "Linetype scale"); Equal(source.Transparency.Value, target.Transparency.Value, "Transparency");
        Check(source.Color.R == target.Color.R && source.Color.G == target.Color.G && source.Color.B == target.Color.B, "Color changed");
        Check(!ReferenceEquals(source.Layer, target.Layer) && !ReferenceEquals(source.Linetype, target.Linetype)
            && !ReferenceEquals(source.Color, target.Color) && !ReferenceEquals(source.Transparency, target.Transparency), "Appearance aliases source");
        Check(target.Owner == null && target.Handle == null && target.ProxyGraphics == null, "Converted identity/cache must be detached");
        Check(target.ExtensionDictionary == null && target.PersistentReactors.Count == 0 && target.Reactors.Count == 0, "Converted graph must be detached");
        Check(target.XData.ContainsAppId("CURVE_PROJECTION"), "Conversion lost XData");
        Check(!ReferenceEquals(source.XData["CURVE_PROJECTION"], target.XData["CURVE_PROJECTION"]), "XData aliases source");
        var bytes = (byte[])target.XData["CURVE_PROJECTION"].XDataRecord[1].Value; bytes[0] = 0;
        Equal((byte)9, ((byte[])source.XData["CURVE_PROJECTION"].XDataRecord[1].Value)[0], "Binary XData aliases source");
        target.XData["CURVE_PROJECTION"].XDataRecord[0] = new XDataRecord(XDataCode.String, "changed");
        Equal("retained", (string)source.XData["CURVE_PROJECTION"].XDataRecord[0].Value, "Source XData changed");
    }
    private static Polyline2D ProjectCurve(object source, int precision, double? elevation)
    {
        if (!elevation.HasValue)
            return source is Polyline3D p ? p.ToPolyline2D(precision) : ((Spline)source).ToPolyline2D(precision);
        var method = source.GetType().GetMethod("ToPolyline2D", new[] { typeof(int), typeof(double) });
        Check(method != null, "Explicit projection elevation overload missing");
        try { return (Polyline2D)method!.Invoke(source, new object[] { precision, elevation.Value })!; }
        catch (TargetInvocationException error) when (error.InnerException != null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static Polyline3D ProjectionSubject(int normal, int smooth, bool closed)
    {
        var result = new Polyline3D(ProjectionControls, closed) { Normal = ProjectionNormals[normal],
            SmoothType = (PolylineSmoothType)smooth, LinetypeGeneration = true };
        ProjectionAppearance(result); return result;
    }
    private static void ProjectionPointsCheck(IList<Vector3> samples, Vector3 normal, Polyline2D result, double elevation)
    {
        Equal(samples.Count, result.Vertexes.Count, "Projected sample count"); Equal(elevation, result.Elevation, "Projection elevation");
        var frame = MathHelper.ArbitraryAxis(normal).Transpose();
        for (int i = 0; i < samples.Count; i++)
        {
            var expected = frame * samples[i]; var actual = result.Vertexes[i];
            Check(Math.Abs(actual.Position.X - expected.X) < 2e-12 && Math.Abs(actual.Position.Y - expected.Y) < 2e-12, "Projected coordinates");
            Check(actual.Bulge == 0 && actual.StartWidth == 0 && actual.EndWidth == 0, "Projection invented segment data");
        }
        Check(DirectionBits(normal).SequenceEqual(DirectionBits(result.Normal)), "Projection normal changed");
        Check(result.SmoothType == PolylineSmoothType.NoSmooth && result.Thickness == 0, "Sampled output must not be fitted twice");
    }
    private static void PolylineProjectionModel(int normal, int smooth, bool closed, int owner, bool explicitPlane)
    {
        var source = ProjectionSubject(normal, smooth, closed); var doc = new DxfDocument();
        if (owner == 1) new Block("DETACHED_PROJECTION").Entities.Add(source);
        if (owner == 2) doc.Entities.Add(source);
        var points = source.Vertexes.ToArray(); var handles = source.Handle; var originalOwner = source.Owner;
        var proxy = source.ProxyGraphics!; var samples = source.PolygonalVertexes(17);
        var result = ProjectCurve(source, 17, explicitPlane ? -7.5 : null);
        ProjectionPointsCheck(samples, source.Normal, result, explicitPlane ? -7.5 : 0);
        Equal(closed, result.IsClosed, "Closure"); Check(result.LinetypeGeneration, "Continuous linetype flag lost");
        ProjectionAppearanceCheck(source, result);
        Check(points.SequenceEqual(source.Vertexes) && proxy.SequenceEqual(source.ProxyGraphics!), "Source geometry or proxy changed");
        Check(ReferenceEquals(originalOwner, source.Owner), "Source owner changed"); Equal(handles, source.Handle, "Source handle changed");
    }
    private static void ProjectionWire(int normal, bool closed, int plane, DxfVersion version, bool binary)
    {
        var source = ProjectionSubject(normal, 0, closed);
        // Preserve legal per-profile metadata; no version conversion is implicit.
        if (version < DxfVersion.AutoCad2004) { source.Color = AciColor.Blue; source.Transparency = new Transparency(0); source.ColorName = null; }
        if (version < DxfVersion.AutoCad2007) source.ShadowMode = null;
        double? elevation = plane == 0 ? null : plane == 1 ? -7.5 : 12.0;
        var target = ProjectCurve(source, 17, elevation); var document = new DxfDocument(version); document.Entities.Add(target);
        using var stream = new MemoryStream(); Check(document.Save(stream, binary), "Projection save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"polyline-projection-{normal}-{closed}-{plane}-{version}-{binary}.dxf"), stream.ToArray());
        stream.Position = 0; var loaded = DxfDocument.Load(stream)!; var result = loaded.Entities.Polylines2D.Single();
        ProjectionPointsCheck(source.Vertexes, source.Normal, result, elevation ?? 0);
        Check(!result.IsVisible && result.LinetypeGeneration && result.XData.ContainsAppId("CURVE_PROJECTION"), "Projection wire metadata lost");
        Equal(closed, result.IsClosed, "Wire closure"); Equal(0, loaded.Objects.Validate().Count, "Projection graph");
    }
    private static void RegisterPolylineProjectionTests()
    {
        for (int normal = 0; normal < ProjectionNormals.Length; normal++)
        {
            int n = normal;
            foreach (int smooth in new[] { 0, 5, 6 }) foreach (bool closed in new[] { false, true })
                for (int owner = 0; owner < 3; owner++) foreach (bool plane in new[] { false, true })
                { int o = owner; Run($"polyline-projection/model/{n}/{smooth}/{closed}/{o}/{plane}", () => PolylineProjectionModel(n, smooth, closed, o, plane)); }
            foreach (bool closed in new[] { false, true }) for (int plane = 0; plane < 3; plane++)
                foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
                { int p = plane; Run($"polyline-projection/wire/{n}/{closed}/{p}/{version}/{binary}", () => ProjectionWire(n, closed, p, version, binary)); }
        }
        foreach (bool plane in new[] { false, true })
        {
            foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                for (int axis = 0; axis < 3; axis++)
                { int a = axis; Run($"polyline-projection/reject/point/{plane}/{a}/{BitConverter.DoubleToInt64Bits(bad):X16}", () =>
                    { var source = ProjectionSubject(0, 0, false); var p = source.Vertexes[4]; p[a] = bad; source.Vertexes[4] = p;
                      Throws<InvalidOperationException>(() => ProjectCurve(source, 17, plane ? 3 : null)); Check(source.ProxyGraphics!.SequenceEqual(new byte[] {3,1,4,1,5}), "Rejection mutated proxy"); }); }
            Run($"polyline-projection/reject/handle/{plane}", () =>
            { var source = ProjectionSubject(0,0,false); source.XData["CURVE_PROJECTION"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,"0"));
              Throws<NotSupportedException>(() => ProjectCurve(source,17,plane ? 3 : null)); });
            Run($"polyline-projection/reject/precision/{plane}", () => Throws<ArgumentOutOfRangeException>(() => ProjectCurve(ProjectionSubject(0,0,false),-1,plane ? 3 : null)));
            Run($"polyline-projection/reject/normal/{plane}", () =>
            { var source = ProjectionSubject(0,0,false); typeof(EntityObject).GetField("normal",BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(source,Vector3.Zero);
              Throws<InvalidOperationException>(() => ProjectCurve(source,17,plane ? 3 : null)); });
        }
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Run($"polyline-projection/reject/elevation/{BitConverter.DoubleToInt64Bits(bad):X16}", () => Throws<ArgumentOutOfRangeException>(() => ProjectCurve(ProjectionSubject(0,0,false),17,bad)));
        Run("polyline-projection/budget-before-sampling", () =>
        { var field = typeof(Polyline3D).GetField("MaximumConvertedVertices"); Check(field != null, "Conversion budget missing");
          Equal(1000000,(int)field!.GetRawConstantValue()!,"Conversion budget"); Throws<ArgumentOutOfRangeException>(() => ProjectCurve(ProjectionSubject(0,6,false),int.MaxValue,null)); });
        Run("polyline-projection/discarded-axis-overflow", () =>
        { var source = new Polyline3D(new[] { new Vector3(double.MaxValue,double.MaxValue,2) }) { Normal = new Vector3(1,1,0) };
          var result = ProjectCurve(source,0,5); Check(result.Vertexes[0].Position.X == 0 && result.Vertexes[0].Position.Y == 2,"Irrelevant normal-axis overflow rejected/contaminated projection"); });
        foreach (int count in new[] { 0, 1 }) foreach (bool closed in new[] { false,true })
            Run($"polyline-projection/degenerate/{count}/{closed}", () =>
            { var source = new Polyline3D(ProjectionControls.Take(count),closed); var result = ProjectCurve(source,0,12); Equal(count,result.Vertexes.Count,"Degenerate count"); Equal(12.0,result.Elevation,"Empty elevation"); });
        foreach (bool binary in new[] {false,true}) foreach (bool decorated in new[] {false,true})
            Run($"polyline-projection/stored/{binary}/{decorated}", () =>
            { var source = ProjectionSubject(0,0,false); var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.Entities.Add(source);
              using var stream = new MemoryStream(); doc.Save(stream,binary); stream.Position=0; var loaded=DxfDocument.Load(stream)!.Entities.Polylines3D.Single();
              if (decorated) { var data=new XData(new ApplicationRegistry("CHILD_PROJECTION")); data.XDataRecord.Add(new XDataRecord(XDataCode.String,"keep")); loaded.VertexRecords[0].XData.Add(data);
                Throws<NotSupportedException>(()=>ProjectCurve(loaded,17,null)); }
              else { var result=ProjectCurve(loaded,17,4); Check(!result.IsVisible && result.XData.ContainsAppId("CURVE_PROJECTION"),"Loaded metadata lost"); } });
    }
}
