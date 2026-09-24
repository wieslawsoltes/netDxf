// Test-only observations of unchanged native entity codecs, not a second parser.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
internal static partial class Program {
    private sealed class EntityBodyTextOutput : StringWriter {
        public Action<int>? Hook;public int Calls;
        public EntityBodyTextOutput():base(CultureInfo.InvariantCulture){}
        public override void WriteLine(string? value){Hook?.Invoke(++Calls);base.WriteLine(value);}
        public override void WriteLine(int value){Hook?.Invoke(++Calls);base.WriteLine(value.ToString(CultureInfo.InvariantCulture));}
    }
    private static object EntityBodyIORequest(JsonElement input) {
        string mode=input.GetProperty("mode").GetString()!,kind=input.GetProperty("kind").GetString()!;
        bool binary=mode!="text",legacy=mode=="legacy";
        var document=new DxfDocument((DxfVersion)input.GetProperty("version").GetInt32());var assembly=typeof(DxfDocument).Assembly;
        var writerType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueWriter")!;
        var readerType=assembly.GetType("netDxf.IO."+(binary?"Binary":"Text")+"CodeValueReader")!;
        using var sourceBytes=new MemoryStream();using var outputBytes=new MemoryStream();using var sourceText=new StringWriter(CultureInfo.InvariantCulture);using var outputText=new EntityBodyTextOutput();
        sourceText.NewLine=outputText.NewLine="\n";
        object MakeWriter(MemoryStream b,StringWriter t)=>binary?Activator.CreateInstance(writerType,new object[]{new BinaryWriter(b,Encoding.UTF8,true),legacy})!:Activator.CreateInstance(writerType,t)!;
        var sw=MakeWriter(sourceBytes,sourceText);var writer=MakeWriter(outputBytes,outputText);
        foreach(var pair in input.GetProperty("tags").EnumerateArray())TSCall(sw,"Write",pair[0].GetInt16(),TSInput(pair[1]));
        TSCall(sw,"Flush");sourceBytes.Position=0;
        var reader=binary?Activator.CreateInstance(readerType,new object[]{new BinaryReader(sourceBytes,Encoding.UTF8,true),Encoding.UTF8,legacy})!:Activator.CreateInstance(readerType,new StringReader(sourceText.ToString()))!;
        object Context(string type,object chunk){var host=Activator.CreateInstance(assembly.GetType("netDxf.IO."+type)!,true)!;TSSet(host,"doc",document);TSSet(host,"chunk",chunk);TSSet(host,type=="DxfReader"?"decodedStrings":"encodedStrings",new Dictionary<string,string>());return host;}
        var rh=Context("DxfReader",reader);var wh=Context("DxfWriter",writer);EntityObject? entity=null;
        var result=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            object? error=null;string op=step.GetProperty("method").GetString()!;
            try {
                switch(op) {
                    case "next":TSCall(reader,"Next");break;
                    case "read":entity=(EntityObject?)TSCall(rh,kind=="Face3D"?"ReadFace3d":"Read"+kind,kind=="Spline"?new object?[]{step.TryGetProperty("stopAtHelix",out var stop)&&stop.GetBoolean()}:kind=="AcisEntity"?new object?[]{input.GetProperty("entityCode").GetString()}:Array.Empty<object?>());break;
                    case "write": {
                        outputText.Calls=0;outputText.Hook=count=>{
                            if(step.TryGetProperty("hooks",out var hooks))foreach(var hook in hooks.EnumerateArray())if(hook.GetProperty("at").GetInt32()==count){
                                string mutation=hook.GetProperty("kind").GetString()!;
                                if(mutation=="throw")throw new InvalidOperationException("Injected body writer callback.");
                                if(mutation=="control")((Spline)entity!).ControlPoints[hook.GetProperty("index").GetInt32()]=(Vector3)Read(hook.GetProperty("value"))!;
                                else TSSet(entity!,hook.GetProperty("property").GetString()!,Read(hook.GetProperty("value")));
                            }
                        };
                        try{if(kind=="Spline")TSCall(wh,"WriteSpline",entity,!step.TryGetProperty("writeXData",out var writeData)||writeData.GetBoolean());else TSCall(wh,"Write"+kind,entity);}
                        finally{outputText.Hook=null;}break;
                    }
                    case "construct":entity=(EntityObject?)Read(step.GetProperty("value"));break;
                    case "set":TSSet(entity!,step.GetProperty("property").GetString()!,Read(step.GetProperty("value")));break;
                    case "place": {
                        string where=step.GetProperty("where").GetString()!;
                        if(where=="model")document.Entities.Add(entity!);
                        else if(where=="paper")document.Layouts.Add(new netDxf.Objects.Layout("BodySheet")).AssociatedBlock.Entities.Add(entity!);
                        else{var block=new Block("UnusedBody");block.Entities.Add(entity!);document.Blocks.Add(block);}break;
                    }
                    case "class": {
                        var definition=new DxfClass(step.GetProperty("name").GetString()!,step.GetProperty("cpp").GetString()!,step.TryGetProperty("app",out var app)?app.GetString()!:"Probe");
                        definition.IsEntity=step.GetProperty("isEntity").GetBoolean();definition.InstanceCount=step.TryGetProperty("count",out var instances)?instances.GetInt32():null;document.Classes.Add(definition);break;
                    }
                    case "prepare":TSCall(wh,"PrepareHelixClass",document.Classes);break;
                    case "validate":TSCall(wh,kind=="Helix"?"ValidateHelixVersions":kind=="Light"?"ValidateLightVersions":kind=="LwPolyline"?"ValidateLwPolylineFidelity":"ValidateAcisEntities");break;
                    default:throw new InvalidOperationException("Unknown entity codec operation "+op);
                }
            }catch(Exception e){error=TSFailure(e);}
            TSCall(writer,"Flush");
            var observation=new Dictionary<string,object?> { ["error"]=error,["reader"]=TSCodecState(reader),["entity"]=Wire(entity),["output"]=binary?Convert.ToBase64String(outputBytes.ToArray()):Convert.ToBase64String(Encoding.UTF8.GetBytes(outputText.ToString())),
                ["apps"]=document.ApplicationRegistries.Select(app=>new{name=app.Name,handle=app.Handle}).ToArray(),["seed"]=document.DrawingVariables.HandleSeed };
            if(input.TryGetProperty("captureClasses",out var capture)&&capture.GetBoolean())observation["classes"]=Wire(document.Classes);
            result.Add(new{ok=true,value=observation});
        }
        return result;
    }
}
