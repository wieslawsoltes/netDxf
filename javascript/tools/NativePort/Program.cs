// Development-only semantic lowering for an explicitly audited source cluster.
// Unsupported syntax, foreign methods and unbound symbols are fatal; no stubs are emitted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    internal static string Root = "", Js = "";
    internal static bool DimensionMode, GteMode;
    internal static readonly HashSet<INamedTypeSymbol> GteTypes = new(SymbolEqualityComparer.Default);
    internal static readonly HashSet<INamedTypeSymbol> DimensionTypes = new(SymbolEqualityComparer.Default);
    internal static CSharpCompilation Compilation = null!;
    internal static readonly Dictionary<INamedTypeSymbol, string> Files = new(SymbolEqualityComparer.Default);
    internal static readonly Dictionary<IMethodSymbol, string> Names = new(SymbolEqualityComparer.Default);
    internal static readonly Dictionary<IMethodSymbol, int> Constructors = new(SymbolEqualityComparer.Default);
    internal static bool Struct(ITypeSymbol? type) => type != null && type.TypeKind == TypeKind.Struct && (type.ContainingNamespace.ToString() == "netDxf" || GteMode && type.ContainingNamespace.ToString() == "netDxf.GTE");
    internal static string Q(string? text) => JsonSerializer.Serialize(text);
    internal static string TypeName(ITypeSymbol? t) => t?.Name ?? throw new Exception("Unbound type.");
    public static void Main(string[] args)
    {
        Root=Path.GetFullPath(args[0]);Js=Path.GetFullPath(args[1]);string refs=args[2];
        bool check=args.Contains("--check");
        DimensionMode=args.Contains("--dimensions");
        GteMode=args.Contains("--gte");
        if(DimensionMode && GteMode)throw new ArgumentException("Select one source cluster.");
        var selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/selection.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var dimensionFiles = DimensionMode ? JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/dimensions.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray() : Array.Empty<string>();
        var gteFiles = GteMode ? JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/gte.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray() : Array.Empty<string>();
        selected=selected.Concat(dimensionFiles).Concat(gteFiles).ToArray();
        var sourceFiles = Directory.EnumerateFiles(Path.Combine(Root,"netDxf"),"*.cs",SearchOption.AllDirectories)
            .Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(s=>s is "obj" or "bin")).OrderBy(p=>p,StringComparer.Ordinal).ToArray();
        var trees=sourceFiles.Select(p=>CSharpSyntaxTree.ParseText(File.ReadAllText(p),path:p)).ToArray();
        Compilation=CSharpCompilation.Create("NativePort.Input",trees,Directory.GetFiles(refs,"*.dll").Select(p=>MetadataReference.CreateFromFile(p)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var errors=Compilation.GetDiagnostics().Where(d=>d.Severity==DiagnosticSeverity.Error).ToArray();
        if(errors.Length>0)throw new Exception(string.Join("\n",errors.Select(x=>x.ToString())));
        var declarations = new List<(string file,TypeDeclarationSyntax node,SemanticModel model)>();
        foreach(string file in selected)
        {
            // Manifest paths use forward slashes; filesystem paths use host separators.
            var selectedPath=Path.GetFullPath(Path.Combine(Root,file));
            var tree=trees.Single(t=>Path.GetFullPath(t.FilePath)==selectedPath);var model=Compilation.GetSemanticModel(tree);
            foreach(var node in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol=(INamedTypeSymbol)model.GetDeclaredSymbol(node)!;Files.Add(symbol,file);declarations.Add((file,node,model));
                if(dimensionFiles.Contains(file))DimensionTypes.Add(symbol);
                if(gteFiles.Contains(file))GteTypes.Add(symbol);
                foreach(var group in symbol.GetMembers().OfType<IMethodSymbol>().Where(m=>m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion).GroupBy(m=>(m.Name,m.IsStatic)))
                {
                    var methods=group.OrderBy(m=>m.Locations.FirstOrDefault()?.SourceSpan.Start??0).ToArray();
                    for(int i=0;i<methods.Length;i++)Names[methods[i]]=methods.Length==1?group.Key.Name:"$"+group.Key.Name+i;
                }
                int c=0;foreach(var ctor in symbol.InstanceConstructors.Where(c=>!c.IsImplicitlyDeclared).OrderBy(c=>c.Locations.First().SourceSpan.Start))Constructors[ctor]=c++;
            }
        }
        // Existing hand-written modules keep their documented PascalCase APIs. Do not
        // infer bindings for missing paths or treat a placeholder as a dependency.
        if(DimensionMode || GteMode)foreach(var tree in trees)foreach(var node in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var symbol=(INamedTypeSymbol)Compilation.GetSemanticModel(tree).GetDeclaredSymbol(node)!;
            string file=Path.GetRelativePath(Root,tree.FilePath).Replace('\\','/');
            string primary=symbol.ContainingNamespace.ToString().Replace('.','/')+"/"+symbol.Name+".cs";
            if(File.Exists(Path.Combine(Js,primary[..^3]+".js")))file=primary;
            if(!Files.ContainsKey(symbol) && File.Exists(Path.Combine(Js,file[..^3]+".js")))Files.Add(symbol,file);
        }
        var outputs=new List<(string file,string text)>();var inventory=new List<object>();
        foreach(var group in declarations.GroupBy(d=>d.file))
        {
            string file=group.Key;
            if(DimensionMode&&!dimensionFiles.Contains(file) || GteMode&&!gteFiles.Contains(file))continue;
            var emitted=group.Select(d=>{var emitter=new Emitter(d.node,d.model);return (text:emitter.Emit(),members:emitter.MemberMappings,type:((INamedTypeSymbol)d.model.GetDeclaredSymbol(d.node)!).ToDisplayString());}).ToArray();
            string text=emitted.Length==1?emitted[0].text:MergeModules(emitted.Select(e=>e.text));
            outputs.Add((file[..^3]+".js",text));
            if(GteMode)inventory.Add(new {source=file,target=file[..^3]+".js",sourceSha256=Hash(File.ReadAllBytes(Path.Combine(Root,file))),
                types=emitted.Select(e=>new {name=e.type,members=e.members.Select(GteMember).ToArray()}).ToArray(), outputSha256=Hash(Encoding.UTF8.GetBytes(text))});
            else inventory.Add(new {source=file,target=file[..^3]+".js",sourceSha256=Hash(File.ReadAllBytes(Path.Combine(Root,file))),
                members=emitted.SelectMany(e=>e.members).ToList(),outputSha256=Hash(Encoding.UTF8.GetBytes(text))});
        }
        var manifest=new {schemaVersion=1,sourceRef="3496ab91893a1e4ec9261b4833479f1799149cdc",generator="tools/NativePort/Program.cs",files=inventory};
        // .NET 8 indented JSON uses Environment.NewLine. Generated metadata must
        // be identical across hosts; JSON string control characters are escaped,
        // so this changes formatting whitespace only, never values or source hashes.
        string manifestText=JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=!GteMode}).Replace("\r\n","\n")+"\n";
        outputs.Add((GteMode?"gte-port-manifest.json":DimensionMode?"dimension-port-manifest.json":"native-port-manifest.json",manifestText));
        // Commit output only after every selected method successfully lowers.
        foreach(var (file,text) in outputs)
        {
            string target=Path.Combine(Js,file);
            if(check){if(!File.Exists(target)||File.ReadAllText(target)!=text)throw new Exception("Generated native source drift: "+file);}
            else{Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllText(target,text,new UTF8Encoding(false));}
        }
        Console.WriteLine($"Native source lowering: {declarations.Count} types, {Names.Count} methods/operators; {(check?"exact regeneration verified":"emitted")}.");
    }
    private static object GteMember(object value)
    {
        var keys=new[]{"kind","name","signature","isStatic","implementation","returnType","canSet","isAbstract","accessibility"};
        return JsonSerializer.SerializeToElement(value).EnumerateObject().Where(p=>keys.Contains(p.Name))
            .ToDictionary(p=>p.Name,p=>p.Value);
    }
    private static string MergeModules(IEnumerable<string> modules)
    {
        var imports=new List<string>();var bodies=new List<string>();
        foreach(string module in modules){int at=module.IndexOf("\nexport class ",StringComparison.Ordinal);
            foreach(string line in module[..at].Split('\n'))if(line.Length>0&&!imports.Contains(line))imports.Add(line);
            bodies.Add(module[(at+1)..].TrimEnd());}
        string header=string.Join("\n",imports);
        bool hasEnum=imports.Any(line=>line.StartsWith("export const ",StringComparison.Ordinal));
        if(hasEnum)header=header.Replace("\nexport const ","\n\nexport const ");
        return header+(hasEnum?"\n\n":"\n\n\n")+string.Join("\n\n",bodies)+"\n";
    }
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
