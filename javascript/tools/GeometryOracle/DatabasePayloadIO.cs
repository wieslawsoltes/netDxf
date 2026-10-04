// Observation-only calls to unchanged database payload methods; not a second parser.
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
    private static object? PayloadRef(DxfObject? value)=>value==null?null:new{type=value.GetType().Name,handle=value.Handle};
    private static object? PayloadModel(DxfObject? value) {
        if(value is DxfSortentsTable table)return new{model=Wire(value),block=PayloadRef(table.BlockRecord),entries=table.Entries.Select(e=>new{entity=PayloadRef(e.Entity),key=e.SortHandle}).ToArray()};
        return Wire(value);
    }
    private static object PayloadError(Exception e) {
        while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;
        return new{type=e.GetType().Name,param=(e as ArgumentException)?.ParamName,message=e is ArgumentException||e is NullReferenceException?null:e.Message,
            inner=e.InnerException==null?null:new{type=e.InnerException.GetType().Name,param=(e.InnerException as ArgumentException)?.ParamName}};
    }
    private static object DatabasePayloadRequest(JsonElement input){
        string kind=input.GetProperty("kind").GetString()!,mode=input.GetProperty("mode").GetString()!;
        int version=input.TryGetProperty("version",out var versionInput)?versionInput.GetInt32():18;
        var document=new DxfDocument((DxfVersion)version);var assembly=typeof(DxfDocument).Assembly;
        var readerType=assembly.GetType("netDxf.IO.DxfReader")!;
        var context=Activator.CreateInstance(readerType,true)!;TSSet(context,"doc",document);TSSet(context,"decodedStrings",new Dictionary<string,string>());
        var resources=new Dictionary<string,DxfObject>();
        var line=new Line();var light=new Light();document.Entities.Add(line);document.Entities.Add(light);
        resources["line"]=line;resources["light"]=light;resources["block"]=line.Owner.Record;resources["root"]=document.NamedObjects;resources["style"]=document.TextStyles["Standard"];
        foreach(string name in new[]{"buffer","buffer2","leaf"}){DxfDatabaseObject item=name=="leaf"?new DxfPlaceholder():new DxfIdBuffer();document.NamedObjects.Add(name,item);resources[name]=item;}
        ((DxfIdBuffer)resources["buffer"]).References.Add(line);
        var identities=(HashSet<ulong>)TSGet(context,"sourceObjectIdentities")!;
        var accepted=(IDictionary)TSGet(context,"acceptedSourceObjects")!;
        var sources=(IDictionary)TSGet(context,"acceptedSourceRecords")!;
        void Source(DxfObject item){var source=Activator.CreateInstance(readerType.GetNestedType("SourceRecordIdentity",BindingFlags.NonPublic)!,true)!;
            ulong handle=Convert.ToUInt64(item.Handle,16);TSSet(source,"Handle",handle);TSSet(source,"IdentitySeen",true);identities.Add(handle);TSCall(context,"RecordSourceObject",item,source);}
        var added=(IDictionary<string,DxfObject>)TSGet(document,"AddedObjects")!;
        foreach(DxfObject item in added.Values)if(item.Handle!="0")Source(item);
        object record=Activator.CreateInstance(readerType.GetNestedType("DatabaseRecord",BindingFlags.NonPublic)!,true)!;
        DxfObject? Target(string name)=>name=="item"?(DxfObject?)TSGet(record,"Object"):resources[name];
        object? Value(JsonElement value){
            if(value.ValueKind==JsonValueKind.Object){
                if(value.TryGetProperty("h",out var h)){string handle=Target(h.GetString()!)!.Handle!;if(handle==null)throw new NullReferenceException();if(value.TryGetProperty("lower",out var lower)&&lower.GetBoolean())handle=handle.ToLowerInvariant();return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+handle;}
                if(value.TryGetProperty("ref",out var reference))return Target(reference.GetString()!);
                if(value.TryGetProperty("vector",out var vector))return new Vector3((double)Value(vector[0])!,(double)Value(vector[1])!,(double)Value(vector[2])!);
            }
            return TSInput(value);
        }
        var tags=input.GetProperty("tags").EnumerateArray().Select(p=>new DxfTag(p[0].GetInt16(),Value(p[1])!)).ToList();
        bool binary=mode!="text";using var bytes=new MemoryStream();using var text=new EntityBodyTextOutput();text.NewLine="\n";
        var chunkType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        object chunk=binary?Activator.CreateInstance(chunkType,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),mode=="legacy"})!:Activator.CreateInstance(chunkType,text)!;
        var writer=Activator.CreateInstance(assembly.GetType("netDxf.IO.DxfWriter")!,true)!;TSSet(writer,"doc",document);TSSet(writer,"chunk",chunk);TSSet(writer,"encodedStrings",new Dictionary<string,string>());
        IEnumerable<object> List(object value)=>((IEnumerable)value).Cast<object>();
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? result=null,error=null;string method=step.GetProperty("method").GetString()!;
            try{
                switch(method){
                    case "read":{
                        int start=step.TryGetProperty("start",out var startInput)?startInput.GetInt32():0;
                        switch(kind){
                            case "Container":result=TSCall(context,"ReadContainerPayload",record,input.GetProperty("type").GetString(),tags,start);break;
                            case "LightList":result=TSCall(context,"ReadLightListPayload",record,input.TryGetProperty("type",out var lt)?lt.GetString():"LIGHTLIST",tags,start);break;
                            case "DataTable":if(input.TryGetProperty("payloadOnly",out var po)&&po.GetBoolean())result=TSCall(context,"ReadDataTablePayload",record,tags,start);else record=TSCall(context,"ReadDataTableRecord",tags)!;break;
                            case "LayerIndex":record=TSCall(context,"ReadLayerIndexRecord",tags)!;break;
                            case "LayerFilterPointer":record=TSCall(context,"ReadLayerFilterPointerRecord",input.GetProperty("type").GetString(),tags)!;break;
                        }break;
                    }
                    case "register":{
                        var item=(DxfDatabaseObject)Target("item")!;var owner=Target(step.TryGetProperty("owner",out var own)?own.GetString()!:"root");TSSet(item,"Owner",owner);
                        TSCall(document.Objects,"Register",item,item.Handle!=null);if(owner is DxfDictionary parent)TSCall(parent,"AddLoaded","PAYLOAD",item,true);Source(item);break;
                    }
                    case "owner":{
                        var item=Target(step.GetProperty("target").GetString()!)!;if(item.Owner is DxfDictionary dict)foreach(var entry in dict.Entries.ToArray())if(ReferenceEquals(entry.Target,item))dict.Remove(entry.Name);
                        TSSet(item,"Owner",Target(step.GetProperty("owner").GetString()!));break;
                    }
                    case "resolve":if(kind=="Container")TSCall(context,"ResolveContainerReferences",record);else if(kind!="LayerFilterPointer")TSCall(context,"Resolve"+kind+"References");break;
                    case "source":{
                        var item=Target(step.GetProperty("target").GetString()!)!;ulong key=Convert.ToUInt64(item.Handle,16);
                        switch(step.GetProperty("action").GetString()){
                            case "ambiguous":TSSet(sources[key]!,"Ambiguous",true);break;
                            case "unaccept":accepted.Remove(key);break;case "discard":identities.Remove(key);break;
                            case "replace":added[item.Handle]=new DxfPlaceholder();break;
                            case "validate":TSCall(context,"ValidateSourceIdentityDeclarations");break;
                        }break;
                    }
                    case "lookup":result=PayloadRef((DxfObject?)TSCall(context,"GetObjectBySourceHandle",Value(step.GetProperty("handle")),step.TryGetProperty("includeMetadata",out var im)&&im.GetBoolean()));break;
                    case "clear":TSCall(TSGet(Target(step.GetProperty("target").GetString()!)!,"References")!,"Clear");break;
                    case "set":TSSet(Target(step.TryGetProperty("target",out var st)?st.GetString()!:"item")!,step.GetProperty("property").GetString()!,Value(step.GetProperty("value")));break;
                    case "class":{
                        var definition=new DxfClass(step.GetProperty("name").GetString()!,step.GetProperty("cpp").GetString()!,step.TryGetProperty("app",out var app)?app.GetString()!:"Probe");
                        definition.IsEntity=step.TryGetProperty("entity",out var en)&&en.GetBoolean();definition.InstanceCount=step.TryGetProperty("count",out var ct)?ct.GetInt32():9;document.Classes.Add(definition);break;
                    }
                    case "prepare":TSCall(writer,"Prepare"+(kind=="LayerFilterPointer"?"LayerFilterPointerClasses":kind+"Class"),document.Classes);break;
                    case "write":{
                        text.Calls=0;text.Hook=count=>{
                            if(step.TryGetProperty("hooks",out var hooks))foreach(var hook in hooks.EnumerateArray())if(hook.GetProperty("at").GetInt32()==count){
                                string mutation=hook.GetProperty("kind").GetString()!;if(mutation=="throw")throw new InvalidOperationException("Injected database writer callback.");
                                var target=Target(hook.TryGetProperty("target",out var tar)?tar.GetString()!:"item")!;string property=hook.GetProperty("property").GetString()!;
                                if(mutation=="clear")TSCall(TSGet(target,property)!,"Clear");else TSSet(target,property,Value(hook.GetProperty("value")));
                            }
                        };
                        try{result=TSCall(writer,"Write"+kind+"Payload",Target("item"));}finally{text.Hook=null;}break;
                    }
                    case "snapshot":break;
                }
            }catch(Exception e){error=PayloadError(e);}
            TSCall(chunk,"Flush");
            var metadata=TSGet(record,"Metadata")!;
            var data=List(TSGet(context,"dataTableReferences")!).Select(p=>new{table=PayloadRef((DxfObject?)TSGet(p,"Item1")),rows=(int)TSGet(p,"Item2")!,columns=List(TSGet(p,"Item3")!).Select(c=>new{type=(int)(DxfDataCellType)TSGet(c,"Type")!,name=(string?)TSGet(c,"Name"),values=Wire(TSGet(c,"Values"))}).ToArray()}).ToArray();
            var lights=List(TSGet(context,"lightListReferences")!).Select(p=>new{list=PayloadRef((DxfObject?)TSGet(p,"Item1")),entries=List(TSGet(p,"Item2")!).Select(e=>new object?[]{(string?)TSGet(e,"Item1"),Wire(TSGet(e,"Item2"))}).ToArray()}).ToArray();
            var indexes=List(TSGet(context,"pendingLayerIndexes")!).Select(p=>new{index=PayloadRef((DxfObject?)TSGet(p,"Key")),entries=List(TSGet(p,"Value")!).Select(e=>new object?[]{TSGet(e,"Item1"),TSGet(e,"Item2"),TSGet(e,"Item3")}).ToArray()}).ToArray();
            results.Add(new{ok=true,value=new{result,error,record=new{model=PayloadModel((DxfObject?)TSGet(record,"Object")),owner=TSGet(metadata,"Owner"),extension=TSGet(metadata,"Extension"),reactors=List(TSGet(metadata,"Reactors")!).ToArray(),refs=List(TSGet(record,"ContainerReferences")!).ToArray(),keys=List(TSGet(record,"SortKeys")!).ToArray()},
                pending=new{tables=data,lights,indexes},seed=document.DrawingVariables.HandleSeed,apps=document.ApplicationRegistries.Select(a=>new{name=a.Name,handle=a.Handle}).ToArray(),classes=Wire(document.Classes),output=binary?Convert.ToBase64String(bytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(text.ToString()))}});
        }
        return results;
    }
}
