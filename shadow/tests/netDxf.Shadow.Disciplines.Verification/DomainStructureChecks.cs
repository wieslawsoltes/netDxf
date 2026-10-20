using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Symbols;

internal static class DomainStructureChecks
{
    internal static int Run()
    {
        var checks = 0;
        // Independent transitive-closure and deletion oracles over all 64 directed graphs on three terminals.
        for (var mask = 0; mask < 64; mask++)
        {
            var passages = new List<DomainPassage>(); var bit = 0;
            for (var a = 0; a < 3; a++) for (var b = 0; b < 3; b++)
                if (a != b && (mask & (1 << bit++)) != 0) passages.Add(new("p" + a, "p" + b, "forward"));
            Verify(Build(Record("oracle", 3, passages.ToArray())));
        }
        Verify(Build(Record("parallel", 2, new("p0", "p1", "both"), new("p0", "p1", "forward"))));
        Verify(Build(Record("mixed", 3, new("p0", "p1"), new("p0", "p2", "forward"), new("p2", "p1", "forward"))));
        var one = Build(Record("one", 2, new DomainPassage("p0", "p1"))).AnalyzeStructure();
        Check(one.StrongComponents.Length == 1 && one.Loops.IsEmpty && one.LoopSearchComplete &&
            one.CriticalConnections.Length == 1, "Bidirectional single connection is mutual reachability, never a loop.");
        var triangle = Record("triangle", 3, new("p0", "p1", "forward"), new("p1", "p2", "forward"), new("p2", "p0", "forward"));
        var graph = Build(triangle); var full = graph.AnalyzeStructure();
        Check(full.Loops.Length == 1 && full.LoopProof(0).Edges.Length == 3 && full.LoopSearchComplete, "Directed triangle has an exact closed proof.");
        Check(graph.AnalyzeStructure(new(MaximumProofSteps:2)) is { ProofLimitReached:true, LoopSearchComplete:false, LoopSteps.Length:0 },
            "Proof cap never emits an incomplete loop as evidence.");
        Check(graph.AnalyzeStructure(new(MaximumSearchWork:1)) is { SearchLimitReached:true, LoopSearchComplete:false, SearchWork:1 },
            "Search cap is exact and never claims absence.");
        var limited = Build(triangle, triangle with { Key = "second" }).AnalyzeStructure(new(MaximumLoops:1));
        Check(limited.Loops.Length == 1 && limited.LoopLimitReached && !limited.LoopSearchComplete, "Witness cap reports unexamined components.");
        var legacy = Build(Record("legacy", 2) with { Passages = null }).AnalyzeStructure();
        Check(!legacy.ScopeComplete && legacy.Loops.IsEmpty, "Missing declarations prevent complete-scope conclusions.");
        var document = Document(triangle);
        var edgeLimited = DomainConnectivity.Build(new ShadowSession(document).Current, "pid", options:new(MaximumEdges:2)).AnalyzeStructure();
        Check(!edgeLimited.ScopeComplete && edgeLimited.CriticalConnections.Length == 2, "Apparent bridges in truncated graph remain scoped, not global claims.");
        foreach (var binary in new[] { false, true })
        {
            using var bytes = new MemoryStream(); Check(document.Save(bytes, binary), "Structure fixture saves to native DXF."); bytes.Position = 0;
            var imported = DomainConnectivity.Build(new ShadowSession(DxfDocument.Load(bytes)!).Current, "pid").AnalyzeStructure();
            Check(imported.LoopSteps.SequenceEqual(full.LoopSteps) && imported.PortStructures.SequenceEqual(full.PortStructures),
                "ASCII/binary imported direction and exact proof identities persist.");
        }
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        Throws<OperationCanceledException>(() => graph.AnalyzeStructure(cancellationToken:canceled.Token));
        Throws<OperationCanceledException>(() => full.LoopProof(0,canceled.Token));
        Throws<ArgumentOutOfRangeException>(() => graph.AnalyzeStructure(new(MaximumLoops:101)));
        Throws<ArgumentOutOfRangeException>(() => graph.AnalyzeStructure(new(MaximumProofSteps:1)));
        Throws<ArgumentOutOfRangeException>(() => graph.AnalyzeStructure(new(MaximumSearchWork:1000001)));
        Throws<ArgumentOutOfRangeException>(() => full.LoopProof(1));
        Throws<ArgumentException>(() => IndexedGraphAnalysis.StrongComponents(1, [new(0,1)]));
        Throws<ArgumentException>(() => IndexedGraphAnalysis.LowLinks(1, [new(-1,0)]));
        var parallel = IndexedGraphAnalysis.LowLinks(2, [new(0,1), new(0,1), new(1,1)]);
        Check(parallel.Bridges.IsEmpty && parallel.ArticulationPoints.IsEmpty, "Indexed multigraph retains parallel edges and self-loops.");
        var large = IndexedGraphAnalysis.StrongComponents(50000, Enumerable.Range(0,49999).Select(i => new IndexedGraphArc(i,i+1)).ToImmutableArray());
        Check(large.ComponentCount == 50000 && large.Membership[49999] == 49999, "Iterative SCC handles maximum-depth chain without recursion.");
        var low = IndexedGraphAnalysis.LowLinks(50000, Enumerable.Range(0,49999).Select(i => new IndexedGraphConnection(i,i+1)).ToImmutableArray());
        Check(low.Bridges.Length == 49999 && low.ArticulationPoints.Length == 49998, "Iterative low-link handles maximum-depth chain.");
        Console.WriteLine($"Domain structure: {checks} checks passed."); return checks;

        void Verify(DomainConnectivity graph)
        {
            var result = graph.AnalyzeStructure(); var nodes = graph.Nodes.Select(n => n.Id).ToArray(); var count = nodes.Length;
            var index = nodes.Select((id,i)=>(id,i)).ToDictionary(p=>p.id,p=>p.i); var reach = new bool[count,count];
            for (var i=0;i<count;i++) reach[i,i]=true;
            foreach(var e in graph.Edges)
            {
                if(e.Direction is "both" or "forward") reach[index[e.Source],index[e.Target]]=true;
                if(e.Direction is "both" or "reverse") reach[index[e.Target],index[e.Source]]=true;
            }
            for(var k=0;k<count;k++)for(var a=0;a<count;a++)for(var b=0;b<count;b++)reach[a,b]|=reach[a,k]&&reach[k,b];
            var membership=result.PortStructures.ToDictionary(p=>p.Id,p=>p.StrongComponent);
            for(var a=0;a<count;a++)for(var b=0;b<count;b++)Check((membership[nodes[a]]==membership[nodes[b]])==(reach[a,b]&&reach[b,a]),"Independent SCC reachability oracle.");
            var baseline=WeakComponents(graph,null,null);
            var bridges=result.CriticalConnections.Select(e=>e.Id).ToHashSet();
            foreach(var e in graph.Edges)Check(bridges.Contains(e.Id)==(WeakComponents(graph,e.Id,null)>baseline),"Independent edge deletion bridge oracle.");
            foreach(var n in result.PortStructures)Check(n.ArticulationPoint==(WeakComponents(graph,null,n.Id)>baseline),"Independent vertex deletion articulation oracle.");
            foreach(var group in result.StrongComponents)
            {
                var members=result.PortStructures.Where(n=>n.StrongComponent==group.Id).Select(n=>n.Id).ToArray();
                var expected=members.Any(start=>HasLoop(graph,start,start,new HashSet<string>{start},[]));
                Check((group.LoopStatus=="found")==expected&&group.LoopStatus!="incomplete","Independent exhaustive simple-cycle oracle.");
            }
            foreach(var loop in result.Loops)
            {
                var proof=result.LoopProof(loop.Id);
                Check(proof.Nodes.Length==proof.Edges.Length+1&&proof.Nodes[0]==proof.Nodes[^1]&&proof.Edges.Distinct().Count()==proof.Edges.Length,"Closed connection-distinct loop proof.");
                for(var i=0;i<proof.Edges.Length;i++)
                {
                    var e=graph.Edges.Single(e=>e.Id==proof.Edges[i]);
                    Check(e.Source==proof.Nodes[i]&&e.Target==proof.Nodes[i+1]&&e.Direction!="reverse"||
                        e.Target==proof.Nodes[i]&&e.Source==proof.Nodes[i+1]&&e.Direction!="forward","Witness follows actual declared direction.");
                }
            }
            Check(result.ScopeComplete&&result.LoopSearchComplete,"Small complete graph receives no false truncation.");
            foreach(var link in result.Condensation)Check(link.Source!=link.Target,"Condensation excludes internal component edges.");
        }
        void Check(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
        void Throws<T>(Action action)where T:Exception{try{action();}catch(T){checks++;return;}throw new InvalidOperationException("Expected "+typeof(T).Name);}
    }
    private static bool HasLoop(DomainConnectivity graph,string start,string current,HashSet<string> seen,HashSet<string> edges)
    {
        foreach(var e in graph.Edges)
        {
            if(edges.Contains(e.Id))continue;
            var next=e.Source==current&&e.Direction!="reverse"?e.Target:e.Target==current&&e.Direction!="forward"?e.Source:null;
            if(next is null)continue;
            if(next==start&&edges.Count>=1)return true;
            if(!seen.Add(next))continue;edges.Add(e.Id);
            if(HasLoop(graph,start,next,seen,edges))return true;
            edges.Remove(e.Id);seen.Remove(next);
        }
        return false;
    }
    private static int WeakComponents(DomainConnectivity graph,string? excludedEdge,string? excludedNode)
    {
        var unseen=graph.Nodes.Select(n=>n.Id).Where(id=>id!=excludedNode).ToHashSet();var count=0;
        while(unseen.Count>0)
        {
            count++;var queue=new Queue<string>();var start=unseen.First();unseen.Remove(start);queue.Enqueue(start);
            while(queue.TryDequeue(out var id))foreach(var e in graph.Edges.Where(e=>e.Id!=excludedEdge))
            {
                var next=e.Source==id?e.Target:e.Target==id?e.Source:null;if(next is not null&&unseen.Remove(next))queue.Enqueue(next);
            }
        }
        return count;
    }
    private static DomainRecord Record(string key,int count,params DomainPassage[] passages)=>new("pid","test",key,key,
        Ports:Enumerable.Range(0,count).Select(i=>new SymbolPort("p"+i,"process",new(i,0))).ToImmutableArray(),References:[]){Passages=passages.ToImmutableArray()};
    private static DxfDocument Document(params DomainRecord[] records)
    {
        var doc=new DxfDocument();foreach(var record in records){var line=new Line(Vector2.Zero,Vector2.UnitX);DomainMetadata.Write(line,record);doc.Entities.Add(line);}return doc;
    }
    private static DomainConnectivity Build(params DomainRecord[] records)=>DomainConnectivity.Build(new ShadowSession(Document(records)).Current,"pid");
}
