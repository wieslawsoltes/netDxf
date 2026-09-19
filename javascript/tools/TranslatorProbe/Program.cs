// Development-only feasibility test. Nothing emitted here is counted as a completed port.
using System.Security.Cryptography;
using System.Text.Json;
using Transpose.Compiler.Library;

if(args.Length != 2) throw new ArgumentException("Usage: TranslatorProbe <pinned-source-root> <artifact-output>");
string root=Path.GetFullPath(args[0]), output=Path.GetFullPath(args[1]);
Directory.CreateDirectory(output);
var files=Directory.EnumerateFiles(Path.Combine(root,"netDxf"),"*.cs",SearchOption.AllDirectories)
    .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "bin" or "obj")).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
if(files.Length!=510)throw new InvalidDataException("Expected all 510 pinned library source files.");
var request=new CompilationRequest("netDxf.NativeProbe").WithRuntime()
    .WithPackageReference("Transpose.BCL","26.9.4872");
foreach(string file in files)request.WithSourceFile(Path.GetRelativePath(root,file).Replace('\\','/'),File.ReadAllText(file));
request.WithSourceFile("ProbeEntry.cs","""
using System;
using System.IO;
using netDxf;
using netDxf.Entities;
public static class ProbeEntry {
    public static void Main() {
        var doc = new DxfDocument();
        doc.Entities.Add(new Line(new Vector3(1,2,3),new Vector3(4,5,6)));
        using(var stream=new MemoryStream()) {
            if(!doc.Save(stream,false)) throw new Exception("Typed save failed.");
            stream.Position=0;
            var loaded=DxfDocument.Load(stream);
            if(loaded==null || loaded.Entities.Count!=1) throw new Exception("Typed reload failed.");
            Console.WriteLine("NATIVE_TYPED_PROBE_OK");
        }
    }
}
""");
var timer=System.Diagnostics.Stopwatch.StartNew();
try {
    var result=TransposeCompilerLibrary.Compile(request);
    var errors=result.Errors.Select(x=>x.ToString()).ToArray();
    File.WriteAllText(Path.Combine(output,"diagnostics.txt"),string.Join("\n",errors));
    File.WriteAllText(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new {
        sourceRef="3496ab91893a1e4ec9261b4833479f1799149cdc",sourceFiles=files.Length,
        compiler="Transpose.Compiler.Library/26.9.5288",bcl="Transpose.BCL/26.9.4872",
        success=result.Success,seconds=timer.Elapsed.TotalSeconds,errors,
        fullParityVerified=false,scope="Feasibility only; not production output or parity evidence.",
        inputs=files.Select(p=>new {path=Path.GetRelativePath(root,p),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()})
    },new JsonSerializerOptions{WriteIndented=true}));
    if(result.Success)File.WriteAllText(Path.Combine(output,"probe.js"),result.Javascript);
    Console.WriteLine($"Whole-library translation probe: {files.Length} files; success={result.Success}; errors={errors.Length}.");
    foreach(var error in errors.Take(25))Console.WriteLine(error);
    return result.Success?0:1;
} catch(Exception error) {
    File.WriteAllText(Path.Combine(output,"fatal.txt"),error.ToString());
    throw;
}
