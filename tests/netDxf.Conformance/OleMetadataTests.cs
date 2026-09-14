using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static readonly short[][] OleMetadataGroupCodes =
    {
        new short[] {70}, new short[] {3}, new short[] {10,20,30},
        new short[] {11,21,31}, new short[] {71}, new short[] {72}
    };

    private static void RegisterOleMetadataTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] {false,true})
                for (int mask=0; mask<64; ++mask)
                {
                    DxfVersion v=version;bool b=binary;int m=mask;
                    Run($"olemetadata/wire/{v}/{b}/{m}",()=>OleMetadataWire(v,b,m));
                }
    }

    private static List<DxfTag> OleMetadataTags(DxfVersion version,int mask)
    {
        var tags=OleTags(version,33,3,true);
        int start=tags.FindIndex(t=>t.Code==100 && Equals(t.Value,"AcDbOle2Frame"));
        var remove=new HashSet<short>();
        for(int bit=0;bit<6;++bit)
            if((mask&(1<<bit))==0)foreach(short code in OleMetadataGroupCodes[bit])remove.Add(code);
        int end=tags.FindIndex(t=>t.Code==1001);
        for(int at=end-1;at>start;--at)if(remove.Contains(tags[at].Code))tags.RemoveAt(at);
        return tags;
    }

    private static void CheckOleMetadataPresence(DxfRawRecord record,int mask)
    {
        var tags=record.Tags.SkipWhile(t=>t.Code!=100 || !Equals(t.Value,"AcDbOle2Frame")).Skip(1).ToArray();
        for(int bit=0;bit<6;++bit)
            foreach(short code in OleMetadataGroupCodes[bit])
                Equal((mask&(1<<bit))!=0,tags.Any(t=>t.Code==code),$"Optional metadata presence {code}");
        Equal(33,(int)tags.Single(t=>t.Code==90).Value,"Metadata payload length");
        Check(tags.Where(t=>t.Code==310).SelectMany(t=>(byte[])t.Value).SequenceEqual(OlePayload(33)),"Metadata editing changed payload.");
        Equal("OLE",(string)tags.Single(t=>t.Code==1).Value,"Required terminator changed");
    }

    private static void OleMetadataWire(DxfVersion version,bool binary,int mask)
    {
        using var input=new MemoryStream(RawFixtureBytes(OleMetadataTags(version,mask),binary));
        var doc=DxfDocument.Load(input)??throw new InvalidOperationException("Optional OLE metadata rejected.");
        var frame=doc.Entities.Ole2Frames.Single();
        // Existing default-valued getters remain unchanged. Wire presence is separate information.
        Equal((mask&2)!=0?"Picture Żółć":"",frame.Description,"Optional description value");
        Equal((short)2,frame.OleVersion,"Existing default version");
        Equal((mask&16)!=0?OleObjectType.Static:OleObjectType.Embedded,frame.ObjectType,"Existing default object kind");
        Equal((mask&4)!=0?new Vector3(1.0000000000000002,6,-2):Vector3.Zero,frame.UpperLeftCorner,"Optional upper value");
        Equal((mask&8)!=0?new Vector3(8,-4,-2):Vector3.Zero,frame.LowerRightCorner,"Optional lower value");
        var clone=(Ole2Frame)frame.Clone();doc.Entities.Add(clone);
        var block=new Block("OptionalOle");block.Entities.Add((Ole2Frame)frame.Clone());
        var insert=(Insert)new Insert(block).Clone();
        using(var separate=new MemoryStream())
        {
            var nested=new DxfDocument(version);nested.Entities.Add(insert);
            Check(nested.Save(separate,binary),"Nested optional metadata save failed.");separate.Position=0;
            var record=DxfRawDocument.Load(separate).Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="OLE2FRAME");
            CheckOleMetadataPresence(record,mask);
        }
        for(int cycle=0;cycle<3;++cycle)
        {
            using var output=new MemoryStream();
            Check(doc.Save(output,cycle%2==0?!binary:binary),"Optional metadata save failed.");output.Position=0;
            var raw=DxfRawDocument.Load(output);
            foreach(var record in raw.Sections.SelectMany(s=>s.Records).Where(r=>r.Name=="OLE2FRAME"))CheckOleMetadataPresence(record,mask);
            if(cycle==1 && new[]{0,1,2,4,8,16,32,63}.Contains(mask))
                File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"olemetadata-{version}-{binary}-{mask}.dxf"),output.ToArray());
            output.Position=0;doc=DxfDocument.Load(output)??throw new InvalidOperationException("Optional metadata reload failed.");
            Equal(2,doc.Entities.Ole2Frames.Count(),"Metadata original/clone count");
            foreach(var current in doc.Entities.Ole2Frames)
                Equal("after binary data",(string)current.XData["OLE_TEST"].XDataRecord.Single().Value,"Adjacent metadata XData");
            Equal(new Vector3(10,20,30),doc.Entities.Lines.Single().StartPoint,"Entity after metadata changed");
        }
        Check(input.CanRead,"Metadata reader closed the caller stream.");
    }
}
