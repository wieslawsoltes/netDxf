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

internal static class CadAuthoringVerification
{
    private sealed class Drawing(params EntityObject[] entities)
    {
        internal DxfDocument Document { get; } = Make(entities);
        internal ShadowSession Source = null!;
        internal EditorSession Editor = null!;
        internal readonly DraftingPlanner Planner = new();
        internal Drawing Attach() { Source = new(Document); Editor = new(Source); return this; }
        internal ImmutableArray<ObjectId> Ids => Source.Current.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray();
        private static DxfDocument Make(EntityObject[] entities) { var d = new DxfDocument(); d.Entities.Add(entities); return d; }
    }
    private static void Require(bool condition, string message = "CAD authoring assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < 1e-7, $"Expected {expected:R}; actual {actual:R}");
    private static void Refuse(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException or ArithmeticException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected refusal before publication.");
    }
    private static DxfDocument Reload(DxfDocument document, bool binary)
    { using var stream = new MemoryStream(); Require(document.Save(stream, binary)); stream.Position = 0; return DxfDocument.Load(stream)!; }
    private static Polyline2D Rectangle(double x, double y, double width, double height) =>
        new([new Vector2(x,y), new Vector2(x+width,y), new Vector2(x+width,y+height), new Vector2(x,y+height)], true);
    private static ObjectId BlockId(Drawing d, string name) => d.Source.Current.Document["Blocks"].Items.Single(b => b["Name"].Text == name).Id!.Value;

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Add(string name, Action test) => tests.Add(("authoring/" + name, test));
        Add("block base point preserves source appearance and canonical undo", () =>
        {
            var line = new Line(new Vector3(10,20,0), new Vector3(30,20,0)) { Color = new AciColor((byte)12,(byte)45,(byte)123) };
            var f = new Drawing(line, new Circle(new Vector3(20,30,0), 4)).Attach(); var before = f.Source.Current;
            var plan = f.Planner.PlanBlockDefinition(before, new("Pump", new(10,20), f.Ids)); plan.Execute(f.Editor);
            Require(plan.Created.Length == 1 && f.Document.Entities.All.Single() is Insert);
            var block = f.Document.Blocks["Pump"]; var clone = block.Entities.OfType<Line>().Single();
            Near(clone.StartPoint.X,0); Near(clone.StartPoint.Y,0); Near(clone.EndPoint.X,20);
            Require(!ReferenceEquals(clone,line) && clone.Color.UseTrueColor && clone.Color.B == 123);
            Near(f.Document.Entities.Inserts.Single().Position.X,10); Near(f.Document.Entities.Inserts.Single().Position.Y,20);
            Require(before.DrawingEntities().Count()==2 && f.Editor.State.UndoCount==1);
            f.Editor.Undo(); Require(f.Document.Entities.All.Count()==2 && !f.Document.Blocks.Contains("Pump"));
            Require(ReferenceEquals(f.Document.Entities.Lines.Single(),line));
            f.Editor.Redo(); Require(ReferenceEquals(f.Document.Blocks["Pump"],block));
        });
        foreach (var disposition in Enum.GetValues<BlockSourceDisposition>())
            Add("block disposition " + disposition, () =>
            {
                var f = new Drawing(new Circle(Vector3.Zero,5)).Attach();
                f.Planner.PlanBlockDefinition(f.Source.Current,new("Part",Vec3.Zero,f.Ids,disposition)).Execute(f.Editor);
                Require(f.Document.Blocks["Part"].Entities.Count==1);
                Require(f.Document.Entities.All.Count()==(disposition==BlockSourceDisposition.Delete?0:1));
                if(disposition==BlockSourceDisposition.Retain) Require(f.Document.Entities.All.Single() is Circle);
                if(disposition==BlockSourceDisposition.ConvertToInsert) Require(f.Document.Entities.All.Single() is Insert);
            });
        foreach(var binary in new[]{false,true})
            Add("block DXF roundtrip " + binary, () =>
            {
                var f = new Drawing(Rectangle(10,20,30,40)).Attach();
                f.Planner.PlanBlockDefinition(f.Source.Current,new("Valve",new(10,20),f.Ids)).Execute(f.Editor);
                var doc = Reload(f.Document,binary); var insert = doc.Entities.Inserts.Single();
                Require(insert.Block.Name=="Valve" && insert.Block.Entities.Single() is Polyline2D);
                Near(((Polyline2D)insert.Block.Entities.Single()).Vertexes[2].Position.X,30);
            });
        Add("block definition works in immutable memory store", () =>
        {
            var f = new Drawing(new Line(Vector3.Zero,new Vector3(20,0,0))).Attach();
            var memory = new ShadowMemoryStore(f.Source.Current); var editor = new EditorSession(memory);
            f.Planner.PlanBlockDefinition(memory.Current,new("Memory",new(5,0),f.Ids)).Execute(editor);
            var block = memory.Current.Document["Blocks"].Items.Single(b=>b["Name"].Text=="Memory");
            Near(block["Entities"].Items.Single()["StartPoint"].Vector().X,-5);
            Require(f.Document.Entities.Lines.Count()==1 && !f.Document.Blocks.Contains("Memory"));
            editor.Undo(); Require(memory.Current.DrawingEntities().Single().Type=="Line");
        });
        Add("redefine retains shared definition and insertion identities", () =>
        {
            var block = new Block("Part") { Origin = new Vector3(2,3,0) }; block.Entities.Add(new Circle(new Vector3(2,3,0),1));
            var first = new Insert(block,new Vector3(10,20,0)); var second = new Insert(block,new Vector3(50,60,0));
            var replacement = new Line(new Vector3(100,100,0),new Vector3(120,100,0));
            var f = new Drawing(first,second,replacement).Attach(); var id=f.Source.Current.DrawingEntities().Single(e=>e.Type=="Line").Id!.Value;
            var plan=f.Planner.PlanBlockDefinition(f.Source.Current,new("Part",new(100,100),[id],BlockSourceDisposition.Retain,BlockId(f,"Part")));
            plan.Execute(f.Editor);
            Require(ReferenceEquals(first.Block,block) && ReferenceEquals(second.Block,block));
            Near(block.Entities.OfType<Line>().Single().StartPoint.X,2);Near(block.Entities.OfType<Line>().Single().EndPoint.X,22);
            Near(first.Position.X,10); Near(second.Position.X,50); Require(f.Document.Entities.Lines.Single()==replacement);
            f.Editor.Undo(); Require(block.Entities.Single() is Circle); f.Editor.Redo();Require(block.Entities.Single() is Line);
        });
        Add("block rename propagates through native name table and history", () =>
        {
            var f=new Drawing(new Circle(Vector3.Zero,4)).Attach();
            f.Planner.PlanBlockDefinition(f.Source.Current,new("Part",Vec3.Zero,f.Ids)).Execute(f.Editor);
            var id=BlockId(f,"Part");f.Planner.RenameBlock(f.Source.Current,id,"Revised").Execute(f.Editor);
            Require(f.Document.Blocks.Contains("Revised") && !f.Document.Blocks.Contains("Part") && f.Document.Entities.Inserts.Single().Block.Name=="Revised");
            f.Editor.Undo();Require(f.Document.Blocks.Contains("Part"));
        });
        Add("recursive redefinition and duplicate names are atomic refusals", () =>
        {
            var block=new Block("Part");block.Entities.Add(new Circle(Vector3.Zero,3));
            var f=new Drawing(new Insert(block,Vector3.Zero)).Attach();var before=f.Source.Current;
            Refuse(()=>f.Planner.PlanBlockDefinition(before,new("Part",Vec3.Zero,f.Ids)));
            Refuse(()=>f.Planner.PlanBlockDefinition(before,new("Part",Vec3.Zero,f.Ids,Redefine:BlockId(f,"Part"))));
            Refuse(()=>f.Planner.PlanBlockDefinition(before,new("bad/name",Vec3.Zero,f.Ids)));
            Require(ReferenceEquals(before,f.Source.Current) && f.Editor.State.UndoCount==0);
        });
        Add("block stale plans budgets and cancellation retain source", () =>
        {
            var f=new Drawing(new Line(Vector3.Zero,Vector3.UnitX),new Circle(Vector3.Zero,3)).Attach();var before=f.Source.Current;
            Refuse(()=>new DraftingPlanner(new(MaximumEntities:1)).PlanBlockDefinition(before,new("Part",Vec3.Zero,f.Ids)));
            Refuse(()=>f.Planner.PlanBlockDefinition(before,new("Part",Vec3.Zero,f.Ids),cancellationToken:new(true)));
            var plan=f.Planner.PlanBlockDefinition(before,new("Part",Vec3.Zero,f.Ids));
            f.Editor.SetProperty(f.Ids[0],"IsVisible",ShadowValue.From(false));Refuse(()=>plan.Execute(f.Editor));
            Require(!f.Document.Blocks.Contains("Part"));
        });
        Add("hatch polygon preserves analytic native edges and undo", () =>
        {
            var f=new Drawing().Attach();var initial=f.Source.Current;
            var plan=DraftingHatches.Create(initial,new(Polygons:[[new(0,0),new(30,0),new(30,20),new(0,20)]]));plan.Execute(f.Editor);
            var hatch=f.Document.Entities.Hatches.Single();Require(!hatch.Associative && hatch.Pattern.Fill==HatchFillType.SolidFill);
            var edge=(HatchBoundaryPath.Polyline)hatch.BoundaryPaths.Single().Edges.Single();Require(edge.Vertexes.Length==4 && edge.IsClosed);
            Require(!initial.DrawingEntities().Any() && f.Editor.State.UndoCount==1);
            f.Editor.Undo();Require(!f.Document.Entities.All.Any());f.Editor.Redo();Require(f.Document.Entities.Hatches.Single()==hatch);
        });
        foreach(var binary in new[]{false,true})
            Add("hatch source circle ellipse bulge roundtrip " + binary, () =>
            {
                var poly=Rectangle(30,0,10,10);poly.Vertexes[0].Bulge=.5;
                var f=new Drawing(new Circle(Vector3.Zero,10),new Ellipse(new Vector3(70,0,0),20,10){Rotation=30},poly).Attach();
                DraftingHatches.Create(f.Source.Current,new(f.Ids)).Execute(f.Editor);
                var hatch=Reload(f.Document,binary).Entities.Hatches.Single();var edges=hatch.BoundaryPaths.SelectMany(p=>p.Edges).ToArray();
                Require(edges.Length==3 && edges.OfType<HatchBoundaryPath.Arc>().Count()==1 && edges.OfType<HatchBoundaryPath.Ellipse>().Count()==1);
                Near(edges.OfType<HatchBoundaryPath.Polyline>().Single().Vertexes[0].Z,.5);
                Require(f.Document.Entities.All.Count()==4);
            });
        foreach(var style in Enum.GetValues<CadHatchIslandStyle>())
            Add("hatch authored islands " + style, () =>
            {
                var f=new Drawing(new Circle(Vector3.Zero,20),new Circle(Vector3.Zero,10),new Circle(Vector3.Zero,5)).Attach();
                var plan=DraftingHatches.Create(f.Source.Current,new(f.Ids,Pattern:new(Islands:style)));plan.Execute(f.Editor);
                var scene=new RenderCompiler().Compile(f.Source.Current);
                var hatch=scene.Items.Single(i=>i.Entity==plan.Created[0]);
                Require(hatch.Regions.Single().Contours.Length==(style==CadHatchIslandStyle.Normal?3:style==CadHatchIslandStyle.Outer?2:1));
            });
        foreach(var name in DraftingHatches.PatternNames)
            Add("hatch named pattern " + name, () =>
            {
                var f=new Drawing(Rectangle(0,0,20,20)).Attach();
                DraftingHatches.Create(f.Source.Current,new(f.Ids,Pattern:new(name,2,15))).Execute(f.Editor);
                var hatch=f.Document.Entities.Hatches.Single();Require(hatch.Pattern.Name==name);Near(hatch.Pattern.Scale,2);Near(hatch.Pattern.Angle,15);
                var scene=new RenderCompiler().Compile(f.Source.Current);Require(scene.Items.Any());
            });
        Add("hatch edit copies shared pattern and preserves all native boundaries", () =>
        {
            var pattern=HatchPattern.Solid;
            var a=new Hatch(pattern,[new HatchBoundaryPath([new Circle(Vector3.Zero,10)])],false);
            var b=new Hatch(pattern,[new HatchBoundaryPath([new Circle(new Vector3(30,0,0),10)])],false);
            var f=new Drawing(a,b).Attach();var id=f.Ids[0];var edges=a.BoundaryPaths[0].Edges.ToArray();
            DraftingHatches.ChangePattern(f.Source.Current,[id],new("LINE",3,40,Islands:CadHatchIslandStyle.Outer)).Execute(f.Editor);
            Require(a.Pattern.Name=="LINE" && b.Pattern.Name=="SOLID" && a.BoundaryPaths[0].Edges.SequenceEqual(edges));
            f.Editor.Undo();Require(a.Pattern.Name=="SOLID");f.Editor.Redo();Require(a.Pattern.Name=="LINE");
        });
        Add("hatch memory store and custom line definitions", () =>
        {
            var f=new Drawing(Rectangle(0,0,20,20)).Attach();var memory=new ShadowMemoryStore(f.Source.Current);var editor=new EditorSession(memory);
            var pattern=new DraftingHatchPattern("MY_PATTERN",Lines:[new(30,Vec3.Zero,new(.5,2),[1,-1])]);
            var plan=DraftingHatches.Create(memory.Current,new(f.Ids,Pattern:pattern));plan.Execute(editor);
            Require(memory.Current.Read(plan.Created[0])["Pattern"]["Name"].Text=="MY_PATTERN");
            Require(!f.Document.Entities.Hatches.Any());
        });
        Add("hatch rejects open crossing touching nonplanar and over-dense inputs", () =>
        {
            var f=new Drawing(new Line(Vector3.Zero,Vector3.UnitX)).Attach();var before=f.Source.Current;
            Refuse(()=>DraftingHatches.Create(before,new(f.Ids)));
            Refuse(()=>DraftingHatches.Create(before,new(Polygons:[[new(0,0),new(5,5),new(0,5),new(5,0)]])));
            Refuse(()=>DraftingHatches.Create(before,new(Polygons:[[new(0,0,1),new(10,0,1),new(10,10,1)]])));
            Refuse(()=>DraftingHatches.Create(before,new(Polygons:[[new(0,0),new(10,0),new(10,10)]],Pattern:new("LINE",1e-9))));
            Refuse(()=>DraftingHatches.Create(before,new(Polygons:[[new(0,0),new(10,0),new(10,10)]],Pattern:new(Scale:double.NaN))));
            Refuse(()=>DraftingHatches.Create(before,new(Polygons:[[new(0,0),new(10,0),new(10,10)]]),cancellationToken:new(true)));
            Require(ReferenceEquals(before,f.Source.Current) && f.Editor.State.UndoCount==0);
        });
        Add("Studio block and hatch operations use current selected sources", () =>
        {
            using var studio=new Studio(false);
            studio.CadCommand(new(studio.CadRevision,new("RECTANG",[new(0,0),new(20,20)])));
            studio.CadCreateHatch(new(studio.CadRevision,new(Pattern:new("LINE",2))));
            var data=JsonSerializer.SerializeToElement(studio.CadState(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Require(data.GetProperty("selectedEntities").GetArrayLength()==1);
            studio.CadChangeHatchPattern(new(studio.CadRevision,new("SOLID")));
            studio.CadSelect(new(studio.CadRevision,"all"));
            studio.CadDefineBlock(new(studio.CadRevision,new("Symbol",Vec3.Zero,[])));
            var saved=studio.Save();using var input=new MemoryStream(saved);var document=DxfDocument.Load(input)!;
            Require(document.Entities.All.Single() is Insert && document.Blocks["Symbol"].Entities.Count==2);
        });
    }
}
