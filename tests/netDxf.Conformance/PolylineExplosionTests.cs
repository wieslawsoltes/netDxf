// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Polyline3D PolylineExplosionSubject(int smooth, bool closed)
    {
        var result = (Polyline3D)LegacyAffineSubject(false, smooth); result.IsClosed = closed;
        result.ColorName = "Palette$Ink"; result.ShadowMode = EntityShadowMode.Ignore;
        // Seed the cache after the final geometry edit: opening a previously closed
        // polyline correctly invalidates the cache supplied by LegacyAffineSubject.
        // The explosion operation below must still preserve this nonempty payload.
        result.ProxyGraphics = new byte[] { 2,3,5,7 };
        return result;
    }
    private static void PolylineExplosionModel(int smooth, bool closed, int ownership)
    {
        short old = Polyline3D.DefaultSplineSegs;
        try
        {
            Polyline3D.DefaultSplineSegs = 3;
            var subject = PolylineExplosionSubject(smooth, closed); var document = new DxfDocument(); document.DrawingVariables.SplineSegs = 7;
            if (ownership == 1) new Block("DETACHED").Entities.Add(subject);
            else if (ownership == 2) document.Entities.Add(subject);
            else if (ownership == 3) { var block = new Block("REGISTERED"); block.Entities.Add(subject); document.Blocks.Add(block); }
            int precision = (ownership >= 2 ? 7 : 3) * (closed ? subject.Vertexes.Count : subject.Vertexes.Count - 1);
            var expected = subject.PolygonalVertexes(precision); var before = LegacyAffineBits(subject); var proxy = subject.ProxyGraphics!;
            var owner = subject.Owner; string handle = subject.Handle;
            var lines = subject.Explode().Cast<Line>().ToArray();
            Equal(closed ? expected.Count : expected.Count-1, lines.Length, "Exploded segment count");
            for (int i=0;i<lines.Length;i++)
            {
                var line = lines[i];
                Check(DirectionBits(expected[i]).SequenceEqual(DirectionBits(line.StartPoint)),"Start coordinates changed");
                Check(DirectionBits(expected[(i+1)%expected.Count]).SequenceEqual(DirectionBits(line.EndPoint)),"End coordinates changed");
                Check(!line.IsVisible,"Visibility lost"); Equal(subject.ColorName,line.ColorName,"Named color lost"); Equal(subject.ShadowMode,line.ShadowMode,"Shadow mode lost");
                Equal(subject.Layer.Name,line.Layer.Name,"Layer name lost"); Equal(subject.Color.Index,line.Color.Index,"Color lost");
                Equal(subject.LinetypeScale,line.LinetypeScale,"Line scale lost"); Equal(subject.Lineweight,line.Lineweight,"Lineweight lost");
                Check(line.Owner == null && line.Handle == null && line.ProxyGraphics == null,"New line carries old identity/cache");
                Check(!ReferenceEquals(subject.Layer,line.Layer) && !ReferenceEquals(subject.Color,line.Color),"Source appearance aliases");
                Check(line.XData.ContainsAppId("LEGACY_AFFINE"),"Parent XData lost");
                Check(!ReferenceEquals(subject.XData["LEGACY_AFFINE"],line.XData["LEGACY_AFFINE"]),"Source XData aliases");
                if(i>0)Check(!ReferenceEquals(lines[0].Layer,line.Layer) && !ReferenceEquals(lines[0].XData["LEGACY_AFFINE"],line.XData["LEGACY_AFFINE"]),"Sibling aliases");
            }
            lines[0].XData["LEGACY_AFFINE"].XDataRecord[0]=new XDataRecord(XDataCode.String,"changed");
            Equal("unchanged",(string)subject.XData["LEGACY_AFFINE"].XDataRecord[0].Value,"Source XData changed");
            Check(before.SequenceEqual(LegacyAffineBits(subject)) && proxy.SequenceEqual(subject.ProxyGraphics!),"Source geometry/cache changed");
            Check(ReferenceEquals(owner,subject.Owner),"Source ownership changed"); Equal(handle,subject.Handle,"Source identity changed");
        }
        finally { Polyline3D.DefaultSplineSegs = old; }
    }
    private static void PolylineExplosionWire(bool closed,DxfVersion version,bool binary)
    {
        var subject=PolylineExplosionSubject(0,closed);subject.ColorName=null;subject.ShadowMode=null;
        var document=new DxfDocument(version);document.Entities.Add(subject.Explode());
        using var stream=new MemoryStream();Check(document.Save(stream,binary),"Exploded line output");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polyline-explosion-{closed}-{version}-{binary}.dxf"),stream.ToArray());
        stream.Position=0;var loaded=DxfDocument.Load(stream)!;
        Equal(closed?6:5,loaded.Entities.Lines.Count(),"Reloaded lines");
        foreach(var line in loaded.Entities.Lines) { Check(!line.IsVisible,"Reloaded visibility lost");Check(line.XData.ContainsAppId("LEGACY_AFFINE"),"Reloaded XData lost"); }
    }
    private static void RegisterPolylineExplosionTests()
    {
        foreach(int smooth in new[]{0,5,6})foreach(bool closed in new[]{false,true})for(int owner=0;owner<4;owner++)
        {int o=owner;Run($"polyline-explosion/model/{smooth}/{closed}/{o}",()=>PolylineExplosionModel(smooth,closed,o));}
        foreach(bool closed in new[]{false,true})foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"polyline-explosion/wire/{closed}/{version}/{binary}",()=>PolylineExplosionWire(closed,version,binary));
        foreach(bool binary in new[]{false,true})foreach(bool decorated in new[]{false,true})Run($"polyline-explosion/stored/{binary}/{decorated}",()=>
        {
            var doc=new DxfDocument(DxfVersion.AutoCad2018);var source=PolylineExplosionSubject(0,false);doc.Entities.Add(source);
            using var stream=new MemoryStream();doc.Save(stream,binary);stream.Position=0;var line=DxfDocument.Load(stream)!.Entities.Polylines3D.Single();
            if(decorated)
            {
                var data=new XData(new ApplicationRegistry("CHILD_ONLY"));data.XDataRecord.Add(new XDataRecord(XDataCode.String,"keep"));line.VertexRecords[0].XData.Add(data);
                Throws<NotSupportedException>(()=>line.Explode());Equal("keep",(string)line.VertexRecords[0].XData["CHILD_ONLY"].XDataRecord[0].Value,"Child metadata changed");
            }
            else {var lines=line.Explode();Equal(5,lines.Count,"Ordinary retained sequence rejected");Check(lines.All(e=>!e.IsVisible && e.XData.ContainsAppId("LEGACY_AFFINE")),"Loaded appearance lost");}
        });
        Run("polyline-explosion/reject/handle-reference",()=>
        {
            var line=PolylineExplosionSubject(0,false);line.XData["LEGACY_AFFINE"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,"0"));
            Throws<NotSupportedException>(()=>line.Explode());
        });
        foreach(int smooth in new[]{0,5,6})foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Run($"polyline-explosion/reject/nonfinite/{smooth}/{BitConverter.DoubleToInt64Bits(invalid):X16}",()=>
            {var line=PolylineExplosionSubject(smooth,false);line.Vertexes[5]=new(invalid,1,2);Throws<InvalidOperationException>(()=>line.Explode());});
        Run("polyline-explosion/reject/unknown-smoothing",()=>
        {var line=PolylineExplosionSubject(7,false);Throws<NotSupportedException>(()=>line.Explode());});
        Run("polyline-explosion/reject/budget-before-sampling",()=>
        {
            var field=typeof(Polyline3D).GetField("MaximumExplodedSegments");Check(field!=null,"Sampling budget must exist before allocation probe");
            Equal(1000000,(int)field!.GetRawConstantValue()!,"Sampling limit");
            short old=Polyline3D.DefaultSplineSegs;
            try {Polyline3D.DefaultSplineSegs=short.MaxValue;var line=new Polyline3D(Enumerable.Range(0,1001).Select(i=>new Vector3(i,1,2))){SmoothType=PolylineSmoothType.Cubic};Throws<ArgumentOutOfRangeException>(()=>line.Explode());}
            finally {Polyline3D.DefaultSplineSegs=old;}
        });
        foreach (int smooth in new[] { 0, 5, 6 }) foreach (bool attached in new[] { false, true })
            Run($"polyline-explosion/hatch-geometry/{smooth}/{attached}", () =>
            {
                var source = PolylineExplosionSubject(smooth, false);
                var before = LegacyAffineBits(source);
                source.XData["LEGACY_AFFINE"].XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle, "0"));
                var path = new HatchBoundaryPath(new EntityObject[] { source });
                if (attached) new Hatch(HatchPattern.Solid, new[] { path }, true);
                int reactors = source.Reactors.Count;
                for (int cycle = 0; cycle < 3; cycle++) path.Update();
                var points = source.PolygonalVertexes(Polyline3D.DefaultSplineSegs * (source.Vertexes.Count - 1));
                var frame = MathHelper.ArbitraryAxis(source.Normal).Transpose();
                Equal(points.Count - 1, path.Edges.Count, "HATCH geometry segment count");
                for (int i = 0; i < path.Edges.Count; i++)
                {
                    var edge = (HatchBoundaryPath.Line)path.Edges[i];
                    var start = frame * points[i]; var end = frame * points[i + 1];
                    Check(edge.Start.Equals(new Vector2(start.X, start.Y)) && edge.End.Equals(new Vector2(end.X, end.Y)), "HATCH geometry projection differs");
                }
                Check(ReferenceEquals(path.Entities.Single(), source), "HATCH original source lost");
                Equal(reactors, source.Reactors.Count, "HATCH update changed associations");
                Equal("0", (string)source.XData["LEGACY_AFFINE"].XDataRecord[1].Value, "Source XData handle lost");
                Check(before.SequenceEqual(LegacyAffineBits(source)), "HATCH update changed source geometry");
                Throws<NotSupportedException>(() => source.Explode());
            });
        foreach(int count in new[]{0,1})foreach(bool closed in new[]{false,true})Run($"polyline-explosion/degenerate/{count}/{closed}",()=>
        {var line=new Polyline3D(Enumerable.Repeat(Vector3.UnitX,count),closed);Equal(closed?count:0,line.Explode().Count,"Existing degenerate segment behavior");});
    }
}
