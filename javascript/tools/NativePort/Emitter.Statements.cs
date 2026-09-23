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
        if(Program.DimensionMode && s.Name=="TransformBy" && s.Parameters.Length==2 && type.BaseType?.Name=="Dimension")L("[transformation, translation] = this.$transformArguments(transformation, translation);");
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
        {bool get=a.IsKind(SyntaxKind.GetAccessorDeclaration);L((Gte?GteIndexerName(s,get)+"(":get?"get_Item(":"set_Item(")+string.Join(", ",s.Parameters.Select(p=>p.Name))+(get?") {":", value) {"));indent++;returns=get?s.Type:null;Block(a.Body!,false);indent--;L("}");returns=null;}
    }
    private void Block(BlockSyntax b,bool braces=true)
    {if(braces){L("{");indent++;}foreach(var s in b.Statements)S(s);if(braces){indent--;L("}");}}
    private void S(StatementSyntax s)
    {
        if(Gte && GteStatement(s))return;
        switch(s)
        {
            case BlockSyntax b:Block(b);break;
            case LocalDeclarationStatementSyntax l:L(Declaration(l.Declaration)+";");break;
            case ExpressionStatementSyntax e:
                if((Program.DimensionMode || Gte) && e.Expression is InvocationExpressionSyntax call && Sym(call) is IMethodSymbol conditional && conditional.GetAttributes().Any(a=>a.AttributeClass?.ToDisplayString()=="System.Diagnostics.ConditionalAttribute" && a.ConstructorArguments[0].Value is "DEBUG"))
                    L("// Conditional(DEBUG) call omitted by Release-profile lowering; native Debug assertion failures remain blocking evidence.");
                else L(E(e.Expression)+";");break;
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
        if(Gte && Sym(e) is IDiscardSymbol)return "{ value: 0 }";
        if(e is IdentifierNameSyntax id&&Sym(id) is IParameterSymbol p&&p.RefKind!=RefKind.None)return id.Identifier.ValueText;
        if(Gte && e is ElementAccessExpressionSyntax element)return "GteElementRef("+E(element.Expression)+", ["+string.Join(", ",element.ArgumentList.Arguments.Select(a=>E(a.Expression)))+"], "+(Sym(element) is IPropertySymbol?"true":"false")+")";
        if(Gte){string location=e is DeclarationExpressionSyntax declaration?((SingleVariableDesignationSyntax)declaration.Designation).Identifier.ValueText:E(e);return "GteRef(() => "+location+", v => { "+location+" = v; })";}
        string target=e is DeclarationExpressionSyntax de?((SingleVariableDesignationSyntax)de.Designation).Identifier.ValueText:E(e);
        return "{ get value() { return "+target+"; }, set value(v) { "+target+" = v; } }";
    }
}
