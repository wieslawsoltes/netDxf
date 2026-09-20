// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawLwFields = {10,20,40,41,42};
    private static DxfRawRecord RawLwRecord(DxfRawDocument raw)
        => raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="LWPOLYLINE");
    private static DxfRawLwPolylineGeometry RawLwRead(DxfRawDocument raw)
        => raw.ReadLwPolylineGeometry(RawLwRecord(raw));
    private static List<DxfTag> RawLwTags(DxfVersion version, bool block=false, int variant=0)
    {
        var tags=RawLineTags(version,block);
        int a=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE"));
        int b=tags.FindIndex(a+1,t=>t.Code==0);
        var data=new List<DxfTag>{new(0,"LWPOLYLINE"),new(5,"A")};
        if(variant!=3)data.Add(new(100,"AcDbEntity"));
        data.AddRange(new DxfTag[]{new(8,"0"),new(62,(short)3)});
        if(variant!=3)data.Add(new(100,"AcDbPolyline"));
        data.AddRange(new DxfTag[]{new(90,3),new(70,(short)(variant==0?0:129)),new(38,5.0),new(39,-2.0)});
        if(variant==2)data.Add(new(43,2.0));
        for(int i=0;i<3;i++)
        {
            data.Add(new(10,1.0+i*3));
            if(variant==3)data.Add(new(42,i==1?-.5:0.0));
            data.Add(new(20,2.0+i*3));
            data.Add(new(91,i==1?-7:100+i));
            if(variant!=1)
            {
                data.Add(new(40,.25+i*.25));data.Add(new(41,.5+i*.25));
                if(variant!=3)data.Add(new(42,i==1?-.5:0.0));
            }
        }
        data.AddRange(new DxfTag[]{new(210,0.0),new(220,variant==2?.6:0.0),new(230,variant==2?.8:-1.0),
            new(1001,"RAW_LWP"),new(1000,"retained"),new(1040,3.125)});
        tags.RemoveRange(a,b-a);tags.InsertRange(a,data);return tags;
    }
    private static void RegisterRawLwPolylineGeometryTests()
    {
        foreach(DxfVersion version in HandleProfiles.Where(v=>v>=DxfVersion.AutoCad14))
        foreach(bool binary in new[]{false,true})foreach(bool block in new[]{false,true})for(int variant=0;variant<4;variant++)
        {
            int v=variant;
            Run($"raw-lwpolyline/matrix/{version}/{binary}/{block}/{v}",()=>
            {
                var raw=LoadRaw(RawFixtureBytes(RawLwTags(version,block,v),binary));var record=RawLwRecord(raw);
                byte[] before=SaveRaw(raw);var view=RawLwRead(raw);var vertex=view.Vertices[1];
                Equal(3,view.Vertices.Count,"Count");Equal((short)(v==0?0:129),view.Flags,"Flags");
                Equal(v!=0,view.IsClosed,"Closure");Equal(5.0,view.Elevation,"Elevation");Equal(-2.0,view.Thickness,"Thickness");
                Equal(v==2?2.0:0.0,view.ConstantWidth,"Constant width");Equal((int?)-7,vertex.Identifier,"Opaque vertex ID");
                SameDoubleBits(4,vertex.Position.X,"X");SameDoubleBits(5,vertex.Position.Y,"Y");
                Equal(v!=1,vertex.HasStartWidth,"Optional width");Equal(v!=1,vertex.HasBulge,"Optional bulge");
                Check(ReferenceEquals(raw,raw.WithLwPolylineVertex(record,1,vertex.Position,vertex.StartWidth,vertex.EndWidth,vertex.Bulge)),"No-op snapshot");
                double sw=v==2?vertex.StartWidth:1.25,ew=v==2?vertex.EndWidth:2.5;
                var edited=raw.WithLwPolylineVertex(record,1,new(-8.5,16.25),sw,ew,1.0);
                var updated=RawLwRead(edited);var current=updated.Vertices[1];
                SameDoubleBits(-8.5,current.Position.X,"Changed X");SameDoubleBits(16.25,current.Position.Y,"Changed Y");
                Equal(sw,current.StartWidth,"Start width");Equal(ew,current.EndWidth,"End width");Equal(1.0,current.Bulge,"Bulge");
                Equal(vertex.Identifier,current.Identifier,"Identifier changed");
                Check(!edited.HasOriginalBytes&&before.SequenceEqual(SaveRaw(raw)),"Source bytes changed");
                SameDoubleBits(4,vertex.Position.X,"Immutable view changed");
                var newRecord=RawLwRecord(edited);AssertOutsideRecordUnchanged(raw,record,edited,newRecord.Tags.Count);
                var oldOther=record.Tags.Where(t=>!RawLwFields.Contains(t.Code)).ToArray();
                var newOther=newRecord.Tags.Where(t=>!RawLwFields.Contains(t.Code)).ToArray();SameRawTags(oldOther,newOther);
                Check(oldOther.Zip(newOther).All(p=>ReferenceEquals(p.First,p.Second)),"Untouched tag identity");
                foreach(bool output in new[]{false,true})
                {
                    byte[] bytes=SaveRaw(edited,output);var loaded=LoadRaw(bytes);SameRawTags(edited.Tags,loaded.Tags);
                    Equal(version,loaded.Version,"Version changed");Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Encoding changed");
                    string stem=$"raw-lwpolyline-{version}-{binary}-{block}-{v}-{output}";
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
                }
                Throws<ArgumentException>(()=>edited.ReadLwPolylineGeometry(record));
                Throws<NotSupportedException>(()=>((IList<DxfRawLwPolylineVertex>)view.Vertices).Clear());
            });
        }
        DxfRawDocument Modified(Action<List<DxfTag>> edit,int variant=0)
        {var tags=RawLwTags(DxfVersion.AutoCad2018,false,variant);edit(tags);return DxfRawDocument.Create(tags);}
        foreach(short code in new short[]{90,70,38,39,210,220,230,20,40,41,42,91})
            Run($"raw-lwpolyline/duplicate/{code}",()=>
            {var r=Modified(t=>{int i=RawLineAt(t,code);t.Insert(i,t[i]);});Throws<FormatException>(()=>RawLwRead(r));});
        foreach(int count in new[]{-1,0,2,4,1000001,int.MaxValue})
            Run($"raw-lwpolyline/count/{count}",()=>
            {var r=Modified(t=>t[RawLineAt(t,90)]=new(90,count));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(short code in new short[]{20,40,41,42,91})
            Run($"raw-lwpolyline/orphan/{code}",()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,10),new(code,code==91?(object)8:2.0)));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(short code in new short[]{90,20})
            Run($"raw-lwpolyline/missing/{code}",()=>
            {var r=Modified(t=>t.RemoveAt(RawLineAt(t,code)));Throws<FormatException>(()=>RawLwRead(r));});
        Run("raw-lwpolyline/missing-last-y",()=>
        {var r=Modified(t=>t.RemoveAt(t.FindLastIndex(RawLineAt(t,210)-1,x=>x.Code==20)));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(short flags in new short[]{-1,2,128+2,short.MaxValue})
            Run($"raw-lwpolyline/flags/{flags}",()=>
            {var r=Modified(t=>t[RawLineAt(t,70)]=new(70,flags));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(short code in new short[]{40,41,43})
            Run($"raw-lwpolyline/negative-width/{code}",()=>
            {var r=Modified(t=>{if(code==43)t.Insert(RawLineAt(t,10),new(43,-1.0));else t[RawLineAt(t,code)]=new(code,-1.0);});Throws<FormatException>(()=>RawLwRead(r));});
        foreach(int index in new[]{-1,3,int.MaxValue})
            Run($"raw-lwpolyline/index/{index}",()=>
            {var r=Modified(_=>{});Throws<ArgumentOutOfRangeException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),index,Vector2.Zero,0,0,0));});
        for(int field=0;field<5;field++)foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            int f=field;
            Run($"raw-lwpolyline/nonfinite/{f}/{ParameterBits(bad)}",()=>
            {var r=Modified(_=>{});var x=new double[5];x[f]=bad;Throws<ArgumentOutOfRangeException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,new(x[0],x[1]),x[2],x[3],x[4]));});
        }
        foreach(bool start in new[]{false,true})
            Run($"raw-lwpolyline/reject-width/{start}",()=>
            {var r=Modified(_=>{});byte[] before=SaveRaw(r);Throws<ArgumentOutOfRangeException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,Vector2.Zero,start?-1:0,start?0:-1,0));Check(before.SequenceEqual(SaveRaw(r)),"Rejected mutation");});
        foreach(short code in new short[]{92,310,1005,1010,1041,1042})
            Run($"raw-lwpolyline/guard/{code}",()=>
            {
                var r=Modified(t=>t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,code==92?(object)0:code==310?new byte[]{1}:code==1005?"B":2.0)));
                var p=RawLwRead(r).Vertices[1];var record=RawLwRecord(r);
                Check(ReferenceEquals(r,r.WithLwPolylineVertex(record,1,p.Position,p.StartWidth,p.EndWidth,p.Bulge)),"Guarded no-op");
                Throws<NotSupportedException>(()=>r.WithLwPolylineVertex(record,1,Vector2.Zero,0,0,0));
            });
        foreach(short code in new short[]{320,330,340,350,360,1005})
            Run($"raw-lwpolyline/incoming/{code}",()=>
            {var r=Modified(t=>{int a=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(a++,new(1001,"REF"));t.Insert(a,new(code,"000a"));});Throws<NotSupportedException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,Vector2.Zero,0,0,0));});
        foreach(short code in new short[]{10,90,100,101,102})
            Run($"raw-lwpolyline/after-xdata/{code}",()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,1001)+1,new(code,code==10?(object)1.0:code==90?3:code==100?"AcDbPolyline":code==101?"Embedded Object":"{APP")));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(bool embedded in new[]{false,true})
            Run($"raw-lwpolyline/private/{embedded}",()=>
            {
                var r=Modified(t=>t.InsertRange(RawLineAt(t,1001),embedded?new DxfTag[]{new(101,"Embedded Object"),new(10,999.0),new(102,"}")}:new DxfTag[]{new(102,"{APP"),new(10,999.0),new(90,-1),new(102,"}")}));
                Equal(3,RawLwRead(r).Vertices.Count,"Private coordinates leaked");Throws<NotSupportedException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,Vector2.Zero,0,0,0));
            });
        foreach(string value in new[]{"}","bad","{OPEN"})
            Run("raw-lwpolyline/control/"+value,()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,1001),new(102,value)));Throws<FormatException>(()=>RawLwRead(r));});
        foreach(string value in new[]{"AcDbLine","AcDbPolyline","Unknown"})
            Run("raw-lwpolyline/subclass/"+value,()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,90),new(100,value)));Throws<NotSupportedException>(()=>RawLwRead(r));});
        Run("raw-lwpolyline/constant-width-conflict",()=>
        {
            var r=Modified(_=>{},2);var p=RawLwRead(r).Vertices[1];byte[] before=SaveRaw(r);
            Throws<NotSupportedException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,p.Position,2,3,p.Bulge));
            Check(before.SequenceEqual(SaveRaw(r)),"Width-policy failure mutated source");
        });
        Run("raw-lwpolyline/optional-budget",()=>
        {
            var tags=RawLwTags(DxfVersion.AutoCad14,false,1);var r=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));
            Throws<InvalidOperationException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),1,Vector2.Zero,1,2,1));
            var edited=r.WithLwPolylineVertex(RawLwRecord(r),1,Vector2.Zero,0,0,0);Equal(tags.Count,edited.Tags.Count,"Defaults materialized");
            r=DxfRawDocument.Create(tags);edited=r.WithLwPolylineVertex(RawLwRecord(r),1,new(-0.0,double.Epsilon),-0.0,double.Epsilon,-0.0);
            var p=RawLwRead(edited).Vertices[1];SameDoubleBits(-0.0,p.Position.X,"Signed zero position");SameDoubleBits(-0.0,p.StartWidth,"Signed zero width");SameDoubleBits(-0.0,p.Bulge,"Signed zero bulge");
            Check(p.HasStartWidth&&p.HasEndWidth&&p.HasBulge,"Optional fields absent");
        });
        Run("raw-lwpolyline/empty-definition",()=>
        {
            var r=Modified(t=>{int a=RawLineAt(t,10),b=RawLineAt(t,210);t.RemoveRange(a,b-a);t[RawLineAt(t,90)]=new(90,0);});
            Equal(0,RawLwRead(r).Vertices.Count,"Empty geometry");Throws<ArgumentOutOfRangeException>(()=>r.WithLwPolylineVertex(RawLwRecord(r),0,Vector2.Zero,0,0,0));
        });
        Run("raw-lwpolyline/profile-snapshot",()=>
        {
            var r=Modified(_=>{});var other=Modified(_=>{});Throws<ArgumentException>(()=>r.ReadLwPolylineGeometry(RawLwRecord(other)));
            Throws<ArgumentNullException>(()=>r.ReadLwPolylineGeometry(null!));
            foreach(var version in new[]{DxfVersion.AutoCad12,DxfVersion.AutoCad13}){var old=DxfRawDocument.Create(RawLwTags(version));Throws<NotSupportedException>(()=>RawLwRead(old));}
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"raw-lwpolyline/typed/{version}/{binary}",()=>
            {
                var p=new Polyline2D(new[]{new Vector2(1,2),new Vector2(4,5),new Vector2(7,8)},true);
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(p);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Typed source save");
                var raw=LoadRaw(stream.ToArray());var changed=raw.WithLwPolylineVertex(RawLwRecord(raw),1,new(-8.5,16.25),1.25,2.5,1);
                using var input=new MemoryStream(SaveRaw(changed,binary));var q=DxfDocument.Load(input)!.Entities.Polylines2D.Single();
                Equal(3,q.Vertexes.Count,"Typed count");Check(q.IsClosed,"Typed closure");
                SameDoubleBits(1.25,q.Vertexes[1].StartWidth,"Typed width");SameDoubleBits(1,q.Vertexes[1].Bulge,"Typed bulge");
            });
    }
}
