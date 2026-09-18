// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private static (Vector3[] Controls, double[] Weights, double[] Knots) ActiveDomainInput(int degree, int form, int exponent)
    {
        int n = degree + 4;
        var controls = Enumerable.Range(0, n).Select(i => new Vector3(3*i+10, (17*i)%13-5, i%4-2)).ToArray();
        var weights = Enumerable.Range(0, n).Select(i => Math.ScaleB(1.0, i%5-2)).ToArray();
        var knots = new double[n + degree + 1];
        if (form == 0) for (int i=0; i<knots.Length; i++) knots[i]=i-degree-2;
        else if (form == 3) for (int i=1; i<knots.Length; i++) knots[i]=knots[i-1]+1+i%3;
        else
        {
            for (int i=degree+1; i<n; i++) knots[i]=form==1 ? i-degree : degree==1 ? (i<degree+3 ? 1 : 2) : 2;
            for (int i=n; i<knots.Length; i++) knots[i]=4;
        }
        for (int i=0; i<knots.Length; i++) knots[i]=Math.ScaleB(knots[i],exponent);
        return (controls, weights, knots);
    }
    private static double[][] ActiveCoordinates(IEnumerable<Vector3> points) => points.Select(p=>new[]{p.X,p.Y,p.Z}).ToArray();
    private static void ActiveClose(Vector3 expected, Vector3 actual, double tolerance=1e-12)
    {
        Check(double.IsFinite(actual.X)&&double.IsFinite(actual.Y)&&double.IsFinite(actual.Z), "Nonfinite sample");
        for(int axis=0;axis<3;axis++) Check(Math.Abs(expected[axis]-actual[axis])<=tolerance, "Wrong active-domain sample");
    }
    private static void ActiveWire(int kind, DxfVersion version, bool binary)
    {
        int degree=2+kind%2, form=kind/2;
        var data=ActiveDomainInput(degree,form,0);
        var spline=new Spline(data.Controls,data.Weights,data.Knots,(short)degree,false);
        var expected=spline.PolygonalVertexes(9); var poly=spline.ToPolyline3D(9);
        var doc=new DxfDocument(version);doc.Entities.Add(new EntityObject[]{spline,poly});
        using var bytes=new MemoryStream();Check(doc.Save(bytes,binary),"Active domain save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"spline-active-domain-{kind}-{version}-{binary}.dxf"),bytes.ToArray());
        bytes.Position=0;var loaded=DxfDocument.Load(bytes)??throw new InvalidOperationException("Active domain load");
        var copy=loaded.Entities.Splines.Single();
        Check(copy.Knots.SequenceEqual(spline.Knots),"Stored knot domain changed");
        Check(copy.ControlPoints.SequenceEqual(spline.ControlPoints),"Stored control polygon changed");
        var actual=copy.PolygonalVertexes(9);Equal(9,actual.Count,"Reloaded sample count");
        for(int i=0;i<9;i++){ActiveClose(expected[i],actual[i]);ActiveClose(expected[i],loaded.Entities.Polylines3D.Single().Vertexes[i]);}
    }
    private static void RegisterSplineActiveDomainTests()
    {
        Run("spline-active-domain/unclamped-endpoints",()=>
        {
            var c=new[]{new Vector3(10,0,0),new Vector3(20,2,0),new Vector3(30,8,0),new Vector3(40,18,0),new Vector3(50,32,0)};
            var actual=Spline.NurbsEvaluator(c,null!,Enumerable.Range(0,8).Select(i=>(double)i).ToArray(),2,false,false,5);
            ActiveClose((c[0]+c[1])*.5,actual[0]);ActiveClose((c[3]+c[4])*.5,actual[^1]);
        });
        Run("spline-active-domain/signed-weights",()=>
        {
            var actual=Spline.NurbsEvaluator(new[]{new Vector3(0,2,4),new Vector3(2,4,6)},new[]{-1.0,-2.0},null!,1,false,false,3);
            ActiveClose(new Vector3(4.0/3,10.0/3,16.0/3),actual[1]);
        });
        Run("spline-active-domain/zero-inactive-weight",()=>
        {
            var c=new[]{new Vector3(4,4,4),new Vector3(99,99,99),new Vector3(4,4,4)};
            foreach(var p in Spline.NurbsEvaluator(c,new[]{1.0,0,1},null!,2,false,false,9))ActiveClose(c[0],p,0);
        });
        foreach(double weight in new[]{double.Epsilon,1.0,double.MaxValue}) Run($"spline-active-domain/extreme-weight/{BitConverter.DoubleToInt64Bits(weight):X}",()=>
        {
            var c=new Vector3(double.MaxValue,-double.MaxValue,double.Epsilon);
            foreach(var p in Spline.NurbsEvaluator(new[]{c,c,c},new[]{weight,weight,weight},null!,2,false,false,9))
                Check(DirectionBits(c).SequenceEqual(DirectionBits(p)),"Constant rational geometry changed");
        });
        Run("spline-active-domain/overflowing-domain-width",()=>
        {
            var actual=Spline.NurbsEvaluator(new[]{new Vector3(0,2,4),new Vector3(4,6,8)},null!,
                new[]{-double.MaxValue,-double.MaxValue,double.MaxValue,double.MaxValue},1,false,false,5);
            for(int i=0;i<5;i++)ActiveClose(new Vector3(i,i+2,i+4),actual[i]);
        });
        var rows=new List<object>();
        foreach(int degree in new[]{1,2,3,5,10})foreach(int form in new[]{0,1,2,3})foreach(int exponent in new[]{-500,0,500})foreach(bool closed in new[]{false,true})
        {
            Run($"spline-active-domain/corpus/{degree}/{form}/{exponent}/{closed}",()=>
            {
                var d=ActiveDomainInput(degree,form,exponent);
                var originalControls=(Vector3[])d.Controls.Clone();var originalWeights=(double[])d.Weights.Clone();var originalKnots=(double[])d.Knots.Clone();
                var result=Spline.NurbsEvaluator(d.Controls,d.Weights,d.Knots,degree,closed,false,9);
                Equal(9,result.Count,"Active-domain count");
                foreach(var p in result)
                {
                    Check(double.IsFinite(p.X)&&double.IsFinite(p.Y)&&double.IsFinite(p.Z),"Nonfinite rational result");
                    Check(p.X>=10-1e-12&&p.X<=d.Controls[^1].X+1e-12,"Artificial origin/outside convex hull");
                }
                Check(originalControls.SequenceEqual(d.Controls)&&originalWeights.SequenceEqual(d.Weights)&&originalKnots.SequenceEqual(d.Knots),"Evaluator mutated source");
                rows.Add(new{degree,form,exponent,closed,points=ActiveCoordinates(result)});
            });
        }
        Run("spline-active-domain/write-corpus",()=>File.WriteAllText(Path.Combine(ArtifactDirectory,"spline-active-domain-numerics.json"),JsonSerializer.Serialize(rows)));
        for(int kind=0;kind<4;kind++)foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
        {int k=kind;Run($"spline-active-domain/wire/{k}/{version}/{binary}",()=>ActiveWire(k,version,binary));}
        foreach(int fault in Enumerable.Range(0,15))Run($"spline-active-domain/invalid/{fault}",()=>
        {
            var d=ActiveDomainInput(2,1,0);int degree=2,precision=9;
            switch(fault)
            {
                case 0:degree=0;break;case 1:degree=11;break;
                case 2:precision=1;break;
                case 3:
                    Check(typeof(Spline).GetMethod("EvaluateNonPeriodicSpline",BindingFlags.NonPublic|BindingFlags.Static)!=null,"Missing preallocation admission check");
                    precision=int.MaxValue;break;
                case 4:d.Controls[2]=new Vector3(double.NaN,0,0);break;
                case 5:d.Controls[2]=new Vector3(0,double.PositiveInfinity,0);break;
                case 6:d.Weights[2]=double.NaN;break;case 7:d.Weights[0]=double.NegativeInfinity;break;
                case 8:Array.Fill(d.Weights,0);break;
                case 9:d.Knots[3]=double.NaN;break;case 10:d.Knots[^1]=double.PositiveInfinity;break;
                case 11:d.Knots[3]=-1;break;case 12:Array.Fill(d.Knots,0);break;
                case 13:d.Knots=new double[]{0,0,0,0,1,2,3,3,3};break;
                case 14:d.Weights=new double[]{1};break;
            }
            Throws<ArgumentException>(()=>Spline.NurbsEvaluator(d.Controls,d.Weights,d.Knots,degree,false,false,precision));
        });
        Run("spline-active-domain/rational-pole",()=>Throws<ArgumentException>(()=>Spline.NurbsEvaluator(
            new[]{new Vector3(0,0,0),new Vector3(1,0,0)},new[]{1.0,-1.0},null!,1,false,false,3)));
        Run("spline-active-domain/unrepresentable-parameters",()=>Throws<ArgumentException>(()=>Spline.NurbsEvaluator(
            new[]{new Vector3(0,0,0),new Vector3(1,0,0)},null!,new[]{1.0,1.0,Math.BitIncrement(1.0),Math.BitIncrement(1.0)},1,false,false,9)));
    }
}
