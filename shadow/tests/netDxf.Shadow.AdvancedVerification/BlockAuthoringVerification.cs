using System.Collections.Immutable;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using netDxf.Tables;

internal static class BlockAuthoringVerification
{
    private static void Require(bool condition, string message = "Block authoring assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double value, double expected)
    { Require(Math.Abs(value - expected) <= 1e-7, $"Expected {expected:R}, got {value:R}"); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected a refusal before source publication");
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static DxfDocument RoundTrip(DxfDocument doc, bool binary = false)
    {
        using var stream = new MemoryStream(); Require(doc.Save(stream, binary)); stream.Position = 0;
        return DxfDocument.Load(stream) ?? throw new InvalidOperationException("DXF reload failed");
    }
    private static (DxfDocument Doc, ShadowSession Source, EditorSession Editor, DraftingPlanner Planner) Drawing()
    {
        var doc = new DxfDocument();
        doc.Entities.Add(new Line(new Vector3(10, 20, 0), new Vector3(30, 20, 0)));
        doc.Entities.Add(new Circle(new Vector3(30, 30, 0), 5));
        var source = new ShadowSession(doc); return (doc, source, new(source), new());
    }
    private static ImmutableArray<ObjectId> Roots(ShadowSnapshot s) => s.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray();
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var disposition in Enum.GetValues<BlockSourceDisposition>().Distinct())
            tests.Add(($"block/atomic {disposition} creation localizes owned geometry", () =>
            {
                var (doc, source, editor, planner) = Drawing(); var before = source.Current;
                planner.CreateBlock(before, new("Valve", new(10,20), Roots(before), disposition)).Execute(editor);
                var block = doc.Blocks["Valve"];
                Require(block.Entities.Count == 2 && block.Origin == Vector3.Zero);
                Near(block.Entities.OfType<Line>().Single().StartPoint.X, 0);
                Near(block.Entities.OfType<Circle>().Single().Center.X, 20);
                Near(block.Entities.OfType<Circle>().Single().Center.Y, 10);
                Require(doc.Entities.All.Count() == (disposition == BlockSourceDisposition.Convert ? 1 : disposition == BlockSourceDisposition.Retain ? 2 : 0));
                Require(editor.State.UndoCount == 1 && before.DrawingEntities().Count() == 2);
                if (disposition == BlockSourceDisposition.Convert)
                {
                    var insert = doc.Entities.Inserts.Single(); Require(ReferenceEquals(insert.Block, block));
                    Near(insert.Position.X, 10); Near(insert.Position.Y, 20);
                }
                editor.Undo(); Require(doc.Entities.All.Count() == 2 && !doc.Blocks.Contains("Valve"));
                editor.Redo(); Require(doc.Blocks["Valve"].Entities.Count == 2);
            }));
        foreach (var binary in new[] {false,true})
            tests.Add(($"block/{(binary ? "binary" : "ASCII")} roundtrip retains named definition and reference", () =>
            {
                var (doc, source, editor, planner) = Drawing();
                planner.CreateBlock(source.Current, new("Part A", new(10,20), Roots(source.Current))).Execute(editor);
                var actual = RoundTrip(doc,binary); var insert = actual.Entities.Inserts.Single();
                Require(insert.Block.Name == "Part A" && insert.Block.Entities.Count == 2);
                Near(insert.Block.Entities.OfType<Line>().Single().EndPoint.X, 20); Near(insert.Position.Y,20);
                Require(actual.Blocks.GetReferences("Part A").Count > 0);
            }));
        tests.Add(("block/retained definition deeply detaches polyline vertices", () =>
        {
            var doc = new DxfDocument(); var polyline = new Polyline2D([new Vector2(10,20),new Vector2(30,20),new Vector2(30,30)],true);
            polyline.Vertexes[0].Bulge = .25; doc.Entities.Add(polyline);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var planner = new DraftingPlanner();
            planner.CreateBlock(source.Current,new("Outline",new(10,20),Roots(source.Current),BlockSourceDisposition.Retain)).Execute(editor);
            var copy = doc.Blocks["Outline"].Entities.OfType<Polyline2D>().Single();
            Require(!ReferenceEquals(copy.Vertexes[0],polyline.Vertexes[0])); Near(copy.Vertexes[0].Bulge,.25);
            Near(polyline.Vertexes[0].Position.X,10); Near(copy.Vertexes[0].Position.X,0);
        }));
        tests.Add(("block/memory store uses the identical definition authoring plan", () =>
        {
            var (doc, source, _, planner) = Drawing(); var memory = new EditorSession(new ShadowMemoryStore(source.Current));
            planner.CreateBlock(memory.Snapshot,new("Detached",new(10,20),Roots(memory.Snapshot))).Execute(memory);
            Require(!doc.Blocks.Contains("Detached") && doc.Entities.All.Count()==2);
            Require(memory.Snapshot.DrawingEntities().Single().Type=="Insert");
            memory.Undo(); Require(memory.Snapshot.DrawingEntities().Count()==2);
            memory.Redo(); Require(memory.Snapshot.DrawingEntities().Single()["Block"]["Name"].Text=="Detached");
        }));
        tests.Add(("block/definition scene root is isolated and cache identity includes root", () =>
        {
            var (doc, source, editor, planner) = Drawing();
            planner.CreateBlock(source.Current,new("Isolated",new(10,20),Roots(source.Current))).Execute(editor);
            var root = source.Current.Document["Blocks"].Items.Single(b=>b["Name"].Text=="Isolated").Id!.Value;
            var builder = new CadSceneBuilder(); var compiler = new RenderCompiler(builder);
            var ordinary = compiler.Compile(source.Current);
            var isolated = compiler.Compile(source.Current,new(){RootBlock=root});
            Require(isolated.Items.Length==2 && isolated.Items.All(i=>i.InsertPath.IsEmpty));
            Require(ordinary.Items.All(i=>i.InsertPath.Length==1));
            Near(ordinary.Bounds.MinX-isolated.Bounds.MinX,10); Near(ordinary.Bounds.MinY-isolated.Bounds.MinY,20);
            Require(ReferenceEquals(isolated,compiler.Compile(source.Current,new(){RootBlock=root})));
            planner.PlanInBlock(editor.Snapshot,new("CIRCLE",[new(50,40)],Value:3),root).Execute(editor);
            Require(doc.Blocks["Isolated"].Entities.Count==3 && doc.Entities.Inserts.Count()==1);
            Require(compiler.Compile(source.Current,new(){RootBlock=root}).Items.Length==3);
            editor.Undo(); Require(doc.Blocks["Isolated"].Entities.Count==2);
        }));
        tests.Add(("block/INSERT rejects direct and indirect recursion without publication", () =>
        {
            var doc = new DxfDocument(); var child = new Block("Child"); var parent = new Block("Parent");
            child.Entities.Add(new Circle(Vector3.Zero,2)); parent.Entities.Add(new Insert(child,Vector3.Zero)); doc.Blocks.Add(parent);
            var source = new ShadowSession(doc); var editor = new EditorSession(source); var planner = new DraftingPlanner();
            var blocks=source.Current.Document["Blocks"].Items.Where(b=>b.Id is not null).ToDictionary(b=>b["Name"].Text,b=>b.Id!.Value);
            var before=source.Current;
            Reject(()=>planner.PlanInBlock(before,new("INSERT",[new(0,0)],Value:1,Reference:blocks["Child"]),blocks["Child"]));
            Reject(()=>planner.PlanInBlock(before,new("INSERT",[new(0,0)],Value:1,Reference:blocks["Parent"]),blocks["Child"]));
            Require(ReferenceEquals(before,source.Current) && editor.State.UndoCount==0);
            planner.PlanInBlock(before,new("INSERT",[new(10,0)],Value:1,Reference:blocks["Child"]),blocks["Parent"]).Execute(editor);
            Require(parent.Entities.OfType<Insert>().Count()==2);
        }));
        tests.Add(("block/definition editing refuses foreign-space entities", () =>
        {
            var (doc,source,editor,planner)=Drawing();
            planner.CreateBlock(source.Current,new("Part",new(0,0),Roots(source.Current),BlockSourceDisposition.Retain)).Execute(editor);
            var root=source.Current.Document["Blocks"].Items.Single(b=>b["Name"].Text=="Part").Id!.Value;
            Reject(()=>planner.PlanInBlock(source.Current,new("ERASE",Entities:Roots(source.Current)),root));
            Require(doc.Entities.All.Count()==2);
        }));
        foreach(var name in new[]{"", "*Anonymous", "bad/name", "line\nfeed", "A|B"})
            tests.Add(($"block/name validation rejects {JsonSerializer.Serialize(name)}",()=>
            {
                var (_,source,_,planner)=Drawing(); var before=source.Current;
                Reject(()=>planner.CreateBlock(before,new(name,new(0,0),Roots(before)))); Require(ReferenceEquals(before,source.Current));
            }));
        tests.Add(("block/duplicate name and locked source are atomic refusals",()=>
        {
            var (doc,source,editor,planner)=Drawing();
            planner.CreateBlock(source.Current,new("Part",new(0,0),Roots(source.Current),BlockSourceDisposition.Retain)).Execute(editor);
            var before=source.Current;
            Reject(()=>planner.CreateBlock(before,new("pArT",new(0,0),Roots(before))));
            doc.Layers["0"].IsLocked=true; source.Refresh(); editor.ResetHistory(); before=source.Current;
            Reject(()=>planner.CreateBlock(before,new("Other",new(0,0),Roots(before))));
            Require(ReferenceEquals(before,source.Current) && !doc.Blocks.Contains("Other"));
        }));
        tests.Add(("block/cancellation and coordinate quotas precede publication",()=>
        {
            var (_,source,_,planner)=Drawing(); var before=source.Current; var request=new BlockCreation("Part",new(0,0),Roots(before));
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            Reject(()=>planner.CreateBlock(before,request,cancellationToken:cancellation.Token));
            Reject(()=>planner.CreateBlock(before,request with{BasePoint=new(double.NaN,0)}));
            Reject(()=>new DraftingPlanner(new(MaximumClonedObjects:1)).CreateBlock(before,request));
            Require(ReferenceEquals(before,source.Current));
        }));
        tests.Add(("block/rename preserves definition identity and updates all native references",()=>
        {
            var (doc,source,editor,planner)=Drawing();
            planner.CreateBlock(source.Current,new("Old",new(10,20),Roots(source.Current))).Execute(editor);
            var definition=source.Current.DrawingEntities().Single()["Block"].Id!.Value;
            DraftingBlocks.Rename(source.Current,definition,"Renamed").Execute(editor);
            Require(!doc.Blocks.Contains("Old") && doc.Entities.Inserts.Single().Block.Name=="Renamed");
            editor.Undo(); Require(doc.Blocks.Contains("Old"));editor.Redo();Require(doc.Blocks.Contains("Renamed"));
            Require(source.Current.DrawingEntities().Single()["Block"].Id==definition);
        }));
        foreach (var definitionFirst in new[] { false, true })
            tests.Add(($"block/source creation is independent of definition-first={definitionFirst} patch order", () =>
            {
                for (var iteration = 0; iteration < 12; iteration++)
                {
                    var (doc, source, editor, planner) = Drawing();
                    var plan = planner.CreateBlock(source.Current, new("Ordered", new(10,20), Roots(source.Current)));
                    var patch = plan.Patch with { Changes = plan.Patch.Changes
                        .OrderBy(c => c.After?.Kind == ShadowObjectKind.Block ? (definitionFirst ? 0 : 2) : 1).ToImmutableArray() };
                    editor.Execute("Create ordered block", patch);
                    var definition = doc.Blocks["Ordered"]; var insert = doc.Entities.Inserts.Single();
                    for (var replay = 0; replay < 3; replay++)
                    {
                        editor.Undo(); Require(!doc.Blocks.Contains("Ordered") && doc.Entities.All.Count() == 2);
                        editor.Redo(); Require(ReferenceEquals(doc.Blocks["Ordered"], definition));
                        Require(ReferenceEquals(doc.Entities.Inserts.Single(), insert) && definition.Entities.Count == 2);
                    }
                }
            }));
        tests.Add(("block/nested reference conversion replays without reordering shared definitions", () =>
        {
            var (doc, source, editor, planner) = Drawing();
            planner.CreateBlock(source.Current, new("Child", new(10,20), Roots(source.Current))).Execute(editor);
            planner.CreateBlock(source.Current, new("Parent", new(0,0), Roots(source.Current))).Execute(editor);
            var child = doc.Blocks["Child"];
            for (var i = 0; i < 4; i++)
            {
                editor.Undo(); Require(doc.Entities.Inserts.Single().Block == child && !doc.Blocks.Contains("Parent"));
                editor.Redo(); Require(doc.Entities.Inserts.Single().Block.Name == "Parent" && ReferenceEquals(doc.Blocks["Child"], child));
                Require(doc.Blocks["Parent"].Entities.OfType<Insert>().Single().Block == child);
            }
        }));
        tests.Add(("block/Studio editor saves one canonical transaction and closes the isolated branch",()=>
        {
            using var studio=new Studio(false);
            studio.CadCommand(new(studio.CadRevision,new("CIRCLE",[new(10,10)],Value:5)));
            studio.CadCreateBlock(new(studio.CadRevision,"Part",new(10,10)));
            var before=Json(studio.CadState()); var block=before.GetProperty("blocks").EnumerateArray().Single(b=>b.GetProperty("name").GetString()=="Part").GetProperty("id").GetString()!;
            studio.CadBeginBlock(new(studio.CadRevision,block));
            studio.CadCommand(new(studio.CadRevision,new("RECTANG",[new(10,10),new(20,20)])));
            var editing=Json(studio.CadState()); Require(editing.GetProperty("blockEditor").GetProperty("changed").GetBoolean());
            Require(editing.GetProperty("state").GetProperty("workspaceId").GetString()!="live");
            Reject(()=>studio.CadCommand(new(studio.CadRevision,new("INSERT",[new(0,0)],Value:1,Reference:new ObjectId(block)))));
            Reject(()=>studio.CadFinishBlock(new(studio.CadRevision,-1,true)));
            studio.CadFinishBlock(new(studio.CadRevision,editing.GetProperty("state").GetProperty("sourceRevision").GetInt64(),true));
            var result=Json(studio.CadState()); Require(result.GetProperty("blockEditor").ValueKind==JsonValueKind.Null && !studio.CadHasBranches);
            Require(result.GetProperty("state").GetProperty("undoCount").GetInt32()==before.GetProperty("state").GetProperty("undoCount").GetInt32()+1);
            using var stream=new MemoryStream(studio.Save()); var loaded=DxfDocument.Load(stream)!;Require(loaded.Blocks["Part"].Entities.Count==2);
            studio.History("undo",studio.CadRevision);
            Require(Json(studio.CadState()).GetProperty("blocks").EnumerateArray().Single(b=>b.GetProperty("name").GetString()=="Part").GetProperty("entities").GetInt32()==1);
        }));
        tests.Add(("block/Studio discard is explicit and leaves live source and history unchanged",()=>
        {
            using var studio=new Studio(false);studio.CadCommand(new(studio.CadRevision,new("CIRCLE",[new(0,0)],Value:2)));
            studio.CadCreateBlock(new(studio.CadRevision,"Part",new(0,0)));var before=Json(studio.CadState());
            var block=before.GetProperty("blocks").EnumerateArray().Single(b=>b.GetProperty("name").GetString()=="Part").GetProperty("id").GetString()!;
            studio.CadBeginBlock(new(studio.CadRevision,block));studio.CadCommand(new(studio.CadRevision,new("CIRCLE",[new(20,0)],Value:3)));
            Reject(()=>studio.CadFinishBlock(new(studio.CadRevision,0,false)));
            studio.CadFinishBlock(new(studio.CadRevision,0,false,true)); var after=Json(studio.CadState());
            Require(!studio.CadHasBranches && after.GetProperty("state").GetProperty("revision").GetInt64()==before.GetProperty("state").GetProperty("revision").GetInt64());
            Require(after.GetProperty("blocks").EnumerateArray().Single(b=>b.GetProperty("name").GetString()=="Part").GetProperty("entities").GetInt32()==1);
        }));
    }
}
