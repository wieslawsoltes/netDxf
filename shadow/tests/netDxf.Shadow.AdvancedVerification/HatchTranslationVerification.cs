using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;

internal static class HatchTranslationVerification
{
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Require(bool condition, string message = "Hatch translation assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Require(Math.Abs(actual - expected) < 1e-7, $"Expected {expected}, got {actual}");
    private static Hatch Rectangle(HatchPattern? pattern = null) => new(pattern ?? new HatchPattern("CUSTOM"),
        [new HatchBoundaryPath(new HatchBoundaryPath.Edge[] { new HatchBoundaryPath.Polyline
        { IsClosed = true, Vertexes = [new(10,20,.25), new(50,20,0), new(50,60,0), new(10,60,0)] } })], false);
    private static void Refuse(Action action)
    { try { action(); } catch (Exception e) when (e is NotSupportedException or InvalidOperationException or OperationCanceledException) { return; } throw new Exception("Expected translation refusal"); }
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var binary in new[] { false, true })
        tests.Add(($"affine/hatch authored block nonzero base point and DXF roundtrip {binary}", () =>
        {
            var document = new DxfDocument();
            var outline = new Polyline2D([new Vector2(10,20), new Vector2(50,20), new Vector2(50,60), new Vector2(10,60)], true);
            document.Entities.Add(outline);
            var source = new ShadowSession(document); var editor = new EditorSession(source);
            DraftingHatches.Create(source.Current, new([Id(outline)], Pattern:new("DASH", Scale:3, Angle:15, Origin:new(6,7)))).Execute(editor);
            var original = document.Entities.Hatches.Single();
            var before = source.Current; var ids = before.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray();
            var planner = new DraftingPlanner();
            planner.PlanBlockDefinition(before, new("Assembly", new(10,20), ids)).Execute(editor);
            var block = document.Blocks["Assembly"]; var copy = block.Entities.OfType<Hatch>().Single();
            var edge = (HatchBoundaryPath.Polyline)copy.BoundaryPaths.Single().Edges.Single();
            Near(edge.Vertexes[0].X,0); Near(edge.Vertexes[0].Y,0);
            Near(edge.Vertexes[2].X,40); Near(edge.Vertexes[2].Y,40);
            Near(copy.Pattern.Origin.X,-4); Near(copy.Pattern.Origin.Y,-13);
            Near(original.Pattern.Origin.X,6); Near(original.Pattern.Origin.Y,7);
            Require(!ReferenceEquals(copy.Pattern,original.Pattern));
            for (int i = 0; i < 4; i++)
            {
                editor.Undo(); Require(ReferenceEquals(document.Entities.Hatches.Single(),original));
                editor.Redo(); Require(ReferenceEquals(document.Blocks["Assembly"],block));
            }
            using var stream = new MemoryStream(); Require(document.Save(stream,binary)); stream.Position=0;
            var loaded = DxfDocument.Load(stream)!; var inserted = loaded.Entities.Inserts.Single();
            var reloaded = (HatchBoundaryPath.Polyline)inserted.Block.Entities.OfType<Hatch>().Single().BoundaryPaths.Single().Edges.Single();
            Near(inserted.Position.X+reloaded.Vertexes[0].X,10); Near(inserted.Position.Y+reloaded.Vertexes[0].Y,20);
            Require(before.DrawingEntities().Count()==2);
        }));
        tests.Add(("affine/hatch shared pattern copy-on-write preserves bulges seeds and undo", () =>
        {
            var pattern = new HatchPattern("TEST") { Origin=new(3,4), Scale=2, Angle=30, IsDouble=true, Type=HatchType.UserDefined };
            var family = new HatchPatternLineDefinition { Origin=new(1,2), Delta=new(0,3), Angle=15 };
            family.DashPattern.AddRange([1,-2]); pattern.LineDefinitions.Add(family);
            var first = Rectangle(pattern); var other = Rectangle(pattern); first.SeedPoints.Clear(); first.SeedPoints.Add(new(12,23));
            var doc = new DxfDocument(); doc.Entities.Add(first); doc.Entities.Add(other);
            var source = new ShadowSession(doc); var before=source.Current; var editor = new EditorSession(source);
            editor.Transform([Id(first)], Affine3.Move(new(5,-7,9)));
            var edge=(HatchBoundaryPath.Polyline)first.BoundaryPaths.Single().Edges.Single();
            Near(edge.Vertexes[0].X,15); Near(edge.Vertexes[0].Y,13); Near(edge.Vertexes[0].Z,.25);
            Near(first.Elevation,9); Near(first.SeedPoints[0].X,17); Near(first.SeedPoints[0].Y,16);
            Near(first.Pattern.Origin.X,8); Near(first.Pattern.Origin.Y,-3); Require(first.Pattern.IsDouble && first.Pattern.Type==HatchType.UserDefined);
            Near(first.Pattern.LineDefinitions[0].Origin.X,1); Near(first.Pattern.LineDefinitions[0].Origin.Y,2);
            Require(ReferenceEquals(other.Pattern,pattern) && !ReferenceEquals(first.Pattern,pattern));
            Near(other.Pattern.Origin.X,3); Near(((HatchBoundaryPath.Polyline)other.BoundaryPaths.Single().Edges.Single()).Vertexes[0].X,10);
            editor.Undo(); Require(ReferenceEquals(first.Pattern,pattern)); Near(edge.Vertexes[0].X,10);
            editor.Redo(); Near(edge.Vertexes[0].X,15); Require(before.Read(Id(first))["Elevation"].Number()==0);
        }));
        foreach (var normal in new[] { Vec3.UnitZ, -Vec3.UnitZ, new Vec3(0,1,0), new Vec3(1,2,3).Normalized })
        tests.Add(($"affine/hatch OCS translation {normal}", () =>
        {
            var hatch=Rectangle(HatchPattern.Solid); hatch.Normal=new(normal.X,normal.Y,normal.Z); hatch.Elevation=4;
            var doc=new DxfDocument();doc.Entities.Add(hatch);var source=new ShadowSession(doc);var editor=new EditorSession(source);
            var before=new CadSceneBuilder().Build(source.Current).Occurrences.Single().Geometry.Contours[0];
            var delta=new Vec3(5,-8,13);editor.Transform([Id(hatch)],Affine3.Move(delta));
            var after=new CadSceneBuilder().Build(source.Current).Occurrences.Single().Geometry.Contours[0];
            Require(after.Curves.Length==before.Curves.Length);
            for(int i=0;i<before.Curves.Length;i++) for(int sample=0;sample<=8;sample++)
                Require(Vec3.Distance(after.Curves[i].At(sample/8.0),before.Curves[i].At(sample/8.0)+delta)<1e-7);
            editor.Undo();Near(hatch.Elevation,4);
        }));
        tests.Add(("affine/hatch conic line and rational spline translation retains analytic data", () =>
        {
            var spline=new HatchBoundaryPath.Spline { Degree=2, IsRational=true,
                Knots=[0,0,0,1,1,1], ControlPoints=[new(0,0,1),new(2,4,.5),new(5,0,2)], StartTangent=new(2,3), EndTangent=new(4,5) };
            spline.FitPoints.Add(new(1,2));
            var edges=new HatchBoundaryPath.Edge[] {
                new HatchBoundaryPath.Line { Start=new(0,0), End=new(1,2) },
                new HatchBoundaryPath.Arc { Center=new(10,20), Radius=5, StartAngle=10, EndAngle=280, IsCounterclockwise=true },
                new HatchBoundaryPath.Ellipse { Center=new(20,30), EndMajorAxis=new(5,2), MinorRatio=.5, StartAngle=0, EndAngle=360, IsCounterclockwise=true }, spline };
            var hatch=new Hatch(HatchPattern.Solid,edges.Select(e=>new HatchBoundaryPath(new[]{e})),false);
            var doc=new DxfDocument();doc.Entities.Add(hatch);var source=new ShadowSession(doc);var editor=new EditorSession(source);
            editor.Transform([Id(hatch)],Affine3.Move(new(7,-3,4)));
            Near(((HatchBoundaryPath.Line)edges[0]).Start.X,7); Near(((HatchBoundaryPath.Arc)edges[1]).Center.Y,17);
            Near(((HatchBoundaryPath.Arc)edges[1]).StartAngle,10); Near(((HatchBoundaryPath.Ellipse)edges[2]).EndMajorAxis.X,5);
            Near(spline.ControlPoints[1].X,9);Near(spline.ControlPoints[1].Y,1);Near(spline.ControlPoints[1].Z,.5);
            Near(spline.FitPoints[0].X,8); Near(spline.StartTangent!.Value.X,2); Near(spline.EndTangent!.Value.Y,5);
            editor.Undo(); Near(spline.ControlPoints[1].X,2); editor.Redo();Near(spline.ControlPoints[1].Z,.5);
        }));
        tests.Add(("affine/hatch translation also works in immutable memory stores", () =>
        {
            var hatch=Rectangle(HatchPattern.Solid);var doc=new DxfDocument();doc.Entities.Add(hatch);var source=new ShadowSession(doc);
            var memory=new ShadowMemoryStore(source.Current);var editor=new EditorSession(memory);
            editor.Transform([Id(hatch)],Affine3.Move(new(5,7,9)));
            Near(memory.Current.Read(Id(hatch))["Elevation"].Number(),9);Near(hatch.Elevation,0);
            Near(memory.Current.Read(Id(hatch))["BoundaryPaths"].Items.First()["Edges"].Items.First()["Vertexes"].Items.First().Vector().Z,.25);
            editor.Undo();Near(memory.Current.Read(Id(hatch))["Elevation"].Number(),0);
        }));
        tests.Add(("affine/hatch rotation scaling budgets and cancellation fail before publication", () =>
        {
            var hatch=Rectangle(HatchPattern.Solid);var doc=new DxfDocument();doc.Entities.Add(hatch);var source=new ShadowSession(doc);var before=source.Current;
            foreach(var matrix in new[]{Affine3.RotateZ(.3),Affine3.Scale(new(2,2,2)),Affine3.Scale(new(2,1,1))})
                Refuse(()=>AffineEditOperations.Plan(before,[Id(hatch)],matrix));
            Refuse(()=>AffineEditOperations.Plan(before,[Id(hatch)],Affine3.Move(new(1,2,3)),new(MaximumCoordinates:1)));
            Refuse(()=>AffineEditOperations.Plan(before,[Id(hatch)],Affine3.Move(new(1,2,3)),cancellationToken:new(true)));
            Require(ReferenceEquals(before,source.Refresh()));
        }));
        tests.Add(("affine/hatch external shared edge and associative boundaries are refused atomically", () =>
        {
            var edge=new HatchBoundaryPath.Arc { Center=new(10,20),Radius=5,StartAngle=0,EndAngle=360,IsCounterclockwise=true };
            var a=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath(new HatchBoundaryPath.Edge[]{edge})],false);
            var b=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath(new HatchBoundaryPath.Edge[]{edge})],false);
            var doc=new DxfDocument();doc.Entities.Add(a);doc.Entities.Add(b);var source=new ShadowSession(doc);var before=source.Current;
            Refuse(()=>AffineEditOperations.Plan(before,[Id(a)],Affine3.Move(new(5,6))));Require(ReferenceEquals(before,source.Refresh()));
            var circle=new Circle(Vector3.Zero,5);var associative=new Hatch(HatchPattern.Solid,[new HatchBoundaryPath(new EntityObject[]{circle})],true);
            var second=new DxfDocument();second.Entities.Add(associative);var shared=new ShadowSession(second);
            Refuse(()=>AffineEditOperations.Plan(shared.Current,[Id(associative)],Affine3.Move(new(1,2))));
        }));
    }
}
