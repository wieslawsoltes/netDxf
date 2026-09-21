// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly string[] EllipseMutationProperties = { "Center", "Rotation", "StartAngle", "EndAngle", "Thickness" };
    private static readonly byte[] EllipseMutationProxy = { 1, 3, 7, 11 };
    private static double EllipseMutationNormalized(double value)
    { double n=value%360; if(n<0)n+=360; return n==0||n==360?0:n; }
    private static Ellipse MutationEllipse() => new(new Vector3(1,2,3),8,4) { Rotation=31,StartAngle=17,EndAngle=201,Thickness=-2 };
    private static void AssertEllipseMutationValue(object expected,object actual)
    {
        if(expected is Vector3 vector) RawLinePointBits(vector,(Vector3)actual);
        else SameDoubleBits((double)expected,(double)actual,"Stored scalar changed");
    }

    private static void RegisterEllipseMutationProxyTests()
    {
        for(int index=0;index<5;index++)foreach(bool owned in new[]{false,true})for(int mode=0;mode<6;mode++)
        {
            int i=index,m=mode;Run($"ellipse-mutation/set/{i}/{owned}/{m}",()=>
            {
                var e=MutationEllipse();var p=typeof(Ellipse).GetProperty(EllipseMutationProperties[i])!;
                object original=p.GetValue(e)!;
                object value=i==0?(object)(m==0?e.Center:m==1?new Vector3(-8,9,10):m==2?new Vector3(Math.BitIncrement(1.0),2,3):
                    m==3?new Vector3(1,Math.BitIncrement(2.0),3):m==4?new Vector3(1,2,Math.BitIncrement(3.0)):e.Center):
                    m==0?(double)original:m==1?45.0:m==2?Math.BitIncrement((double)original):
                    m==3?(double)original+360:m==4?(double)original-360:(double)original+720;
                object expected=i>0&&i<4?(object)EllipseMutationNormalized((double)value):value;
                var doc=new DxfDocument();if(owned)doc.Entities.Add(e);var owner=e.Owner;var handle=e.Handle;var color=e.Color;
                object[] originals=EllipseMutationProperties.Select(n=>typeof(Ellipse).GetProperty(n)!.GetValue(e)!).ToArray();
                var before=SafeEllipseState(e,e.Normal);e.ProxyGraphics=EllipseMutationProxy;p.SetValue(e,value);
                AssertEllipseMutationValue(expected,p.GetValue(e)!);
                bool changed=!before.SequenceEqual(SafeEllipseState(e,e.Normal));
                Check(changed?e.ProxyGraphics==null:e.ProxyGraphics!=null&&e.ProxyGraphics.SequenceEqual(EllipseMutationProxy),"Wrong mutation proxy state");
                for(int k=0;k<5;k++)if(k!=i)AssertEllipseMutationValue(originals[k],typeof(Ellipse).GetProperty(EllipseMutationProperties[k])!.GetValue(e)!);
                SameDoubleBits(8,e.MajorAxis,"Major changed");SameDoubleBits(4,e.MinorAxis,"Minor changed");RawLinePointBits(Vector3.UnitZ,e.Normal);
                Check(ReferenceEquals(owner,e.Owner)&&handle==e.Handle&&ReferenceEquals(color,e.Color),"Mutation changed metadata identity");
            });
        }
        for(int index=0;index<5;index++)foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            int i=index;Run($"ellipse-mutation/nonfinite-storage/{i}/{ParameterBits(bad)}",()=>
            {
                var e=MutationEllipse();var p=typeof(Ellipse).GetProperty(EllipseMutationProperties[i])!;e.ProxyGraphics=EllipseMutationProxy;
                object value=i==0?(object)new Vector3(bad,2,3):bad;object expected=i>0&&i<4?(object)EllipseMutationNormalized(bad):value;
                p.SetValue(e,value);AssertEllipseMutationValue(expected,p.GetValue(e)!);Check(e.ProxyGraphics==null,"Changed nonfinite storage retained proxy");
                e.ProxyGraphics=EllipseMutationProxy;p.SetValue(e,value);AssertEllipseMutationValue(expected,p.GetValue(e)!);
                Check(e.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Same stored nonfinite bits cleared proxy");
            });
        }
        for(int coordinate=0;coordinate<3;coordinate++)
        {
            int c=coordinate;Run($"ellipse-mutation/center-zero/{c}",()=>
            {
                var e=MutationEllipse();e.Center=Vector3.Zero;e.ProxyGraphics=EllipseMutationProxy;var v=Vector3.Zero;v[c]=-0.0;e.Center=v;
                Check(e.ProxyGraphics==null,"Center signed-zero edit retained proxy");SameDoubleBits(-0.0,e.Center[c],"Center zero sign");
                e.ProxyGraphics=EllipseMutationProxy;e.Center=v;Check(e.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Same signed center cleared proxy");
            });
        }
        foreach(string name in EllipseMutationProperties.Skip(1))
            Run("ellipse-mutation/scalar-zero/"+name,()=>
            {
                var e=MutationEllipse();var p=typeof(Ellipse).GetProperty(name)!;p.SetValue(e,0.0);e.ProxyGraphics=EllipseMutationProxy;p.SetValue(e,-0.0);
                if(name=="Thickness"){SameDoubleBits(-0.0,e.Thickness,"Thickness zero sign");Check(e.ProxyGraphics==null,"Thickness sign edit retained proxy");}
                else{SameDoubleBits(0.0,(double)p.GetValue(e)!,"Angle normalization changed");Check(e.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Normalized no-op cleared proxy");}
            });
        Run("ellipse-mutation/vector-cache",()=>
        {
            var e=MutationEllipse();var n=Vector3.Normalize(new Vector3(3,4,0));e.Center=new Vector3(n.X,n.Y,n.Z);e.ProxyGraphics=EllipseMutationProxy;e.Center=n;
            Equal(n.IsNormalized,e.Center.IsNormalized,"Center assignment dropped struct cache");Check(e.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Cache-only change invalidated geometry");
        });
        for(int index=0;index<5;index++)
        {
            int i=index;Run($"ellipse-mutation/clone/{i}",()=>
            {
                var e=MutationEllipse();e.ProxyGraphics=EllipseMutationProxy;var clone=(Ellipse)e.Clone();var before=SafeEllipseState(e,e.Normal);
                Check(clone.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Clone initialization lost proxy");
                typeof(Ellipse).GetProperty(EllipseMutationProperties[i])!.SetValue(clone,i==0?(object)new Vector3(9,8,7):45.0);
                Check(clone.ProxyGraphics==null&&e.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Clone mutation leaked proxy state");
                Check(before.SequenceEqual(SafeEllipseState(e,e.Normal)),"Clone mutation changed source");
            });
        }
        foreach(double epsilon in new[]{1e-12,1.0,100.0})for(int index=0;index<5;index++)
        {
            int i=index;Run($"ellipse-mutation/epsilon/{epsilon}/{i}",()=>
            {
                var e=MutationEllipse();var p=typeof(Ellipse).GetProperty(EllipseMutationProperties[i])!;e.ProxyGraphics=EllipseMutationProxy;double prior=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;p.SetValue(e,i==0?(object)new Vector3(Math.BitIncrement(1.0),2,3):Math.BitIncrement((double)p.GetValue(e)!));Check(e.ProxyGraphics==null,"Epsilon hid one-ULP edit");}
                finally{MathHelper.Epsilon=prior;}
            });
        }
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})for(int index=0;index<5;index++)foreach(bool changed in new[]{false,true})
        {
            int i=index;Run($"ellipse-mutation/wire/{version}/{binary}/{i}/{changed}",()=>
            {
                var e=new Ellipse(new Vector3(1,2,3),8,4){ProxyGraphics=EllipseMutationProxy};
                object value=i==0?(object)(changed?new Vector3(-8,9,10):e.Center):i==4?(changed?-2.0:0.0):(changed?90.0:360.0);
                typeof(Ellipse).GetProperty(EllipseMutationProperties[i])!.SetValue(e,value);
                Check(changed?e.ProxyGraphics==null:e.ProxyGraphics!=null,"Wire proxy before save");
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(e);using var output=new MemoryStream();Check(doc.Save(output,binary),"Mutation save");
                var bytes=output.ToArray();var raw=LoadRaw(bytes);var record=RawEllipseRecord(raw);Equal(!changed,record.Tags.Any(t=>t.Code==310),"Wire proxy packet");
                output.Position=0;var loaded=DxfDocument.Load(output)!.Entities.Ellipses.Single();
                if(changed)Check(loaded.ProxyGraphics==null,"Reload restored stale proxy");else Check(loaded.ProxyGraphics!.SequenceEqual(EllipseMutationProxy),"Reload lost unchanged proxy");
                // ELLIPSE thickness is not serialized by the existing writer; no thickness round trip is inferred.
                RawLinePointBits(e.Center,loaded.Center);Near(e.Rotation,loaded.Rotation,"Mutation rotation");Near(e.StartAngle,loaded.StartAngle,"Mutation start");Near(e.EndAngle,loaded.EndAngle,"Mutation end");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"ellipse-mutation-{version}-{binary}-{i}-{changed}.dxf"),bytes);
            });
        }
    }
}
