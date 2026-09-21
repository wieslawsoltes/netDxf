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
    private string E(ExpressionSyntax e)
    {
        switch(e)
        {
            case LiteralExpressionSyntax lit:return Literal(lit.Token.Value);
            case TupleExpressionSyntax t when Program.DimensionMode:return "["+string.Join(", ",t.Arguments.Select(a=>Copy(a.Expression)))+"]";
            case ThisExpressionSyntax:return "this";
            case ThrowExpressionSyntax t when Program.DimensionMode:return "(() => { throw "+E(t.Expression)+"; })()";
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
                if(Program.DimensionMode && Typ(c.Expression)?.SpecialType==SpecialType.System_Object && (IsNumber(castType)||castType?.TypeKind==TypeKind.Enum||castType?.SpecialType==SpecialType.System_Boolean||castType?.SpecialType==SpecialType.System_Char))value="DimensionValue("+value+")";
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
}
