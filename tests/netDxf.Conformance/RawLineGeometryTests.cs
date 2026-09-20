// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Reflection;
using System.Runtime.ExceptionServices;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    // Reflection keeps the complete same harness runnable against the preceding library.
    private static object RawLineCall(DxfRawDocument raw, string name, params object[] args)
    {
        MethodInfo method = typeof(DxfRawDocument).GetMethod(name) ?? throw new MissingMethodException(name);
        try { return method.Invoke(raw, args)!; }
        catch (TargetInvocationException e) when (e.InnerException != null)
        { ExceptionDispatchInfo.Capture(e.InnerException).Throw(); throw; }
    }
    private static object RawLineRead(DxfRawDocument raw, DxfRawRecord record) => RawLineCall(raw, "ReadLineGeometry", record);
    private static Vector3 RawLinePoint(object geometry, string property) => (Vector3)geometry.GetType().GetProperty(property)!.GetValue(geometry)!;
    private static DxfRawDocument RawLineEdit(DxfRawDocument raw, DxfRawRecord record, Vector3 start, Vector3 end)
        => (DxfRawDocument)RawLineCall(raw, "WithLineEndpoints", record, start, end);
    private static void RawLinePointBits(Vector3 expected, Vector3 actual)
    {
        SameDoubleBits(expected.X, actual.X, "LINE X"); SameDoubleBits(expected.Y, actual.Y, "LINE Y");
        SameDoubleBits(expected.Z, actual.Z, "LINE Z");
    }
    private static DxfRawRecord RawLineRecord(DxfRawDocument raw)
        => raw.Sections.SelectMany(s => s.Records).First(r => r.Name == "LINE");

    private static List<DxfTag> RawLineTags(DxfVersion version, bool block = false, int variant = 0)
    {
        var result = new List<DxfTag> { new(0, "SECTION"), new(2, "HEADER"), new(9, "$ACADVER"),
            new(1, HandleProfileName(version)), new(9, "$DWGCODEPAGE"), new(3, "ANSI_1252"),
            new(9, "$HANDSEED"), new(5, "20"), new(0, "ENDSEC"),
            new(0, "SECTION"), new(2, block ? "BLOCKS" : "ENTITIES") };
        if (block) result.AddRange(new DxfTag[] { new(0,"BLOCK"), new(2,"TEST"), new(70,(short)0),
            new(10,0.0),new(20,0.0),new(30,0.0) });
        result.Add(new(0,"LINE")); result.Add(new(5,"A"));
        if (version >= DxfVersion.AutoCad13) result.Add(new(100,"AcDbEntity"));
        result.Add(new(8,"0")); result.Add(new(62,(short)3)); result.Add(new(48,1.25));
        if (version >= DxfVersion.AutoCad13) result.Add(new(100,"AcDbLine"));
        var geometry = new List<DxfTag> { new(10,1.25),new(20,-2.0),new(11,4.0),new(21,5.0) };
        if ((variant & 1) == 0) { geometry.Insert(2,new(30,3.0));geometry.Add(new(31,-6.0)); }
        if ((variant & 2) != 0) geometry = geometry.OrderBy(t => t.Code % 10 == 1 ? 0 : 1).ToList();
        result.AddRange(geometry);
        result.AddRange(new DxfTag[] { new(39,-2.5),new(210,0.0),new(220,0.6),new(230,0.8),
            new(1001,"RAW_LINE"),new(1000,"preserve me"),new(1040,3.125),new(1070,(short)-7) });
        // A neighboring entity, block terminator and unknown section must retain their exact tags.
        result.AddRange(new DxfTag[] { new(0,"POINT"),new(5,"B"),new(8,"0"),new(10,11.0),new(20,12.0),new(30,13.0) });
        if (block) result.Add(new(0,"ENDBLK"));
        result.AddRange(new DxfTag[] { new(0,"ENDSEC"),new(0,"SECTION"),new(2,"PRIVATE_PAYLOAD"),
            new(0,"OPAQUE"),new(1,"unchanged"),new(310,new byte[]{0,1,255}),new(0,"ENDSEC"),new(0,"EOF") });
        return result;
    }
    private static DxfRawDocument RawLineSource(DxfVersion version, bool binary = false, bool block = false, int variant = 0)
    {
        var tags = RawLineTags(version, block, variant);
        return LoadRaw(version == DxfVersion.AutoCad12 ? RawR12Bytes(tags, binary) : RawFixtureBytes(tags, binary));
    }
    private static readonly short[] RawLineCoordinateCodes = {10,20,30,11,21,31};
    private static void RawLineMatrix(DxfVersion version, bool binary, bool block, int variant)
    {
        var raw = RawLineSource(version,binary,block,variant); var line = RawLineRecord(raw);
        byte[] original = SaveRaw(raw); object geometry = RawLineRead(raw,line);
        var beforeStart = new Vector3(1.25,-2,(variant & 1)==0 ? 3 : 0);
        var beforeEnd = new Vector3(4,5,(variant & 1)==0 ? -6 : 0);
        RawLinePointBits(beforeStart,RawLinePoint(geometry,"StartPoint"));
        RawLinePointBits(beforeEnd,RawLinePoint(geometry,"EndPoint"));
        RawLinePointBits(new(0,.6,.8),RawLinePoint(geometry,"ExtrusionDirection"));
        SameDoubleBits(-2.5,(double)geometry.GetType().GetProperty("Thickness")!.GetValue(geometry)!,"Thickness");
        Check(ReferenceEquals(raw,RawLineEdit(raw,line,beforeStart,beforeEnd)),"No-op lost snapshot identity");
        var start = new Vector3(-8.5,16.25,-32); var end = new Vector3(64,-128.5,256.25);
        DxfRawDocument changed = RawLineEdit(raw,line,start,end); var updated = RawLineRecord(changed);
        Check(!changed.HasOriginalBytes,"Changed snapshot retained original bytes");
        AssertOutsideRecordUnchanged(raw,line,changed,updated.Tags.Count);
        SameRawTags(line.Tags.Where(t=>!RawLineCoordinateCodes.Contains(t.Code)).ToArray(),
            updated.Tags.Where(t=>!RawLineCoordinateCodes.Contains(t.Code)).ToArray());
        var untouched = updated.Tags.Where(t=>!RawLineCoordinateCodes.Contains(t.Code)).ToArray();
        Check(line.Tags.Where(t=>!RawLineCoordinateCodes.Contains(t.Code)).Zip(untouched)
            .All(p=>ReferenceEquals(p.First,p.Second)),"Non-coordinate tag identity changed");
        Check(original.SequenceEqual(SaveRaw(raw)),"Source bytes mutated");
        RawLinePointBits(beforeStart,RawLinePoint(geometry,"StartPoint"));
        foreach(bool output in new[]{false,true})
        {
            byte[] bytes = SaveRaw(changed,output); var loaded=LoadRaw(bytes);
            object read=RawLineRead(loaded,RawLineRecord(loaded));
            RawLinePointBits(start,RawLinePoint(read,"StartPoint")); RawLinePointBits(end,RawLinePoint(read,"EndPoint"));
            Equal(version,loaded.Version,"Version changed"); Equal(raw.EncodingCodePage,loaded.EncodingCodePage,"Encoding changed");
            SameRawTags(changed.Tags,loaded.Tags);
            string stem=$"raw-line-{version}-{binary}-{block}-{variant}-{output}";
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
            File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),bytes);
        }
        Throws<ArgumentException>(()=>RawLineRead(changed,line));
        Throws<ArgumentException>(()=>RawLineEdit(changed,line,start,end));
    }
    private static DxfRawDocument RawLineModified(Action<List<DxfTag>> change)
    { var tags=RawLineTags(DxfVersion.AutoCad2018);change(tags);return DxfRawDocument.Create(tags); }
    private static int RawLineAt(List<DxfTag> tags, short code) => tags.FindIndex(t=>t.Code==code);
    private static void RawLineRejectMutation(DxfRawDocument raw)
    {
        var line=RawLineRecord(raw); byte[] before=SaveRaw(raw);
        Throws<NotSupportedException>(()=>RawLineEdit(raw,line,Vector3.Zero,Vector3.UnitX));
        Check(before.SequenceEqual(SaveRaw(raw)),"Rejected edit mutated source");
    }
    private static void RegisterRawLineGeometryTests()
    {
        foreach(DxfVersion version in HandleProfiles) foreach(bool binary in new[]{false,true})
            foreach(bool block in new[]{false,true}) for(int variant=0;variant<4;variant++)
            { int v=variant;Run($"raw-line/matrix/{version}/{binary}/{block}/{v}",()=>RawLineMatrix(version,binary,block,v)); }
        foreach(short code in RawLineCoordinateCodes.Concat(new short[]{39,210,220,230}))
            Run($"raw-line/duplicate/{code}",()=>
            {
                var raw=RawLineModified(t=>{int at=RawLineAt(t,code);t.Insert(at,t[at]);});
                Throws<FormatException>(()=>RawLineRead(raw,RawLineRecord(raw)));
            });
        foreach(short code in new short[]{10,20,11,21})
            Run($"raw-line/missing/{code}",()=>
            {
                var raw=RawLineModified(t=>t.RemoveAt(RawLineAt(t,code)));
                Throws<FormatException>(()=>RawLineRead(raw,RawLineRecord(raw)));
            });
        foreach(short code in new short[]{92,160,310,91,1005,1010,1020,1030})
            Run($"raw-line/unsupported/{code}",()=>
            {
                var raw=RawLineModified(t=>
                {
                    object value=code==92||code==91 ? (object)0 : code==160 ? 0L : code==310 ? new byte[]{1} : code==1005 ? "B" : 2.0;
                    int at=RawLineAt(t,1001)+(code>=1000 ? 1 : 0);t.Insert(at,new DxfTag(code,value));
                });
                RawLineRead(raw,RawLineRecord(raw));RawLineRejectMutation(raw);
            });
        foreach(string group in new[]{"{VENDOR","{ACAD_REACTORS","{ACAD_XDICTIONARY"})
            Run("raw-line/control/"+group,()=>
            {
                var raw=RawLineModified(t=>t.InsertRange(RawLineAt(t,10),new DxfTag[]{new(102,group),new(10,99.0),new(102,"{NESTED"),new(20,88.0),new(102,"}"),new(102,"}")}));
                RawLinePointBits(new(1.25,-2,3),RawLinePoint(RawLineRead(raw,RawLineRecord(raw)),"StartPoint"));
                RawLineRejectMutation(raw);
            });
        Run("raw-line/embedded-tail",()=>
        {
            var raw=RawLineModified(t=>t.InsertRange(RawLineAt(t,1001),new DxfTag[]{new(101,"Embedded Object"),new(10,999.0),new(100,"NOT_LINE"),new(102,"}"),new(11,888.0)}));
            RawLinePointBits(new(1.25,-2,3),RawLinePoint(RawLineRead(raw,RawLineRecord(raw)),"StartPoint"));
            RawLineRejectMutation(raw);
        });
        foreach(string marker in new[]{"AcDbCircle","AcDbLine","Other"})
            Run("raw-line/wrong-subclass/"+marker,()=>
            {
                var raw=RawLineModified(t=>t.Insert(RawLineAt(t,10),new(100,marker)));
                Throws<NotSupportedException>(()=>RawLineRead(raw,RawLineRecord(raw)));
            });
        Run("raw-line/unmatched-control",()=>
        {
            var raw=RawLineModified(t=>t.Insert(RawLineAt(t,10),new(102,"}")));
            Throws<FormatException>(()=>RawLineRead(raw,RawLineRecord(raw)));
        });
        Run("raw-line/unterminated-control",()=>
        {
            var raw=RawLineModified(t=>t.Insert(RawLineAt(t,1001),new(102,"{OPEN")));
            Throws<FormatException>(()=>RawLineRead(raw,RawLineRecord(raw)));
        });
        foreach(short code in new short[]{330,340,350,360,320,1005})
            Run($"raw-line/incoming/{code}",()=>
            {
                var raw=RawLineModified(t=>
                {
                    int at=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;
                    if(code==1005)t.Insert(at++,new(1001,"REFERENCES"));
                    t.Insert(at,new(code,"000a"));
                });RawLineRejectMutation(raw);
            });
        Run("raw-line/duplicate-identity",()=>
        { RawLineRejectMutation(RawLineModified(t=>{int at=t.FindLastIndex(x=>x.Code==5);t[at]=new(5,"A");})); });
        Run("raw-line/null-identity",()=>
        { RawLineRejectMutation(RawLineModified(t=>{int at=t.FindIndex(x=>x.Code==5&&Equals(x.Value,"A"));t[at]=new(5,"0");})); });
        Run("raw-line/handle-free",()=>
        {
            var raw=RawLineModified(t=>t.RemoveAt(t.FindIndex(x=>x.Code==5&&Equals(x.Value,"A"))));
            var changed=RawLineEdit(raw,RawLineRecord(raw),Vector3.Zero,Vector3.UnitX);
            RawLinePointBits(Vector3.Zero,RawLinePoint(RawLineRead(changed,RawLineRecord(changed)),"StartPoint"));
        });
        Run("raw-line/defaults-and-negative-zero",()=>
        {
            var tags=RawLineTags(DxfVersion.AutoCad12,false,1);tags.RemoveAll(t=>t.Code==39||t.Code==210||t.Code==220||t.Code==230);
            var raw=DxfRawDocument.Create(tags);var line=RawLineRecord(raw);var read=RawLineRead(raw,line);
            RawLinePointBits(Vector3.UnitZ,RawLinePoint(read,"ExtrusionDirection"));
            Equal(0.0,(double)read.GetType().GetProperty("Thickness")!.GetValue(read)!,"Default thickness");
            var edit=RawLineEdit(raw,line,new(1.25,-2,-0.0),new(4,5,double.Epsilon));var updated=RawLineRecord(edit);
            SameDoubleBits(-0.0,(double)updated.Tags.Single(t=>t.Code==30).Value,"Negative zero inserted");
            SameDoubleBits(double.Epsilon,(double)updated.Tags.Single(t=>t.Code==31).Value,"Subnormal inserted");
            Check(!updated.Tags.Any(t=>t.Code==39||t.Code==210),"Unrelated defaults materialized");
        });
        Run("raw-line/budget",()=>
        {
            var tags=RawLineTags(DxfVersion.AutoCad12,false,1);
            var raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count));var line=RawLineRecord(raw);
            byte[] before=SaveRaw(raw);Throws<InvalidOperationException>(()=>RawLineEdit(raw,line,Vector3.UnitZ,Vector3.UnitZ));
            Check(before.SequenceEqual(SaveRaw(raw)),"Budget failure mutated source");
        });
        foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})for(int axis=0;axis<6;axis++)
        {int a=axis;Run($"raw-line/nonfinite/{ParameterBits(bad)}/{a}",()=>
            {
                var raw=RawLineSource(DxfVersion.AutoCad2018);var p=new double[6];p[a]=bad;
                Throws<ArgumentOutOfRangeException>(()=>RawLineEdit(raw,RawLineRecord(raw),new(p[0],p[1],p[2]),new(p[3],p[4],p[5])));
            });}
        Run("raw-line/foreign-and-null",()=>
        {
            var raw=RawLineSource(DxfVersion.AutoCad2018);var other=RawLineSource(DxfVersion.AutoCad2018);
            Throws<ArgumentException>(()=>RawLineRead(raw,RawLineRecord(other)));
            Throws<ArgumentNullException>(()=>RawLineRead(raw,null!));
            var point=raw.Sections.SelectMany(s=>s.Records).First(r=>r.Name=="POINT");
            Throws<ArgumentException>(()=>RawLineRead(raw,point));
        });
        foreach(string name in new[]{"ASCII_R12.dxf","bin_dxf_r12.dxf","small_r13.dxf","small_r14.dxf","bin_dxf_r13.dxf","bin_dxf_r14.dxf"})
            Run("raw-line/native/"+name,()=>
            {
                var raw=LoadRaw(File.ReadAllBytes(Path.Combine("tests","fixtures","legacy",name)));
                if(name.StartsWith("small_",StringComparison.Ordinal))
                {
                    // These native files have no physical LINE records. Readers may generate
                    // default arrow blocks; those generated objects are not source evidence.
                    Check(!raw.Sections.SelectMany(s=>s.Records).Any(r=>r.Name=="LINE"),"Native inventory changed");
                    var other=raw.Sections.SelectMany(s=>s.Records).First();
                    Throws<ArgumentException>(()=>RawLineRead(raw,other));
                    return;
                }
                var line=RawLineRecord(raw);
                var read=RawLineRead(raw,line);Vector3 start=new(-8.5,16.25,-32),end=new(64,-128.5,256.25);
                var changed=RawLineEdit(raw,line,start,end);AssertOutsideRecordUnchanged(raw,line,changed,RawLineRecord(changed).Tags.Count);
                foreach(bool binary in new[]{false,true})
                {
                    string stem=$"raw-line-native-{name}-{binary}";
                    // Native text comments cannot be emitted as binary without explicit removal.
                    var source=raw.WithTags(raw.Tags.Where(t=>t.Code!=999));var edited=changed.WithTags(changed.Tags.Where(t=>t.Code!=999));
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(source,binary));
                    File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),SaveRaw(edited,binary));
                    SameRawTags(edited.Tags,LoadRaw(SaveRaw(edited,binary)).Tags);
                }
            });
        foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
            Run($"raw-line/typed/{version}/{binary}",()=>
            {
                var doc=new DxfDocument(version);doc.Comments.Clear();doc.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)){Thickness=-2,Normal=new(0,.6,.8)});
                using var output=new MemoryStream();Check(doc.Save(output,binary),"Typed source save");
                var raw=LoadRaw(output.ToArray());var changed=RawLineEdit(raw,RawLineRecord(raw),Vector3.Zero,Vector3.UnitX);
                using var input=new MemoryStream(SaveRaw(changed,binary));var loaded=DxfDocument.Load(input)!.Entities.Lines.Single();
                RawLinePointBits(Vector3.Zero,loaded.StartPoint);RawLinePointBits(Vector3.UnitX,loaded.EndPoint);
                Equal(-2.0,loaded.Thickness,"Typed thickness changed");
            });
    }
}
