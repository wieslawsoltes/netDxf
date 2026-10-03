// Observation-only harness. Every constructor/edit invokes the unchanged C# source.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.IO;
using netDxf.Objects;
internal static partial class Program {
    private static object? ContentScalar(object? value) => value is double d ? TableReal(d) : value is Vector3 v ? GeometryPoint(v) : value is string text ? TableText(text) : value;
    private static object? TableContentSnapshot(object? value) {
        if(value==null)return null;
        if(value is DxfStoredTableContent content)return new {kind="content",common=TableRef(content),version=(int)content.SourceVersion,erased=content.IsErased,
            name=content.Name==null?null:TableText(content.Name),description=content.Description==null?null:TableText(content.Description),rows=content.RowCount,columns=content.ColumnCount,
            tags=content.Payload.Select(TableStyleSnapshot).ToArray(),subclasses=content.Subclasses.Select(TableContentSnapshot).ToArray(),values=content.StoredValues.Select(TableContentSnapshot).ToArray(),style=TableRef(content.TableStyle),references=content.References.Select(TableRef).ToArray()};
        if(value is DxfStoredTableContentSubclass packet)return new {kind="subclass",name=packet.Name,tags=packet.Tags.Select(TableStyleSnapshot).ToArray()};
        if(value is DxfStoredTableContentValue scalar)return new {kind="value",type=(int)scalar.Kind,value=ContentScalar(scalar.Value),index=scalar.PayloadIndex,flags=scalar.StoredFormatFlags,unit=scalar.StoredUnitType,
            format=scalar.FormatString==null?null:TableText(scalar.FormatString),display=scalar.FormattedText==null?null:TableText(scalar.FormattedText),tags=scalar.Tags.Select(TableStyleSnapshot).ToArray()};
        if(value is DxfStoredTableContentValueEdit edit)return new {kind="edit",original=TableContentSnapshot(edit.Original),value=ContentScalar(edit.Value),display=edit.FormattedText==null?null:TableText(edit.FormattedText)};
        if(value is IEnumerable values && value is not string)return values.Cast<object?>().Select(TableContentSnapshot).ToArray();
        return TableStyleSnapshot(value);
    }
    private static object TableContentLoad(JsonElement step,DxfDocument document) {
        var tags=step.GetProperty("tags").EnumerateArray().Select(row=>new DxfTag(row[0].GetInt16(),TableTagInput(row[1]))).ToList();
        var value=(DxfStoredTableContent)Activator.CreateInstance(typeof(DxfStoredTableContent),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{document,tags,TableDecoder},null)!;
        if(!step.TryGetProperty("register",out var register)||register.GetBoolean()) {
            DxfObject parent=step.TryGetProperty("owner",out var owner)?(DxfObject)Read(owner)!:document.NamedObjects;
            if(parent is DxfDictionary dictionary)dictionary.Add(step.TryGetProperty("name",out var name)?name.GetString()!:"CONTENT_FIXTURE",value);
            else { RetainedPut(value,"Owner",parent);typeof(DxfObjectDatabase).GetMethod("Register",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(document.Objects,new object[]{value,false}); }
            if(!step.TryGetProperty("resolve",out var resolve)||resolve.GetBoolean()) {
                var lookup=typeof(DxfDocument).GetMethod("StoredTableHandleTarget",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Func<string,DxfObject> resolver=h=>(DxfObject)lookup.Invoke(document,new object[]{h})!;
                typeof(DxfStoredTableContent).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,new object[]{resolver});
            }
        }
        return value;
    }
    private static object? TableContentCall(JsonElement step,DxfStoredTableContent content) {
        var input=step.GetProperty("members");object?[]? members=input.ValueKind==JsonValueKind.Null?null:input.EnumerateArray().Select(Read).ToArray();
        int count=step.TryGetProperty("repeat",out var repeat)?repeat.GetInt32():members?.Length??0;var counters=new int[5];
        if(step.TryGetProperty("log",out var log))Values[log.GetString()!]=counters;
        string? name=(string?)Read(step.GetProperty("name")),description=(string?)Read(step.GetProperty("description"));
        DxfDatabaseObject? style=step.TryGetProperty("style",out var styleInput)?(DxfDatabaseObject?)Read(styleInput):null;
        bool nullEnumerator=step.TryGetProperty("nullEnumerator",out var nullInput)&&nullInput.GetBoolean();
        void Invoke(bool recursive) { content.ReplaceContent(name!,description!,style,members==null?null!:recursive?Array.Empty<DxfStoredTableContentValueEdit>():new TableEnumerable<DxfStoredTableContentValueEdit>(members,count,counters,Hook,nullEnumerator)); }
        void Hook(string stage) {
            if(!step.TryGetProperty("hooks",out var hooks))return;
            foreach(var hook in hooks.EnumerateArray()) {
                if(hook.GetProperty("stage").GetString()!=stage)continue;
                string kind=hook.GetProperty("kind").GetString()!;
                if(kind=="throw")throw new InvalidOperationException("Injected caller failure.");
                if(kind=="reenter"||kind=="catch-reenter"){try{Invoke(true);}catch{if(kind=="reenter")throw;counters[4]++;}}
                else {var subject=Read(hook.GetProperty("target"))!;string field=hook.GetProperty("member").GetString()!;
                    if(kind=="set")RetainedPut(subject,field,Read(hook.GetProperty("value")));
                    else {var args=Arguments(hook);subject.GetType().GetMethods().Where(m=>m.Name==field).Single(m=>Matches(m.GetParameters(),args)).Invoke(subject,args);}
                }
            }
        }
        Invoke(false);return null;
    }
}
