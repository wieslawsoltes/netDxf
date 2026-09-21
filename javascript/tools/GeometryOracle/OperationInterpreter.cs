// Test-only reflection runner. All results come from the pinned production assembly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Units;

internal static partial class Program
{
    private static object? Step(JsonElement step)
    {
        string kind=step.GetProperty("kind").GetString()!;
        object? target=step.TryGetProperty("target",out var key)?Values[key.GetString()!]:null;
        Type? type=step.TryGetProperty("type",out var name)?Resolve(name.GetString()!):target?.GetType();
        string member=step.TryGetProperty("member",out var m)?m.GetString()!:"";
        var args=Arguments(step); object? result;
        switch(kind) {
            case "lin-names": case "lin-load": case "lin-save": case "shape-names": case "shape-query": result=StyleFileStep(step,target);break;
            case "events": return Observations.ToArray();
            case "observe": case "unobserve": result=ObservationStep(step,target);break;
            case "reference-equals": result=ReferenceEquals(args[0],args[1]);break;
            case "pat-names": case "pat-load": case "pat-save": result=PatternTextStep(step,target);break;
            case "value": result=Read(step.GetProperty("value"));break;
            case "new": result=Create(type!,args,Signature(step),step.TryGetProperty("nonPublic",out var ctorHidden)&&ctorHidden.GetBoolean());break;
            case "get": {
                var flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
                if(step.TryGetProperty("nonPublic",out var hidden)&&hidden.GetBoolean())flags|=BindingFlags.NonPublic;
                var property=type!.GetProperty(member,flags);var field=type!.GetField(member,flags);
                if(property is null && field is null)throw new MissingMemberException(type.Name,member);
                result=property is not null?property.GetValue(target):field!.GetValue(target);break;
            }
            case "map-add": ((IDictionary)target!).Add(args[0]!,args[1]);result=null;break;
            case "set": {
                var flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
                if(step.TryGetProperty("nonPublic",out var setterHidden)&&setterHidden.GetBoolean())flags|=BindingFlags.NonPublic;
                var property=type!.GetProperty(member,flags);var field=type.GetField(member,flags);
                if(property is not null)property.SetValue(target,Read(step.GetProperty("value")));
                else if(field is not null)field.SetValue(target,Read(step.GetProperty("value")));
                else throw new MissingMemberException(type.Name,member);
                result=null;break;
            }
            case "index": if(target is Array array){result=array.GetValue((int)args[0]!);break;} result=type!.GetProperty("Item",Signature(step) ?? args.Select(a=>a!.GetType()).ToArray())!.GetValue(target,args);break;
            case "set-index": if(target is Array indexedArray){indexedArray.SetValue(Read(step.GetProperty("value")),(int)args[0]!);result=null;break;} type!.GetProperty("Item",Signature(step) ?? args.Select(a=>a!.GetType()).ToArray())!.SetValue(target,Read(step.GetProperty("value")),args);result=null;break;
            case "snapshot": result=target;break;
            case "call": {
                var sig=Signature(step);
                var flags=BindingFlags.Public|(target is null?BindingFlags.Static:BindingFlags.Instance);
                if(step.TryGetProperty("nonPublic",out var hiddenCall)&&hiddenCall.GetBoolean())flags|=BindingFlags.NonPublic;
                var method=sig is null?type!.GetMethods(flags).Where(v=>v.Name==member).Single(v=>Matches(v.GetParameters(),args))
                    :type!.GetMethod(member,flags,null,sig,null)!;
                result=method.Invoke(target,args);
                if(method.GetParameters().Any(p=>p.IsOut)) return new { result=Wire(result), outputs=method.GetParameters().Select((p,i)=>(p,i)).Where(v=>v.p.IsOut).Select(v=>Wire(args[v.i])).ToArray() };
                break;
            }
            case "emit": {
                var v=(Vector3)target!;
                var tags=new[]{new DxfTag(0,"SECTION"),new DxfTag(2,"HEADER"),new DxfTag(9,"$ACADVER"),new DxfTag(1,"AC1032"),new DxfTag(0,"ENDSEC"),
                    new DxfTag(0,"SECTION"),new DxfTag(2,"ENTITIES"),new DxfTag(0,"POINT"),new DxfTag(10,v.X),new DxfTag(20,v.Y),new DxfTag(30,v.Z),new DxfTag(0,"ENDSEC"),new DxfTag(0,"EOF")};
                var doc=DxfRawDocument.Create(tags);using var text=new MemoryStream();using var binary=new MemoryStream();doc.Save(text,false);doc.Save(binary,true);
                return new {text=Convert.ToBase64String(text.ToArray()),binary=Convert.ToBase64String(binary.ToArray())};
            }
            default: throw new ArgumentException("Unknown step kind " + kind);
        }
        if(step.TryGetProperty("id",out var id))Values[id.GetString()!]=result;
        return Wire(result);
    }
}
