// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly (double Axis, double Ratio, double First, double Last, bool Full)[] EllipseIoCases = {
        (4, .5, 0, 2*Math.PI, true),
        (4, .5, .3, .3+1e-11, false),
        (4, .5, 0, 2*Math.PI-1e-11, false),
        (4, .5, 1e-13, 2e-13, false),
        (4, .5, 5.5, .4, false),
        (4, .5, -Math.PI/2, 0, false),
        (4, .5, Math.PI, 3*Math.PI/2, false),
        (1e-200, .5, .25, 2.5, false),
        (1e-310, .5, .25, 2.5, false),
        (1e200, .5, .25, 2.5, false),
        (1e307, .5, .25, 2.5, false),
        (4, 1e-200, Math.PI/2, Math.PI, false),
        (4, .5, .3, .3, true)
    };

    private static byte[] EllipseIoSource(DxfVersion version, bool binary, bool block, int kind)
    {
        var spec = EllipseIoCases[kind];
        var doc = new DxfDocument(version); doc.Comments.Clear();
        var e = new Ellipse(new Vector3(1,2,3), 8,4) { Color = new AciColor(3), ProxyGraphics = new byte[] { 1,5,9 } };
        if (block) doc.Entities.Add(new Insert(new Block("ELLIPSE_IO",new EntityObject[] {e})));
        else doc.Entities.Add(e);
        using var stream = new MemoryStream(); Check(doc.Save(stream,binary),"Seed drawing save");
        var raw = LoadRaw(stream.ToArray()); var record = RawEllipseRecord(raw);
        // Orthogonal +Y axis also reproduces epsilon-dependent axis orientation.
        var values = new Dictionary<short,double> { [11]=0,[21]=spec.Axis,[31]=0,
            [40]=spec.Ratio,[41]=spec.First,[42]=spec.Last };
        var tags = record.Tags.Select(t => values.TryGetValue(t.Code,out double v) ? new DxfTag(t.Code,v) : t).ToList();
        return SaveRaw(raw.WithRecord(record,tags),binary);
    }
    private static Ellipse EllipseIoEntity(DxfDocument doc, bool block)
        => block ? (Ellipse)doc.Blocks["ELLIPSE_IO"].Entities.Single() : doc.Entities.Ellipses.Single();
    private static double EllipseIoPhase(double p)
    { p %= 2*Math.PI; if(p<0)p+=2*Math.PI; return p==2*Math.PI||p==0?0:p; }
    private static void EllipseIoCheckPacket(byte[] bytes,int kind)
    {
        var spec=EllipseIoCases[kind];var raw=LoadRaw(bytes);var record=RawEllipseRecord(raw);
        double Value(short code)=>(double)record.Tags.Single(t=>t.Code==code).Value;
        Check(Math.Abs(Value(21)/spec.Axis-1)<2e-12,"Wire major semi-axis changed scale");
        SameDoubleBits(0,Value(11),"Quarter-turn major X residual");SameDoubleBits(0,Value(31),"Major plane");
        Check(Math.Abs(Value(40)/spec.Ratio-1)<2e-12,"Wire ratio changed");
        if(spec.Full) {SameDoubleBits(0,Value(41),"Full start");SameDoubleBits(2*Math.PI,Value(42),"Full end");}
        else
        {
            Check(EllipseIoPhase(Value(41))!=EllipseIoPhase(Value(42)),"Wire endpoints collapsed");
            foreach(var pair in new[]{(spec.First,Value(41)),(spec.Last,Value(42))})
                Check(Math.Abs(Math.IEEERemainder(pair.Item1-pair.Item2,2*Math.PI))<8e-14,"Eccentric parameter changed");
        }
        Check(record.Tags.Any(t=>t.Code==310),"IO lost source proxy");
    }
    private static void RegisterEllipseIoParameterTests()
    {
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true})
            foreach(bool block in new[]{false,true}) for(int kind=0;kind<EllipseIoCases.Length;kind++)
            {
                int k=kind;Run($"ellipse-io/roundtrip/{version}/{binary}/{block}/{k}",()=>
                {
                    var spec=EllipseIoCases[k];byte[] source=EllipseIoSource(version,binary,block,k);
                    using var input=new MemoryStream(source);var doc=DxfDocument.Load(input)??throw new InvalidOperationException("Typed ellipse load failed");
                    var e=EllipseIoEntity(doc,block);Equal(spec.Full,e.IsFullEllipse,"Typed closure");
                    Check(Math.Abs(e.MajorAxis/(2*spec.Axis)-1)<2e-12,"Typed major scale");
                    Check(Math.Abs(e.MinorAxis/(2*spec.Axis*spec.Ratio)-1)<2e-12,"Typed minor scale");
                    SameDoubleBits(90,e.Rotation,"Typed major orientation");
                    RawLinePointBits(new(1,2,3),e.Center);Equal((short)3,e.Color.Index,"Appearance");
                    Check(e.ProxyGraphics!.SequenceEqual(new byte[]{1,5,9}),"Typed source proxy");
                    Equal(0,doc.Objects.Validate().Count,"Typed source graph");
                    string stem=$"ellipse-io-{version}-{binary}-{block}-{k}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-source.dxf"),source);
                    foreach(bool output in new[]{false,true})
                    {
                        using var stream=new MemoryStream();Check(doc.Save(stream,output),"Typed ellipse resave");
                        byte[] bytes=stream.ToArray();EllipseIoCheckPacket(bytes,k);
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+$"-{output}.dxf"),bytes);
                        stream.Position=0;var second=DxfDocument.Load(stream)??throw new InvalidOperationException("Second typed load failed");
                        Equal(spec.Full,EllipseIoEntity(second,block).IsFullEllipse,"Second closure");
                        Equal(0,second.Objects.Validate().Count,"Second graph");
                    }
                    Check(input.CanRead,"Input ownership changed");
                });
            }
        foreach(double epsilon in new[]{1e-12,1e-3,100.0})foreach(int kind in new[]{1,2,3,7,8})
            Run($"ellipse-io/epsilon/{epsilon}/{kind}",()=>
            {
                byte[] source=EllipseIoSource(DxfVersion.AutoCad2018,false,false,kind);double old=MathHelper.Epsilon;
                try
                {
                    if (epsilon < 1)
                    {
                        MathHelper.Epsilon=epsilon;using var input=new MemoryStream(source);
                        var doc=DxfDocument.Load(input)??throw new InvalidOperationException("Epsilon affected load");
                        Check(!doc.Entities.Ellipses.Single().IsFullEllipse,"Epsilon coalesced short arc");
                    }
                    else
                    {
                        // Historically DIMSTYLE blocked whole-document loading at 100.
                        // Retain this direct codec coverage; DimLfacFidelityTests also
                        // exercises complete document loading at that extreme epsilon.
                        var spec=EllipseIoCases[kind];
                        var e=new Ellipse(Vector3.Zero,2*spec.Axis,2*spec.Axis*spec.Ratio);
                        MathHelper.Epsilon=epsilon;
                        object[] axes={new Vector3(0,spec.Axis,0),Vector3.UnitZ,spec.Ratio,0.0,0.0,0.0};
                        Codec("DxfEllipseParameterCodec","ReadAxes",axes);
                        SameDoubleBits(90,(double)axes[5],"Epsilon changed ellipse orientation");
                        Codec("DxfReader","SetEllipseParameters",e,new[]{spec.First,spec.Last});
                        Check(!e.IsFullEllipse,"Epsilon coalesced short arc");
                        var parameters=(double[])Codec("DxfWriter","GetEllipseParameters",e)!;
                        Check(parameters[0]!=parameters[1],"Epsilon collapsed written parameters");
                    }
                }
                finally{MathHelper.Epsilon=old;}
            });
        // Exercise the actual reader/writer conversion entry points, not a copy.
        object? Codec(string type,string method,params object[] args)
        {
            try{return typeof(DxfDocument).Assembly.GetType("netDxf.IO."+type,true)!.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,args);}
            catch(TargetInvocationException error) when(error.InnerException!=null){ExceptionDispatchInfo.Capture(error.InnerException).Throw();throw;}
        }
        foreach(double size in new[]{1e-310,1e-200,1.0,1e200})foreach(double angle in new[]{0.0,25.0,90.0,180.0,270.0})
            Run($"ellipse-io/writer-scale/{ParameterBits(size)}/{angle}",()=>
            {
                var e=new Ellipse(Vector3.Zero,8*size,4*size){StartAngle=angle,EndAngle=angle+10};
                var result=(double[])Codec("DxfWriter","GetEllipseParameters",e)!;
                Check(result.All(double.IsFinite)&&result[0]!=result[1],"Writer reciprocal overflow/collapse");
            });
        Run("ellipse-io/unrepresentable-polar-span",()=>
        {
            var e=new Ellipse(Vector3.Zero,8,1e-300){StartAngle=17,EndAngle=29,ProxyGraphics=new byte[]{7}};
            long[] state=SafeEllipseState(e,e.Normal);
            Throws<NotSupportedException>(()=>Codec("DxfReader","SetEllipseParameters",e,new[]{Math.PI-.01,Math.PI-.02}));
            Check(state.SequenceEqual(SafeEllipseState(e,e.Normal))&&e.ProxyGraphics!.SequenceEqual(new byte[]{7}),"Rejected conversion changed existing ellipse");
        });
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
            Run("ellipse-io/nonfinite-parameters/"+ParameterBits(bad),()=>
            {
                var e=new Ellipse(Vector3.Zero,8,4){StartAngle=10,EndAngle=20};long[] before=SafeEllipseState(e,e.Normal);
                Throws<ArgumentException>(()=>Codec("DxfReader","SetEllipseParameters",e,new[]{.1,bad}));
                Check(before.SequenceEqual(SafeEllipseState(e,e.Normal)),"Bad endpoint partially published");
                e.EndAngle=bad;Throws<ArgumentException>(()=>Codec("DxfWriter","GetEllipseParameters",e));
            });
    }
}
