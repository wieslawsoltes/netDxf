using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Studio;
using System.Text.Json;

internal static class ModifyParityVerification
{
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Check(Math.Abs(actual - expected) < 1e-7, $"Expected {expected}, got {actual}.");
    private static void Near(Vector3 actual, Vector3 expected) => Check(Vector3.Distance(actual, expected) < 1e-7, $"Expected {expected}, got {actual}.");
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or NotSupportedException) { return; } throw new InvalidOperationException("Expected rejection."); }
    private static ObjectId Id(ShadowSnapshot snapshot, EntityObject entity) => snapshot.DrawingEntities().Single(e => e["Handle"].Text == entity.Handle).Id!.Value;
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("modify-parity/stretch crossing vertices and center/insertion anchors atomically", () =>
        {
            var document = new DxfDocument();
            var partial = new Line(new Vector2(0, 0), new Vector2(10, 0));
            var whole = new Line(new Vector2(8, 1), new Vector2(10, 1));
            var crossing = new Line(new Vector2(0, 2), new Vector2(20, 2));
            var polyline = new Polyline2D([new Polyline2DVertex(0, 3) { Bulge = .5, StartWidth = 1, EndWidth = 2 }, new(10, 3), new(10, 4)]);
            var movingCircle = new Circle(new Vector2(10, 5), 3); var fixedCircle = new Circle(new Vector2(4, 5), 5);
            var block = new Block("StretchMarker"); block.Entities.Add(new Line(Vector2.Zero, Vector2.UnitX));
            block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Position = new(2, 0, 0), Value = "X" });
            var insert = new Insert(block, new Vector3(10, 6, 0));
            document.Entities.Add([partial, whole, crossing, polyline, movingCircle, fixedCircle, insert]);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var before = source.Current;
            var ids = before.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray();
            var request = new DraftingCommand("STRETCH", [new(7, -1), new(12, 8), Vec3.Zero, new(5, 2)], ids);
            var plan = new DraftingPlanner().Plan(before, request);
            Check(ReferenceEquals(before, source.Current), "STRETCH planning mutated the source.");
            var branch = new EditorSession(new ShadowMemoryStore(before)); plan.Execute(branch);
            Near(branch.Snapshot.Read(Id(before, partial))["EndPoint"].Vector().X, 15); Near(partial.EndPoint.X, 10);
            plan.Execute(editor);
            Near(partial.StartPoint, Vector3.Zero); Near(partial.EndPoint, new(15, 2, 0));
            Near(whole.StartPoint, new(13, 3, 0)); Near(whole.EndPoint, new(15, 3, 0));
            Near(crossing.EndPoint, new(20, 2, 0)); Near(polyline.Vertexes[1].Position.X, 15);
            Near(polyline.Vertexes[0].Bulge, .5); Near(polyline.Vertexes[0].StartWidth, 1); Near(polyline.Vertexes[0].EndWidth, 2);
            Near(movingCircle.Center, new(15, 7, 0)); Near(movingCircle.Radius, 3); Near(fixedCircle.Center, new(4, 5, 0));
            Near(insert.Position, new(15, 8, 0)); Near(insert.Attributes.Single().Position, new(17, 8, 0));
            Check(editor.State.UndoCount == 1 && insert.Block == block && block.Entities.Single() is Line unchanged && unchanged.EndPoint == Vector3.UnitX,
                "STRETCH did not preserve shared definitions and one-step history.");
            editor.Undo(); Near(partial.EndPoint, new(10, 0, 0)); Near(insert.Position, new(10, 6, 0));
            editor.Redo(); Near(partial.EndPoint, new(15, 2, 0));
        }));
        tests.Add(("modify-parity/stretch rejects collapsed, locked, excessive and unsupported selection without mutation", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0));
            var arc = new Arc(Vector2.Zero, 5, 0, 90); document.Entities.Add([line, arc]);
            var source = new ShadowSession(document); var before = source.Current; var planner = new DraftingPlanner();
            var lineId = Id(before, line); var arcId = Id(before, arc);
            Reject(() => planner.Plan(before, new("STRETCH", [new(9, -1), new(11, 1), Vec3.Zero, new(-10, 0)], [lineId])));
            Reject(() => planner.Plan(before, new("STRETCH", [new(4, -1), new(11, 2), Vec3.Zero, new(1, 1)], [lineId, arcId])));
            Reject(() => new DraftingPlanner(new(MaximumClonedObjects: 1)).Plan(before, new("STRETCH", [new(-1, -1), new(11, 11), Vec3.Zero, new(1, 1)], [lineId])));
            Reject(() => planner.Plan(before, new("STRETCH", [new(20, 20), new(30, 30), Vec3.Zero, Vec3.UnitX], [lineId])));
            Check(ReferenceEquals(before, source.Current) && line.EndPoint.X == 10, "Rejected STRETCH published a partial change.");
            source.MutateSource(d => line.Layer.IsLocked = true);
            Reject(() => planner.Plan(source.Current, new("STRETCH", [new(-1, -1), new(11, 11), Vec3.Zero, Vec3.UnitX], [lineId])));
        }));
        tests.Add(("modify-parity/stretch fully enclosed arcs, splines and solid hatches use exact affine movement", () =>
        {
            var document = new DxfDocument(); var arc = new Arc(new Vector2(2, 2), 1, 0, 210);
            var spline = new Spline([new Vector3(1, 1, 0), new(2, 3, 0), new(3, 2, 0), new(4, 4, 0)], null, 3);
            var hatch = new Hatch(HatchPattern.Solid, [new HatchBoundaryPath(new HatchBoundaryPath.Edge[] {
                new HatchBoundaryPath.Line { Start = new(1, 1), End = new(3, 1) },
                new HatchBoundaryPath.Line { Start = new(3, 1), End = new(3, 3) },
                new HatchBoundaryPath.Line { Start = new(3, 3), End = new(1, 3) },
                new HatchBoundaryPath.Line { Start = new(1, 3), End = new(1, 1) } })], false);
            document.Entities.Add([arc, spline, hatch]); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var ids = source.Current.DrawingEntities().Select(e => e.Id!.Value).ToImmutableArray();
            new DraftingPlanner().Plan(source.Current, new("STRETCH", [Vec3.Zero, new(6, 6), Vec3.Zero, new(10, 20)], ids)).Execute(editor);
            Near(arc.Center, new(12, 22, 0)); Near(spline.ControlPoints[0], new(11, 21, 0));
            Near(((HatchBoundaryPath.Line)hatch.BoundaryPaths.Single().Edges[0]).Start.X, 11);
            editor.Undo(); Near(arc.Center, new(2, 2, 0)); Near(spline.ControlPoints[0], new(1, 1, 0));
            editor.Redo(); Near(arc.Center, new(12, 22, 0));
            var bowed = new DxfDocument(); var upper = new Arc(Vector2.Zero, 10, 0, 180); bowed.Entities.Add(upper);
            var bowedSource = new ShadowSession(bowed);
            Reject(() => new DraftingPlanner().Plan(bowedSource.Current, new("STRETCH", [new(-11, -1), new(11, 1), Vec3.Zero, Vec3.UnitX], [Id(bowedSource.Current, upper)])));
            Near(upper.Center, Vector3.Zero);
        }));
        tests.Add(("modify-parity/break line projects off-curve points and preserves source identity, style and undo", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(20, 0)) { Color = AciColor.Red, Thickness = 2 };
            document.Entities.Add(line); var source = new ShadowSession(document); var before = source.Current; var id = Id(before, line); var editor = new EditorSession(source);
            var plan = new DraftingPlanner().Plan(before, new("BREAK", [new(12, 8), new(5, -4)], [id]));
            Check(plan.Created.Length == 1 && document.Entities.Lines.Count() == 1, "BREAK plan was not pure or did not describe new piece.");
            var memory = new EditorSession(new ShadowMemoryStore(before)); plan.Execute(memory);
            Check(memory.Snapshot.DrawingEntities().Count() == 2 && line.EndPoint.X == 20, "Memory BREAK changed native source.");
            plan.Execute(editor); var second = document.Entities.Lines.Single(e => e != line);
            Near(line.StartPoint.X, 0); Near(line.EndPoint.X, 5); Near(second.StartPoint.X, 12); Near(second.EndPoint.X, 20);
            Check(second.Color.Index == 1 && second.Thickness == 2 && Id(source.Current, line) == id && editor.State.UndoCount == 1, "BREAK lost identity or appearance.");
            editor.Undo(); Check(document.Entities.Lines.Single() == line, "BREAK undo lost original entity."); Near(line.EndPoint.X, 20);
            editor.Redo(); Check(document.Entities.Lines.Count() == 2, "BREAK redo lost piece.");
            using var stream = new MemoryStream(); Check(document.Save(stream), "BREAK DXF save failed."); stream.Position = 0;
            Check(DxfDocument.Load(stream)!.Entities.Lines.Count() == 2, "BREAK pieces failed DXF roundtrip.");
        }));
        tests.Add(("modify-parity/break-at-point splits line without gap and break clips an end", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(20, 0)); document.Entities.Add(line);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var planner = new DraftingPlanner(); var id = Id(source.Current, line);
            planner.Plan(source.Current, new("BREAKATPOINT", [new(5, 4)], [id])).Execute(editor);
            var other = document.Entities.Lines.Single(l => l != line); Near(line.EndPoint, other.StartPoint); Near(line.EndPoint.X, 5);
            editor.Undo(); planner.Plan(source.Current, new("BREAK", [new(12, 3), new(50, 3)], [id])).Execute(editor);
            Check(document.Entities.Lines.Single() == line, "Breaking one end should retain the existing entity."); Near(line.EndPoint.X, 12);
            editor.Undo(); planner.Plan(source.Current, new("BREAK", [new(-5, 3), new(50, 3)], [id])).Execute(editor);
            Check(!document.Entities.Lines.Any(), "BREAK encompassing entire line did not remove it."); editor.Undo(); Check(document.Entities.Lines.Single() == line, "Whole-gap undo failed.");
        }));
        tests.Add(("modify-parity/break circle removes counterclockwise gap and open arc projects to extent", () =>
        {
            var document = new DxfDocument(); var circle = new Circle(new Vector2(10, 20), 5) { Color = AciColor.Blue };
            document.Entities.Add(circle); var source = new ShadowSession(document); var editor = new EditorSession(source); var planner = new DraftingPlanner();
            planner.Plan(source.Current, new("BREAK", [new(20, 20), new(10, 40)], [Id(source.Current, circle)])).Execute(editor);
            var arc = document.Entities.Arcs.Single(); Check(!document.Entities.Circles.Any(), "Circle BREAK did not produce an arc.");
            Near(arc.StartAngle, 90); Near(arc.EndAngle, 0); Near(arc.Radius, 5); Near(arc.Center, circle.Center); Check(arc.Color.Index == 5, "Circle BREAK lost appearance.");
            editor.Undo(); Check(document.Entities.Circles.Single() == circle, "Circle undo lost native identity."); editor.Redo();
            var arcId = Id(source.Current, arc); planner.Plan(source.Current, new("BREAKATPOINT", [new(0, 20)], [arcId])).Execute(editor);
            Check(document.Entities.Arcs.Count() == 2, "Arc split did not create two native arcs.");
            Near(arc.EndAngle, 180); Near(document.Entities.Arcs.Single(a => a != arc).StartAngle, 180);
        }));
        tests.Add(("modify-parity/break polyline preserves analytic bulges and interpolates tapered widths", () =>
        {
            foreach (var bulge in new[] { 1d, -1d, .5, -.5 })
            {
                var document = new DxfDocument(); var polyline = new Polyline2D([new Polyline2DVertex(0, 0) { Bulge = bulge, StartWidth = 2, EndWidth = 6 }, new(10, 0), new(20, 0)]);
                document.Entities.Add(polyline); var curve = (EllipseCurve)StandardEntityProjector.Bulge(Vec3.Zero, new(10, 0), bulge);
                var source = new ShadowSession(document); var editor = new EditorSession(source); var before = source.Current;
                var cut = curve.At(.25); var plan = new DraftingPlanner().Plan(before, new("BREAKATPOINT", [cut], [Id(before, polyline)]));
                plan.Execute(editor); var second = document.Entities.Polylines2D.Single(p => p != polyline);
                Near(polyline.Vertexes[0].Bulge, Math.Tan(Math.Atan(bulge) * .25));
                Near(second.Vertexes[0].Bulge, Math.Tan(Math.Atan(bulge) * .75));
                Near(polyline.Vertexes[0].StartWidth, 2); Near(polyline.Vertexes[0].EndWidth, 3);
                Near(second.Vertexes[0].StartWidth, 3); Near(second.Vertexes[0].EndWidth, 6);
                Near(polyline.Vertexes[^1].Position.X, cut.X); Near(second.Vertexes[0].Position.Y, cut.Y);
                Check(polyline.Vertexes.Count == 2 && second.Vertexes.Count == 3, "Polyline split lost its following segment.");
                editor.Undo(); Near(polyline.Vertexes[0].Bulge, bulge); Check(polyline.Vertexes.Count == 3, "Polyline undo lost vertices.");
                editor.Redo(); Check(document.Entities.Polylines2D.Count() == 2, "Polyline redo failed.");
            }
        }));
        tests.Add(("modify-parity/break closed polyline retains one open path across authored seam", () =>
        {
            var document = new DxfDocument(); var polyline = new Polyline2D([new Vector2(0, 0), new(10, 0), new(10, 10), new(0, 10)], true);
            document.Entities.Add(polyline); var source = new ShadowSession(document); var editor = new EditorSession(source); var id = Id(source.Current, polyline);
            new DraftingPlanner().Plan(source.Current, new("BREAK", [new(5, 0), new(10, 5)], [id])).Execute(editor);
            Check(document.Entities.Polylines2D.Single() == polyline && !polyline.IsClosed && polyline.Vertexes.Count == 5, "Closed BREAK lost root identity or seam geometry.");
            Near(polyline.Vertexes[0].Position.Y, 5); Near(polyline.Vertexes[^1].Position.X, 5); Near(polyline.Vertexes[2].Position.Y, 10);
            editor.Undo(); Check(polyline.IsClosed && polyline.Vertexes.Count == 4, "Closed polyline undo failed.");
        }));
        tests.Add(("modify-parity/break rejects metadata, closed point cuts and exhausted budgets atomically", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0)); var circle = new Circle(Vector2.Zero, 5);
            var polyline = new Polyline2D([Vector2.Zero, Vector2.UnitX, new Vector2(2, 0)]);
            var data = new XData(new ApplicationRegistry("APP")); data.XDataRecord.Add(new(XDataCode.String, "stable domain key")); line.XData.Add(data);
            document.Entities.Add([line, circle, polyline]); var source = new ShadowSession(document); var before = source.Current; var planner = new DraftingPlanner();
            Reject(() => planner.Plan(before, new("BREAKATPOINT", [new(5, 0)], [Id(before, line)])));
            Reject(() => planner.Plan(before, new("BREAKATPOINT", [new(5, 0)], [Id(before, circle)])));
            Reject(() => planner.Plan(before, new("BREAK", [new(5, 0), new(10, 0)], [Id(before, circle)])));
            Reject(() => new DraftingPlanner(new(MaximumClonedObjects: 1)).Plan(before, new("BREAKATPOINT", [new(.5, 0)], [Id(before, polyline)])));
            Reject(() => new DraftingPlanner(new(MaximumEntities: 1)).Plan(before, new("BREAKATPOINT", [new(.5, 0)], [Id(before, polyline)])));
            Reject(() => planner.Plan(before, new("BREAKATPOINT", [Vec3.Zero], [Id(before, polyline)])));
            Check(ReferenceEquals(before, source.Current) && document.Entities.All.Count() == 3 && line.XData["APP"] == data, "Rejected BREAK changed source graph.");
            try { planner.Plan(before, new("BREAKATPOINT", [new(.5, 0)], [Id(before, polyline)]), cancellationToken: new(true)); throw new InvalidOperationException("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }));
        tests.Add(("modify-parity/Studio commands consume exact selected roots and remain replayable", () =>
        {
            using var studio = new Studio(false);
            studio.CadCommand(new(studio.CadRevision, new("LINE", [Vec3.Zero, new(10, 0)])));
            studio.CadWorkflowStart(new(studio.CadRevision, "Modify command workflow"));
            studio.CadCommand(new(studio.CadRevision, new("STRETCH", [new(5, -1), new(12, 1), Vec3.Zero, new(5, 0)])));
            var state = JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions());
            Near(state.GetProperty("state").GetProperty("bounds").GetProperty("maxX").GetDouble(), 15);
            studio.CadCommand(new(studio.CadRevision, new("BREAKATPOINT", [new(5, 0)])));
            studio.CadCommand(new(studio.CadRevision, new("BREAK", [new(8, 0), new(10, 0)])));
            state = JsonSerializer.SerializeToElement(studio.CadState(), StudioJson.CreateOptions());
            Check(state.GetProperty("itemCount").GetInt32() == 3, "Studio did not forward BREAK selection.");
            studio.CadWorkflowStop(new(studio.CadRevision));
            var report = studio.CadWorkflowReplay(new(studio.CadRevision, studio.CadWorkflowExport()));
            Check(report.Passed, "The new modify commands could not replay through the real Studio driver.");
        }));
    }
}
