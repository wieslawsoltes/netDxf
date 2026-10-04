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
    private string Member(MemberAccessExpressionSyntax m)
    {
        var s=Sym(m);
        if(Gte && GteMember(m,s) is string gteMember)return gteMember;
        if(s is IFieldSymbol empty&&empty.ContainingType.SpecialType==SpecialType.System_String&&empty.Name=="Empty")return "\"\"";
        if(s is IFieldSymbol f&&f.HasConstantValue)return Literal(f.ConstantValue);
        if(s is IFieldSymbol nf&&nf.ContainingNamespace.ToString().StartsWith("netDxf"))return (nf.IsStatic?Ref(nf.ContainingType):E(m.Expression))+"."+FieldName(nf);
        if(s is IPropertySymbol p)
        {
            if(p.ContainingNamespace.ToString().StartsWith("netDxf"))return (p.IsStatic?Ref(p.ContainingType):Program.DimensionMode && Typ(m.Expression)?.IsReferenceType==true ? "RequireReference("+E(m.Expression)+")" : E(m.Expression))+"."+p.Name;
            if(Program.DimensionMode && p.Name=="Count" && p.ContainingType.Name!="List")return "RequireReference("+E(m.Expression)+").Count";
            if(p.Name is "Length" or "Count")return E(m.Expression)+".length";
            if(Program.DimensionMode && p.Name is "NumberDecimalDigits" or "NumberDecimalSeparator")return "DimensionCulture()."+p.Name;
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
        if(Gte && GteInvocation(i,s,args) is string invocation)return invocation;
        string? receiver=i.Expression is MemberAccessExpressionSyntax ma?EReceiver(ma.Expression,s):null;
        if(Program.Names.TryGetValue(key,out var name)||Program.Names.TryGetValue(s,out name))
            return (s.IsStatic?Ref(s.ContainingType):Gte?"GteReference("+(receiver??"this")+")":receiver??"this")+"."+name+"("+args+")";
        string ns=s.ContainingNamespace.ToString(), owner=s.ContainingType.Name;
        if((Program.DimensionMode || Gte) && ns.StartsWith("netDxf") && Program.Files.ContainsKey(s.ContainingType))
            return (s.IsStatic?Ref(s.ContainingType):(Gte?"GteReference(":"RequireReference(")+(receiver??"this")+")")+"."+s.Name+"("+args+")";
        if(Program.DimensionMode && owner=="ICloneable" && s.Name=="Clone")return "RequireReference("+receiver+").Clone()";
        if(Program.DimensionMode && owner=="Char" && s.Name=="ToString")return "String("+receiver+")";
        if(Program.DimensionMode && owner=="Char" && s.Name=="Equals")return "("+receiver+" === "+args+")";
        if(Program.DimensionMode && owner=="String" && s.Name=="Equals")return "StringEquals("+(receiver==null?args:receiver+", "+args)+")";
        if(Program.DimensionMode && owner=="String" && s.Name=="Replace")return "StringReplace("+receiver+", "+args+")";
        if(Program.DimensionMode && owner=="String" && s.Name=="Substring")return "StringSubstring("+receiver+", "+args+")";
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
        else if(Gte && t.Name=="SortedDictionary")value="new GteSortedDictionary()";
        else if(t.Name.StartsWith("Tuple"))value="new Tuple("+Arguments(args,ctor)+")";
        else if(t.Name=="StringBuilder")value="new StringBuilder()";
        else if(Program.DimensionMode && t.ToDisplayString()=="netDxf.Blocks.Block")value=Ref(t)+".CreateOverload("+Program.Q(Signature(ctor))+(args.Count>0?", "+Arguments(args,ctor):"")+")";
        else if(Program.Files.ContainsKey(t))
        {string name=Ref(t); int ci; value="new "+name+"("+Arguments(args,ctor)+")";
            // All non-derived selected types expose exact-constructor dispatch, including structs.
            if(Program.Constructors.TryGetValue(ctor,out ci)&&(Program.DimensionTypes.Contains(t)||!(t.BaseType!=null&&Program.Files.ContainsKey(t.BaseType))))value=name+".$create"+ci+"("+Arguments(args,ctor)+")";
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
        if(Gte && Sym(a) is IMethodSymbol compound && Program.Names.TryGetValue(compound,out var compoundName))
        {right=Ref(compound.ContainingType)+"."+compoundName+"("+E(lhs)+", "+right+")";op="=";}
        if(Program.DimensionMode && lhs is TupleExpressionSyntax tuple)return "(["+string.Join(", ",tuple.Arguments.Select(arg=>E(arg.Expression)))+"] = "+right+")";
        if(Gte && lhs is TupleExpressionSyntax tupleGte)return "(["+string.Join(", ",tupleGte.Arguments.Select(arg=>E(arg.Expression)))+"] = "+right+")";
        if(lhs is ThisExpressionSyntax)return "this.$assign("+right+")";
        if(Sym(lhs) is IPropertySymbol property && property.SetMethod==null && SymbolEqualityComparer.Default.Equals(property.ContainingType,type))
          return (property.IsStatic?type.Name:"this")+".$property_"+property.Name+" "+op+" "+right;
        if(lhs is ElementAccessExpressionSyntax index)
        {
            string r=E(index.Expression);string args=string.Join(", ",index.ArgumentList.Arguments.Select(x=>E(x.Expression)));
            string v=op=="="?right:"("+E(lhs)+" "+op[..^1]+" "+right+")";
            if(Sym(index) is IPropertySymbol p && p.IsIndexer&&(p.ContainingNamespace.ToString().StartsWith("netDxf") || Gte && p.ContainingType.Name=="SortedDictionary"))return r+".set_Item("+args+", "+v+")";
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
