// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawReverseVertexFields = {10,20,40,41,42,91};
    private static void RawReverseCompare(DxfRawLwPolylineGeometry source,DxfRawLwPolylineGeometry target)
    {
        int count=source.Vertices.Count;Equal(count,target.Vertices.Count,"Reverse count");Equal(source.Flags,target.Flags,"Reverse flags");
        SameDoubleBits(source.Elevation,target.Elevation,"Reverse elevation");SameDoubleBits(source.Thickness,target.Thickness,"Reverse thickness");
        SameDoubleBits(source.ConstantWidth,target.ConstantWidth,"Reverse constant width");RawLinePointBits(source.ExtrusionDirection,target.ExtrusionDirection);
        for(int j=0;j<count;j++)
        {
            int point=count-1-j,edge=point==0?count-1:point-1;var p=source.Vertices[point];var e=source.Vertices[edge];var r=target.Vertices[j];
            SameDoubleBits(p.Position.X,r.Position.X,"Reverse position X");SameDoubleBits(p.Position.Y,r.Position.Y,"Reverse position Y");Equal(p.Identifier,r.Identifier,"ID follows point");
            Equal(e.HasEndWidth,r.HasStartWidth,"Reversed start presence");Equal(e.HasStartWidth,r.HasEndWidth,"Reversed end presence");Equal(e.HasBulge,r.HasBulge,"Reversed bulge presence");
            SameDoubleBits(e.EndWidth,r.StartWidth,"Reversed start width");SameDoubleBits(e.StartWidth,r.EndWidth,"Reversed end width");
            SameDoubleBits(e.HasBulge?-e.Bulge:0.0,r.Bulge,"Reversed bulge sign");
        }
    }
    private static void RegisterRawLwPolylineReverseTests()
    {
        foreach(DxfVersion version in HandleProfiles.Where(v=>v>=DxfVersion.AutoCad14))foreach(bool binary in new[]{false,true})
            foreach(bool block in new[]{false,true})foreach(int variant in new[]{0,1,2,3})
                Run($"raw-lw-reverse/matrix/{version}/{binary}/{block}/{variant}",()=>
                {
                    var raw=LoadRaw(RawFixtureBytes(RawLwTags(version,block,variant),binary));var record=RawLwRecord(raw);var before=RawLwRead(raw);byte[] original=SaveRaw(raw);
                    var reversed=raw.ReverseLwPolyline(record);var after=RawLwRead(reversed);var target=RawLwRecord(reversed);RawReverseCompare(before,after);
                    Equal(raw.Tags.Count,reversed.Tags.Count,"Reversal grew tag count");AssertOutsideRecordUnchanged(raw,record,reversed,target.Tags.Count);
                    var a=record.Tags.Where(t=>!RawReverseVertexFields.Contains(t.Code)).ToArray();var b=target.Tags.Where(t=>!RawReverseVertexFields.Contains(t.Code)).ToArray();
                    SameRawTags(a,b);Check(a.Zip(b).All(pair=>ReferenceEquals(pair.First,pair.Second)),"Nonvertex identity changed");
                    foreach(var tag in record.Tags.Where(t=>t.Code==10||t.Code==20||t.Code==91))Check(target.Tags.Any(t=>ReferenceEquals(t,tag)),"Point/ID tag identity replaced");
                    Check(!reversed.HasOriginalBytes&&original.SequenceEqual(SaveRaw(raw)),"Original bytes changed or falsely retained");
                    var twice=reversed.ReverseLwPolyline(target);var restored=RawLwRead(twice);
                    for(int i=0;i<before.Vertices.Count;i++)LwTopologySameVertex(before.Vertices[i],restored.Vertices[i]);
                    foreach(bool output in new[]{false,true})
                    {
                        var bytes=SaveRaw(reversed,output);var loaded=LoadRaw(bytes);SameRawTags(reversed.Tags,loaded.Tags);RawReverseCompare(before,RawLwRead(loaded));
                        Equal(version,loaded.Version,"Reversed dialect changed");Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Reversed encoding changed");
                        string stem=$"raw-lw-reverse-{version}-{binary}-{block}-{variant}-{output}";
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
                    }
                    Throws<ArgumentException>(()=>reversed.ReverseLwPolyline(record));
                });
        foreach(int count in new[]{0,1})foreach(bool decorated in new[]{false,true})
            Run($"raw-lw-reverse/no-op/{count}/{decorated}",()=>
            {
                var raw=DxfRawDocument.Create(RawLwTags(DxfVersion.AutoCad2018,false,1));
                while(RawLwRead(raw).Vertices.Count>count)raw=raw.RemoveLwPolylineVertex(RawLwRecord(raw),0);
                if(decorated){var rec=RawLwRecord(raw);var tags=rec.Tags.ToList();tags.Insert(1,new DxfTag(92,0));raw=raw.WithRecord(rec,tags);}
                Check(ReferenceEquals(raw,raw.ReverseLwPolyline(RawLwRecord(raw))),"No-op lost snapshot");
            });
        foreach(int mask in Enumerable.Range(0,8))
            Run($"raw-lw-reverse/optional-fields/{mask}",()=>
            {
                var tags=RawLwTags(DxfVersion.AutoCad2018,false,1);int at=tags.FindIndex(t=>t.Code==10);
                if((mask&1)!=0)tags.Insert(at+2,new DxfTag(40,-0.0));
                if((mask&2)!=0)tags.Insert(at+2,new DxfTag(41,double.Epsilon));
                if((mask&4)!=0)tags.Insert(at+2,new DxfTag(42,-0.0));
                var raw=DxfRawDocument.Create(tags);var reversed=raw.ReverseLwPolyline(RawLwRecord(raw));RawReverseCompare(RawLwRead(raw),RawLwRead(reversed));
                var twice=reversed.ReverseLwPolyline(RawLwRecord(reversed));for(int i=0;i<3;i++)LwTopologySameVertex(RawLwRead(raw).Vertices[i],RawLwRead(twice).Vertices[i]);
            });
        foreach(double value in new[]{double.Epsilon,-double.Epsilon,double.MaxValue,-double.MaxValue})
            Run("raw-lw-reverse/extreme/"+ParameterBits(value),()=>
            {
                var raw=DxfRawDocument.Create(RawLwTags(DxfVersion.AutoCad2018,false,1));raw=raw.WithLwPolylineVertex(RawLwRecord(raw),1,new Vector2(value,-value),Math.Abs(value),0,value);
                var after=raw.ReverseLwPolyline(RawLwRecord(raw));RawReverseCompare(RawLwRead(raw),RawLwRead(after));
                foreach(bool binary in new[]{false,true})SameRawTags(after.Tags,LoadRaw(SaveRaw(after,binary)).Tags);
            });
        Run("raw-lw-reverse/interleaved-and-budget",()=>
        {
            var tags=RawLwTags(DxfVersion.AutoCad2018,false,3);int at=tags.FindIndex(t=>t.Code==20);var layer=tags.First(t=>t.Code==8);tags.Remove(layer);tags.Insert(at,layer);tags.Insert(at+1,new DxfTag(999,"keep comment"));
            var raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));var reversed=raw.ReverseLwPolyline(RawLwRecord(raw));
            Equal(tags.Count,reversed.Tags.Count,"Reversal exceeded exact tag budget");Check(reversed.Tags.Any(t=>ReferenceEquals(layer,t)),"Interleaved layer lost");
            Check(reversed.Tags.Any(t=>t.Code==999&&Equals(t.Value,"keep comment")),"Interleaved comment lost");RawReverseCompare(RawLwRead(raw),RawLwRead(reversed));
        });
        foreach(short code in new short[]{92,160,310,1005,1010,1041,1042})
            Run($"raw-lw-reverse/guard/{code}",()=>
            {
                var tags=RawLwTags(DxfVersion.AutoCad2018,false,1);tags.Insert(RawLineAt(tags,1001)+(code>=1000?1:0),new DxfTag(code,code==92?(object)0:code==160?0L:code==310?new byte[]{1}:code==1005?"B":2.0));
                var raw=DxfRawDocument.Create(tags);byte[] original=SaveRaw(raw);Throws<NotSupportedException>(()=>raw.ReverseLwPolyline(RawLwRecord(raw)));Check(original.SequenceEqual(SaveRaw(raw)),"Guard failure changed source");
            });
        foreach(short code in new short[]{320,330,340,350,360,1005})
            Run($"raw-lw-reverse/incoming/{code}",()=>
            {
                var tags=RawLwTags(DxfVersion.AutoCad2018,false,1);int at=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"POINT"))+2;
                if(code==1005)tags.Insert(at++,new DxfTag(1001,"REF"));tags.Insert(at,new DxfTag(code,"000a"));
                var raw=DxfRawDocument.Create(tags);Throws<NotSupportedException>(()=>raw.ReverseLwPolyline(RawLwRecord(raw)));
            });
        foreach(bool embedded in new[]{false,true})
            Run($"raw-lw-reverse/private/{embedded}",()=>
            {
                var tags=RawLwTags(DxfVersion.AutoCad2018,false,1);tags.InsertRange(RawLineAt(tags,1001),embedded?new DxfTag[]{new(101,"Embedded Object"),new(10,999.0)}:new DxfTag[]{new(102,"{APP"),new(10,999.0),new(102,"}")});
                var raw=DxfRawDocument.Create(tags);Throws<NotSupportedException>(()=>raw.ReverseLwPolyline(RawLwRecord(raw)));
            });
        Run("raw-lw-reverse/schema-and-snapshot",()=>
        {
            var raw=DxfRawDocument.Create(RawLwTags(DxfVersion.AutoCad2018));var foreign=DxfRawDocument.Create(RawLwTags(DxfVersion.AutoCad2018));
            Throws<ArgumentNullException>(()=>raw.ReverseLwPolyline(null!));Throws<ArgumentException>(()=>raw.ReverseLwPolyline(RawLwRecord(foreign)));
            var tags=RawLwTags(DxfVersion.AutoCad2018);tags[RawLineAt(tags,90)]=new DxfTag(90,4);var bad=DxfRawDocument.Create(tags);Throws<FormatException>(()=>bad.ReverseLwPolyline(RawLwRecord(bad)));
        });
    }
}
