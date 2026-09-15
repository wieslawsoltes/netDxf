using System.Numerics;
using System.Reflection;
using System.Text.Json;
using netDxf.Entities;
using Vector3 = netDxf.Vector3;
var input=JsonDocument.Parse(File.ReadAllText(args[0]));
var results=new List<object>();
foreach(var item in input.RootElement.EnumerateArray())
{
    string name=item.GetProperty("name").GetString();
    try
    {
        if(item.GetProperty("kind").GetString()=="ratio")
        {
            var type=typeof(PeriodicSplineExactEvaluation).GetNestedType("Rational",BindingFlags.NonPublic);
            var ctor=type.GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(BigInteger),typeof(BigInteger),typeof(bool)},null);
            object value=ctor.Invoke(new object[]{BigInteger.Parse(item.GetProperty("numerator").GetString()),BigInteger.Parse(item.GetProperty("denominator").GetString()),true});
            double result=(double)type.GetMethod("ToDouble",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(value,null);
            results.Add(new{name,bits=BitConverter.DoubleToInt64Bits(result).ToString("X16")});
        }
        else
        {
            var controls=item.GetProperty("controls").EnumerateArray().Select(v=>new Vector3(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble())).ToArray();
            var weights=item.GetProperty("weights").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            var knots=item.GetProperty("knots").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
            var result=PeriodicSplineExactEvaluation.Evaluate(controls,weights,knots,item.GetProperty("degree").GetInt32(),item.GetProperty("parameter").GetDouble(),item.GetProperty("first").GetInt32());
            results.Add(new{name,bits=new[]{result.X,result.Y,result.Z}.Select(v=>BitConverter.DoubleToInt64Bits(v).ToString("X16")).ToArray()});
        }
    }
    catch(Exception error){results.Add(new{name,error=error.ToString()});}
}
File.WriteAllText(args[1],JsonSerializer.Serialize(results));
