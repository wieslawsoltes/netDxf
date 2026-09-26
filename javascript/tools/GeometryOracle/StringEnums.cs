// Test-only reflection of the unchanged generic source class, not expected output.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;
internal static partial class Program
{
    private static object StringEnumRequest(JsonElement input)
    {
        var type=typeof(DxfObject).Assembly.GetTypes().Single(t=>t.IsEnum&&t.Name==input.GetProperty("enum").GetString());
        var helper=typeof(StringEnum<>).MakeGenericType(type);var model=Activator.CreateInstance(helper)!;
        object? Call(JsonElement step) {
            string method=step.GetProperty("method").GetString()!;
            if(method=="Attribute")return new StringValueAttribute(step.GetProperty("value").GetString()!).Value;
            if(method=="EnumType")return ReferenceEquals(type,helper.GetProperty("EnumType")!.GetValue(model));
            if(method=="GetStringValues")return ((IEnumerable)helper.GetMethod(method)!.Invoke(model,null)!).Cast<object?>().ToArray();
            if(method=="GetValues")return ((IEnumerable)helper.GetMethod(method)!.Invoke(model,null)!).Cast<object>().Select(pair=>new{
                key=Convert.ToInt64(pair.GetType().GetProperty("Key")!.GetValue(pair)),value=pair.GetType().GetProperty("Value")!.GetValue(pair)}).ToArray();
            if(method=="GetStringValue")return helper.GetMethod(method)!.Invoke(null,new[]{Enum.ToObject(type,step.GetProperty("value").GetInt32())});
            string? value=step.GetProperty("value").GetString();
            if(step.TryGetProperty("comparison",out var comparison))return helper.GetMethod(method,new[]{typeof(string),typeof(StringComparison)})!
                .Invoke(null,new object?[]{value,(StringComparison)comparison.GetInt32()});
            return helper.GetMethod(method,new[]{typeof(string)})!.Invoke(null,new object?[]{value});
        }
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            try {var value=Call(step);if(value is Enum e)value=Convert.ToInt64(e);results.Add(new{ok=true,value});}
            catch(Exception e){while(e is TargetInvocationException&&e.InnerException is not null)e=e.InnerException;
                results.Add(new{ok=false,error=e.GetType().Name,param=(e as ArgumentException)?.ParamName});}
        }
        return results;
    }
}
