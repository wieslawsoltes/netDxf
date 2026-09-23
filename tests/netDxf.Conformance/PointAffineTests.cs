// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using DxfPoint = netDxf.Entities.Point;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly string[] PointAffineModes = {
        "identity", "translate", "nonuniform", "negative", "shear", "mirror", "rotate", "planar"
    };
    private static DxfPoint PointAffineSubject(int normal, int thickness) => new(new Vector3(1,2,3)) {
        Normal = new[] { Vector3.UnitZ, Vector3.UnitX, new Vector3(1,2,3) }[normal],
        Rotation = 30, Thickness = (thickness-1)*1.75, Color = new AciColor(3),
        Layer = new Layer("POINT_AFFINE"), IsVisible = false, ProxyGraphics = new byte[] {1,3,7,255}
    };
    private static long[] PointAffineBits(DxfPoint point) => new[] {
        point.Position.X, point.Position.Y, point.Position.Z, point.Normal.X, point.Normal.Y,
        point.Normal.Z, point.Thickness, point.Rotation
    }.Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void PointAffineAssert(DxfPoint before, DxfPoint after, Matrix3 matrix, Vector3 translation)
    {
        LineReviewNear(matrix*before.Position+translation, after.Position, "Point WCS location");
        Vector3 extrusion=matrix*before.Normal;
        double scale=Math.Max(Math.Abs(extrusion.X),Math.Max(Math.Abs(extrusion.Y),Math.Abs(extrusion.Z)));
        if(scale==0) {
            Equal(0.0,after.Thickness,"Collapsed extrusion");
            LineReviewNear(before.Normal,after.Normal,"Unused direction retained");
        } else {
            double length=(extrusion/scale).Modulus();
            LineReviewNear((extrusion/scale)/length,after.Normal,"Extrusion direction");
            LineReviewNear((before.Thickness*length)*scale,after.Thickness,"Signed extrusion length");
            LineReviewNear(matrix*(before.Normal*before.Thickness),after.Normal*after.Thickness,"Extrusion vector image");
        }
        LineReviewNear(1,after.Normal.Modulus(),"Unit direction");
        // Separate projected-marker reference; it does not assert sheared glyph equivalence.
        if(matrix.Equals(Matrix3.Identity)) SameDoubleBits(before.Rotation,after.Rotation,"Translation retained marker bits");
        else {
            Matrix3 from=MathHelper.ArbitraryAxis(before.Normal), to=MathHelper.ArbitraryAxis(after.Normal).Transpose();
            Vector2 axis=Vector2.Rotate(Vector2.UnitX,before.Rotation*MathHelper.DegToRad);
            Vector3 marker=to*(matrix*(from*new Vector3(axis.X,axis.Y,0)));
            double expected=MathHelper.NormalizeAngle(Vector2.Angle(new Vector2(marker.X,marker.Y))*MathHelper.RadToDeg);
            Check(Math.Abs(expected-after.Rotation)<1e-10,"Projected marker rotation");
        }
        Equal(before.Color.Index,after.Color.Index,"Appearance");
        Equal(before.IsVisible,after.IsVisible,"Visibility");
        Equal(before.Layer.Name,after.Layer.Name,"Layer");
        bool changed=!PointAffineBits(before).SequenceEqual(PointAffineBits(after));
        Check(changed ? after.ProxyGraphics==null : after.ProxyGraphics!.SequenceEqual(before.ProxyGraphics!),"Proxy invalidation");
    }
    private static void RegisterPointAffineTests()
    {
        foreach(string mode in LineReviewModes) for(int normal=0;normal<3;normal++) for(int thickness=0;thickness<3;thickness++)
        foreach(bool four in new[]{false,true}) {
            string m=mode;int n=normal,t=thickness;bool f=four;
            Run($"point-affine/model/{m}/{n}/{t}/{f}",()=>{
                var before=PointAffineSubject(n,t);var point=(DxfPoint)before.Clone();var doc=new DxfDocument();
                doc.Entities.Add(point);string handle=point.Handle;var owner=point.Owner;
                var matrix=LineReviewMatrix(m);var translation=LineReviewTranslation(m);
                if(f)point.TransformBy(LineReviewMatrix4(matrix,translation));else point.TransformBy(matrix,translation);
                PointAffineAssert(before,point,matrix,translation);
                Equal(handle,point.Handle,"Stable handle");Check(ReferenceEquals(owner,point.Owner),"Stable owner");
                Equal(0,doc.Objects.Validate().Count,"Graph");
                SameDoubleBits((t-1)*1.75,before.Thickness,"Clone changed original");
            });
        }
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity}) {
            string label=double.IsNaN(bad)?"nan":bad>0?"positive-infinity":"negative-infinity";
            for(int i=0;i<9;i++) {int at=i;Run($"point-affine/reject/matrix3/{at}/{label}",()=>{
                var a=Matrix3.Identity;a[at/3,at%3]=bad;var p=PointAffineSubject(0,2);
                PointAffineRollback(p,()=>p.TransformBy(a,new Vector3(5,6,7)));});
            }
            for(int i=0;i<16;i++) {int at=i;Run($"point-affine/reject/matrix4/{at}/{label}",()=>{
                var a=Matrix4.Identity;a[at/4,at%4]=bad;var p=PointAffineSubject(0,2);
                PointAffineRollback(p,()=>p.TransformBy(a));});
            }
            for(int i=0;i<3;i++) {int at=i;Run($"point-affine/reject/translation/{at}/{label}",()=>{
                var v=Vector3.Zero;v[at]=bad;var p=PointAffineSubject(0,2);PointAffineRollback(p,()=>p.TransformBy(Matrix3.Identity,v));});
            }
            for(int i=0;i<5;i++) {int at=i;Run($"point-affine/reject/source/{at}/{label}",()=>{
                var p=PointAffineSubject(0,2);
                if(at<3){var v=p.Position;v[at]=bad;p.Position=v;}else if(at==3)p.Thickness=bad;else p.Rotation=bad;
                p.ProxyGraphics=new byte[]{1,3,7,255};
                PointAffineRollback(p,()=>p.TransformBy(Matrix3.Identity,Vector3.Zero));});
            }
        }
        for(int index=0;index<4;index++) {
            int i=index;Run($"point-affine/reject/projective/{i}",()=>{
                var a=Matrix4.Identity;a[3,i]=i==3?2:double.Epsilon;var p=PointAffineSubject(0,2);
                PointAffineRollback(p,()=>p.TransformBy(a));});
        }
        foreach(string fault in new[]{"coordinate-overflow","coordinate-underflow","thickness-overflow","thickness-underflow","direction-underflow"}) {
            string f=fault;Run("point-affine/reject/"+f,()=>{
                var p=PointAffineSubject(0,2);var a=Matrix3.Identity;
                if(f=="coordinate-overflow"){p.Position=new Vector3(double.MaxValue,0,0);a=Matrix3.Scale(2);}
                if(f=="coordinate-underflow"){p.Position=new Vector3(double.Epsilon,0,0);a=Matrix3.Scale(.25);}
                if(f=="thickness-overflow"){p.Position=Vector3.Zero;p.Thickness=double.MaxValue;a=Matrix3.Scale(2);}
                if(f=="thickness-underflow"){p.Thickness=double.Epsilon;a=Matrix3.Scale(.25);}
                if(f=="direction-underflow"){p.Normal=new Vector3(0,1,1);a=Matrix3.Scale(1,1e300,1e-300);}
                p.ProxyGraphics=new byte[]{1,3,7,255};PointAffineRollback(p,()=>p.TransformBy(a,f=="coordinate-underflow"?Vector3.Zero:new Vector3(11,12,13)));
            });
        }
        Run("point-affine/exact-cancellation",()=>{
            var p=new DxfPoint(new Vector3(double.MaxValue,double.MaxValue,0)){Thickness=1};
            p.TransformBy(new Matrix3(2,-2,0, 0,1,0, 0,0,1),Vector3.Zero);
            RawLinePointBits(new Vector3(0,double.MaxValue,0),p.Position);
        });
        Run("point-affine/identity-bits",()=>{
            var p=new DxfPoint(new Vector3(-0.0,2,3)){Thickness=-0.0,Rotation=Math.BitIncrement(30)};
            p.ProxyGraphics=new byte[]{1,3,7,255};var bits=PointAffineBits(p);var proxy=p.ProxyGraphics!;
            p.TransformBy(Matrix4.Identity);
            Check(bits.SequenceEqual(PointAffineBits(p)) && proxy.SequenceEqual(p.ProxyGraphics!),"Identity changed bits/proxy");
        });
        Run("point-affine/translation-orientation-bits",()=>{
            var p=PointAffineSubject(2,0);var bits=PointAffineBits(p);
            p.TransformBy(Matrix3.Identity,new Vector3(4,5,6));
            Check(bits.Skip(3).SequenceEqual(PointAffineBits(p).Skip(3)),"Translation changed extrusion/rotation");
        });
        Run("point-affine/derived-normal",()=>{
            var p=new PointAffineNormalTrap(new Vector3(1,2,3));p.Thickness=2;p.Poison=true;
            p.TransformBy(Matrix3.Scale(2,3,4),Vector3.Zero);
            SameDoubleBits(8,p.Thickness,"Derived thickness");RawLinePointBits(new Vector3(2,6,12),p.Position);
        });
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true})
        for(int normal=0;normal<2;normal++) for(int placement=0;placement<4;placement++) {
            int n=normal,p=placement;Run($"point-affine/wire/{version}/{binary}/{n}/{p}",()=>{
                var doc=new DxfDocument(version);doc.Comments.Clear();
                var hosts=new List<DxfPoint>();
                for(int m=0;m<PointAffineModes.Length;m++)for(int t=0;t<3;t++) {
                    var point=PointAffineSubject(n,t);point.Layer=new Layer($"POINT_AFFINE_{m*3+t:D2}");
                    var keep=new XData(new ApplicationRegistry("POINT_AFFINE_KEEP"));
                    keep.XDataRecord.Add(new XDataRecord(XDataCode.String,"unchanged"));point.XData.Add(keep);
                    var before=(DxfPoint)point.Clone();var matrix=LineReviewMatrix(PointAffineModes[m]);var translation=LineReviewTranslation(PointAffineModes[m]);
                    point.TransformBy(LineReviewMatrix4(matrix,translation));PointAffineAssert(before,point,matrix,translation);
                    hosts.Add(point);
                }
                if(p==0)foreach(var h in hosts)doc.Entities.Add(h);
                else if(p==1) {doc.Layouts.Add(new Layout("POINT_PAPER"));foreach(var h in hosts)doc.Layouts["POINT_PAPER"].AssociatedBlock.Entities.Add(h);}
                else {var block=new Block("POINT_HOLDER",hosts);if(p==2)doc.Entities.Add(new Insert(block));else doc.Blocks.Add(block);}
                var line=new Line(new Vector3(17.25,-4.5,2),new Vector3(18.5,9.25,-3)){Layer=new Layer("FOLLOWING")};doc.Entities.Add(line);
                string stem=$"point-affine-{version}-{binary}-{n}-{p}";
                var handles=hosts.Select(h=>h.Handle).ToArray();
                using var source=new MemoryStream();Check(doc.Save(source,binary),"Point save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source.ToArray());source.Position=0;
                doc=DxfDocument.Load(source)??throw new InvalidOperationException("Point load");PointAffineCheckDocument(doc,hosts,handles);
                foreach(bool output in new[]{false,true}) {
                    using var stream=new MemoryStream();Check(doc.Save(stream,output),"Point resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    var second=DxfDocument.Load(stream)??throw new InvalidOperationException("Point reload");
                    PointAffineCheckDocument(second,hosts,handles);
                }
                Check(source.CanRead,"Caller source closed");
            });
        }
    }
    private sealed class PointAffineNormalTrap : DxfPoint {
        public bool Poison;
        public PointAffineNormalTrap(Vector3 position):base(position){}
        public override Vector3 Normal {
            get {if(Poison)throw new InvalidOperationException("Virtual getter invoked");return base.Normal;}
            set {if(Poison)throw new InvalidOperationException("Virtual setter invoked");base.Normal=value;}
        }
    }
    private static void PointAffineRollback(DxfPoint point,Action action)
    {
        var before=PointAffineBits(point);var proxy=point.ProxyGraphics;var layer=point.Layer;bool rejected=false;
        try{action();}catch(ArgumentException){rejected=true;}catch(NotSupportedException){rejected=true;}catch(InvalidOperationException){rejected=true;}
        Check(rejected,"Invalid transform accepted");
        Check(before.SequenceEqual(PointAffineBits(point)),"Rejected operation mutated geometry");
        Check(proxy!.SequenceEqual(point.ProxyGraphics!) && ReferenceEquals(layer,point.Layer),"Rejected operation changed proxy/metadata");
    }
    private static void PointAffineCheckDocument(DxfDocument doc,IList<DxfPoint> expected,string[] handles)
    {
        var actual=doc.Blocks.SelectMany(b=>b.Entities).OfType<DxfPoint>().OrderBy(p=>p.Layer.Name,StringComparer.Ordinal).ToArray();
        Equal(expected.Count,actual.Length,"Point inventory");
        for(int i=0;i<actual.Length;i++) {
            var a=actual[i];var e=expected[i];Equal(handles[i],a.Handle,"Point handle");
            LineReviewNear(e.Position,a.Position,"Stored WCS point");LineReviewNear(e.Normal,a.Normal,"Stored normal");
            LineReviewNear(e.Thickness,a.Thickness,"Stored thickness");
            Check(Math.Abs(e.Rotation-a.Rotation)<1e-10,"Stored marker rotation");
            Check((e.ProxyGraphics==null && a.ProxyGraphics==null) || (e.ProxyGraphics!=null && e.ProxyGraphics.SequenceEqual(a.ProxyGraphics!)),"Stored proxy");
            Equal("unchanged",(string)a.XData["POINT_AFFINE_KEEP"].XDataRecord.Single().Value,"Other application");
        }
        Equal(0,doc.Objects.Validate().Count,"Point object graph");
        var line=doc.Entities.Lines.Single();RawLinePointBits(new Vector3(17.25,-4.5,2),line.StartPoint);RawLinePointBits(new Vector3(18.5,9.25,-3),line.EndPoint);
    }
}
