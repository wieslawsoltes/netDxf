// Independent observations of unchanged native OBJECTS methods and physical-source tracking.
using System;using System.Collections;using System.Collections.Generic;using System.Globalization;using System.IO;using System.Linq;using System.Reflection;using System.Text;using System.Text.Json;
using netDxf;using netDxf.Entities;using netDxf.Header;using netDxf.IO;using netDxf.Objects;
internal static partial class Program {
    private static object? OGRef(DxfObject? item)=>item==null?null:new{type=item.GetType().Name,handle=item.Handle};
    private static object OGTag(DxfTag tag)=>new{code=tag.Code,value=TSValue(tag.Value)};
    private static object? OGModel(DxfObject? item){
        if(item==null)return null;
        var payload=TSGet(item,"Payload") as IEnumerable<DxfTag> ?? TSGet(item,"Tags") as IEnumerable<DxfTag>;
        return new{type=item.GetType().Name,code=item.CodeName,handle=item.Handle,owner=OGRef(item.Owner),extension=OGRef(item.ExtensionDictionary),reactors=item.PersistentReactors.Select(OGRef).ToArray(),
            entries=(item as DxfDictionary)?.Entries.Select(e=>new{name=e.Name,target=OGRef(e.Target),hard=e.IsHardOwner}).ToArray(),
            fallback=OGRef((item as DxfDictionaryWithDefault)?.Default),hard=(item as DxfDictionary)?.IsHardOwner,cloning=(item is DxfDictionary d?(int?)d.Cloning:item is DxfXRecord x?(int?)x.Cloning:null),
            data=(item as DxfXRecord)?.Data.Select(OGTag).ToArray(),payload=payload?.Select(OGTag).ToArray(),schema=(item as DxfDictionaryVariable)?.Schema,value=(item as DxfDictionaryVariable)?.Value,
            references=item is DxfDatabaseObject ? ((IEnumerable<DxfObject>)TSGet(item,"DatabaseReferences")!).Select(OGRef).ToArray() : null,
            xdata=item.XData.Values.Select(v=>new{name=v.ApplicationRegistry.Name,handle=v.ApplicationRegistry.Handle,records=v.XDataRecord.Select(t=>new{code=(int)t.Code,value=TSValue(t.Value)}).ToArray()}).ToArray()};
    }
    private static object ObjectGraphIORequest(JsonElement input){
        var doc=new DxfDocument((DxfVersion)input.GetProperty("version").GetInt32());var assembly=typeof(DxfDocument).Assembly;
        var rt=assembly.GetType("netDxf.IO.DxfReader")!;var context=Activator.CreateInstance(rt,true)!;TSSet(context,"doc",doc);TSSet(context,"decodedStrings",new Dictionary<string,string>());TSSet(context,"dictionaries",Activator.CreateInstance(rt.GetField("dictionaries",TSFlags)!.FieldType,new object[]{StringComparer.OrdinalIgnoreCase}));
        var resources=new Dictionary<string,object>();var line=new Line();var light=new Light();doc.Entities.Add(line);doc.Entities.Add(light);
        resources["line"]=line;resources["light"]=light;resources["block"]=line.Owner.Record;resources["style"]=doc.TextStyles["Standard"];resources["layer"]=doc.Layers["0"];resources["groups"]=doc.Groups;resources["layouts"]=doc.Layouts;resources["mlineStyles"]=doc.MlineStyles;resources["images"]=doc.ImageDefinitions;resources["dgn"]=doc.UnderlayDgnDefinitions;resources["dwf"]=doc.UnderlayDwfDefinitions;resources["pdf"]=doc.UnderlayPdfDefinitions;
        object? Get(string id)=>resources.TryGetValue(id,out var result)?result:doc.GetObjectByHandle(id);
        object? ReadInput(JsonElement value){
            if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("h",out var name)){string handle=((DxfObject)Get(name.GetString()!)!).Handle;return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+(value.TryGetProperty("lower",out var lower)&&lower.GetBoolean()?handle.ToLowerInvariant():handle);}
            if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("ref",out var reference))return Get(reference.GetString()!);
            return TSInput(value);
        }
        var ids=(HashSet<ulong>)TSGet(context,"sourceObjectIdentities")!;
        var added=(IDictionary<string,DxfObject>)TSGet(doc,"AddedObjects")!;
        if(input.TryGetProperty("admitResources",out var admit)&&admit.GetBoolean())foreach(var item in added.Values.ToArray())if(item.Handle!="0"){
            var source=Activator.CreateInstance(rt.GetNestedType("SourceRecordIdentity",BindingFlags.NonPublic)!,true)!;ulong id=Convert.ToUInt64(item.Handle,16);TSSet(source,"Handle",id);TSSet(source,"IdentitySeen",true);ids.Add(id);TSCall(context,"RecordSourceObject",item,source);
        }
        string mode=input.GetProperty("mode").GetString()!;bool binary=mode!="text",legacy=mode=="legacy";
        using var inputBytes=new MemoryStream();using var outputBytes=new MemoryStream();using var inputText=new StringWriter(CultureInfo.InvariantCulture);using var outputText=new EntityBodyTextOutput();inputText.NewLine=outputText.NewLine="\n";
        var wt=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        object Make(MemoryStream bytes,StringWriter text)=>binary?Activator.CreateInstance(wt,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),legacy})!:Activator.CreateInstance(wt,text)!;
        var sourceWriter=Make(inputBytes,inputText);var chunk=Make(outputBytes,outputText);
        foreach(var pair in input.GetProperty("tags").EnumerateArray())TSCall(sourceWriter,"Write",pair[0].GetInt16(),ReadInput(pair[1]));TSCall(sourceWriter,"Flush");inputBytes.Position=0;
        var codecType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueReader")!;
        var inner=binary?Activator.CreateInstance(codecType,new object[]{new BinaryReader(inputBytes,Encoding.UTF8,true),Encoding.UTF8,legacy})!:Activator.CreateInstance(codecType,new StringReader(inputText.ToString()))!;
        var metadata=TSGet(context,"entityDatabaseMetadata")!;
        var reader=Activator.CreateInstance(rt.GetNestedType("DatabaseMetadataReader",BindingFlags.NonPublic)!,TSFlags,null,new object[]{inner,metadata,ids},null)!;TSSet(context,"chunk",reader);
        var writer=Activator.CreateInstance(assembly.GetType("netDxf.IO.DxfWriter")!,true)!;TSSet(writer,"doc",doc);TSSet(writer,"chunk",chunk);TSSet(writer,"encodedStrings",new Dictionary<string,string>());TSSet(writer,"isBinary",binary);
        var records=(IList)TSGet(context,"databaseRecords")!;var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? result=null,error=null;
            try{
                string method=step.GetProperty("method").GetString()!;string target=step.TryGetProperty("target",out var ti)?ti.GetString()!:"item";
                switch(method){
                    case "next":TSCall(reader,"Next");break;
                    case "read":{
                        string kind=step.TryGetProperty("kind",out var k)?k.GetString()!:"record";
                        object? value=TSCall(context,kind=="dictionary"?"ReadDictionaryDatabaseRecord":kind=="xrecord"?"ReadXRecordDatabaseRecord":"ReadDatabaseRecord");
                        string id=step.TryGetProperty("id",out var ri)?ri.GetString()!:"item";
                        if(records.Count>0)resources[id]=(DxfObject)TSGet(records[records.Count-1]!,"Object")!;
                        if(kind=="dictionary"&&value!=null){((IDictionary)TSGet(context,"dictionaries")!).Add((string)TSGet(value,"Handle")!,value);if(step.TryGetProperty("root",out var root)&&root.GetBoolean())TSSet(context,"namedDictionary",value);}
                        if(kind!="record"){
                            if(value==null)result=null;
                            else if(kind=="dictionary")result=new{handle=TSGet(value,"Handle"),hard=TSGet(value,"IsHardOwner"),cloning=(int)(DictionaryCloningFlags)TSGet(value,"Cloning")!,entries=((IDictionary)TSGet(value,"Entries")!).Keys.Cast<string>().Select(key=>new{key,value=((IDictionary)TSGet(value,"Entries")!)[key]}).ToArray()};
                            else result=new{handle=TSGet(value,"Handle"),owner=TSGet(value,"OwnerHandle"),code=TSGet(value,"Codename"),flags=(int)(DictionaryCloningFlags)TSGet(value,"Flags")!,entries=((IEnumerable)TSGet(value,"Entries")!).Cast<object>().Select(e=>new{code=TSGet(e,"Code"),value=TSValue(TSGet(e,"Value"))}).ToArray()};
                        }break;
                    }
                    case "import":TSCall(context,"ImportDatabaseObjects");break;
                    case "lookup":result=OGRef((DxfObject?)TSCall(context,"GetObjectBySourceHandle",ReadInput(step.GetProperty("handle")),false));break;
                    case "validate":result=doc.Objects.Validate().ToArray();break;
                    case "transport":TSCall(writer,"ValidateDatabaseTransport");break;
                    case "prepare":TSCall(writer,"PrepareDatabaseClasses",doc.Classes);break;
                    case "generated":{var projection=Activator.CreateInstance(assembly.GetType("netDxf.Objects.DictionaryObject")!,TSFlags,null,new object?[]{null},null)!;var entries=(IDictionary)TSGet(projection,"Entries")!;foreach(var pair in step.GetProperty("entries").EnumerateArray())entries.Add(ReadInput(pair[0])!,ReadInput(pair[1]));resources["generated"]=projection;break;}
                    case "class":{var definition=new DxfClass(step.GetProperty("name").GetString()!,step.GetProperty("cpp").GetString()!,"Probe");definition.IsEntity=step.TryGetProperty("entity",out var en)&&en.GetBoolean();definition.InstanceCount=9;doc.Classes.Add(definition);break;}
                    case "set":{var item=target=="variables"?(object)doc.DrawingVariables:Get(target)!;var name=step.GetProperty("property").GetString()!;var value=ReadInput(step.GetProperty("value"));var property=item.GetType().GetProperty(name,TSFlags);if(property?.PropertyType.IsEnum==true)value=Enum.ToObject(property.PropertyType,Convert.ToInt32(value));TSSet(item,name,value);break;}
                    case "reactor":((DxfObject)Get(target)!).PersistentReactors.Add((DxfObject?)ReadInput(step.GetProperty("value"))!);break;
                    case "manage":((HashSet<string>)TSGet(context,"managedReactorHandles")!).Add((string)ReadInput(step.GetProperty("value"))!);break;
                    case "replace":added[((DxfObject)Get(target)!).Handle]=new DxfPlaceholder();break;
                    case "write":case "metadata":{
                        outputText.Calls=0;outputText.Hook=count=>{if(step.TryGetProperty("throwAt",out var at)&&count==at.GetInt32())throw new InvalidOperationException("Injected object writer callback.");if(step.TryGetProperty("versionAt",out var va)&&count==va.GetInt32())doc.DrawingVariables.AcadVer=(DxfVersion)step.GetProperty("newVersion").GetInt32();};
                        try{if(method=="write")TSCall(writer,"WriteDatabaseObject",Get(target),step.TryGetProperty("generated",out var generated)&&generated.GetBoolean()?resources["generated"]:null);
                        else TSCall(writer,"WriteDatabaseMetadata",Get(target),step.TryGetProperty("automatic",out var autos)?autos.EnumerateArray().Select(v=>(string?)ReadInput(v)).ToArray():null);}
                        finally{outputText.Hook=null;}break;
                    }
                    case "snapshot":break;
                    default:throw new InvalidOperationException("Unknown object graph operation "+method);
                }
            }catch(Exception e){error=PayloadError(e);}
            TSCall(chunk,"Flush");var database=(DxfObjectDatabase?)TSGet(doc,"objectDatabase");
            results.Add(new{ok=true,value=new{result,error,reader=TSCodecState(reader),records=records.Cast<object>().Select(r=>new{item=OGModel((DxfObject?)TSGet(r,"Object")),owner=TSGet(TSGet(r,"Metadata")!,"Owner"),extension=TSGet(TSGet(r,"Metadata")!,"Extension"),reactors=((IEnumerable)TSGet(TSGet(r,"Metadata")!,"Reactors")!).Cast<string>().ToArray(),entries=((IEnumerable)TSGet(r,"Entries")!).Cast<object>().Select(e=>new object?[]{TSGet(e,"Item1"),TSGet(e,"Item2"),TSGet(e,"Item3")}).ToArray(),fallback=TSGet(r,"Default"),source=new{handle=((ulong)TSGet(TSGet(r,"SourceIdentity")!,"Handle")!).ToString(CultureInfo.InvariantCulture),ambiguous=(bool)TSGet(TSGet(r,"SourceIdentity")!,"Ambiguous")!}}).ToArray(),
                objects=database?.Items.Select(OGModel).ToArray(),root=OGRef(database?.Root),seed=doc.DrawingVariables.HandleSeed,registry=added.Select(p=>new{key=p.Key,item=OGRef(p.Value)}).ToArray(),classes=Wire(doc.Classes),pendingStyles=((IEnumerable)TSGet(context,"mleaderReferences")!).Cast<object>().Count(),output=binary?Convert.ToBase64String(outputBytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(outputText.ToString()))}});
        }
        return results;
    }
}
