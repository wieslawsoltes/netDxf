// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly (short U,short V)[] BezierGridSizes={(2,2),(2,5),(3,2),(3,4),(4,4),(5,3),(7,2),(2,9),(12,3),(3,12),(32,2),(2,32)};
    private static PolygonMesh BezierGrid(short u,short v,double scale=1)
    {
        var points=Enumerable.Range(0,u*v).Select(n=>
        {int i=n%u,j=n/u;return new Vector3((i-1.25)*scale,(j-.75)*scale,((i*i%7)-3)*(j%3-1)*scale);});
        return new PolygonMesh(u,v,points){SmoothType=(PolylineSmoothType)8};
    }
    private static double BezierWeight(int n,int i,double t)
    {
        double choose=1;for(int k=1;k<=i;k++)choose*=((double)n-k+1)/k;
        return choose*Math.Pow(t,i)*Math.Pow(1-t,n-i);
    }
    private static void CheckBezierGrid(PolygonMesh mesh,int u,int v)
    {
        var original=mesh.Vertexes.ToArray();var values=mesh.MeshVertexes(u,v);Equal(u*v,values.Count,"Bezier sample count");
        double scale=original.SelectMany(p=>new[]{Math.Abs(p.X),Math.Abs(p.Y),Math.Abs(p.Z)}).Max();
        for(int j=0;j<v;j++)for(int i=0;i<u;i++)
        {
            Vector3 expected=Vector3.Zero;
            for(int b=0;b<mesh.V;b++)for(int a=0;a<mesh.U;a++)
                expected+=original[a+b*mesh.U]*(BezierWeight(mesh.U-1,a,i/(u-1.0))*BezierWeight(mesh.V-1,b,j/(v-1.0)));
            Vector3 actual=values[i+j*u];
            foreach(double d in new[]{actual.X-expected.X,actual.Y-expected.Y,actual.Z-expected.Z})
                Check(double.IsFinite(d) && Math.Abs(d)<=scale*2e-12,"Bernstein comparison");
        }
        foreach(var corner in new[]{(0,0),(u-1,mesh.U-1),((v-1)*u,(mesh.V-1)*mesh.U),(u*v-1,original.Length-1)})
            Check(DirectionBits(values[corner.Item1]).SequenceEqual(DirectionBits(original[corner.Item2])),"Bezier endpoint bits");
        var converted=mesh.ToMesh(u,v);Check(converted.Vertexes.SequenceEqual(values),"Bezier MESH conversion");
        Equal((u-1)*(v-1),converted.Faces.Count,"Bezier cell count");
        var clone=(PolygonMesh)mesh.Clone();Equal((PolylineSmoothType)8,clone.SmoothType,"Bezier clone type");
        Check(!ReferenceEquals(mesh.Vertexes,clone.Vertexes) && clone.MeshVertexes(u,v).SequenceEqual(values),"Bezier clone isolation/geometry");
        Check(mesh.Vertexes.SequenceEqual(original),"Bezier source controls changed");
    }
    private static void BezierNumericalCorpus()
    {
        var rows=new List<object>();
        foreach(var size in new (short U,short V)[]{(2,2),(3,5),(5,3),(4,4),(12,7),(2,64)})
        foreach(int exponent in new[]{-500,0,500})foreach(var precision in new[]{(4,5),(7,4)})
        {
            var mesh=BezierGrid(size.U,size.V,Math.ScaleB(1,exponent));var points=mesh.MeshVertexes(precision.Item1,precision.Item2);
            rows.Add(new{u=size.U,v=size.V,exponent,pu=precision.Item1,pv=precision.Item2,controls=mesh.Vertexes.Select(DirectionBits),points=points.Select(DirectionBits)});
        }
        var extreme=new PolygonMesh(201,2,Enumerable.Range(0,402).Select(i=>new Vector3(i%201==200?double.MaxValue/4:0,0,0))){SmoothType=(PolylineSmoothType)8};
        var sample=extreme.MeshVertexes(101,3)[1];Check(sample.X>0 && double.IsFinite(sample.X),"Representable tiny-weight contribution lost");
        File.WriteAllText(Path.Combine(ArtifactDirectory,"bezier-grid-numerics.json"),JsonSerializer.Serialize(new{rows,extreme=DirectionBits(sample)}));
    }
    private static void RegisterPolygonMeshBezierTests()
    {
        foreach(var size in BezierGridSizes)foreach(var precision in new[]{(3,3),(4,5),(7,4)})foreach(int exponent in new[]{-300,0,300})
            Run($"bezier-grid/model/{size.U}/{size.V}/{precision.Item1}/{precision.Item2}/{exponent}",()=>
                CheckBezierGrid(BezierGrid(size.U,size.V,Math.ScaleB(1,exponent)),precision.Item1,precision.Item2));
        foreach(double value in new[]{double.MaxValue,double.Epsilon,-double.MaxValue})
            Run($"bezier-grid/constant/{BitConverter.DoubleToInt64Bits(value):X16}",()=>
            {
                var mesh=new PolygonMesh(4,5,Enumerable.Repeat(new Vector3(value,-value,value),20)){SmoothType=(PolylineSmoothType)8};
                foreach(var point in mesh.MeshVertexes(9,11))Check(DirectionBits(point).SequenceEqual(DirectionBits(mesh.Vertexes[0])),"Constant extreme changed");
            });
        foreach(int closure in new[]{1,2,3})Run($"bezier-grid/closed-rejection/{closure}",()=>
        {
            var mesh=BezierGrid(4,4);mesh.IsClosedInU=(closure&1)!=0;mesh.IsClosedInV=(closure&2)!=0;var before=mesh.Vertexes.ToArray();
            Throws<NotSupportedException>(()=>mesh.MeshVertexes());Throws<NotSupportedException>(()=>mesh.ToMesh());Throws<NotSupportedException>(()=>mesh.Explode());
            Check(before.SequenceEqual(mesh.Vertexes),"Closed rejection mutated controls");
        });
        Run("bezier-grid/budgets",()=>
        {
            Check(typeof(PolygonMesh).GetField("MaximumBezierBlendOperations")!=null,"Missing Bezier work budget");
            var mesh=BezierGrid(256,256);Throws<ArgumentOutOfRangeException>(()=>mesh.MeshVertexes(201,201));
            Throws<ArgumentOutOfRangeException>(()=>mesh.MeshVertexes(int.MaxValue,3));
        });
        Run("bezier-grid/invalid-control",()=>{var mesh=BezierGrid(4,4);mesh.Vertexes[15]=new(double.NaN,0,0);Throws<InvalidOperationException>(()=>mesh.MeshVertexes());});
        Run("bezier-grid/curve-types",()=>
        {
            var p2=new Polyline2D();var p3=new Polyline3D();
            Throws<ArgumentOutOfRangeException>(()=>p2.SmoothType=(PolylineSmoothType)8);Throws<ArgumentOutOfRangeException>(()=>p3.SmoothType=(PolylineSmoothType)8);
            Equal(PolylineSmoothType.NoSmooth,p2.SmoothType,"Rejected 2D type changed");Equal(PolylineSmoothType.NoSmooth,p3.SmoothType,"Rejected 3D type changed");
        });
        foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})foreach(int shape in Enumerable.Range(0,4))foreach(bool nested in new[]{false,true})
            Run($"bezier-grid/wire/{version}/{binary}/{shape}/{nested}",()=>
            {
                var size=new (short U,short V)[]{(2,2),(3,5),(4,4),(5,3)}[shape];var mesh=BezierGrid(size.U,size.V);mesh.DensityU=5;mesh.DensityV=7;
                var doc=new DxfDocument(version);if(nested){var block=new Block("BEZIER_PATCH");block.Entities.Add(mesh);doc.Entities.Add(new Insert(block));}else doc.Entities.Add(mesh);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Bezier save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"bezier-grid-{version}-{binary}-{shape}-{nested}.dxf"),stream.ToArray());
                stream.Position=0;var loaded=DxfDocument.Load(stream)??throw new InvalidOperationException("Bezier load");
                var actual=nested?loaded.Blocks["BEZIER_PATCH"].Entities.OfType<PolygonMesh>().Single():loaded.Entities.PolygonMeshes.Single();
                Equal((PolylineSmoothType)8,actual.SmoothType,"Bezier wire type");Check(actual.Vertexes.SequenceEqual(mesh.Vertexes),"Bezier control order");
                Equal((short)5,actual.DensityU,"Bezier U density");Equal((short)7,actual.DensityV,"Bezier V density");CheckBezierGrid(actual,5,7);
                Equal(24,actual.Explode().Count,"Bezier Explode density");Equal(0,loaded.Objects.Validate().Count,"Bezier object graph");
            });
        foreach(var version in SupportedVersions)foreach(bool inputBinary in new[]{false,true})foreach(bool binary in new[]{false,true})
            Run($"bezier-grid/input/{version}/{inputBinary}/{binary}",()=>
            {
                using var input=new MemoryStream(RawFixtureBytes(PolygonGridTags(version,8),inputBinary));
                var doc=DxfDocument.Load(input)??throw new InvalidOperationException("Bezier input type rejected");
                var mesh=doc.Entities.PolygonMeshes.Single();Equal((PolylineSmoothType)8,mesh.SmoothType,"Input type downgraded");
                Equal(16,mesh.Vertexes.Length,"Input controls");Equal(1,doc.Entities.Lines.Count(),"Following record consumed");
                using var output=new MemoryStream();Check(doc.Save(output,binary),"Bezier input save");
                output.Position=0;var reloaded=DxfDocument.Load(output)??throw new InvalidOperationException("Bezier reopen");
                Check(reloaded.Entities.PolygonMeshes.Single().Vertexes.SequenceEqual(mesh.Vertexes),"Reopened controls changed");
            });
        Run("bezier-grid/numerical-oracle",BezierNumericalCorpus);
    }
}
