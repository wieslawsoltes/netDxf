using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Modules;
using netDxf.Shadow.Pid;
using netDxf.Shadow.Piping;
using netDxf.Shadow.Electrical;
using netDxf.Shadow.Symbols;

internal static class DisciplineChecks
{
    internal static int Run()
    {
        var checks = 0;
        ICadModule[] modules = [new PidModule(), new PipingModule(), new ElectricalModule()];
        foreach (var module in modules)
        {
            Check(JsonSerializer.Serialize(module.Descriptor).Length > 0, "Direct descriptor JSON has no default immutable arrays.");
            var catalog = module.Catalog();
            Check(JsonSerializer.Serialize(catalog).Length > 0 && catalog.Total >= 4, "Direct catalog JSON is serializable.");
            var document = new DxfDocument(); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var values = ImmutableDictionary<string,string>.Empty.Add("tag", "TEST-1").Add("service", "Water").Add("spec", "CS").Add("nominalSize", "50").Add("flow", "forward").Add("circuit", "C1").Add("crossSection", "1.5");
            var placement = module.Plan(source.Current, new("place", [Vec3.Zero], SymbolId: catalog.Items[0].SymbolId, Properties: values));
            Check(document.Entities.All.Count() == 0, "Planning is pure."); placement.Execute(editor);
            Check(document.Entities.Inserts.Count() == 1, "Module places attributed native INSERT.");
            var route = module.Plan(source.Current, new("route", [new(50,0),new(80,0),new(80,20)], Properties: values.SetItem("tag", "LINE-1")));
            route.Execute(editor);
            Check(document.Entities.Lines.Count() == 2, "Route creates bounded native segments.");
            Check(module.Analyze(source.Current).Records.Length == 3, "Analysis includes roots, excludes symbol strokes.");
            var first = route.Created[0]; var second = route.Created[1];
            var a = DomainMetadata.Read(source.Current.Read(first))!; var b = DomainMetadata.Read(source.Current.Read(second))!;
            Check(a.References.Single().TargetKey == b.Key && b.References.Single().TargetKey == a.Key, "Route connections are reciprocal and stable.");
            module.Plan(source.Current, new("update", Entities:[first], Properties:values.SetItem("tag", "LINE-2"))).Execute(editor);
            Check(DomainMetadata.Read(source.Current.Read(first))!.Tag == "LINE-2", "Data command edits persistent metadata.");
            editor.Undo(); Check(DomainMetadata.Read(source.Current.Read(first))!.Tag == "LINE-1", "Domain metadata update undoes.");
            var limited = module.Analyze(source.Current, options:new(MaximumObjects:1));
            Check(!limited.Complete && limited.ObjectsVisited == 1, "Object cap reports partial analysis.");
            var issueLimited = module.Analyze(source.Current, options:new(MaximumIssues:1));
            Check(!issueLimited.Complete && issueLimited.Issues.Length == 1, "Combined issue cap reports partial analysis.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            Throws<OperationCanceledException>(()=>module.Analyze(source.Current,cancellationToken:canceled.Token), "Analysis supports cancellation.");
            using var stream = new MemoryStream(); Check(document.Save(stream,true), "Domain native binary save.");stream.Position=0;
            var reopened=DxfDocument.Load(stream)!;
            Check(module.Analyze(new ShadowSession(reopened).Current).Records.Length == 3, "Native binary DXF preserves domain objects.");
        }
        var record = new DomainRecord("pid","pipe","key","P-1",Ports:[new("start","process",Vec3.Zero)],References:[]);
        Throws<ArgumentException>(()=>DomainMetadata.Chunks(record with {References=[new("start","target",new string('a',65))]}), "Target port bounds.");
        Throws<ArgumentException>(()=>DomainMetadata.Chunks(record with {References=[new("start","target","end","execute")]}), "Reference role allowlist.");
        Throws<ArgumentException>(()=>DomainMetadata.Chunks(record with {Ports=[new("start","process",new(1e13,0))]}), "Port coordinate bounds.");
        Throws<ArgumentException>(()=>DomainMetadata.Chunks(record with {Ports=[new("start","process",Vec3.Zero,new(1e13,0))]}), "Direction bounds.");
        var invalid = new Line(Vector2.Zero,Vector2.UnitX);
        var payload = new XData(new netDxf.Tables.ApplicationRegistry(DomainMetadata.ApplicationName));
        payload.XDataRecord.Add(new(XDataCode.String,"netdxf.domain.v1"));payload.XDataRecord.Add(new(XDataCode.String,"{\"module\":\"pid\",\"ports\":[null]}"));invalid.XData.Add(payload);
        var malformed = new DxfDocument();malformed.Entities.Add(invalid);
        Check(new PidModule().Analyze(new ShadowSession(malformed).Current).Issues.Any(i=>i.Code=="invalid-metadata"),"Malformed metadata yields a bounded diagnostic.");
        var pid = new DxfDocument();var pump=SymbolNative.Place(pid,PidSymbols.Catalog.Resolve("pid.pump.liquid"),Vec3.Zero,"pid","P-1");
        var pumpData=DomainMetadata.Read(pump)!;
        var line=new Line(new Vector2(-30,0),new Vector2(-16,0));pid.Entities.Add(line);
        DomainMetadata.Write(line,record with {Key="suction",Ports=[new("start","process",new(-30,0)),new("end","process",new(-16,0))],References=[new("end",pumpData.Key,"inlet")],Properties=ImmutableDictionary<string,string>.Empty.Add("nominalSize","50").Add("service","Water").Add("flow","reverse")});
        var pidReport=new PidModule().Analyze(new ShadowSession(pid).Current);
        Check(pidReport.Issues.Any(i=>i.Code=="inconsistent-flow"),"Pump inlet flow conflict is detected.");
        Check(pidReport.Issues.Any(i=>i.Code=="nonreciprocal-reference"),"One-way references are diagnosed.");
        DomainMetadata.Write(line,DomainMetadata.Read(line)! with {Properties=ImmutableDictionary<string,string>.Empty});
        Check(new PidModule().Analyze(new ShadowSession(pid).Current).Issues.Any(i=>i.Code=="missing-flow"),"Missing process flow is diagnosed.");
        var piping = new DxfDocument();SymbolNative.Place(piping,PipingSymbols.Catalog.Resolve("piping.reducer"),Vec3.Zero,"piping","R-1");
        Check(new PipingModule().Analyze(new ShadowSession(piping).Current).Issues.Any(i=>i.Code=="invalid-reducer-sizes"),"Reducer sizes require an actual size transition.");
        var electrical=new DxfDocument();var terminal=SymbolNative.Place(electrical,ElectricalSymbols.Catalog.Resolve("electrical.terminal"),Vec3.Zero,"electrical","X-1");
        var td=DomainMetadata.Read(terminal)!;var wire=new Line(new Vector2(7,0),new Vector2(20,0));electrical.Entities.Add(wire);
        DomainMetadata.Write(wire,new("electrical","wire","wire","W-1",Properties:ImmutableDictionary<string,string>.Empty.Add("circuit","C2").Add("crossSection","-1"),Ports:[new("start","electrical",new(7,0)),new("end","electrical",new(20,0))],References:[new("start",td.Key,"2")]));
        var electricalReport=new ElectricalModule().Analyze(new ShadowSession(electrical).Current);
        Check(electricalReport.Issues.Any(i=>i.Code=="circuit-mismatch"),"Cross-circuit connections are diagnosed.");
        Check(electricalReport.Issues.Any(i=>i.Code=="invalid-conductor-area"),"Invalid conductor area is diagnosed.");
        var sparse = new DxfDocument(); var sparseLine = new Line(Vector2.Zero,Vector2.UnitX); sparse.Entities.Add(sparseLine);
        DomainMetadata.Write(sparseLine,new("electrical","wire","sparse","W-2",Ports:[],References:[]));
        var sparseSource = new ShadowSession(sparse);
        var ownLimit = new ElectricalModule().Analyze(sparseSource.Current,options:new(MaximumIssues:1));
        Check(!ownLimit.Complete && ownLimit.Issues.Length == 1 && ownLimit.Limitation!.Contains("Discipline"), "Discipline-only issue overflow marks partial.");
        Throws<ArgumentException>(()=>new ElectricalModule().Plan(sparseSource.Current,new("place")), "Direct module calls reject missing points predictably.");
        return checks;
        void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
        void Throws<T>(Action action,string label)where T:Exception {try{action();}catch(T){checks++;return;}throw new InvalidOperationException(label);}
    }
}
