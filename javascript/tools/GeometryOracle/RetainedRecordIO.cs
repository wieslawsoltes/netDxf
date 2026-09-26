// Observation-only controller for unchanged retained-record and section codecs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Objects;
internal static partial class Program {
    private static readonly string[] RRGeometryFields={"SectionType","GeometryValue","Flags","ColorCode","ColorIndex","LayerName","LinetypeName","LinetypeScale","PlotStyleName","Lineweight","FaceTransparency","EdgeTransparency","HatchPatternType","HatchPatternName","HatchAngle","HatchScale","HatchSpacing"};
    private static object RRGeometry(object value)=>RRGeometryFields.ToDictionary(k=>k,k=>Wire(TSGet(value,k)));
    private static object? RRSnapshot(DxfDatabaseObject? value) {
        if(value==null)return null;object? details=null;
        if(value is DxfSectionSettings settings)details=new{sectionType=settings.SectionType,types=settings.TypeSettings.Select(t=>new{type=t.SectionType,options=t.GenerationOptions,sources=t.SourceObjects.Select(PayloadRef).ToArray(),destination=PayloadRef(t.DestinationBlock),file=Wire(t.DestinationFileName),repeat=t.RepeatGeometryMarkers,geometry=t.GeometrySettings.Select(RRGeometry).ToArray()}).ToArray()};
        else if(value is DxfStoredSectionManager manager)details=ManagerSnapshot(manager);
        else if(value is DxfStoredField || value is DxfStoredDimAssoc || value is DxfStoredSunStudy)details=DependencySnapshot(value);
        else if(value is DxfTableStyle || value is DxfStoredCellStyleMap)details=TableStyleSnapshot(value);
        else if(value is DxfStoredTableGeometry)details=TableGeometrySnapshot(value);
        else if(value is DxfStoredTableContent)details=TableContentSnapshot(value);
        return new{common=Wire(value),details};
    }
    private delegate bool RRHeaderParser(List<DxfTag> tags,int start,int end,out string? evaluator,out string? code,out List<string> children,out List<string> objects);
    private static object RetainedRecordIORequest(JsonElement input) {
        string kind=input.GetProperty("kind").GetString()!,mode=input.GetProperty("mode").GetString()!;
        int version=input.TryGetProperty("version",out var v)?v.GetInt32():18;
        var document=new DxfDocument(DxfVersion.AutoCad2018);var assembly=typeof(DxfDocument).Assembly;
        var readerType=assembly.GetType("netDxf.IO.DxfReader")!;var context=Activator.CreateInstance(readerType,true)!;
        TSSet(context,"doc",document);TSSet(context,"decodedStrings",new Dictionary<string,string>());
        var line=new Line();var dimension=new AlignedDimension();var section=new Section();foreach(var item in new EntityObject[]{line,dimension,section})document.Entities.Add(item);
        var root=document.NamedObjects;var leaf=new DxfPlaceholder();root.Add("leaf",leaf);var extension=new DxfDictionary();document.Objects.SetExtensionDictionary(dimension,extension);
        var resources=new Dictionary<string,DxfObject>{{"document",document},{"line",line},{"dimension",dimension},{"section",section},{"root",root},{"leaf",leaf},{"extension",extension},{"block",line.Owner.Record},{"style",document.TextStyles["Standard"]}};
        var identities=(HashSet<ulong>)TSGet(context,"sourceObjectIdentities")!;
        var accepted=(IDictionary)TSGet(context,"acceptedSourceObjects")!;var sources=(IDictionary)TSGet(context,"acceptedSourceRecords")!;
        var added=(IDictionary<string,DxfObject>)TSGet(document,"AddedObjects")!;
        void Source(DxfObject item){var source=Activator.CreateInstance(readerType.GetNestedType("SourceRecordIdentity",BindingFlags.NonPublic)!,true)!;ulong key=Convert.ToUInt64(item.Handle,16);TSSet(source,"Handle",key);TSSet(source,"IdentitySeen",true);identities.Add(key);TSCall(context,"RecordSourceObject",item,source);}
        foreach(var item in added.Values)if(item.Handle!="0")Source(item);
        document.DrawingVariables.AcadVer=(DxfVersion)version;
        object record=Activator.CreateInstance(readerType.GetNestedType("DatabaseRecord",BindingFlags.NonPublic)!,true)!;object? header=null;
        DxfObject? Target(string name)=>name=="item"?(DxfObject?)TSGet(record,"Object"):resources.TryGetValue(name,out var item)?item:null;
        object? Value(JsonElement value){if(value.ValueKind==JsonValueKind.Object){
            if(value.TryGetProperty("h",out var h)){string handle=Target(h.GetString()!)!.Handle;if(value.TryGetProperty("lower",out var low)&&low.GetBoolean())handle=handle.ToLowerInvariant();return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+handle;}
            if(value.TryGetProperty("ref",out var rr))return Target(rr.GetString()!);
            if(value.TryGetProperty("vector",out var vector))return new Vector3((double)Value(vector[0])!,(double)Value(vector[1])!,(double)Value(vector[2])!);
        }return TSInput(value);}
        var tags=input.GetProperty("tags").EnumerateArray().Select(p=>new DxfTag(p[0].GetInt16(),Value(p[1])!)).ToList();
        bool binary=mode!="text";using var bytes=new MemoryStream();using var text=new EntityBodyTextOutput();text.NewLine="\n";
        var chunkType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        object chunk=binary?Activator.CreateInstance(chunkType,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),mode=="legacy"})!:Activator.CreateInstance(chunkType,text)!;
        var writer=Activator.CreateInstance(assembly.GetType("netDxf.IO.DxfWriter")!,true)!;TSSet(writer,"doc",document);TSSet(writer,"chunk",chunk);TSSet(writer,"encodedStrings",new Dictionary<string,string>());
        IEnumerable<object> List(object value)=>((IEnumerable)value).Cast<object>();
        var names=new[]{"storedFields","storedDimAssocs","storedSunStudies","storedTableContents","storedTableGeometries","storedCellStyleMaps","tableStyles","storedSectionManagers"};
        var resolvers=new Dictionary<string,string>{{"StoredField","ResolveStoredFields"},{"StoredDimAssoc","ResolveStoredDimAssocReferences"},{"StoredSunStudy","ResolveStoredSunStudyReferences"},{"StoredTableContent","ResolveStoredTableContentReferences"},{"StoredTableGeometry","ResolveStoredTableGeometryReferences"},{"StoredCellStyleMap","ResolveStoredCellStyleMapReferences"},{"TableStyle","ResolveTableStyleReferences"},{"SectionSettings","ResolveSectionSettingsReferences"},{"SectionManager","ResolveSectionManagerReferences"}};
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            object? result=null,error=null;string method=step.GetProperty("method").GetString()!;
            try {
                switch(method){
                    case "read": {
                        if(kind=="StoredEnvelope")result=TSCall(context,"ReadStoredEnvelopePayload",record,input.GetProperty("type").GetString(),tags,step.TryGetProperty("start",out var st)?st.GetInt32():0);
                        else if(kind=="PrivateXRecord") {object?[] args={tags,null};try{result=readerType.GetMethod("TryReadPrivateXRecord",TSFlags)!.Invoke(context,args);}finally{if(args[1]!=null)record=args[1]!;}}
                        else if(kind=="StoredField"||kind=="SectionSettings"||kind=="SectionManager")record=TSCall(context,"Read"+kind+"Record",input.GetProperty("type").GetString(),tags)!;
                        else record=TSCall(context,"Read"+kind+"Record",tags)!;
                        break;
                    }
                    case "header":{
                        var parser=(RRHeaderParser)readerType.GetMethod("TryReadStoredFieldHeader",TSFlags)!.CreateDelegate(typeof(RRHeaderParser),context);
                        string? evaluator=null,code=null;var children=new List<string>();var objects=new List<string>();
                        try{result=parser(tags,step.TryGetProperty("start",out var start)?start.GetInt32():0,step.TryGetProperty("end",out var end)?end.GetInt32():tags.Count,out evaluator,out code,out children,out objects);}
                        finally{header=new{evaluator,code,children,objects};}break;
                    }
                    case "shape":result=readerType.GetMethod("IsStoredSunStudyShape",TSFlags)!.Invoke(null,new object[]{tags});break;
                    case "register":{
                        var item=(DxfDatabaseObject)Target("item")!;var owner=Target(step.TryGetProperty("owner",out var own)?own.GetString()!:"root");TSSet(item,"Owner",owner);
                        TSCall(document.Objects,"Register",item,item.Handle!=null);if(owner is DxfDictionary parent)TSCall(parent,"AddLoaded",step.TryGetProperty("name",out var name)?name.GetString():"PAYLOAD",item,!step.TryGetProperty("hard",out var hard)||hard.GetBoolean());Source(item);break;
                    }
                    case "resolve":if(resolvers.TryGetValue(kind,out var member))TSCall(context,member);break;
                    case "source":{
                        var item=Target(step.GetProperty("target").GetString()!)!;ulong key=Convert.ToUInt64(item.Handle,16);
                        switch(step.GetProperty("action").GetString()) {
                            case "ambiguous":TSSet(sources[key]!,"Ambiguous",true);break;
                            case "unaccept":accepted.Remove(key);break;case "discard":identities.Remove(key);break;
                            case "replace":added[item.Handle]=new DxfPlaceholder();break;case "unregister":added.Remove(item.Handle);break;
                        }break;
                    }
                    case "set":{var name=step.TryGetProperty("target",out var tar)?tar.GetString()!:"item";object item=name=="variables"?document.DrawingVariables:Target(name)!;TSSet(item,step.GetProperty("property").GetString()!,Value(step.GetProperty("value")));break;}
                    case "reactor":Target(step.TryGetProperty("target",out var react)?react.GetString()!:"item")!.PersistentReactors.Add(Target(step.GetProperty("value").GetString()!)!);break;
                    case "alias":{var owner=(DxfDictionary)Target(step.TryGetProperty("owner",out var al)?al.GetString()!:"root")!;owner.Remove(step.TryGetProperty("remove",out var rm)?rm.GetString()!:"PAYLOAD");if(step.GetProperty("name").ValueKind!=JsonValueKind.Null)TSCall(owner,"AddLoaded",step.GetProperty("name").GetString(),Target("item"),!step.TryGetProperty("hard",out var ah)||ah.GetBoolean());break;}
                    case "opaque":{var item=(DxfOpaqueObject)Activator.CreateInstance(typeof(DxfOpaqueObject),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{input.TryGetProperty("type",out var ot)?ot.GetString()!:step.GetProperty("code").GetString()!,new List<DxfTag>()},CultureInfo.InvariantCulture)!;TSSet(item,"Owner",root);TSCall(document.Objects,"Register",item,false);TSCall(root,"AddLoaded","Opaque",item,true);Source(item);resources["opaque"]=item;break;}
                    case "class":{var definition=new DxfClass(step.GetProperty("name").GetString()!,step.GetProperty("cpp").GetString()!,step.TryGetProperty("app",out var app)?app.GetString()!:"Probe");definition.IsEntity=step.TryGetProperty("entity",out var ent)&&ent.GetBoolean();definition.InstanceCount=step.TryGetProperty("count",out var ct)?ct.GetInt32():9;document.Classes.Add(definition);break;}
                    case "prepare":TSCall(writer,"Prepare"+kind+(kind=="StoredEnvelope"||kind=="SectionManager"?"Classes":"Class"),document.Classes);break;
                    case "validate":result=document.Objects.Validate().ToArray();break;
                    case "write":{
                        text.Calls=0;text.Hook=count=>{if(step.TryGetProperty("hooks",out var hooks))foreach(var hook in hooks.EnumerateArray())if(hook.GetProperty("at").GetInt32()==count){
                            string mutation=hook.GetProperty("kind").GetString()!;if(mutation=="throw")throw new InvalidOperationException("Injected retained record writer callback.");
                            string name=hook.TryGetProperty("target",out var ht)?ht.GetString()!:"item";object item=name=="variables"?document.DrawingVariables:Target(name)!;
                            string property=hook.GetProperty("property").GetString()!;if(mutation=="clear")TSCall(TSGet(item,property)!,"Clear");else TSSet(item,property,Value(hook.GetProperty("value")));
                        }};
                        try{result=TSCall(writer,"Write"+kind+"Payload",Target(step.TryGetProperty("target",out var wt)?wt.GetString()!:"item"));}finally{text.Hook=null;}break;
                    }
                    case "snapshot":break;
                    default:throw new InvalidOperationException("Unknown retained observation "+method);
                }
            }catch(Exception e){error=PayloadError(e);}
            TSCall(chunk,"Flush");var metadata=TSGet(record,"Metadata")!;
            var pending=new Dictionary<string,object?>();foreach(var name in names)pending[name]=List(TSGet(context,name)!).Select(p=>name=="storedFields"?(object)new{item=PayloadRef((DxfObject?)TSGet(p,"Item1")),children=List(TSGet(p,"Item2")!).ToArray(),objects=List(TSGet(p,"Item3")!).ToArray()}:PayloadRef((DxfObject)p)).ToArray();
            pending["sections"]=List(TSGet(context,"pendingSectionSettings")!).Select(p=>new{item=PayloadRef((DxfObject?)TSGet(p,"Key")),types=List(TSGet(p,"Value")!).Select(t=>new{type=(int)TSGet(t,"Type")!,options=(int)TSGet(t,"Options")!,destination=TSGet(t,"Destination"),file=Wire(TSGet(t,"File")),repeat=(bool)TSGet(t,"RepeatMarkers")!,sources=List(TSGet(t,"Sources")!).ToArray(),geometry=List(TSGet(t,"Geometry")!).Select(RRGeometry).ToArray()}).ToArray()}).ToArray();
            results.Add(new{ok=true,value=new{result,error,record=new{model=RRSnapshot((DxfDatabaseObject?)Target("item")),owner=TSGet(metadata,"Owner"),extension=TSGet(metadata,"Extension"),reactors=List(TSGet(metadata,"Reactors")!).ToArray()},header,pending,seed=document.DrawingVariables.HandleSeed,apps=document.ApplicationRegistries.Select(a=>new{name=a.Name,handle=a.Handle}).ToArray(),classes=Wire(document.Classes),output=binary?Convert.ToBase64String(bytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(text.ToString()))}});
        }
        return results;
    }
}
