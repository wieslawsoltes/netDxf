// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static Polyline2D SafeAffinePolyline(int kind = 0)
    {
        var line = new Polyline2D(new[] {
            new Polyline2DVertex(1,2) { VertexIdentifier = 7 },
            new Polyline2DVertex(4,5) { VertexIdentifier = -9 },
            new Polyline2DVertex(7,1) { VertexIdentifier = 12 }
        }, true) { Elevation = 3, Thickness = 0, LinetypeGeneration = true };
        if (kind == 1) line.Vertexes[0].Bulge = .5;
        if (kind == 2) { line.ConstantWidth = 2; line.Vertexes[1].StartWidthOverride = 3; line.Vertexes[1].EndWidthOverride = 4; }
        line.ProxyGraphics = new byte[] { 2,4,6,8 };
        return line;
    }
    private static string SafeAffineState(Polyline2D line) => JsonSerializer.Serialize(new {
        p = line.Vertexes.Select(v => v == null ? null : new {
            x = ParameterBits(v.Position.X), y = ParameterBits(v.Position.Y), b = ParameterBits(v.Bulge),
            s = v.StartWidthOverride.HasValue ? ParameterBits(v.StartWidth) : null,
            e = v.EndWidthOverride.HasValue ? ParameterBits(v.EndWidth) : null, v.VertexIdentifier }),
        n = ParameterVector(line.Normal), z = ParameterBits(line.Elevation), t = ParameterBits(line.Thickness),
        w = line.ConstantWidth.HasValue ? ParameterBits(line.ConstantWidth.Value) : null,
        line.IsClosed, line.LinetypeGeneration, line.SmoothType, line.Handle, line.ProxyGraphics
    });
    private static void SafeAffineApply(Polyline2D line, Matrix3 matrix, Vector3 move, bool four)
    {
        if (four) line.TransformBy(new Matrix4(matrix.M11,matrix.M12,matrix.M13,move.X,
            matrix.M21,matrix.M22,matrix.M23,move.Y,matrix.M31,matrix.M32,matrix.M33,move.Z,0,0,0,1));
        else line.TransformBy(matrix, move);
    }
    private sealed class SafeAffineDerived : Polyline2D
    {
        internal bool Armed;
        internal int Reads, Writes, Transforms;
        internal Vector3 StoredNormal => base.Normal;
        public override Vector3 Normal
        {
            get { if(Armed) {Reads++;throw new InvalidOperationException("Unexpected overridden normal read");} return base.Normal; }
            set { if(Armed) {Writes++;base.Normal=Vector3.UnitX;throw new InvalidOperationException("Unexpected overridden normal write");} base.Normal=value; }
        }
        public override void TransformBy(Matrix3 matrix, Vector3 move) { Transforms++;base.TransformBy(matrix,move); }
    }
    private static void RegisterPolylineAffineSafetyTests()
    {
        foreach (bool four in new[] { false,true }) foreach (bool owned in new[] { false,true })
            foreach (string fault in new[] { "late-nan", "late-overflow", "infinite-translation", "nan-matrix", "null", "nan-bulge", "nan-elevation", "nan-thickness", "width-underflow" })
            {
                Run($"polyline-affine-safety/reject/{four}/{owned}/{fault}", () => {
                    var line=SafeAffinePolyline();var doc=new DxfDocument();if(owned) doc.Entities.Add(line);
                    Matrix3 m=Matrix3.Identity;Vector3 t=Vector3.UnitX;
                    if(fault=="late-nan") line.Vertexes[2].Position=new(double.NaN,1);
                    if(fault=="late-overflow") {line.Vertexes[2].Position=new(double.MaxValue,1);m=Matrix3.Scale(2);}
                    if(fault=="infinite-translation") t=new(double.PositiveInfinity,0,0);
                    if(fault=="nan-matrix") m.M12=double.NaN;
                    if(fault=="null") line.Vertexes[2]=null!;
                    if(fault=="nan-bulge") line.Vertexes[2].Bulge=double.NaN;
                    if(fault=="nan-elevation") line.Elevation=double.NaN;
                    if(fault=="nan-thickness") line.Thickness=double.NaN;
                    if(fault=="width-underflow") {line.ConstantWidth=double.Epsilon;m=Matrix3.Scale(.125);}
                    line.ProxyGraphics=new byte[]{2,4,6,8};var before=SafeAffineState(line);var owner=line.Owner;var list=line.Vertexes;
                    Exception? error=null;try{SafeAffineApply(line,m,t,four);}catch(Exception e){error=e;}
                    Check(error is ArgumentException or InvalidOperationException or NotSupportedException,"Invalid affine input/result was accepted");
                    Equal(before,SafeAffineState(line),"Rejected affine transform changed source state");
                    Check(ReferenceEquals(owner,line.Owner)&&ReferenceEquals(list,line.Vertexes),"Rejected transform changed identity");
                });
            }
        foreach(bool four in new[]{false,true}) foreach(int kind in new[]{0,1,2})
            foreach(string mode in new[]{"identity","translate","scale","rotate"})
            Run($"polyline-affine-safety/state/{four}/{kind}/{mode}",()=>{
                var line=SafeAffinePolyline(kind);var old=line.Vertexes.ToArray();var list=line.Vertexes;
                string before=SafeAffineState(line);var m=mode=="scale"?Matrix3.Scale(2):mode=="rotate"?new Matrix3(0,-1,0,1,0,0,0,0,1):Matrix3.Identity;
                Vector3 t=mode=="translate"?new(10,20,30):Vector3.Zero;
                SafeAffineApply(line,m,t,four);Check(ReferenceEquals(list,line.Vertexes)&&old.SequenceEqual(line.Vertexes),"Vertex identity changed");
                if(mode=="identity") Equal(before,SafeAffineState(line),"Identity changed components/proxy");
                else Check(line.ProxyGraphics==null,"Changed transform retained stale proxy");
                Equal((int?)7,line.Vertexes[0].VertexIdentifier,"ID changed");
                Equal(kind==1?.5:0,line.Vertexes[0].Bulge,"Bulge changed under direct similarity");
                if(kind==2) {Equal(mode=="scale"?4.0:2.0,line.ConstantWidth!.Value,"Width scale");Check(!line.Vertexes[0].StartWidthOverride.HasValue,"Absent width materialized");}
            });
        foreach(bool four in new[]{false,true}) foreach(string mode in new[]{"shear-plane","normal-shear","thickness","mirror-arc","nonuniform-arc"})
            Run($"polyline-affine-safety/plane/{four}/{mode}",()=>{
                var line=SafeAffinePolyline(mode.Contains("arc")?1:0);
                Matrix3 m=mode=="shear-plane"?new(1,0,0,0,1,0,.5,0,1):mode=="normal-shear"?new(1,0,2,0,1,0,0,0,1):
                    mode=="mirror-arc"?Matrix3.Scale(-1,1,1):mode=="nonuniform-arc"?Matrix3.Scale(2,3,1):Matrix3.Scale(2,2,4);
                if(mode=="thickness")line.Thickness=-2;
                var points=line.Vertexes.Select(v=>new Vector3(v.Position.X,v.Position.Y,line.Elevation)).ToArray();
                if(mode=="nonuniform-arc") {var before=SafeAffineState(line);Throws<NotSupportedException>(()=>SafeAffineApply(line,m,Vector3.Zero,four));Equal(before,SafeAffineState(line),"Unsupported arc mutated source");return;}
                SafeAffineApply(line,m,Vector3.Zero,four);
                for(int i=0;i<points.Length;i++) {
                    var p=line.Vertexes[i].Position;var actual=MathHelper.ArbitraryAxis(line.Normal)*new Vector3(p.X,p.Y,line.Elevation);
                    var expected=m*points[i];Near(expected.X,actual.X,"Affine WCS X");Near(expected.Y,actual.Y,"Affine WCS Y");Near(expected.Z,actual.Z,"Affine WCS Z");
                }
                if(mode=="thickness")Near(-8,line.Thickness,"Extrusion thickness failed to scale");
                if(mode=="mirror-arc")RawLinePointBits(new(0,0,-1),line.Normal);
            });
        foreach(bool four in new[]{false,true})
            Run($"polyline-affine-safety/alias/{four}",()=>{
                var line=SafeAffinePolyline();line.Vertexes.Add(line.Vertexes[0]);
                SafeAffineApply(line,Matrix3.Identity,Vector3.UnitX,four);
                Equal(2.0,line.Vertexes[0].Position.X,"Aliased vertex transformed twice");Check(ReferenceEquals(line.Vertexes[0],line.Vertexes[3]),"Alias replaced");
            });
        foreach(double epsilon in new[]{1e-12,1.0,100.0})
            Run($"polyline-affine-safety/epsilon/{epsilon}",()=>{
                var line=SafeAffinePolyline(1);double old=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;Throws<NotSupportedException>(()=>line.TransformBy(Matrix3.Scale(2,3,1),Vector3.Zero));}
                finally{MathHelper.Epsilon=old;}
            });
        foreach(int row in new[]{0,1,2,3})
            Run($"polyline-affine-safety/projective/{row}",()=>{
                var line=SafeAffinePolyline();var before=SafeAffineState(line);var m=Matrix4.Identity;m[3,row]=row==3?2:.1;
                Throws<NotSupportedException>(()=>line.TransformBy(m));Equal(before,SafeAffineState(line),"Projective transform mutated source");
            });
        foreach(bool four in new[]{false,true}) foreach(int kind in new[]{0,1,2})
            Run($"polyline-affine-safety/callback/{four}/{kind}",()=>{
                var line=new SafeAffineDerived();line.Vertexes.AddRange(SafeAffinePolyline(kind).Vertexes);
                line.Elevation=3;line.ProxyGraphics=new byte[]{2,4,6,8};line.Armed=true;
                SafeAffineApply(line,Matrix3.Scale(2),new(10,20,30),four);
                Equal(0,line.Reads+line.Writes,"Transform invoked overridden normal accessor");Equal(1,line.Transforms,"Lost virtual Matrix3 dispatch");
                RawLinePointBits(Vector3.UnitZ,line.StoredNormal);Near(36,line.Elevation,"Derived plane elevation");
                Check(line.ProxyGraphics==null,"Derived geometry retained proxy");
            });
        foreach(bool four in new[]{false,true}) foreach(PolylineSmoothType smooth in new[]{PolylineSmoothType.NoSmooth,PolylineSmoothType.Quadratic,PolylineSmoothType.Cubic})
            foreach(Vector3 normal in new[]{Vector3.UnitZ,-Vector3.UnitZ,Vector3.UnitX})
            Run($"polyline-affine-safety/orientation/{four}/{smooth}/{string.Join('-',ParameterVector(normal))}",()=>{
                var line=SafeAffinePolyline();line.SmoothType=smooth;line.Normal=normal;line.Thickness=-2;
                var axes=MathHelper.ArbitraryAxis(normal);var points=line.Vertexes.Select(v=>axes*new Vector3(v.Position.X,v.Position.Y,3)).ToArray();
                Matrix3 m=new(0,0,1,0,2,0,-1,0,0);Vector3 t=new(10,20,30);
                SafeAffineApply(line,m,t,four);
                for(int i=0;i<points.Length;i++) {
                    var actual=MathHelper.ArbitraryAxis(line.Normal)*new Vector3(line.Vertexes[i].Position.X,line.Vertexes[i].Position.Y,line.Elevation);
                    var expected=m*points[i]+t;Near(expected.X,actual.X,"Oriented X");Near(expected.Y,actual.Y,"Oriented Y");Near(expected.Z,actual.Z,"Oriented Z");
                }
                Equal(smooth,line.SmoothType,"Smoothing changed");
                var actualExtrusion=line.Normal*line.Thickness;var expectedExtrusion=m*(normal*-2);
                Near(expectedExtrusion.X,actualExtrusion.X,"Extrusion X");Near(expectedExtrusion.Y,actualExtrusion.Y,"Extrusion Y");Near(expectedExtrusion.Z,actualExtrusion.Z,"Extrusion Z");
            });
        foreach(bool four in new[]{false,true}) foreach(int count in new[]{0,1})
            Run($"polyline-affine-safety/degenerate/{four}/{count}",()=>{
                var line=new Polyline2D();if(count==1)line.Vertexes.Add(new Polyline2DVertex(1,2));line.Elevation=3;line.Thickness=-2;
                line.ProxyGraphics=new byte[]{2,4};SafeAffineApply(line,Matrix3.Scale(2,2,4),new(10,20,30),four);
                Equal(count,line.Vertexes.Count,"Degenerate count changed");Near(42,line.Elevation,"Degenerate plane");Near(-8,line.Thickness,"Degenerate thickness");Check(line.ProxyGraphics==null,"Degenerate changed plane proxy");
            });
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true}) foreach(int mode in new[]{0,1,2,3})
            Run($"polyline-affine-safety/plane-wire/{version}/{binary}/{mode}",()=>{
                var line=SafeAffinePolyline(mode==2?1:0);
                if(version < DxfVersion.AutoCad2013) foreach(var vertex in line.Vertexes) vertex.VertexIdentifier=null;
                Matrix3 m=mode==0?new(1,0,0,0,1,0,.5,0,1):mode==1?new(1,0,2,0,1,0,0,0,1):
                    mode==2?Matrix3.Scale(-1,1,1):new Matrix3(1,.5,0,0,1,0,0,0,1);
                SafeAffineApply(line,m,new(10,20,30),true);
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(line);
                using var output=new MemoryStream();Check(doc.Save(output,binary),"Plane output failed");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polyline-affine-safety-plane-{version}-{binary}-{mode}.dxf"),output.ToArray());
            });
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true})foreach(int kind in new[]{0,1,2})
            Run($"polyline-affine-safety/wire/{version}/{binary}/{kind}",()=>{
                var line=SafeAffinePolyline(kind);line.Thickness=-2;
                // Group 91 IDs retain their existing R2013+ typed admission.
                if(version < DxfVersion.AutoCad2013) foreach(var vertex in line.Vertexes) vertex.VertexIdentifier=null;
                SafeAffineApply(line,Matrix3.Scale(2,2,4),new(10,20,30),true);
                Check(line.ProxyGraphics==null,"Stale proxy before wire");var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(line);
                using var output=new MemoryStream();Check(doc.Save(output,binary),"Polyline affine save failed");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polyline-affine-safety-{version}-{binary}-{kind}.dxf"),output.ToArray());
                output.Position=0;var loaded=DxfDocument.Load(output)!.Entities.Polylines2D.Single();
                for(int i=0;i<3;i++) {Near(line.Vertexes[i].Position.X,loaded.Vertexes[i].Position.X,"Reload X");Near(line.Vertexes[i].Position.Y,loaded.Vertexes[i].Position.Y,"Reload Y");}
                Near(42,loaded.Elevation,"Reload plane elevation");Near(-8,loaded.Thickness,"Reload thickness");Check(loaded.ProxyGraphics==null,"Reload stale proxy");
            });
    }
}
