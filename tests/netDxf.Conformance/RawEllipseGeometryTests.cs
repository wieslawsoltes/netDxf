// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawEllipseFields = {10,20,30,11,21,31,40,41,42};
    private static DxfRawRecord RawEllipseRecord(DxfRawDocument raw)
        => raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="ELLIPSE");
    private static object RawEllipseRead(DxfRawDocument raw,DxfRawRecord record)
        => RawLineCall(raw,"ReadEllipseGeometry",record);
    private static double RawEllipseScalar(object value,string name)
        => (double)value.GetType().GetProperty(name)!.GetValue(value)!;
    private static DxfRawDocument RawEllipseEdit(DxfRawDocument raw,DxfRawRecord record,Vector3 center,Vector3 major,
        double ratio=.25,double start=.25,double end=5.75)
        => (DxfRawDocument)RawLineCall(raw,"WithEllipseGeometry",record,center,major,ratio,start,end);
    private static Vector3 RawEllipseNormal(int variant) => variant==2 ? new(0,3,4) : variant==3 ? new(0,0,-2) : Vector3.UnitZ;
    private static Vector3 RawEllipseMajor(int variant) => variant==2 ? new(0,4,-3) : variant==3 ? new(-3,4,0) : new(4,0,0);
    private static Vector3 RawEllipseTarget(int variant) => variant==2 ? new(0,-8,6) : new(6,-8,0);
    private static List<DxfTag> RawEllipseTags(DxfVersion version,bool block=false,int variant=0)
    {
        var tags=RawLineTags(version,block);
        int begin=tags.FindIndex(t=>t.Code==0&&Equals(t.Value,"LINE"));
        int end=tags.FindIndex(begin+1,t=>t.Code==0);
        var entity=new List<DxfTag>{new(0,"ELLIPSE"),new(5,"A")};
        if(variant!=3)entity.Add(new(100,"AcDbEntity"));
        entity.AddRange(new DxfTag[]{new(8,"0"),new(62,(short)3),new(48,1.25)});
        if(variant!=3)entity.Add(new(100,"AcDbEllipse"));
        var major=RawEllipseMajor(variant);var normal=RawEllipseNormal(variant);
        entity.AddRange(new DxfTag[]{new(10,1.25),new(20,-2.0)});
        if(variant!=1)entity.Add(new(30,3.0));
        entity.AddRange(new DxfTag[]{new(11,major.X),new(21,major.Y)});
        if(variant!=1)entity.Add(new(31,major.Z));
        entity.AddRange(new DxfTag[]{new(40,variant==3 ? 1.0 : .5),new(41,variant==2 ? 5.5 : 0.0),new(42,variant==2 ? .4 : 2*Math.PI)});
        if(variant!=1)entity.AddRange(new DxfTag[]{new(210,normal.X),new(220,normal.Y),new(230,normal.Z)});
        entity.AddRange(new DxfTag[]{new(1001,"RAW_ELLIPSE"),new(1000,"preserve me"),new(1040,3.125),new(1070,(short)-7)});
        tags.RemoveRange(begin,end-begin);tags.InsertRange(begin,entity);return tags;
    }
    private static void RegisterRawEllipseGeometryTests()
    {
        foreach(DxfVersion version in HandleProfiles.Where(v=>v>=DxfVersion.AutoCad13))
            foreach(bool binary in new[]{false,true})foreach(bool block in new[]{false,true})for(int variant=0;variant<4;variant++)
            {
                int v=variant;
                Run($"raw-ellipse/matrix/{version}/{binary}/{block}/{v}",()=>
                {
                    var raw=LoadRaw(RawFixtureBytes(RawEllipseTags(version,block,v),binary));var record=RawEllipseRecord(raw);
                    byte[] source=SaveRaw(raw);var view=RawEllipseRead(raw,record);
                    var center=new Vector3(1.25,-2,v==1?0:3);
                    RawLinePointBits(center,RawLinePoint(view,"Center"));RawLinePointBits(RawEllipseMajor(v),RawLinePoint(view,"MajorAxis"));
                    RawLinePointBits(RawEllipseNormal(v),RawLinePoint(view,"ExtrusionDirection"));
                    double ratio=v==3?1:.5,start=v==2?5.5:0,end=v==2?.4:2*Math.PI;
                    SameDoubleBits(ratio,RawEllipseScalar(view,"AxisRatio"),"Stored ratio");
                    SameDoubleBits(start,RawEllipseScalar(view,"StartParameter"),"Stored start");SameDoubleBits(end,RawEllipseScalar(view,"EndParameter"),"Stored end");
                    Check(ReferenceEquals(raw,RawEllipseEdit(raw,record,center,RawEllipseMajor(v),ratio,start,end)),"Ellipse no-op lost snapshot");
                    var edited=RawEllipseEdit(raw,record,new(8,-16,32),RawEllipseTarget(v));var replacement=RawEllipseRecord(edited);
                    AssertOutsideRecordUnchanged(raw,record,edited,replacement.Tags.Count);
                    var a=record.Tags.Where(t=>!RawEllipseFields.Contains(t.Code)).ToArray();var b=replacement.Tags.Where(t=>!RawEllipseFields.Contains(t.Code)).ToArray();
                    SameRawTags(a,b);Check(a.Zip(b).All(p=>ReferenceEquals(p.First,p.Second)),"Unselected tag identity lost");
                    Check(source.SequenceEqual(SaveRaw(raw))&&!edited.HasOriginalBytes,"Original bytes mutated or falsely retained");
                    RawLinePointBits(center,RawLinePoint(view,"Center"));
                    foreach(bool output in new[]{false,true})
                    {
                        var bytes=SaveRaw(edited,output);var loaded=LoadRaw(bytes);var g=RawEllipseRead(loaded,RawEllipseRecord(loaded));
                        RawLinePointBits(new(8,-16,32),RawLinePoint(g,"Center"));RawLinePointBits(RawEllipseTarget(v),RawLinePoint(g,"MajorAxis"));
                        RawLinePointBits(RawEllipseNormal(v),RawLinePoint(g,"ExtrusionDirection"));
                        SameDoubleBits(.25,RawEllipseScalar(g,"AxisRatio"),"Ratio changed");SameDoubleBits(.25,RawEllipseScalar(g,"StartParameter"),"Parameter changed");SameDoubleBits(5.75,RawEllipseScalar(g,"EndParameter"),"Parameter changed");
                        Equal(version,loaded.Version,"Dialect changed");Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Encoding changed");SameRawTags(edited.Tags,loaded.Tags);
                        string stem=$"raw-ellipse-{version}-{binary}-{block}-{v}-{output}";
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
                    }
                    Throws<ArgumentException>(()=>RawEllipseRead(edited,record));
                });
            }
        DxfRawDocument Modified(Action<List<DxfTag>> action)
        {var tags=RawEllipseTags(DxfVersion.AutoCad2018);action(tags);return DxfRawDocument.Create(tags);}
        foreach(short code in RawEllipseFields.Concat(new short[]{210,220,230}))
            Run($"raw-ellipse/duplicate/{code}",()=>
            {var r=Modified(t=>{int at=RawLineAt(t,code);t.Insert(at,t[at]);});Throws<FormatException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        foreach(short code in new short[]{10,20,11,21,40,41,42})
            Run($"raw-ellipse/missing/{code}",()=>
            {var r=Modified(t=>t.RemoveAt(RawLineAt(t,code)));Throws<FormatException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        foreach(double bad in new[]{0.0,-1.0,1.0001,double.NaN,double.PositiveInfinity})
            Run("raw-ellipse/ratio/"+ParameterBits(bad),()=>
            {
                var r=Modified(_=>{});byte[] before=SaveRaw(r);
                Throws<ArgumentOutOfRangeException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,Vector3.UnitX,bad));
                Check(before.SequenceEqual(SaveRaw(r)),"Ratio rejection mutated source");
                if(double.IsFinite(bad)){var malformed=Modified(t=>t[RawLineAt(t,40)]=new(40,bad));Throws<FormatException>(()=>RawEllipseRead(malformed,RawEllipseRecord(malformed)));}
            });
        foreach(short code in new short[]{39,92,310,1005,1010,1041,1042})
            Run($"raw-ellipse/guard/{code}",()=>
            {
                var r=Modified(t=>t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,code==92?(object)0:code==310?new byte[]{1}:code==1005?"B":2.0)));
                var record=RawEllipseRecord(r);var g=RawEllipseRead(r,record);
                Check(ReferenceEquals(r,RawEllipseEdit(r,record,RawLinePoint(g,"Center"),RawLinePoint(g,"MajorAxis"),.5,0,2*Math.PI)),"Decorated no-op changed snapshot");
                Throws<NotSupportedException>(()=>RawEllipseEdit(r,record,Vector3.Zero,Vector3.UnitX));
            });
        foreach(short code in new short[]{320,330,340,350,360,1005})
            Run($"raw-ellipse/incoming/{code}",()=>
            {var r=Modified(t=>{int at=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(at++,new(1001,"REF"));t.Insert(at,new(code,"000a"));});Throws<NotSupportedException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,Vector3.UnitX));});
        foreach(bool embedded in new[]{false,true})
            Run($"raw-ellipse/private/{embedded}",()=>
            {
                var r=Modified(t=>t.InsertRange(RawLineAt(t,1001),embedded?new DxfTag[]{new(101,"Embedded Object"),new(40,-5.0),new(102,"}")}:new DxfTag[]{new(102,"{APP"),new(40,-5.0),new(102,"}")}));
                Equal(.5,RawEllipseScalar(RawEllipseRead(r,RawEllipseRecord(r)),"AxisRatio"),"Private ratio leaked");
                Throws<NotSupportedException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,Vector3.UnitX));
            });
        foreach(short code in new short[]{10,100,101,102})
            Run($"raw-ellipse/after-xdata/{code}",()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,1001)+1,new(code,code==10?(object)1.0:code==100?"AcDbEllipse":code==101?"Embedded Object":"{APP")));Throws<FormatException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        foreach(string marker in new[]{"AcDbCircle","AcDbEllipse","Unknown"})
            Run("raw-ellipse/subclass/"+marker,()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,10),new(100,marker)));Throws<NotSupportedException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        foreach(string control in new[]{"}","bad","{OPEN"})
            Run("raw-ellipse/control/"+control,()=>
            {var r=Modified(t=>t.Insert(RawLineAt(t,1001),new(102,control)));Throws<FormatException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        for(int slot=0;slot<8;slot++)foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
        {
            int s=slot;
            Run($"raw-ellipse/nonfinite/{s}/{ParameterBits(bad)}",()=>
            {var r=Modified(_=>{});double[] p={0,0,0,4,0,0,.25,5.75};p[s]=bad;Throws<ArgumentOutOfRangeException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),new(p[0],p[1],p[2]),new(p[3],p[4],p[5]),.5,p[6],p[7]));});
        }
        foreach(double scale in new[]{double.Epsilon,1e-200,1.0,1e200,double.MaxValue})
            Run("raw-ellipse/scale/"+ParameterBits(scale),()=>
            {
                var r=Modified(t=>t[RawLineAt(t,230)]=new(230,scale));
                var edited=RawEllipseEdit(r,RawEllipseRecord(r),new(scale,-scale,0),new(scale,0,0),double.Epsilon,-7,19);
                foreach(bool binary in new[]{false,true}){var loaded=LoadRaw(SaveRaw(edited,binary));var g=RawEllipseRead(loaded,RawEllipseRecord(loaded));RawLinePointBits(new(scale,0,0),RawLinePoint(g,"MajorAxis"));SameDoubleBits(double.Epsilon,RawEllipseScalar(g,"AxisRatio"),"Tiny ratio lost");}
            });
        foreach(double epsilon in new[]{1e-12,1.0,100.0})
            Run($"raw-ellipse/plane-policy/{epsilon}",()=>
            {
                var r=Modified(_=>{});double old=MathHelper.Epsilon;
                try{MathHelper.Epsilon=epsilon;RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,new(1,0,1e-13));Throws<ArgumentOutOfRangeException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,new(1,0,1e-8)));Throws<ArgumentOutOfRangeException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,Vector3.Zero));}
                finally{MathHelper.Epsilon=old;}
            });
        foreach(bool zeroNormal in new[]{false,true})
            Run($"raw-ellipse/invalid-plane/{zeroNormal}",()=>
            {var r=Modified(t=>t[RawLineAt(t,zeroNormal?(short)230:(short)31)]=new(zeroNormal?(short)230:(short)31,zeroNormal?0.0:1.0));Throws<FormatException>(()=>RawEllipseRead(r,RawEllipseRecord(r)));});
        Run("raw-ellipse/omitted-budget",()=>
        {
            var tags=RawEllipseTags(DxfVersion.AutoCad13,false,1);var r=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));
            Throws<InvalidOperationException>(()=>RawEllipseEdit(r,RawEllipseRecord(r),Vector3.UnitZ,Vector3.UnitX));
            var edit=RawEllipseEdit(r,RawEllipseRecord(r),Vector3.Zero,Vector3.UnitX);Equal(tags.Count,edit.Tags.Count,"Unneeded defaults materialized");
            r=DxfRawDocument.Create(tags);edit=RawEllipseEdit(r,RawEllipseRecord(r),new(0,0,-0.0),new(1,0,double.Epsilon));
            var g=RawEllipseRead(edit,RawEllipseRecord(edit));RawLinePointBits(new(0,0,-0.0),RawLinePoint(g,"Center"));RawLinePointBits(new(1,0,double.Epsilon),RawLinePoint(g,"MajorAxis"));
        });
        Run("raw-ellipse/snapshot-profile",()=>
        {
            var r=Modified(_=>{});var other=Modified(_=>{});
            Throws<ArgumentException>(()=>RawEllipseRead(r,RawEllipseRecord(other)));Throws<ArgumentNullException>(()=>RawEllipseRead(r,null!));
            Throws<ArgumentException>(()=>RawEllipseRead(r,r.Sections.SelectMany(s=>s.Records).First(x=>x.Name=="POINT")));
            var legacy=DxfRawDocument.Create(RawEllipseTags(DxfVersion.AutoCad12));Throws<NotSupportedException>(()=>RawEllipseRead(legacy,RawEllipseRecord(legacy)));
        });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"raw-ellipse/typed/{version}/{binary}",()=>
            {
                var d=new DxfDocument(version);d.Comments.Clear();d.Entities.Add(new Ellipse(new Vector3(1,2,3),8,4));
                using var output=new MemoryStream();Check(d.Save(output,binary),"Typed source save");var r=LoadRaw(output.ToArray());
                var edit=RawEllipseEdit(r,RawEllipseRecord(r),new(8,-16,32),new(0,6,0),.25,0,2*Math.PI);
                using var input=new MemoryStream(SaveRaw(edit,binary));var ellipse=DxfDocument.Load(input)!.Entities.Ellipses.Single();
                RawLinePointBits(new(8,-16,32),ellipse.Center);Near(12,ellipse.MajorAxis,"Typed diameter");Near(3,ellipse.MinorAxis,"Typed minor diameter");Near(90,ellipse.Rotation,"Typed rotation");Check(ellipse.IsFullEllipse,"Full ellipse became arc");
            });
    }
}
