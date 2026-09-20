// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly byte[] ReverseProxy = { 1, 3, 7, 11 };
    private static Polyline2D ReverseModel(bool closed, int attributes = 0)
    {
        var p = new Polyline2D(new[] {
            new Polyline2DVertex(new Vector2(-3,0)), new Polyline2DVertex(new Vector2(2,4)),
            new Polyline2DVertex(new Vector2(7,-1)), new Polyline2DVertex(new Vector2(8,3)) }, closed);
        p.Elevation = 5; p.Thickness = -2; p.Normal = -Vector3.UnitZ;
        double[] bulges = { 0, .5, -.25, -.75 };
        for (int i = 0; i < p.Vertexes.Count; i++)
        {
            p.Vertexes[i].Bulge = bulges[i];
            if (attributes == 0 || (i & 1) == 0) p.Vertexes[i].StartWidthOverride = .25 + i * .25;
            if (attributes == 0 || (i & 1) != 0) p.Vertexes[i].EndWidthOverride = .5 + i * .25;
        }
        p.ProxyGraphics = ReverseProxy;
        return p;
    }
    private static long[] ReverseVertexBits(Polyline2DVertex v) => new[] { v.Position.X,v.Position.Y,v.Bulge,
        v.StartWidth,v.EndWidth }.Select(BitConverter.DoubleToInt64Bits).ToArray();
    private static void ReverseAssert(Polyline2DVertex[] points, long[][] bits, double?[] start, double?[] end, Polyline2D p)
    {
        int count = points.Length;
        Check(p.Vertexes.SequenceEqual(points.Reverse()), "Reversal replaced point identities");
        for (int i = 0; i < count; i++)
        {
            int point = count - 1 - i, edge = point == 0 ? count - 1 : point - 1;
            SameDoubleBits(BitConverter.Int64BitsToDouble(bits[point][0]),p.Vertexes[i].Position.X,"Reverse X");
            SameDoubleBits(BitConverter.Int64BitsToDouble(bits[point][1]),p.Vertexes[i].Position.Y,"Reverse Y");
            SameDoubleBits(-BitConverter.Int64BitsToDouble(bits[edge][2]),p.Vertexes[i].Bulge,"Reverse bulge");
            Equal(end[edge].HasValue,p.Vertexes[i].StartWidthOverride.HasValue,"Reverse start presence");
            Equal(start[edge].HasValue,p.Vertexes[i].EndWidthOverride.HasValue,"Reverse end presence");
            if (end[edge].HasValue) SameDoubleBits(end[edge]!.Value,p.Vertexes[i].StartWidth,"Reverse start");
            if (start[edge].HasValue) SameDoubleBits(start[edge]!.Value,p.Vertexes[i].EndWidth,"Reverse end");
        }
    }
    private sealed class ReverseEqualityTrap : Polyline2DVertex
    {
        public override bool Equals(object? value) => throw new InvalidOperationException("Equality callback");
        public override int GetHashCode() => throw new InvalidOperationException("Hash callback");
    }
    private static void RegisterPolylineReverseAtomicTests()
    {
        foreach(bool closed in new[]{false,true}) foreach(bool owned in new[]{false,true}) foreach(int attributes in new[]{0,1})
            Run($"polyline-reverse-atomic/model/{closed}/{owned}/{attributes}",()=>
            {
                var p=ReverseModel(closed,attributes);var doc=new DxfDocument();if(owned)doc.Entities.Add(p);
                var list=p.Vertexes;var vertices=list.ToArray();var bits=vertices.Select(ReverseVertexBits).ToArray();
                var start=vertices.Select(v=>v.StartWidthOverride).ToArray();var end=vertices.Select(v=>v.EndWidthOverride).ToArray();
                var owner=p.Owner;string handle=p.Handle;var color=p.Color;
                p.Reverse();ReverseAssert(vertices,bits,start,end,p);Check(p.ProxyGraphics==null,"Reverse retained stale proxy");
                Check(ReferenceEquals(list,p.Vertexes)&&ReferenceEquals(owner,p.Owner)&&handle==p.Handle&&ReferenceEquals(color,p.Color),"Reverse changed metadata identity");
                p.Reverse();Check(p.Vertexes.SequenceEqual(vertices),"Double reverse lost identities");
                for(int i=0;i<vertices.Length;i++)
                {Check(bits[i].SequenceEqual(ReverseVertexBits(vertices[i])),"Double reverse changed component bits");Equal(start[i],vertices[i].StartWidthOverride,"Double reverse start");Equal(end[i],vertices[i].EndWidthOverride,"Double reverse end");}
            });
        foreach(bool owned in new[]{false,true}) foreach(int index in new[]{0,1,3}) foreach(int fault in Enumerable.Range(0,7))
            Run($"polyline-reverse-atomic/reject/{owned}/{index}/{fault}",()=>
            {
                var p=ReverseModel(false);var doc=new DxfDocument();if(owned)doc.Entities.Add(p);
                var v=p.Vertexes[index];
                if(fault==0)p.Vertexes[index]=null!;
                else if(fault==1)v.Position=new(double.NaN,v.Position.Y);
                else if(fault==2)v.Position=new(v.Position.X,double.PositiveInfinity);
                else if(fault==3)v.Bulge=double.NegativeInfinity;
                else if(fault==4){typeof(Polyline2DVertex).GetField("startWidth",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(v,-1.0);}
                else if(fault==5){typeof(Polyline2DVertex).GetField("endWidth",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(v,double.NaN);}
                else p.Vertexes[index]=p.Vertexes[(index+1)%p.Vertexes.Count];
                p.ProxyGraphics=ReverseProxy;var order=p.Vertexes.ToArray();var present=order.Where(x=>x!=null).ToArray();
                var bits=present.Select(ReverseVertexBits).ToArray();var owner=p.Owner;string handle=p.Handle;
                Exception? error=null;try{p.Reverse();}catch(Exception ex){error=ex;}
                Check(error is InvalidOperationException or ArgumentOutOfRangeException,"Malformed reverse did not fail validation");
                Check(order.Zip(p.Vertexes).All(pair=>ReferenceEquals(pair.First,pair.Second)),"Rejected reverse partially reordered vertices");
                for(int i=0;i<present.Length;i++)Check(bits[i].SequenceEqual(ReverseVertexBits(present[i])),"Rejected reverse mutated attributes");
                Check(p.ProxyGraphics!.SequenceEqual(ReverseProxy)&&ReferenceEquals(owner,p.Owner)&&handle==p.Handle,"Rejected reverse changed proxy/ownership");
            });
        foreach(int count in new[]{0,1}) foreach(bool closed in new[]{false,true})
            Run($"polyline-reverse-atomic/degenerate/{count}/{closed}",()=>
            {
                var p=new Polyline2D(Enumerable.Range(0,count).Select(_=>new Polyline2DVertex(new Vector2(1,2))),closed);
                p.ProxyGraphics=ReverseProxy;var list=p.Vertexes;p.Reverse();Check(ReferenceEquals(list,p.Vertexes)&&p.ProxyGraphics!.SequenceEqual(ReverseProxy),"Zero/one vertex no-op changed state");
            });
        Run("polyline-reverse-atomic/reference-identity",()=>
        {
            var a=new ReverseEqualityTrap();var b=new ReverseEqualityTrap();var p=new Polyline2D(new Polyline2DVertex[]{a,b});
            p.Reverse();Check(ReferenceEquals(p.Vertexes[0],b)&&ReferenceEquals(p.Vertexes[1],a),"Distinct equal points confused");
            p.Vertexes[0]=a;Throws<InvalidOperationException>(()=>p.Reverse());
        });
        foreach(double value in new[]{-0.0,double.Epsilon,-double.Epsilon,double.MaxValue,-double.MaxValue})
            Run("polyline-reverse-atomic/bits/"+ParameterBits(value),()=>
            {
                var p=ReverseModel(false);p.Vertexes[1].Bulge=value;p.Vertexes[0].Position=new(value,-value);
                p.Vertexes[2].StartWidthOverride=Math.Abs(value);var original=p.Vertexes.Select(ReverseVertexBits).ToArray();
                p.Reverse();p.Reverse();for(int i=0;i<original.Length;i++)Check(original[i].SequenceEqual(ReverseVertexBits(p.Vertexes[i])),"Reversal lost finite bits");
            });
        foreach(double epsilon in new[]{1e-12,1.0,100.0})
            Run($"polyline-reverse-atomic/epsilon/{epsilon}",()=>
            {var p=ReverseModel(false);double old=MathHelper.Epsilon;try{MathHelper.Epsilon=epsilon;p.Reverse();Check(p.ProxyGraphics==null,"Epsilon suppressed reversal");}finally{MathHelper.Epsilon=old;}});
        foreach(DxfVersion version in SupportedVersions) foreach(bool binary in new[]{false,true}) foreach(bool closed in new[]{false,true})
            Run($"polyline-reverse-atomic/wire/{version}/{binary}/{closed}",()=>
            {
                var p=ReverseModel(closed,1);p.Reverse();Check(p.ProxyGraphics==null,"Pre-save stale proxy");
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(p);using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Reverse save failed");
                var raw=LoadRaw(stream.ToArray());var record=RawLwRecord(raw);Check(!record.Tags.Any(t=>t.Code==92||t.Code==160||t.Code==310),"Stale reversal proxy emitted");
                stream.Position=0;var loaded=DxfDocument.Load(stream)!.Entities.Polylines2D.Single();
                for(int i=0;i<p.Vertexes.Count;i++)Check(ReverseVertexBits(p.Vertexes[i]).SequenceEqual(ReverseVertexBits(loaded.Vertexes[i])),"Reverse roundtrip bits");
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"polyline-reverse-atomic-{version}-{binary}-{closed}.dxf"),stream.ToArray());
            });
        foreach(bool binary in new[]{false,true})
            Run($"polyline-reverse-atomic/legacy/{binary}",()=>
            {
                var doc=StoredDimAssocLoad(Legacy2DInput(DxfVersion.AutoCad2018,binary));var p=Legacy2DPolyline(doc,DxfVersion.AutoCad2018,binary,true);
                var vertices=p.Vertexes.ToArray();var records=p.VertexRecords.ToArray();var end=p.EndSequenceRecord;
                double? startWidth=p.LegacyDefaultStartWidth,endWidth=p.LegacyDefaultEndWidth;
                p.ProxyGraphics=ReverseProxy;p.Reverse();Check(p.ProxyGraphics==null,"Legacy reversal retained proxy");
                Check(p.Vertexes.SequenceEqual(vertices.Reverse())&&p.VertexRecords.SequenceEqual(records.Reverse())&&ReferenceEquals(end,p.EndSequenceRecord),"Legacy record mapping changed");
                Equal(endWidth,p.LegacyDefaultStartWidth,"Reversed legacy start");Equal(startWidth,p.LegacyDefaultEndWidth,"Reversed legacy end");
                p.Reverse();Check(p.Vertexes.SequenceEqual(vertices)&&p.VertexRecords.SequenceEqual(records),"Legacy round-trip order");
            });
    }
}
