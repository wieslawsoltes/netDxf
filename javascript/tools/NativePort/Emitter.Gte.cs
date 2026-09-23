// Explicit GTE language lowering. No algorithm replacement or precomputed output.
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
internal sealed partial class Emitter
{
    private string GteIndexerName(IPropertySymbol symbol,bool get) =>
        (symbol.ContainingType.GetMembers().OfType<IPropertySymbol>().Count(p=>p.IsIndexer)>1?"$":"")+
        (get?"get_Item":"set_Item")+(symbol.ContainingType.GetMembers().OfType<IPropertySymbol>().Count(p=>p.IsIndexer)>1?symbol.Parameters.Length.ToString():"");
    private void EmitGteIndexerDispatch(){
        var properties=type.GetMembers().OfType<IPropertySymbol>().Where(p=>p.IsIndexer).ToArray();if(properties.Length<2)return;
        foreach(bool get in new[]{true,false}){L((get?"get_Item":"set_Item")+"(...args) {");indent++;
            foreach(var p in properties)L("if (args.length === "+(p.Parameters.Length+(get?0:1))+") return this."+GteIndexerName(p,get)+"(...args);");
            L("throw new ArgumentException('No matching indexer overload.');");indent--;L("}");}
    }
    private string? GteExpression(ExpressionSyntax expression){
        if(expression is InitializerExpressionSyntax initializer)return "["+string.Join(", ",initializer.Expressions.Select(Copy))+"]";
        if(expression is TupleExpressionSyntax tuple)return "["+string.Join(", ",tuple.Arguments.Select(a=>Copy(a.Expression)))+"]";
        if(expression is SimpleLambdaExpressionSyntax lambda)return "("+lambda.Parameter.Identifier.ValueText+") => "+E((ExpressionSyntax)lambda.Body);
        if(expression is ParenthesizedLambdaExpressionSyntax paren)return "("+string.Join(", ",paren.ParameterList.Parameters.Select(p=>p.Identifier.ValueText))+") => "+E((ExpressionSyntax)paren.Body);
        if(expression is IdentifierNameSyntax id && Sym(id) is IMethodSymbol method && method.MethodKind==MethodKind.LocalFunction)return id.Identifier.ValueText;
        return null;
    }
    private string? GteMember(MemberAccessExpressionSyntax member,ISymbol? symbol){
        if(symbol is IPropertySymbol p){
            if(p.ContainingType.Name=="KeyValuePair")return E(member.Expression)+"."+p.Name;
            if(p.ContainingNamespace.ToString().StartsWith("netDxf"))return (p.IsStatic?Ref(p.ContainingType):p.ContainingType.IsReferenceType?"GteReference("+E(member.Expression)+")":E(member.Expression))+"."+p.Name;
            if(p.Name is "Length" or "Count")return "GteReference("+E(member.Expression)+")."+(p.ContainingType.Name=="SortedDictionary"?"Count":"length");
        }
        if(symbol is IFieldSymbol f && f.Name=="Empty" && f.ContainingType.SpecialType==SpecialType.System_String)return "\"\"";
        return null;
    }
    private string? GteInvocation(InvocationExpressionSyntax call,IMethodSymbol symbol,string args){
        if(symbol.MethodKind==MethodKind.LocalFunction)return symbol.Name+"("+args+")";
        if(symbol.MethodKind==MethodKind.DelegateInvoke)return "GteInvoke("+E(call.Expression)+(args.Length>0?", "+args:"")+")";
        string? receiver=call.Expression is MemberAccessExpressionSyntax member?EReceiver(member.Expression,symbol):null;
        string owner=symbol.ContainingType.Name;
        if(owner=="Object"&&symbol.Name=="GetType")return "GteReference("+(receiver??"this")+").constructor";
        if(symbol.Name=="GetHashCode"&&!Program.Names.ContainsKey(symbol))return "GteHash("+(receiver??"this")+")";
        if(owner=="Array"&&symbol.Name=="CopyTo")return "GteCopyTo("+receiver+", "+args+")";
        if(owner=="Enumerable"&&symbol.Name is "First" or "Last")return "Gte"+symbol.Name+"("+(receiver??args)+")";
        if(owner=="SortedDictionary")return "GteReference("+receiver+")."+symbol.Name+"("+args+")";
        return null;
    }
    private bool GteStatement(StatementSyntax statement){
        if(statement is not LocalFunctionStatementSyntax local)return false;
        var method=(IMethodSymbol)model.GetDeclaredSymbol(local)!;
        L("const "+method.Name+" = ("+Parameters(method)+") => {");indent++;Locals(local.Body);
        if(local.Body!=null)Block(local.Body,false);else L("return "+E(local.ExpressionBody!.Expression)+";");indent--;L("};");return true;
    }
}
