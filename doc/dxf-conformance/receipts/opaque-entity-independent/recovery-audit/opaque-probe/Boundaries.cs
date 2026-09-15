using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
using netDxf.Tables;

internal static partial class Program
{
    private static DxfRawDocument Modify(DxfRawDocument raw,Action<List<DxfTag>> edit)
    {var packet=Packet(raw);var tags=packet.Tags.ToList();edit(tags);return raw.WithRecord(packet,tags);}
    private static void RejectedLoad(DxfRawDocument raw,bool binary,string label)
    {byte[] bytes;using(var stream=new MemoryStream()){raw.Save(stream,binary);bytes=stream.ToArray();}bool rejected=false;try{Load(bytes);}catch(Exception){rejected=true;}Check(rejected,"Unsupported source envelope admitted: "+label);}
    private static DxfRawRecord ByHandle(DxfRawDocument raw,string handle)=>raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Tags.Any(t=>t.Code==5&&Equals(t.Value,handle)));
    private static DxfDocument MetadataFixture(bool binary)
    {
        var doc=Load(RawFixture(DxfVersion.AutoCad2018),false);var line=doc.Entities.Lines.Single();var dictionary=new DxfDictionary();dictionary.Add("INDEPENDENT_PAYLOAD",new DxfXRecord());doc.Objects.SetExtensionDictionary(line,dictionary);
        var raw=Raw(Save(doc,false));var lineRecord=ByHandle(raw,line.Handle);var lineTags=lineRecord.Tags.ToList();int start=lineTags.FindIndex(t=>t.Code==102&&Equals(t.Value,"{ACAD_XDICTIONARY"));Check(start>=0,"Authored extension fixture missing.");var metadata=lineTags.GetRange(start,3);lineTags.RemoveRange(start,3);raw=raw.WithRecord(lineRecord,lineTags);
        var owner=ByHandle(raw,dictionary.Handle);raw=raw.WithRecord(owner,owner.Tags.Select(t=>t.Code==330?new DxfTag(330,"F001"):t));
        raw=Modify(raw,tags=>{tags.InsertRange(3,metadata);tags.InsertRange(3,new[]{new DxfTag(102,"{ACAD_REACTORS"),new DxfTag(330,line.Handle),new DxfTag(102,"}")});});return Load(raw,binary);
    }
    private static void Boundaries()
    {
        foreach(bool binary in new[]{false,true})
        {
            foreach(string kind in new[]{"layer","linetype","style","appid"})
                Run("table-pointer-"+kind+"-"+binary,()=>
                {
                    var doc=Load(RawFixture(DxfVersion.AutoCad2018),false);DxfObject target=kind switch
                    {
                        "layer"=>doc.Layers.Add(new Layer("INDEPENDENT_POINTER")),
                        "linetype"=>doc.Linetypes.Add(new Linetype("INDEPENDENT_POINTER")),
                        "style"=>doc.TextStyles.Add(new TextStyle("INDEPENDENT_POINTER","txt.shx")),
                        _=>doc.ApplicationRegistries.Add(new ApplicationRegistry("INDEPENDENT_POINTER"))
                    };
                    string handle=target.Handle;var raw=Modify(Raw(Save(doc,false)),tags=>{int index=tags.FindIndex(t=>t.Code==340);tags[index]=new DxfTag(340,handle);});doc=Load(raw,binary);var entity=Opaque(doc);target=doc.GetObjectByHandle(handle);
                    Check(entity.References.Contains(target),"Qualified table pointer did not resolve exact source identity.");long seed=Seed(doc);bool removed=target switch{Layer value=>doc.Layers.Remove(value),Linetype value=>doc.Linetypes.Remove(value),TextStyle value=>doc.TextStyles.Remove(value),ApplicationRegistry value=>doc.ApplicationRegistries.Remove(value),_=>true};
                    Check(!removed&&ReferenceEquals(doc.GetObjectByHandle(handle),target)&&Seed(doc)==seed,"Qualified table pointer allowed resource removal: "+kind);Save(doc,!binary);
                });
            Run("current-common-resource-guards-"+binary,()=>
            {
                var doc=Load(RawFixture(DxfVersion.AutoCad2018),binary);var entity=Opaque(doc);entity.Layer=new Layer("CURRENT_LAYER");entity.Linetype=new Linetype("CURRENT_TYPE");var layer=entity.Layer;var linetype=entity.Linetype;var app=entity.XData["OPAQUE_TEST"].ApplicationRegistry;
                Check(!doc.Layers.Remove(layer)&&!doc.Linetypes.Remove(linetype)&&!doc.ApplicationRegistries.Remove(app),"Current common or XData APPID target was removed.");
                entity.Layer=Layer.Default;entity.Linetype=Linetype.ByLayer;Check(doc.Layers.Remove(layer)&&doc.Linetypes.Remove(linetype),"Old common resource remained guarded after actual identity replacement.");Save(doc,!binary);
            });
            foreach(string defect in new[]{"aggregate-name","proxy-name","known-casing","aggregate-subclass","public-66","public-101","duplicate-common","bad-control","wrong-owner","class-extra"})
                Run("reject-"+defect+"-"+binary,()=>
                {
                    var raw=RawFixture(DxfVersion.AutoCad2018);
                    if(defect=="class-extra")
                    {var record=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,Name)));raw=raw.WithRecord(record,record.Tags.Append(new DxfTag(301,"unretained class field")));}
                    else raw=Modify(raw,tags=>
                    {
                        int common=tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbEntity"));int body=tags.FindIndex(t=>t.Code==100&&Equals(t.Value,"AcDbQualifiedFutureCurve"));
                        if(defect=="aggregate-name")tags[0]=new DxfTag(0,"ATTRIB");if(defect=="proxy-name")tags[0]=new DxfTag(0,"ACAD_PROXY_ENTITY");if(defect=="known-casing")tags[0]=new DxfTag(0,"line");
                        if(defect=="aggregate-subclass")tags[body]=new DxfTag(100,"AcDbFaceRecord");if(defect=="public-66")tags.Insert(body,new DxfTag(66,(short)1));if(defect=="public-101")tags.Insert(body,new DxfTag(101,"Embedded Object"));
                        if(defect=="duplicate-common")tags.Insert(body,new DxfTag(100,"AcDbEntity"));if(defect=="bad-control")tags.Insert(common,new DxfTag(102,"invalid"));if(defect=="wrong-owner")tags[2]=new DxfTag(330,"DEAD");
                    });
                    RejectedLoad(raw,binary,defect);
                });
            Run("source-extension-reactor-"+binary,()=>
            {
                var doc=MetadataFixture(binary);var entity=Opaque(doc);var source=entity.SourceTags;var dictionary=entity.ExtensionDictionary;Check(dictionary!=null&&ReferenceEquals(dictionary.Owner,entity)&&entity.References.Contains(dictionary),"Actual source extension ownership missing.");
                var line=doc.Entities.Lines.Single();Check(entity.PersistentReactors.Count==1&&ReferenceEquals(entity.PersistentReactors.Single(),line),"Actual source reactor missing.");Check(!doc.Entities.Remove(line),"Source pointer/reactor target removed.");
                byte[] bytes=Save(doc,!binary);var other=Opaque(Load(bytes));Check(Same(source,other.SourceTags)&&other.ExtensionDictionary.Handle==dictionary!.Handle,"Source metadata changed on output.");File.WriteAllBytes(Path.Combine(Output,"metadata-"+binary+".dxf"),bytes);
                entity.PersistentReactors.Clear();Refuse(doc,binary,"reactor-clear-"+binary);entity.PersistentReactors.Add(line);Save(doc,binary);
            });
            Run("xdata-layer-identity-"+binary,()=>
            {
                var doc=Load(RawFixture(DxfVersion.AutoCad2018),false);doc.Layers.Add(new Layer("INDEPENDENT_LAYER"));var raw=Modify(Raw(Save(doc,false)),tags=>tags.Add(new DxfTag(1003,"INDEPENDENT_LAYER")));doc=Load(raw,binary);
                var entity=Opaque(doc);var source=entity.SourceTags;var layer=doc.Layers["INDEPENDENT_LAYER"];var refs=entity.References;Check(refs.Contains(layer),"XData1003 does not bind actual layer.");Check(!doc.Layers.Remove(layer),"XData1003 actual referenced layer was removed.");
                layer.Name="RENAMED_Ω";var bytes=Save(doc,!binary);var other=Opaque(Load(bytes));Check(other.XData["OPAQUE_TEST"].XDataRecord.Any(r=>r.Code==XDataCode.LayerName&&Equals(r.Value,"RENAMED_Ω")),"Renamed actual layer was not emitted in XData1003.");Check(ReferenceEquals(source,entity.SourceTags)&&refs.Contains(layer),"Layer rename changed original source/reference snapshot.");File.WriteAllBytes(Path.Combine(Output,"xdata-layer-"+binary+".dxf"),bytes);
            });
            Run("comments-or-nul-preflight-"+binary,()=>
            {
                var raw=Modify(RawFixture(DxfVersion.AutoCad2018),tags=>tags.Insert(3,new DxfTag(999,"retained comment")));var doc=Load(raw,false);Refuse(doc,true,"binary-comment-"+binary);
                doc=Load(RawFixture(DxfVersion.AutoCad2018),binary);Opaque(doc).XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.String,"embedded\0nul"));Refuse(doc,binary,"nul-"+binary);
            });
            Run("generated-class-preflight-"+binary,()=>
            {
                var raw=RawFixture(DxfVersion.AutoCad2018);var generated=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,"RASTERVARIABLES")));raw=raw.WithoutRecord(generated);var packet=Packet(raw);raw=raw.WithRecord(packet,packet.Tags.Select(t=>t.Code==0?new DxfTag(0,"RASTERVARIABLES"):t));var declaration=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,Name)));
                raw=raw.WithRecord(declaration,declaration.Tags.Select(t=>t.Code==1?new DxfTag(1,"RASTERVARIABLES"):t.Code==2?new DxfTag(2,"AcDbRasterVariables"):t));var doc=Load(raw,binary);Refuse(doc,binary,"generated-class-"+binary);
            });
        }
    }
}
