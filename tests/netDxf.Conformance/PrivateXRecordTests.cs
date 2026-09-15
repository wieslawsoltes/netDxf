using System.Collections;
using netDxf;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterPrivateXRecordTests()
    {
        foreach (var version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"private-xrecord/graph/{version}/{binary}", () => PrivateXRecordGraph(version, binary));
        foreach (bool binary in new[] { false, true })
        {
            Run($"private-xrecord/native/{binary}", () => PrivateXRecordNative(binary));
            foreach (string variant in new[] { "valid-xdata", "nested-appid", "private-subclass", "malformed-xdata", "missing-subclass" })
                Run($"private-xrecord/boundary/{variant}/{binary}", () => PrivateXRecordBoundary(variant, binary));
        }
        Run("private-xrecord/authored-boundary", () => {
            var record = new DxfXRecord();
            foreach (var tag in new DxfTag[] { new(1000,"private"),new(1001,"APP"),new(1004,new byte[]{1,2}),new(1005,"0"),new(1070,(short)1),new(1071,1) })
                Throws<ArgumentException>(() => record.Data.Add(tag));
            Equal(0, record.Data.Count, "rejected authored data changed the record");
        });
    }
    private static DxfRawDocument PrivateXRecordFixture(DxfVersion version, string variant)
    {
        var doc = new DxfDocument(version); var parent = new DxfDictionary(); var record = new DxfXRecord();
        doc.Objects.Root.Add("PRIVATE_OWNER",parent); parent.Add("PRIVATE_RECORD",record);
        var reactor = new DxfPlaceholder(); doc.Objects.Root.Add("PRIVATE_REACTOR",reactor); record.PersistentReactors.Add(reactor);
        var extension = new DxfDictionary(); doc.Objects.SetExtensionDictionary(record,extension); extension.Add("NOTE",new DxfDictionaryVariable { Value="retained extension" });
        var app = doc.ApplicationRegistries.Add(new ApplicationRegistry("PRIVATE_XDATA")); var xdata = new XData(app);
        xdata.XDataRecord.Add(new XDataRecord(XDataCode.String,"actual XData")); xdata.XDataRecord.Add(new XDataRecord(XDataCode.DatabaseHandle,reactor.Handle)); record.XData.Add(xdata);
        using var bytes = new MemoryStream(); Check(doc.Save(bytes),"private XRECORD fixture save"); bytes.Position=0; var raw=DxfRawDocument.Load(bytes);
        var packet=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==record.Handle));
        int start=packet.Tags.ToList().FindIndex(t=>t.Code==100),end=packet.Tags.ToList().FindIndex(t=>t.Code==1001);
        var body=new List<DxfTag>{new(100,"AcDbXrecord"),new(280,(short)1),new(1070,(short)0),new(1070,(short)1),new(1070,(short)1)};
        if(variant=="valid-xdata")body=new(){new(100,"AcDbXrecord"),new(280,(short)1),new(1,"ordinary data"),new(369,"0")};
        if(variant=="nested-appid")body=new(){new(100,"AcDbXrecord"),new(280,(short)1),new(102,"{PRIVATE"),new(102,"{NESTED"),new(1001,"not a registry"),new(1000,"nested private"),new(102,"}"),new(102,"}"),new(1,"after private group")};
        if(variant=="private-subclass")body.Insert(2,new DxfTag(100,"PrivateData"));
        if(variant=="missing-subclass")body.RemoveAt(0);
        var tail=packet.Tags.Skip(end).ToList(); if(variant=="malformed-xdata")tail.Add(new DxfTag(1,"not XData"));
        return raw.WithRecord(packet,packet.Tags.Take(start).Concat(body).Concat(tail));
    }
    private static DxfDocument? PrivateXRecordLoad(DxfRawDocument raw,bool binary)
    {using var bytes=new MemoryStream();raw.WithTags(raw.Tags.Where(t=>t.Code!=999)).Save(bytes,binary);bytes.Position=0;return DxfDocument.Load(bytes);}
    private static void PrivateXRecordGraph(DxfVersion version,bool binary)
    {
        var raw=PrivateXRecordFixture(version,"direct");var doc=PrivateXRecordLoad(raw,binary)!;
        var parent=(DxfDictionary)doc.Objects.Root["PRIVATE_OWNER"]; var value=parent["PRIVATE_RECORD"];
        Check(value is DxfOpaqueObject,"private native-shaped XRECORD acquired an authored projection");var record=(DxfOpaqueObject)value;
        Equal("XRECORD",record.CodeName,"private record class");Check(ReferenceEquals(record,doc.GetObjectByHandle(record.Handle)),"private source identity");Check(ReferenceEquals(parent,record.Owner),"private owning dictionary");
        Equal(1,record.PersistentReactors.Count,"persistent reactors");Equal("retained extension",((DxfDictionaryVariable)record.ExtensionDictionary["NOTE"]).Value,"private extension");
        Equal("actual XData",(string)record.XData["PRIVATE_XDATA"].XDataRecord[0].Value,"real XData boundary");
        Equal(0,doc.Objects.Validate().Count,"private graph validation");long seed=OwnershipSeed(doc);int count=doc.Objects.Items.Count;
        Throws<NotSupportedException>(()=>((IList)record.Tags).Clear());Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(record));Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(parent));
        var target=new DxfDocument(version);int before=target.Objects.Items.Count;long allocation=OwnershipSeed(target);
        Throws<NotSupportedException>(()=>target.Objects.Clone(parent,target.Objects.Root,"COPY"));Throws<ArgumentException>(()=>target.Objects.Root.Add("FOREIGN",record));
        Equal(before,target.Objects.Items.Count,"private clone registration");Equal(allocation,OwnershipSeed(target),"private clone allocation");Equal(seed,OwnershipSeed(doc),"private source allocation");Equal(count,doc.Objects.Items.Count,"private source objects");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"private graph save");File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"private-xrecord-{version}-{binary}.dxf"),output.ToArray());output.Position=0;var saved=DxfRawDocument.Load(output);
        var after=CompositeTableRecord(saved,record.Handle);var beforePacket=CompositeTableRecord(raw,record.Handle);
        Check(OwnershipTagValues(beforePacket.Tags).SequenceEqual(OwnershipTagValues(after.Tags)),"complete private packet/common metadata differs");output.Position=0;Check(DxfDocument.Load(output)!.GetObjectByHandle(record.Handle) is DxfOpaqueObject,"private reload classification");
    }
    private static void PrivateXRecordBoundary(string variant,bool binary)
    {
        var raw=PrivateXRecordFixture(DxfVersion.AutoCad2018,variant);
        if(variant is "malformed-xdata" or "missing-subclass")
        {bool rejected;try{rejected=PrivateXRecordLoad(raw,binary)==null;}catch(FormatException){rejected=true;}Check(rejected,"invalid XRECORD boundary admitted");return;}
        var doc=PrivateXRecordLoad(raw,binary)!;var parent=(DxfDictionary)doc.Objects.Root["PRIVATE_OWNER"];var record=parent["PRIVATE_RECORD"];
        Check(variant=="valid-xdata"?record is DxfXRecord:record is DxfOpaqueObject,"public/private boundary classification");Equal(1,record.XData.Count,"private APPID-looking text was projected as real XData");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"private boundary save");output.Position=0;var saved=DxfRawDocument.Load(output);
        Check(OwnershipTagValues(CompositeTableRecord(raw,record.Handle).Tags).SequenceEqual(OwnershipTagValues(CompositeTableRecord(saved,record.Handle).Tags)),"private boundary packet changed");
    }
    private static void PrivateXRecordNative(bool binary)
    {
        using var bytes=new MemoryStream(TableContentSourceBytes("sample_AC1018_ascii.dxf"));var raw=DxfRawDocument.Load(bytes);var doc=PrivateXRecordLoad(raw,binary)!;
        var record=doc.GetObjectByHandle("145A");Check(record is DxfOpaqueObject,"native145A must remain opaque");Equal("1459",record.Owner.Handle,"native145A owner");Equal(0,doc.Objects.Validate().Count,"complete native carrier validation");
        using var output=new MemoryStream();Check(doc.Save(output,binary),"native145A save");File.WriteAllBytes(Path.Combine(ArtifactDirectory,$"private-xrecord-native-{binary}.dxf"),output.ToArray());output.Position=0;var saved=DxfRawDocument.Load(output);
        Check(OwnershipTagValues(CompositeTableRecord(CompositeTableSource(),"145A").Tags).SequenceEqual(OwnershipTagValues(CompositeTableRecord(saved,"145A").Tags)),"native145A complete source packet differs");
    }
}
