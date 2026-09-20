// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly byte[] WidthProxy = {1,3,7,11};
    private static Polyline2D WidthProxySource() => new(new[]{new Vector2(1,2),new Vector2(4,5),new Vector2(7,8)},true);
    private static bool WidthBits(double? a,double? b) => a.HasValue==b.HasValue&&(!a.HasValue||BitConverter.DoubleToInt64Bits(a.Value)==BitConverter.DoubleToInt64Bits(b!.Value));
    private static void RegisterPolylineWidthProxyTests()
    {
        double?[] widths={null,0,-0.0,1,Math.BitIncrement(1.0),double.Epsilon,double.MaxValue};
        for(int i=0;i<widths.Length;i++)for(int j=0;j<widths.Length;j++)foreach(bool owned in new[]{false,true})
        {
            int a=i,b=j;
            Run($"polyline-width-proxy/property/{a}/{b}/{owned}",()=>
            {
                var p=WidthProxySource();p.ConstantWidth=widths[a];var doc=new DxfDocument();if(owned)doc.Entities.Add(p);
                var owner=p.Owner;string handle=p.Handle;var list=p.Vertexes;var v=list[1];p.ProxyGraphics=WidthProxy;
                p.ConstantWidth=widths[b];Check(WidthBits(widths[b],p.ConstantWidth),"Stored constant width");
                Check(WidthBits(widths[a],widths[b])?p.ProxyGraphics!=null&&p.ProxyGraphics.SequenceEqual(WidthProxy):p.ProxyGraphics==null,"Property proxy state");
                Check(ReferenceEquals(list,p.Vertexes)&&ReferenceEquals(v,p.Vertexes[1])&&ReferenceEquals(owner,p.Owner)&&handle==p.Handle,"Property identity");
                Check(!v.StartWidthOverride.HasValue&&!v.EndWidthOverride.HasValue,"Property rewrote vertex widths");
            });
        }
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity,-1.0})foreach(bool bulk in new[]{false,true})
            Run($"polyline-width-proxy/reject/{bulk}/{ParameterBits(bad)}",()=>
            {
                var p=WidthProxySource();p.ConstantWidth=2;p.ProxyGraphics=WidthProxy;
                Throws<ArgumentOutOfRangeException>(()=>{if(bulk)p.SetConstantWidth(bad);else p.ConstantWidth=bad;});
                Equal((double?)2,p.ConstantWidth,"Failed assignment changed constant");Check(p.ProxyGraphics!.SequenceEqual(WidthProxy),"Failed assignment cleared proxy");
                Check(p.Vertexes.All(v=>!v.StartWidthOverride.HasValue&&!v.EndWidthOverride.HasValue),"Failed bulk width mutated vertices");
            });
        foreach(double width in new[]{0.0,-0.0,double.Epsilon,1.0,double.MaxValue})foreach(bool explicitBefore in new[]{false,true})
            Run($"polyline-width-proxy/bulk/{ParameterBits(width)}/{explicitBefore}",()=>
            {
                var p=WidthProxySource();if(explicitBefore)p.SetConstantWidth(width);p.ProxyGraphics=WidthProxy;
                var list=p.Vertexes;var vertices=list.ToArray();p.SetConstantWidth(width);
                Check(p.ConstantWidth==null,"Bulk did not clear group 43");Check(explicitBefore?p.ProxyGraphics!=null:p.ProxyGraphics==null,"Bulk proxy state");
                for(int i=0;i<vertices.Length;i++)
                {
                    Check(ReferenceEquals(vertices[i],p.Vertexes[i]),"Bulk replaced vertex object");
                    SameDoubleBits(width,p.Vertexes[i].StartWidth,"Bulk start");SameDoubleBits(width,p.Vertexes[i].EndWidth,"Bulk end");
                    Check(p.Vertexes[i].StartWidthOverride.HasValue&&p.Vertexes[i].EndWidthOverride.HasValue,"Bulk width presence");
                }
            });
        Run("polyline-width-proxy/late-invalid-source",()=>
        {
            var p=WidthProxySource();p.ConstantWidth=2;p.Vertexes.Add(null!);p.ProxyGraphics=WidthProxy;
            Throws<InvalidOperationException>(()=>p.SetConstantWidth(1));
            Equal((double?)2,p.ConstantWidth,"Late validation changed header");Check(p.ProxyGraphics!.SequenceEqual(WidthProxy),"Late validation changed proxy");
            Check(!p.Vertexes[0].StartWidthOverride.HasValue,"Late validation changed earlier vertex");
        });
        Run("polyline-width-proxy/clone",()=>
        {
            var p=WidthProxySource();p.ConstantWidth=2;p.ProxyGraphics=WidthProxy;var copy=(Polyline2D)p.Clone();
            Check(copy.ProxyGraphics!.SequenceEqual(WidthProxy),"Clone lost valid proxy");copy.SetConstantWidth(3);
            Check(copy.ProxyGraphics==null&&p.ProxyGraphics!.SequenceEqual(WidthProxy),"Clone proxy isolation");Equal((double?)2,p.ConstantWidth,"Clone changed original widths");
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})for(int mode=0;mode<7;mode++)
        {
            int m=mode;
            Run($"polyline-width-proxy/wire/{version}/{binary}/{m}",()=>
            {
                var p=WidthProxySource();
                if(m==2||m==5)p.ConstantWidth=2;
                if(m==4)p.SetConstantWidth(1);
                p.ProxyGraphics=WidthProxy;
                if(m==0)p.ConstantWidth=2;
                else if(m==1)p.ConstantWidth=null;
                else if(m==2)p.ConstantWidth=2;
                else if(m==6)p.ConstantWidth=0;
                else p.SetConstantWidth(1);
                bool retained=m==1||m==2||m==4;
                Check(retained?p.ProxyGraphics!=null:p.ProxyGraphics==null,"Width proxy before save");
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(p);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Width save failed");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polyline-width-proxy-{version}-{binary}-{m}.dxf"),stream.ToArray());
                stream.Position=0;var restored=DxfDocument.Load(stream)!.Entities.Polylines2D.Single();
                Check(WidthBits(p.ConstantWidth,restored.ConstantWidth),"Wire constant width");
                for(int i=0;i<3;i++){SameDoubleBits(p.Vertexes[i].StartWidth,restored.Vertexes[i].StartWidth,"Wire start");SameDoubleBits(p.Vertexes[i].EndWidth,restored.Vertexes[i].EndWidth,"Wire end");}
                Check(retained?restored.ProxyGraphics!=null&&restored.ProxyGraphics.SequenceEqual(WidthProxy):restored.ProxyGraphics==null,"Wire proxy");
            });
        }
    }
}
