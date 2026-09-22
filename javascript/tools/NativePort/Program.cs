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
    internal static bool DimensionMode;
    internal static readonly HashSet<INamedTypeSymbol> DimensionTypes = new(SymbolEqualityComparer.Default);
    internal static CSharpCompilation Compilation = null!;
    internal static readonly Dictionary<INamedTypeSymbol, string> Files = new(SymbolEqualityComparer.Default);
    internal static readonly Dictionary<IMethodSymbol, string> Names = new(SymbolEqualityComparer.Default);
    internal static readonly Dictionary<IMethodSymbol, int> Constructors = new(SymbolEqualityComparer.Default);
    internal static bool Struct(ITypeSymbol? type) => type != null && type.TypeKind == TypeKind.Struct && type.ContainingNamespace.ToString() == "netDxf";
    internal static string Q(string? text) => JsonSerializer.Serialize(text);
    internal static string TypeName(ITypeSymbol? t) => t?.Name ?? throw new Exception("Unbound type.");
    public static void Main(string[] args)
    {
        Root=Path.GetFullPath(args[0]);Js=Path.GetFullPath(args[1]);string refs=args[2];
        bool check=args.Contains("--check");
        DimensionMode=args.Contains("--dimensions");
        var selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/selection.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray();
        var dimensionFiles = DimensionMode ? JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/dimensions.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray() : Array.Empty<string>();
        selected=selected.Concat(dimensionFiles).ToArray();
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
        if(DimensionMode)foreach(var tree in trees)foreach(var node in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var symbol=(INamedTypeSymbol)Compilation.GetSemanticModel(tree).GetDeclaredSymbol(node)!;
            string file=Path.GetRelativePath(Root,tree.FilePath).Replace('\\','/');
            string primary=symbol.ContainingNamespace.ToString().Replace('.','/')+"/"+symbol.Name+".cs";
            if(File.Exists(Path.Combine(Js,primary[..^3]+".js")))file=primary;
            if(!Files.ContainsKey(symbol) && File.Exists(Path.Combine(Js,file[..^3]+".js")))Files.Add(symbol,file);
        }
        var outputs=new List<(string file,string text)>();var inventory=new List<object>();
        foreach(var (file,node,model) in declarations)
        {
            if(DimensionMode&&!dimensionFiles.Contains(file))continue;
            var emitter=new Emitter(node,model);string text=emitter.Emit();
            outputs.Add((file[..^3]+".js",text));inventory.Add(new {source=file,target=file[..^3]+".js",sourceSha256=Hash(File.ReadAllBytes(Path.Combine(Root,file))),
                members=emitter.MemberMappings,outputSha256=Hash(Encoding.UTF8.GetBytes(text))});
        }
        var manifest=new {schemaVersion=1,sourceRef="3496ab91893a1e4ec9261b4833479f1799149cdc",generator="tools/NativePort/Program.cs",files=inventory};
        outputs.Add((DimensionMode?"dimension-port-manifest.json":"native-port-manifest.json",JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true})+"\n"));
        // Commit output only after every selected method successfully lowers.
        foreach(var (file,text) in outputs)
        {
            string target=Path.Combine(Js,file);
            if(check){if(!File.Exists(target)||File.ReadAllText(target)!=text)throw new Exception("Generated native source drift: "+file);}
            else{Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.WriteAllText(target,text,new UTF8Encoding(false));}
        }
        Console.WriteLine($"Native source lowering: {declarations.Count} types, {Names.Count} methods/operators; {(check?"exact regeneration verified":"emitted")}.");
    }
    private static string Hash(byte[] b)=>Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
}
