// Independent test-only System.Math oracle. Never generates production answers.
using System;
using System.Linq;
using System.Text.Json;
internal static partial class Program
{
    private static object ReferenceMath(JsonElement input) => input.GetProperty("calls").EnumerateArray().Select(call => {
        string name=call.GetProperty("name").GetString()!;
        double[] a=call.GetProperty("args").EnumerateArray().Select(v=>FromBits(v.GetString()!)).ToArray();
        double value=name switch {
            "Sin"=>Math.Sin(a[0]),"Cos"=>Math.Cos(a[0]),"Tan"=>Math.Tan(a[0]),
            "Asin"=>Math.Asin(a[0]),"Acos"=>Math.Acos(a[0]),"Atan"=>Math.Atan(a[0]),
            "Atan2"=>Math.Atan2(a[0],a[1]),"Fma"=>Math.FusedMultiplyAdd(a[0],a[1],a[2]),
            _=>throw new ArgumentException("Unknown math method")
        };
        return Bits(value);
    }).ToArray();
}
