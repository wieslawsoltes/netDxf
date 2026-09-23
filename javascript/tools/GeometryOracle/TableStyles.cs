// Input/observation adapter only: constructors and mutations execute unchanged C#.
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
    private static object? TableRef(DxfObject? item)=>item==null?null:new {type=item.GetType().Name,handle=item.Handle,owner=item.Owner?.Handle};
    private static object TableText(string value)=>new {utf16=value.Select(c=>(int)c).ToArray()};
    private static object TableReal(double value)=>new {@double=Bits(value)};
    private static object? TableStyleSnapshot(object? value) {
        if(value==null)return null;
        if(value is DxfTableStyle style)return new {kind="style",common=TableRef(style),version=(int)style.SourceVersion,erased=style.IsErased,
            tags=style.Tags.Select(TableStyleSnapshot).ToArray(),header=TableStyleSnapshot(style.Header),rows=style.Rows.Select(TableStyleSnapshot).ToArray(),references=style.References.Select(TableRef).ToArray(),map=TableRef(style.CellStyleMap),typedMap=TableRef(style.StoredCellStyleMap)};
        if(value is DxfTableStyleHeader h)return new {kind="header",description=TableText(h.Description),flow=h.FlowDirection,flags=h.StoredFlags,horizontal=TableReal(h.HorizontalCellMargin),vertical=TableReal(h.VerticalCellMargin),title=h.SuppressTitle,heading=h.SuppressColumnHeading,version=h.StoredVersion};
        if(value is DxfTableStyleRow row)return new {kind="row",name=TableText(row.StoredTextStyleName),style=TableRef(row.TextStyle),values=TableStyleSnapshot(row.Values),borders=TableStyleSnapshot(row.Borders),data=TableStyleSnapshot(row.DataTypes),tags=row.Tags.Select(TableStyleSnapshot).ToArray()};
        if(value is DxfTableStyleRowValues scalars)return new {kind="scalars",height=TableReal(scalars.TextHeight),alignment=scalars.CellAlignment,color=scalars.StoredTextColor,fill=scalars.StoredFillColor,background=scalars.BackgroundColorEnabled};
        if(value is DxfTableStyleBorderValues border)return new {kind="border",lineweight=border.StoredLineweight,visible=border.IsVisible,color=border.StoredColor};
        if(value is DxfTableStyleRowBorders borders)return new {kind="borders",values=borders.Values.Select(TableStyleSnapshot).ToArray()};
        if(value is DxfTableStyleRowDataTypes types)return new {kind="data",data=types.StoredDataType,unit=types.StoredUnitType};
        if(value is DxfTableStyleRowEdit edit)return new {kind="edit",original=TableStyleSnapshot(edit.Original),values=TableStyleSnapshot(edit.Values),borders=TableStyleSnapshot(edit.Borders),data=TableStyleSnapshot(edit.DataTypes),style=TableRef(edit.TextStyle)};
        if(value is DxfStoredCellStyleMap map)return new {kind="map",common=TableRef(map),version=(int)map.SourceVersion,erased=map.IsErased,payload=map.Payload.Select(TableStyleSnapshot).ToArray(),entries=map.Entries.Select(TableStyleSnapshot).ToArray(),references=map.References.Select(TableRef).ToArray()};
        if(value is DxfStoredCellStyleMapEntry entry)return new {kind="entry",id=entry.Id,type=entry.StoredType,name=TableText(entry.Name),format=entry.FormatPayload.Select(TableStyleSnapshot).ToArray()};
        if(value is DxfTag tag)return new {code=tag.Code,value=tag.Value is double d?TableReal(d):tag.Value is byte[] bytes?bytes.Select(b=>(int)b).ToArray():tag.Value is string text?TableText(text):tag.Value};
        if(value is DxfObject obj)return TableRef(obj);
        if(value is string)return value;
        if(value is IEnumerable items)return items.Cast<object?>().Select(TableStyleSnapshot).ToArray();
        return value;
    }
    private static readonly Func<string,string> TableDecoder=(Func<string,string>)typeof(DxfTableStyle).GetMethod("DecodeEditedDescription",BindingFlags.Static|BindingFlags.NonPublic)!.CreateDelegate(typeof(Func<string,string>));
    private static object TableTagInput(JsonElement value) {
        if(value.ValueKind==JsonValueKind.Object&&value.TryGetProperty("handleOf",out var id)) {
            var item=(DxfObject)Values[id.GetString()!]!;string handle=item.Handle;
            if(value.TryGetProperty("lower",out var lower)&&lower.GetBoolean())handle=handle.ToLowerInvariant();
            return new string('0',value.TryGetProperty("pad",out var pad)?pad.GetInt32():0)+handle;
        }
        return Read(value)!;
    }
    private static object TableStyleLoad(JsonElement step,DxfDocument document) {
        var tags=step.GetProperty("tags").EnumerateArray().Select(row=>new DxfTag(row[0].GetInt16(),TableTagInput(row[1]))).ToList();
        Type type=step.TryGetProperty("kind",out var kind)&&kind.GetString()=="map"?typeof(DxfStoredCellStyleMap):typeof(DxfTableStyle);
        var value=(DxfDatabaseObject)Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{document,tags,TableDecoder},null)!;
        if(!step.TryGetProperty("register",out var register)||register.GetBoolean()) {
            var parent=step.TryGetProperty("owner",out var owner)?(DxfDictionary)Read(owner)!:document.NamedObjects;
            parent.Add(step.TryGetProperty("name",out var name)?name.GetString()!:"TABLE_FIXTURE",value);
            if(!step.TryGetProperty("resolve",out var resolve)||resolve.GetBoolean()) {
                var lookup=typeof(DxfDocument).GetMethod("StoredTableHandleTarget",BindingFlags.Instance|BindingFlags.NonPublic)!;
                Func<string,DxfObject> resolver=h=>(DxfObject)lookup.Invoke(document,new object[]{h})!;
                type.GetMethod("Resolve",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,type==typeof(DxfTableStyle)?new object[]{resolver,TableDecoder}:new object[]{resolver});
            }
        }
        return value;
    }
    private sealed class TableEnumerable<T> : IEnumerable<T> {
        private readonly T[] members;private readonly int count;private readonly int[] counters;private readonly Action<string> hook;private readonly bool nullEnumerator;
        internal TableEnumerable(object?[] members,int count,int[] counters,Action<string> hook,bool nullEnumerator){this.nullEnumerator=nullEnumerator;this.members=members.Select(v=>(T)v!).ToArray();this.count=count;this.counters=counters;this.hook=hook;}
        public IEnumerator<T> GetEnumerator(){counters[0]++;hook("get");return nullEnumerator?null!:new Iterator(this);}
        IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
        private sealed class Iterator : IEnumerator<T> {
            private readonly TableEnumerable<T> owner;private int at=-1;
            internal Iterator(TableEnumerable<T> owner){this.owner=owner;}
            public bool MoveNext(){owner.counters[1]++;owner.hook("move");return ++at<owner.count;}
            public T Current {get{owner.counters[2]++;owner.hook("current");return owner.members[at%owner.members.Length];}}
            object? IEnumerator.Current=>Current;
            public void Reset()=>throw new NotSupportedException();
            public void Dispose(){owner.counters[3]++;owner.hook("dispose");}
        }
    }
    private static object? TableStyleCall(JsonElement step,object? target) {
        var input=step.GetProperty("members");object?[]? members=input.ValueKind==JsonValueKind.Null?null:input.EnumerateArray().Select(Read).ToArray();
        int count=step.TryGetProperty("repeat",out var repeat)?repeat.GetInt32():members?.Length??0;var counters=new int[5];
        if(step.TryGetProperty("log",out var log))Values[log.GetString()!]=counters;
        var header=step.TryGetProperty("header",out var headerInput)?(DxfTableStyleHeader?)Read(headerInput):null;
        string action=step.GetProperty("action").GetString()!;bool nullEnumerator=step.TryGetProperty("nullEnumerator",out var nullInput)&&nullInput.GetBoolean();
        object? Invoke(bool recursive) {
            object?[]? current=recursive?Array.Empty<object>():members;int length=recursive?0:count;
            if(action=="style"){((DxfTableStyle)target!).ReplaceStyle(header!,current==null?null!:recursive?Array.Empty<DxfTableStyleRowEdit>():new TableEnumerable<DxfTableStyleRowEdit>(current,length,counters,Hook,nullEnumerator));return null;}
            if(action=="map"){((DxfStoredCellStyleMap)target!).ReplaceEntryNames(current==null?null!:recursive?Array.Empty<string>():new TableEnumerable<string>(current,length,counters,Hook,nullEnumerator));return null;}
            return new DxfTableStyleRowBorders(current==null?null!:new TableEnumerable<DxfTableStyleBorderValues>(current,length,counters,Hook,nullEnumerator));
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
                    else {var args=Arguments(hook);subject.GetType().GetMethods().Where(m=>m.Name==name).Single(m=>Matches(m.GetParameters(),args)).Invoke(subject,args);}
                }
            }
        }
        return Invoke(false);
    }
}
