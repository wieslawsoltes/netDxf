// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using DxfPoint = netDxf.Entities.Point;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (int kind, string field)[] PrimitiveMutationSlots = {
        (0,"StartPoint"), (0,"EndPoint"), (0,"Thickness"),
        (1,"Position"), (1,"Thickness"), (1,"Rotation"),
        (2,"Origin"), (2,"Direction"), (3,"Origin"), (3,"Direction")
    };
    private static readonly byte[] PrimitiveProxy = { 1, 3, 7, 255 };
    private static EntityObject PrimitiveSubject(int kind, bool tilted = false)
    {
        EntityObject entity = kind switch {
            0 => new Line(new Vector3(1,2,3),new Vector3(4,5,6)) { Thickness = 2 },
            1 => new DxfPoint(new Vector3(1,2,3)) { Thickness = 2, Rotation = 30 },
            2 => new Ray(new Vector3(1,2,3),Vector3.UnitX),
            _ => new XLine(new Vector3(1,2,3),Vector3.UnitX)
        };
        entity.Normal = tilted && kind < 2 ? Vector3.UnitX : Vector3.UnitZ;
        entity.Color = new AciColor(3); entity.IsVisible = false; entity.LinetypeScale = 1.25;
        var data = new XData(new ApplicationRegistry("PRIMITIVE_KEEP"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String,"opaque"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.BinaryData,new byte[] { 17,33,201 }));
        entity.XData.Add(data); entity.ProxyGraphics = PrimitiveProxy;
        return entity;
    }
    private static object PrimitiveGet(EntityObject e, string field) => e.GetType().GetProperty(field)!.GetValue(e)!;
    private static void PrimitiveSet(EntityObject e, string field, object value)
    {
        switch (e) {
            case Line line:
                if(field=="StartPoint") line.StartPoint=(Vector3)value;
                else if(field=="EndPoint") line.EndPoint=(Vector3)value; else line.Thickness=(double)value; break;
            case DxfPoint point:
                if(field=="Position") point.Position=(Vector3)value;
                else if(field=="Thickness") point.Thickness=(double)value; else point.Rotation=(double)value; break;
            case Ray ray:
                if(field=="Origin") ray.Origin=(Vector3)value; else ray.Direction=(Vector3)value; break;
            case XLine xline:
                if(field=="Origin") xline.Origin=(Vector3)value; else xline.Direction=(Vector3)value; break;
        }
    }
    private static double[] PrimitiveState(EntityObject e) => e switch {
        Line l => new[] {l.StartPoint.X,l.StartPoint.Y,l.StartPoint.Z,l.EndPoint.X,l.EndPoint.Y,l.EndPoint.Z,l.Thickness,l.Normal.X,l.Normal.Y,l.Normal.Z},
        DxfPoint p => new[] {p.Position.X,p.Position.Y,p.Position.Z,p.Thickness,p.Rotation,p.Normal.X,p.Normal.Y,p.Normal.Z},
        Ray r => new[] {r.Origin.X,r.Origin.Y,r.Origin.Z,r.Direction.X,r.Direction.Y,r.Direction.Z,r.Normal.X,r.Normal.Y,r.Normal.Z},
        XLine x => new[] {x.Origin.X,x.Origin.Y,x.Origin.Z,x.Direction.X,x.Direction.Y,x.Direction.Z,x.Normal.X,x.Normal.Y,x.Normal.Z},
        _ => throw new ArgumentException(nameof(e))
    };
    private static long[] PrimitiveBits(EntityObject e) => PrimitiveState(e).Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static object PrimitiveInput(EntityObject e, string field, int mode)
    {
        object old=PrimitiveGet(e,field);
        if(field=="Direction") return mode switch {
            0=>Vector3.UnitX,1=>Vector3.UnitY,2=>-Vector3.UnitX,3=>new Vector3(8,0,0),
            4=>new Vector3(double.Epsilon,0,0),_=>new Vector3(double.MaxValue,0,0)};
        if(old is Vector3 v) return mode switch {
            0=>v,1=>new Vector3(-8,9,10),2=>new Vector3(Math.BitIncrement(v.X),v.Y,v.Z),
            3=>new Vector3(v.X,v.Y,v.Z),4=>Vector3.Zero,_=>new Vector3(-1,-2,-3)};
        double x=(double)old;
        return mode switch {0=>x,1=>field=="Rotation"?90.0:-2.0,2=>Math.BitIncrement(x),
            3=>field=="Rotation"?x+360:x,4=>0.0,_=>field=="Rotation"?360.0:BitConverter.Int64BitsToDouble(long.MinValue)};
    }
    private static void PrimitiveData(EntityObject e)
    {
        Equal("opaque",(string)e.XData["PRIMITIVE_KEEP"].XDataRecord[0].Value,"Other application string");
        Check(((byte[])e.XData["PRIMITIVE_KEEP"].XDataRecord[1].Value).SequenceEqual(new byte[] {17,33,201}),"Other application binary");
    }
    private static void RegisterPrimitiveMutationTests()
    {
        foreach(var slot in PrimitiveMutationSlots) foreach(bool owned in new[] {false,true}) for(int mode=0;mode<6;mode++) {
            int m=mode;
            Run($"primitive-mutation/api/{slot.kind}/{slot.field}/{owned}/{m}",()=> {
                var e=PrimitiveSubject(slot.kind); var doc=new DxfDocument(); if(owned) doc.Entities.Add(e);
                var owner=e.Owner; var handle=e.Handle; var layer=e.Layer; var color=e.Color;
                var data=e.XData["PRIMITIVE_KEEP"];var records=data.XDataRecord.ToArray();
                var fields=PrimitiveMutationSlots.Where(s=>s.kind==slot.kind).Select(s=>s.field).Distinct().ToArray();
                var old=fields.ToDictionary(n=>n,n=>PrimitiveGet(e,n));var before=PrimitiveBits(e);
                object value=PrimitiveInput(e,slot.field,m);PrimitiveSet(e,slot.field,value);
                object expected=slot.field=="Rotation" ? MathHelper.NormalizeAngle((double)value)
                    : slot.field=="Direction" ? new Ray(Vector3.Zero,(Vector3)value).Direction : value;
                if(expected is Vector3 v) { RawLinePointBits(v,(Vector3)PrimitiveGet(e,slot.field));
                    Equal(v.IsNormalized,((Vector3)PrimitiveGet(e,slot.field)).IsNormalized,"Complete vector assignment"); }
                else SameDoubleBits((double)expected,(double)PrimitiveGet(e,slot.field),"Stored scalar");
                bool changed=!before.SequenceEqual(PrimitiveBits(e));
                Check(changed?e.ProxyGraphics==null:e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Direct edit/no-op proxy policy");
                foreach(string n in fields.Where(n=>n!=slot.field)) {
                    if(old[n] is Vector3 p) RawLinePointBits(p,(Vector3)PrimitiveGet(e,n));
                    else SameDoubleBits((double)old[n],(double)PrimitiveGet(e,n),"Unrelated geometry");
                }
                Check(ReferenceEquals(owner,e.Owner)&&handle==e.Handle&&ReferenceEquals(layer,e.Layer)&&ReferenceEquals(color,e.Color),"Metadata identity changed");
                Check(ReferenceEquals(data,e.XData["PRIMITIVE_KEEP"])&&records.Zip(data.XDataRecord).All(x=>ReferenceEquals(x.First,x.Second)),"XData identity changed");
                PrimitiveData(e);var clone=(EntityObject)e.Clone(); Check(PrimitiveBits(e).SequenceEqual(PrimitiveBits(clone)),"Clone geometry");
                Check(changed?clone.ProxyGraphics==null:clone.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Clone proxy copy");
                clone.ProxyGraphics=PrimitiveProxy;
                object next=slot.field=="Direction"?(object)Vector3.UnitZ:PrimitiveGet(clone,slot.field) is Vector3?new Vector3(11,22,33):17.0;
                PrimitiveSet(clone,slot.field,next);Check(clone.ProxyGraphics==null,"Clone retained stale proxy");
                Check(changed?e.ProxyGraphics==null:e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Clone edit changed source proxy");
                if(owned) Equal(0,doc.Objects.Validate().Count,"Owned graph");
            });
        }
        foreach(var slot in PrimitiveMutationSlots.Where(s=>s.field!="Direction")) {
            foreach(double input in new[] {double.NaN,BitConverter.Int64BitsToDouble(0x7ff8000000000001),double.PositiveInfinity,double.NegativeInfinity})
                Run($"primitive-mutation/nonfinite/{slot.kind}/{slot.field}/{ParameterBits(input)}",()=> {
                    var e=PrimitiveSubject(slot.kind);object v=PrimitiveGet(e,slot.field) is Vector3?(object)new Vector3(input,2,3):input;
                    PrimitiveSet(e,slot.field,v);Check(e.ProxyGraphics==null,"Nonfinite changed bits retained proxy");
                    e.ProxyGraphics=PrimitiveProxy;var before=PrimitiveBits(e);PrimitiveSet(e,slot.field,v);
                    Check(before.SequenceEqual(PrimitiveBits(e))&&e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Identical nonfinite assignment changed proxy");
                });
            if(PrimitiveGet(PrimitiveSubject(slot.kind),slot.field) is Vector3) {
                for(int axis=0;axis<3;axis++) {int a=axis;
                    Run($"primitive-mutation/signed-zero/{slot.kind}/{slot.field}/{a}",()=> {
                        var e=PrimitiveSubject(slot.kind);PrimitiveSet(e,slot.field,Vector3.Zero);e.ProxyGraphics=PrimitiveProxy;
                        var v=Vector3.Zero;v[a]=BitConverter.Int64BitsToDouble(long.MinValue);PrimitiveSet(e,slot.field,v);
                        Check(e.ProxyGraphics==null,"Signed-zero edit retained proxy");RawLinePointBits(v,(Vector3)PrimitiveGet(e,slot.field));
                    });
                }
                Run($"primitive-mutation/vector-state/{slot.kind}/{slot.field}",()=> {
                    var e=PrimitiveSubject(slot.kind);var v=Vector3.Normalize(new Vector3(3,4,0));
                    PrimitiveSet(e,slot.field,new Vector3(v.X,v.Y,v.Z));e.ProxyGraphics=PrimitiveProxy;PrimitiveSet(e,slot.field,v);
                    Check(((Vector3)PrimitiveGet(e,slot.field)).IsNormalized,"Normalization cache not transferred");
                    Check(e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Cache-only assignment invalidated geometry");
                });
            }
        }
        foreach(int kind in new[] {2,3}) foreach(var input in new[] {Vector3.Zero,new Vector3(double.NaN,0,0),new Vector3(0,double.NaN,0),new Vector3(0,0,double.NaN),
            new Vector3(double.PositiveInfinity,0,0),new Vector3(0,double.PositiveInfinity,0),new Vector3(0,0,double.PositiveInfinity),
            new Vector3(double.NegativeInfinity,0,0),new Vector3(0,double.NegativeInfinity,0),new Vector3(0,0,double.NegativeInfinity)})
            Run($"primitive-mutation/reject-direction/{kind}/{ParameterBits(input.X)}/{ParameterBits(input.Y)}/{ParameterBits(input.Z)}",()=> {
                var e=PrimitiveSubject(kind);var before=PrimitiveBits(e);
                Throws<ArgumentException>(()=>PrimitiveSet(e,"Direction",input));
                Check(before.SequenceEqual(PrimitiveBits(e))&&e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Rejected direction changed source");PrimitiveData(e);
            });
        foreach(int kind in new[] {0,1})
            Run($"primitive-mutation/thickness-zero/{kind}",()=> {
                var e=PrimitiveSubject(kind);PrimitiveSet(e,"Thickness",0.0);e.ProxyGraphics=PrimitiveProxy;
                PrimitiveSet(e,"Thickness",BitConverter.Int64BitsToDouble(long.MinValue));
                SameDoubleBits(BitConverter.Int64BitsToDouble(long.MinValue),(double)PrimitiveGet(e,"Thickness"),"Signed thickness");
                Check(e.ProxyGraphics==null,"Signed thickness edit retained proxy");
            });
        foreach(var version in SupportedVersions) foreach(bool binary in new[] {false,true})
        foreach(bool tilted in new[] {false,true}) for(int placement=0;placement<4;placement++) {int p=placement;
            Run($"primitive-mutation/wire/{version}/{binary}/{tilted}/{p}",()=> {
                var doc=new DxfDocument(version);doc.Comments.Clear();
                var hosts=Enumerable.Range(0,20).Select(row=> {
                    var s=PrimitiveMutationSlots[row/2];var e=PrimitiveSubject(s.kind,tilted);e.Layer=new Layer("PRIMITIVE_"+row.ToString("D2"));
                    PrimitiveSet(e,s.field,PrimitiveInput(e,s.field,row%2));return e;
                }).ToArray();
                if(p==0) foreach(var e in hosts)doc.Entities.Add(e);
                else if(p==1) {doc.Layouts.Add(new Layout("PRIMITIVE_PAPER"));foreach(var e in hosts)doc.Layouts["PRIMITIVE_PAPER"].AssociatedBlock.Entities.Add(e);}
                else {var block=new Block("PRIMITIVE_HOLDER",hosts);if(p==2)doc.Entities.Add(new Insert(block));else doc.Blocks.Add(block);}
                doc.Entities.Add(new Line(new Vector3(17.25,-4.5,2),new Vector3(18.5,9.25,-3)){Layer=new Layer("FOLLOWING")});
                var states=hosts.Select(PrimitiveBits).ToArray();var handles=hosts.Select(e=>e.Handle).ToArray();
                void CheckDocument(DxfDocument d) {
                    var values=d.Blocks.SelectMany(b=>b.Entities).Where(e=>e.Layer.Name.StartsWith("PRIMITIVE_",StringComparison.Ordinal)).OrderBy(e=>e.Layer.Name,StringComparer.Ordinal).ToArray();
                    Equal(20,values.Length,"Host inventory");
                    for(int row=0;row<20;row++) {var e=values[row];Equal(hosts[row].GetType(),e.GetType(),"Host type");Equal(handles[row],e.Handle,"Host identity");
                        Check(states[row].SequenceEqual(PrimitiveBits(e)),"Round-trip geometry");
                        Check(row%2==1?e.ProxyGraphics==null:e.ProxyGraphics!.SequenceEqual(PrimitiveProxy),"Round-trip proxy policy");PrimitiveData(e);}
                    var line=d.Entities.Lines.Single(l=>l.Layer.Name=="FOLLOWING");
                    RawLinePointBits(new Vector3(17.25,-4.5,2),line.StartPoint);RawLinePointBits(new Vector3(18.5,9.25,-3),line.EndPoint);
                    Equal(0,d.Objects.Validate().Count,"Round-trip graph");
                }
                CheckDocument(doc);using var source=new MemoryStream();Check(doc.Save(source,binary),"Source save");
                string stem=$"primitive-mutation-{version}-{binary}-{tilted}-{p}";
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source.ToArray());source.Position=0;
                var loaded=DxfDocument.Load(source)??throw new InvalidOperationException("Source load");CheckDocument(loaded);
                foreach(bool output in new[] {false,true}) {using var stream=new MemoryStream();Check(loaded.Save(stream,output),"Output save");
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),stream.ToArray());stream.Position=0;
                    CheckDocument(DxfDocument.Load(stream)??throw new InvalidOperationException("Output load"));}
                Check(source.CanRead,"Caller stream closed");
            });
        }
    }
}
