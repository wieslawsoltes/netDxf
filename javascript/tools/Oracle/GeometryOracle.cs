// A development-only caller of the real production methods, not a replacement geometry model.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using netDxf;

internal static class GeometryOracle
{
    private static readonly Dictionary<string,Type> Primitives=new()
    { ["double"]=typeof(double),["float"]=typeof(float),["int"]=typeof(int),["short"]=typeof(short),["byte"]=typeof(byte),
      ["bool"]=typeof(bool),["string"]=typeof(string),["object"]=typeof(object),["void"]=typeof(void),["long"]=typeof(long) };
    private static string Bits(double d)=>unchecked((ulong)BitConverter.DoubleToInt64Bits(d)).ToString("X16",CultureInfo.InvariantCulture);
    private static double Double(string s)=>BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(s,NumberStyles.HexNumber,CultureInfo.InvariantCulture)));
    private static string TypeId(Type t)
    {
        if(t.IsByRef)return TypeId(t.GetElementType()!);
        foreach(var p in Primitives)if(p.Value==t)return p.Key;
        if(t.IsArray)return TypeId(t.GetElementType()!)+"[]";
        if(t.IsGenericType)return t.GetGenericTypeDefinition().FullName!.Split('`')[0]+"<"+string.Join(", ",t.GetGenericArguments().Select(TypeId))+">";
        return t.FullName!;
    }
    private static string Signature(MethodBase m)=>string.Join(",",m.GetParameters().Select(p=>(p.IsOut?"out ":p.ParameterType.IsByRef?"ref ":"")+TypeId(p.ParameterType)));
    private static Type Resolve(string name)=>Primitives.TryGetValue(name,out var p)?p:
        (name=="System.Drawing.Color"?typeof(System.Drawing.Color):null)??typeof(Vector2).Assembly.GetType(name,false)??Type.GetType(name,false)??throw new ArgumentException("Unmapped oracle type "+name);
    private static object? Decode(JsonElement value,Type target)
    {
        if(target.IsByRef)target=target.GetElementType()!;
        if(Nullable.GetUnderlyingType(target) is Type underlying)target=underlying;
        if(value.ValueKind==JsonValueKind.Null)return null;
        if(value.ValueKind==JsonValueKind.Object)
        {
            if(value.TryGetProperty("d",out var d))return Double(d.GetString()!);
            if(value.TryGetProperty("culture",out var culture))return CultureInfo.GetCultureInfo(culture.GetString()!);
            if(value.TryGetProperty("type",out var name))
            {
                Type type=Resolve(name.GetString()!);
                if(value.TryGetProperty("factory",out var factory)) {
                    var values=value.GetProperty("args");
                    var method=type.GetMethods(BindingFlags.Public|BindingFlags.Static).Single(m=>m.Name==factory.GetString()&&m.GetParameters().Length==values.GetArrayLength());
                    return method.Invoke(null,Arguments(values,method.GetParameters()));
                }
                string signature=value.GetProperty("signature").GetString()!;
                var ctor=type.GetConstructors(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance).SingleOrDefault(c=>Signature(c)==signature);
                object? result;
                if(ctor==null&&type.IsValueType&&signature=="")result=Activator.CreateInstance(type);
                else
                {
                    if(ctor==null)throw new ArgumentException("No constructor "+type+"("+signature+")");
                    result=ctor.Invoke(Arguments(value.GetProperty("args"),ctor.GetParameters()));
                }
                if(value.TryGetProperty("properties",out var props))foreach(var entry in props.EnumerateObject())
                {var prop=type.GetProperty(entry.Name)!;prop.SetValue(result,Decode(entry.Value,prop.PropertyType));}
                if(value.TryGetProperty("normalize",out var norm)&&norm.GetBoolean())type.GetMethod("Normalize",Type.EmptyTypes)!.Invoke(result,null);
                return result;
            }
        }
        if(target.IsArray || (target.IsGenericType&&typeof(IEnumerable).IsAssignableFrom(target)))
        {
            Type element=target.IsArray?target.GetElementType()!:target.GetGenericArguments()[0];
            var values=value.EnumerateArray().ToArray();Array array=Array.CreateInstance(element,values.Length);
            for(int i=0;i<values.Length;i++)array.SetValue(Decode(values[i],element),i);
            return array;
        }
        if(target==typeof(double))return value.GetDouble();
        if(target==typeof(float))return value.GetSingle();
        if(target==typeof(int))return value.GetInt32();
        if(target==typeof(short))return value.GetInt16();
        if(target==typeof(byte))return value.GetByte();
        if(target==typeof(bool))return value.GetBoolean();
        if(target==typeof(string))return value.GetString();
        if(target.IsEnum)return Enum.ToObject(target,value.GetInt32());
        if(target==typeof(object))return value.ValueKind switch{JsonValueKind.String=>value.GetString(),JsonValueKind.True=>true,JsonValueKind.False=>false,_=>value.GetDouble()};
        throw new ArgumentException("Cannot decode "+target+" from "+value);
    }
    private static object?[] Arguments(JsonElement args,ParameterInfo[] ps)
    {
        var values=args.EnumerateArray().ToArray();if(values.Length!=ps.Length)throw new ArgumentException("Argument arity mismatch.");
        return ps.Select((p,i)=>p.IsOut?null:Decode(values[i],p.ParameterType)).ToArray();
    }
    private static object? Encode(object? value)
    {
        if(value==null)return null;
        if(value is CultureInfo culture)return new {culture=culture.Name};
        if(value is double d)return new {d=Bits(d)};
        if(value is float f)return new {d=Bits(f)};
        if(value is bool||value is string||value is int||value is short||value is byte||value is long)return value;
        Type type=value.GetType();if(type.IsEnum)return Convert.ToInt32(value);
        if(value is IEnumerable sequence)return sequence.Cast<object?>().Select(Encode).ToArray();
        // Only read the audited cluster's public state (no arbitrary property traversal).
        if(type.Namespace?.StartsWith("netDxf")==true||type.FullName!.StartsWith("System.Tuple")||type.FullName=="System.Drawing.Color")
        {
            var result=new SortedDictionary<string,object?>(StringComparer.Ordinal);
            var properties=type.GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(p=>p.CanRead&&p.GetIndexParameters().Length==0);
            if(type.FullName=="System.Drawing.Color")properties=properties.Where(p=>p.Name is "A" or "R" or "G" or "B");
            foreach(var p in properties)result.Add(p.Name,Encode(p.GetValue(value)));
            return new {type=type.Name,properties=result};
        }
        throw new ArgumentException("Unmapped oracle result "+type);
    }
    internal static object Execute(JsonElement input)
    {
        double epsilon=MathHelper.Epsilon;CultureInfo old=CultureInfo.CurrentCulture;
        try
        {
            if(input.TryGetProperty("culture",out var culture))CultureInfo.CurrentCulture=CultureInfo.GetCultureInfo(culture.GetString()!);
            else CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
            if(input.TryGetProperty("epsilon",out var ep))MathHelper.Epsilon=ep.GetDouble();
            var answers=new List<object>();
            foreach(var request in input.GetProperty("requests").EnumerateArray())
            {
                try
                {
                    Type type=Resolve(request.GetProperty("type").GetString()!);
                    object? instance=request.TryGetProperty("instance",out var obj)?Decode(obj,type):null;
                    string action=request.GetProperty("action").GetString()!;
                    object? result=null;object?[] arguments=Array.Empty<object?>();
                    if(action=="construct")result=Decode(request.GetProperty("value"),type);
                    else if(action=="get"||action=="set")
                    {
                        string member=request.GetProperty("member").GetString()!;
                        var prop=type.GetProperty(member,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance);
                        if(prop==null){var field=type.GetField(member,BindingFlags.Public|BindingFlags.Static)!;result=field.GetValue(null);}
                        else
                        {
                            var indexes=request.TryGetProperty("indexes",out var ix)?Arguments(ix,prop.GetIndexParameters()):null;
                            if(action=="set")prop.SetValue(instance,Decode(request.GetProperty("value"),prop.PropertyType),indexes);
                            result=prop.GetValue(instance,indexes);
                        }
                    }
                    else if(action=="call")
                    {
                        string name=request.GetProperty("member").GetString()!,signature=request.GetProperty("signature").GetString()!;
                        bool stat=request.GetProperty("static").GetBoolean();
                        var method=type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|(stat?BindingFlags.Static:BindingFlags.Instance))
                            .Single(m=>m.Name==name&&Signature(m)==signature);
                        arguments=Arguments(request.GetProperty("args"),method.GetParameters());result=method.Invoke(instance,arguments);
                    }
                    else throw new ArgumentException("Unknown geometry action.");
                    answers.Add(new {ok=true,result=Encode(result),instance=Encode(instance),arguments=arguments.Select(Encode).ToArray()});
                }
                catch(Exception error)
                {
                    while(error is TargetInvocationException&&error.InnerException!=null)error=error.InnerException;
                    answers.Add(new {ok=false,error=error.GetType().Name,paramName=(error as ArgumentException)?.ParamName});
                }
            }
            return answers;
        }
        finally{MathHelper.Epsilon=epsilon;CultureInfo.CurrentCulture=old;}
    }
}
