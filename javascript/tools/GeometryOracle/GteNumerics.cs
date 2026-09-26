// Observation-only numerical controller. Algorithms come from the unchanged C# assembly.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
using netDxf.GTE;
internal static partial class Program
{
    private static Type GteType(string name) {
        if(name.EndsWith("&"))return GteType(name[..^1]).MakeByRefType();
        if(name.EndsWith("[]"))return GteType(name[..^2]).MakeArrayType();
        return name switch {"DoubleFunction"=>typeof(Func<double,double>),"SortedDoubleInt"=>typeof(SortedDictionary<double,int>),_=>Resolve(name)};
    }
    private static object? GteWire(object? value) {
        if(value==null)return null;
        if(value is GVector vector)return new {type="netDxf.GTE.GVector",data=vector.Vector.Select(Bits).ToArray()};
        if(value is GMatrix matrix)return new {type="netDxf.GTE.GMatrix",rows=matrix.NumRows,cols=matrix.NumCols,data=matrix.Elements.Vector.Select(Bits).ToArray()};
        if(value is KeyValuePair<double,int> pair)return new {key=Bits(pair.Key),value=pair.Value};
        if(value is IEnumerable sequence && value is not string)return sequence.Cast<object?>().Select(GteWire).ToArray();
        if(value is not Enum && value.GetType().Namespace=="netDxf.GTE")return new {type=value.GetType().FullName};
        return Wire(value);
    }
    private static object GteRequest(JsonElement input) {
        netDxf.GTE.GTE.UseRowMajor=!input.TryGetProperty("rowMajor",out var layout)||layout.GetBoolean();
        var values=new Dictionary<string,object?>();var trace=new List<object>();
        object? ReadGte(JsonElement value) {
            if(value.ValueKind!=JsonValueKind.Object)return Read(value);
            if(value.TryGetProperty("ref",out var reference))return values[reference.GetString()!];
            if(value.TryGetProperty("cell",out var cell))return values[cell.GetString()!];
            if(value.TryGetProperty("out",out _))return null;
            if(value.TryGetProperty("function",out var function)) {
                var coefficients=function.GetProperty("coefficients").EnumerateArray().Select(v=>Convert.ToDouble(ReadGte(v))).ToArray();int calls=0;
                return new Func<double,double>(x=>{calls++;trace.Add(new {argument=Bits(x)});
                    if(function.TryGetProperty("throwAt",out var fail)&&calls==fail.GetInt32())throw new InvalidOperationException("Injected numerical callback failure.");
                    double y=0;foreach(double coefficient in coefficients)y=y*x+coefficient;return y;});
            }
            if(value.TryGetProperty("array",out var arrayType)) {
                var elements=value.GetProperty("values").EnumerateArray().Select(ReadGte).ToArray();var array=Array.CreateInstance(GteType(arrayType.GetString()!),elements.Length);
                for(int j=0;j<elements.Length;j++)array.SetValue(elements[j],j);return array;
            }
            if(value.TryGetProperty("new",out var name)) {
                var type=GteType(name.GetString()!);var args=value.TryGetProperty("args",out var parameters)?parameters.EnumerateArray().Select(ReadGte).ToArray():Array.Empty<object?>();
                var signature=value.TryGetProperty("signature",out var sig)?sig.EnumerateArray().Select(v=>GteType(v.GetString()!)).ToArray():null;
                return Create(type,args,signature);
            }
            return Read(value);
        }
        var output=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            try {
                object? target=step.TryGetProperty("target",out var targetId)?values[targetId.GetString()!]:null;
                var type=step.TryGetProperty("type",out var typeName)?GteType(typeName.GetString()!):target?.GetType();
                string member=step.TryGetProperty("member",out var name)?name.GetString()!:"";
                var descriptors=step.TryGetProperty("args",out var arguments)?arguments.EnumerateArray().ToArray():Array.Empty<JsonElement>();
                object?[] args=descriptors.Select(ReadGte).ToArray();object? result=null;object?[] refs=Array.Empty<object?>();
                var flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
                var signature=step.TryGetProperty("signature",out var sig)?sig.EnumerateArray().Select(v=>GteType(v.GetString()!)).ToArray():null;
                switch(step.GetProperty("kind").GetString()) {
                    case "new":case "value":result=ReadGte(step.GetProperty("value"));break;
                    case "get": {var p=type!.GetProperty(member,flags);result=p!=null?p.GetValue(target):type.GetField(member,flags)!.GetValue(target);break;}
                    case "set": {var value=ReadGte(step.GetProperty("value"));var p=type!.GetProperty(member,flags);if(p!=null)p.SetValue(target,value);else type.GetField(member,flags)!.SetValue(target,value);break;}
                    case "index":result=target is Array array?array.GetValue(Convert.ToInt32(args[0])):type!.GetProperty("Item",Enumerable.Repeat(typeof(int),args.Length).ToArray())!.GetValue(target,args);break;
                    case "set-index": {var value=ReadGte(step.GetProperty("value"));if(target is Array writableArray)writableArray.SetValue(value,Convert.ToInt32(args[0]));else type!.GetProperty("Item",Enumerable.Repeat(typeof(int),args.Length).ToArray())!.SetValue(target,value,args);break;}
                    case "call": {
                        var method=signature!=null?type!.GetMethod(member,flags,null,signature,null)!:type!.GetMethods(flags).Where(m=>m.Name==member).Single(m=>Matches(m.GetParameters(),args));
                        result=method.Invoke(target,args);
                        var parameters=method.GetParameters();refs=parameters.Select((p,j)=>(p,j)).Where(x=>x.p.ParameterType.IsByRef).Select(x=>args[x.j]).ToArray();
                        for(int j=0;j<descriptors.Length;j++)if(parameters[j].ParameterType.IsByRef){var key=descriptors[j].TryGetProperty("cell",out var cell)?cell:descriptors[j].GetProperty("out");values[key.GetString()!]=args[j];}
                        break;
                    }
                    case "snapshot":result=target;break;
                    case "trace":output.Add(new {ok=true,value=trace.ToArray(),outputs=Array.Empty<object>()});continue;
                    default:throw new ArgumentException("Unknown GTE observation kind.");
                }
                if(step.TryGetProperty("id",out var id))values[id.GetString()!]=result;
                output.Add(new {ok=true,value=GteWire(result),outputs=refs.Select(GteWire).ToArray()});
            } catch(Exception e) {while(e is TargetInvocationException&&e.InnerException!=null)e=e.InnerException;output.Add(new {ok=false,error=e.GetType().Name,param=(e as ArgumentException)?.ParamName});}
        }
        return output;
    }
}
