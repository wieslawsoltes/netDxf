using netDxf; using netDxf.Entities; using System.Text.Json; using System.Text.Json.Serialization; using System.Security.Cryptography;
var points = new[] { new Vector3(0,0,0),new Vector3(4,7,0),new Vector3(10,1,0),new Vector3(7,-5,0),new Vector3(-3,-2,0) };
var rows=new List<object>();
foreach(bool offset in new[]{false,true}) {
 var knots=Enumerable.Range(0,10).Select(i=>offset?1e16+i*2.0:i*double.Epsilon).ToArray(); var spline=new Spline(points,Enumerable.Repeat(1.0,points.Length),knots,(short)2,true); string error=null; double[][] samples=null;
 try {samples=spline.PolygonalVertexes(64).Select(v=>new[]{v.X,v.Y,v.Z}).ToArray();}catch(Exception ex){error=ex.GetType().Name+": "+ex.Message;}
 rows.Add(new{offset,knots,error,samples,distinct_samples=samples==null?0:samples.Select(p=>string.Join(",",p.Select(v=>v.ToString("R")))).Distinct().Count()});
}
var assembly=typeof(Spline).Assembly.Location;Console.WriteLine(JsonSerializer.Serialize(new{dll_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly))).ToLowerInvariant(),rows},new JsonSerializerOptions{WriteIndented=true,NumberHandling=JsonNumberHandling.AllowNamedFloatingPointLiterals}));
