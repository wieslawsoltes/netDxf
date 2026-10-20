using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Mcp;
using netDxf.Shadow.Studio;
using netDxf.Shadow.Symbols;

internal static class DomainConnectivityStudioVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Add(string name,Action test)=>tests.Add(("discipline-connectivity/"+name,test));
        Add("default sample uses declared ports, symbols and useful components without history",()=>
        {
            using var studio=new Studio();studio.CadSetModule(new(studio.CadRevision,"pid",true));var before=State(studio);
            var page=studio.CadModuleConnectivity(new(studio.CadRevision,"pid",Limit:500));
            Require(page.Nodes.Total>40&&page.Edges.Total>30&&page.Components.Total>1&&page.Complete&&page.DeclarationsComplete,"Default P&ID has explicit passage network.");
            Require(State(studio).GetProperty("undoCount").GetInt32()==before.GetProperty("undoCount").GetInt32()&&studio.CadRevision==before.GetProperty("revision").GetInt64(),"Read-only analysis leaves native history/revision.");
            var pumpIn=page.Nodes.Items.Single(n=>n.Tag=="P-101"&&n.Port=="inlet");var pumpOut=page.Nodes.Items.Single(n=>n.Tag=="P-101"&&n.Port=="outlet");
            var path=studio.CadModuleConnectivity(new(studio.CadRevision,"pid","path",pumpIn.Id,pumpOut.Id,"forward"));
            Require(path.Walk?.Status=="found"&&path.Walk.PathLength==1&&path.Visits.Items.Length==2,"Pump direct passage path and proof.");
            Require(path.Nodes.Items.All(n=>n.Entity.Value.StartsWith("H:",StringComparison.Ordinal)),"Domain nodes refer to source roots, not synthetic geometry.");
        });
        Add("module guards and stale identities reject without drawing writes",()=>
        {
            using var studio=new Studio();studio.CadSetModule(new(studio.CadRevision,"pid",true));var request=new CadModuleConnectivityRequest(studio.CadRevision,"pid",Limit:1);var page=studio.CadModuleConnectivity(request);
            Throws<InvalidOperationException>(()=>studio.CadModuleConnectivity(request with{GraphKey=page.GraphKey,Direction="forward"}));
            Throws<ArgumentException>(()=>studio.CadModuleConnectivity(request with{NodeOffset=1}));
            studio.CadSetModule(new(studio.CadRevision,"pid",false));Throws<InvalidOperationException>(()=>studio.CadModuleConnectivity(request));
            studio.CadSetModule(new(studio.CadRevision,"pid",true));Throws<InvalidOperationException>(()=>studio.CadModuleConnectivity(request with{GraphKey=page.GraphKey}));
            var fresh=studio.CadModuleConnectivity(request);studio.CadCommand(new(studio.CadRevision,new("LINE",[new(900,900),new(910,900)])));
            Throws<Exception>(()=>studio.CadModuleConnectivity(request with{GraphKey=fresh.GraphKey}));
            Throws<InvalidOperationException>(()=>studio.CadModuleConnectivity(request with{Revision=studio.CadRevision,GraphKey=fresh.GraphKey}));
        });
        Add("MCP follows adaptive pages over large imported native graph and preserves all five streams",()=>
        {
            using var studio=new Studio(false);using var stream=LargeDrawing();studio.Load(stream);studio.CadSetModule(new(studio.CadRevision,"pid",true));
            var before=State(studio);var identity=new StudioRequestIdentity(before.GetProperty("documentId").GetGuid(),before.GetProperty("workspaceId").GetString()!,before.GetProperty("viewVersion").GetInt64());
            var registry=studio.CreateMcpRegistry();Require(registry.Tools.Single(t=>t.Name=="studio_module_connectivity").ReadOnly,"MCP discovery marks read-only.");
            var request=new CadModuleConnectivityRequest(studio.CadRevision,"pid",Limit:500);var nodes=new HashSet<string>();var edges=new HashSet<string>();var components=new HashSet<int>();var iterations=0;
            while(true)
            {
                var answer=registry.ExecuteAsync("studio_module_connectivity",McpJson.Element(new{identity,request})).AsTask().GetAwaiter().GetResult();
                Require(!answer.IsError&&answer.Text.Length<=registry.MaximumResultCharacters,"Bounded MCP result is a complete page, not an archive marker.");
                var result=JsonSerializer.Deserialize<CadModuleConnectivityResult>(answer.Text,McpJson.Options)!;
                foreach(var node in result.Nodes.Items)Require(nodes.Add(node.Id),"No duplicate projected port.");
                foreach(var edge in result.Edges.Items)Require(edges.Add(edge.Id),"No duplicate projected connection.");
                foreach(var component in result.Components.Items)Require(components.Add(component.Id),"No duplicate component.");
                if(result.Nodes.NextOffset is null&&result.Edges.NextOffset is null&&result.Issues.NextOffset is null&&result.Components.NextOffset is null&&result.Visits.NextOffset is null)break;
                request=request with{GraphKey=result.GraphKey,NodeOffset=result.Nodes.NextOffset??result.Nodes.Total,EdgeOffset=result.Edges.NextOffset??result.Edges.Total,IssueOffset=result.Issues.NextOffset??result.Issues.Total,ComponentOffset=result.Components.NextOffset??result.Components.Total,VisitOffset=result.Visits.NextOffset??result.Visits.Total};
                Require(++iterations<100,"Bounded pagination terminates.");
            }
            Require(nodes.Count==2600&&edges.Count==1300&&components.Count==1300,"Large graph projection retains every node, edge and component.");
            Require(State(studio).GetProperty("undoCount").GetInt32()==before.GetProperty("undoCount").GetInt32(),"All MCP reads preserve history.");
            var stale=registry.ExecuteAsync("studio_module_connectivity",McpJson.Element(new{identity=identity with{WorkspaceId="other"},request})).AsTask().GetAwaiter().GetResult();Require(stale.IsError,"MCP stale workspace is rejected.");
        });
        Add("maximum Unicode port identifiers are accepted by the Studio path request",()=>
        {
            var key=new string('水',128);var port=new string('端',64);var doc=new DxfDocument();var line=new Line(Vector2.Zero,Vector2.UnitX);doc.Entities.Add(line);
            DomainMetadata.Write(line,new("pid","test",key,"Unicode",Ports:[new(port,"process",Vec3.Zero),new("out","process",Vec3.UnitX)],References:[]){Passages=[new(port,"out")]});
            using var bytes=new MemoryStream();Require(doc.Save(bytes),"Unicode fixture saved.");bytes.Position=0;
            using var studio=new Studio(false);studio.Load(bytes);studio.CadSetModule(new(studio.CadRevision,"pid",true));
            var request=new CadModuleConnectivityRequest(studio.CadRevision,"pid","path",DomainConnectivity.PortId(key,port),DomainConnectivity.PortId(key,"out"));
            var result=studio.CadModuleConnectivity(request);Require(result.Walk?.Status=="found"&&result.Walk.PathLength==1,"Unicode source port accepted through public Studio API.");
        });
        Add("cancellation and invalid scope do not replace the current cache",()=>
        {
            using var studio=new Studio();studio.CadSetModule(new(studio.CadRevision,"pid",true));var request=new CadModuleConnectivityRequest(studio.CadRevision,"pid");var result=studio.CadModuleConnectivity(request);
            using var cancel=new CancellationTokenSource();cancel.Cancel();Throws<OperationCanceledException>(()=>studio.CadModuleConnectivity(request,cancel.Token));
            Throws<ArgumentException>(()=>studio.CadModuleConnectivity(request with{Operation="eval"}));
            Require(studio.CadModuleConnectivity(request with{GraphKey=result.GraphKey}).GraphKey==result.GraphKey,"Canceled/invalid work leaves valid cached result intact.");
        });
        Add("structure streams retain scope, directed evidence and exact adaptive cursors",()=>
        {
            using var studio=new Studio(false);using var bytes=LargeDrawing();studio.Load(bytes);studio.CadSetModule(new(studio.CadRevision,"pid",true));
            var request=new CadModuleConnectivityRequest(studio.CadRevision,"pid","structure",Limit:500);
            var groups=new HashSet<int>();var ports=new HashSet<string>();var critical=new HashSet<string>();var iterations=0;
            var before=State(studio);var registry=studio.CreateMcpRegistry();
            var identity=new StudioRequestIdentity(before.GetProperty("documentId").GetGuid(),before.GetProperty("workspaceId").GetString()!,before.GetProperty("viewVersion").GetInt64());
            while(true)
            {
                var answer=registry.ExecuteAsync("studio_module_connectivity",McpJson.Element(new{identity,request})).AsTask().GetAwaiter().GetResult();
                Require(!answer.IsError&&answer.Text.Length<=registry.MaximumResultCharacters,"Every structure page fits exact MCP response budget.");
                var result=JsonSerializer.Deserialize<CadModuleConnectivityResult>(answer.Text,McpJson.Options)!;
                Require(result.Structure is {ScopeComplete:true,LoopSearchComplete:true}&&result.Loops.Total==0,"Bidirectional bridges do not fabricate loops.");
                foreach(var group in result.StrongComponents.Items)Require(groups.Add(group.Id),"No duplicate strong component.");
                foreach(var port in result.PortStructures.Items)Require(ports.Add(port.Id),"No duplicate structural port.");
                foreach(var edge in result.CriticalConnections.Items)Require(critical.Add(edge.Id),"No duplicate weak bridge.");
                var next=request with{GraphKey=result.GraphKey,NodeOffset=result.Nodes.NextOffset??result.Nodes.Total,EdgeOffset=result.Edges.NextOffset??result.Edges.Total,
                    IssueOffset=result.Issues.NextOffset??result.Issues.Total,ComponentOffset=result.Components.NextOffset??result.Components.Total,VisitOffset=result.Visits.NextOffset??result.Visits.Total,
                    StrongComponentOffset=result.StrongComponents.NextOffset??result.StrongComponents.Total,PortStructureOffset=result.PortStructures.NextOffset??result.PortStructures.Total,
                    CondensationOffset=result.Condensation.NextOffset??result.Condensation.Total,CriticalConnectionOffset=result.CriticalConnections.NextOffset??result.CriticalConnections.Total,
                    LoopOffset=result.Loops.NextOffset??result.Loops.Total,LoopStepOffset=result.LoopSteps.NextOffset??result.LoopSteps.Total};
                if(new[]{result.Nodes.NextOffset,result.Edges.NextOffset,result.Issues.NextOffset,result.Components.NextOffset,result.Visits.NextOffset,
                    result.StrongComponents.NextOffset,result.PortStructures.NextOffset,result.Condensation.NextOffset,result.CriticalConnections.NextOffset,result.Loops.NextOffset,result.LoopSteps.NextOffset}.All(x=>x is null))break;
                request=next;Require(++iterations<100,"All eleven streams terminate without dropped rows.");
            }
            Require(groups.Count==1300&&ports.Count==2600&&critical.Count==1300,"All structure streams retain independent complete coverage.");
            Throws<InvalidOperationException>(()=>studio.CadModuleConnectivity(request with{StructureOptions=new(MaximumLoops:1)}));
            Throws<ArgumentException>(()=>studio.CadModuleConnectivity(request with{GraphKey=null,StrongComponentOffset=1}));
            Require(State(studio).GetProperty("undoCount").GetInt32()==before.GetProperty("undoCount").GetInt32(),"Structural reads preserve source history.");
        });
    }
    private static MemoryStream LargeDrawing()
    {
        var doc=new DxfDocument();for(var i=0;i<1300;i++)
        {
            var line=new Line(new Vector2(i,0),new Vector2(i,1));doc.Entities.Add(line);
            DomainMetadata.Write(line,new("pid","pipe","large."+i,"P-"+i,Ports:[new("start","process",new(i,0)),new("end","process",new(i,1))],References:[],Properties:ImmutableDictionary<string,string>.Empty.Add("openPorts","start,end")){Passages=[new("start","end")]});
        }
        var stream=new MemoryStream();Require(doc.Save(stream),"Fixture DXF saved.");stream.Position=0;return stream;
    }
    private static JsonElement State(Studio studio)=>JsonSerializer.SerializeToElement(studio.CadState(),StudioJson.CreateOptions()).GetProperty("state");
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static void Throws<T>(Action action)where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
}
