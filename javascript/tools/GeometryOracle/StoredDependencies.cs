// Observation-only retained fixtures for unchanged native models. Not a DXF reader.
using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.IO;
internal static partial class Program {
    private static object? DependencyText(string? value) => value==null?null:TableText(value);
    private static object? DependencySnapshot(object? value) {
        if(value==null)return null;
        object? Database(DxfDatabaseObject item) => item.Database?.Document.Handle;
        if(value is DxfStoredField field)return new {kind="field",common=TableRef(field),version=(int)field.SourceVersion,erased=field.IsErased,database=Database(field),
            payload=field.Payload.Select(TableStyleSnapshot).ToArray(),evaluator=DependencyText(field.EvaluatorId),code=DependencyText(field.FieldCode),children=field.Children.Select(TableRef).ToArray(),
            objects=field.ReferencedObjects.Select(TableRef).ToArray(),references=field.References.Select(TableRef).ToArray(),declared=((IEnumerable<DxfDatabaseObject>)RetainedGet(field,"DeclaredOwnedObjects")!).Select(TableRef).ToArray()};
        if(value is DxfStoredDimAssocPoint point)return new {kind="point",index=point.PointIndex,osnap=point.OsnapType,handle=DependencyText((string?)RetainedGet(point,"GeometryHandle")),
            geometry=TableRef(point.Geometry),subentity=point.SubentityType,marker=point.MarkerIndex,parameter=TableReal(point.NearParameter),point=new[]{TableReal(point.Point.X),TableReal(point.Point.Y),TableReal(point.Point.Z)}};
        if(value is DxfStoredDimAssoc association)return new {kind="association",common=TableRef(association),version=(int)association.SourceVersion,erased=association.IsErased,database=Database(association),
            tags=association.Tags.Select(TableStyleSnapshot).ToArray(),dimension=TableRef(association.Dimension),mask=association.AssociativityMask,trans=association.IsTransSpace,rotated=association.RotatedDimensionType,
            points=association.PointReferences.Select(DependencySnapshot).ToArray(),references=association.References.Select(TableRef).ToArray()};
        if(value is DxfStoredSunStudy study)return new {kind="study",common=TableRef(study),version=(int)study.SourceVersion,erased=study.IsErased,database=Database(study),
            payload=study.Payload.Select(TableStyleSnapshot).ToArray(),version0=study.Version,name=DependencyText(study.Name),description=DependencyText(study.Description),output=study.OutputType,
            sheet=DependencyText(study.SheetSetName),subset=DependencyText(study.SheetSubsetName),useSubset=study.UseSubset,selectDates=study.SelectDates,dateCount=study.DateCount,selectRange=study.SelectDateRange,
            start=study.StartTime,end=study.EndTime,interval=study.Interval,hours=study.RawHourFlags.ToArray(),page=TableRef(study.PageSetup),view=TableRef(study.View),visual=TableRef(study.VisualStyle),style=TableRef(study.TextStyle),references=study.References.Select(TableRef).ToArray()};
        if(value is DxfObject item)return TableRef(item);
        if(value is Vector3 vector)return new[]{TableReal(vector.X),TableReal(vector.Y),TableReal(vector.Z)};
        if(value is DxfTag tag)return TableStyleSnapshot(tag);
        if(value is IEnumerable items && value is not string)return items.Cast<object?>().Select(DependencySnapshot).ToArray();
        return value;
    }
    private static object? DependencyInput(JsonElement value) => TableTagInput(value);
    private static object DependencyNew(JsonElement step,DxfDocument? source) {
        string kind=step.GetProperty("kind").GetString()!;
        if(kind=="point")return Activator.CreateInstance(typeof(DxfStoredDimAssocPoint),RetainedFlags,null,step.GetProperty("args").EnumerateArray().Select(DependencyInput).ToArray(),null)!;
        var tags=step.GetProperty("tags").EnumerateArray().Select(row=>new DxfTag(row[0].GetInt16(),DependencyInput(row[1])!)).ToList();
        if(kind=="field")return Activator.CreateInstance(typeof(DxfStoredField),RetainedFlags,null,new object?[]{source,tags,Read(step.GetProperty("evaluator")),Read(step.GetProperty("code"))},null)!;
        if(kind=="association")return Activator.CreateInstance(typeof(DxfStoredDimAssoc),RetainedFlags,null,new object?[]{source,tags,DependencyInput(step.GetProperty("dimension")),step.GetProperty("mask").GetInt32(),step.GetProperty("trans").GetInt16(),step.GetProperty("rotated").GetInt16(),step.GetProperty("points").EnumerateArray().Select(v=>(DxfStoredDimAssocPoint)Read(v)!).ToList()},null)!;
        if(kind=="study")return Activator.CreateInstance(typeof(DxfStoredSunStudy),RetainedFlags,null,new object?[]{source,tags,TableDecoder},null)!;
        throw new ArgumentException("Unknown dependency fixture kind.");
    }
    private static void DependencyRegister(JsonElement step,DxfDatabaseObject target) {
        var document=(DxfDocument)Read(step.GetProperty("document"))!;var owner=(DxfObject?)Read(step.GetProperty("owner"));
        RetainedPut(target,"Owner",owner);
        if(step.TryGetProperty("handle",out var handle))RetainedPut(target,"Handle",DependencyInput(handle));
        typeof(DxfObjectDatabase).GetMethod("Register",RetainedFlags)!.Invoke(document.Objects,new object[]{target,step.TryGetProperty("preserve",out var preserve)&&preserve.GetBoolean()});
        if(owner is DxfDictionary parent && step.TryGetProperty("name",out var name) && name.ValueKind!=JsonValueKind.Null)
            typeof(DxfDictionary).GetMethod("AddLoaded",RetainedFlags)!.Invoke(parent,new object[]{name.GetString()!,target,!step.TryGetProperty("hard",out var hard)||hard.GetBoolean()});
    }
    private static void DependencyResolve(JsonElement step,object target) {
        var document=(DxfDocument)Read(step.GetProperty("document"))!;var trace=new List<string>();if(step.TryGetProperty("trace",out var traceId))Values[traceId.GetString()!]=trace;
        var overrides=step.TryGetProperty("overrides",out var overridesInput)?overridesInput.EnumerateArray().Select(p=>Tuple.Create((string?)DependencyInput(p[0]),(DxfObject?)Read(p[1]))).ToArray():Array.Empty<Tuple<string?,DxfObject?>>();
        var missing=step.TryGetProperty("missing",out var missingInput)?missingInput.EnumerateArray().Select(v=>(string?)DependencyInput(v)).ToArray():Array.Empty<string?>();
        DxfObject? Lookup(string value) {
            trace.Add(value);
            if(step.TryGetProperty("hooks",out var hooks))foreach(var hook in hooks.EnumerateArray())if(hook.GetProperty("at").GetInt32()==trace.Count) {
                string kind=hook.GetProperty("kind").GetString()!;
                if(kind=="throw")throw new InvalidOperationException("Injected source resolver failure.");
                var subject=Read(hook.GetProperty("target"))!;string member=hook.GetProperty("member").GetString()!;
                if(kind=="set")RetainedPut(subject,member,Read(hook.GetProperty("value")));
                else {var args=Arguments(hook);subject.GetType().GetMethods().Where(m=>m.Name==member).Single(m=>Matches(m.GetParameters(),args)).Invoke(subject,args);}
            }
            if(missing.Contains(value))return null;
            var entry=overrides.FirstOrDefault(p=>p.Item1==value);if(entry!=null)return entry.Item2;
            if(step.TryGetProperty("ownerHeld",out var held)&&held.GetBoolean())return (DxfObject?)typeof(DxfDocument).GetMethod("StoredTableHandleTarget",RetainedFlags)!.Invoke(document,new object[]{value});
            return document.GetObjectByHandle(value);
        }
        Func<string,DxfObject?> resolver=Lookup;
        if(target is DxfStoredField)target.GetType().GetMethod("Resolve",RetainedFlags)!.Invoke(target,new object[]{
            step.GetProperty("children").EnumerateArray().Select(v=>(string)DependencyInput(v)!).ToList(),step.GetProperty("objects").EnumerateArray().Select(v=>(string)DependencyInput(v)!).ToList(),resolver});
        else target.GetType().GetMethod("Resolve",RetainedFlags)!.Invoke(target,new object[]{resolver});
    }
    private static object DependencyValidate(JsonElement step,object target) {
        var errors=new List<string>();target.GetType().GetMethod("ValidateDatabaseSchema",RetainedFlags)!.Invoke(target,new object?[]{Read(step.GetProperty("database")),errors});return errors;
    }
    private static object? DependencyInternal(JsonElement step,object target) {
        string name=step.GetProperty("member").GetString()!;return target.GetType().GetMethod(name,RetainedFlags)!.Invoke(target,Arguments(step));
    }
    private static object DependencyCodec(JsonElement step) {
        using var input=new MemoryStream(step.GetProperty("bytes").EnumerateArray().Select(v=>v.GetByte()).ToArray());
        input.Position=step.TryGetProperty("origin",out var origin)?origin.GetInt32():0;
        using var binary=new BinaryReader(input,Encoding.UTF8,true);
        var type=typeof(DxfDocument).Assembly.GetType("netDxf.IO.BinaryCodeValueReader")!;
        var reader=Activator.CreateInstance(type,RetainedFlags,null,new object[]{binary,Encoding.UTF8,step.TryGetProperty("legacy",out var legacy)&&legacy.GetBoolean()},null)!;
        string? error=null,message=null;
        try {int count=step.TryGetProperty("reads",out var reads)?reads.GetInt32():1;for(int i=0;i<count;i++)type.GetMethod("Next")!.Invoke(reader,null);}
        catch(Exception e){while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;error=e.GetType().Name;message=e.Message;}
        return new {error,message,code=(short)type.GetProperty("Code")!.GetValue(reader)!,position=(long)type.GetProperty("CurrentPosition")!.GetValue(reader)!,canRead=input.CanRead};
    }

}
