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
    private static object Run(JsonElement input)
    {
        netDxf.Blocks.BlockRecord.DefaultUnits=DrawingUnits.Unitless;netDxf.Entities.Insert.DefaultInsUnits=DrawingUnits.Unitless;
        Values.Clear();ResetObservations();MathHelper.Epsilon=1e-12;netDxf.Entities.Text.DefaultMirrText=false;netDxf.Entities.MText.DefaultMirrText=false;
        if(input.TryGetProperty("op",out var mathOp)&&mathOp.GetString()=="reference-math")return ReferenceMath(input);
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
