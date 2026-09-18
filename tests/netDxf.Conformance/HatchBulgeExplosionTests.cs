// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static Vector2 HatchBulgeRotate(Vector2 p,int turns)
    {
        for(int i=0;i<turns;i++)p=new Vector2(-p.Y,p.X);
        return p;
    }
    private static Vector2 HatchBulgeExpected(Vector2 first,Vector2 last,double bulge,double t)
    {
        var d=last-first;
        var center=(first+last)*.5+new Vector2(-d.Y,d.X)*((1/bulge-bulge)*.25);
        double radius=(first-center).Modulus();
        double angle=Math.Atan2(first.Y-center.Y,first.X-center.X)+4*Math.Atan(bulge)*t;
        return center+new Vector2(radius*Math.Cos(angle),radius*Math.Sin(angle));
    }
    private static void CheckExplodedBulge(HatchBoundaryPath.Arc edge,Vector2 first,Vector2 last,double bulge)
    {
        Equal(bulge>0,edge.IsCounterclockwise,"Bulge traversal orientation");
        double bound=1e-11*Math.Max(1,edge.Radius);
        var copy=(HatchBoundaryPath.Arc)edge.Clone();
        Equal(edge.IsCounterclockwise,copy.IsCounterclockwise,"Cloned traversal");
        SameDoubleBits(edge.StartAngle,copy.StartAngle,"Clone start angle");SameDoubleBits(edge.EndAngle,copy.EndAngle,"Clone end angle");
        var entity=(Arc)edge.ConvertTo();
        double sweep=MathHelper.NormalizeAngle(entity.EndAngle-entity.StartAngle)*MathHelper.DegToRad;
        for(int i=0;i<=32;i++)
        {
            double t=i/32.0;var expected=HatchBulgeExpected(first,last,bulge,t);var actual=HatchConicPoint(edge,t);
            Check(Vector2.Distance(expected,actual)<=bound,"Exploded arc curve/traversal changed");
            double a=entity.StartAngle*MathHelper.DegToRad+sweep*(bulge>0?t:1-t);
            var converted=new Vector2(entity.Center.X+entity.Radius*Math.Cos(a),entity.Center.Y+entity.Radius*Math.Sin(a));
            Check(Vector2.Distance(expected,converted)<=bound,"Converted arc differs from source bulge");
        }
    }
    private static void RegisterHatchBulgeExplosionTests()
    {
        foreach(double bulge in new[]{-.125,-.5,-1.0,-2.0,-4.0,.125,.5,1.0,2.0,4.0})
        foreach(int turn in new[]{0,1,2,3})foreach(bool closed in new[]{false,true})
            Run($"hatch-bulge-explosion/model/{BitConverter.DoubleToInt64Bits(bulge):X}/{turn}/{closed}",()=>
            {
                var first=HatchBulgeRotate(new Vector2(2,-3),turn);var last=HatchBulgeRotate(new Vector2(8,5),turn);
                var poly=new HatchBoundaryPath.Polyline {IsClosed=closed,Vertexes=new[]{new Vector3(first.X,first.Y,bulge),new Vector3(last.X,last.Y,closed?0:123)}};
                var before=poly.Vertexes.SelectMany(DirectionBits).ToArray();var edges=poly.Explode();
                Equal(closed?2:1,edges.Count,"Open/closed explosion edge count");
                CheckExplodedBulge((HatchBoundaryPath.Arc)edges[0],first,last,bulge);
                if(closed){var line=(HatchBoundaryPath.Line)edges[1];Equal(last,line.Start,"Closure start");Equal(first,line.End,"Closure end");}
                Check(before.SequenceEqual(poly.Vertexes.SelectMany(DirectionBits)),"Explosion mutated source vertices");
            });
        Run("hatch-bulge-explosion/zero-and-dormant",()=>
        {
            var poly=new HatchBoundaryPath.Polyline {IsClosed=false,Vertexes=new[]{new Vector3(2,-3,0),new Vector3(8,5,2)}};
            var edge=(HatchBoundaryPath.Line)poly.Explode().Single();Equal(new Vector2(2,-3),edge.Start,"Zero-bulge start");Equal(new Vector2(8,5),edge.End,"Zero-bulge end");
        });
        double[] values={-2,-1,-.25,.25,1,2};
        for(int index=0;index<values.Length;index++)foreach(int turn in new[]{0,1})foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
        {
            int k=index;Run($"hatch-bulge-explosion/wire/{k}/{turn}/{version}/{binary}",()=>
            {
                double bulge=values[k];var first=HatchBulgeRotate(new Vector2(2,-3),turn);var last=HatchBulgeRotate(new Vector2(8,5),turn);
                var poly=new HatchBoundaryPath.Polyline {IsClosed=false,Vertexes=new[]{new Vector3(first.X,first.Y,bulge),new Vector3(last.X,last.Y,0)}};
                var closing=new HatchBoundaryPath.Line {Start=last,End=first};
                var path=new HatchBoundaryPath(new HatchBoundaryPath.Edge[]{poly,closing});Equal(2,path.Edges.Count,"Mixed boundary count");
                Check(ReferenceEquals(closing,path.Edges[1]),"Closing edge identity");
                CheckExplodedBulge((HatchBoundaryPath.Arc)path.Edges[0],first,last,bulge);
                var doc=new DxfDocument(version);doc.Entities.Add(new Hatch(HatchPattern.Solid,new[]{path},false));
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Bulge HATCH save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"hatch-bulge-explosion-{k}-{turn}-{version}-{binary}.dxf"),stream.ToArray());
                stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new Exception("Bulge HATCH load");
                CheckExplodedBulge((HatchBoundaryPath.Arc)loaded.Entities.Hatches.Single().BoundaryPaths.Single().Edges[0],first,last,bulge);
            });
        }
    }
}
