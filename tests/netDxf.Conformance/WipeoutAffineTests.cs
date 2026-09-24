// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly byte[] WipeoutAffineProxy = Enumerable.Range(0, 300).Select(i => (byte)(i * 29)).ToArray();
    private static readonly Matrix3[] WipeoutAffineMaps = {
        Matrix3.Identity, Matrix3.Identity, Matrix3.Scale(2,3,4),
        new(.6,-.8,0, .8,.6,0, 0,0,1), new(1,.5,0, 0,1,0, 0,0,1),
        new(1,0,.5, .5,1,0, .25,.75,1), Matrix3.Scale(-1,1,1),
        Matrix3.Scale(1,1,0), new(0,-1,0, 1,0,0, 0,0,1)
    };
    private static Vector3 WipeoutTranslation(int map) => map == 0 ? Vector3.Zero : new Vector3(7,-11,13);
    private static Wipeout WipeoutSubject(bool polygon, int plane)
    {
        var boundary = polygon ? new ClippingBoundary(new[] { new Vector2(1,-2),new Vector2(5,-2),new Vector2(6,1),new Vector2(3,4),new Vector2(0,2) })
            : new ClippingBoundary(new Vector2(1,-2), new Vector2(5,3));
        var item = new Wipeout(boundary) { Elevation=2.5, Normal=plane==0 ? Vector3.UnitZ : plane==1 ? -Vector3.UnitZ : new Vector3(0,3,4), Color=new AciColor(4) };
        var data=new XData(new ApplicationRegistry("WIPEOUT_AFFINE_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String,"unchanged"));item.XData.Add(data);
        return item;
    }
    private static Vector3[] WipeoutWorld(Wipeout item)
    {
        var p=item.ClippingBoundary.Vertexes.ToArray();
        if(item.ClippingBoundary.Type==ClippingBoundaryType.Rectangular)
            p=new[] {p[0],new Vector2(p[1].X,p[0].Y),p[1],new Vector2(p[0].X,p[1].Y)};
        var axes=MathHelper.ArbitraryAxis(item.Normal);
        return p.Select(v=>axes*new Vector3(v.X,v.Y,item.Elevation)).ToArray();
    }
    private static void WipeoutCheckPoints(Vector3[] expected,Wipeout item)
    {
        var actual=WipeoutWorld(item);Equal(expected.Length,actual.Length,"Wipeout expanded corner count");
        // Rectangular representations can reorder equivalent corners after a quarter-turn.
        Check(expected.All(a=>actual.Any(b=>(a-b).Modulus()<=1e-10+2e-12*a.Modulus())),"Wipeout WCS footprint changed");
        Check(actual.All(a=>expected.Any(b=>(a-b).Modulus()<=1e-10+2e-12*a.Modulus())),"Wipeout gained an incorrect corner");
    }
    private static void WipeoutApply(Wipeout item,int map,bool matrix4)
    {
        if(matrix4)item.TransformBy(LineReviewMatrix4(WipeoutAffineMaps[map],WipeoutTranslation(map)));
        else item.TransformBy(WipeoutAffineMaps[map],WipeoutTranslation(map));
    }
    private static void RegisterWipeoutAffineTests()
    {
        RegisterWipeoutBasisTests();
        foreach(bool polygon in new[]{false,true})for(int plane=0;plane<3;plane++)for(int map=0;map<WipeoutAffineMaps.Length;map++)
        foreach(bool matrix4 in new[]{false,true})for(int payload=0;payload<3;payload++)
        {
            int p=plane,m=map,c=payload;
            Run($"wipeout-affine/api/{polygon}/{p}/{m}/{matrix4}/{c}",()=>{
                var item=WipeoutSubject(polygon,p);var doc=new DxfDocument();doc.Entities.Add(item);
                item.ProxyGraphics=c==0?null:c==1?Array.Empty<byte>():WipeoutAffineProxy;
                var original=(Wipeout)item.Clone();var boundary=item.ClippingBoundary;var owner=item.Owner;string handle=item.Handle;
                var xdata=item.XData["WIPEOUT_AFFINE_KEEP"];
                var expected=WipeoutWorld(item).Select(v=>WipeoutAffineMaps[m]*v+WipeoutTranslation(m)).ToArray();
                WipeoutApply(item,m,matrix4);WipeoutCheckPoints(expected,item);
                if(m==0){Check(ReferenceEquals(boundary,item.ClippingBoundary),"Identity replaced boundary");RawLinePointBits(original.Normal,item.Normal);SameDoubleBits(original.Elevation,item.Elevation,"Identity elevation");}
                Check(HatchGraphicsSameProxy(m==0?original.ProxyGraphics:null,item.ProxyGraphics),"Transformed proxy policy");
                Check(ReferenceEquals(owner,item.Owner)&&item.Handle==handle,"Owner/handle changed");
                Check(ReferenceEquals(xdata,item.XData["WIPEOUT_AFFINE_KEEP"]),"XData identity changed");
                WipeoutCheckPoints(WipeoutWorld(original),original);
                var copy=(Wipeout)item.Clone();Check(!ReferenceEquals(copy.ClippingBoundary,item.ClippingBoundary),"Cloned clipping aliases source");
                WipeoutCheckPoints(expected,copy);copy.Elevation+=1;WipeoutCheckPoints(expected,item);
                if(!polygon&&p==0&&m is 3 or 4 or 5)Equal(ClippingBoundaryType.Polygonal,item.ClippingBoundary.Type,"Nonaxis rectangle was not promoted");
                if(!polygon&&p==0&&m is 0 or 1 or 2 or 6 or 7 or 8)Equal(ClippingBoundaryType.Rectangular,item.ClippingBoundary.Type,"Axis-aligned image lost rectangle representation");
                Equal(0,doc.Objects.Validate().Count,"Wipeout object graph");
            });
        }
        foreach(bool polygon in new[]{false,true})foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        for(int component=0;component<16;component++)
        {
            int c=component;double bad=invalid;
            Run($"wipeout-affine/reject/nonfinite/{polygon}/{c}/{ParameterBits(bad)}",()=>{
                var item=WipeoutSubject(polygon,2);var matrix=Matrix4.Identity;matrix[c/4,c%4]=bad;
                WipeoutReject(item,()=>item.TransformBy(matrix));
            });
        }
        foreach(bool polygon in new[]{false,true})for(int component=0;component<4;component++)
        {
            int c=component;
            Run($"wipeout-affine/reject/projective/{polygon}/{c}",()=>{
                var item=WipeoutSubject(polygon,0);var matrix=Matrix4.Identity;matrix[3,c]=c==3?2:double.Epsilon;
                WipeoutReject(item,()=>item.TransformBy(matrix));
            });
        }
        foreach(bool polygon in new[]{false,true})foreach(bool matrix4 in new[]{false,true})
        foreach(var matrix in new[]{Matrix3.Zero,Matrix3.Scale(1,0,0),Matrix3.Scale(double.MaxValue)})
        {
            Matrix3 captured=matrix;
            Run($"wipeout-affine/reject/rank-overflow/{polygon}/{matrix4}/{matrix.M11}/{matrix.M22}",()=>{
                var item=WipeoutSubject(polygon,0);
                WipeoutReject(item,()=>{if(matrix4)item.TransformBy(LineReviewMatrix4(captured,Vector3.Zero));else item.TransformBy(captured,Vector3.Zero);});
            });
        }
        foreach(bool polygon in new[]{false,true})for(int edit=0;edit<5;edit++)
        {
            int e=edit;
            Run($"wipeout-affine/edit/{polygon}/{e}",()=>{
                var item=WipeoutSubject(polygon,0);item.ProxyGraphics=WipeoutAffineProxy;var boundary=item.ClippingBoundary;
                if(e==0)item.Elevation=item.Elevation;
                else if(e==1)item.ClippingBoundary=boundary;
                else if(e==2)Throws<ArgumentNullException>(()=>item.ClippingBoundary=null!);
                else if(e==3)item.Elevation+=1;
                else item.ClippingBoundary=(ClippingBoundary)boundary.Clone();
                Check(HatchGraphicsSameProxy(e<3?WipeoutAffineProxy:null,item.ProxyGraphics),"Edit cache policy");
            });
        }
        Run("wipeout-affine/normal-callback",()=>{
            var item=new WipeoutNormalTrap();item.ProxyGraphics=WipeoutAffineProxy;
            item.TransformBy(Matrix3.Scale(2),new Vector3(1,2,3));
            Check(item.ProxyGraphics==null,"Transform did not invalidate");Equal(0,item.Calls,"Virtual Normal callback invoked");
        });
        Run("wipeout-affine/matrix4-callback",()=>{
            var item=new WipeoutTransformTrap();var matrix=Matrix4.Identity;matrix.M44=2;
            Throws<NotSupportedException>(()=>item.TransformBy(matrix));Equal(0,item.Calls,"Invalid Matrix4 reached virtual affine callback");
            item.TransformBy(Matrix4.Identity);Equal(1,item.Calls,"Valid Matrix4 lost virtual affine dispatch");
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
        foreach(bool polygon in new[]{false,true})for(int placement=0;placement<4;placement++)
        {
            int p=placement;
            Run($"wipeout-affine/wire/{version}/{binary}/{polygon}/{p}",()=>{
                var doc=new DxfDocument(version);doc.Comments.Clear();var items=new List<Wipeout>();
                for(int plane=0;plane<3;plane++)for(int map=0;map<WipeoutAffineMaps.Length;map++){
                    var item=WipeoutSubject(polygon,plane);item.Layer=new Layer($"WA_{plane}_{map}");item.ProxyGraphics=WipeoutAffineProxy;
                    WipeoutApply(item,map,(map&1)!=0);items.Add(item);
                }
                if(p==0)doc.Entities.Add(items);
                else if(p==1){doc.Layouts.Add(new Layout("WA_PAPER"));foreach(var item in items)doc.Layouts["WA_PAPER"].AssociatedBlock.Entities.Add(item);}
                else{var block=new Block("WA_HOLDER",items);if(p==2)doc.Entities.Add(new Insert(block));else doc.Blocks.Add(block);}
                doc.Entities.Add(new Line(new Vector3(17.25,-4.5,2),new Vector3(18.5,9.25,-3)));
                string[] handles=items.Select(i=>i.Handle).ToArray();
                string stem=$"wipeout-affine-{version}-{binary}-{polygon}-{p}";
                using var source=new MemoryStream();Check(doc.Save(source,binary),"Wipeout source save");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source.ToArray());source.Position=0;
                var loaded=DxfDocument.Load(source)??throw new InvalidOperationException("Wipeout source load");
                WipeoutCheckDocument(loaded,polygon,handles);
                foreach(bool output in new[]{false,true}){
                    using var stream=new MemoryStream();Check(loaded.Save(stream,output),"Wipeout resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    WipeoutCheckDocument(DxfDocument.Load(stream)??throw new InvalidOperationException("Wipeout reload"),polygon,handles);
                }
                Check(source.CanRead,"Load closed caller stream");
            });
        }
    }
    private static void RegisterWipeoutBasisTests()
    {
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
        for(int shape=0;shape<3;shape++)for(int basis=0;basis<3;basis++)
        {
            int sh=shape,b=basis;
            Run($"wipeout-basis/wire/{version}/{binary}/{sh}/{b}",()=>{
                var doc=new DxfDocument(version);doc.Comments.Clear();
                var item=new Wipeout(0,0,4,3){ProxyGraphics=WipeoutAffineProxy};doc.Entities.Add(item);
                using var seed=new MemoryStream();Check(doc.Save(seed,binary),"Basis seed");
                var raw=LoadRaw(seed.ToArray());var record=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="WIPEOUT");
                Vector3 u=b==0?new(4,0,0):b==1?new(0,2,1):new(-.6,.8,0);
                Vector3 v=b==0?new(0,2,0):b==1?new(-3,1,0):new(.8,.6,0);
                var position=new Vector3(3,-5,7);const double height=3;
                var pixels=sh==0?new[]{new Vector2(-.5,-.5),new Vector2(2.5,1.5)}:
                    new[]{new Vector2(-.5,-.5),new Vector2(2.5,-.5),new Vector2(2.5,1.5),new Vector2(.5,2.5),new Vector2(-.5,1.5)};
                var emitted=sh==1?pixels.Append(pixels[0]).ToArray():pixels;
                var numbers=new Dictionary<short,double>{{10,position.X},{20,position.Y},{30,position.Z},
                    {11,u.X},{21,u.Y},{31,u.Z},{12,v.X},{22,v.Y},{32,v.Z},{13,4},{23,height}};
                var tags=record.Tags.Where(t=>t.Code!=14&&t.Code!=24).Select(t=>numbers.TryGetValue(t.Code,out double value)?new DxfTag(t.Code,value):
                    t.Code==71?new DxfTag(71,(short)(sh==0?1:2)):t.Code==91?new DxfTag(91,emitted.Length):t).ToList();
                int at=tags.FindIndex(t=>t.Code==91)+1;
                foreach(var point in emitted){tags.Insert(at++,new DxfTag(14,point.X));tags.Insert(at++,new DxfTag(24,point.Y));}
                raw=raw.WithRecord(record,tags);byte[] bytes=SaveRaw(raw,binary);
                string stem=$"wipeout-basis-{version}-{binary}-{sh}-{b}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),bytes);
                if(sh==0)pixels=new[]{pixels[0],new Vector2(pixels[1].X,pixels[0].Y),pixels[1],new Vector2(pixels[0].X,pixels[1].Y)};
                Vector3[] expected=pixels.Select(p=>position+u*(p.X+.5)+v*(height-.5-p.Y)).ToArray();
                using var input=new MemoryStream(bytes);var loaded=DxfDocument.Load(input)??throw new InvalidOperationException("Basis load");
                void CheckBasis(DxfDocument d){var w=d.Entities.Wipeouts.Single();WipeoutCheckPoints(expected,w);Equal(item.Handle,w.Handle,"Basis handle");
                    Check(HatchGraphicsSameProxy(WipeoutAffineProxy,w.ProxyGraphics),"Hydration lost proxy");Equal(0,d.Objects.Validate().Count,"Basis graph");}
                CheckBasis(loaded);
                foreach(bool output in new[]{false,true}){
                    using var stream=new MemoryStream();Check(loaded.Save(stream,output),"Basis resave");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    CheckBasis(DxfDocument.Load(stream)??throw new InvalidOperationException("Basis reload"));
                }
                Check(input.CanRead,"Basis load closed stream");
            });
        }
    }
    private static void WipeoutCheckDocument(DxfDocument doc,bool polygon,string[] handles)
    {
        var items=doc.Blocks.SelectMany(b=>b.Entities).OfType<Wipeout>().OrderBy(w=>w.Layer.Name,StringComparer.Ordinal).ToArray();
        Equal(27,items.Length,"Wipeout record count");
        for(int row=0;row<items.Length;row++){
            int plane=row/9,map=row%9;var item=items[row];Equal(handles[row],item.Handle,"Wipeout handle");
            var expected=WipeoutWorld(WipeoutSubject(polygon,plane)).Select(v=>WipeoutAffineMaps[map]*v+WipeoutTranslation(map)).ToArray();
            WipeoutCheckPoints(expected,item);Check(HatchGraphicsSameProxy(map==0?WipeoutAffineProxy:null,item.ProxyGraphics),"Reloaded proxy presence/bytes");
            Equal("unchanged",(string)item.XData["WIPEOUT_AFFINE_KEEP"].XDataRecord.Single().Value,"Reloaded XData");
            Equal((short)4,item.Color.Index,"Reloaded color");
        }
        var line=doc.Entities.Lines.Single();RawLinePointBits(new(17.25,-4.5,2),line.StartPoint);RawLinePointBits(new(18.5,9.25,-3),line.EndPoint);
        Equal(0,doc.Objects.Validate().Count,"Loaded graph");
    }
    private static void WipeoutReject(Wipeout item,Action action)
    {
        item.ProxyGraphics=WipeoutAffineProxy;var boundary=item.ClippingBoundary;var normal=item.Normal;double elevation=item.Elevation;
        bool refused=false;try{action();}catch(ArgumentException){refused=true;}catch(NotSupportedException){refused=true;}catch(InvalidOperationException){refused=true;}
        Check(refused,"Invalid transform accepted");Check(ReferenceEquals(boundary,item.ClippingBoundary),"Rejected transform replaced boundary");
        RawLinePointBits(normal,item.Normal);SameDoubleBits(elevation,item.Elevation,"Rejected elevation");
        Check(HatchGraphicsSameProxy(WipeoutAffineProxy,item.ProxyGraphics),"Rejected transform cleared cache");
    }
    private sealed class WipeoutNormalTrap:Wipeout
    {
        internal int Calls;internal WipeoutNormalTrap():base(0,0,4,3){}
        public override Vector3 Normal{get{Calls++;throw new InvalidOperationException("Normal getter");}set{Calls++;throw new InvalidOperationException("Normal setter");}}
    }
    private sealed class WipeoutTransformTrap:Wipeout
    {
        internal int Calls;internal WipeoutTransformTrap():base(0,0,4,3){}
        public override void TransformBy(Matrix3 matrix,Vector3 translation){Calls++;}
    }
}
