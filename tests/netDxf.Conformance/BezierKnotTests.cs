using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterBezierKnotTests()
    {
        foreach(DxfVersion v in SupportedVersions)
            foreach(bool binary in new[]{false,true})
                foreach(int degree in new[]{2,3})
                    foreach(int count in new[]{1,2,3,5})
                    {
                        DxfVersion version=v;bool b=binary;int d=degree,n=count;
                        Run($"spline/bezier-knots/{v}/{binary}/{degree}/{count}",()=>BezierKnots(version,b,d,n));
                    }
        Run("spline/bezier-knots/cubic-empty",()=>Throws<ArgumentException>(()=>new Spline(Array.Empty<BezierCurveCubic>())));
        Run("spline/bezier-knots/quadratic-empty",()=>Throws<ArgumentException>(()=>new Spline(Array.Empty<BezierCurveQuadratic>())));
        Run("spline/bezier-knots/cubic-null-list",()=>Throws<ArgumentNullException>(()=>new Spline((IEnumerable<BezierCurveCubic>)null!)));
        Run("spline/bezier-knots/quadratic-null-list",()=>Throws<ArgumentNullException>(()=>new Spline((IEnumerable<BezierCurveQuadratic>)null!)));
        Run("spline/bezier-knots/cubic-null-element",()=>Throws<ArgumentException>(()=>new Spline(new BezierCurveCubic[]{null!})));
        Run("spline/bezier-knots/quadratic-null-element",()=>Throws<ArgumentException>(()=>new Spline(new BezierCurveQuadratic[]{null!})));
    }
    private static Vector3 BezierEndpoint(int i)=>new(5*i,i*i,2*i);
    private static Spline CompositeBezier(int degree,int count)
    {
        if(degree==2) return new Spline(Enumerable.Range(0,count).Select(i=>new BezierCurveQuadratic(BezierEndpoint(i),BezierEndpoint(i)+new Vector3(2,3,4),BezierEndpoint(i+1))));
        return new Spline(Enumerable.Range(0,count).Select(i=>new BezierCurveCubic(BezierEndpoint(i),BezierEndpoint(i)+new Vector3(1,2,3),BezierEndpoint(i+1)-new Vector3(2,3,-1),BezierEndpoint(i+1))));
    }
    private static void BezierKnots(DxfVersion version,bool binary,int degree,int count)
    {
        var spline=CompositeBezier(degree,count);var controls=spline.ControlPoints.ToArray();
        for(int i=0;i<spline.Knots.Length;i++) Near((i/(degree+1))/(double)count,spline.Knots[i],"Uniform composite-Bezier knot "+i);
        Equal((degree+1)*(count+1),spline.Knots.Length,"Composite knot count");Equal(count*(degree+1),spline.ControlPoints.Length,"Composite control count");
        var clone=(Spline)spline.Clone();Check(spline.Knots.SequenceEqual(clone.Knots),"Cloning changed corrected knots");
        var doc=new DxfDocument(version);doc.Entities.Add(spline);doc.Entities.Add(clone);
        using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Composite save");
        if(count==3) File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"bezier-knots-{version}-{binary}-{degree}.dxf"),stream.ToArray());
        stream.Position=0;var loaded=DxfDocument.Load(stream) ?? throw new InvalidOperationException("Composite reload");
        foreach(Spline actual in loaded.Entities.Splines)
        {
            Check(controls.SequenceEqual(actual.ControlPoints),"Corrected knots changed controls");
            for(int i=0;i<actual.Knots.Length;i++) Near((i/(degree+1))/(double)count,actual.Knots[i],"Wire knot "+i);
        }
        Check(stream.CanRead,"Composite closed caller stream");
    }
}
