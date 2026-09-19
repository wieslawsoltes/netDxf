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
        var selected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Js,"tools/NativePort/selection.json"))).RootElement.GetProperty("files").EnumerateArray().Select(x=>x.GetString()!).ToArray();
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
            var tree=trees.Single(t=>t.FilePath==Path.Combine(Root,file));var model=Compilation.GetSemanticModel(tree);
            foreach(var node in tree.GetRoot().DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var symbol=(INamedTypeSymbol)model.GetDeclaredSymbol(node)!;Files.Add(symbol,file);declarations.Add((file,node,model));
                foreach(var group in symbol.GetMembers().OfType<IMethodSymbol>().Where(m=>m.MethodKind is MethodKind.Ordinary or MethodKind.UserDefinedOperator or MethodKind.Conversion).GroupBy(m=>(m.Name,m.IsStatic)))
                {
                    var methods=group.OrderBy(m=>m.Locations.FirstOrDefault()?.SourceSpan.Start??0).ToArray();
                    for(int i=0;i<methods.Length;i++)Names[methods[i]]=methods.Length==1?group.Key.Name:"$"+group.Key.Name+i;
                }
                int c=0;foreach(var ctor in symbol.InstanceConstructors.Where(c=>!c.IsImplicitlyDeclared).OrderBy(c=>c.Locations.First().SourceSpan.Start))Constructors[ctor]=c++;
            }
        }
        var outputs=new List<(string file,string text)>();var inventory=new List<object>();
        foreach(var (file,node,model) in declarations)
        {
            var emitter=new Emitter(node,model);string text=emitter.Emit();
            outputs.Add((file[..^3]+".js",text));inventory.Add(new {source=file,target=file[..^3]+".js",sourceSha256=Hash(File.ReadAllBytes(Path.Combine(Root,file))),
                members=emitter.MemberMappings,outputSha256=Hash(Encoding.UTF8.GetBytes(text))});
        }
        var manifest=new {schemaVersion=1,sourceRef="3496ab91893a1e4ec9261b4833479f1799149cdc",generator="tools/NativePort/Program.cs",files=inventory};
        outputs.Add(("native-port-manifest.json",JsonSerializer.Serialize(manifest,new JsonSerializerOptions{WriteIndented=true})+"\n"));
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
internal sealed class Emitter
{
    private readonly TypeDeclarationSyntax node;private readonly SemanticModel model;private readonly INamedTypeSymbol type;
    private readonly HashSet<INamedTypeSymbol> dependencies=new(SymbolEqualityComparer.Default);
    private readonly StringBuilder body=new();private int indent=1;private int temp=0;private ITypeSymbol? returns;private string? initializerReceiver;
    internal readonly List<object> MemberMappings=new();
    internal Emitter(TypeDeclarationSyntax n,SemanticModel m){node=n;model=m;type=(INamedTypeSymbol)m.GetDeclaredSymbol(n)!;}
    private Exception Bad(SyntaxNode n,string reason="Unsupported syntax")=>new Exception($"{reason}: {n.Kind()} at {n.GetLocation().GetLineSpan()}\n{n}");
    private ISymbol? Sym(SyntaxNode n)=>model.GetSymbolInfo(n).Symbol;
    private ITypeSymbol? Typ(SyntaxNode n)=>model.GetTypeInfo(n).Type;
    private void L(string s="")=>body.Append(' ',indent*2).Append(s).Append('\n');
    private string Ref(INamedTypeSymbol t){if(!SymbolEqualityComparer.Default.Equals(t,type))dependencies.Add(t);return t.Name;}
    private static string FieldName(ISymbol s)=>s.DeclaredAccessibility==Accessibility.Public?s.Name:"$"+s.Name;
    private string Copy(ExpressionSyntax e) {
        if(e is ParenthesizedExpressionSyntax p)return "("+Copy(p.Expression)+")";
        if(e is InvocationExpressionSyntax or ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax || Sym(e) is IPropertySymbol)
            return E(e);
        if(e is BinaryExpressionSyntax b && Sym(b) is IMethodSymbol)return E(e);
        return Copy(E(e),Typ(e),e);
    }
    private string Copy(string e,ITypeSymbol? t,SyntaxNode? source=null)=>Program.Struct(t)&&source is not ObjectCreationExpressionSyntax&&source is not ImplicitObjectCreationExpressionSyntax?"Copy("+e+")":e;
    private string Def(ITypeSymbol? t)=>Program.Struct(t)?"new "+Ref((INamedTypeSymbol)t!)+"()":t?.SpecialType==SpecialType.System_Boolean?"false":t?.TypeKind==TypeKind.Enum||IsNumber(t)?"0":"null";
    private static bool IsNumber(ITypeSymbol? t)=>t?.SpecialType is SpecialType.System_Double or SpecialType.System_Single or SpecialType.System_Int32 or SpecialType.System_Int16 or SpecialType.System_Byte or SpecialType.System_Int64 or SpecialType.System_UInt32 or SpecialType.System_UInt16 or SpecialType.System_SByte or SpecialType.System_UInt64;
    private static string Literal(object? v)=>v switch{null=>"null",bool b=>b?"true":"false",string s=>Program.Q(s),char c=>Program.Q(c.ToString()),double d when double.IsNaN(d)=>"DotNetNaN",double d when double.IsPositiveInfinity(d)=>"Infinity",double d when double.IsNegativeInfinity(d)=>"-Infinity",double d=>d.ToString("R",System.Globalization.CultureInfo.InvariantCulture),float f=>((double)f).ToString("R",System.Globalization.CultureInfo.InvariantCulture),long n=>n.ToString()+"n",ulong n=>n.ToString()+"n",_=>Convert.ToString(v,System.Globalization.CultureInfo.InvariantCulture)!};
    internal string Emit()
    {
        string baseName=type.BaseType!=null&&Program.Files.ContainsKey(type.BaseType)?" extends "+Ref(type.BaseType):"";
        L("// C# backing state is prefixed with $; public members retain their original names.");
        foreach(var field in node.Members.OfType<FieldDeclarationSyntax>())foreach(var v in field.Declaration.Variables)
        {
            var s=(IFieldSymbol)model.GetDeclaredSymbol(v)!;string value=s.HasConstantValue?Literal(s.ConstantValue):v.Initializer!=null?Copy(v.Initializer.Value):Def(s.Type);
            if(s.DeclaredAccessibility==Accessibility.Public) MemberMappings.Add(new {kind="field",name=s.Name,accessibility="Public",isStatic=s.IsStatic,returnType=s.Type.ToDisplayString(),constant=s.HasConstantValue});
            L(s.IsConst ? "static get "+FieldName(s)+"() { return "+value+"; }" : (s.IsStatic?"static ":"")+FieldName(s)+" = "+value+";");
        }
        foreach(var p in node.Members.OfType<PropertyDeclarationSyntax>())if(p.AccessorList?.Accessors.All(a=>a.Body==null&&a.ExpressionBody==null)==true)
        {var s=(IPropertySymbol)model.GetDeclaredSymbol(p)!;L((s.IsStatic?"static ":"")+"$property_"+s.Name+" = "+(p.Initializer!=null?Copy(p.Initializer.Value):Def(s.Type))+";");}
        EmitConstructors(baseName.Length>0);
        if(type.TypeKind==TypeKind.Struct)
        {
            var fields=type.GetMembers().OfType<IFieldSymbol>().Where(f=>!f.IsStatic).ToArray();
            L("[CopyValue]() {");indent++;L("const value = new "+type.Name+"();");foreach(var f in fields)L("value."+FieldName(f)+" = this."+FieldName(f)+";");L("return value;");indent--;L("}");
            L("$assign(value) {");indent++;foreach(var f in fields)L("this."+FieldName(f)+" = value."+FieldName(f)+";");L("return this;");indent--;L("}");
        }
        foreach(var member in node.Members)
        {
            switch(member)
            {
                case FieldDeclarationSyntax:case ConstructorDeclarationSyntax:break;
                case PropertyDeclarationSyntax p:EmitProperty(p);break;
                case IndexerDeclarationSyntax i:EmitIndexer(i);break;
                case MethodDeclarationSyntax m:EmitMethod(m,(IMethodSymbol)model.GetDeclaredSymbol(m)!,m.Body,m.ExpressionBody?.Expression);break;
                case OperatorDeclarationSyntax o:EmitMethod(o,(IMethodSymbol)model.GetDeclaredSymbol(o)!,o.Body,o.ExpressionBody?.Expression);break;
                case ConversionOperatorDeclarationSyntax c:EmitMethod(c,(IMethodSymbol)model.GetDeclaredSymbol(c)!,c.Body,c.ExpressionBody?.Expression);break;
                default:throw Bad(member,"Unsupported member");
            }
        }
        foreach(var group in type.GetMembers().OfType<IMethodSymbol>().Where(m=>Program.Names.ContainsKey(m)&&!m.IsAbstract).GroupBy(m=>(m.Name,m.IsStatic)))
        {
            if(group.Count()<2)continue;
            L((group.Key.IsStatic?"static ":"")+group.Key.Name+"(...args) {");indent++;
            foreach(var m in group.OrderByDescending(m=>Specificity(m.Parameters)))
                L("if ("+Match(m.Parameters)+") return "+(m.IsStatic?type.Name:"this")+"."+Program.Names[m]+"(...args);");
            L("throw new ArgumentException("+Program.Q("No matching "+type.Name+"."+group.Key.Name+" overload. Consult native-port-manifest.json.")+");");indent--;L("}");
        }
        var imports=new StringBuilder("// Copyright (c) Daniel Carvajal. MIT License; see package LICENSE.\n// Native JavaScript generated from the pinned C# file by tools/NativePort.\n// Do not edit generated bodies: update the audited lowerer and regenerate.\n");
        string moduleDir=Path.GetDirectoryName(Program.Files[type])!;
        string runtimePrefix=Path.GetRelativePath(moduleDir,"runtime").Replace('\\','/');
        imports.Append("import * as Errors from '../runtime/Errors.js';\nimport { Copy, CopyValue, DotNetMath, List, Tuple, Init, NumberText, Format, DoubleHash, Culture, StringBuilder, Int32, Int16, Byte, Color, GetElement, SetElement, Int32FromBytes, ConstructorTag, DotNetNaN, MultiplyDouble, RemainderDouble, NativeString, MemberwiseClone } from '../runtime/GeometryRuntime.js';\n");
        imports.Append("const { ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArithmeticException, NotSupportedException } = Errors;\n");
        foreach(var d in dependencies.OrderBy(d=>d.ToDisplayString(),StringComparer.Ordinal))
        {
            string path=Program.Files.TryGetValue(d,out var file)?file[..^3]+".js":d.ContainingNamespace.ToString().StartsWith("netDxf")?d.ContainingNamespace.ToString().Replace('.','/')+"/"+d.Name+".js":throw new Exception("Unmapped dependency "+d);
            string rel=Path.GetRelativePath(moduleDir,path).Replace('\\','/');imports.Append("import { ").Append(d.Name).Append(" } from './").Append(rel).Append("';\n");
        }
        imports.Replace("../runtime/",runtimePrefix+"/");
        foreach(var enumNode in node.SyntaxTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>()) {
          var en=(INamedTypeSymbol)model.GetDeclaredSymbol(enumNode)!;
          imports.Append("\nexport const ").Append(en.Name).Append(" = Object.freeze(").Append(JsonSerializer.Serialize(en.GetMembers().OfType<IFieldSymbol>().Where(f=>f.HasConstantValue).ToDictionary(f=>f.Name,f=>f.ConstantValue))).Append(");\n");
        }
        return imports+"\nexport class "+type.Name+baseName+" {\n"+body+"}\n";
    }
    private void EmitConstructors(bool derived)
    {
        var ctors=node.Members.OfType<ConstructorDeclarationSyntax>().ToArray();
        L("constructor(...args) {");indent++;
        if(type.IsAbstract)L("if (new.target === "+type.Name+") throw new NotSupportedException('Cannot construct an abstract class.');");
        if(type.TypeKind==TypeKind.Struct||(!type.IsStatic&&ctors.Length==0))L("if (args.length === 0) "+(derived?"{ super(); return; }":"return;"));
        if(!type.IsStatic&&ctors.Length==0)MemberMappings.Add(new{kind="constructor",signature="",implementation="constructor",accessibility="Public",parameters=Array.Empty<object>()});
        foreach(var c in ctors.OrderByDescending(c=>Specificity(((IMethodSymbol)model.GetDeclaredSymbol(c)!).Parameters)))
        {
            var s=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("if ("+Match(s.Parameters)+") {");indent++;
            if(derived)
            {
                for(int i=0;i<s.Parameters.Length;i++)L("let "+s.Parameters[i].Name+" = "+Copy("args["+i+"]",s.Parameters[i].Type)+";");
                if(c.Initializer==null||!c.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer))throw Bad(c,"Derived this-chaining not audited");
                L("super("+string.Join(", ",c.Initializer.ArgumentList.Arguments.Select(a=>Copy(a.Expression)))+");");
                Block(c.Body!,false);
            }
            else L("this.$ctor"+Program.Constructors[s]+"(...args);");
            L("return;");indent--;L("}");
        }
        L("throw new ArgumentException("+Program.Q("No matching "+type.Name+" constructor. Use "+type.Name+".CreateOverload(signature, ...args) for ambiguous numeric overloads.")+");");indent--;L("}");
        if(!derived)
        {
            foreach(var c in ctors)
            {
                var s=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("$ctor"+Program.Constructors[s]+"("+Parameters(s)+") {");indent++;ParamCopies(s);
                if(c.Initializer!=null)
                {
                    var target=(IMethodSymbol)Sym(c.Initializer)!;
                    L("this.$ctor"+Program.Constructors[target]+"("+Arguments(c.Initializer.ArgumentList.Arguments,target)+");");
                }
                Block(c.Body!,false);indent--;L("}");
                MemberMappings.Add(new{kind="constructor",signature=Signature(s),implementation="$ctor"+Program.Constructors[s],accessibility=s.DeclaredAccessibility.ToString(),parameters=s.Parameters.Select(p=>new{name=p.Name,type=p.Type.ToDisplayString(),refKind=p.RefKind.ToString()}).ToArray()});
            }
            // Exact constructor selectors are compiler-generated and permit unambiguous user migration.
            L("static CreateOverload(signature, ...args) {");indent++;
            if(type.TypeKind==TypeKind.Struct||(!type.IsStatic&&ctors.Length==0))L("if (signature === '' && args.length === 0) return new "+type.Name+"();");
            foreach(var c in ctors)
            {
                var s=(IMethodSymbol)model.GetDeclaredSymbol(c)!;
                L("if (signature === "+Program.Q(Signature(s))+") {");indent++;
                // Avoid running any constructor twice; initialized instance fields are required.
                L("if (!("+Match(s.Parameters)+")) throw new ArgumentException('Arguments do not match the selected constructor.');"); L("return "+type.Name+".$create"+Program.Constructors[s]+"(...args);");indent--;L("}");
            }
            L("throw new ArgumentException('Unknown constructor signature.', 'signature');");indent--;L("}");
            // A private tag calls exactly one constructor while still running JS field initializers.
            // Implement as a constructor dispatch branch injected below, not Object.create (which bypasses fields).
            foreach(var c in ctors)
            {var s=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("static $create"+Program.Constructors[s]+"(...args) { return new "+type.Name+"(ConstructorTag, "+Program.Constructors[s]+", args); }");}
            string marker="  constructor(...args) {\n";
            string dispatch="    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }\n";
            body.Replace(marker,marker+dispatch);
        }
        if(derived) {
          L("static CreateOverload(signature, ...args) {");indent++;
          foreach(var c in ctors) {var s=(IMethodSymbol)model.GetDeclaredSymbol(c)!;
            L("if (signature === "+Program.Q(Signature(s))+") return new "+type.Name+"(...args);");
            MemberMappings.Add(new{kind="constructor",signature=Signature(s),implementation="constructor",accessibility=s.DeclaredAccessibility.ToString(),parameters=s.Parameters.Select(p=>new{name=p.Name,type=p.Type.ToDisplayString(),refKind=p.RefKind.ToString()}).ToArray()});
          }
          L("throw new ArgumentException('Unknown constructor signature.', 'signature');");indent--;L("}");
        }
    }
    private string Signature(IMethodSymbol m)=>string.Join(",",m.Parameters.Select(p=>(p.RefKind!=RefKind.None?p.RefKind.ToString().ToLowerInvariant()+" ":"")+p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::","")));
    private static int Specificity(IEnumerable<IParameterSymbol> ps)=>ps.Sum(p=>p.Type.SpecialType==SpecialType.System_Object?0:p.Type is IArrayTypeSymbol?5:IsNumber(p.Type)?2:p.Type.TypeKind is TypeKind.Class or TypeKind.Struct?8:3);
    private string Match(IEnumerable<IParameterSymbol> parameters)
    {
        var p=parameters.ToArray();int min=p.Count(p=>!p.IsOptional);
        return "args.length "+(min==p.Length?"=== "+p.Length:">= "+min+" && args.length <= "+p.Length)+string.Concat(p.Select((s,i)=>" && "+(s.IsOptional?"(args["+i+"] === undefined || ":"(")+Test("args["+i+"]",s.Type,s.RefKind)+")"));
    }
    private string Test(string a,ITypeSymbol t,RefKind refKind=RefKind.None)
    {
        if(refKind!=RefKind.None)return a+" != null && typeof "+a+" === 'object' && 'value' in "+a;
        if(t is IArrayTypeSymbol at)return a+" === null || "+(at.ElementType.SpecialType==SpecialType.System_Byte?a+" instanceof Uint8Array": "Array.isArray("+a+") || "+a+" instanceof Float64Array");
        if(t.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T)return a+" === null || ("+Test(a,((INamedTypeSymbol)t).TypeArguments[0])+")";
        if(IsNumber(t))return t.SpecialType is SpecialType.System_Double or SpecialType.System_Single ? "typeof "+a+" === 'number'" : "Number.isInteger("+a+")"+(t.SpecialType==SpecialType.System_Byte?" && "+a+" >= 0 && "+a+" <= 255":t.SpecialType==SpecialType.System_Int16?" && "+a+" >= -32768 && "+a+" <= 32767":t.SpecialType==SpecialType.System_Int32?" && "+a+" >= -2147483648 && "+a+" <= 2147483647":"");
        if(t.SpecialType==SpecialType.System_Boolean)return "typeof "+a+" === 'boolean'";
        if(t.SpecialType==SpecialType.System_String)return a+" === null || typeof "+a+" === 'string'";
        if(t.SpecialType==SpecialType.System_Object)return "true";
        if(t.TypeKind==TypeKind.Enum)return "typeof "+a+" === 'number'";
        if(t is INamedTypeSymbol n && Program.Files.ContainsKey(n))return (n.IsReferenceType?a+" === null || ":"")+a+" instanceof "+Ref(n);
        if(t.Name is "IEnumerable" or "IReadOnlyList" or "List")return a+" == null || typeof "+a+"[Symbol.iterator] === 'function'";
        if(t.Name is "Color")return a+" instanceof Color";
        if(t.Name is "IFormatProvider")return a+" === null || typeof "+a+" === 'object' || typeof "+a+" === 'string'";
        throw new Exception("Unmapped overload type "+t);
    }
    private string Parameters(IMethodSymbol m)=>string.Join(", ",m.Parameters.Select(p=>p.Name+(p.HasExplicitDefaultValue?" = "+Literal(p.ExplicitDefaultValue):"")));
    private void ParamCopies(IMethodSymbol m)
    {
      var syntax=m.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
      foreach(var p in m.Parameters)if(p.RefKind==RefKind.None&&Program.Struct(p.Type)) {
        bool mutable=syntax?.DescendantNodes().OfType<IdentifierNameSyntax>().Any(id=>
          SymbolEqualityComparer.Default.Equals(Sym(id),p) && id.Parent is MemberAccessExpressionSyntax access &&
          ((access.Parent is AssignmentExpressionSyntax assign && assign.Left==access) || access.Name.Identifier.ValueText=="IsIdentity" ||
           (access.Parent is InvocationExpressionSyntax && access.Name.Identifier.ValueText is not ("Modulus" or "Equals" or "GetHashCode" or "ToString" or "ToArray")))) == true;
        if(mutable)L(p.Name+" = Copy("+p.Name+");");
      }
    }
    private void Locals(SyntaxNode? n)
    {
        if(n==null)return;
        var vars=n.DescendantNodes().OfType<SingleVariableDesignationSyntax>().Select(x=>x.Identifier.ValueText).Distinct().ToArray();
        if(vars.Length>0)L("let "+string.Join(", ",vars)+";");
    }
    private void EmitMethod(SyntaxNode n,IMethodSymbol s,BlockSyntax? b,ExpressionSyntax? expr)
    {
        MemberMappings.Add(new{kind=s.MethodKind.ToString(),name=s.Name,signature=Signature(s),isStatic=s.IsStatic,implementation=Program.Names[s],accessibility=s.DeclaredAccessibility.ToString(),isAbstract=s.IsAbstract,returnType=s.ReturnType.ToDisplayString(),parameters=s.Parameters.Select(p=>new{name=p.Name,type=p.Type.ToDisplayString(),refKind=p.RefKind.ToString()}).ToArray()});
        if(s.IsAbstract)return;
        returns=s.ReturnType;L((s.IsStatic?"static ":"")+Program.Names[s]+"("+Parameters(s)+") {");indent++;ParamCopies(s);Locals(b??(SyntaxNode?)expr);
        if(b!=null)Block(b,false);else if(expr!=null)L((s.ReturnsVoid?"":"return ")+Copy(expr)+";");else throw Bad(n,"Body missing");
        indent--;L("}");returns=null;
    }
    private void EmitProperty(PropertyDeclarationSyntax p)
    {
        var s=(IPropertySymbol)model.GetDeclaredSymbol(p)!;MemberMappings.Add(new{kind="property",name=s.Name,accessibility=s.DeclaredAccessibility.ToString(),isStatic=s.IsStatic,returnType=s.Type.ToDisplayString(),canSet=s.SetMethod?.DeclaredAccessibility==Accessibility.Public});
        foreach(var a in p.AccessorList?.Accessors??default)
        {
            bool get=a.IsKind(SyntaxKind.GetAccessorDeclaration);string prefix=s.IsStatic?"static ":"";
            L(prefix+(get?"get ":"set ")+s.Name+(get?"() {":"(value) {"));indent++;returns=get?s.Type:null;
            if(a.Body!=null)Block(a.Body,false);else if(a.ExpressionBody!=null)L((get?"return ":"")+Copy(a.ExpressionBody.Expression)+";");
            else L(get?"return "+Copy("this.$property_"+s.Name,s.Type)+";":"this.$property_"+s.Name+" = "+Copy("value",s.Type)+";");
            indent--;L("}");returns=null;
        }
        if(p.ExpressionBody!=null){L((s.IsStatic?"static ":"")+"get "+s.Name+"() { return "+Copy(p.ExpressionBody.Expression)+"; }");}
    }
    private void EmitIndexer(IndexerDeclarationSyntax i)
    {
        var s=(IPropertySymbol)model.GetDeclaredSymbol(i)!;MemberMappings.Add(new{kind="indexer",name="Item",signature=string.Join(",",s.Parameters.Select(p=>p.Type.ToDisplayString()))});
        foreach(var a in i.AccessorList!.Accessors)
        {bool get=a.IsKind(SyntaxKind.GetAccessorDeclaration);L((get?"get_Item(":"set_Item(")+string.Join(", ",s.Parameters.Select(p=>p.Name))+(get?") {":", value) {"));indent++;returns=get?s.Type:null;Block(a.Body!,false);indent--;L("}");returns=null;}
    }
    private void Block(BlockSyntax b,bool braces=true)
    {if(braces){L("{");indent++;}foreach(var s in b.Statements)S(s);if(braces){indent--;L("}");}}
    private void S(StatementSyntax s)
    {
        switch(s)
        {
            case BlockSyntax b:Block(b);break;
            case LocalDeclarationStatementSyntax l:L(Declaration(l.Declaration)+";");break;
            case ExpressionStatementSyntax e:L(E(e.Expression)+";");break;
            case ReturnStatementSyntax r:L(r.Expression==null?"return;":"return "+Copy(r.Expression)+";");break;
            case ThrowStatementSyntax t:L("throw "+(t.Expression!=null?E(t.Expression):"$caught")+";");break;
            case IfStatementSyntax i:L("if ("+E(i.Condition)+")");S(i.Statement);if(i.Else!=null){L("else");S(i.Else.Statement);}break;
            case ForStatementSyntax f:L("for ("+(f.Declaration!=null?Declaration(f.Declaration):string.Join(", ",f.Initializers.Select(E)))+"; "+(f.Condition==null?"":E(f.Condition))+"; "+string.Join(", ",f.Incrementors.Select(E))+")");S(f.Statement);break;
            case ForEachStatementSyntax f:
                string it="$item"+temp++;L("for (const "+it+" of "+E(f.Expression)+") {");indent++;L("let "+f.Identifier.ValueText+" = "+Copy(it,model.GetTypeInfo(f.Type).Type)+";");if(f.Statement is BlockSyntax b2)Block(b2,false);else S(f.Statement);indent--;L("}");break;
            case WhileStatementSyntax w:L("while ("+E(w.Condition)+")");S(w.Statement);break;
            case DoStatementSyntax d:L("do");S(d.Statement);L("while ("+E(d.Condition)+");");break;
            case SwitchStatementSyntax sw:
                string variable="$switch"+temp++;L("{ const "+variable+" = "+E(sw.Expression)+";");L("switch (true) {");indent++;
                foreach(var section in sw.Sections)
                {foreach(var label in section.Labels)L(label switch{
                    CaseSwitchLabelSyntax cs=>"case "+variable+" === "+E(cs.Value)+":",
                    DefaultSwitchLabelSyntax=>"default:",
                    CasePatternSwitchLabelSyntax cp when cp.Pattern is ConstantPatternSyntax c=>"case "+variable+" === "+E(c.Expression)+(cp.WhenClause!=null?" && "+E(cp.WhenClause.Condition):"")+":",
                    _=>throw Bad(label)});indent++;foreach(var statement in section.Statements)S(statement);indent--;}
                indent--;L("} }");break;
            case BreakStatementSyntax:L("break;");break;
            case ContinueStatementSyntax:L("continue;");break;
            case EmptyStatementSyntax:L(";");break;
            case CheckedStatementSyntax c:Block(c.Block);break;
            default:throw Bad(s);
        }
    }
    private string Declaration(VariableDeclarationSyntax d)=>"let "+string.Join(", ",d.Variables.Select(v=>v.Identifier.ValueText+" = "+(v.Initializer!=null?Copy(v.Initializer.Value):Def(((ILocalSymbol)model.GetDeclaredSymbol(v)!).Type))));
    private string Arguments(SeparatedSyntaxList<ArgumentSyntax> args,IMethodSymbol? target=null)=>string.Join(", ",args.Select((a,i)=>
        a.RefKindKeyword.Kind() is SyntaxKind.OutKeyword or SyntaxKind.RefKeyword?Reference(a.Expression):E(a.Expression)));
    private string Reference(ExpressionSyntax e)
    {
        if(e is IdentifierNameSyntax id&&Sym(id) is IParameterSymbol p&&p.RefKind!=RefKind.None)return id.Identifier.ValueText;
        string target=e is DeclarationExpressionSyntax de?((SingleVariableDesignationSyntax)de.Designation).Identifier.ValueText:E(e);
        return "{ get value() { return "+target+"; }, set value(v) { "+target+" = v; } }";
    }
    private string E(ExpressionSyntax e)
    {
        switch(e)
        {
            case LiteralExpressionSyntax lit:return Literal(lit.Token.Value);
            case ThisExpressionSyntax:return "this";
            case BaseExpressionSyntax:return "super";
            case ParenthesizedExpressionSyntax p:return "("+E(p.Expression)+")";
            case IdentifierNameSyntax id:return Identifier(id);
            case MemberAccessExpressionSyntax m:return Member(m);
            case InvocationExpressionSyntax i:return Invoke(i);
            case ConditionalAccessExpressionSyntax c when c.WhenNotNull is InvocationExpressionSyntax call && call.Expression is MemberBindingExpressionSyntax bind:
                var method=Sym(call) as IMethodSymbol ?? throw Bad(c);
                if(!Program.Names.TryGetValue(method,out var lowered))throw Bad(c,"Unmapped conditional call");
                return "("+E(c.Expression)+"?."+lowered+"("+Arguments(call.ArgumentList.Arguments,method)+") ?? null)";
            case ObjectCreationExpressionSyntax o:return New(o,(IMethodSymbol)Sym(o)!,o.ArgumentList?.Arguments??default,o.Initializer);
            case ImplicitObjectCreationExpressionSyntax o:return New(o,(IMethodSymbol)Sym(o)!,o.ArgumentList.Arguments,o.Initializer);
            case ArrayCreationExpressionSyntax a:
                if(a.Initializer!=null)return ArrayInitializer(a.Initializer,((IArrayTypeSymbol)Typ(a)!).ElementType);
                var at=(IArrayTypeSymbol)Typ(a)!;if(at.Rank!=1)throw Bad(a,"Multidimensional arrays require explicit lowering");
                return "Array.from({ length: "+E(a.Type.RankSpecifiers[0].Sizes[0])+" }, () => "+Def(at.ElementType)+")";
            case ImplicitArrayCreationExpressionSyntax a:return ArrayInitializer(a.Initializer,((IArrayTypeSymbol)Typ(a)!).ElementType);
            case ElementAccessExpressionSyntax a:
                if(Sym(a) is IPropertySymbol pi&&pi.IsIndexer&&pi.ContainingNamespace.ToString().StartsWith("netDxf"))return E(a.Expression)+".get_Item("+string.Join(", ",a.ArgumentList.Arguments.Select(x=>E(x.Expression)))+")";
                return "GetElement("+E(a.Expression)+", "+string.Join(", ",a.ArgumentList.Arguments.Select(x=>E(x.Expression)))+")";
            case AssignmentExpressionSyntax a:return Assign(a);
            case BinaryExpressionSyntax b:return Binary(b);
            case PrefixUnaryExpressionSyntax u:
                if(Sym(u) is IMethodSymbol op&&Program.Names.TryGetValue(op,out var on))return Ref(op.ContainingType)+"."+on+"("+Copy(u.Operand)+")";
                return "("+u.OperatorToken.Text+E(u.Operand)+")";
            case PostfixUnaryExpressionSyntax u:return "("+E(u.Operand)+u.OperatorToken.Text+")";
            case ConditionalExpressionSyntax c:return "("+E(c.Condition)+" ? "+E(c.WhenTrue)+" : "+E(c.WhenFalse)+")";
            case CastExpressionSyntax c:
                var castType=model.GetTypeInfo(c.Type).Type;
                if(Sym(c) is IMethodSymbol conv&&Program.Names.ContainsKey(conv))return Ref(conv.ContainingType)+"."+Program.Names[conv]+"("+Copy(c.Expression)+")";
                string value=E(c.Expression);
                if(castType?.SpecialType==SpecialType.System_Int32)return "Int32("+value+")";
                if(castType?.SpecialType==SpecialType.System_Int16)return "Int16("+value+")";
                if(castType?.SpecialType==SpecialType.System_Byte)return "Byte("+value+")";
                return Copy(value,castType,c.Expression);
            case IsPatternExpressionSyntax p:
                if(p.Pattern is DeclarationPatternSyntax dp && dp.Designation is SingleVariableDesignationSyntax sd)
                {var t=model.GetTypeInfo(dp.Type).Type!;return "("+Test(E(p.Expression),t)+" && (("+sd.Identifier.ValueText+" = "+Copy(p.Expression)+"), true))";}
                if(p.Pattern is ConstantPatternSyntax cp)return "("+E(p.Expression)+" === "+E(cp.Expression)+")";
                throw Bad(e);
            case DefaultExpressionSyntax d:return Def(model.GetTypeInfo(d.Type).Type);
            case CheckedExpressionSyntax c:return E(c.Expression);
            default:throw Bad(e);
        }
    }
    private string ArrayInitializer(InitializerExpressionSyntax i,ITypeSymbol t)=>"["+string.Join(", ",i.Expressions.Select(Copy))+"]";
    private string Identifier(IdentifierNameSyntax id)
    {
        var s=Sym(id);
        if(s is ILocalSymbol)return id.Identifier.ValueText;
        if(s is IParameterSymbol p)return p.Name+(p.RefKind!=RefKind.None?".value":"");
        if(s is IFieldSymbol f){if(f.HasConstantValue)return Literal(f.ConstantValue);return (f.IsStatic?Ref(f.ContainingType):initializerReceiver??"this")+"."+FieldName(f);}
        if(s is IPropertySymbol prop)return (prop.IsStatic?Ref(prop.ContainingType):initializerReceiver??"this")+"."+prop.Name;
        if(s is INamedTypeSymbol t)return Ref(t);
        if(id.Identifier.ValueText=="value")return "value";
        throw Bad(id,"Unmapped identifier "+s);
    }
    private string Member(MemberAccessExpressionSyntax m)
    {
        var s=Sym(m);
        if(s is IFieldSymbol empty&&empty.ContainingType.SpecialType==SpecialType.System_String&&empty.Name=="Empty")return "\"\"";
        if(s is IFieldSymbol f&&f.HasConstantValue)return Literal(f.ConstantValue);
        if(s is IFieldSymbol nf&&nf.ContainingNamespace.ToString().StartsWith("netDxf"))return (nf.IsStatic?Ref(nf.ContainingType):E(m.Expression))+"."+FieldName(nf);
        if(s is IPropertySymbol p)
        {
            if(p.ContainingNamespace.ToString().StartsWith("netDxf"))return (p.IsStatic?Ref(p.ContainingType):E(m.Expression))+"."+p.Name;
            if(p.Name is "Length" or "Count")return E(m.Expression)+".length";
            if(p.ContainingType.OriginalDefinition.SpecialType==SpecialType.System_Nullable_T)
                return p.Name=="HasValue"?"("+E(m.Expression)+" !== null)":E(m.Expression);
            if(p.ContainingType.Name=="Color")return (p.IsStatic?"Color":E(m.Expression))+"."+p.Name;
            if(p.ContainingType.Name=="Environment"&&p.Name=="NewLine")return "Culture.NewLine";
            if(p.Name=="ListSeparator")return "Culture.ListSeparator";
            if(p.Name is "CurrentCulture" or "InvariantCulture")return p.Name=="InvariantCulture"?"Culture.Invariant":"Culture.Current";
            if(p.ContainingType.Name.StartsWith("Tuple"))return E(m.Expression)+"."+p.Name;
        }
        throw Bad(m,"Unmapped member "+s);
    }
    private string Invoke(InvocationExpressionSyntax i)
    {
        if(i.Expression is IdentifierNameSyntax id&&id.Identifier.ValueText=="nameof")return Program.Q(i.ArgumentList.Arguments[0].Expression.ToString().Split('.').Last());
        var s=Sym(i) as IMethodSymbol ?? throw Bad(i,"Unbound method");
        var key=s.OriginalDefinition;
        string args=Arguments(i.ArgumentList.Arguments,s);
        string? receiver=i.Expression is MemberAccessExpressionSyntax ma?EReceiver(ma.Expression,s):null;
        if(Program.Names.TryGetValue(key,out var name)||Program.Names.TryGetValue(s,out name))
            return (s.IsStatic?Ref(s.ContainingType):receiver??"this")+"."+name+"("+args+")";
        string ns=s.ContainingNamespace.ToString(), owner=s.ContainingType.Name;
        if(owner=="Math")return "DotNetMath."+s.Name+"("+args+")";
        if(owner=="Double"&&s.Name=="IsInfinity")return "("+args+" === Infinity || "+args+" === -Infinity)";
        if(owner=="Double"&&s.Name=="IsNaN")return "Number.isNaN("+args+")";
        if(owner=="Double"&&s.Name=="GetHashCode")return "DoubleHash("+receiver+")";
        if(IsNumber(s.ContainingType)&&s.Name=="ToString")return "NumberText("+receiver+(args.Length>0?", "+args:"")+")";
        if(owner=="String"&&s.Name=="Format")return "Format("+args+")";
        if(owner=="Object"&&s.Name=="MemberwiseClone")return "MemberwiseClone("+(receiver??"this")+")";
        if(owner=="String"&&s.Name is "IsNullOrEmpty" or "IsNullOrWhiteSpace")return "NativeString."+s.Name+"("+args+")";
        if(owner=="String"&&s.Name is "IndexOfAny" or "StartsWith")return "NativeString."+s.Name+"("+receiver+", "+args+")";
        if(owner=="List"&&s.Name is "AddRange" or "ToArray" or "Clear")return receiver+"."+s.Name+"("+args+")";
        if(owner=="StringBuilder")return receiver+"."+s.Name+"("+args+")";
        if(owner=="Enumerable"&&s.Name=="ToArray")return "Array.from("+(receiver??args)+", Copy)";
        if(owner=="List"&&s.Name=="Add")return receiver+".Add("+args+")";
        if(owner=="Array"&&s.Name=="Reverse")return "("+args+").reverse()";
        if(owner=="Color")return "Color."+s.Name+"("+args+")";
        if(owner=="BitConverter")return s.Name switch{
            "GetBytes"=>"Array.from(Uint8Array.of(Byte("+args+"), Byte(("+args+") >> 8), Byte(("+args+") >> 16), Byte(("+args+") >> 24)))",
            "ToInt32"=>"Int32FromBytes("+args+")",_=>throw Bad(i)};
        throw Bad(i,"Unmapped method "+s);
    }
    private string? EReceiver(ExpressionSyntax e,IMethodSymbol m)=>m.IsStatic&&!m.IsExtensionMethod?null:E(e);
    private string New(ExpressionSyntax n,IMethodSymbol ctor,SeparatedSyntaxList<ArgumentSyntax> args,InitializerExpressionSyntax? init)
    {
        var t=ctor.ContainingType;string value;
        if(t.Name.EndsWith("Exception"))value="new Errors."+t.Name+"("+Arguments(args,ctor)+")";
        else if(t.Name=="List")
        {string input=args.Count==0?"":Arguments(args,ctor);if(init!=null)input="["+string.Join(", ",init.Expressions.Select(Copy))+"]";value="new List("+input+")";init=null;}
        else if(t.Name.StartsWith("Tuple"))value="new Tuple("+Arguments(args,ctor)+")";
        else if(t.Name=="StringBuilder")value="new StringBuilder()";
        else if(Program.Files.ContainsKey(t))
        {string name=Ref(t); int ci; value="new "+name+"("+Arguments(args,ctor)+")";
            // All non-derived selected types expose exact-constructor dispatch, including structs.
            if(Program.Constructors.TryGetValue(ctor,out ci)&&!(t.BaseType!=null&&Program.Files.ContainsKey(t.BaseType)))value=name+".$create"+ci+"("+Arguments(args,ctor)+")";
        }
        else throw Bad(n,"Unmapped constructor "+ctor);
        if(init!=null)
        {
            string saved=initializerReceiver??"";initializerReceiver="$new";
            var assignments=init.Expressions.Select(e=>E(e)+";").ToArray();initializerReceiver=saved.Length==0?null:saved;
            value="Init("+value+", $new => { "+string.Join(" ",assignments)+" })";
        }
        return value;
    }
    private string Assign(AssignmentExpressionSyntax a)
    {
        string op=a.OperatorToken.Text,right=Copy(a.Right);var lhs=a.Left;
        if(lhs is ThisExpressionSyntax)return "this.$assign("+right+")";
        if(Sym(lhs) is IPropertySymbol property && property.SetMethod==null && SymbolEqualityComparer.Default.Equals(property.ContainingType,type))
          return (property.IsStatic?type.Name:"this")+".$property_"+property.Name+" "+op+" "+right;
        if(lhs is ElementAccessExpressionSyntax index)
        {
            string r=E(index.Expression);string args=string.Join(", ",index.ArgumentList.Arguments.Select(x=>E(x.Expression)));
            string v=op=="="?right:"("+E(lhs)+" "+op[..^1]+" "+right+")";
            if(Sym(index) is IPropertySymbol p && p.IsIndexer&&p.ContainingNamespace.ToString().StartsWith("netDxf"))return r+".set_Item("+args+", "+v+")";
            return "SetElement("+r+", "+args+", "+v+")";
        }
        return E(lhs)+" "+op+" "+right;
    }
    private string Binary(BinaryExpressionSyntax b)
    {
        if(Sym(b) is IMethodSymbol op&&Program.Names.TryGetValue(op,out var name))return Ref(op.ContainingType)+"."+name+"("+E(b.Left)+", "+E(b.Right)+")";
        if(Typ(b)?.SpecialType==SpecialType.System_Double) {
            if(b.IsKind(SyntaxKind.ModuloExpression))return "RemainderDouble("+E(b.Left)+", "+E(b.Right)+")";
            if(b.IsKind(SyntaxKind.MultiplyExpression) && !model.GetConstantValue(b.Left).HasValue && !model.GetConstantValue(b.Right).HasValue)
                return "MultiplyDouble("+E(b.Left)+", "+E(b.Right)+")";
        }
        string operation=b.OperatorToken.Text switch{"=="=>"===","!="=>"!==",_=>b.OperatorToken.Text};
        string value="("+E(b.Left)+" "+operation+" "+E(b.Right)+")";
        if(operation=="/" && Typ(b)?.SpecialType==SpecialType.System_Int32)return "Int32("+value+")";
        return value;
    }
}
