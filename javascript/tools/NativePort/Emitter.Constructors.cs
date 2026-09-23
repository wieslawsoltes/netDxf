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
    private void EmitDimensionConstructors()
    {
        var ctors=node.Members.OfType<ConstructorDeclarationSyntax>().ToArray();
        var bases=ctors.Where(c=>c.Initializer?.IsKind(SyntaxKind.BaseConstructorInitializer)==true).Select(c=>c.Initializer!).ToArray();
        if(type.BaseType?.ToDisplayString()!="netDxf.Entities.Dimension" || bases.Length==0 ||
            bases.Any(b=>b.ArgumentList.Arguments.Any(a=>!model.GetConstantValue(a.Expression).HasValue)))
            throw Bad(node,"Only the audited constant-base dimension constructor chain is supported");
        string parent=string.Join(", ",bases[0].ArgumentList.Arguments.Select(a=>E(a.Expression)));
        if(bases.Any(b=>string.Join(", ",b.ArgumentList.Arguments.Select(a=>E(a.Expression)))!=parent))throw Bad(node,"Inconsistent dimension base arguments");
        L("constructor(...args) {");indent++;L("super("+parent+");");
        L("if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }");
        foreach(var c in ctors.OrderByDescending(c=>Specificity(((IMethodSymbol)model.GetDeclaredSymbol(c)!).Parameters)))
        {var ctor=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("if ("+Match(ctor.Parameters)+") { this.$ctor"+Program.Constructors[ctor]+"(...args); return; }");}
        L("throw new ArgumentException('No matching dimension constructor. Use CreateOverload for an explicit signature.');");indent--;L("}");
        foreach(var c in ctors)
        {
            var ctor=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("$ctor"+Program.Constructors[ctor]+"("+Parameters(ctor)+") {");indent++;ParamCopies(ctor);Locals(c.Body);
            if(c.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer)==true)
            {var target=(IMethodSymbol)Sym(c.Initializer)!;L("this.$ctor"+Program.Constructors[target]+"("+Arguments(c.Initializer.ArgumentList.Arguments,target)+");");}
            Block(c.Body!,false);indent--;L("}");
            MemberMappings.Add(new{kind="constructor",signature=Signature(ctor),implementation="$ctor"+Program.Constructors[ctor],accessibility=ctor.DeclaredAccessibility.ToString(),parameters=ctor.Parameters.Select(p=>new{name=p.Name,type=p.Type.ToDisplayString(),refKind=p.RefKind.ToString()}).ToArray()});
            L("static $create"+Program.Constructors[ctor]+"(...args) { return new "+type.Name+"(ConstructorTag, "+Program.Constructors[ctor]+", args); }");
        }
        L("static CreateOverload(signature, ...args) {");indent++;
        foreach(var c in ctors){var ctor=(IMethodSymbol)model.GetDeclaredSymbol(c)!;L("if (signature === "+Program.Q(Signature(ctor))+") { if (!("+Match(ctor.Parameters)+")) throw new ArgumentException('Arguments do not match the selected constructor.'); return "+type.Name+".$create"+Program.Constructors[ctor]+"(...args); }");}
        L("throw new ArgumentException('Unknown dimension constructor signature.', 'signature');");indent--;L("}");
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
            string dispatch=(Gte&&type.IsAbstract?"    if (new.target === "+type.Name+") throw new NotSupportedException('Cannot construct an abstract class.');\n":"")+"    if (args[0] === ConstructorTag) { this['$ctor' + args[1]](...args[2]); return; }\n";
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
        if(Gte && t.TypeKind==TypeKind.Delegate)return a+" === null || typeof "+a+" === 'function'";
        if(Gte && t.Name=="SortedDictionary")return a+" === null || "+a+" instanceof GteSortedDictionary";
        if(t.TypeKind==TypeKind.Enum)return "typeof "+a+" === 'number'";
        if(t is INamedTypeSymbol n && Program.Files.ContainsKey(n))return (n.IsReferenceType?a+" === null || ":"")+a+" instanceof "+Ref(n);
        if(Program.DimensionMode && t.Name=="ICloneable")return "Cloneable("+a+")";
        if(t.Name is "IEnumerable" or "IReadOnlyList" or "List")return a+" == null || typeof "+a+"[Symbol.iterator] === 'function'";
        if(t.Name is "Color")return a+" instanceof Color";
        if(t.Name is "IFormatProvider")return a+" === null || typeof "+a+" === 'object' || typeof "+a+" === 'string'";
        throw new Exception("Unmapped overload type "+t);
    }
}
