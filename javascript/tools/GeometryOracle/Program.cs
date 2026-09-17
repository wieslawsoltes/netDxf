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
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly Dictionary<string, object?> Values = new();
    private static string Bits(double v) => unchecked((ulong)BitConverter.DoubleToInt64Bits(v)).ToString("X16", CultureInfo.InvariantCulture);
    private static double FromBits(string v) => BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    private static Type Resolve(string name)
    {
        if (name.EndsWith("&")) return Resolve(name[..^1]).MakeByRefType();
        if (name.EndsWith("[]")) return Resolve(name[..^2]).MakeArrayType();
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
        if (v.TryGetProperty("ref", out var id)) return Values[id.GetString()!];
        if (v.TryGetProperty("double", out var bits)) return FromBits(bits.GetString()!);
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
    private static object? Create(Type type, object?[] args, Type[]? signature)
    {
        if (args.Length==0 && type.IsValueType) return Activator.CreateInstance(type);
        var constructor = signature is null ? type.GetConstructors().Single(c=>Matches(c.GetParameters(),args)) : type.GetConstructor(signature);
        return (constructor ?? throw new MissingMethodException(type.Name)).Invoke(args);
    }
    private static object? Wire(object? value)
    {
        if (value is null) return null;
        if (value is double d) return new { @double = Bits(d) };
        if (value is long l) return new { @long = l.ToString(CultureInfo.InvariantCulture) };
        if (value is string || value is bool) return value;
        if (value is int || value is short || value is byte || value is Enum) return new { @double = Bits(Convert.ToDouble(value, CultureInfo.InvariantCulture)) };
        if (value is DateTime date) return new { date = new[] {date.Year,date.Month,date.Day,date.Hour,date.Minute,date.Second,date.Millisecond}, ticks=date.Ticks.ToString(), kind=(int)date.Kind };
        if (value is TimeSpan span) return new { ticks = span.Ticks.ToString() };
        if (value is Vector2 v2) return new { type="Vector2", values=new[]{Bits(v2.X),Bits(v2.Y)}, normalized=v2.IsNormalized };
        if (value is Vector3 v3) return new { type="Vector3", values=new[]{Bits(v3.X),Bits(v3.Y),Bits(v3.Z)}, normalized=v3.IsNormalized };
        if (value is Vector4 v4) return new { type="Vector4", values=new[]{Bits(v4.X),Bits(v4.Y),Bits(v4.Z),Bits(v4.W)}, normalized=v4.IsNormalized };
        if (value is Matrix2 || value is Matrix3 || value is Matrix4) {
            var type = value.GetType(); int n=int.Parse(type.Name[^1..]);
            var cells = new List<string>();
            for(int r=1;r<=n;r++)for(int c=1;c<=n;c++)cells.Add(Bits((double)type.GetProperty($"M{r}{c}")!.GetValue(value)!));
            return new { type=type.Name, values=cells, identity=(bool)type.GetProperty("IsIdentity")!.GetValue(RuntimeHelpers.GetObjectValue(value))! };
        }
        if(value is BezierCurve curve) return new { type=value.GetType().Name, degree=curve.Degree, points=curve.ControlPoints.Select(v=>Wire(v)).ToArray() };
        if(value is BoundingRectangle box) return new { type="BoundingRectangle", min=Wire(box.Min),max=Wire(box.Max),center=Wire(box.Center),radius=Wire(box.Radius),width=Wire(box.Width),height=Wire(box.Height) };
        if(value is ClippingBoundary clip) return new { type="ClippingBoundary", kind=(int)clip.Type, vertices=clip.Vertexes.Select(v=>Wire(v)).ToArray() };
        if(value is AciColor color) return new {type="AciColor",r=color.R,g=color.G,b=color.B,index=color.Index,trueColor=color.UseTrueColor,byLayer=color.IsByLayer,byBlock=color.IsByBlock};
        if(value is netDxf.Entities.HatchPatternLineDefinition line) return new {type=value.GetType().Name,angle=Wire(line.Angle),origin=Wire(line.Origin),delta=Wire(line.Delta),dashes=line.DashPattern.Select(WireDouble).ToArray()};
        if(value is netDxf.Entities.HatchPattern hatchPattern) {
            var pattern=new {type=value.GetType().Name,name=hatchPattern.Name,description=hatchPattern.Description,style=(int)hatchPattern.Style,fill=(int)hatchPattern.Fill,
                kind=(int)hatchPattern.Type,isDouble=hatchPattern.IsDouble,origin=Wire(hatchPattern.Origin),angle=Wire(hatchPattern.Angle),scale=Wire(hatchPattern.Scale),
                lines=hatchPattern.LineDefinitions.Select(v=>Wire(v)).ToArray()};
            if(value is netDxf.Entities.HatchGradientPattern gradient) return new {pattern,gradientType=(int)gradient.GradientType,color1=Wire(gradient.Color1),color2=Wire(gradient.Color2),
                single=gradient.SingleColor,tint=Wire(gradient.Tint),shift=Wire(gradient.Shift),centered=gradient.Centered,aci1=Wire(gradient.Color1AciIndex),aci2=Wire(gradient.Color2AciIndex),
                auto1=gradient.IsColor1AciIndexAutomatic,auto2=gradient.IsColor2AciIndexAutomatic};
            return new {pattern};
        }
        if(value is XDataRecord record) return new {type="XDataRecord",code=(int)record.Code,value=Wire(record.Value)};
        if(value is DxfClass definition) return new {type="DxfClass",name=definition.Name,cpp=definition.CppClassName,application=definition.ApplicationName,flags=definition.ProxyFlags,count=definition.InstanceCount,wasProxy=definition.WasProxy,entity=definition.IsEntity};
        if(value is System.Drawing.Color rgba) return new {type="Color",argb=rgba.ToArgb(),name=rgba.Name,known=rgba.IsKnownColor,named=rgba.IsNamedColor,empty=rgba.IsEmpty};
        if(value is Transparency alpha) return new { type="Transparency", value=alpha.Value, stored=alpha.StoredAlphaValue, byLayer=alpha.IsByLayer, byBlock=alpha.IsByBlock };
        if (StyleWire(value, out var styleValue)) return styleValue;
        if (value is ITuple tuple) return Enumerable.Range(0,tuple.Length).Select(i=>Wire(tuple[i])).ToArray();
        if (value is IEnumerable list) return list.Cast<object?>().Select(Wire).ToArray();
        throw new ArgumentException("Unmapped result type " + value.GetType().FullName);
    }
    private static object? WireDouble(double value) => Wire(value);
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
    private static object? Step(JsonElement step)
    {
        string kind=step.GetProperty("kind").GetString()!;
        object? target=step.TryGetProperty("target",out var key)?Values[key.GetString()!]:null;
        Type? type=step.TryGetProperty("type",out var name)?Resolve(name.GetString()!):target?.GetType();
        string member=step.TryGetProperty("member",out var m)?m.GetString()!:"";
        var args=Arguments(step); object? result;
        switch(kind) {
            case "lin-names": case "lin-load": case "lin-save": case "shape-names": case "shape-query": result=StyleFileStep(step,target);break;
            case "events": return Observations.ToArray();
            case "observe": case "unobserve": result=ObservationStep(step,target);break;
            case "reference-equals": result=ReferenceEquals(args[0],args[1]);break;
            case "pat-names": case "pat-load": case "pat-save": result=PatternTextStep(step,target);break;
            case "new": result=Create(type!,args,Signature(step));break;
            case "get": {
                var flags=BindingFlags.Public|BindingFlags.Instance|BindingFlags.Static;
                if(step.TryGetProperty("nonPublic",out var hidden)&&hidden.GetBoolean())flags|=BindingFlags.NonPublic;
                var property=type!.GetProperty(member,flags);var field=type!.GetField(member,flags);
                if(property is null && field is null)throw new MissingMemberException(type.Name,member);
                result=property is not null?property.GetValue(target):field!.GetValue(target);break;
            }
            case "set": type!.GetProperty(member)!.SetValue(target,Read(step.GetProperty("value"))); result=null;break;
            case "index": result=type!.GetProperty("Item",Signature(step) ?? args.Select(a=>a!.GetType()).ToArray())!.GetValue(target,args);break;
            case "set-index": type!.GetProperty("Item",Signature(step) ?? args.Select(a=>a!.GetType()).ToArray())!.SetValue(target,Read(step.GetProperty("value")),args);result=null;break;
            case "snapshot": result=target;break;
            case "call": {
                var sig=Signature(step);
                var flags=BindingFlags.Public|(target is null?BindingFlags.Static:BindingFlags.Instance);
                var method=sig is null?type!.GetMethods(flags).Where(v=>v.Name==member).Single(v=>Matches(v.GetParameters(),args))
                    :type!.GetMethod(member,flags,null,sig,null)!;
                result=method.Invoke(target,args);
                if(method.GetParameters().Any(p=>p.IsOut)) return new { result=Wire(result), outputs=method.GetParameters().Select((p,i)=>(p,i)).Where(v=>v.p.IsOut).Select(v=>Wire(args[v.i])).ToArray() };
                break;
            }
            case "emit": {
                var v=(Vector3)target!;
                var tags=new[]{new DxfTag(0,"SECTION"),new DxfTag(2,"HEADER"),new DxfTag(9,"$ACADVER"),new DxfTag(1,"AC1032"),new DxfTag(0,"ENDSEC"),
                    new DxfTag(0,"SECTION"),new DxfTag(2,"ENTITIES"),new DxfTag(0,"POINT"),new DxfTag(10,v.X),new DxfTag(20,v.Y),new DxfTag(30,v.Z),new DxfTag(0,"ENDSEC"),new DxfTag(0,"EOF")};
                var doc=DxfRawDocument.Create(tags);using var text=new MemoryStream();using var binary=new MemoryStream();doc.Save(text,false);doc.Save(binary,true);
                return new {text=Convert.ToBase64String(text.ToArray()),binary=Convert.ToBase64String(binary.ToArray())};
            }
            default: throw new ArgumentException("Unknown step kind " + kind);
        }
        if(step.TryGetProperty("id",out var id))Values[id.GetString()!]=result;
        return Wire(result);
    }
    private static object Run(JsonElement input)
    {
        Values.Clear();ResetObservations();MathHelper.Epsilon=1e-12;
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        if(input.TryGetProperty("op",out var op)&&op.GetString()=="unit-factors")return Enumerable.Range(0,25).Select(a=>Enumerable.Range(0,25).Select(b=>Bits(UnitHelper.ConversionFactor((DrawingUnits)a,(DrawingUnits)b))).ToArray()).ToArray();
        if(input.TryGetProperty("op",out var environmentOp)&&environmentOp.GetString()=="environment")return new {
            framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),runtime=Environment.Version.ToString()
        };
        if(input.TryGetProperty("op",out var benchmarkOp)&&benchmarkOp.GetString()=="benchmark")return Benchmark();
        var results=new List<object>();
        foreach(var step in input.GetProperty("steps").EnumerateArray()) {
            try { results.Add(new {ok=true,value=Step(step)}); }
            catch(Exception error) { while(error is TargetInvocationException && error.InnerException is not null)error=error.InnerException;
                results.Add(new {ok=false,error=error.GetType().Name,param=(error as ArgumentException)?.ParamName}); }
        }
        return results;
    }
    private static object Benchmark()
    {
        const int size=50000,samples=12;
        var points=Enumerable.Range(0,size).Select(i=>new Vector3(i*0.125,i%17,i%31)).ToArray();
        var matrix=new Matrix3(2,3,4,5,6,7,8,9,10);
        double Work(){double sum=0;foreach(var p in points){var q=Matrix3.Multiply(matrix,p);sum+=q.X+q.Y+q.Z;}return sum;}
        for(int i=0;i<3;i++)Work();
        var timings=new List<double>();long before=GC.GetTotalAllocatedBytes(true);double checksum=0;
        for(int i=0;i<samples;i++){var timer=Stopwatch.StartNew();checksum=Work();timer.Stop();timings.Add(timer.Elapsed.TotalMilliseconds);}
        return new {size,samples,timings,checksum=Bits(checksum),allocatedBytes=GC.GetTotalAllocatedBytes(true)-before};
    }
    private static int Main()
    {
        string? line;
        while((line=Console.ReadLine())is not null) {
            try { using var input=JsonDocument.Parse(line);Console.WriteLine(JsonSerializer.Serialize(Run(input.RootElement),Json)); }
            catch(Exception error) { Console.WriteLine(JsonSerializer.Serialize(new{fatal=error.ToString()},Json)); }
        }
        return 0;
    }
}
