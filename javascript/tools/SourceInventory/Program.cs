// Development-only: inspect the pinned C# source with Roslyn, not regex API inference.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Blob(byte[] bytes) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes("blob " + bytes.Length + "\0").Concat(bytes).ToArray())).ToLowerInvariant();
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
    private static IEnumerable<string> Files(string root) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .Where(p => !p.Replace('\\','/').Split('/').Any(s => s is "bin" or "obj" or ".git"));
    public static void Main(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("Usage: SourceInventory <source-root> <library-assembly> <javascript-root> [--generate]");
        string root = Path.GetFullPath(args[0]), js = Path.GetFullPath(args[2]);
        var assembly = Assembly.LoadFrom(args[1]);
        bool generate = args.Contains("--generate");
        var files = new List<object>(); var enums = new List<object>(); var fixtures = new List<object>();
        var lockRows = new List<string>();
        var generated = new List<string>();
        foreach (var path in new[] { "netDxf", "tests", "TestDxfDocument" }.SelectMany(p => Files(Path.Combine(root,p))).OrderBy(p => Relative(root,p),StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(path); string relative = Relative(root,path), blob = Blob(bytes);
            lockRows.Add(relative + "\0" + blob + "\n");
            if (path.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)) fixtures.Add(new { path = relative, sha256 = Hash(bytes), gitBlob = blob, length = bytes.Length });
            if (!path.EndsWith(".cs",StringComparison.Ordinal)) continue;
            var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
            var types = syntax.DescendantNodes().OfType<BaseTypeDeclarationSyntax>().Select(t => new {
                name = t.Identifier.ValueText, kind = t.Kind().ToString(), modifiers = t.Modifiers.ToString(),
                members = t is TypeDeclarationSyntax td ? td.Members.Select(m => m switch {
                    MethodDeclarationSyntax v => new { kind = "method", name = v.Identifier.ValueText, signature = v.Modifiers + " " + v.ReturnType + " " + v.Identifier + v.TypeParameterList + v.ParameterList },
                    ConstructorDeclarationSyntax v => new { kind = "constructor", name = v.Identifier.ValueText, signature = v.Modifiers + " " + v.Identifier + v.ParameterList },
                    PropertyDeclarationSyntax v => new { kind = "property", name = v.Identifier.ValueText, signature = v.Modifiers + " " + v.Type + " " + v.Identifier },
                    IndexerDeclarationSyntax v => new { kind = "indexer", name = "Item", signature = v.Modifiers + " " + v.Type + " this" + v.ParameterList },
                    OperatorDeclarationSyntax v => new { kind = "operator", name = v.OperatorToken.Text, signature = v.Modifiers + " " + v.ReturnType + " operator " + v.OperatorToken + v.ParameterList },
                    ConversionOperatorDeclarationSyntax v => new { kind = "conversion", name = v.ImplicitOrExplicitKeyword.Text, signature = v.Modifiers + " " + v.ImplicitOrExplicitKeyword + " operator " + v.Type + v.ParameterList },
                    FieldDeclarationSyntax v => new { kind = "field", name = string.Join(",",v.Declaration.Variables.Select(d=>d.Identifier.ValueText)), signature = v.Modifiers + " " + v.Declaration },
                    EventFieldDeclarationSyntax v => new { kind = "event", name = string.Join(",",v.Declaration.Variables.Select(d=>d.Identifier.ValueText)), signature = v.Modifiers + " event " + v.Declaration },
                    EventDeclarationSyntax v => new { kind = "event", name = v.Identifier.ValueText, signature = v.Modifiers + " event " + v.Type + " " + v.Identifier },
                    _ => null
                }).Where(v=>v!=null).ToArray() : Array.Empty<object>()
            }).ToArray();
            var methods = syntax.DescendantNodes().OfType<MethodDeclarationSyntax>().Select(m=>m.Identifier.ValueText).Distinct().ToArray();
            files.Add(new { source = relative, target = relative[..^3]+".js", sha256 = Hash(bytes), gitBlob = blob, types, methods });
            if (!relative.StartsWith("netDxf/",StringComparison.Ordinal)) continue;
            var enumNodes = syntax.DescendantNodes().OfType<EnumDeclarationSyntax>().ToArray();
            bool enumOnly = enumNodes.Length > 0 && !syntax.DescendantNodes().Any(n => n is TypeDeclarationSyntax or DelegateDeclarationSyntax);
            if (!enumOnly) continue;
            var output = new StringBuilder("// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.\n// Generated from the pinned C# source by tools/SourceInventory. Do not hand-edit.\n");
            foreach (var node in enumNodes)
            {
                string ns = string.Join(".",node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(n=>n.Name.ToString()));
                string fullName = ns + "." + node.Identifier.ValueText;
                var type = assembly.GetType(fullName,true)!;
                var values = type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(f=>f.MetadataToken).ToDictionary(f=>f.Name,f=>f.GetRawConstantValue());
                var strings = node.Members.SelectMany(m=>m.AttributeLists.SelectMany(l=>l.Attributes)
                    .Where(a=>a.Name.ToString() is "StringValue" or "StringValueAttribute")
                    .Select(a=>new { key = Convert.ToString(values[m.Identifier.ValueText],CultureInfo.InvariantCulture)!, value = ((LiteralExpressionSyntax)a.ArgumentList!.Arguments[0].Expression).Token.ValueText }))
                    .ToDictionary(x=>x.key,x=>x.value);
                output.Append("export const ").Append(node.Identifier.ValueText).Append(" = Object.freeze(").Append(JsonSerializer.Serialize(values,Json)).Append(");\n");
                if (strings.Count > 0) output.Append("export const ").Append(node.Identifier.ValueText).Append("StringValues = Object.freeze(").Append(JsonSerializer.Serialize(strings,Json)).Append(");\n");
                enums.Add(new { source = relative, name = fullName, export = node.Identifier.ValueText, values, strings });
            }
            if (generate)
            {
                string target = Path.Combine(js,relative[..^3]+".js"); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target,output.ToString().Replace("\r\n","\n"));
                generated.Add(relative[..^3]+".js");
            }
        }
        string outDir = Path.Combine(js,"artifacts","inventory"); Directory.CreateDirectory(outDir);
        File.WriteAllText(Path.Combine(outDir,"source-inventory.json"),JsonSerializer.Serialize(new { schemaVersion = 1,
            sourceFingerprint = Hash(Encoding.UTF8.GetBytes(string.Concat(lockRows))), files, enums, fixtures },Json));
        Console.WriteLine($"Inventoried {files.Count} C# files, {enums.Count} enum types and {fixtures.Count} original DXF fixtures.");
        if (generate)
        {
            GenerateEncodings(js);
            string enumExports = string.Concat(generated.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>"export * from './"+p+"';\n"));
            File.WriteAllText(Path.Combine(js,"Enums.generated.js"),"// Generated exports for mirrored enum files.\n"+enumExports);
            generated.Add("runtime/CodePages.generated.js"); generated.Add("Enums.generated.js");
            File.WriteAllText(Path.Combine(js,"generated-manifest.json"),JsonSerializer.Serialize(new { schemaVersion=1,
                sourceFingerprint=Hash(Encoding.UTF8.GetBytes(string.Concat(lockRows))),
                files=generated.OrderBy(p=>p,StringComparer.Ordinal).Select(p=>new { path=p,sha256=Hash(File.ReadAllBytes(Path.Combine(js,p))) }) },Json)+"\n");
        }
    }
    // Compact lossless run encoding for Unicode lookup tables. It is not a character fallback.
    private static object[] Pack(string text)
    {
        var result=new List<object>(); var literal=new StringBuilder();
        void Flush() { if(literal.Length>0) { result.Add(literal.ToString());literal.Clear(); } }
        for(int i=0;i<text.Length;)
        {
            int repeated=1,ascending=1;
            while(i+repeated<text.Length && text[i+repeated]==text[i]) repeated++;
            while(i+ascending<text.Length && text[i+ascending]==text[i]+ascending) ascending++;
            int length=Math.Max(repeated,ascending);
            if(length>=4) { Flush();result.Add(new int[] { repeated>=ascending ? 0 : 1,text[i],length });i+=length; }
            else literal.Append(text[i++]);
        }
        Flush();return result.ToArray();
    }
    private static void GenerateEncodings(string js)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var rows = new SortedDictionary<int,object>();
        string bmp = new string(Enumerable.Range(0,65536).Where(c=>c<0xD800 || c>0xDFFF).Select(c=>(char)c).ToArray());
        foreach (var info in Encoding.GetEncodings().OrderBy(e=>e.CodePage))
        {
            Encoding enc;
            try { enc = Encoding.GetEncoding(info.CodePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback); }
            catch (ArgumentException) { continue; }
            catch (NotSupportedException) { continue; }
            if (!enc.IsSingleByte && !new[] { 932, 936, 949, 950, 1361 }.Contains(info.CodePage)) continue;
            try { if (!enc.GetBytes("0\r\nA\0").SequenceEqual(new byte[] {48,13,10,65,0})) continue; }
            catch (EncoderFallbackException) { continue; }
            var singles = new char[256]; var leads = new SortedDictionary<int,string>();
            var reverse = new Dictionary<char,int>();
            for(int b=0;b<256;b++)
            {
                try { string s=enc.GetString(new byte[] {(byte)b}); singles[b]=s.Length==1?s[0]:'\uffff'; }
                catch(DecoderFallbackException) { singles[b]='\uffff'; }
                if(singles[b]!='\uffff') reverse[singles[b]]=b;
            }
            if(!enc.IsSingleByte) for(int b=0;b<256;b++)
            {
                if(singles[b]!='\uffff') continue;
                var trail=new char[256]; bool any=false;
                for(int c=0;c<256;c++)
                {
                    try { string s=enc.GetString(new byte[] {(byte)b,(byte)c}); trail[c]=s.Length==1?s[0]:'\uffff'; }
                    catch(DecoderFallbackException) { trail[c]='\uffff'; }
                    if(trail[c]!='\uffff') {any=true;reverse[trail[c]]=(b<<8)|c;}
                }
                if(any) leads[b]=new string(trail);
            }
            var replacement=Encoding.GetEncoding(info.CodePage,new EncoderReplacementFallback("AA"),DecoderFallback.ExceptionFallback);
            var overrides=new SortedDictionary<int,int>();
            foreach(char c in bmp)
            {
                byte[] encoded=replacement.GetBytes(new string(c,1));
                if(encoded.Length==2 && encoded[0]==65 && encoded[1]==65) continue;
                if(encoded.Length>2) throw new InvalidDataException("Unsupported variable-width table " + info.CodePage);
                int value=encoded.Length==0 ? -1 : encoded.Length==1 ? encoded[0] : (encoded[0]<<8)|encoded[1];
                if(value==0 && c!=0) continue;
                if(!reverse.TryGetValue(c,out int previous) || value!=previous) overrides[c]=value;
            }
            rows.Add(info.CodePage,new { single=Pack(new string(singles)), leads=leads.ToDictionary(p=>p.Key,p=>Pack(p.Value)), overrides });
        }
        var compact=new JsonSerializerOptions {Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
        File.WriteAllText(Path.Combine(js,"runtime","CodePages.generated.js"),"// Generated by tools/SourceInventory from .NET CodePagesEncodingProvider.\n// Development data source: pinned .NET 8 oracle; no .NET dependency at runtime.\nexport const CodePages = "+JsonSerializer.Serialize(rows,compact)+";\n");
        Console.WriteLine("Generated "+rows.Count+" ASCII-compatible single-byte/DBCS code pages.");
    }
}
