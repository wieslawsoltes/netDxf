// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static EntityObject LegacyAffineSubject(bool polyface, int smooth = 0)
    {
        var points = Enumerable.Range(0, 6).Select(i => new Vector3(1+i, 2-i, 3+i*.125)).ToArray();
        EntityObject entity = polyface
            ? new PolyfaceMesh(points, new[] { new short[] { 1,-2,3 }, new short[] { 3,4,-5,6 } })
            : new Polyline3D(points, true) { SmoothType = (PolylineSmoothType)smooth, LinetypeGeneration = true };
        entity.Layer = new Layer("LEGACY_AFFINE"); entity.Color = new AciColor(3); entity.IsVisible = false;
        entity.Normal = new Vector3(2,-3,6); entity.LinetypeScale = 1.75;
        entity.ProxyGraphics = new byte[] { 2,3,5,7 };
        var data = new XData(new ApplicationRegistry("LEGACY_AFFINE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "unchanged")); entity.XData.Add(data);
        return entity;
    }
    private static IList<Vector3> LegacyAffinePoints(EntityObject entity) => entity is PolyfaceMesh mesh ? mesh.Vertexes : ((Polyline3D)entity).Vertexes;
    private static DxfObject[] LegacyAffineChildren(EntityObject entity) => entity is PolyfaceMesh mesh
        ? mesh.RecordSequence.Cast<DxfObject>().ToArray()
        : ((Polyline3D)entity).VertexRecords.Cast<DxfObject>().Concat(((Polyline3D)entity).EndSequenceRecord == null
            ? Array.Empty<DxfObject>() : new DxfObject[] { ((Polyline3D)entity).EndSequenceRecord }).ToArray();
    private static long[] LegacyAffineBits(EntityObject entity) => LegacyAffinePoints(entity).Append(entity.Normal)
        .SelectMany(v => new[] { v.X,v.Y,v.Z }).Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void LegacyAffineReject(EntityObject entity, Action operation)
    {
        var bits = LegacyAffineBits(entity); var points = LegacyAffinePoints(entity); var children = LegacyAffineChildren(entity);
        var proxy = entity.ProxyGraphics!; var data = entity.XData.Values.ToArray(); var owner = entity.Owner; string handle = entity.Handle;
        bool rejected = false;
        try { operation(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; } catch (NotSupportedException) { rejected = true; }
        Check(rejected, "Invalid legacy transform accepted"); Check(bits.SequenceEqual(LegacyAffineBits(entity)), "Partial legacy transform mutation");
        Check(ReferenceEquals(points,LegacyAffinePoints(entity)), "Vertex container replaced");
        Check(children.SequenceEqual(LegacyAffineChildren(entity)), "Child records replaced");
        Check(proxy.SequenceEqual(entity.ProxyGraphics!), "Rejected proxy changed");
        Check(data.SequenceEqual(entity.XData.Values) && ReferenceEquals(owner,entity.Owner), "Rejected metadata identity changed"); Equal(handle,entity.Handle,"Handle changed");
    }
    private static void LegacyAffineModel(bool polyface, string mode, bool four, bool owned, int smooth = 0, DxfVersion? version = null, bool binary = false)
    {
        var entity = LegacyAffineSubject(polyface,smooth); var document = new DxfDocument(version ?? DxfVersion.AutoCad2018);
        if (owned) document.Entities.Add(entity);
        var points = LegacyAffinePoints(entity); var original = points.ToArray(); var normal = entity.Normal; var proxy = entity.ProxyGraphics!;
        var owner = entity.Owner; string handle = entity.Handle; var data = entity.XData.Values.ToArray();
        var faces = (entity as PolyfaceMesh)?.Faces.ToArray();
        var a = VertexAffineMatrix(mode); var t = VertexAffineTranslation(mode);
        VertexAffineApply(entity,a,t,four);
        Check(ReferenceEquals(points,LegacyAffinePoints(entity)), "Vertex container changed");
        for (int i=0;i<original.Length;i++) LineReviewNear(a*original[i]+t,points[i],"WCS point image");
        var direction = a*normal; double scale = Math.Max(Math.Abs(direction.X),Math.Max(Math.Abs(direction.Y),Math.Abs(direction.Z)));
        LineReviewNear(scale == 0 ? normal : Vector3.Normalize(direction/scale),entity.Normal,"Auxiliary normal");
        if (mode == "identity") Check(proxy.SequenceEqual(entity.ProxyGraphics!),"Identity proxy lost");
        else Check(entity.ProxyGraphics == null,"Stale proxy retained");
        Check(ReferenceEquals(owner,entity.Owner) && data.SequenceEqual(entity.XData.Values),"Metadata identity changed"); Equal(handle,entity.Handle,"Handle changed");
        if (entity is PolyfaceMesh mesh)
        {
            Check(faces!.SequenceEqual(mesh.Faces),"Face identity changed");
            Check(mesh.Faces[0].VertexIndexes.SequenceEqual(new short[] {1,-2,3}) && mesh.Faces[1].VertexIndexes.SequenceEqual(new short[] {3,4,-5,6}),"Signed face topology changed");
        }
        else
        {
            var line=(Polyline3D)entity; Check(line.IsClosed && line.LinetypeGeneration,"Polyline flags changed"); Equal((PolylineSmoothType)smooth,line.SmoothType,"Curve type changed");
        }
        if (!version.HasValue) return;
        using var stream = new MemoryStream(); Check(document.Save(stream,binary),"Legacy affine wire save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"legacy-vertex-affine-{polyface}-{version}-{binary}-{mode}.dxf"),stream.ToArray());
        stream.Position=0;var loaded=DxfDocument.Load(stream) ?? throw new InvalidOperationException("Legacy affine reload");
        EntityObject found=polyface ? loaded.Entities.PolyfaceMeshes.Single() : loaded.Entities.Polylines3D.Single();
        Check(points.SequenceEqual(LegacyAffinePoints(found)),"Exact WCS round trip"); Equal(0,loaded.Objects.Validate().Count,"Reloaded graph");
    }
    private static void LegacyAffineStored(bool polyface,string mode,bool four,bool binary)
    {
        var document=new DxfDocument(); document.Entities.Add(LegacyAffineSubject(polyface));
        using var source=new MemoryStream(); Check(document.Save(source,binary),"Stored setup save"); source.Position=0;
        var loaded=DxfDocument.Load(source)!; EntityObject entity=polyface ? loaded.Entities.PolyfaceMeshes.Single() : loaded.Entities.Polylines3D.Single();
        var children=LegacyAffineChildren(entity); var handles=children.Select(c=>c.Handle).ToArray(); var points=LegacyAffinePoints(entity); var original=points.ToArray();
        var childData=new XData(new ApplicationRegistry("CHILD_AFFINE"));childData.XDataRecord.Add(new XDataRecord(XDataCode.String,"retained"));children[0].XData.Add(childData);
        var a=VertexAffineMatrix(mode);var t=VertexAffineTranslation(mode);VertexAffineApply(entity,a,t,four);
        Check(children.SequenceEqual(LegacyAffineChildren(entity)) && handles.SequenceEqual(LegacyAffineChildren(entity).Select(c=>c.Handle)),"Retained child identities changed");
        Check(ReferenceEquals(points,LegacyAffinePoints(entity)) && ReferenceEquals(childData,children[0].XData["CHILD_AFFINE"]),"Stored object identity changed");
        for(int i=0;i<original.Length;i++)LineReviewNear(a*original[i]+t,points[i],"Stored image");
        using var output=new MemoryStream();Check(loaded.Save(output,binary),"Stored transformed save");output.Position=0;
        var again=DxfDocument.Load(output)!;EntityObject found=polyface ? again.Entities.PolyfaceMeshes.Single() : again.Entities.Polylines3D.Single();
        Check(points.SequenceEqual(LegacyAffinePoints(found)),"Stored coordinates round trip");
        Equal("retained",(string)LegacyAffineChildren(found)[0].XData["CHILD_AFFINE"].XDataRecord[0].Value,"Child XData lost");
    }
    private static void RegisterLegacyVertexAffineTests()
    {
        foreach(bool polyface in new[]{false,true})
        {
            foreach(string mode in VertexAffineModes)foreach(bool four in new[]{false,true})foreach(bool owned in new[]{false,true})
                Run($"legacy-vertex-affine/model/{polyface}/{mode}/{four}/{owned}",()=>LegacyAffineModel(polyface,mode,four,owned));
            foreach(string mode in VertexAffineModes)foreach(var version in SupportedVersions)foreach(bool binary in new[]{false,true})
                Run($"legacy-vertex-affine/wire/{polyface}/{version}/{binary}/{mode}",()=>LegacyAffineModel(polyface,mode,binary,true,0,version,binary));
            foreach(string mode in VertexAffineModes)foreach(bool four in new[]{false,true})foreach(bool binary in new[]{false,true})
                Run($"legacy-vertex-affine/stored/{polyface}/{mode}/{four}/{binary}",()=>LegacyAffineStored(polyface,mode,four,binary));
            foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            {
                string label=BitConverter.DoubleToInt64Bits(invalid).ToString("X16");
                for(int k=0;k<18;k++){int n=k;Run($"legacy-vertex-affine/reject/source/{polyface}/{n}/{label}",()=>{var e=LegacyAffineSubject(polyface);var points=LegacyAffinePoints(e);var p=points[n/3];p[n%3]=invalid;points[n/3]=p;LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});}
                for(int k=0;k<12;k++){int n=k;Run($"legacy-vertex-affine/reject/matrix3/{polyface}/{n}/{label}",()=>{var e=LegacyAffineSubject(polyface);var a=Matrix3.Identity;var t=Vector3.Zero;if(n<9)a[n/3,n%3]=invalid;else t[n-9]=invalid;LegacyAffineReject(e,()=>e.TransformBy(a,t));});}
                for(int k=0;k<16;k++){int n=k;Run($"legacy-vertex-affine/reject/matrix4/{polyface}/{n}/{label}",()=>{var e=LegacyAffineSubject(polyface);var a=Matrix4.Identity;a[n/4,n%4]=invalid;LegacyAffineReject(e,()=>e.TransformBy(a));});}
            }
            for(int k=0;k<4;k++){int n=k;Run($"legacy-vertex-affine/reject/projective/{polyface}/{n}",()=>{var e=LegacyAffineSubject(polyface);var a=Matrix4.Identity;a[3,n]=n==3?2:1e-30;LegacyAffineReject(e,()=>e.TransformBy(a));});}
            foreach(int index in Enumerable.Range(0,4))Run($"legacy-vertex-affine/reject/normal/{polyface}/{index}",()=>
            {
                var e=LegacyAffineSubject(polyface);var normal=new[]{Vector3.Zero,new Vector3(double.NaN,0,1),new Vector3(1,2,3),new Vector3(0,double.PositiveInfinity,1)}[index];
                typeof(EntityObject).GetField("normal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(e,normal);
                LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Identity,Vector3.Zero));
            });
            Run($"legacy-vertex-affine/reject/overflow/{polyface}",()=>{var e=LegacyAffineSubject(polyface);LegacyAffinePoints(e)[5]=new(double.MaxValue,0,0);LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});
            Run($"legacy-vertex-affine/reject/underflow/{polyface}",()=>{var e=LegacyAffineSubject(polyface);LegacyAffinePoints(e)[5]=new(double.Epsilon,0,0);LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Scale(.25),Vector3.Zero));});
            Run($"legacy-vertex-affine/cancellation/{polyface}",()=>{var e=LegacyAffineSubject(polyface);var points=LegacyAffinePoints(e);for(int i=0;i<points.Count;i++)points[i]=new(double.MaxValue,double.MaxValue,3);e.TransformBy(new Matrix3(2,-2,1,0,0,1,0,0,1),Vector3.Zero);foreach(var p in points)Equal(new Vector3(3,3,3),p,"Exact cancellation");});
        }
        foreach(int smooth in new[]{5,6})foreach(string mode in VertexAffineModes)foreach(bool four in new[]{false,true})
            Run($"legacy-vertex-affine/smoothed/{smooth}/{mode}/{four}",()=>LegacyAffineModel(false,mode,four,false,smooth));
        Run("legacy-vertex-affine/reject/polyface-topology",()=>{var e=(PolyfaceMesh)LegacyAffineSubject(true);e.Faces[1].VertexIndexes[0]=7;LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX));});
        Run("legacy-vertex-affine/reject/retained-polyline-count",()=>
        {
            var doc=new DxfDocument();doc.Entities.Add(LegacyAffineSubject(false));using var s=new MemoryStream();doc.Save(s);s.Position=0;
            var e=DxfDocument.Load(s)!.Entities.Polylines3D.Single();e.Vertexes.Add(Vector3.UnitZ);e.ProxyGraphics=new byte[]{1,2,3};
            LegacyAffineReject(e,()=>e.TransformBy(Matrix3.Scale(2),Vector3.UnitX));
        });
        foreach(bool polyface in new[]{false,true})Run($"legacy-vertex-affine/derived-normal/{polyface}",()=>
        {
            EntityObject e=polyface ? new LegacyAffineDerivedMesh() : new LegacyAffineDerivedLine();e.TransformBy(Matrix3.Scale(2),Vector3.UnitX);
            Equal(new Vector3(3,0,0),LegacyAffinePoints(e)[0],"Nonvirtual publication");
        });
    }
    private sealed class LegacyAffineDerivedLine:Polyline3D
    {
        internal LegacyAffineDerivedLine():base(new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ}){}
        public override Vector3 Normal { get=>throw new InvalidOperationException("virtual read"); set=>throw new InvalidOperationException("virtual write"); }
    }
    private sealed class LegacyAffineDerivedMesh:PolyfaceMesh
    {
        internal LegacyAffineDerivedMesh():base(new[]{Vector3.UnitX,Vector3.UnitY,Vector3.UnitZ},new[]{new short[]{1,2,3}}){}
        public override Vector3 Normal { get=>throw new InvalidOperationException("virtual read"); set=>throw new InvalidOperationException("virtual write"); }
    }
}
