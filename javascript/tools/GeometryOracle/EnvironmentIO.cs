// Test-only native observations. No parser, writer, or expected-value implementation.
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
    private delegate PlotSettings EnvironmentPlotParser(List<DxfTag> tags,out string? handle);
    private static object EnvironmentIORequest(JsonElement input) {
        string kind=input.GetProperty("kind").GetString()!,mode=input.GetProperty("mode").GetString()!;
        int version=input.TryGetProperty("version",out var versionInput)?versionInput.GetInt32():18;
        var document=new DxfDocument((DxfVersion)version);var assembly=typeof(DxfDocument).Assembly;
        var readerType=assembly.GetType("netDxf.IO.DxfReader")!;var context=Activator.CreateInstance(readerType,true)!;
        TSSet(context,"doc",document);TSSet(context,"decodedStrings",new Dictionary<string,string>());
        var line=new Line();var viewport=new Viewport();document.Entities.Add(line);document.Entities.Add(viewport);
        var root=document.NamedObjects;var leaf=new DxfPlaceholder();root.Add("leaf",leaf);
        var view=document.Views.Add(new netDxf.Tables.View("EnvironmentView"));var layout=document.Layouts.Add(new Layout("EnvironmentPaper"));
        var ext=new DxfDictionary();document.Objects.SetExtensionDictionary(line.Owner.Record,ext);
        var foreign=new DxfDocument(DxfVersion.AutoCad2018);var foreignLeaf=new DxfPlaceholder();foreign.NamedObjects.Add("foreign",foreignLeaf);
        var resources=new Dictionary<string,object?>{{"line",line},{"viewport",viewport},{"root",root},{"leaf",leaf},{"view",view},{"port",document.Viewport},{"layout",layout},{"layoutPlot",layout.PlotSettings},{"block",line.Owner.Record},{"style",document.TextStyles["Standard"]},{"ext",ext},{"foreignLeaf",foreignLeaf},{"foreignStyle",foreign.TextStyles["Standard"]},{"document",document}};
        var identities=(HashSet<ulong>)TSGet(context,"sourceObjectIdentities")!;var accepted=(IDictionary)TSGet(context,"acceptedSourceObjects")!;var sources=(IDictionary)TSGet(context,"acceptedSourceRecords")!;
        void Source(DxfObject item){var source=Activator.CreateInstance(readerType.GetNestedType("SourceRecordIdentity",BindingFlags.NonPublic)!,true)!;ulong handle=Convert.ToUInt64(item.Handle,16);TSSet(source,"Handle",handle);TSSet(source,"IdentitySeen",true);identities.Add(handle);TSCall(context,"RecordSourceObject",item,source);}
        var added=(IDictionary<string,DxfObject>)TSGet(document,"AddedObjects")!;foreach(var item in added.Values)if(item.Handle!="0")Source(item);
        object record=Activator.CreateInstance(readerType.GetNestedType("DatabaseRecord",BindingFlags.NonPublic)!,true)!;PlotSettings? plot=null;string? shade=null;int cursor=0;
        object? Target(string name)=>name=="item"?TSGet(record,"Object"):name=="settings"?(TSGet(record,"Object") as DxfPlotSettingsObject)?.Settings:name=="plot"?plot:resources[name];
        object? Value(JsonElement value) {
            if(value.ValueKind==JsonValueKind.Object){
                if(value.TryGetProperty("h",out var h)){string handle=((DxfObject)Target(h.GetString()!)!).Handle!;if(handle==null)throw new NullReferenceException();if(value.TryGetProperty("lower",out var lower)&&lower.GetBoolean())handle=handle.ToLowerInvariant();return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+handle;}
                if(value.TryGetProperty("ref",out var reference))return Target(reference.GetString()!);
                if(value.TryGetProperty("vector",out var vector))return new Vector3((double)Value(vector[0])!,(double)Value(vector[1])!,(double)Value(vector[2])!);
            }
            return TSInput(value);
        }
        var tags=input.GetProperty("tags").EnumerateArray().Select(p=>new DxfTag(p[0].GetInt16(),Value(p[1])!)).ToList();
        var ownerContext=Activator.CreateInstance(readerType.GetNestedType("SunOwnerContext",BindingFlags.NonPublic)!,TSFlags,null,new object?[]{input.TryGetProperty("marker",out var marker)?marker.GetString():"AcDbViewport"},null)!;
        bool binary=mode!="text";using var bytes=new MemoryStream();using var text=new EntityBodyTextOutput();text.NewLine="\n";
        var chunkType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        object chunk=binary?Activator.CreateInstance(chunkType,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),mode=="legacy"})!:Activator.CreateInstance(chunkType,text)!;
        var writer=Activator.CreateInstance(assembly.GetType("netDxf.IO.DxfWriter")!,true)!;TSSet(writer,"doc",document);TSSet(writer,"chunk",chunk);TSSet(writer,"encodedStrings",new Dictionary<string,string>());
        IEnumerable<object> List(object value)=>((IEnumerable)value).Cast<object>();
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? result=null,error=null;string method=step.GetProperty("method").GetString()!;
            try {
                switch(method){
                    case "read":{
                        int start=step.TryGetProperty("start",out var si)?si.GetInt32():0;
                        if(kind=="OutputSettings")result=TSCall(context,"ReadOutputSettingsPayload",record,input.TryGetProperty("type",out var ty)?ty.GetString():"PLOTSETTINGS",tags,start);
                        else if(kind=="GeoData")result=TSCall(context,"ReadGeoDataPayload",record,input.TryGetProperty("type",out var gt)?gt.GetString():"GEODATA",tags,start);
                        else if(kind=="Sun"){if(input.TryGetProperty("payloadOnly",out var po)&&po.GetBoolean())result=TSCall(context,"ReadSunPayload",record,tags,start);else record=TSCall(context,"ReadSunRecord",tags)!;}break;
                    }
                    case "parse":{var parse=readerType.GetMethod("ParsePlotSettings",TSFlags)!.CreateDelegate<EnvironmentPlotParser>(context);plot=parse(tags,out shade);break;}
                    case "split":result=TSCall(writer,"SplitGeoDefinition",Value(step.GetProperty("text")));break;
                    case "geo-tag":{var args=new object?[]{tags,step.TryGetProperty("cursor",out var ci)?ci.GetInt32():cursor,step.GetProperty("code").GetInt16()};try{result=Wire(readerType.GetMethod("ReadGeoTag",TSFlags)!.Invoke(null,args));}finally{cursor=(int)args[1]!;}break;}
                    case "register":{
                        var item=(DxfDatabaseObject)Target("item")!;var owner=(DxfObject?)Target(step.TryGetProperty("owner",out var ow)?ow.GetString()!:"root");TSSet(item,"Owner",owner);
                        TSCall(document.Objects,"Register",item,item.Handle!=null);if(owner is DxfDictionary parent)TSCall(parent,"AddLoaded",step.TryGetProperty("name",out var nm)?nm.GetString():"PAYLOAD",item,!step.TryGetProperty("hard",out var ha)||ha.GetBoolean());Source(item);break;
                    }
                    case "resolve":TSCall(context,"Resolve"+kind+(kind=="GeoData"?"Hosts":"References"));break;
                    case "add-sun":TSCall(context,"AddSunReference",Value(step.GetProperty("owner")),Value(step.GetProperty("handle")));break;
                    case "write-ref":TSCall(writer,"WriteSunReference",Value(step.GetProperty("owner")));break;
                    case "null-handle":result=TSCall(context,"IsNullSourceHandle",Value(step.GetProperty("handle")));break;
                    case "observe":TSCall(ownerContext,"Observe",step.GetProperty("code").GetInt16(),Value(step.GetProperty("value")));result=TSGet(ownerContext,"IsPublic");break;
                    case "set":TSSet(Target(step.TryGetProperty("target",out var st)?st.GetString()!:"item")!,step.GetProperty("property").GetString()!,Value(step.GetProperty("value")));break;
                    case "set-sun":TSCall(typeof(DxfDocument).Assembly.GetType("netDxf.Objects.SunReferences")!,"Set",Value(step.GetProperty("owner")),Value(step.GetProperty("sun")),!step.TryGetProperty("present",out var pr)||pr.GetBoolean());break;
                    case "alias":{
                        var owner=(DxfDictionary)Target(step.TryGetProperty("owner",out var ow)?ow.GetString()!:"ext")!;owner.Remove(step.TryGetProperty("remove",out var rm)?rm.GetString()!:"ACAD_GEOGRAPHICDATA");
                        if(!step.TryGetProperty("name",out var nm)||nm.ValueKind!=JsonValueKind.Null)TSCall(owner,"AddLoaded",nm.ValueKind==JsonValueKind.Undefined?"ACAD_GEOGRAPHICDATA":nm.GetString(),Target("item"),!step.TryGetProperty("hard",out var ha)||ha.GetBoolean());break;
                    }
                    case "source":{
                        var item=(DxfObject)Target(step.GetProperty("target").GetString()!)!;ulong key=Convert.ToUInt64(item.Handle,16);
                        switch(step.GetProperty("action").GetString()){case "ambiguous":TSSet(sources[key]!,"Ambiguous",true);break;case "unaccept":accepted.Remove(key);break;case "discard":identities.Remove(key);break;case "replace":added[item.Handle]=new DxfPlaceholder();break;case "unregister":added.Remove(item.Handle);break;}break;
                    }
                    case "opaque":{var item=(DxfDatabaseObject)Activator.CreateInstance(typeof(DxfOpaqueObject),TSFlags,null,new object?[]{step.TryGetProperty("code",out var oc)?oc.GetString():"GEODATA",new List<DxfTag>()},null)!;TSSet(item,"Owner",root);TSCall(document.Objects,"Register",item,false);TSCall(root,"AddLoaded","Opaque",item,true);Source(item);resources["opaque"]=item;break;}
                    case "class":{var definition=new DxfClass(step.GetProperty("name").GetString()!,step.GetProperty("cpp").GetString()!,step.TryGetProperty("app",out var app)?app.GetString()!:"Probe");definition.IsEntity=step.TryGetProperty("entity",out var en)&&en.GetBoolean();definition.InstanceCount=step.TryGetProperty("count",out var ct)?ct.GetInt32():9;document.Classes.Add(definition);break;}
                    case "prepare":TSCall(writer,"Prepare"+kind+"Class",document.Classes);break;
                    case "validate":TSCall(writer,"ValidateOutputSettings");break;
                    case "database-validate":result=document.Objects.Validate();break;
                    case "write":case "write-plot":{
                        text.Calls=0;text.Hook=count=>{if(step.TryGetProperty("hooks",out var hooks))foreach(var hook in hooks.EnumerateArray())if(hook.GetProperty("at").GetInt32()==count){
                            string mutation=hook.GetProperty("kind").GetString()!;if(mutation=="throw")throw new InvalidOperationException("Injected environment writer callback.");
                            var target=Target(hook.TryGetProperty("target",out var tar)?tar.GetString()!:"item")!;string property=hook.GetProperty("property").GetString()!;
                            if(mutation=="clear")TSCall(TSGet(target,property)!,"Clear");else TSSet(target,property,Value(hook.GetProperty("value")));
                        }};
                        try{if(method=="write-plot")TSCall(writer,"WritePlotSettingsPayload",Target(step.TryGetProperty("target",out var wt)?wt.GetString()!:"plot"));else result=TSCall(writer,"Write"+kind+"Payload",Target(step.TryGetProperty("target",out var wi)?wi.GetString()!:"item"));}finally{text.Hook=null;}break;
                    }
                    default:throw new InvalidOperationException("Unknown environment observation "+method);
                }
            }catch(Exception e){error=PayloadError(e);}
            TSCall(chunk,"Flush");var metadata=TSGet(record,"Metadata")!;var sunType=assembly.GetType("netDxf.Objects.SunReferences")!;
            var pendingShade=List(TSGet(context,"outputShadeReferences")!).Select(p=>new{settings=Wire(TSGet(p,"Item1")),handle=TSGet(p,"Item2")}).ToArray();
            var pendingGeo=List(TSGet(context,"geoDataHosts")!).Select(p=>new{data=PayloadRef((DxfObject?)TSGet(p,"Item1")),handle=TSGet(p,"Item2")}).ToArray();
            var pendingSun=List(TSGet(context,"sunReferences")!).Select(p=>new{owner=PayloadRef((DxfObject?)TSGet(p,"Item1")),handle=TSGet(p,"Item2")}).ToArray();
            results.Add(new{ok=true,value=new{result,error,record=new{model=Wire(TSGet(record,"Object")),owner=TSGet(metadata,"Owner"),extension=TSGet(metadata,"Extension"),reactors=List(TSGet(metadata,"Reactors")!).ToArray()},
                pending=new{shade=pendingShade,geo=pendingGeo,sun=pendingSun},plot=Wire(plot),shade,cursor,@public=TSGet(ownerContext,"IsPublic"),hosts=new[]{"port","view","viewport"}.Select(name=>new{name,sun=PayloadRef((DxfObject?)TSCall(sunType,"Get",Target(name))),present=TSCall(sunType,"IsPresent",Target(name))}).ToArray(),
                layouts=document.Layouts.Select(l=>new{name=l.Name,plot=Wire(l.PlotSettings)}).ToArray(),seed=document.DrawingVariables.HandleSeed,apps=document.ApplicationRegistries.Select(a=>new{name=a.Name,handle=a.Handle}).ToArray(),classes=Wire(document.Classes),output=binary?Convert.ToBase64String(bytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(text.ToString()))}});
        }
        return results;
    }
}
