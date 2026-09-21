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
    private static Type Resolve(string name)
    {
        if (name.EndsWith("&")) return Resolve(name[..^1]).MakeByRefType();
        if (name.EndsWith("[]")) return Resolve(name[..^2]).MakeArrayType();
        if (name.StartsWith("List<")) return typeof(List<>).MakeGenericType(Resolve(name[5..^1]));
        if (name.StartsWith("IEnumerable<")) return typeof(IEnumerable<>).MakeGenericType(Resolve(name[12..^1]));
        return name switch {
            "Double" => typeof(double), "Int32" => typeof(int), "Int16" => typeof(short), "Byte" => typeof(byte),
            "Boolean" => typeof(bool), "String" => typeof(string), "Object" => typeof(object),
            "IFormatProvider" => typeof(IFormatProvider), "Color" => typeof(System.Drawing.Color), "DateTime" => typeof(DateTime), "TimeSpan" => typeof(TimeSpan),
            _ => typeof(Vector2).Assembly.GetType(name.StartsWith("netDxf.") ? name : "netDxf." + name) ??
                typeof(Vector2).Assembly.GetType("netDxf.Collections." + name) ?? typeof(Vector2).Assembly.GetType("netDxf.Units." + name) ?? throw new ArgumentException("Unknown type " + name)
        };
    }
    private static object? Read(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind == JsonValueKind.Number) return v.GetDouble();
        if (v.ValueKind == JsonValueKind.String) return v.GetString();
        if (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) return v.GetBoolean();
        if (v.TryGetProperty("char", out var character)) return (char)character.GetUInt16();
        if (v.TryGetProperty("datetime", out var headerDate)) return new DateTime(long.Parse(headerDate.GetProperty("ticks").GetString()!,CultureInfo.InvariantCulture), (DateTimeKind)(headerDate.TryGetProperty("kind",out var dateKind)?dateKind.GetInt32():0));
        if (v.TryGetProperty("timespan", out var headerSpan)) return new TimeSpan(long.Parse(headerSpan.GetString()!,CultureInfo.InvariantCulture));
        if (v.TryGetProperty("box", out var headerBox)) return Read(headerBox);
        if (v.TryGetProperty("utf16", out var chars)) return new string(chars.EnumerateArray().Select(x=>(char)x.GetUInt16()).ToArray());
        if (v.TryGetProperty("resolver", out var mappings)) {
            var map=new Dictionary<DxfObject,DxfObject>();
            foreach(var pair in mappings.EnumerateArray())map.Add((DxfObject)Read(pair[0])!,(DxfObject)Read(pair[1])!);
            return new Func<DxfObject,DxfObject>(item=>map[item]);
        }
        if (v.TryGetProperty("copy", out var copied)) return RuntimeHelpers.GetObjectValue(Read(copied)!);
        if (v.TryGetProperty("ref", out var id)) return Values[id.GetString()!];
        if (v.TryGetProperty("double", out var bits)) return FromBits(bits.GetString()!);
        if (v.TryGetProperty("long", out var lng)) return long.Parse(lng.GetString()!,CultureInfo.InvariantCulture);
        if (v.TryGetProperty("int", out var i)) return i.GetInt32();
        if (v.TryGetProperty("short", out var s)) return s.GetInt16();
        if (v.TryGetProperty("byte", out var b)) return b.GetByte();
        if (v.TryGetProperty("enum", out var e)) return Enum.ToObject(Resolve(e.GetString()!), v.GetProperty("value").GetInt32());
        if (v.TryGetProperty("out", out _)) return null;
        if (v.TryGetProperty("culture", out var culture)) return CultureInfo.GetCultureInfo(culture.GetString()!);
        if (v.TryGetProperty("array", out var element)) {
            Type type = Resolve(element.GetString()!);
            var items = v.GetProperty("values").EnumerateArray().Select(Read).ToArray();
            var array = Array.CreateInstance(type, items.Length);
            for (int at=0;at<items.Length;at++) array.SetValue(items[at],at);
            return array;
        }
        if (v.TryGetProperty("new", out var constructor)) return Create(Resolve(constructor.GetString()!), Arguments(v), Signature(v));
        if (v.TryGetProperty("static", out var typeName)) {
            var type = Resolve(typeName.GetString()!);
            return type.GetProperty(v.GetProperty("property").GetString()!, BindingFlags.Public|BindingFlags.Static)!.GetValue(null);
        }
        throw new ArgumentException("Unknown value descriptor.");
    }
    private static object?[] Arguments(JsonElement v) => v.TryGetProperty("args", out var a) ? a.EnumerateArray().Select(Read).ToArray() : Array.Empty<object?>();
    private static Type[]? Signature(JsonElement v) => v.TryGetProperty("signature", out var s) ? s.EnumerateArray().Select(x=>Resolve(x.GetString()!)).ToArray() : null;
    private static bool Matches(ParameterInfo[] parameters, object?[] args)
    {
        if (parameters.Length != args.Length) return false;
        for (int i=0;i<args.Length;i++) {
            if (parameters[i].IsOut) continue;
            if (args[i] is null) { if(parameters[i].ParameterType.IsValueType) return false; }
            else if (!parameters[i].ParameterType.IsInstanceOfType(args[i])) return false;
        }
        return true;
    }
    private static object? Create(Type type, object?[] args, Type[]? signature, bool nonPublic = false)
    {
        if (args.Length==0 && type.IsValueType) return Activator.CreateInstance(type);
        var flags = BindingFlags.Public|BindingFlags.Instance|(nonPublic ? BindingFlags.NonPublic : 0);
        var constructor = signature is null ? type.GetConstructors(flags).Single(c=>Matches(c.GetParameters(),args)) : type.GetConstructor(flags,null,signature,null);
        return (constructor ?? throw new MissingMethodException(type.Name)).Invoke(args);
    }
    private static object? PatternTextStep(JsonElement step, object? target)
    {
        string file=Path.Combine(Path.GetTempPath(), "netdxf-pat-oracle-"+Guid.NewGuid().ToString("N")+".pat");
        try {
            string kind=step.GetProperty("kind").GetString()!;
            if(kind=="pat-save") {
                ((netDxf.Entities.HatchPattern)target!).Save(file);
                return File.ReadAllText(file);
            }
            File.WriteAllText(file,step.GetProperty("text").GetString()!);
            if(kind=="pat-names") return netDxf.Entities.HatchPattern.NamesFromFile(file);
            return netDxf.Entities.HatchPattern.Load(file,step.GetProperty("patternName").GetString()!);
        }
        finally { if(File.Exists(file))File.Delete(file); }
    }
}
