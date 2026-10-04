// Observation-only controls for unchanged C# TABLEGEOMETRY. No expected outputs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Objects;
using netDxf.IO;
internal static partial class Program {
    private static object GeometryPoint(Vector3 value) => new[] { TableReal(value.X), TableReal(value.Y), TableReal(value.Z) };
    private static object? TableGeometrySnapshot(object? value) {
        if(value==null)return null;
        if(value is DxfStoredTableGeometry geometry)return new {kind="geometry",common=TableRef(geometry),version=(int)geometry.SourceVersion,erased=geometry.IsErased,rows=geometry.RowCount,columns=geometry.ColumnCount,tags=geometry.Payload.Select(TableStyleSnapshot).ToArray(),cells=geometry.Cells.Select(TableGeometrySnapshot).ToArray(),references=geometry.References.Select(TableRef).ToArray()};
        if(value is DxfStoredTableGeometryCell cell)return new {kind="cell",flags=cell.GeometryDataFlags,width=TableReal(cell.WidthWithGap),height=TableReal(cell.HeightWithGap),reference=TableRef(cell.GeometryReference),geometry=cell.Geometry.Select(TableGeometrySnapshot).ToArray()};
        if(value is DxfStoredTableCellGeometry content)return new {kind="content",topLeft=GeometryPoint(content.TopLeftDistance),center=GeometryPoint(content.CenterDistance),width=TableReal(content.Width),height=TableReal(content.Height),contentWidth=TableReal(content.ContentWidth),contentHeight=TableReal(content.ContentHeight),value95=content.StoredValue95};
        if(value is IEnumerable items && value is not string)return items.Cast<object?>().Select(TableGeometrySnapshot).ToArray();
        return TableStyleSnapshot(value);
    }
    private static object TableGeometryLoad(JsonElement step,DxfDocument document) {
        var tags=step.GetProperty("tags").EnumerateArray().Select(row=>new DxfTag(row[0].GetInt16(),TableTagInput(row[1]))).ToList();
        var value=(DxfStoredTableGeometry)Activator.CreateInstance(typeof(DxfStoredTableGeometry),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{document,tags},null)!;
        if(!step.TryGetProperty("register",out var register)||register.GetBoolean()) {
            var parent=step.TryGetProperty("owner",out var owner)?(DxfDictionary)Read(owner)!:document.NamedObjects;
            parent.Add(step.TryGetProperty("name",out var name)?name.GetString()!:"GEOMETRY_FIXTURE",value);
            if(!step.TryGetProperty("resolve",out var resolve)||resolve.GetBoolean()) {
                var lookup=typeof(DxfDocument).GetMethod("StoredTableHandleTarget",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Func<string,DxfObject> resolver=h=>(DxfObject)lookup.Invoke(document,new object[]{h})!;
                typeof(DxfStoredTableGeometry).GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,new object[]{resolver});
            }
        }
        return value;
    }
    private static object? TableGeometryCall(JsonElement step,object? target) {
        var input=step.GetProperty("members");object?[]? members=input.ValueKind==JsonValueKind.Null?null:input.EnumerateArray().Select(Read).ToArray();
        int count=step.TryGetProperty("repeat",out var repeat)?repeat.GetInt32():members?.Length??0;var counters=new int[5];
        if(step.TryGetProperty("log",out var log))Values[log.GetString()!]=counters;
        string action=step.GetProperty("action").GetString()!;bool nullEnumerator=step.TryGetProperty("nullEnumerator",out var nullInput)&&nullInput.GetBoolean();
        var args=Arguments(step);
        object? Invoke(bool recursive) {
            object?[]? current=recursive?Array.Empty<object>():members;int length=recursive?0:count;
            if(action=="cell")return new DxfStoredTableGeometryCell((int)args[0]!, (double)args[1]!, (double)args[2]!, (DxfObject)args[3]!,current==null?null!:new TableEnumerable<DxfStoredTableCellGeometry>(current,length,counters,Hook,nullEnumerator));
            ((DxfStoredTableGeometry)target!).ReplaceGeometry(step.GetProperty("rows").GetInt32(),step.GetProperty("columns").GetInt32(),current==null?null!:recursive?Array.Empty<DxfStoredTableGeometryCell>():new TableEnumerable<DxfStoredTableGeometryCell>(current,length,counters,Hook,nullEnumerator));return null;
        }
        void Hook(string stage) {
            if(!step.TryGetProperty("hooks",out var hooks))return;
            foreach(var hook in hooks.EnumerateArray()) {
                if(hook.GetProperty("stage").GetString()!=stage)continue;
                string kind=hook.GetProperty("kind").GetString()!;
                if(kind=="throw")throw new InvalidOperationException("Injected caller failure.");
                if(kind=="reenter"||kind=="catch-reenter"){try{Invoke(true);}catch{if(kind=="reenter")throw;counters[4]++;}}
                else {var subject=Read(hook.GetProperty("target"))!;string name=hook.GetProperty("member").GetString()!;
                    if(kind=="set")RetainedPut(subject,name,Read(hook.GetProperty("value")));
                    else {var values=Arguments(hook);subject.GetType().GetMethods().Where(m=>m.Name==name).Single(m=>Matches(m.GetParameters(),values)).Invoke(subject,values);}
                }
            }
        }
        return Invoke(false);
    }
}
