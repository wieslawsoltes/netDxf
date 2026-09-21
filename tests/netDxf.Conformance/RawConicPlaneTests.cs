// Copyright (c) netDxf contributors. Licensed under the MIT License.
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static readonly short[] ConicPlaneFields = { 10,20,30,40,50,51,39,210,220,230 };
    private static DxfRawDocument ConicPlaneEdit(DxfRawDocument raw,DxfRawRecord record,bool arc,
        Vector3 center,double radius,double thickness,Vector3 normal,double start=350,double end=35)
        => arc ? raw.WithArcGeometryAndPlane(record,center,radius,start,end,thickness,normal)
               : raw.WithCircleGeometryAndPlane(record,center,radius,thickness,normal);
    private static List<DxfTag> ConicPlaneSource(DxfVersion version,bool arc,bool block,bool missing)
    {
        var tags=ConicTags(version,arc,block,missing);
        if(missing) tags.RemoveAll(t=>t.Code==39||t.Code==210||t.Code==220||t.Code==230);
        return tags;
    }
    private static void ConicPlanePacket(DxfRawRecord record)
    {
        int radius=record.Tags.ToList().FindIndex(t=>t.Code==40);
        Equal((short)39,record.Tags[radius+1].Code,"Thickness packet position");
        for(int i=0;i<3;i++) Equal((short)(210+i*10),record.Tags[radius+2+i].Code,"Complete adjacent extrusion");
    }
    private static void RegisterRawConicPlaneTests()
    {
        foreach(DxfVersion version in HandleProfiles)foreach(bool arc in new[]{false,true})
            foreach(bool binary in new[]{false,true})foreach(bool block in new[]{false,true})foreach(bool missing in new[]{false,true})
                Run($"conic-plane/matrix/{version}/{arc}/{binary}/{block}/{missing}",()=>
                {
                    var tags=ConicPlaneSource(version,arc,block,missing);
                    byte[] bytes=version==DxfVersion.AutoCad12?RawR12Bytes(tags,binary):RawFixtureBytes(tags,binary);
                    var raw=LoadRaw(bytes);var record=ConicRecord(raw,arc);var initial=ConicRead(raw,record,arc);
                    Vector3 center=new(-8,16,32),normal=new(0,2,0);
                    Check(ReferenceEquals(raw,ConicPlaneEdit(raw,record,arc,new(1.25,-2,missing?0:3),7.5,missing?0:-2.5,
                        missing?Vector3.UnitZ:-Vector3.UnitZ,15,270)),"No-op snapshot identity");
                    var edited=ConicPlaneEdit(raw,record,arc,center,3.75,-3,normal);var result=ConicRecord(edited,arc);
                    ConicPlanePacket(result);AssertOutsideRecordUnchanged(raw,record,edited,result.Tags.Count);
                    var before=record.Tags.Where(t=>!ConicPlaneFields.Contains(t.Code)).ToArray();
                    var after=result.Tags.Where(t=>!ConicPlaneFields.Contains(t.Code)).ToArray();SameRawTags(before,after);
                    Check(before.Zip(after).All(p=>ReferenceEquals(p.First,p.Second)),"Unselected tag identities");
                    var oldX=record.Tags.FirstOrDefault(t=>t.Code==210);
                    if(oldX!=null)Check(result.Tags.Any(t=>ReferenceEquals(t,oldX)),"Unchanged extrusion component not reused");
                    Check(bytes.SequenceEqual(SaveRaw(raw))&&!edited.HasOriginalBytes,"Source byte isolation");
                    RawLinePointBits(new(1.25,-2,missing?0:3),RawLinePoint(initial,"CenterInObjectCoordinates"));
                    foreach(bool output in new[]{false,true})
                    {
                        byte[] outputBytes=SaveRaw(edited,output);var loaded=LoadRaw(outputBytes);var g=ConicRead(loaded,ConicRecord(loaded,arc),arc);
                        RawLinePointBits(center,RawLinePoint(g,"CenterInObjectCoordinates"));RawLinePointBits(normal,RawLinePoint(g,"ExtrusionDirection"));
                        SameDoubleBits(-3,ConicScalar(g,"Thickness"),"Signed thickness");SameDoubleBits(3.75,ConicScalar(g,"Radius"),"Radius");
                        if(arc){SameDoubleBits(350,ConicScalar(g,"StartAngle"),"Start");SameDoubleBits(35,ConicScalar(g,"EndAngle"),"End");}
                        Equal(version,loaded.Version,"Declared version");SameRawTags(edited.Tags,loaded.Tags);
                        string stem=$"conic-plane-{version}-{arc}-{binary}-{block}-{missing}-{output}";
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-before.dxf"),SaveRaw(raw,output));
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem+"-after.dxf"),outputBytes);
                    }
                    Check(ReferenceEquals(edited,ConicPlaneEdit(edited,result,arc,center,3.75,-3,normal)),"Repeated edit identity");
                    Throws<ArgumentException>(()=>ConicPlaneEdit(edited,record,arc,center,3.75,-3,normal));
                });
        foreach(bool arc in new[]{false,true})
        {
            DxfRawDocument Source(Action<List<DxfTag>> change,bool missing=false,DxfRawOptions? options=null)
            {var tags=ConicPlaneSource(DxfVersion.AutoCad2018,arc,false,missing);change(tags);return DxfRawDocument.Create(tags,false,options);}
            foreach(short code in new short[]{92,310,1005,1010,1041,1042})
                Run($"conic-plane/guard/{arc}/{code}",()=>
                {
                    var raw=Source(t=>t.Insert(RawLineAt(t,1001)+(code>=1000?1:0),new(code,code==92?(object)0:code==310?new byte[]{1}:code==1005?"B":1.0)));
                    var record=ConicRecord(raw,arc);byte[] source=SaveRaw(raw);
                    Check(ReferenceEquals(raw,ConicPlaneEdit(raw,record,arc,new(1.25,-2,3),7.5,-2.5,-Vector3.UnitZ,15,270)),"Guarded no-op");
                    Throws<NotSupportedException>(()=>ConicPlaneEdit(raw,record,arc,Vector3.Zero,3,-3,Vector3.UnitY));
                    Check(source.SequenceEqual(SaveRaw(raw)),"Guard changed source");
                });
            foreach(short code in new short[]{320,330,340,350,360,1005})
                Run($"conic-plane/incoming/{arc}/{code}",()=>
                {
                    var raw=Source(t=>{int at=t.FindIndex(x=>x.Code==0&&Equals(x.Value,"POINT"))+2;if(code==1005)t.Insert(at++,new(1001,"REF"));t.Insert(at,new(code,"000a"));});
                    Throws<NotSupportedException>(()=>ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,Vector3.Zero,3,-3,Vector3.UnitY));
                });
            foreach(double bad in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})foreach(int index in (arc ? Enumerable.Range(0,10) : new[]{0,1,2,3,6,7,8,9}))
            {
                int i=index;Run($"conic-plane/nonfinite/{arc}/{i}/{ParameterBits(bad)}",()=>
                {
                    var raw=Source(_=>{});var record=ConicRecord(raw,arc);double[] p={0,0,0,3,.25,5.75,-3,0,1,0};p[i]=bad;
                    Throws<ArgumentOutOfRangeException>(()=>ConicPlaneEdit(raw,record,arc,new(p[0],p[1],p[2]),p[3],p[6],new(p[7],p[8],p[9]),p[4],p[5]));
                });
            }
            foreach(double radius in new[]{0.0,-1.0})
                Run($"conic-plane/radius/{arc}/{radius}",()=>{var raw=Source(_=>{});Throws<ArgumentOutOfRangeException>(()=>ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,Vector3.Zero,radius,0,Vector3.UnitZ));});
            Run($"conic-plane/zero-normal/{arc}",()=>{var raw=Source(_=>{});Throws<ArgumentOutOfRangeException>(()=>ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,Vector3.Zero,1,0,Vector3.Zero));});
            Run($"conic-plane/budget-bits/{arc}",()=>
            {
                var tags=ConicPlaneSource(DxfVersion.AutoCad2018,arc,false,true);
                foreach(int allowance in new[]{3,4,5})
                {
                    var raw=DxfRawDocument.Create(tags,false,new DxfRawOptions(maximumTags:tags.Count+allowance));
                    if(allowance<5)Throws<InvalidOperationException>(()=>ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,new(1,2,3),1,-0.0,new(-0.0,1,0)));
                    else
                    {
                        var edit=ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,new(1,2,3),1,-0.0,new(-0.0,1,0));
                        Equal(tags.Count+5,edit.Tags.Count,"Full vector exact budget");var r=ConicRecord(edit,arc);ConicPlanePacket(r);
                        SameDoubleBits(-0.0,(double)r.Tags.Single(t=>t.Code==39).Value,"Thickness zero bit");
                        SameDoubleBits(-0.0,(double)r.Tags.Single(t=>t.Code==210).Value,"Normal zero bit");
                    }
                }
                var original=DxfRawDocument.Create(tags);var record=ConicRecord(original,arc);
                var onlyRadius=ConicPlaneEdit(original,record,arc,new(1.25,-2,0),3,0,Vector3.UnitZ,15,270);
                Check(!ConicRecord(onlyRadius,arc).Tags.Any(t=>t.Code==39||t.Code==210||t.Code==220||t.Code==230),"Unchanged absent defaults materialized");
            });
            for(int variant=0;variant<4;variant++)
            {
                int v=variant;Run($"conic-plane/vector-packet/{arc}/{v}",()=>
                {
                    var raw=Source(t=>
                    {
                        int at=RawLineAt(t,210);var x=t[at];var y=t[at+1];var z=t[at+2];t.RemoveRange(at,3);
                        if(v==0)t.Insert(at,z);
                        if(v==1)t.InsertRange(at,new[]{z,new DxfTag(60,(short)0),y,x});
                        if(v==2)t.InsertRange(RawLineAt(t,1001),new[]{z,y,x});
                    });
                    var record=ConicRecord(raw,arc);var unselected=record.Tags.Where(t=>t.Code!=210&&t.Code!=220&&t.Code!=230&&t.Code!=39).ToArray();
                    var edit=ConicPlaneEdit(raw,record,arc,new(1.25,-2,3),7.5,-3,new(0,2,0),15,270);var r=ConicRecord(edit,arc);ConicPlanePacket(r);
                    var others=r.Tags.Where(t=>t.Code!=210&&t.Code!=220&&t.Code!=230&&t.Code!=39).ToArray();
                    Check(unselected.Zip(others).All(p=>ReferenceEquals(p.First,p.Second))&&unselected.Length==others.Length,"Regroup changed unrelated identity/order");
                    foreach(bool binary in new[]{false,true})
                    {
                        string stem=$"conic-plane-packet-{arc}-{v}-{binary}.dxf";
                        File.WriteAllBytes(Path.Combine(ArtifactDirectory,stem),SaveRaw(edit,binary));
                    }
                });
            }
            foreach(bool embedded in new[]{false,true})
                Run($"conic-plane/private/{arc}/{embedded}",()=>
                {
                    var raw=Source(t=>t.InsertRange(RawLineAt(t,1001),embedded
                        ? new DxfTag[]{new(101,"Embedded Object"),new(210,99.0)}
                        : new DxfTag[]{new(102,"{PRIVATE"),new(210,99.0),new(102,"}")}));
                    var r=ConicRecord(raw,arc);byte[] before=SaveRaw(raw);
                    RawLinePointBits(-Vector3.UnitZ,RawLinePoint(ConicRead(raw,r,arc),"ExtrusionDirection"));
                    Throws<NotSupportedException>(()=>ConicPlaneEdit(raw,r,arc,Vector3.Zero,1,0,Vector3.UnitY));
                    Check(before.SequenceEqual(SaveRaw(raw)),"Private refusal changed source");
                });
            foreach(DxfVersion version in SupportedVersions)foreach(bool binary in new[]{false,true})
                Run($"conic-plane/typed/{arc}/{version}/{binary}",()=>
                {
                    var doc=new DxfDocument(version);doc.Comments.Clear();
                    EntityObject e=arc?new Arc(new Vector3(1,2,3),7.5,15,270):new Circle(new Vector3(1,2,3),7.5);
                    doc.Entities.Add(e);using var stream=new MemoryStream();Check(doc.Save(stream,binary),"Typed conic source save");
                    var raw=LoadRaw(stream.ToArray());var edit=ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,new(-8,16,32),3.75,-3,new(0,2,0));
                    using var input=new MemoryStream(SaveRaw(edit,binary));var loaded=DxfDocument.Load(input)!;
                    Vector3 center=arc?loaded.Entities.Arcs.Single().Center:loaded.Entities.Circles.Single().Center;
                    double thickness=arc?loaded.Entities.Arcs.Single().Thickness:loaded.Entities.Circles.Single().Thickness;
                    Vector3 normal=arc?loaded.Entities.Arcs.Single().Normal:loaded.Entities.Circles.Single().Normal;
                    RawLinePointBits(new(8,32,16),center);RawLinePointBits(Vector3.UnitY,normal);SameDoubleBits(-3,thickness,"Typed signed thickness");
                    if(arc){SameDoubleBits(350,loaded.Entities.Arcs.Single().StartAngle,"Typed start");SameDoubleBits(35,loaded.Entities.Arcs.Single().EndAngle,"Typed end");}
                    Equal(0,loaded.Objects.Validate().Count,"Typed edited graph");
                });
            foreach(double scale in new[]{double.Epsilon,-double.Epsilon,1e-200,-1e200,double.MaxValue})
                Run($"conic-plane/extreme-normal/{arc}/{ParameterBits(scale)}",()=>
                {
                    var raw=Source(_=>{});var edit=ConicPlaneEdit(raw,ConicRecord(raw,arc),arc,Vector3.Zero,double.Epsilon,-scale,new(0,0,scale),-725,725);
                    var g=ConicRead(edit,ConicRecord(edit,arc),arc);RawLinePointBits(new(0,0,scale),RawLinePoint(g,"ExtrusionDirection"));
                    if(arc){SameDoubleBits(-725,ConicScalar(g,"StartAngle"),"No angle normalization");SameDoubleBits(725,ConicScalar(g,"EndAngle"),"No end normalization");}
                });
            Run($"conic-plane/snapshot-and-old-api/{arc}",()=>
            {
                var raw=Source(_=>{});var other=Source(_=>{});var r=ConicRecord(raw,arc);
                Throws<ArgumentException>(()=>ConicPlaneEdit(raw,ConicRecord(other,arc),arc,Vector3.Zero,3,0,Vector3.UnitZ));
                Throws<ArgumentNullException>(()=>ConicPlaneEdit(raw,null!,arc,Vector3.Zero,3,0,Vector3.UnitZ));
                var old=ConicEdit(raw,r,arc,Vector3.Zero,3);var added=ConicPlaneEdit(raw,r,arc,Vector3.Zero,3,-2.5,-Vector3.UnitZ);
                SameRawTags(old.Tags,added.Tags);
            });
        }
    }
}
