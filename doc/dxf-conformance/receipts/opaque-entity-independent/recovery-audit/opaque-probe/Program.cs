using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.IO;
using netDxf.Tables;

internal static partial class Program
{
    private const string Name="QUALIFIED_FUTURE_CURVE";
    private static MethodInfo Factory=null!;
    private static string Output="";
    private static readonly List<object> Results=new();
    private static int Failed;
    private static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
    private static void Run(string name,Action action){try{action();Results.Add(new{name,passed=true,error=""});Console.WriteLine("PASS "+name);}catch(Exception error){Failed++;Results.Add(new{name,passed=false,error=error.ToString()});Console.WriteLine("FAIL "+name+": "+error.Message);}}
    private static long Seed(DxfDocument doc)=>Convert.ToInt64(typeof(DxfDocument).GetProperty("NumHandles",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(doc));
    private static DxfRawDocument RawFixture(DxfVersion version)=>(DxfRawDocument)Factory.Invoke(null,new object[]{version,""})!;
    private static DxfRawRecord Packet(DxfRawDocument raw)=>raw.Sections.SelectMany(s=>s.Records).Single(r=>r.Name==Name);
    private static byte[] Save(DxfDocument doc,bool binary){using var output=new MemoryStream();Check(doc.Save(output,binary),"Save failed.");return output.ToArray();}
    private static DxfDocument Load(byte[] bytes){using var input=new MemoryStream(bytes);return DxfDocument.Load(input)??throw new Exception("Load failed.");}
    private static DxfDocument Load(DxfRawDocument raw,bool binary){using var input=new MemoryStream();raw.Save(input,binary);return Load(input.ToArray());}
    private static DxfRawDocument Raw(byte[] bytes){using var input=new MemoryStream(bytes);return DxfRawDocument.Load(input);}
    private static DxfOpaqueEntity Opaque(DxfDocument doc)=>doc.Entities.OpaqueEntities.Single();
    private static bool Same(IEnumerable<DxfTag> left,IEnumerable<DxfTag> right){var a=left.ToArray();var b=right.ToArray();return a.Length==b.Length&&a.Zip(b).All(p=>p.First.Code==p.Second.Code&&(p.First.Value is byte[] bytes?bytes.SequenceEqual((byte[])p.Second.Value):Equals(p.First.Value,p.Second.Value)));}
    private static DxfTag[] Body(IEnumerable<DxfTag> tags)=>tags.SkipWhile(t=>t.Code!=100||!Equals(t.Value,"AcDbQualifiedFutureCurve")).TakeWhile(t=>t.Code!=1001).ToArray();
    private static void Refuse(DxfDocument doc,bool binary,string label)
    {
        var entity=doc.Entities.OpaqueEntities.First();var tags=entity.SourceTags;var objects=doc.Objects.Items.ToArray();var classes=doc.Classes.ToArray();long seed=Seed(doc);byte[] sentinel={1,7,19,37};
        using var output=new MemoryStream();output.Write(sentinel);output.Position=2;bool saved;try{saved=doc.Save(output,binary);}catch(Exception){saved=false;}
        Check(!saved&&output.Position==2&&output.ToArray().SequenceEqual(sentinel),"Rejected save changed stream bytes or cursor: "+label);
        Check(seed==Seed(doc)&&objects.SequenceEqual(doc.Objects.Items)&&classes.SequenceEqual(doc.Classes)&&ReferenceEquals(tags,entity.SourceTags),"Rejected save changed state: "+label);
        string path=Path.Combine(Output,"sentinel-"+label+".bin");File.WriteAllBytes(path,sentinel);try{saved=doc.Save(path,binary);}catch(Exception){saved=false;}
        Check(!saved&&File.ReadAllBytes(path).SequenceEqual(sentinel)&&Seed(doc)==seed,"Rejected file save changed existing file or seed: "+label);File.Delete(path);
    }
    private static void Main(string[] args)
    {
        Factory=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("NetDxf.Conformance.Program")!.GetMethod("OpaqueFixture",BindingFlags.NonPublic|BindingFlags.Static)!;
        Output=Path.GetFullPath(args[1]);Directory.CreateDirectory(Output);
        foreach(var version in new[]{DxfVersion.AutoCad2000,DxfVersion.AutoCad2004,DxfVersion.AutoCad2007,DxfVersion.AutoCad2010,DxfVersion.AutoCad2013,DxfVersion.AutoCad2018})foreach(bool input in new[]{false,true})
        {
            string label=version+"-"+input;
            Run(label+"-exact-packet",()=>
            {
                var raw=RawFixture(version);var doc=Load(raw,input);var entity=Opaque(doc);var source=entity.SourceTags;
                Check(Same(Packet(raw).Tags,source),"Reader changed complete source packet.");Check(ReferenceEquals(doc.GetObjectByHandle(entity.SourceHandle),entity),"Physical source registration differs.");
                foreach(bool binary in new[]{false,true}){var bytes=Save(doc,binary);Check(Same(source,Packet(Raw(bytes)).Tags),"No-op output changed source tags.");Check(Same(source,Opaque(Load(bytes)).SourceTags),"No-op reload changed source tags.");File.WriteAllBytes(Path.Combine(Output,label+"-exact-"+binary+".dxf"),bytes);}
                Check(ReferenceEquals(source,entity.SourceTags),"Save replaced original snapshot.");
            });
            Run(label+"-common-overlay",()=>
            {
                var doc=Load(RawFixture(version),input);var entity=Opaque(doc);var source=entity.SourceTags;var before=source.ToArray();
                entity.Color=new AciColor(17,39,211);entity.Layer=new Layer("OPAQUE_INDEPENDENT_Ω");entity.LinetypeScale=7.25;entity.IsVisible=false;entity.Lineweight=Lineweight.W70;
                entity.ProxyGraphics=Enumerable.Range(0,260).Select(i=>(byte)i).ToArray();if(version>=DxfVersion.AutoCad2004)entity.ColorName="Book\\U+0041 Ω";if(version>=DxfVersion.AutoCad2007)entity.ShadowMode=EntityShadowMode.Ignore;
                entity.XData["OPAQUE_TEST"].XDataRecord.Add(new XDataRecord(XDataCode.String,"literal \\U+0041 Ω"));
                var bytes=Save(doc,!input);var other=Opaque(Load(bytes));Check(Same(before,entity.SourceTags)&&ReferenceEquals(source,entity.SourceTags),"Overlay changed original snapshot.");Check(Same(Body(before),Body(other.SourceTags)),"Common edit changed private body.");
                Check(other.Color.R==17&&other.Color.G==39&&other.Color.B==211&&other.Layer.Name==entity.Layer.Name&&other.LinetypeScale==7.25&&!other.IsVisible&&other.Lineweight==Lineweight.W70,"Common appearance projection differs.");
                Check(other.ColorName==entity.ColorName&&other.ShadowMode==entity.ShadowMode&&other.ProxyGraphics.SequenceEqual(entity.ProxyGraphics),"Color-name/shadow/proxy overlay differs.");Check(Equals(other.XData["OPAQUE_TEST"].XDataRecord.Last().Value,entity.XData["OPAQUE_TEST"].XDataRecord.Last().Value),"Mutable XData string changed.");
                Check(other.SourceTags.Any(t=>t.Code==300&&Equals(t.Value,"unknown common field")),"Uninterpreted common field was dropped.");File.WriteAllBytes(Path.Combine(Output,label+"-overlay.dxf"),bytes);
            });
            Run(label+"-class-pinning",()=>
            {
                var doc=Load(RawFixture(version),input);var entity=Opaque(doc);var declaration=doc.Classes[Name];string application=declaration.ApplicationName;declaration.ApplicationName="changed";Refuse(doc,!input,label+"-class-change");declaration.ApplicationName=application;
                int index=doc.Classes.IndexOf(declaration);doc.Classes[index]=(DxfClass)declaration.Clone();Refuse(doc,input,label+"-class-identity");doc.Classes[index]=declaration;
                var target=version==DxfVersion.AutoCad2000?DxfVersion.AutoCad2018:DxfVersion.AutoCad2000;var report=doc.AnalyzeVersionCompatibility(target);Check(report.Diagnostics.Any(d=>ReferenceEquals(d.SourceObject,entity)&&d.Code=="STORED_SOURCE_PROFILE"&&d.PropertyPath=="SourceVersion"),"Opaque source profile diagnostic missing.");doc.DrawingVariables.AcadVer=target;Refuse(doc,input,label+"-profile");doc.DrawingVariables.AcadVer=version;Save(doc,input);
            });
            Run(label+"-class-escaped-text",()=>
            {
                var raw=RawFixture(version);var record=raw.Sections.Single(s=>s.Name=="CLASSES").Records.Single(r=>r.Tags.Any(t=>t.Code==1&&Equals(t.Value,Name)));
                raw=raw.WithRecord(record,record.Tags.Select(t=>t.Code==3?new DxfTag(3,"literal \\U+005CU+0041 \\U+03A9"):t));var doc=Load(raw,input);string before=doc.Classes[Name].ApplicationName;Check(before=="literal \\U+0041 Ω","Escaped CLASS fixture decode differs.");
                byte[] bytes=Save(doc,!input);var other=Load(bytes);Check(other.Classes[Name].ApplicationName==before,"CLASS ApplicationName changed across output: "+before+" -> "+other.Classes[Name].ApplicationName);File.WriteAllBytes(Path.Combine(Output,label+"-class-escape.dxf"),bytes);
            });
        }
        Boundaries();
        PlacementsAndBudget();
        File.WriteAllText(Path.Combine(Output,"results.json"),JsonSerializer.Serialize(new{cases=Results.Count,failed=Failed,librarySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(DxfDocument).Assembly.Location))).ToLowerInvariant(),factorySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0]))).ToLowerInvariant(),results=Results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(Results.Count+" cases; "+Failed+" failed.");Environment.ExitCode=Failed==0?0:1;
    }
}
