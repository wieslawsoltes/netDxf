// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly byte[] EllipseAxisProxy = {1,3,7,11};
    private static Ellipse AxisProxySource() => new(new Vector3(1,2,3),8,4)
        { Rotation=31, StartAngle=17, EndAngle=201, ProxyGraphics=EllipseAxisProxy };
    private static void RegisterEllipseAxisProxyTests()
    {
        foreach(bool owned in new[]{false,true})foreach(double a in new[]{4.0,8.0,16.0,Math.BitIncrement(8.0)})
            foreach(double b in new[]{2.0,4.0,8.0})
                Run($"ellipse-axis-proxy/assignment/{owned}/{ParameterBits(a)}/{ParameterBits(b)}",()=>
                {
                    var e=AxisProxySource();var d=new DxfDocument();if(owned)d.Entities.Add(e);
                    string handle=e.Handle;var owner=e.Owner;var color=e.Color;
                    e.SetAxis(a,b);SameDoubleBits(Math.Max(a,b),e.MajorAxis,"Major axis");SameDoubleBits(Math.Min(a,b),e.MinorAxis,"Minor axis");
                    if(Math.Max(a,b)==8&&Math.Min(a,b)==4)Check(e.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Unchanged axes cleared proxy");
                    else Check(e.ProxyGraphics==null,"Resized ellipse retained stale proxy");
                    RawLinePointBits(new(1,2,3),e.Center);Equal(31.0,e.Rotation,"Axis update rotated ellipse");Equal(17.0,e.StartAngle,"Axis update changed start");Equal(201.0,e.EndAngle,"Axis update changed end");
                    Check(handle==e.Handle&&ReferenceEquals(owner,e.Owner)&&ReferenceEquals(color,e.Color),"Axis update changed identity");
                });
        foreach(bool first in new[]{false,true})foreach(bool owned in new[]{false,true})
            foreach(double bad in new[]{0.0,-1.0,-double.Epsilon,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
                Run($"ellipse-axis-proxy/reject/{first}/{owned}/{ParameterBits(bad)}",()=>
                {
                    var e=AxisProxySource();var d=new DxfDocument();if(owned)d.Entities.Add(e);
                    string handle=e.Handle;var owner=e.Owner;ArgumentOutOfRangeException? failure=null;
                    try{e.SetAxis(first?bad:16,first?16:bad);}catch(ArgumentOutOfRangeException ex){failure=ex;}
                    Check(failure!=null,"Invalid axis accepted");Equal(first?"axis1":"axis2",failure!.ParamName,"Axis argument");
                    SameDoubleBits(8,e.MajorAxis,"Rejected major changed");SameDoubleBits(4,e.MinorAxis,"Rejected minor changed");
                    Check(e.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Rejected axes cleared valid proxy");Check(handle==e.Handle&&ReferenceEquals(owner,e.Owner),"Rejected axes changed identity");
                });
        foreach(double scale in new[]{double.Epsilon,1e-200,1.0,1e200,double.MaxValue})
            Run("ellipse-axis-proxy/extreme/"+ParameterBits(scale),()=>
            {
                var e=AxisProxySource();e.SetAxis(scale,scale);Check(e.ProxyGraphics==null,"Extreme resize retained proxy");
                e.ProxyGraphics=EllipseAxisProxy;e.SetAxis(scale,scale);Check(e.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Extreme no-op cleared proxy");
                SameDoubleBits(scale,e.MajorAxis,"Extreme major changed");SameDoubleBits(scale,e.MinorAxis,"Extreme minor changed");
            });
        foreach(double epsilon in new[]{1e-12,1.0,100.0})
            Run($"ellipse-axis-proxy/epsilon/{epsilon}",()=>
            {
                var e=AxisProxySource();double old=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;e.SetAxis(Math.BitIncrement(8.0),4);Check(e.ProxyGraphics==null,"Epsilon hid axis edit");}
                finally{MathHelper.Epsilon=old;}
            });
        Run("ellipse-axis-proxy/clone",()=>
        {
            var e=AxisProxySource();var clone=(Ellipse)e.Clone();Check(clone.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Clone lost valid proxy");
            clone.SetAxis(16,4);Check(clone.ProxyGraphics==null,"Clone edit retained proxy");Check(e.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Clone edit cleared source proxy");SameDoubleBits(8,e.MajorAxis,"Clone edit changed source");
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})for(int mode=0;mode<6;mode++)
        {
            int m=mode;
            Run($"ellipse-axis-proxy/wire/{version}/{binary}/{m}",()=>
            {
                double[] a={8,16,4,8,4,8},b={4,4,16,2,8,8};
                var e=new Ellipse(new Vector3(1,2,3),8,4){ProxyGraphics=EllipseAxisProxy};e.SetAxis(a[m],b[m]);
                bool unchanged=m==0||m==4;
                Check(unchanged?e.ProxyGraphics!=null:e.ProxyGraphics==null,"Wrong proxy before output");
                var d=new DxfDocument(version);d.Comments.Clear();d.Entities.Add(e);
                using var stream=new MemoryStream();Check(d.Save(stream,binary),"Ellipse axis save");
                byte[] bytes=stream.ToArray();var raw=LoadRaw(bytes);var record=RawEllipseRecord(raw);
                Equal(unchanged,record.Tags.Any(t=>t.Code==310),"Wrong wire proxy state");
                stream.Position=0;var loaded=DxfDocument.Load(stream)!.Entities.Ellipses.Single();
                SameDoubleBits(Math.Max(a[m],b[m]),loaded.MajorAxis,"Round-trip major");SameDoubleBits(Math.Min(a[m],b[m]),loaded.MinorAxis,"Round-trip minor");
                if(unchanged)Check(loaded.ProxyGraphics!.SequenceEqual(EllipseAxisProxy),"Wire no-op proxy lost");else Check(loaded.ProxyGraphics==null,"Wire stale proxy");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"ellipse-axis-proxy-{version}-{binary}-{m}.dxf"),bytes);
            });
        }
    }
}
