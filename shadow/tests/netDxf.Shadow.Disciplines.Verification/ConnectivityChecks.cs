using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Pid;
using netDxf.Shadow.Electrical;
using netDxf.Shadow.Piping;
using netDxf.Shadow.Symbols;

internal static class ConnectivityChecks
{
    internal static int Run()
    {
        var checks = 0;
        foreach (var catalog in new[] { PidSymbols.Catalog, ElectricalSymbols.Catalog, PipingSymbols.Catalog })
        {
            foreach (var symbol in catalog.Search(limit:200).Items)
            {
                Check(symbol.Version == "1.0.0" ? symbol.Passages is null : symbol.Passages is not null, "Exact legacy version preserved; new symbol version declares passages.");
                var doc = new DxfDocument(); var insert = SymbolNative.Place(doc, symbol, Vec3.Zero, "pid", "TEST");
                Check(DomainMetadata.Read(insert)!.Passages.HasValue == symbol.Passages.HasValue, "Native insertion persists passage declaration state.");
                var source = new ShadowSession(new DxfDocument()); var editor = new EditorSession(source);
                SymbolAuthoring.Place(source.Current, symbol, Vec3.Zero, "pid", "TEST").Execute(editor);
                Check(DomainMetadata.Read(source.Current.DrawingEntities().Single())!.Passages.HasValue == symbol.Passages.HasValue, "Planned insertion persists passage declaration state.");
            }
        }
        var document = new DxfDocument(); var session = new ShadowSession(document); var editing = new EditorSession(session);
        var route = DomainPlans.Route(session.Current, "pid", "pipe", [new(0,0),new(10,0),new(20,0)], "P-1", new Dictionary<string,string>{{"flow","forward"}});
        route.Execute(editing);var records=session.Current.DrawingEntities().Select(e=>DomainMetadata.Read(e)!).ToArray();
        var start=DomainConnectivity.PortId(records[0].Key,"start");var end=DomainConnectivity.PortId(records[1].Key,"end");
        var graph=DomainConnectivity.Build(session.Current,"pid");
        Check(graph.Nodes.Length==4 && graph.Edges.Length==3 && graph.Components.Length==1,"Route passages plus explicit reciprocal connection form one component.");
        var path=graph.ShortestPath(start,end,"forward");
        Check(path.Status=="found" && path.Path!.Edges.Length==3 && path.Complete,"Declared forward minimum-link path.");
        Check(graph.ShortestPath(end,start,"forward").Status=="unreachable","Forward path does not ignore passage direction.");
        Check(graph.ShortestPath(end,start,"reverse").Status=="found","Reverse traversal follows the declared relation backwards.");
        var trace=graph.Trace(start,"forward");Check(trace.ProofTo(end).Edges.SequenceEqual(path.Path!.Edges),"Trace proof uses exact path connections.");
        Check(graph.ShortestPath(start,end,maximumVisits:2).Status=="incomplete","Visit budget cannot claim unreachable.");
        Check(graph.ShortestPath(start,end,maximumDepth:1).Status=="incomplete","Depth budget cannot claim unreachable.");
        Check(graph.Trace(start,maximumDepth:0).DepthLimitReached,"Depth zero exposes an unexpanded frontier.");
        DomainPlans.Update(session.Current,[route.Created[0]],new Dictionary<string,string>{{"flow","reverse"}}).Execute(editing);
        Check(DomainConnectivity.Build(session.Current,"pid").ShortestPath(start,end,"forward").Status=="unreachable","Route flow editing updates persisted passage direction.");
        editing.Undo();Check(DomainConnectivity.Build(session.Current,"pid").ShortestPath(start,end,"forward").Status=="found","Passage editing is undoable.");
        foreach(var binary in new[]{false,true})
        {
            using var stream=new MemoryStream();Check(document.Save(stream,binary),"Native roundtrip writes domain passages.");stream.Position=0;
            var imported=DomainConnectivity.Build(new ShadowSession(DxfDocument.Load(stream)!).Current,"pid");
            Check(imported.Nodes.Select(n=>n.Id).SequenceEqual(graph.Nodes.Select(n=>n.Id)) && imported.Edges.Select(e=>e.Id).SequenceEqual(graph.Edges.Select(e=>e.Id)),"Port and connection IDs survive imported DXF.");
            Check(imported.ShortestPath(start,end,"forward").Path!.Edges.Length==3,"Imported passage direction and path survive DXF.");
        }
        var unicodeKey=new string('水',128);var unicodePort=new string('端',64);
        var unicode=Build(new DomainRecord("pid","test",unicodeKey,"Unicode",Ports:[new(unicodePort,"process",Vec3.Zero),new("out","process",Vec3.UnitX)],References:[]){Passages=[new(unicodePort,"out")]});
        var unicodeId=DomainConnectivity.PortId(unicodeKey,unicodePort);
        Check(unicodeId.Length>600&&unicodeId.Length<=DomainConnectivity.MaximumPortIdLength&&unicode.ShortestPath(unicodeId,DomainConnectivity.PortId(unicodeKey,"out")).Status=="found","Maximum Unicode domain keys and ports remain usable identifiers.");
        var lowerDoc=new DxfDocument();var lowerLine=new Line(Vector2.Zero,Vector2.UnitX);var lowerData=new XData(new netDxf.Tables.ApplicationRegistry("shadow_cad"));
        foreach(var chunk in DomainMetadata.Chunks(Record("lower",2) with{Passages=[new("p0","p1")]}))lowerData.XDataRecord.Add(new(XDataCode.String,chunk));
        var foreignBefore=new XData(new netDxf.Tables.ApplicationRegistry("FOREIGN_BEFORE"));foreignBefore.XDataRecord.Add(new(XDataCode.String,"Keep before"));
        var foreignAfter=new XData(new netDxf.Tables.ApplicationRegistry("FOREIGN_AFTER"));foreignAfter.XDataRecord.Add(new(XDataCode.String,"Keep after"));
        lowerLine.XData.Add(foreignBefore);lowerLine.XData.Add(lowerData);lowerLine.XData.Add(foreignAfter);lowerDoc.Entities.Add(lowerLine);
        Check(DomainMetadata.Read(lowerLine)?.Key=="lower"&&DomainConnectivity.Build(new ShadowSession(lowerDoc).Current,"pid").Edges.Length==1,"Native and shadow readers both recognize case-insensitive imported APPID.");
        var lowerSession=new ShadowSession(lowerDoc);var lowerEditor=new EditorSession(lowerSession);var lowerId=lowerSession.Current.DrawingEntities().Single().Id!.Value;
        DomainPlans.Update(lowerSession.Current,[lowerId],new Dictionary<string,string>{{"tag","LOWER-UPDATED"}}).Execute(lowerEditor);
        var lowerKeys=new[]{"FOREIGN_BEFORE","shadow_cad","FOREIGN_AFTER"};
        Check(DomainMetadata.Read(lowerLine)!.Tag=="LOWER-UPDATED"&&lowerLine.XData.AppIds.SequenceEqual(lowerKeys),"Editing imported lowercase APPID preserves exact spelling and metadata order without duplication.");
        Check(ReferenceEquals(lowerData.ApplicationRegistry,lowerDoc.ApplicationRegistries["SHADOW_CAD"])&&ReferenceEquals(foreignBefore,lowerLine.XData["FOREIGN_BEFORE"])&&ReferenceEquals(foreignAfter,lowerLine.XData["FOREIGN_AFTER"]),"Domain update preserves registry and unrelated native XDATA identities.");
        lowerEditor.Undo();Check(DomainMetadata.Read(lowerLine)!.Tag=="lower"&&ReferenceEquals(lowerData,lowerLine.XData["SHADOW_CAD"])&&lowerLine.XData.AppIds.SequenceEqual(lowerKeys),"Lowercase metadata update undoes without rewriting its prior declaration.");
        lowerEditor.Redo();Check(DomainMetadata.Read(lowerLine)!.Tag=="LOWER-UPDATED"&&lowerLine.XData.AppIds.SequenceEqual(lowerKeys),"Lowercase metadata update redoes with native spelling and ordering.");
        foreach(var binary in new[]{false,true})
        {
            using var lowerStream=new MemoryStream();Check(lowerDoc.Save(lowerStream,binary),"Lowercase registry DXF saves.");lowerStream.Position=0;
            var importedDoc=DxfDocument.Load(lowerStream)!;var importedLine=importedDoc.Entities.Lines.Single();
            Check(DomainMetadata.Read(importedLine)!.Tag=="LOWER-UPDATED"&&importedLine.XData.AppIds.SequenceEqual(lowerKeys)&&
                (string)importedLine.XData["FOREIGN_BEFORE"].XDataRecord.Single().Value=="Keep before"&&
                (string)importedLine.XData["FOREIGN_AFTER"].XDataRecord.Single().Value=="Keep after","Text/binary reload retains lower-case ownership and foreign payloads.");
            var importedSession=new ShadowSession(importedDoc);var importedEditor=new EditorSession(importedSession);
            DomainPlans.Update(importedSession.Current,[importedSession.Current.DrawingEntities().Single().Id!.Value],new Dictionary<string,string>{{"tag","RELOADED"}}).Execute(importedEditor);
            Check(DomainMetadata.Read(importedLine)!.Tag=="RELOADED"&&importedLine.XData.AppIds.SequenceEqual(lowerKeys),"Reloaded lower-case metadata remains editable.");
            importedEditor.Undo();Check(DomainMetadata.Read(importedLine)!.Tag=="LOWER-UPDATED","Reloaded metadata update restores prior data with one undo.");
            importedEditor.Redo();Check(DomainMetadata.Read(importedLine)!.Tag=="RELOADED","Reloaded metadata update redoes.");
        }
        var badDoc=new DxfDocument();var block=new netDxf.Blocks.Block("Overflow");block.Entities.Add(new Line(Vector2.Zero,Vector2.UnitX));var badInsert=new Insert(block){Scale=new(double.MaxValue)};
        DomainMetadata.Write(badInsert,Record("overflow",2));badDoc.Entities.Add(badInsert);
        var badWorld=DomainConnectivity.Build(new ShadowSession(badDoc).Current,"pid");Check(badWorld.Nodes.IsEmpty&&!badWorld.DeclarationsComplete&&badWorld.Issues.Any(i=>i.Code=="invalid-world-port"),"Nonfinite or oversized transformed ports produce a diagnostic, not an invalid graph.");
        var old=Record("legacy",2) with {Passages=null};var legacy=Build(old);
        Check(!legacy.DeclarationsComplete && legacy.Edges.IsEmpty && legacy.Issues.Any(i=>i.Code=="unspecified-passages"),"Legacy internal topology remains unknown.");
        Check(legacy.ShortestPath("legacy/p0","legacy/p1").Status=="incomplete","Legacy absence is not asserted disconnected.");
        var isolated=Build(old with {Passages=[]});Check(isolated.DeclarationsComplete && isolated.ShortestPath("legacy/p0","legacy/p1").Status=="unreachable","Explicit empty declaration means separate terminals.");
        var malformed=Record("bad",2);
        foreach(var passage in new[]{new DomainPassage("p0","missing"),new("p0","p0"),new("p0","p1","eval")})
            Throws<ArgumentException>(()=>DomainMetadata.Chunks(malformed with {Passages=[passage]}),"Malformed passage rejected before DXF mutation.");
        var duplicate=Build(Record("duplicate",1),Record("duplicate",1));Check(duplicate.Nodes.IsEmpty&&!duplicate.DeclarationsComplete,"Duplicate portable keys cannot fabricate connectivity.");
        var cross=Build(Record("same",1),Record("same",1) with {Module="electrical"});Check(cross.Nodes.IsEmpty,"Cross-module duplicate key stays ambiguous.");
        var boundary=Build(Record("pid",1) with{References=[new("p0","electrical","p0")]},Record("electrical",1) with{Module="electrical"});
        Check(boundary.Nodes.Length==1 && boundary.Edges.IsEmpty && boundary.Issues.Any(i=>i.Code=="scope-boundary"),"Module boundary is reported without traversing another discipline.");
        var dangling=Build(Record("a",1) with {References=[new("p0","b","p0")]});Check(dangling.Issues.Any(i=>i.Code=="missing-target")&&!dangling.DeclarationsComplete,"Dangling references are explicit.");
        var missingPort=Build(Record("a",1) with {References=[new("p0","b","missing")]},Record("b",1));Check(missingPort.Edges.IsEmpty&&missingPort.Issues.Any(i=>i.Code=="missing-port"),"Missing target port does not create a link.");
        var wrongKind=Build(Record("a",1) with {References=[new("p0","b","p0")]},Record("b",1) with {Ports=[new("p0","signal",Vec3.Zero)]});Check(wrongKind.Edges.IsEmpty&&wrongKind.Issues.Any(i=>i.Code=="port-kind-mismatch"),"Incompatible kinds are not connected.");
        var association=Build(Record("a",1) with {References=[new("p0","b","p0","association")]},Record("b",1));Check(association.Edges.IsEmpty&&association.Issues.Any(i=>i.Code=="association"),"Associations are inspected but not physical links.");
        var oneSided=Build(Record("a",1) with {References=[new("p0","b","p0")]},Record("b",1));Check(oneSided.Edges.Length==1&&!oneSided.Edges[0].Reciprocal&&oneSided.Issues.Any(i=>i.Code=="one-sided-connection"),"One-sided assertion is visible and diagnosed.");
        var a=Record("a",1) with {Ports=[new("p0","process",Vec3.Zero,NominalSize:"50")],References=[new("p0","b","p0")]};
        var b=Record("b",1) with {Ports=[new("p0","process",new(99,0),NominalSize:"25")],References=[new("p0","a","p0")]};
        var mismatch=Build(a,b);Check(mismatch.Edges.Length==1&&mismatch.Issues.Count(i=>i.Code=="size-mismatch")==1&&mismatch.Issues.Count(i=>i.Code=="connection-gap")==1,"Reciprocal relations deduplicate and report mismatch once.");
        using var canceled=new CancellationTokenSource();canceled.Cancel();
        Throws<OperationCanceledException>(()=>DomainConnectivity.Build(session.Current,"pid",cancellationToken:canceled.Token),"Build cancellation.");
        Throws<OperationCanceledException>(()=>graph.Trace(start,cancellationToken:canceled.Token),"Traversal cancellation.");
        var limited=DomainConnectivity.Build(session.Current,"pid",options:new(MaximumEdges:1));Check(!limited.Complete&&limited.Edges.Length==1&&limited.ShortestPath(start,end).Status=="incomplete","Edge cap has truthful result status.");
        var objectLimit=DomainConnectivity.Build(session.Current,"pid",options:new(MaximumObjects:1));Check(!objectLimit.Complete&&objectLimit.ObjectsVisited==1&&objectLimit.Issues.Any(i=>i.Code=="unscanned-target"),"Scan cap distinguishes unscanned target from missing target.");
        var issueLimit=DomainConnectivity.Build(session.Current,"pid",options:new(MaximumIssues:1));Check(issueLimit.Complete&&!issueLimit.IssuesComplete&&issueLimit.Issues.Length==1,"Findings cap is distinct from graph scan completeness.");
        // Independent Floyd-Warshall oracle for every directed simple graph on three explicit terminals.
        for(var mask=0;mask<64;mask++)
        {
            var passages=new List<DomainPassage>();var distances=new int[3,3];var bit=0;
            for(var i=0;i<3;i++)for(var j=0;j<3;j++){distances[i,j]=i==j?0:99;if(i==j)continue;if((mask&(1<<bit++))!=0){passages.Add(new("p"+i,"p"+j,"forward"));distances[i,j]=1;}}
            for(var k=0;k<3;k++)for(var i=0;i<3;i++)for(var j=0;j<3;j++)distances[i,j]=Math.Min(distances[i,j],distances[i,k]+distances[k,j]);
            var oracle=Build(Record("oracle",3) with {Passages=passages.ToImmutableArray()});
            for(var i=0;i<3;i++)for(var j=0;j<3;j++)
            {
                var actual=oracle.ShortestPath("oracle/p"+i,"oracle/p"+j,"forward");
                Check(distances[i,j]<99?actual.Status=="found"&&actual.Path!.Edges.Length==distances[i,j]:actual.Status=="unreachable","Exhaustive directed path oracle.");
                var reverse=oracle.ShortestPath("oracle/p"+j,"oracle/p"+i,"reverse");Check(reverse.Status==actual.Status&&reverse.Path?.Edges.Length==actual.Path?.Edges.Length,"Directed reverse oracle.");
            }
        }
        var largeDoc=new DxfDocument();for(var i=0;i<1300;i++)Add(largeDoc,Record("large"+i,2) with{Passages=[new("p0","p1")]});
        // Ordinary crossing geometry is intentionally invisible to this graph.
        largeDoc.Entities.Add(new Line(new Vector2(-100,0),new Vector2(100,0)));
        var large=DomainConnectivity.Build(new ShadowSession(largeDoc).Current,"pid");Check(large.Nodes.Length==2600&&large.Edges.Length==1300&&large.Components.Length==1300&&large.Complete,"Large network excludes unannotated crossing geometry and stays bounded.");
        var portLimited=DomainConnectivity.Build(new ShadowSession(largeDoc).Current,"pid",options:new(MaximumNodes:100));Check(portLimited.Nodes.Length==100&&!portLimited.Complete,"Port cap bounds retained records before graph construction.");
        Console.WriteLine($"Connectivity: {checks} checks passed.");return checks;
        void Check(bool condition,string message){checks++;if(!condition)throw new InvalidOperationException(message);}
        void Throws<T>(Action action,string message)where T:Exception{try{action();}catch(T){checks++;return;}throw new InvalidOperationException(message);}
    }
    private static DomainRecord Record(string key,int ports)=>new("pid","test",key,key,Ports:Enumerable.Range(0,ports).Select(i=>new SymbolPort("p"+i,"process",new(i,0))).ToImmutableArray(),References:[]){Passages=[]};
    private static void Add(DxfDocument doc,DomainRecord record){var line=new Line(Vector2.Zero,Vector2.UnitX);DomainMetadata.Write(line,record);doc.Entities.Add(line);}
    private static DomainConnectivity Build(params DomainRecord[] records){var doc=new DxfDocument();foreach(var record in records)Add(doc,record);return DomainConnectivity.Build(new ShadowSession(doc).Current,"pid");}
}
