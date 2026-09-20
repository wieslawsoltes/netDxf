// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawInfiniteFields = {10,20,30,11,21,31};
    private static readonly Vector3 RawInfiniteTargetOrigin = new(-8.5,16.25,-32);
    private static Vector3 RawInfiniteTargetDirection(bool xline) => xline ? new(-.6,0,-.8) : new(.6,0,.8);
    private static DxfRawRecord RawInfiniteRecord(DxfRawDocument raw, bool xline)
        => raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name==(xline ? "XLINE" : "RAY"));
    private static object RawInfiniteRead(DxfRawDocument raw,DxfRawRecord record,bool xline)
        => RawLineCall(raw,xline ? "ReadXLineGeometry" : "ReadRayGeometry",record);
    private static DxfRawDocument RawInfiniteEdit(DxfRawDocument raw,DxfRawRecord record,bool xline,Vector3 origin,Vector3 direction)
        => (DxfRawDocument)RawLineCall(raw,xline ? "WithXLineGeometry" : "WithRayGeometry",record,origin,direction);
    private static Vector3 RawInfiniteSourceDirection(int variant) => variant==2 ? new(0,0,-1) : new(.6,.8,0);
    private static List<DxfTag> RawInfiniteTags(DxfVersion version,bool xline,bool block=false,int variant=0)
    {
        var tags=RawLineTags(version,block,variant==1 ? 1 : 0);
        int start=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE"));
        int end=tags.FindIndex(start+1,t=>t.Code==0);
        var record=tags.GetRange(start,end-start);
        record[0]=new(0,xline ? "XLINE" : "RAY");
        int marker=record.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbLine"));
        if(marker>=0)record[marker]=new(100,xline ? "AcDbXline" : "AcDbRay");
        record.RemoveAll(t=>t.Code==39||t.Code==210||t.Code==220||t.Code==230||(variant==3&&t.Code==100));
        var d=RawInfiniteSourceDirection(variant);
        for(int i=0;i<record.Count;i++)
        {
            if(record[i].Code==11)record[i]=new(11,d.X);
            else if(record[i].Code==21)record[i]=new(21,d.Y);
            else if(record[i].Code==31)record[i]=new(31,d.Z);
        }
        tags.RemoveRange(start,end-start);tags.InsertRange(start,record);return tags;
    }
    private static DxfRawDocument RawInfiniteSource(DxfVersion version,bool xline,bool binary=false,bool block=false,int variant=0)
        => LoadRaw(RawFixtureBytes(RawInfiniteTags(version,xline,block,variant),binary));
    private static void RegisterRawInfiniteGeometryTests()
    {
        foreach(DxfVersion version in HandleProfiles.Where(v=>v>=DxfVersion.AutoCad13))
            foreach(bool xline in new[]{false,true})foreach(bool binary in new[]{false,true})foreach(bool block in new[]{false,true})
                for(int variant=0;variant<4;variant++)
                {
                    int v=variant;
                    Run($"raw-infinite/matrix/{version}/{xline}/{binary}/{block}/{v}",()=>
                    {
                        var raw=RawInfiniteSource(version,xline,binary,block,v);var record=RawInfiniteRecord(raw,xline);
                        byte[] before=SaveRaw(raw);var view=RawInfiniteRead(raw,record,xline);
                        Vector3 origin=new(1.25,-2,v==1 ? 0 : 3),direction=RawInfiniteSourceDirection(v);
                        RawLinePointBits(origin,RawLinePoint(view,"Origin"));RawLinePointBits(direction,RawLinePoint(view,"Direction"));
                        Check(ReferenceEquals(raw,RawInfiniteEdit(raw,record,xline,origin,direction)),"Infinite no-op lost original snapshot");
                        var changed=RawInfiniteEdit(raw,record,xline,RawInfiniteTargetOrigin,RawInfiniteTargetDirection(xline));
                        var target=RawInfiniteRecord(changed,xline);AssertOutsideRecordUnchanged(raw,record,changed,target.Tags.Count);
                        var a=record.Tags.Where(t=>!RawInfiniteFields.Contains(t.Code)).ToArray();
                        var b=target.Tags.Where(t=>!RawInfiniteFields.Contains(t.Code)).ToArray();SameRawTags(a,b);
                        Check(a.Zip(b).All(p=>ReferenceEquals(p.First,p.Second)),"Unselected infinite tags replaced");
                        Check(before.SequenceEqual(SaveRaw(raw))&&!changed.HasOriginalBytes,"Source bytes or edited-original flag changed");
                        RawLinePointBits(origin,RawLinePoint(view,"Origin"));RawLinePointBits(direction,RawLinePoint(view,"Direction"));
                        foreach(bool output in new[]{false,true})
                        {
                            byte[] bytes=SaveRaw(changed,output);var restored=LoadRaw(bytes);var geometry=RawInfiniteRead(restored,RawInfiniteRecord(restored,xline),xline);
                            RawLinePointBits(RawInfiniteTargetOrigin,RawLinePoint(geometry,"Origin"));
                            RawLinePointBits(RawInfiniteTargetDirection(xline),RawLinePoint(geometry,"Direction"));
                            Equal(version,restored.Version,"Infinite dialect changed");Equal(raw.EncodingCodePage,restored.EncodingCodePage,"Infinite encoding changed");
                            SameRawTags(changed.Tags,restored.Tags);
                            string stem=$"raw-infinite-{version}-{xline}-{binary}-{block}-{v}-{output}";
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
                            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
                        }
                        Throws<ArgumentException>(()=>RawInfiniteRead(changed,record,xline));
                        Throws<ArgumentException>(()=>RawInfiniteEdit(changed,record,xline,origin,direction));
                    });
                }
        foreach(bool xline in new[]{false,true})
        {
            DxfRawDocument Modified(Action<List<DxfTag>> action)
            {var tags=RawInfiniteTags(DxfVersion.AutoCad2018,xline);action(tags);return DxfRawDocument.Create(tags);}
            foreach(short code in RawInfiniteFields)
                Run($"raw-infinite/duplicate/{xline}/{code}",()=>
                {var r=Modified(t=>{int a=RawLineAt(t,code);t.Insert(a,t[a]);});Throws<FormatException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));});
            foreach(short code in new short[]{10,20,11,21})
                Run($"raw-infinite/missing/{xline}/{code}",()=>
                {var r=Modified(t=>t.RemoveAt(RawLineAt(t,code)));Throws<FormatException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));});
            foreach(double length in new[]{0.0,.5,2.0,double.MaxValue})
                Run($"raw-infinite/stored-direction/{xline}/{ParameterBits(length)}",()=>
                {
                    var r=Modified(t=>{t[RawLineAt(t,11)]=new(11,length);t[RawLineAt(t,21)]=new(21,0.0);t[RawLineAt(t,31)]=new(31,0.0);});
                    Throws<FormatException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));
                });
            foreach(var bad in new[]{Vector3.Zero,new Vector3(2,0,0),new Vector3(double.NaN,0,1),new Vector3(0,double.PositiveInfinity,1),new Vector3(1,0,double.NegativeInfinity)})
                Run($"raw-infinite/replacement-direction/{xline}/{string.Join('-',ParameterVector(bad))}",()=>
                {
                    var r=RawInfiniteSource(DxfVersion.AutoCad2018,xline);byte[] before=SaveRaw(r);
                    Throws<ArgumentOutOfRangeException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,bad));
                    Check(before.SequenceEqual(SaveRaw(r)),"Rejected unit direction mutated source");
                });
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})for(int axis=0;axis<3;axis++)
            {
                int a=axis;
                Run($"raw-infinite/replacement-origin/{xline}/{a}/{ParameterBits(bad)}",()=>
                {var r=RawInfiniteSource(DxfVersion.AutoCad2018,xline);var p=new double[3];p[a]=bad;Throws<ArgumentOutOfRangeException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,new(p[0],p[1],p[2]),Vector3.UnitX));});
            }
            foreach(short code in new short[]{39,210,92,310,1005,1010,1041,1042})
                Run($"raw-infinite/guard/{xline}/{code}",()=>
                {
                    var r=Modified(t=>t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,code==92?(object)0:code==310?new byte[]{1}:code==1005?"B":2.0)));
                    var record=RawInfiniteRecord(r,xline);var view=RawInfiniteRead(r,record,xline);byte[] before=SaveRaw(r);
                    Check(ReferenceEquals(r,RawInfiniteEdit(r,record,xline,RawLinePoint(view,"Origin"),RawLinePoint(view,"Direction"))),"Decorated no-op changed snapshot");
                    Throws<NotSupportedException>(()=>RawInfiniteEdit(r,record,xline,Vector3.Zero,Vector3.UnitX));
                    Check(before.SequenceEqual(SaveRaw(r)),"Guard failure changed source bytes");
                });
            foreach(short code in new short[]{320,330,340,350,360,1005})
                Run($"raw-infinite/incoming/{xline}/{code}",()=>
                {var r=Modified(t=>{int at=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(at++,new(1001,"REF"));t.Insert(at,new(code,"000a"));});Throws<NotSupportedException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,Vector3.UnitX));});
            foreach(string handle in new[]{"0","B"})
                Run($"raw-infinite/identity/{xline}/{handle}",()=>
                {var r=Modified(t=>t[t.FindIndex(x=>x.Code==5&&Equals(x.Value,"A"))]=new(5,handle));Throws<NotSupportedException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,Vector3.UnitX));});
            Run($"raw-infinite/handle-free/{xline}",()=>
            {var r=Modified(t=>t.RemoveAt(t.FindIndex(x=>x.Code==5&&Equals(x.Value,"A"))));RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,Vector3.UnitX);});
            foreach(short code in new short[]{10,100,101,102})
                Run($"raw-infinite/after-xdata/{xline}/{code}",()=>
                {var r=Modified(t=>t.Insert(RawLineAt(t,1001)+1,new(code,code==10?(object)1.0:code==100?"AcDbRay":code==101?"Embedded Object":"{APP")));Throws<FormatException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));});
            foreach(bool embedded in new[]{false,true})
                Run($"raw-infinite/private/{xline}/{embedded}",()=>
                {
                    var r=Modified(t=>t.InsertRange(RawLineAt(t,1001),embedded
                        ? new DxfTag[]{new(101,"Embedded Object"),new(11,1000.0),new(102,"}")}
                        : new DxfTag[]{new(102,"{APP"),new(11,1000.0),new(102,"{INNER"),new(21,1000.0),new(102,"}"),new(102,"}")}));
                    RawLinePointBits(new(.6,.8,0),RawLinePoint(RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline),"Direction"));
                    Throws<NotSupportedException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,Vector3.UnitX));
                });
            foreach(string marker in new[]{"AcDbLine","AcDbRay","AcDbXline","Unknown"})
                Run($"raw-infinite/subclass/{xline}/{marker}",()=>
                {var r=Modified(t=>t.Insert(RawLineAt(t,10),new(100,marker)));Throws<NotSupportedException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));});
            foreach(string control in new[]{"}","bad","{OPEN"})
                Run($"raw-infinite/malformed-control/{xline}/{control}",()=>
                {var r=Modified(t=>t.Insert(RawLineAt(t,1001),new(102,control)));Throws<FormatException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),xline));});
            Run($"raw-infinite/negative-zero-budget/{xline}",()=>
            {
                var tags=RawInfiniteTags(DxfVersion.AutoCad13,xline,false,1);
                var r=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));
                var record=RawInfiniteRecord(r,xline);Throws<InvalidOperationException>(()=>RawInfiniteEdit(r,record,xline,RawInfiniteTargetOrigin,Vector3.UnitZ));
                var changed=RawInfiniteEdit(r,record,xline,Vector3.Zero,Vector3.UnitX);Equal(tags.Count,changed.Tags.Count,"Zero defaults unnecessarily materialized");
                r=DxfRawDocument.Create(tags);record=RawInfiniteRecord(r,xline);
                changed=RawInfiniteEdit(r,record,xline,new(double.MaxValue,-double.MaxValue,-0.0),new(1,0,double.Epsilon));
                var g=RawInfiniteRead(changed,RawInfiniteRecord(changed,xline),xline);
                RawLinePointBits(new(double.MaxValue,-double.MaxValue,-0.0),RawLinePoint(g,"Origin"));
                RawLinePointBits(new(1,0,double.Epsilon),RawLinePoint(g,"Direction"));
            });
            foreach(double epsilon in new[]{1e-12,1.0,100.0})
                Run($"raw-infinite/unit-policy/{xline}/{epsilon}",()=>
                {
                    var r=RawInfiniteSource(DxfVersion.AutoCad2018,xline);double old=MathHelper.Epsilon;
                    try
                    {
                        MathHelper.Epsilon=epsilon;
                        var d=new Vector3(1+1e-13,0,0);var changed=RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,d);
                        RawLinePointBits(d,RawLinePoint(RawInfiniteRead(changed,RawInfiniteRecord(changed,xline),xline),"Direction"));
                        Throws<ArgumentOutOfRangeException>(()=>RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,Vector3.Zero,new(1+1e-10,0,0)));
                    }
                    finally{MathHelper.Epsilon=old;}
                });
            Run($"raw-infinite/snapshot/{xline}",()=>
            {
                var r=RawInfiniteSource(DxfVersion.AutoCad2018,xline);var foreign=RawInfiniteSource(DxfVersion.AutoCad2018,xline);
                Throws<ArgumentNullException>(()=>RawInfiniteRead(r,null!,xline));
                Throws<ArgumentException>(()=>RawInfiniteRead(r,RawInfiniteRecord(foreign,xline),xline));
                Throws<ArgumentException>(()=>RawInfiniteRead(r,RawInfiniteRecord(r,xline),!xline));
                var point=r.Sections.SelectMany(s=>s.Records).First(x=>x.Name=="POINT");
                Throws<ArgumentException>(()=>RawInfiniteRead(r,point,xline));
                var r12=DxfRawDocument.Create(RawInfiniteTags(DxfVersion.AutoCad12,xline));
                Throws<NotSupportedException>(()=>RawInfiniteRead(r12,RawInfiniteRecord(r12,xline),xline));
            });
        }
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})foreach(bool xline in new[]{false,true})
            Run($"raw-infinite/typed/{version}/{binary}/{xline}",()=>
            {
                var doc=new DxfDocument(version);doc.Comments.Clear();
                EntityObject entity=xline ? new XLine(Vector3.Zero,Vector3.UnitX) : new Ray(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(entity);
                using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Typed infinite source save");
                var r=LoadRaw(stream.ToArray());var changed=RawInfiniteEdit(r,RawInfiniteRecord(r,xline),xline,RawInfiniteTargetOrigin,Vector3.UnitZ);
                using var input=new MemoryStream(SaveRaw(changed,binary));var loaded=DxfDocument.Load(input)!;
                var origin=xline ? loaded.Entities.XLines.Single().Origin : loaded.Entities.Rays.Single().Origin;
                var direction=xline ? loaded.Entities.XLines.Single().Direction : loaded.Entities.Rays.Single().Direction;
                RawLinePointBits(RawInfiniteTargetOrigin,origin);RawLinePointBits(Vector3.UnitZ,direction);
            });
    }
}
