using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using DxfViewport = netDxf.Entities.Viewport;

internal static class ModelAnnotationVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("model-annotation/" + name, run));
        foreach (var factor in new[] { .25, 1, 2, 50 })
        Test("explicit units scale dashes and symbols once/" + factor, () =>
        {
            var session = Drawing(1, .5); var before = session.Current;
            var scene = new RenderCompiler().Compile(before, new() { LinetypeScale = 2, ModelAnnotation = new(new(1, factor)) });
            Near(scene.Items[0].Style.Dashes[0], 4 * factor);
            Near(scene.Items.First(i => i.Text is not null).Text!.XAxis.Length, 2 * factor);
            Check(ReferenceEquals(before,session.Current) && new TopologyBuilder().Build(before).Edges.Length == 1);
        });
        foreach (var mode in new short[] { 0, 1 })
        Test("captured MSLTSCALE controls reciprocal CANNOSCALEVALUE/" + mode, () =>
        {
            var session = Drawing(mode,.02);
            Near(new RenderCompiler().Compile(session.Current).Items[0].Style.Dashes[0], mode == 1 ? 100 : 2);
        });
        Test("explicit disabled context ignores active drawing scale", () =>
        {
            var session = Drawing(1,.01);
            Near(new RenderCompiler().Compile(session.Current,new() { ModelAnnotation = new(Mode:ModelAnnotationMode.Disabled) }).Items[0].Style.Dashes[0],2);
            Near(new RenderCompiler().Compile(session.Current,new() { ModelAnnotation = null }).Items[0].Style.Dashes[0],2);
        });
        Test("explicit enabled context overrides drawing switch", () =>
        {
            var session = Drawing(0,.5);
            Near(new RenderCompiler().Compile(session.Current,new(){ModelAnnotation = new(new(1,3),ModelAnnotationMode.Enabled)}).Items[0].Style.Dashes[0],6);
        });
        Test("named scales use host units not ratios parsed from labels", () =>
        {
            var doc = new DxfDocument(DxfVersion.AutoCad2018); doc.DrawingVariables.AddCustomVariable(new("$CANNOSCALE",1,"Details"));
            var snapshot = new ShadowSession(doc).Current;
            var context = new ModelAnnotationContext { NamedScales = ImmutableDictionary<string,AnnotationScale>.Empty.Add("Details",new(2,25)) };
            Near(context.Resolve(snapshot).Multiplier,12.5);
            var unresolved = new ModelAnnotationContext().Resolve(snapshot);
            Check(unresolved.Diagnostic is not null); Near(unresolved.Multiplier,1);
        });
        Test("unresolved names remain visible as scene diagnostics", () =>
        {
            var session = Drawing(1,.5);
            session.MutateSource(doc => { doc.DrawingVariables.RemoveCustomVariable("$CANNOSCALEVALUE"); doc.DrawingVariables.AddCustomVariable(new("$CANNOSCALE",1,"1:50")); });
            var scene = new RenderCompiler().Compile(session.Current);
            Near(scene.Items[0].Style.Dashes[0],2); Check(scene.Diagnostics.Any(d=>d.Code=="linetype.annotation"));
        });
        Test("header edits invalidate only presentation and retain old snapshots", () =>
        {
            var session = Drawing(1,.5); var editor = new EditorSession(session); var compiler = new RenderCompiler();
            var snapshot = session.Current; var before = compiler.Compile(snapshot);
            editor.Execute("Annotation", CustomHeaderEdits.Set(snapshot,"$CANNOSCALEVALUE",40,ShadowValue.From(.25)));
            var after = compiler.Compile(session.Current);
            Near(before.Items[0].Style.Dashes[0],4); Near(after.Items[0].Style.Dashes[0],8);
            Check(ReferenceEquals(before,compiler.Compile(snapshot)));
            editor.Undo(); Near(compiler.Compile(session.Current).Items[0].Style.Dashes[0],4);
            editor.Redo(); Near(compiler.Compile(session.Current).Items[0].Style.Dashes[0],8);
        });
        foreach (var paperMode in new short[]{0,1})
        Test("model annotation never leaks into paper viewport compensation/"+paperMode, () =>
        {
            var session=Drawing(1,.01);
            session.MutateSource(doc =>
            {
                doc.DrawingVariables.PsLtScale=paperMode;
                var sheet=new Layout("Sheet");doc.Layouts.Add(sheet);
                sheet.AssociatedBlock.Entities.Add(new DxfViewport{Center=new(0,0,0),Width=200,Height=100,ViewHeight=200,Stacking=2,ViewDirection=Vector3.UnitZ});
            });
            var compiler=new RenderCompiler();
            var first=compiler.Compile(session.Current,new("Sheet"));
            var second=compiler.Compile(session.Current,new("Sheet"){ModelAnnotation=new(new(1,900),ModelAnnotationMode.Enabled)});
            Check(first.Items.Length==second.Items.Length);
            var symbol=first.Items.First(i=>i.IsPresentationOnly);
            Near(symbol.Text!.XAxis.Length,paperMode==1?1:.5);
            Near(second.Items.First(i=>i.IsPresentationOnly).Text!.XAxis.Length,symbol.Text.XAxis.Length);
        });
        foreach(var bad in new[]{0d,-1,double.NaN,double.PositiveInfinity})
        Test("invalid scale rejected before caching/"+bad,()=> {
            var session=Drawing(1,.5);
            Throws<ArgumentException>(()=>new RenderCompiler().Compile(session.Current,new(){ModelAnnotation=new(new(1,bad))}));
        });
        Test("ratio overflow and invalid enum are rejected",()=>{
            Throws<ArgumentException>(()=>_=new AnnotationScale(double.Epsilon,double.MaxValue).Multiplier);
            Throws<ArgumentException>(()=>new ModelAnnotationContext(Mode:(ModelAnnotationMode)99).Resolve(Drawing(1,.5).Current));
        });
        Test("malformed source switch and ratio fail rather than invent a value",()=>{
            var session=Drawing(2,.5);
            Throws<ArgumentException>(()=>new RenderCompiler().Compile(session.Current));
            session.MutateSource(doc=>{doc.DrawingVariables.RemoveCustomVariable("$MSLTSCALE");doc.DrawingVariables.AddCustomVariable(new("$MSLTSCALE",70,(short)1));doc.DrawingVariables.TryGetCustomVariable("$CANNOSCALEVALUE",out var v);v.Value=0d;});
            Throws<ArgumentException>(()=>new RenderCompiler().Compile(session.Current));
        });
        Test("legacy missing switch stays unscaled",()=>{
            var doc=new DxfDocument(DxfVersion.AutoCad2007);var snapshot=new ShadowSession(doc).Current;
            Check(!new ModelAnnotationContext(new(1,50)).Resolve(snapshot).Enabled);
        });
        Test("Studio context is workspace local and guards stale epochs",()=>{
            using var studio=new Studio();var before=State(studio);var revision=before.GetProperty("revision").GetInt64();
            var identity=new StudioRequestIdentity(before.GetProperty("documentId").GetGuid(),before.GetProperty("workspaceId").GetString()!,before.GetProperty("viewVersion").GetInt64());
            studio.SetAnnotationView(new(revision,"Enabled",1,50));var changed=State(studio);
            Check(changed.GetProperty("revision").GetInt64()==revision && changed.GetProperty("undoCount").GetInt32()==before.GetProperty("undoCount").GetInt32());
            Check(changed.GetProperty("viewVersion").GetInt64()>identity.ViewVersion);
            Throws<ShadowConflictException>(()=>studio.ExecuteRequest(identity,()=>studio.SetAnnotationView(new(revision,"Disabled"))));
            studio.Fork(new(revision,"Annotation branch"));Check(State(studio).GetProperty("annotationDrawingUnits").GetDouble()==50);
            studio.SetAnnotationView(new(revision,"Enabled",1,25));studio.SwitchWorkspace(new(revision,"live"));
            Check(State(studio).GetProperty("annotationDrawingUnits").GetDouble()==50);
        });
        Test("Studio no-op does not invalidate view or create history",()=>{
            using var studio=new Studio();var before=State(studio);
            studio.SetAnnotationView(new(before.GetProperty("revision").GetInt64(),"Drawing"));
            Check(State(studio).GetProperty("viewVersion").GetInt64()==before.GetProperty("viewVersion").GetInt64());
        });
    }
    private static ShadowSession Drawing(short mode,double scale)
    {
        var doc=new DxfDocument();
        doc.DrawingVariables.AddCustomVariable(new("$MSLTSCALE",70,mode));doc.DrawingVariables.AddCustomVariable(new("$CANNOSCALEVALUE",40,scale));
        var pattern=new Linetype("ANNOTATION_TEST",new LinetypeSegment[]{new LinetypeSimpleSegment(2),new LinetypeTextSegment("A",new TextStyle("Anno","Arial.ttf"),-1),new LinetypeSimpleSegment(-1)});
        doc.Entities.Add(new Line(Vector3.Zero,new Vector3(1000,0,0)){Linetype=pattern});
        return new(doc);
    }
    private static JsonElement State(Studio s)=>JsonSerializer.SerializeToElement(s.State(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Near(double actual,double expected)=>Check(double.IsFinite(actual)&&Math.Abs(actual-expected)<=1e-8*Math.Max(1,Math.Abs(expected)),$"Expected {expected}, got {actual}");
    private static void Check(bool value,string message="Annotation assertion failed"){if(!value)throw new Exception(message);}
    private static void Throws<T>(Action run)where T:Exception{try{run();}catch(T){return;}throw new Exception($"Expected {typeof(T).Name}");}
}
