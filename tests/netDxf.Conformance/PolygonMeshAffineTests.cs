// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static PolygonMesh PolygonAffineSubject(int smooth = 0)
    {
        var mesh = new PolygonMesh(4, 4, Enumerable.Range(0, 16).Select(i => new Vector3(1 + i%4, 2 + i/4, 3 + i*.125)))
        { SmoothType = (PolylineSmoothType)smooth, DensityU = 5, DensityV = 7, Normal = new Vector3(2, -3, 6),
          Layer = new Layer("POLYGON_AFFINE"), Color = new AciColor(3), IsVisible = false,
          LinetypeScale = 1.75, ProxyGraphics = new byte[] { 2, 3, 5, 7 } };
        var data = new XData(new ApplicationRegistry("POLYGON_AFFINE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged"));mesh.XData.Add(data);
        return mesh;
    }
    private static long[] PolygonAffineBits(PolygonMesh mesh) => mesh.Vertexes.Append(mesh.Normal)
        .SelectMany(v => new[] { v.X, v.Y, v.Z }).Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void PolygonAffineReject(PolygonMesh mesh, Action operation)
    {
        var before=PolygonAffineBits(mesh);var vertices=mesh.Vertexes;var proxy=mesh.ProxyGraphics!;
        string handle=mesh.Handle;var owner=mesh.Owner;var data=mesh.XData.Values.ToArray();
        bool rejected=false;
        try { operation(); } catch(ArgumentException) { rejected=true; } catch(InvalidOperationException) { rejected=true; } catch(NotSupportedException) { rejected=true; }
        Check(rejected,"Invalid polygon transform accepted");Check(before.SequenceEqual(PolygonAffineBits(mesh)),"Partial polygon geometry mutation");
        Check(ReferenceEquals(vertices,mesh.Vertexes),"Vertex array replaced");Check(proxy.SequenceEqual(mesh.ProxyGraphics!),"Rejected proxy changed");
        Equal(handle,mesh.Handle,"Rejected identity");Check(ReferenceEquals(owner,mesh.Owner)&&data.SequenceEqual(mesh.XData.Values),"Rejected metadata identity");
    }
    private static void PolygonAffineModel(int smooth, string mode, bool four, bool owned, DxfVersion? version=null, bool binary=false)
    {
        var mesh=PolygonAffineSubject(smooth);var document=new DxfDocument(version??DxfVersion.AutoCad2018);
        if(owned)document.Entities.Add(mesh);
        var before=mesh.Vertexes.ToArray();var array=mesh.Vertexes;var oldNormal=mesh.Normal;
        var oldSamples=mesh.MeshVertexes();var proxy=mesh.ProxyGraphics!;var handle=mesh.Handle;var owner=mesh.Owner;var data=mesh.XData.Values.ToArray();
        var a=VertexAffineMatrix(mode);var t=VertexAffineTranslation(mode);
        VertexAffineApply(mesh,a,t,four);
        for(int i=0;i<before.Length;i++)LineReviewNear(a*before[i]+t,mesh.Vertexes[i],"Polygon control image");
        Vector3 direction=a*oldNormal;double scale=Math.Max(Math.Abs(direction.X),Math.Max(Math.Abs(direction.Y),Math.Abs(direction.Z)));
        LineReviewNear(scale==0?oldNormal:Vector3.Normalize(direction/scale),mesh.Normal,"Polygon auxiliary normal");
        Check(ReferenceEquals(array,mesh.Vertexes),"Polygon vertex array replaced");Equal(handle,mesh.Handle,"Polygon handle");
        Check(ReferenceEquals(owner,mesh.Owner)&&data.SequenceEqual(mesh.XData.Values),"Polygon metadata identity");
        Equal((PolylineSmoothType)smooth,mesh.SmoothType,"Surface type");Equal((short)5,mesh.DensityU,"U density");Equal((short)7,mesh.DensityV,"V density");
        if(mode=="identity")Check(proxy.SequenceEqual(mesh.ProxyGraphics!),"Identity proxy lost");else Check(mesh.ProxyGraphics==null,"Stale polygon proxy retained");
        var samples=mesh.MeshVertexes();Equal(oldSamples.Count,samples.Count,"Transformed sample count");
        for(int i=0;i<samples.Count;i++)LineReviewNear(a*oldSamples[i]+t,samples[i],"Surface affine covariance");
        if(!version.HasValue)return;
        using var output=new MemoryStream();Check(document.Save(output,binary),"Polygon affine save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polygon-affine-{version}-{binary}-{mode}.dxf"),output.ToArray());
        output.Position=0;var loaded=DxfDocument.Load(output)??throw new InvalidOperationException("Polygon affine reload");
        var found=loaded.Entities.PolygonMeshes.Single();Check(mesh.Vertexes.SequenceEqual(found.Vertexes),"Polygon WCS round trip");
        Equal(0,loaded.Objects.Validate().Count,"Polygon graph");
    }
    private static void PolygonAffineLoaded(string mode, bool binary, bool four)
    {
        var document=PolygonGridLoad(PolygonGridTags(DxfVersion.AutoCad2018),binary);var mesh=document.Entities.PolygonMeshes.Single();
        var children=mesh.VertexRecords.ToArray();var end=mesh.EndSequenceRecord;var array=mesh.Vertexes;var controls=mesh.Vertexes.ToArray();
        var handles=children.Select(r=>r.Handle).ToArray();mesh.ProxyGraphics=new byte[]{1,2,3};
        var a=VertexAffineMatrix(mode);var t=VertexAffineTranslation(mode);VertexAffineApply(mesh,a,t,four);
        Check(ReferenceEquals(array,mesh.Vertexes)&&ReferenceEquals(end,mesh.EndSequenceRecord),"Stored arrays/end identity");
        Check(children.SequenceEqual(mesh.VertexRecords)&&handles.SequenceEqual(mesh.VertexRecords.Select(r=>r.Handle)),"Stored vertex identities");
        for(int i=0;i<controls.Length;i++)LineReviewNear(a*controls[i]+t,mesh.Vertexes[i],"Stored control image");
        using var output=new MemoryStream();Check(document.Save(output,binary),"Stored affine save");output.Position=0;
        var loaded=DxfDocument.Load(output)??throw new InvalidOperationException("Stored affine reload");
        Check(mesh.Vertexes.SequenceEqual(loaded.Entities.PolygonMeshes.Single().Vertexes),"Stored affine round trip");
    }
    private static void RegisterPolygonMeshAffineTests()
    {
        foreach(int smooth in new[]{0,5,6,8})foreach(string mode in VertexAffineModes)foreach(bool four in new[]{false,true})foreach(bool owned in new[]{false,true})
            Run($"polygon-affine/model/{smooth}/{mode}/{four}/{owned}",()=>PolygonAffineModel(smooth,mode,four,owned));
        foreach(string mode in VertexAffineModes)foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"polygon-affine/wire/{version}/{binary}/{mode}",()=>PolygonAffineModel(0,mode,binary,true,version,binary));
        foreach(string mode in VertexAffineModes)foreach(bool binary in new[]{false,true})foreach(bool four in new[]{false,true})
            Run($"polygon-affine/stored/{mode}/{binary}/{four}",()=>PolygonAffineLoaded(mode,binary,four));
        foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            string label=BitConverter.DoubleToInt64Bits(invalid).ToString("X16");
            for(int k=0;k<48;k++){int n=k;Run($"polygon-affine/reject/source/{n}/{label}",()=>{var mesh=PolygonAffineSubject();var p=mesh.Vertexes[n/3];p[n%3]=invalid;mesh.Vertexes[n/3]=p;PolygonAffineReject(mesh,()=>mesh.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});}
            for(int k=0;k<12;k++){int n=k;Run($"polygon-affine/reject/matrix3/{n}/{label}",()=>{var mesh=PolygonAffineSubject();var a=Matrix3.Identity;var t=Vector3.Zero;if(n<9)a[n/3,n%3]=invalid;else t[n-9]=invalid;PolygonAffineReject(mesh,()=>mesh.TransformBy(a,t));});}
            for(int k=0;k<16;k++){int n=k;Run($"polygon-affine/reject/matrix4/{n}/{label}",()=>{var mesh=PolygonAffineSubject();var a=Matrix4.Identity;a[n/4,n%4]=invalid;PolygonAffineReject(mesh,()=>mesh.TransformBy(a));});}
        }
        for(int k=0;k<4;k++){int n=k;Run($"polygon-affine/reject/projective/{n}",()=>{var mesh=PolygonAffineSubject();var a=Matrix4.Identity;a[3,n]=n==3?2:1e-30;PolygonAffineReject(mesh,()=>mesh.TransformBy(a));});}
        foreach(int index in Enumerable.Range(0,4))Run($"polygon-affine/reject/normal/{index}",()=>
        {
            var mesh=PolygonAffineSubject();var normal=new[]{Vector3.Zero,new Vector3(double.NaN,0,1),new Vector3(1,2,3),new Vector3(0,double.NegativeInfinity,1)}[index];
            typeof(EntityObject).GetField("normal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(mesh,normal);
            PolygonAffineReject(mesh,()=>mesh.TransformBy(Matrix3.Identity,Vector3.Zero));
        });
        Run("polygon-affine/reject/late-overflow",()=>{var mesh=PolygonAffineSubject();mesh.Vertexes[15]=new(double.MaxValue,0,0);PolygonAffineReject(mesh,()=>mesh.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});
        Run("polygon-affine/reject/late-underflow",()=>{var mesh=PolygonAffineSubject();mesh.Vertexes[15]=new(double.Epsilon,0,0);PolygonAffineReject(mesh,()=>mesh.TransformBy(Matrix3.Scale(.25),Vector3.Zero));});
        Run("polygon-affine/boundary/cancellation",()=>{var mesh=PolygonAffineSubject();for(int i=0;i<16;i++)mesh.Vertexes[i]=new(double.MaxValue,double.MaxValue,3);mesh.TransformBy(new Matrix3(2,-2,1,0,0,1,0,0,1),Vector3.Zero);foreach(var p in mesh.Vertexes)Equal(new Vector3(3,3,3),p,"Exact affine cancellation");});
        Run("polygon-affine/boundary/derived-normal",()=>{var mesh=new PolygonAffineDerived();mesh.TransformBy(Matrix3.Scale(2),Vector3.UnitX);Equal(new Vector3(3,0,0),mesh.Vertexes[0],"Nonvirtual polygon publication");});
    }
    private sealed class PolygonAffineDerived:PolygonMesh
    {
        internal PolygonAffineDerived():base(2,2,new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ,Vector3.Zero}){}
        public override Vector3 Normal { get=>throw new InvalidOperationException("virtual read"); set=>throw new InvalidOperationException("virtual write"); }
    }
}
