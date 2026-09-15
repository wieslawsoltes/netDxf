using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using netDxf.IO;
namespace NetDxf.Conformance;
internal static partial class Program {
private static readonly List<object> results = new();
private static int failures;
private static int Main(string[] args){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);RunSourceAmbiguityTests();File.WriteAllText(args[0],JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));return failures==0?0:1;}
private static void Run(string name,Action test){try{test();results.Add(new{name,passed=true,error=(string?)null});Console.WriteLine("PASS "+name);}catch(Exception error){failures++;results.Add(new{name,passed=false,error=error.ToString()});Console.WriteLine("FAIL "+name+": "+error);}}
private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
private static void Equal<T>(T expected,T actual,string message){if(!EqualityComparer<T>.Default.Equals(expected,actual))throw new InvalidOperationException($"{message}: expected {expected}, actual {actual}.");}
private static DxfRawRecord SourceReferenceRecord(DxfRawDocument raw,string handle)=>raw.Sections.Where(s=>s.Name!="HEADER").SelectMany(s=>s.Records).Single(r=>r.Tags.TakeWhile(t=>t.Code!=100).Any(t=>(t.Code==5||t.Code==105)&&Equals(t.Value,handle)));
private static byte[] SectionManagerNativeBytes(){using var manifest=JsonDocument.Parse(File.ReadAllText("tests/fixtures/section/manifest.json"));using var file=File.OpenRead("tests/fixtures/section/LiveSection1.dxf.gz");using var gzip=new GZipStream(file,CompressionMode.Decompress);using var bytes=new MemoryStream();gzip.CopyTo(bytes);Equal(manifest.RootElement.GetProperty("source_sha256").GetString(),Convert.ToHexString(SHA256.HashData(bytes.ToArray())).ToLowerInvariant(),"Pinned native section source hash");return bytes.ToArray();}
}
