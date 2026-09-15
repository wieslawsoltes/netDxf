using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

namespace NetDxf.Conformance;
internal static partial class Program
{
    private static void RegisterStoredCellStyleMapTests()
    {
        foreach(string file in TableContentFiles)foreach(bool input in new[]{false,true})foreach(bool binary in new[]{false,true})
            Run($"cell-style-map/native/{file}/{input}/{binary}",()=>CellStyleMapNative(file,input,binary));
        foreach(DxfVersion version in SupportedVersions.Where(v=>v>=DxfVersion.AutoCad2004))foreach(bool binary in new[]{false,true})
        {
            Run($"cell-style-map/schema/{version}/{binary}",()=>CellStyleMapSchema(version,binary));
            Run($"cell-style-map/empty/{version}/{binary}",()=>CellStyleMapEmpty(version,binary));
        }
        foreach(bool binary in new[]{false,true})
        {
            foreach(int fault in Enumerable.Range(0,24))Run($"cell-style-map/malformed/{binary}/{fault}",()=>CellStyleMapMalformed(binary,fault));
            foreach(int variant in Enumerable.Range(0,8))Run($"cell-style-map/opaque/{binary}/{variant}",()=>CellStyleMapOpaque(binary,variant));
            foreach(int op in Enumerable.Range(0,10))Run($"cell-style-map/lifecycle/{binary}/{op}",()=>CellStyleMapLifecycle(binary,op));
            foreach(string name in new[]{"", "Custom name", "_DATA", "CELLSTYLE_BEGIN", "CELLSTYLE_END", "TABLEFORMAT_BEGIN", "Zażółć 日本語"})
                Run($"cell-style-map/custom-name/{binary}/{name}",()=>CellStyleMapName(binary,name));
            foreach(string value in new[]{"TABLEFORMAT_BEGIN", "TABLEFORMAT_END", "CELLSTYLE_BEGIN", "CELLSTYLE_END", "CONTENTFORMAT_END"})
                Run($"cell-style-map/format-literal/{binary}/{value}",()=>CellStyleMapFormatLiteral(binary,value));
            foreach(string kind in new[]{"STYLE","LTYPE","APPID","ENTITY","BLOCK_RECORD","BLOCK_MEMBER"})
                Run($"cell-style-map/reference/{binary}/{kind}",()=>CellStyleMapReference(binary,kind));
        }
    }
    private static List<DxfTag> CellStyleMapPayload() => new() {
        new(100,"AcDbCellStyleMap"),new(90,2),
        new(300,"CELLSTYLE"),new(1,"TABLEFORMAT_BEGIN"),new(90,5),new(170,(short)0),new(309,"TABLEFORMAT_END"),
        new(1,"CELLSTYLE_BEGIN"),new(90,-7),new(91,int.MinValue),new(300,"Custom"),new(309,"CELLSTYLE_END"),
        new(300,"CELLSTYLE"),new(1,"TABLEFORMAT_BEGIN"),new(90,5),new(170,(short)0),new(309,"TABLEFORMAT_END"),
        new(1,"CELLSTYLE_BEGIN"),new(90,-7),new(91,int.MaxValue),new(300,"Custom"),new(309,"CELLSTYLE_END") };
    private static DxfRawDocument CellStyleMapRaw(DxfVersion version,Action<DxfDocument,List<DxfTag>>? setup=null)
    {
        var doc=new DxfDocument(version);var placeholder=new DxfXRecord();doc.Objects.Root.Add("MAP",placeholder);
        var body=CellStyleMapPayload();setup?.Invoke(doc,body);var raw=TableContentRaw(TableContentSave(doc,false));
        var record=raw.Sections.Single(s=>s.Name=="OBJECTS").Records.Single(r=>r.Tags.Any(t=>t.Code==5&&(string)t.Value==placeholder.Handle));
        int start=record.Tags.ToList().FindIndex(t=>t.Code==100);
        return raw.WithRecord(record,record.Tags.Take(start).Select(t=>t.Code==0?new DxfTag(0,"CELLSTYLEMAP"):t).Concat(body));
    }
    private static DxfDocument CellStyleMapLoad(DxfRawDocument raw,bool binary)=>TableContentLoad(TableContentRawBytes(raw,binary));
    private static DxfStoredCellStyleMap CellStyleMapObject(DxfDocument doc)=>doc.Objects.Items.OfType<DxfStoredCellStyleMap>().Single();
    private static void CellStyleMapNative(string file,bool input,bool binary)
    {
        byte[] bytes=TableContentSourceBytes(file);if(input)bytes=TableContentRawBytes(TableContentRaw(bytes),true);
        var doc=TableContentLoad(bytes);var map=CellStyleMapObject(doc);Equal(3,map.Entries.Count,"native entry count");
        Check(map.Entries.Select(e=>e.Id).SequenceEqual(new[]{1,2,3}),"native stored entry ids");
        Check(map.Entries.Select(e=>e.Name).SequenceEqual(new[]{"_TITLE","_HEADER","_DATA"}),"native stored names/order");
        var owner=(DxfDictionary)map.Owner;var style=(DxfTableStyle)owner.Owner;
        Check(ReferenceEquals(style.ExtensionDictionary,owner)&&ReferenceEquals(owner["ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP"],map),"actual native owner hierarchy");
        Check(ReferenceEquals(style.CellStyleMap,map)&&ReferenceEquals(style.StoredCellStyleMap,map),"typed TABLESTYLE API identity");
        foreach(var target in map.References)Check(ReferenceEquals(doc.GetObjectByHandle(target.Handle),target),"native source reference identity");
        Equal(0,doc.Objects.Validate().Count,"native map validation");var before=OwnershipTagValues(map.Payload).ToArray();
        var reloaded=TableContentLoad(TableContentSave(doc,binary,$"cell-style-map-native-{file}-{binary}.dxf"));var after=CellStyleMapObject(reloaded);
        Check(before.SequenceEqual(OwnershipTagValues(after.Payload)),"native map payload changed");
        Check(ReferenceEquals(((DxfTableStyle)after.Owner.Owner).StoredCellStyleMap,after),"reloaded TABLESTYLE typed map identity");
    }
    private static void CellStyleMapSchema(DxfVersion version,bool binary)
    {
        var doc=CellStyleMapLoad(CellStyleMapRaw(version),binary);var map=CellStyleMapObject(doc);
        Equal(version,map.SourceVersion,"source map profile");Equal(2,map.Entries.Count,"stored entry count");
        Equal(-7,map.Entries[0].Id,"negative id retained");Equal(-7,map.Entries[1].Id,"duplicate id retained");
        Equal(int.MinValue,map.Entries[0].StoredType,"raw negative type");Equal(int.MaxValue,map.Entries[1].StoredType,"raw positive type");
        Check(map.Entries.All(e=>e.Name=="Custom"),"duplicate custom names retained");
        var before=OwnershipTagValues(map.Payload).ToArray();var after=CellStyleMapObject(TableContentLoad(TableContentSave(doc,binary,$"cell-style-map-schema-{version}-{binary}.dxf")));
        Check(before.SequenceEqual(OwnershipTagValues(after.Payload)),"synthetic map packet changed");
    }
    private static void CellStyleMapEmpty(DxfVersion version,bool binary)
    {
        var raw=CellStyleMapRaw(version,(_,tags)=>{tags.RemoveRange(2,tags.Count-2);tags[1]=new DxfTag(90,0);});
        Equal(0,CellStyleMapObject(CellStyleMapLoad(raw,binary)).Entries.Count,"empty stored map");
    }
    private static void CellStyleMapMalformed(bool binary,int fault)
    {
        var raw=CellStyleMapRaw(DxfVersion.AutoCad2018,(_,tags)=>
        {
            if(fault==0)tags[1]=new DxfTag(90,-1);
            else if(fault==1)tags[1]=new DxfTag(90,int.MaxValue);
            else if(fault==2)tags[1]=new DxfTag(90,0);
            else if(fault==3)tags[1]=new DxfTag(90,3);
            else if(fault<14)tags.RemoveAt(new[]{1,2,3,6,7,8,9,10,11,21}[fault-4]);
            else if(fault==14)tags.Insert(0,new DxfTag(100,"AcDbCellStyleMap"));
            else if(fault==15)tags.Add(new DxfTag(90,99));
            else if(fault==16)tags[6]=new DxfTag(309,"CONTENTFORMAT_END");
            else if(fault==17)tags[11]=new DxfTag(309,"TABLEFORMAT_END");
            else if(fault==18)tags.Insert(5,new DxfTag(1000,"stray public xdata"));
            else if(fault==19)tags.Insert(5,new DxfTag(340,"FFFFFFFFFFFFFFFE"));
            else if(fault==20)tags.Insert(5,new DxfTag(100,"AcDbCellStyleMap"));
            else if(fault==21)tags.InsertRange(5,Enumerable.Repeat(new DxfTag(1,"GRIDFORMAT_BEGIN"),64));
            else if(fault==22)tags[8]=new DxfTag(91,7);
            else tags[9]=new DxfTag(90,7);
        });
        bool rejected=false;try{using var input=new MemoryStream(TableContentRawBytes(raw,binary));rejected=DxfDocument.Load(input)==null;}catch(FormatException){rejected=true;}
        Check(rejected,"malformed known CELLSTYLEMAP accepted or made opaque");
    }
    private static void CellStyleMapOpaque(bool binary,int variant)
    {
        var raw=CellStyleMapRaw(variant==0?DxfVersion.AutoCad2000:DxfVersion.AutoCad2018,(_,tags)=>
        {
            if(variant==1)tags[0]=new DxfTag(100,"PrivateCellStyleMap");
            if(variant==2)tags.Add(new DxfTag(100,"PrivateMapExtension"));
            if(variant==3)tags[3]=new DxfTag(1,"PRIVATEFORMAT_BEGIN");
            if(variant==4)tags.InsertRange(5,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"private value"),new DxfTag(102,"}")});
            if(variant==5)tags.AddRange(new[]{new DxfTag(100,"PrivateMapExtension"),new DxfTag(1000,"private value")});
            if(variant==6)tags.InsertRange(0,new[]{new DxfTag(102,"{PRIVATE"),new DxfTag(1000,"private header"),new DxfTag(102,"}")});
            if(variant==7)tags[6]=new DxfTag(309,"PRIVATEFORMAT_END");
        });
        var doc=CellStyleMapLoad(raw,binary);var map=doc.Objects.Items.OfType<DxfOpaqueObject>().Single(o=>o.CodeName=="CELLSTYLEMAP");var before=OwnershipTagValues(map.Tags).ToArray();
        var after=TableContentLoad(TableContentSave(doc,binary,$"cell-style-map-opaque-{variant}-{binary}.dxf")).Objects.Items.OfType<DxfOpaqueObject>().Single(o=>o.CodeName=="CELLSTYLEMAP");
        Check(before.SequenceEqual(OwnershipTagValues(after.Tags)),"opaque map packet changed");
    }
    private static void CellStyleMapLifecycle(bool binary,int operation)
    {
        var doc=CellStyleMapLoad(CellStyleMapRaw(DxfVersion.AutoCad2018),binary);var map=CellStyleMapObject(doc);int count=doc.Objects.Items.Count();
        if(operation==0)Throws<NotSupportedException>(()=>doc.Objects.CloneObject(map,doc.Objects.Root,"COPY"));
        else if(operation==1)Throws<NotSupportedException>(()=>doc.Objects.EraseOwnedTree(map));
        else if(operation==2)Throws<NotSupportedException>(()=>((IList<DxfTag>)map.Payload).Clear());
        else if(operation==3)Throws<NotSupportedException>(()=>((IList<DxfStoredCellStyleMapEntry>)map.Entries).Clear());
        else if(operation==4)Throws<NotSupportedException>(()=>((IList<DxfTag>)map.Entries[0].FormatPayload).Clear());
        else if(operation==5)
        {
            doc.DrawingVariables.AcadVer=DxfVersion.AutoCad2013;using var output=new MemoryStream();bool rejected=false;
            try{rejected=!doc.Save(output,binary);}catch(NotSupportedException){rejected=true;}catch(InvalidOperationException){rejected=true;}
            Check(rejected,"map source profile changed");Equal(0L,output.Length,"map profile rejection output");
        }
        else if(operation==6){doc.Classes.Remove("CELLSTYLEMAP");var loaded=TableContentLoad(TableContentSave(doc,binary));Equal(1152,loaded.Classes["CELLSTYLEMAP"].ProxyFlags,"map CLASS synthesis");}
        else if(operation==7){doc.Classes.Remove("CELLSTYLEMAP");doc.Classes.Add(new DxfClass("CELLSTYLEMAP","PrivateClass","Private"));using var output=new MemoryStream();CheckSaveRejected(doc,output);Equal(0L,output.Length,"map CLASS rejection output");}
        else if(operation==8)Check(typeof(DxfStoredCellStyleMap).GetConstructors().Length==0,"map authoring constructor exposed");
        else
        {
            var native=TableContentLoad(TableContentSourceBytes("acad_table_simple.dxf"));var child=CellStyleMapObject(native);
            Throws<NotSupportedException>(()=>native.Objects.EraseOwnedTree((DxfDatabaseObject)child.Owner));
            Throws<NotSupportedException>(()=>native.Objects.CloneObject((DxfDatabaseObject)child.Owner,native.Objects.Root,"COPY"));
        }
        Equal(count,doc.Objects.Items.Count(),"map lifecycle changed membership");
    }
    private static void CellStyleMapName(bool binary,string name)
    {
        var raw=CellStyleMapRaw(DxfVersion.AutoCad2018,(_,tags)=>{tags[10]=new DxfTag(300,name);tags[20]=new DxfTag(300,name);});
        var doc=CellStyleMapLoad(raw,binary);var map=CellStyleMapObject(doc);Check(map.Entries.All(e=>e.Name==name),"custom map names or order misinterpreted");
        var loaded=CellStyleMapObject(TableContentLoad(TableContentSave(doc,binary)));Check(loaded.Entries.All(e=>e.Name==name),"stored name changed");
    }
    private static void CellStyleMapFormatLiteral(bool binary,string value)
    {
        var raw=CellStyleMapRaw(DxfVersion.AutoCad2018,(_,tags)=>tags.Insert(5,new DxfTag(300,value)));
        var doc=CellStyleMapLoad(raw,binary);var map=CellStyleMapObject(doc);Check(map.Entries[0].FormatPayload.Any(t=>t.Code==300&&(string)t.Value==value),"format literal interpreted as marker");
        var before=OwnershipTagValues(map.Payload).ToArray();var loaded=CellStyleMapObject(TableContentLoad(TableContentSave(doc,binary)));Check(before.SequenceEqual(OwnershipTagValues(loaded.Payload)),"format literal packet changed");
    }
    private static void CellStyleMapReference(bool binary,string kind)
    {
        string handle="";var raw=CellStyleMapRaw(DxfVersion.AutoCad2018,(doc,tags)=>
        {
            DxfObject target;
            if(kind=="STYLE")target=doc.TextStyles.Add(new TextStyle("MAP_STYLE","Arial.ttf"));
            else if(kind=="LTYPE")target=doc.Linetypes.Add(new Linetype("MAP_LTYPE"));
            else if(kind=="APPID")target=doc.ApplicationRegistries.Add(new ApplicationRegistry("MAP_APP"));
            else if(kind=="ENTITY"){var line=new Line(Vector3.Zero,Vector3.UnitX);doc.Entities.Add(line);target=line;}
            else{var block=new Block("MAP_BLOCK");var line=new Line(Vector3.Zero,Vector3.UnitX);block.Entities.Add(line);doc.Blocks.Add(block);target=kind=="BLOCK_RECORD"?block.Record:line;}
            handle=target.Handle;tags.Insert(5,new DxfTag(340,handle));
        });
        var doc=CellStyleMapLoad(raw,binary);var map=CellStyleMapObject(doc);var target=doc.GetObjectByHandle(handle);Check(ReferenceEquals(map.References.Single(),target),"exact map source identity");
        bool removed=kind switch{"STYLE"=>doc.TextStyles.Remove("MAP_STYLE"),"LTYPE"=>doc.Linetypes.Remove("MAP_LTYPE"),"APPID"=>doc.ApplicationRegistries.Remove("MAP_APP"),"ENTITY"=>doc.Entities.Remove((EntityObject)target),_=>doc.Blocks.Remove("MAP_BLOCK")};
        Check(!removed,"map dependency removed");Equal(0,doc.Objects.Validate().Count,"map reference guard changed graph");
    }
}
