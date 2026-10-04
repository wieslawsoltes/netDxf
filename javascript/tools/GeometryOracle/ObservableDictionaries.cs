// Test-only observations of the unchanged C# generic collection, not expected outputs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using netDxf;
using netDxf.Collections;

internal static partial class Program
{
    private sealed class DictionaryProbe
    {
        public string Id = "", Group = ""; public int Hash; public bool Reflexive = true;
        public override bool Equals(object? other) => other is DictionaryProbe p && Reflexive && Group == p.Group;
        public override int GetHashCode() => Hash;
    }
    private static object? DictionaryWire(object? value) => value switch {
        null => null,
        DictionaryProbe p => new { id=p.Id, group=p.Group, hash=p.Hash, reflexive=p.Reflexive },
        Vector2 p => new { vector2=new[] {Bits(p.X),Bits(p.Y)} },
        double d => new { @double=Bits(d) },
        _ => value
    };
    private static object DictionaryPair<K,V>(KeyValuePair<K,V> pair) => new { key=DictionaryWire(pair.Key), value=DictionaryWire(pair.Value) };
    private sealed class DictionaryComparer<T> : IEqualityComparer<T>
    {
        public string Mode = ""; public List<string> Calls = new();
        public bool Equals(T? a, T? b) {
            Calls.Add("equals");
            if(Mode=="throw-equals")throw new InvalidOperationException("injected");
            if(Mode=="ignore-case")return StringComparer.OrdinalIgnoreCase.Equals(a as string,b as string);
            if(Mode=="modulo")return (int)(object)a! % 5 == (int)(object)b! % 5;
            return EqualityComparer<T>.Default.Equals(a!,b!);
        }
        public int GetHashCode(T item) {
            Calls.Add("hash");
            if(Mode=="throw-hash")throw new InvalidOperationException("injected");
            if(Mode=="ignore-case")return StringComparer.OrdinalIgnoreCase.GetHashCode((string)(object)item!);
            if(Mode=="modulo")return (int)(object)item! % 5;
            return 0;
        }
    }
    private static object ObservableDictionaryRequest(JsonElement input)
    {
        string key=input.GetProperty("keyType").GetString()!,value=input.GetProperty("valueType").GetString()!;
        Type TypeOf(string type) => type switch {"string"=>typeof(string),"int"=>typeof(int),"double"=>typeof(double),"probe"=>typeof(DictionaryProbe),"Vector2"=>typeof(Vector2),_=>typeof(object)};
        return typeof(Program).GetMethod(nameof(ObserveDictionary),System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!
            .MakeGenericMethod(TypeOf(key),TypeOf(value)).Invoke(null,new object[]{input})!;
    }
    private static object ObserveDictionary<K,V>(JsonElement input) where K:notnull
    {
        var pool=new Dictionary<string,object?>();
        object? Read(JsonElement value, Type type) {
            if(value.ValueKind==JsonValueKind.Null)return null;
            if(value.ValueKind==JsonValueKind.String)return new string(value.GetString()!.ToCharArray());
            if(value.ValueKind==JsonValueKind.Number)return type==typeof(int)?(object)value.GetInt32():value.GetDouble();
            if(value.ValueKind==JsonValueKind.True||value.ValueKind==JsonValueKind.False)return value.GetBoolean();
            if(value.TryGetProperty("ref",out var reference))return pool[reference.GetString()!];
            if(value.TryGetProperty("int",out var integer))return integer.GetInt32();
            if(value.TryGetProperty("double",out var number))return FromBits(number.GetString()!);
            if(value.TryGetProperty("string",out var text))return new string(text.GetString()!.ToCharArray());
            if(value.TryGetProperty("vector2",out var vector))return new Vector2(vector[0].GetDouble(),vector[1].GetDouble());
            if(value.TryGetProperty("probe",out var probe))return new DictionaryProbe {Id=probe.GetProperty("id").GetString()!,Group=probe.GetProperty("group").GetString()!,Hash=probe.GetProperty("hash").GetInt32(),Reflexive=!probe.TryGetProperty("reflexive",out var reflexive)||reflexive.GetBoolean()};
            throw new InvalidOperationException("Unknown dictionary input.");
        }
        if(input.TryGetProperty("pool",out var definitions))foreach(var item in definitions.EnumerateArray())pool[item.GetProperty("id").GetString()!]=Read(item.GetProperty("value"),typeof(object));
        K Key(JsonElement op) => (K)Read(op.GetProperty("key"),typeof(K))!;
        V Value(JsonElement op) => (V)Read(op.GetProperty("value"),typeof(V))!;
        int capacity=input.TryGetProperty("capacity",out var initial)?initial.GetInt32():0;
        string constructor=input.TryGetProperty("constructor",out var ctor)?ctor.GetString()!:"capacity-comparer";
        var comparer=input.TryGetProperty("comparer",out var comparison)?new DictionaryComparer<K>{Mode=comparison.GetString()!}:null;
        var results=new List<object>();ObservableDictionary<K,V> dictionary;
        try { dictionary=constructor switch {"default"=>new(),"capacity"=>new(capacity),"comparer"=>new(comparer),_=>new(capacity,comparer)}; }
        catch(Exception e){return new{constructorError=e.GetType().Name,param=(e as ArgumentException)?.ParamName};}
        var pairs=(ICollection<KeyValuePair<K,V>>)dictionary;var keys=dictionary.Keys;var values=dictionary.Values;
        var events=new List<object>();string mode="";JsonElement active=default;bool reentered=false;
        IEnumerator<KeyValuePair<K,V>>? iterator=null;IEnumerator<K>? keyIterator=null;IEnumerator<V>? valueIterator=null;string iteratorKind="pairs";
        void Hook(string name,ObservableDictionaryEventArgs<K,V> args) {
            events.Add(new{name,item=DictionaryPair(args.Item),count=dictionary.Count,cancel=args.Cancel});
            if(mode=="cancel-add"&&name=="before-add"||mode=="cancel-remove"&&name=="before-remove"||mode=="cancel-even-remove"&&name=="before-remove"&&Convert.ToInt32(args.Item.Key)%2==0)args.Cancel=true;
            if(mode=="throw-"+name)throw new NotSupportedException("injected");
            if(!reentered&&active.TryGetProperty("reenter",out var reentry)&&reentry.GetProperty("event").GetString()==name){reentered=true;Operate(reentry);}
        }
        dictionary.BeforeAddItem+=(_,e)=>Hook("before-add",e);dictionary.AddItem+=(_,e)=>Hook("add",e);
        dictionary.BeforeRemoveItem+=(_,e)=>Hook("before-remove",e);dictionary.RemoveItem+=(_,e)=>Hook("remove",e);
        if(input.TryGetProperty("uncancel",out var uncancel)&&uncancel.GetBoolean()) {
            dictionary.BeforeAddItem+=(_,e)=>{events.Add(new{name="last-before-add",item=DictionaryPair(e.Item),count=dictionary.Count,cancel=e.Cancel});e.Cancel=false;};
            dictionary.BeforeRemoveItem+=(_,e)=>{events.Add(new{name="last-before-remove",item=DictionaryPair(e.Item),count=dictionary.Count,cancel=e.Cancel});e.Cancel=false;};
        }
        object? Current() => iteratorKind=="keys"?DictionaryWire(keyIterator!.Current):iteratorKind=="values"?DictionaryWire(valueIterator!.Current):DictionaryPair(iterator!.Current);
        object? Operate(JsonElement op) {
            string method=op.GetProperty("method").GetString()!,kind=op.TryGetProperty("view",out var view)?view.GetString()!:"pairs";
            switch(method) {
                case "Add": dictionary.Add(Key(op),Value(op));break;
                case "AddPair": pairs.Add(new(Key(op),Value(op)));break;
                case "Set": dictionary[Key(op)]=Value(op);break;
                case "Get": return DictionaryWire(dictionary[Key(op)]);
                case "Remove": return dictionary.Remove(Key(op));
                case "RemovePair": return pairs.Remove(new(Key(op),Value(op)));
                case "ContainsPair": return pairs.Contains(new(Key(op),Value(op)));
                case "ContainsKey": return dictionary.ContainsKey(Key(op));
                case "ContainsValue": return dictionary.ContainsValue(Value(op));
                case "TryGetValue": var found=dictionary.TryGetValue(Key(op),out var item);return new{found,value=DictionaryWire(item)};
                case "Clear": dictionary.Clear();break;
                case "Enumerate": iteratorKind=kind;if(kind=="keys")keyIterator=keys.GetEnumerator();else if(kind=="values")valueIterator=values.GetEnumerator();else iterator=dictionary.GetEnumerator();return Current();
                case "Current": return Current();
                case "MoveNext": var moved=iteratorKind=="keys"?keyIterator!.MoveNext():iteratorKind=="values"?valueIterator!.MoveNext():iterator!.MoveNext();return new{moved,current=Current()};
                case "Reset": if(iteratorKind=="keys")keyIterator!.Reset();else if(iteratorKind=="values")valueIterator!.Reset();else iterator!.Reset();return Current();
                case "Dispose": if(iteratorKind=="keys")keyIterator!.Dispose();else if(iteratorKind=="values")valueIterator!.Dispose();else iterator!.Dispose();break;
                case "ViewAdd": if(kind=="keys")keys.Add(Key(op));else values.Add(Value(op));break;
                case "ViewRemove": return kind=="keys"?keys.Remove(Key(op)):values.Remove(Value(op));
                case "ViewClear": if(kind=="keys")keys.Clear();else values.Clear();break;
                case "ViewContains": return kind=="keys"?keys.Contains(Key(op)):values.Contains(Value(op));
                case "CopyTo": {
                    var length=op.GetProperty("length");int index=op.GetProperty("index").GetInt32();
                    if(kind=="keys"){K[]? a=length.ValueKind==JsonValueKind.Null?null:new K[length.GetInt32()];keys.CopyTo(a!,index);return a!.Select(x=>DictionaryWire(x)).ToArray();}
                    if(kind=="values"){V[]? a=length.ValueKind==JsonValueKind.Null?null:new V[length.GetInt32()];values.CopyTo(a!,index);return a!.Select(x=>DictionaryWire(x)).ToArray();}
                    KeyValuePair<K,V>[]? b=length.ValueKind==JsonValueKind.Null?null:new KeyValuePair<K,V>[length.GetInt32()];pairs.CopyTo(b!,index);return b!.Select(x=>DictionaryPair(x)).ToArray();
                }
                case "Mutate": {
                    var id=op.GetProperty("id").GetString()!;var objectValue=pool[id];
                    if(objectValue is DictionaryProbe probe){if(op.TryGetProperty("hash",out var hash))probe.Hash=hash.GetInt32();if(op.TryGetProperty("group",out var group))probe.Group=group.GetString()!;}
                    else if(objectValue is Vector2 vector){vector.X=op.GetProperty("x").GetDouble();pool[id]=vector;}
                    break;
                }
                default:throw new InvalidOperationException("Unknown dictionary operation "+method);
            }
            return null;
        }
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            active=step;reentered=false;events.Clear();comparer?.Calls.Clear();mode=step.TryGetProperty("mode",out var m)?m.GetString()!:"";
            object? result=null;string? error=null,param=null;
            try{result=Operate(step);}catch(Exception e){error=e.GetType().Name;param=(e as ArgumentException)?.ParamName;}
            results.Add(new{result,error,param,items=dictionary.Select(x=>DictionaryPair(x)).ToArray(),keys=keys.Select(x=>DictionaryWire(x)).ToArray(),values=values.Select(x=>DictionaryWire(x)).ToArray(),count=dictionary.Count,readOnly=dictionary.IsReadOnly,keysReadOnly=keys.IsReadOnly,valuesReadOnly=values.IsReadOnly,events=events.ToArray(),comparerCalls=comparer?.Calls.ToArray()??Array.Empty<string>()});
        }
        return results;
    }
}
