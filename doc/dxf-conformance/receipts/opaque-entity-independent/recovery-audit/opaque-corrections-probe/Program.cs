using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

var factory=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("NetDxf.Conformance.Program")!.GetMethod("OpaqueFixture",BindingFlags.NonPublic|BindingFlags.Static)!;
string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);bool expectFixed=args[2]=="fixed";var results=new List<object>();int failed=0;
DxfRawDocument Fixture()=>(DxfRawDocument)factory.Invoke(null,new object[]{DxfVersion.AutoCad2018,""})!;
byte[] Save(DxfDocument doc,bool binary){using var stream=new MemoryStream();if(!doc.Save(stream,binary))throw new Exception("Save failed");return stream.ToArray();}
DxfDocument Load(byte[] bytes){using var stream=new MemoryStream(bytes);return DxfDocument.Load(stream)??throw new Exception("Load failed");}
DxfDocument FromRaw(DxfRawDocument raw,bool binary){using var stream=new MemoryStream();raw.Save(stream,binary);return Load(stream.ToArray());}
DxfRawDocument Raw(byte[] bytes){using var stream=new MemoryStream(bytes);return DxfRawDocument.Load(stream);}
foreach(bool binary in new[]{false,true})
{
    var raw=Fixture();var definition=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,"QUALIFIED_FUTURE_CURVE")));
    raw=raw.WithRecord(definition,definition.Tags.Select(t=>t.Code==3?new DxfTag(3,"literal \\U+005CU+0041"):t));var doc=FromRaw(raw,binary);string initial=doc.Classes["QUALIFIED_FUTURE_CURVE"].ApplicationName;byte[] bytes=Save(doc,!binary);string actual=Load(bytes).Classes["QUALIFIED_FUTURE_CURVE"].ApplicationName;bool preserved=initial==actual;
    if(preserved!=expectFixed)failed++;File.WriteAllBytes(Path.Combine(output,"class-"+binary+".dxf"),bytes);results.Add(new{kind="class-literal",binary,initial,actual,preserved,expectedPreserved=expectFixed});
    doc=FromRaw(Fixture(),binary);doc.Layers.Add(new Layer("CORRECTION_LAYER"));raw=Raw(Save(doc,false));var packet=raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name=="QUALIFIED_FUTURE_CURVE");raw=raw.WithRecord(packet,packet.Tags.Append(new DxfTag(1003,"CORRECTION_LAYER")));doc=FromRaw(raw,binary);var entity=doc.Entities.OpaqueEntities.Single();var layer=doc.Layers["CORRECTION_LAYER"];bool bound=entity.References.Contains(layer);bool removed=doc.Layers.Remove(layer);
    if(!bound||removed==expectFixed)failed++;results.Add(new{kind="xdata-layer-removal",binary,bound,removed,expectedRemoved=!expectFixed});
}
File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{cases=results.Count,failed,expectFixed,librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),results},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(results.Count+" before/after controls; "+failed+" unexpected observations.");Environment.ExitCode=failed==0?0:1;
