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

internal sealed partial class Emitter
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
        if(Program.DimensionMode && baseName.Length>0)EmitDimensionConstructors();else EmitConstructors(baseName.Length>0);
        if(type.TypeKind==TypeKind.Struct)
        {
            var fields=type.GetMembers().OfType<IFieldSymbol>().Where(f=>!f.IsStatic).ToArray();
            L("[CopyValue]() {");indent++;L("const value = new "+type.Name+"();");foreach(var f in fields)L("value."+FieldName(f)+" = this."+FieldName(f)+";");L("return value;");indent--;L("}");
            L("$assign(value) {");indent++;foreach(var f in fields)L("this."+FieldName(f)+" = value."+FieldName(f)+";");L("return this;");indent--;L("}");
        }
        if(Program.DimensionMode && type.BaseType?.Name=="Dimension")MemberMappings.Add(new{kind="Ordinary",name="TransformBy",signature="netDxf.Matrix4",isStatic=false,implementation="TransformBy",accessibility="Public",isAbstract=false,returnType="void",inherited=true,parameters=new[]{new{name="transformation",type="netDxf.Matrix4",refKind="None"}}});
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
        if(Program.DimensionMode)imports.Append("import { DimensionValue, Cloneable, RequireReference, StringEquals, StringReplace, StringSubstring, DimensionCulture } from '../runtime/DimensionRuntime.js';\n");
        imports.Replace("../runtime/",runtimePrefix+"/");
        foreach(var enumNode in node.SyntaxTree.GetRoot().DescendantNodes().OfType<EnumDeclarationSyntax>()) {
          var en=(INamedTypeSymbol)model.GetDeclaredSymbol(enumNode)!;
          imports.Append("\nexport const ").Append(en.Name).Append(" = Object.freeze(").Append(JsonSerializer.Serialize(en.GetMembers().OfType<IFieldSymbol>().Where(f=>f.HasConstantValue).ToDictionary(f=>f.Name,f=>f.ConstantValue))).Append(");\n");
        }
        return imports+"\nexport class "+type.Name+baseName+" {\n"+body+"}\n";
    }
}
