// Test-only direct runtime oracle. No alternative numerical implementation.
using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
internal static class MathOracle
{
    private static string Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value)).ToString("X16", CultureInfo.InvariantCulture);
    private static double Read(JsonElement value) => BitConverter.Int64BitsToDouble(unchecked((long)ulong.Parse(value.GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
    public static object Execute(JsonElement input)
    {
        if(input.TryGetProperty("environment",out var env)&&env.GetBoolean()) return new {
            framework=RuntimeInformation.FrameworkDescription,os=RuntimeInformation.OSDescription,architecture=RuntimeInformation.ProcessArchitecture.ToString(),runtime=Environment.Version.ToString()
        };
        return input.GetProperty("requests").EnumerateArray().Select(request => {
            var args=request.GetProperty("args").EnumerateArray().Select(Read).ToArray();
            double x=args[0];
            double value=request.GetProperty("member").GetString() switch {
                "Sin" => Math.Sin(x), "Cos" => Math.Cos(x), "Tan" => Math.Tan(x),
                "Asin" => Math.Asin(x), "Acos" => Math.Acos(x), "Atan" => Math.Atan(x),
                "Atan2" => Math.Atan2(x,args[1]), "Remainder" => x%args[1],
                _ => throw new ArgumentException("Unknown math request")
            };
            return new {id=request.GetProperty("id").GetString(),result=Bits(value)};
        }).ToArray();
    }
}
