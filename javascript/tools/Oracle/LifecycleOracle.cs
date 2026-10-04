// Direct operations on unchanged pinned production types. No JS-derived expected values.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.Collections;
using netDxf.Tables;
using netDxf.IO;
using System.IO;
internal static class LifecycleOracle
{
    private sealed class Carrier : DxfObject { internal Carrier(string name) : base(name) { } }
    internal static object Execute(JsonElement input)
    {
        var answers = new List<object>();
        foreach (var scenario in input.GetProperty("scenarios").EnumerateArray())
        {
            var refs = new Dictionary<string,object?>(); var events = new List<string>(); string mode = ""; string? collisionTarget = null;
            IEnumerator<KeyValuePair<string,XData>>? iterator = null;
            object? Ref(string key) => refs[key];
            XDataDictionary Dict(string key) => Ref(key) is DxfObject obj ? obj.XData : (XDataDictionary)Ref(key)!;
            ApplicationRegistry Registry(string name)
            {
                var registry = new ApplicationRegistry(name);
                registry.NameChanged += (sender,e) => {
                    events.Add("name:"+sender.Name+":"+e.OldValue+":"+e.NewValue);
                    if(mode=="change-event") e.NewValue="IGNORED";
                    if(mode=="throw-name") throw new NotSupportedException("observer");
                    if(mode=="collision") Dict(collisionTarget!).Add(new XData(new ApplicationRegistry(e.NewValue)));
                    if(mode=="detach") Dict(collisionTarget!).Remove(sender.Name);
                };
                return registry;
            }
            void Watch(string id, object value) {
                if(value is DxfObject obj) {
                    obj.XDataAddAppReg += (_,e) => {events.Add(id+":add:"+e.Item.Name);if(mode=="throw-add")throw new NotSupportedException("observer");};
                    obj.XDataRemoveAppReg += (_,e) => {events.Add(id+":remove:"+e.Item.Name);if(mode=="throw-remove")throw new NotSupportedException("observer");};
                } else if (value is XDataDictionary dict) {
                    dict.AddAppReg += (_,e) => {events.Add(id+":add:"+e.Item.Name);if(mode=="throw-add")throw new NotSupportedException("observer");};
                    dict.RemoveAppReg += (_,e) => {events.Add(id+":remove:"+e.Item.Name);if(mode=="throw-remove")throw new NotSupportedException("observer");};
                }
            }
            object Snapshot()
            {
                var ids = new Dictionary<object,int>(ReferenceEqualityComparer.Instance); var pending = new List<object>();
                int? Id(object? value) { if(value==null)return null;if(!ids.TryGetValue(value,out int id)){id=ids.Count;ids.Add(value,id);pending.Add(value);}return id; }
                var names=refs.Select(p=>new object?[]{p.Key,Id(p.Value)}).ToArray(); var nodes = new List<object>();
                for(int i=0;i<pending.Count;i++) {
                    object item=pending[i];
                    if(item is XDataDictionary dict)nodes.Add(new {type="dictionary",entries=dict.Select(p=>new object?[]{p.Key,Id(p.Value)}).ToArray()});
                    else if(item is XData data) nodes.Add(new {type="data",registry=Id(data.ApplicationRegistry),container=Id(typeof(XData).GetProperty("Container",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(data)),records=data.XDataRecord.Select(r=>Id(r)).ToArray()});
                    else if(item is XDataRecord record) nodes.Add(new {type="record",code=(int)record.Code,value=record.Value is byte[] b?(object)new {bytes=Id(b)}:record.Value is double d?new {bits=unchecked((ulong)BitConverter.DoubleToInt64Bits(d)).ToString("X16")}:record.Value});
                    else if(item is byte[] bytes)nodes.Add(new {type="bytes",values=bytes.Select(b=>(int)b).ToArray()});
                    else if(item is DxfObject obj)nodes.Add(new {type=obj is ApplicationRegistry?"registry":"carrier",code=obj.CodeName,handle=obj.Handle,owner=Id(obj.Owner),extension=Id(obj.ExtensionDictionary),reactors=obj.PersistentReactors.Select(o=>Id(o)).ToArray(),name=(obj as ApplicationRegistry)?.Name,reserved=(obj as ApplicationRegistry)?.IsReserved,xdata=Id(obj.XData)});
                    else throw new InvalidOperationException("Unmapped protocol result " + item.GetType());
                }
                return new { names, nodes };
            }
            var steps = new List<object>();
            foreach(var op in scenario.GetProperty("operations").EnumerateArray())
            {
                events.Clear();mode=op.TryGetProperty("mode",out var m)?m.GetString()!:"";collisionTarget=op.TryGetProperty("collisionTarget",out var ct)?ct.GetString():null;
                string? S(string key) => op.TryGetProperty(key,out var value)?value.GetString():null;
                int N(string key) => op.GetProperty(key).GetInt32();
                string target=S("target")??"";object? result=null;string? error=null,param=null;
                void Store(object value){refs[S("id")!]=value;Watch(S("id")!,value);}
                try {switch(S("method")) {
                    case "Registry":Store(Registry(S("name")!));break;
                    case "Carrier":Store(new Carrier(S("name")!));break;
                    case "Dictionary":Store(new XDataDictionary(op.TryGetProperty("capacity",out var c)?c.GetInt32():0));break;
                    case "Data":Store(new XData((ApplicationRegistry)Ref(S("registry")!)!));break;
                    case "Record":{
                        var code=(XDataCode)N("code");var value=op.GetProperty("value");
                        object decoded=code==XDataCode.BinaryData?value.EnumerateArray().Select(v=>(byte)v.GetInt32()).ToArray():
                            code==XDataCode.Int16?(object)value.GetInt16():code==XDataCode.Int32?value.GetInt32():value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetDouble();
                        ((XData)Ref(target)!).XDataRecord.Add(new XDataRecord(code,decoded));break;
                    }
                    case "Add":Dict(target).Add((XData)Ref(S("data")!)!);break;
                    case "AddKey":((IDictionary<string,XData>)Dict(target)).Add(S("key")!, (XData)Ref(S("data")!)!);break;
                    case "Set":Dict(target)[S("key")!]=(XData)Ref(S("data")!)!;break;
                    case "Remove":result=Dict(target).Remove(S("key")!);break;
                    case "RemovePair":result=((ICollection<KeyValuePair<string,XData>>)Dict(target)).Remove(new(S("key")!,(XData)Ref(S("data")!)!));break;
                    case "ContainsPair":result=((ICollection<KeyValuePair<string,XData>>)Dict(target)).Contains(new(S("key")!,(XData)Ref(S("data")!)!));break;
                    case "Clear":Dict(target).Clear();break;
                    case "Rename":((ApplicationRegistry)Ref(target)!).Name=S("name")!;break;
                    case "Clone":Store(Ref(target) is ApplicationRegistry r?(op.TryGetProperty("name",out var n)?r.Clone(n.GetString()!):r.Clone()):((XData)Ref(target)!).Clone());break;
                    case "Get":Store(Dict(target)[S("key")!]);break;
                    case "Has":result=Dict(target).ContainsAppId(S("key")!);break;
                    case "Try":result=Dict(target).TryGetValue(S("key")!,out var _);break;
                    case "NullData":refs[S("id")!]=null;break;
                    case "Mutate":((byte[])((XData)Ref(target)!).XDataRecord[N("index")].Value)[N("at")]=(byte)N("value");break;
                    case "RecordsClear":((XData)Ref(target)!).XDataRecord.Clear();break;
                    case "Canonicalize":typeof(XDataDictionary).GetMethod("CanonicalizeApplicationRegistry",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Dict(target),new object?[]{S("key"),Ref(S("registry")!)});break;
                    case "ReplaceBinding":typeof(XDataDictionary).GetMethod("ReplaceForBinding",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Dict(target),new object?[]{S("key"),Ref(S("data")!)});break;
                    case "Enumerate":iterator=Dict(target).GetEnumerator();result=new {key=iterator.Current.Key,empty=iterator.Current.Value==null};break;
                    case "Next":result=new {moved=iterator!.MoveNext(),key=iterator.Current.Key,empty=iterator.Current.Value==null};break;
                    case "Reset":iterator!.Reset();result=new {key=iterator.Current.Key,empty=iterator.Current.Value==null};break;
                    case "Compare":result=((TableObject)Ref(target)!).CompareTo((TableObject)Ref(S("other")!)!);break;
                    case "Equal":result=Ref(target)!.Equals(Ref(S("other")!));break;
                    case "References":result=new {has=((ApplicationRegistry)Ref(target)!).HasReferences(),isNull=((ApplicationRegistry)Ref(target)!).GetReferences()==null};break;
                    case "AssignHandle":result=((long)typeof(DxfObject).GetMethod("AssignHandle",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Ref(target),new object[]{long.Parse(S("value")!)})!).ToString();break;
                    case "Emit":{
                        var data=(XData)Ref(target)!;
                        var tags=new List<DxfTag>{new(0,"SECTION"),new(2,"HEADER"),new(9,"$ACADVER"),new(1,"AC1032"),new(0,"ENDSEC"),new(0,"SECTION"),new(2,"ENTITIES"),new(0,"POINT"),new(1001,data.ApplicationRegistry.Name)};
                        foreach(var record in data.XDataRecord)tags.Add(new DxfTag((short)record.Code,record.Value));tags.Add(new DxfTag(0,"ENDSEC"));tags.Add(new DxfTag(0,"EOF"));var doc=DxfRawDocument.Create(tags);using var text=new MemoryStream();using var binary=new MemoryStream();doc.Save(text,false);doc.Save(binary,true);result=new {text=Convert.ToBase64String(text.ToArray()),binary=Convert.ToBase64String(binary.ToArray())};break;
                    }
                    default:throw new InvalidOperationException("Unknown test operation.");
                }} catch(Exception ex){while(ex is TargetInvocationException && ex.InnerException!=null)ex=ex.InnerException;error=ex.GetType().Name;param=(ex as ArgumentException)?.ParamName;}
                steps.Add(new {result,error,param,events=events.ToArray(),graph=Snapshot()});
            }
            answers.Add(steps);
        }
        return answers;
    }
}
