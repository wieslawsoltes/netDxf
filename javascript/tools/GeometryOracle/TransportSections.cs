// Observation-only adapters: invoke the unchanged native private section helpers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
internal static partial class Program
{
    private const BindingFlags TSFlags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
    private static object? TSCall(object target,string name,params object?[] args) {
        var type=target as Type??target.GetType();
        var candidates=type.GetMethods(TSFlags).Where(m=>m.Name==name && m.GetParameters().Length==args.Length);
        var method=candidates.Single(m=>m.GetParameters().Select((p,i)=>args[i]==null||p.ParameterType.IsInstanceOfType(args[i])).All(b=>b));
        return method.Invoke(target is Type?null:target,args);
    }
    private static object? TSGet(object item,string name)=>item.GetType().GetProperty(name,TSFlags)?.GetValue(item)??item.GetType().GetField(name,TSFlags)?.GetValue(item);
    private static void TSSet(object item,string name,object? value){var p=item.GetType().GetProperty(name,TSFlags);if(p!=null)p.SetValue(item,value);else item.GetType().GetField(name,TSFlags)!.SetValue(item,value);}
    private static object? TSInput(JsonElement value) {
        if(value.ValueKind==JsonValueKind.Object && value.TryGetProperty("bytes",out var bytes))return bytes.EnumerateArray().Select(v=>v.GetByte()).ToArray();
        if(value.ValueKind==JsonValueKind.Object && value.TryGetProperty("pattern",out var pattern))return Enumerable.Range(0,pattern.GetInt32()).Select(i=>(byte)(i*73+19)).ToArray();
        return Read(value);
    }
    private static object? TSValue(object? value)=>value is byte[] bytes?Convert.ToBase64String(bytes):value is double d?new{bits=Bits(d)}:value is long l?new{wide=l.ToString(CultureInfo.InvariantCulture)}:value is Enum?Convert.ToInt32(value):value;
    private static object TSFailure(Exception e){while(e is TargetInvocationException && e.InnerException!=null)e=e.InnerException;
        return new{type=e.GetType().Name,param=(e as ArgumentException)?.ParamName,message=e is ArgumentException || e is NullReferenceException?null:e.Message,
            inner=e.InnerException==null?null:new{type=e.InnerException.GetType().Name,param=(e.InnerException as ArgumentException)?.ParamName},version=e is DxfVersionNotSupportedException v?(int?)v.Version:null};}
    private static object TSCodecState(object codec)=>new{code=TSGet(codec,"Code"),value=TSValue(TSGet(codec,"Value")),position=TSGet(codec,"CurrentPosition")};
    private static object TSCommon(object data) {
        var payload=(MemoryStream?)TSGet(data,"Payload");
        return new{color=TSGet(data,"ColorName"),shadow=TSValue(TSGet(data,"ShadowMode")),seen=TSGet(data,"ColorNameSeen"),declared=TSGet(data,"DeclaredLength"),actual=TSGet(data,"ActualLength"),
            proxy=TSValue(TSGet(data,"ProxyGraphics")),payload=payload==null?null:new{bytes=Convert.ToBase64String(payload.ToArray()),open=payload.CanRead}};
    }
    private static object? TSBackground(MTextBackgroundFill? b)=>b==null?null:new{flags=(int)b.Flags,scale=TSValue(b.ScaleFactor),index=b.ColorIndex,color=b.TrueColor,name=b.ColorName,transparency=b.Transparency};
    private delegate bool TSBackgroundReader(ref MTextBackgroundFill? value);
    private static object TransportSectionsRequest(JsonElement input) {
        string mode=input.GetProperty("mode").GetString()!;bool binary=mode!="text",legacy=mode=="legacy";
        int version=input.GetProperty("version").GetInt32();var document=new DxfDocument((DxfVersion)version);
        var assembly=typeof(DxfDocument).Assembly;
        var writerType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        var readerType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueReader")!;
        using var sourceBytes=new MemoryStream();using var outputBytes=new MemoryStream();using var sourceText=new StringWriter(CultureInfo.InvariantCulture);using var outputText=new StringWriter(CultureInfo.InvariantCulture);
        sourceText.NewLine=outputText.NewLine="\n";
        object MakeWriter(MemoryStream bytes,StringWriter text)=>binary?Activator.CreateInstance(writerType,new object[]{new BinaryWriter(bytes,Encoding.UTF8,true),legacy})!:Activator.CreateInstance(writerType,text)!;
        var sourceWriter=MakeWriter(sourceBytes,sourceText);var writer=MakeWriter(outputBytes,outputText);
        foreach(var pair in input.GetProperty("tags").EnumerateArray())TSCall(sourceWriter,"Write",pair[0].GetInt16(),TSInput(pair[1]));
        TSCall(sourceWriter,"Flush");sourceBytes.Position=0;
        var reader=binary?Activator.CreateInstance(readerType,new object[]{new BinaryReader(sourceBytes,Encoding.UTF8,true),Encoding.UTF8,legacy})!:Activator.CreateInstance(readerType,new StringReader(sourceText.ToString()))!;
        object Context(string type,object chunk) {var host=Activator.CreateInstance(assembly.GetType("netDxf.IO."+type)!,true)!;TSSet(host,"doc",document);TSSet(host,"chunk",chunk);
            TSSet(host,type=="DxfReader"?"decodedStrings":"encodedStrings",new Dictionary<string,string>());return host;}
        var readHost=Context("DxfReader",reader);var writeHost=Context("DxfWriter",writer);
        var data=Activator.CreateInstance(readHost.GetType().GetNestedType("EntityCommonDataReader",BindingFlags.NonPublic)!,true)!;
        var backgroundRead=readHost.GetType().GetMethod("TryReadMTextBackground",TSFlags)!.CreateDelegate<TSBackgroundReader>(readHost);
        MTextBackgroundFill? background=null;
        var entity=new Line();object common=TSGet(entity,"CommonData")!;Mesh? mesh=null;
        if(input.TryGetProperty("common",out var commonInput))foreach(var p in commonInput.EnumerateObject())TSSet(common,p.Name,p.Name=="ShadowMode"&&p.Value.ValueKind!=JsonValueKind.Null?(object)(EntityShadowMode)p.Value.GetInt32():TSInput(p.Value));
        if(input.TryGetProperty("background",out var bgInput)&&bgInput.ValueKind!=JsonValueKind.Null){background=new MTextBackgroundFill();foreach(var p in bgInput.EnumerateObject())TSSet(background,p.Name,p.Name=="Flags"?(object)(MTextBackgroundFillFlags)p.Value.GetInt32():TSInput(p.Value));}
        if(input.TryGetProperty("mesh",out var meshInput)){
            var vertices=meshInput.GetProperty("vertices").EnumerateArray().Select(v=>new Vector3((double)TSInput(v[0])!,(double)TSInput(v[1])!,(double)TSInput(v[2])!)).ToArray();
            mesh=new Mesh(vertices,Array.Empty<int[]>());
            foreach(var face in meshInput.GetProperty("faces").EnumerateArray())mesh.Faces.Add(face.ValueKind==JsonValueKind.Null?null!:face.EnumerateArray().Select(v=>v.GetInt32()).ToArray());
            if(meshInput.TryGetProperty("repeat",out var repeat)){var face=new int[repeat.GetProperty("length").GetInt32()];for(int i=0;i<repeat.GetProperty("count").GetInt32();i++)mesh.Faces.Add(face);}
            foreach(var edge in meshInput.GetProperty("edges").EnumerateArray())mesh.Edges.Add(edge.ValueKind==JsonValueKind.Null?null!:new MeshEdge(edge[0].GetInt32(),edge[1].GetInt32(),(double)TSInput(edge[2])!));
        }
        if(input.TryGetProperty("placement",out var placementInput)){
            string placement=placementInput.GetString()!;EntityObject toAdd=input.TryGetProperty("target",out var target)&&target.GetString()=="background"?new MText{BackgroundFill=background}:mesh??(EntityObject)entity;
            if(placement=="attribute"||placement=="definition"){
                var block=new Block("Attributes");var definition=new AttributeDefinition("TAG");block.AttributeDefinitions.Add(definition);var insert=new Insert(block);document.Entities.Add(insert);
                object destination=placement=="attribute"?TSGet(insert.Attributes[0],"CommonData")!:TSGet(definition,"CommonData")!;
                foreach(string name in new[]{"ColorName","ShadowMode","ProxyGraphics"})TSSet(destination,name,TSGet(common,name));
            }else if(placement=="model")document.Entities.Add(toAdd);
            else if(placement=="paper"){var layout=document.Layouts.Add(new netDxf.Objects.Layout("Sheet"));layout.AssociatedBlock.Entities.Add(toAdd);}
            else {var block=new Block("Unused");block.Entities.Add(toAdd);document.Blocks.Add(block);}
        }
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()){
            object? result=null,error=null;string name=step.GetProperty("method").GetString()!;
            try{
                switch(name){
                    case "next":TSCall(reader,"Next");break;
                    case "thumbnailRead":result=TSCall(assembly.GetType("netDxf.IO.DxfThumbnailImage")!,"Read",reader);break;
                    case "thumbnailWrite":TSCall(assembly.GetType("netDxf.IO.DxfThumbnailImage")!,"Write",writer,TSInput(step.GetProperty("data")));break;
                    case "commonRead":TSCall(readHost,"ReadEntityCommonData",data);break;
                    case "complete":TSCall(data,"Complete");break;
                    case "backgroundRead":result=backgroundRead(ref background);break;
                    case "commonWrite":TSCall(writeHost,"WriteEntityCommonData",common);break;
                    case "backgroundWrite":TSCall(writeHost,"WriteMTextBackground",background);break;
                    case "validateCommon":TSCall(writeHost,"ValidateEntityCommonDataVersion",common);break;
                    case "validateCommonDocument":TSCall(writeHost,"ValidateEntityCommonDataVersions");break;
                    case "validateBackgroundDocument":TSCall(writeHost,"ValidateMTextBackgroundVersions");break;
                    case "meshValidate":TSCall(writeHost.GetType(),"ValidateMeshOutput",mesh,"Probe");break;
                    case "meshDocument":TSCall(writeHost,"ValidateMeshOutput");break;
                    case "meshVersions":TSCall(writeHost,"ValidateMeshVersions");break;
                    default:throw new InvalidOperationException("Unknown section observation: "+name);
                }
            }catch(Exception e){error=TSFailure(e);}
            TSCall(writer,"Flush");
            results.Add(new{ok=true,value=new{result=TSValue(result),error,reader=TSCodecState(reader),output=binary?Convert.ToBase64String(outputBytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(outputText.ToString())),
                common=TSCommon(data),background=TSBackground(background)}});
        }
        return results;
    }
}
