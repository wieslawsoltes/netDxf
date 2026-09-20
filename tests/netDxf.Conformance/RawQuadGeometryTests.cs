// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] RawQuadFields = { 10,20,30,11,21,31,12,22,32,13,23,33,39 };
    private static Vector3[] RawQuadSourcePoints(int variant)
    {
        var points = new[] { new Vector3(1,2,3), new Vector3(5,2,3), new Vector3(1,6,3), new Vector3(5,6,3) };
        if (variant == 1) points = points.Select(p => new Vector3(p.X,p.Y,0)).ToArray();
        if (variant == 2) points[3] = points[2];
        if (variant == 3) for (int i=0;i<4;i++) points[i] = new Vector3(points[i].X,points[i].Y,1 << i);
        return points;
    }
    private static Vector3[] RawQuadCorners(DxfRawQuadGeometry geometry) => new[] {
        geometry.FirstVertexInObjectCoordinates, geometry.SecondVertexInObjectCoordinates,
        geometry.ThirdVertexInObjectCoordinates, geometry.FourthVertexInObjectCoordinates };
    private static DxfRawRecord RawQuadRecord(DxfRawDocument raw, bool trace)
        => raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name == (trace ? "TRACE" : "SOLID"));
    private static DxfRawQuadGeometry RawQuadRead(DxfRawDocument raw, DxfRawRecord record, bool trace)
        => trace ? raw.ReadTraceGeometry(record) : raw.ReadSolidGeometry(record);
    private static DxfRawDocument RawQuadEdit(DxfRawDocument raw, DxfRawRecord record, bool trace, Vector3[] points, double thickness)
        => trace ? raw.WithTraceGeometry(record,points[0],points[1],points[2],points[3],thickness)
                 : raw.WithSolidGeometry(record,points[0],points[1],points[2],points[3],thickness);
    private static List<DxfTag> RawQuadTags(DxfVersion version, bool trace, bool block, int variant)
    {
        var tags=RawFaceTags(version,block,0);
        int a=tags.FindIndex(t=>t.Code==0 && Equals(t.Value,"3DFACE"));
        int b=tags.FindIndex(a+1,t=>t.Code==0);
        var part=new List<DxfTag>{new(0,trace ? "TRACE" : "SOLID"),new(5,"A")};
        if(version >= DxfVersion.AutoCad13) part.Add(new(100,"AcDbEntity"));
        part.Add(new(8,"0")); part.Add(new(62,(short)3));
        if(version >= DxfVersion.AutoCad13) part.Add(new(100,"AcDbTrace"));
        Vector3[] points=RawQuadSourcePoints(variant);
        for(int i=0;i<4;i++)
        {
            part.Add(new((short)(10+i),points[i].X));part.Add(new((short)(20+i),points[i].Y));
            if(variant!=1)part.Add(new((short)(30+i),points[i].Z));
        }
        if(variant!=1)
        {
            part.Add(new(39,-2.5));part.Add(new(210,0.0));
            part.Add(new(220,variant==2 ? 0.0 : variant==3 ? 6.0 : .6));
            part.Add(new(230,variant==2 ? -1.0 : variant==3 ? 8.0 : .8));
        }
        part.AddRange(new DxfTag[]{new(1001,"RAW_QUAD"),new(1000,"retain quad"),new(1040,3.125),new(1070,(short)-7)});
        tags.RemoveRange(a,b-a);tags.InsertRange(a,part);return tags;
    }
    private static DxfRawDocument RawQuadSource(DxfVersion version,bool trace,bool binary,bool block,int variant)
    {
        var tags=RawQuadTags(version,trace,block,variant);
        return LoadRaw(version==DxfVersion.AutoCad12 ? RawR12Bytes(tags,binary) : RawFixtureBytes(tags,binary));
    }
    private static void RawQuadMatrix(DxfVersion version,bool trace,bool binary,bool block,int variant)
    {
        var raw=RawQuadSource(version,trace,binary,block,variant);var record=RawQuadRecord(raw,trace);
        byte[] original=SaveRaw(raw);DxfRawQuadGeometry view=RawQuadRead(raw,record,trace);
        Vector3[] source=RawQuadSourcePoints(variant);
        for(int i=0;i<4;i++)RawLinePointBits(source[i],RawQuadCorners(view)[i]);
        SameDoubleBits(variant==1 ? 0 : -2.5,view.Thickness,"Stored quad thickness");
        var normal=variant==1 ? Vector3.UnitZ : variant==2 ? -Vector3.UnitZ : variant==3 ? new Vector3(0,6,8) : new Vector3(0,.6,.8);
        RawLinePointBits(normal,view.ExtrusionDirection);
        Check(ReferenceEquals(raw,RawQuadEdit(raw,record,trace,source,view.Thickness)),"Quad no-op replaced snapshot");
        var changed=RawQuadEdit(raw,record,trace,RawFaceTargets,-4.25);var updated=RawQuadRecord(changed,trace);
        AssertOutsideRecordUnchanged(raw,record,changed,updated.Tags.Count);
        var retained=record.Tags.Where(t=>!RawQuadFields.Contains(t.Code)).ToArray();
        var replaced=updated.Tags.Where(t=>!RawQuadFields.Contains(t.Code)).ToArray();SameRawTags(retained,replaced);
        Check(retained.Zip(replaced).All(p=>ReferenceEquals(p.First,p.Second)),"Unselected quad tags lost identity");
        Check(original.SequenceEqual(SaveRaw(raw)),"Quad edit mutated source bytes");Check(!changed.HasOriginalBytes,"Edited snapshot retained stale byte shortcut");
        foreach(bool output in new[]{false,true})
        {
            byte[] bytes=SaveRaw(changed,output);var loaded=LoadRaw(bytes);var geometry=RawQuadRead(loaded,RawQuadRecord(loaded,trace),trace);
            for(int i=0;i<4;i++)RawLinePointBits(RawFaceTargets[i],RawQuadCorners(geometry)[i]);
            SameDoubleBits(-4.25,geometry.Thickness,"Written thickness");RawLinePointBits(normal,geometry.ExtrusionDirection);
            Equal(version,loaded.Version,"Quad version changed");Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Quad encoding changed");SameRawTags(changed.Tags,loaded.Tags);
            string stem=$"raw-quad-{version}-{trace}-{binary}-{block}-{variant}-{output}";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
        }
        Throws<ArgumentException>(()=>RawQuadRead(changed,record,trace));
        Throws<ArgumentException>(()=>RawQuadRead(raw,record,!trace));
    }
    private static void RegisterRawQuadGeometryTests()
    {
        foreach(DxfVersion version in HandleProfiles)foreach(bool trace in new[]{false,true})foreach(bool binary in new[]{false,true})
            foreach(bool block in new[]{false,true})for(int variant=0;variant<4;variant++)
            {int v=variant;Run($"raw-quad/matrix/{version}/{trace}/{binary}/{block}/{v}",()=>RawQuadMatrix(version,trace,binary,block,v));}
        foreach(bool trace in new[]{false,true})
        {
            DxfRawDocument Modified(Action<List<DxfTag>> edit)
            {var tags=RawQuadTags(DxfVersion.AutoCad2018,trace,false,0);edit(tags);return DxfRawDocument.Create(tags);}
            foreach(short code in RawQuadFields.Concat(new short[]{210,220,230}))
                Run($"raw-quad/duplicate/{trace}/{code}",()=>
                {var raw=Modified(t=>{int i=RawLineAt(t,code);t.Insert(i,t[i]);});Throws<FormatException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));});
            foreach(short code in new short[]{10,20,11,21,12,22,13,23})
                Run($"raw-quad/missing/{trace}/{code}",()=>
                {var raw=Modified(t=>t.RemoveAt(RawLineAt(t,code)));Throws<FormatException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));});
            foreach(short code in new short[]{70,91,92,160,310,1005,1010,1041,1042})
                Run($"raw-quad/guard/{trace}/{code}",()=>
                {
                    object value=code==70 ? (object)(short)1 : code==91||code==92 ? 1 : code==160 ? 1L : code==310 ? new byte[]{1} : code==1005 ? "B" : 2.0;
                    var raw=Modified(t=>t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,value)));var record=RawQuadRecord(raw,trace);var g=RawQuadRead(raw,record,trace);
                    Check(ReferenceEquals(raw,RawQuadEdit(raw,record,trace,RawQuadCorners(g),g.Thickness)),"Decorated no-op changed");
                    byte[] before=SaveRaw(raw);Throws<NotSupportedException>(()=>RawQuadEdit(raw,record,trace,RawFaceTargets,-4.25));Check(before.SequenceEqual(SaveRaw(raw)),"Guard mutated source");
                });
            foreach(short code in new short[]{320,330,340,350,360,1005})
                Run($"raw-quad/incoming/{trace}/{code}",()=>
                {
                    var raw=Modified(t=>{int i=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(i++,new(1001,"REF"));t.Insert(i,new(code,"000a"));});
                    Throws<NotSupportedException>(()=>RawQuadEdit(raw,RawQuadRecord(raw,trace),trace,RawFaceTargets,0));
                });
            foreach(short code in new short[]{10,39,100,101,102})
                Run($"raw-quad/xdata-scope/{trace}/{code}",()=>
                {
                    object value=code==100 ? (object)"AcDbTrace" : code==101 ? "Embedded Object" : code==102 ? "{APP" : 1.0;
                    var raw=Modified(t=>t.Insert(RawLineAt(t,1001)+1,new(code,value)));
                    Throws<FormatException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));
                });
            foreach(bool embedded in new[]{false,true})
                Run($"raw-quad/private-scope/{trace}/{embedded}",()=>
                {
                    var raw=Modified(t=>t.InsertRange(RawLineAt(t,1001),embedded ? new DxfTag[]{new(101,"Embedded Object"),new(10,999.0),new(39,777.0)}
                        : new DxfTag[]{new(102,"{APP"),new(10,999.0),new(39,777.0),new(102,"}")}));var record=RawQuadRecord(raw,trace);var g=RawQuadRead(raw,record,trace);
                    RawLinePointBits(RawQuadSourcePoints(0)[0],g.FirstVertexInObjectCoordinates);SameDoubleBits(-2.5,g.Thickness,"Private thickness contaminated geometry");
                    Check(ReferenceEquals(raw,RawQuadEdit(raw,record,trace,RawQuadCorners(g),g.Thickness)),"Private no-op replaced snapshot");
                    Throws<NotSupportedException>(()=>RawQuadEdit(raw,record,trace,RawFaceTargets,0));
                });
            foreach(string control in new[]{"}","BAD","{OPEN"})
                Run($"raw-quad/control/{trace}/{control}",()=>
                {var raw=Modified(t=>t.Insert(RawLineAt(t,1001),new(102,control)));Throws<FormatException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));});
            foreach(string subclass in new[]{"AcDbFace","AcDbSolid","AcDbTrace"})
                Run($"raw-quad/subclass/{trace}/{subclass}",()=>
                {var raw=Modified(t=>t.Insert(RawLineAt(t,10),new(100,subclass)));Throws<NotSupportedException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));});
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})for(int slot=0;slot<13;slot++)
            {
                int s=slot;Run($"raw-quad/nonfinite/{trace}/{ParameterBits(bad)}/{s}",()=>
                {
                    var raw=RawQuadSource(DxfVersion.AutoCad2018,trace,false,false,0);var x=new double[13];x[s]=bad;
                    var points=Enumerable.Range(0,4).Select(i=>new Vector3(x[3*i],x[3*i+1],x[3*i+2])).ToArray();
                    Throws<ArgumentOutOfRangeException>(()=>RawQuadEdit(raw,RawQuadRecord(raw,trace),trace,points,x[12]));
                });
            }
            foreach(double value in new[]{-0.0,double.Epsilon,-double.Epsilon,double.MaxValue,-double.MaxValue})
                Run($"raw-quad/extreme/{trace}/{ParameterBits(value)}",()=>
                {
                    var raw=RawQuadSource(DxfVersion.AutoCad12,trace,false,false,1);var p=Enumerable.Repeat(new Vector3(value,value,value),4).ToArray();
                    var changed=RawQuadEdit(raw,RawQuadRecord(raw,trace),trace,p,value);
                    foreach(bool binary in new[]{false,true})
                    {var loaded=LoadRaw(SaveRaw(changed,binary));var g=RawQuadRead(loaded,RawQuadRecord(loaded,trace),trace);foreach(var point in RawQuadCorners(g))RawLinePointBits(p[0],point);SameDoubleBits(value,g.Thickness,"Extreme thickness");}
                });
            Run($"raw-quad/zero-extrusion/{trace}",()=>
            {var raw=Modified(t=>{foreach(short code in new short[]{210,220,230})t[RawLineAt(t,code)]=new(code,0.0);});Throws<FormatException>(()=>RawQuadRead(raw,RawQuadRecord(raw,trace),trace));});
            Run($"raw-quad/record-admission/{trace}",()=>
            {
                var raw=RawQuadSource(DxfVersion.AutoCad2018,trace,false,false,0);var other=RawQuadSource(DxfVersion.AutoCad2018,trace,false,false,0);
                Throws<ArgumentNullException>(()=>RawQuadRead(raw,null!,trace));Throws<ArgumentException>(()=>RawQuadRead(raw,RawQuadRecord(other,trace),trace));
                Throws<ArgumentException>(()=>RawQuadRead(raw,raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="POINT"),trace));
            });
            Run($"raw-quad/tag-budget/{trace}",()=>
            {
                var tags=RawQuadTags(DxfVersion.AutoCad12,trace,false,1);var raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));var record=RawQuadRecord(raw,trace);
                byte[] before=SaveRaw(raw);Throws<InvalidOperationException>(()=>RawQuadEdit(raw,record,trace,RawFaceTargets,-4.25));
                Throws<InvalidOperationException>(()=>RawQuadEdit(raw,record,trace,RawQuadSourcePoints(1),-0.0));
                var flat=RawFaceTargets.Select(p=>new Vector3(p.X,p.Y,0)).ToArray();var changed=RawQuadEdit(raw,record,trace,flat,0);
                Equal(tags.Count,changed.Tags.Count,"Default quad fields materialized");Check(before.SequenceEqual(SaveRaw(raw)),"Budget rejection mutated source");
            });
            foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
                Run($"raw-quad/typed/{trace}/{version}/{binary}",()=>
                {
                    EntityObject entity=trace ? new Trace() : new Solid();
                    var t=entity.GetType();string[] names={"FirstVertex","SecondVertex","ThirdVertex","FourthVertex"};
                    Vector3[] points=RawQuadSourcePoints(0);
                    for(int i=0;i<4;i++)t.GetProperty(names[i])!.SetValue(entity,new Vector2(points[i].X,points[i].Y));
                    t.GetProperty("Elevation")!.SetValue(entity,3.0);t.GetProperty("Thickness")!.SetValue(entity,-2.5);entity.Normal=new Vector3(0,.6,.8);
                    var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(entity);using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Typed quad save");
                    var raw=LoadRaw(stream.ToArray());var flat=RawFaceTargets.Select(p=>new Vector3(p.X,p.Y,-7.5)).ToArray();
                    var changed=RawQuadEdit(raw,RawQuadRecord(raw,trace),trace,flat,-4.25);
                    using var input=new MemoryStream(SaveRaw(changed,binary));var loaded=DxfDocument.Load(input)!;
                    EntityObject result=trace ? loaded.Entities.Traces.Single() : loaded.Entities.Solids.Single();
                    for(int i=0;i<4;i++){var p=(Vector2)t.GetProperty(names[i])!.GetValue(result)!;SameDoubleBits(flat[i].X,p.X,"Typed OCS X");SameDoubleBits(flat[i].Y,p.Y,"Typed OCS Y");}
                    SameDoubleBits(-7.5,(double)t.GetProperty("Elevation")!.GetValue(result)!,"Typed elevation");
                    SameDoubleBits(-4.25,(double)t.GetProperty("Thickness")!.GetValue(result)!,"Typed thickness");
                });
        }
    }
}
