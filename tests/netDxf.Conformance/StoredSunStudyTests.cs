using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredSunStudyTests()
    {
        foreach (string file in Directory.GetFiles("tests/fixtures/sunstudy-producer/typed-carriers", "*.dxf").OrderBy(value => value))
        foreach (bool inputBinary in new[] { false, true }) foreach (bool outputBinary in new[] { false, true })
        {
            string path = file; bool input = inputBinary, output = outputBinary;
            Run($"sunstudy/stored/producer/{Path.GetFileNameWithoutExtension(file)}/{input}/{output}", () => StoredSunStudyProducer(path, input, output));
        }
        foreach (bool binary in new[] { false, true })
        {
            bool format = binary;
            foreach (string shape in new[] { "version", "dates", "private", "subclass", "extra", "arbitrary", "older" })
            { string variant = shape; Run($"sunstudy/stored/opaque/{shape}/{binary}", () => StoredSunStudyOpaque(variant, format)); }
            foreach (int code in new[] { 340,341,342,343 })
            { int field = code; Run($"sunstudy/stored/missing-reference/{code}/{binary}", () => StoredSunStudyInvalid(field, format)); }
            foreach (string invalid in new[] { "hours", "negative-hours", "self-owner", "missing-owner" })
            { string variant = invalid; Run($"sunstudy/stored/invalid/{invalid}/{binary}", () => StoredSunStudyMalformed(variant, format)); }
            Run($"sunstudy/stored/lifecycle/{binary}", () => StoredSunStudyLifecycle(format));
            Run($"sunstudy/stored/metadata/{binary}", () => StoredSunStudyMetadata(format));
            Run($"sunstudy/stored/null-references/{binary}", () => StoredSunStudyNull(format));
        }
    }
    private static DxfRawRecord SunStudyPacket(DxfRawDocument raw) => raw.Sections.Single(section => section.Name == "OBJECTS").Records.Single(record => record.Name == "SUNSTUDY");
    private static DxfRawDocument SunStudySource(string? file = null)
    {
        file ??= "tests/fixtures/sunstudy-producer/typed-carriers/ixmilia-sunstudy-R2018-ascii-no-dates-hours.dxf";
        byte[] bytes = File.ReadAllBytes(file);
        using var manifest = JsonDocument.Parse(File.ReadAllBytes("tests/fixtures/sunstudy-producer/typed-manifest.json"));
        var entry = manifest.RootElement.GetProperty("files").EnumerateArray().Single(item => item.GetProperty("name").GetString() == Path.GetFileName(file));
        Equal(entry.GetProperty("sha256").GetString()!, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), "Pinned SUNSTUDY carrier");
        return LoadRaw(bytes);
    }
    private static DxfDocument? LoadSunStudy(DxfRawDocument raw, bool binary)
    {
        byte[] data = SaveRaw(raw.WithTags(raw.Tags), binary);
        Equal(binary, data.AsSpan().StartsWith("AutoCAD Binary DXF"u8), "SUNSTUDY input transport sentinel");
        using var input = new MemoryStream(data); return DxfDocument.Load(input);
    }
    private static DxfStoredSunStudy GetSunStudy(DxfDocument document) => document.Objects.Items.OfType<DxfStoredSunStudy>().Single();
    private static void StoredSunStudyProducer(string file, bool inputBinary, bool outputBinary)
    {
        var raw = SunStudySource(file); var document = LoadSunStudy(raw, inputBinary)!; Check(document != null, "Qualified SUNSTUDY carrier typed load");
        var study = GetSunStudy(document!); var packet = SunStudyPacket(raw);
        Equal(0,study.Version,"Observed internal SUNSTUDY version"); Equal(0,study.DateCount,"Empty date array"); Equal(raw.Version,study.SourceVersion,"Source profile");
        Equal("Stored solar study",study.Name,"Setup name"); Equal("Producer storage probe",study.Description,"Description");
        Equal((short)0,study.OutputType,"Uninterpreted output type"); Equal("SHEET_SET",study.SheetSetName,"Sheet set"); Equal("SHEET_SUBSET",study.SheetSubsetName,"Sheet subset");
        Equal(28800,study.StartTime,"Raw start");Equal(64800,study.EndTime,"Raw end");Equal(3600,study.Interval,"Raw interval");
        Equal(4,study.References.Count,"Four source targets"); Check(study.Owner is DxfDictionary,"Source owner dictionary");
        foreach(var target in new[]{study.PageSetup,study.View,study.VisualStyle,study.TextStyle}) Check(ReferenceEquals(target,document!.GetObjectByHandle(target.Handle)),"Exact source dependency");
        Check(OwnershipTagValues(packet.Tags.SkipWhile(tag=>tag.Code!=100)).SequenceEqual(OwnershipTagValues(study.Payload)),"Complete source payload");
        Equal(file.Contains("no-hours")?0:4,study.RawHourFlags.Count,"Ordered raw hour flags");
        if(study.RawHourFlags.Count!=0) Check(study.RawHourFlags.SequenceEqual(new[]{false,true,false,true}),"Repeated290 order");
        Equal(0,document!.Objects.Validate().Count,"Source graph validity");
        using var output=new MemoryStream();Check(document.Save(output,outputBinary),"Stored SUNSTUDY save");
        Equal(outputBinary,output.ToArray().AsSpan().StartsWith("AutoCAD Binary DXF"u8),"SUNSTUDY output transport sentinel");
        string name=$"sunstudy-stored-{Path.GetFileNameWithoutExtension(file)}-input-{inputBinary}-output-{outputBinary}.dxf";File.WriteAllBytes(Path.Combine(ArtifactDirectory,name),output.ToArray());
        output.Position=0;var saved=DxfRawDocument.Load(output);
        Check(OwnershipTagValues(packet.Tags).SequenceEqual(OwnershipTagValues(SunStudyPacket(saved).Tags)),"Complete SUNSTUDY identity/owner/body");
        output.Position=0;var copy=GetSunStudy(DxfDocument.Load(output)!); Check(OwnershipTagValues(study.Payload).SequenceEqual(OwnershipTagValues(copy.Payload)),"Reload immutable payload");
    }
    private static DxfRawDocument MutateSunStudy(Func<List<DxfTag>,List<DxfTag>> change)
    { var raw=SunStudySource();var packet=SunStudyPacket(raw);return raw.WithRecord(packet,change(packet.Tags.ToList())); }
    private static void RejectSunStudy(DxfRawDocument raw,bool binary)
    {bool rejected;try{rejected=LoadSunStudy(raw,binary)==null;}catch(Exception e)when(e is FormatException or InvalidDataException or ArgumentException or InvalidOperationException){rejected=true;}Check(rejected,"Invalid SUNSTUDY accepted");}
    private static void StoredSunStudyInvalid(int code,bool binary)
    {RejectSunStudy(MutateSunStudy(tags=>tags.Select(tag=>tag.Code==code?new DxfTag((short)code,"7FFFFFFE"):tag).ToList()),binary);}
    private static void StoredSunStudyMalformed(string variant,bool binary)
    {
        RejectSunStudy(MutateSunStudy(tags=>tags.Select(tag=>variant switch {
            "hours" when tag.Code==73=>new DxfTag(73,(short)3),
            "negative-hours" when tag.Code==73=>new DxfTag(73,(short)-1),
            "self-owner" when tag.Code==330=>new DxfTag(330,"22"),
            "missing-owner" when tag.Code==330=>new DxfTag(330,"7FFFFFFE"),_=>tag}).ToList()),binary);
    }
    private static void StoredSunStudyOpaque(string shape,bool binary)
    {
        var raw=MutateSunStudy(tags=>{
            if(shape=="version")return tags.Select(tag=>tag.Code==90?new DxfTag(90,1):tag).ToList();
            if(shape=="dates"){int index=tags.FindIndex(tag=>tag.Code==91);tags[index]=new DxfTag(91,1);tags.InsertRange(index+1,new[]{new DxfTag(90,2460291),new DxfTag(90,3600)});}
            if(shape=="subclass")tags.Add(new DxfTag(100,"PrivateSunStudy"));
            if(shape=="private")tags.AddRange(new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1001,"not an APPID"),new DxfTag(102,"}")});
            if(shape=="extra")tags.Add(new DxfTag(300,"future field"));
            if(shape=="arbitrary")tags.AddRange(new[]{new DxfTag(320,"7FFFFFF0"),new DxfTag(329,"7FFFFFF1")});
            return tags;
        });
        if(shape=="older"){var tags=raw.Tags.ToList();int index=tags.FindIndex(tag=>tag.Code==9&&(string)tag.Value=="$ACADVER");tags[index+1]=new DxfTag(1,"AC1024");raw=LoadRaw(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(SaveRaw(raw.WithTags(raw.Tags),false)).Replace("AC1032","AC1024",StringComparison.Ordinal)));}
        var document=LoadSunStudy(raw,binary)!;var study=document.GetObjectByHandle("22");Check(study is DxfOpaqueObject,"Unqualified SUNSTUDY shape became typed");
        Check(!document.TextStyles.Remove((TextStyle)document.GetObjectByHandle("C")),"Opaque SUNSTUDY text-style reference removal");
        using var output=new MemoryStream();Check(document.Save(output,binary),"Opaque SUNSTUDY save");output.Position=0;var saved=DxfRawDocument.Load(output);
        Check(OwnershipTagValues(SunStudyPacket(raw).Tags).SequenceEqual(OwnershipTagValues(SunStudyPacket(saved).Tags)),"Unknown SUNSTUDY packet lost data");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"sunstudy-stored-opaque-{shape}-{binary}.dxf"),output.ToArray());
    }
    private static DxfRawDocument SunStudyOwned()
    {
        var raw=SunStudySource();var tags=raw.Tags.ToList();var packet=SunStudyPacket(raw);
        raw=raw.WithRecord(packet,packet.Tags.Select(tag=>tag.Code==330?new DxfTag(330,"FFFF100"):tag));
        var dictionary=raw.Sections.Single(section=>section.Name=="OBJECTS").Records.Single(record=>record.Name=="DICTIONARY"&&record.Tags.Any(tag=>tag.Code==5&&(string)tag.Value=="1F"));
        var body=dictionary.Tags.ToList();int entry=body.FindIndex(tag=>tag.Code==3&&(string)tag.Value=="SUN_STUDY");body[entry+1]=new DxfTag(350,"FFFF100");raw=raw.WithRecord(dictionary,body);
        tags=raw.Tags.ToList();int end=tags.FindLastIndex(tag=>tag.Code==0&&(string)tag.Value=="ENDSEC");
        tags.InsertRange(end,new DxfTag[]{new(0,"DICTIONARY"),new(5,"FFFF100"),new(330,"1F"),new(100,"AcDbDictionary"),new(281,(short)1),new(3,"SUN_STUDY"),new(350,"22")});
        return raw.WithTags(tags);
    }
    private static void StoredSunStudyLifecycle(bool binary)
    {
        var document=LoadSunStudy(SunStudyOwned(),binary)!;var study=GetSunStudy(document);var parent=(DxfDictionary)study.Owner;
        long seed=OwnershipSeed(document);int count=document.Objects.Items.Count;
        Throws<NotSupportedException>(()=>((IList)study.Payload).Clear());Throws<NotSupportedException>(()=>((IList)study.RawHourFlags).Clear());
        Throws<NotSupportedException>(()=>document.Objects.EraseOwnedTree(study));Throws<NotSupportedException>(()=>document.Objects.EraseOwnedTree(parent));
        var destination=new DxfDocument(document.DrawingVariables.AcadVer);var root=destination.Objects.Root;long targetSeed=OwnershipSeed(destination);int targetCount=destination.Objects.Items.Count;
        Throws<NotSupportedException>(()=>destination.Objects.Clone(parent,root,"COPY"));Throws<ArgumentException>(()=>root.Add("FOREIGN",study));
        Equal(targetSeed,OwnershipSeed(destination),"Rejected destination allocation");Equal(targetCount,destination.Objects.Items.Count,"Rejected destination registrations");
        Check(!document.TextStyles.Remove((TextStyle)study.TextStyle),"Referenced text style removal");Check(!document.Views.Remove((View)study.View),"Referenced view removal");
        Throws<InvalidOperationException>(()=>document.Objects.EraseOwnedTree((DxfDatabaseObject)study.PageSetup));
        Equal(seed,OwnershipSeed(document),"Rejected source allocation");Equal(count,document.Objects.Items.Count,"Rejected source registrations");
        document.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;using var stream=new MemoryStream();bool rejected;try{rejected=!document.Save(stream,binary);}catch(Exception error)when(error is InvalidOperationException or NotSupportedException){rejected=true;}
        Check(rejected,"SUNSTUDY profile conversion accepted");Equal(0L,stream.Length,"Profile failure wrote output");Equal(seed,OwnershipSeed(document),"Profile failure allocated handles");
    }
    private static void StoredSunStudyMetadata(bool binary)
    {
        var document=LoadSunStudy(SunStudySource(),binary)!;var study=GetSunStudy(document);var extension=new DxfDictionary();document.Objects.SetExtensionDictionary(study,extension);extension.Add("NOTE",new DxfDictionaryVariable{Value="metadata"});
        study.PersistentReactors.Add(study.PageSetup);var app=document.ApplicationRegistries.Add(new ApplicationRegistry("SUNSTUDY_DATA"));var data=new XData(app);data.XDataRecord.Add(new XDataRecord(XDataCode.String,"actual XData"));study.XData.Add(data);
        using var output=new MemoryStream();Check(document.Save(output,binary),"SUNSTUDY metadata save");output.Position=0;var copy=GetSunStudy(DxfDocument.Load(output)!);
        Equal("metadata",((DxfDictionaryVariable)copy.ExtensionDictionary["NOTE"]).Value,"Extension payload");Equal(1,copy.PersistentReactors.Count,"Persistent reactor");Equal("actual XData",(string)copy.XData["SUNSTUDY_DATA"].XDataRecord[0].Value,"Actual XData");
    }
    private static void StoredSunStudyNull(bool binary)
    {
        var raw=MutateSunStudy(tags=>tags.Select(tag=>tag.Code>=340&&tag.Code<=343?new DxfTag(tag.Code,"0000"):tag).ToList());var study=GetSunStudy(LoadSunStudy(raw,binary)!);
        Equal(0,study.References.Count,"Null references excluded");Check(study.PageSetup==null&&study.View==null&&study.VisualStyle==null&&study.TextStyle==null,"Null is not document zero");
    }
}
