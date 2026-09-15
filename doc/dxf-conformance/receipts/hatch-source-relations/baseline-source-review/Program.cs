using System.Text.Json;
using System.Security.Cryptography;
using netDxf;
using netDxf.Entities;
var results=new List<object>();using var manifest=JsonDocument.Parse(File.ReadAllText("manifest.json"));
foreach(var source in manifest.RootElement.EnumerateArray()){
 string file=source.GetProperty("file").GetString()!;string scenario=source.GetProperty("scenario").GetString()!;
 try {var d=DxfDocument.Load(file);var h=d.Entities.Hatches.Single();var refs=h.BoundaryPaths.Single().Entities.Select(e=>e.Handle).ToArray();using var stream=new MemoryStream();bool saved=d.Save(stream,source.GetProperty("binary").GetBoolean());results.Add(new{file,scenario,accepted=true,source_refs=refs,saved,associative=h.Associative});}
 catch(Exception error){results.Add(new{file,scenario,accepted=false,error=error.GetType().Name+": "+error.Message});}
}
Console.WriteLine(JsonSerializer.Serialize(new{library_sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),results},new JsonSerializerOptions{WriteIndented=true}));
