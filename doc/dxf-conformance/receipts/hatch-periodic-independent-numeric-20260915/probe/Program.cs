using System.Text.Json;
using netDxf;
using netDxf.Entities;
var cases=JsonDocument.Parse(File.ReadAllText(args[0])).RootElement;
var result=new List<object>();
foreach(var item in cases.EnumerateArray()) {
 string name=item.GetProperty("name").GetString()!;
 var controls=item.GetProperty("controls").EnumerateArray().Select(p=>new Vector3(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble())).ToArray();
 var weights=item.GetProperty("weights").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
 var knots=item.GetProperty("knots").EnumerateArray().Select(x=>x.GetDouble()).ToArray();
 short degree=item.GetProperty("degree").GetInt16(); int precision=item.GetProperty("precision").GetInt32();
 try {
  var source=new Spline(controls,weights,knots,degree,true);
  var curve=(Spline)new HatchBoundaryPath.Spline(source).ConvertTo();
  var samples=curve.PolygonalVertexes(precision);
  var reverse=(Spline)curve.Clone();reverse.Reverse();var reversed=reverse.PolygonalVertexes(precision);
  result.Add(new{name,samples=samples.Select(x=>new[]{x.X,x.Y,x.Z}),reversed=reversed.Select(x=>new[]{x.X,x.Y,x.Z}),error=(string?)null});
 } catch(Exception e){result.Add(new{name,error=e.GetType().Name+": "+e.Message});}
}
File.WriteAllText(args[1],JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));
