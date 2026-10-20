using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;

internal static class TransformReferenceVerification
{
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static Vec3 V(Vector3 value) => new(value.X, value.Y, value.Z);
    private static void Require(bool condition, string message = "Reference transform regression.")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Near(Vec3 actual, Vec3 expected) => Require(Vec3.Distance(actual, expected) < 1e-7, $"{actual} != {expected}");
    private static void Reject(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected atomic transform rejection.");
    }
    private static byte[] Save(DxfDocument document, bool binary)
    { using var stream = new MemoryStream(); Require(document.Save(stream, binary)); return stream.ToArray(); }

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("drafting/reference scalar angles and lengths transform about the chosen base", () =>
        {
            var document = new DxfDocument();
            var line = new Line(new Vector3(10, 20, 3), new Vector3(15, 20 + 5 * Math.Sqrt(3), 3));
            document.Entities.Add(line); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var planner = new DraftingPlanner(); var baseline = source.Current;
            planner.Plan(source.Current, new("ROTATE", [new(10, 20, 3)], [Id(line)],
                Transform: new(Reference: new(60), Target: new(90)))).Execute(editor);
            Near(V(line.StartPoint), new(10, 20, 3)); Near(V(line.EndPoint), new(10, 30, 3));
            Require(ReferenceEquals(document.GetObjectByHandle(line.Handle), line) && editor.State.UndoCount == 1);
            planner.Plan(source.Current, new("SCALE", [new(10, 20, 3)], [Id(line)],
                Transform: new(Reference: new(10), Target: new(25)))).Execute(editor);
            Near(V(line.EndPoint), new(10, 45, 3));
            editor.Undo(); Near(V(line.EndPoint), new(10, 30, 3));
            editor.Undo(); Near(V(line.EndPoint), baseline.Read(Id(line))["EndPoint"].Vector());
            editor.Redo(); editor.Redo(); Near(V(line.EndPoint), new(10, 45, 3));
        }));
        tests.Add(("drafting/reference point pairs are independent of the base and scale uses 3D distance", () =>
        {
            var document = new DxfDocument(); var line = new Line(new Vector3(10, 20, 3), new Vector3(20, 20, 3));
            document.Entities.Add(line); var source = new ShadowSession(document); var editor = new EditorSession(source);
            var planner = new DraftingPlanner();
            planner.Plan(source.Current, new("ROTATE", [new(10, 20, 3)], [Id(line)], Transform: new(
                Reference: new(Points: [new(100, 100, 9), new(110, 100, 30)]),
                Target: new(Points: [new(-50, -50, 7), new(-50, -40, 7)])))).Execute(editor);
            Near(V(line.EndPoint), new(10, 30, 3));
            planner.Plan(source.Current, new("SCALE", [new(10, 20, 3)], [Id(line)], Transform: new(
                Reference: new(Points: [new(0, 0, 0), new(0, 3, 4)]),
                Target: new(Points: [new(80, 90, 20), new(80, 90, 30)])))).Execute(editor);
            Near(V(line.EndPoint), new(10, 40, 3));
        }));
        tests.Add(("drafting/reference copy keeps block resources and independent transformed attributes through history and DXF", () =>
        {
            foreach (var operation in new[] { "ROTATE", "SCALE", "COPY" })
            {
                var document = new DxfDocument(); var block = new Block("Transform block");
                block.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0)));
                block.AttributeDefinitions.Add(new AttributeDefinition("TAG") { Position = new(2, 3, 0), Height = 2 });
                var insert = new Insert(block, new Vector3(20, 30, 0)) { Scale = new(2, 3, 1), Rotation = 15 };
                insert.Attributes[0].Value = "Custom text"; document.Entities.Add(insert);
                var originalAttribute = insert.Attributes[0]; var originalPosition = V(originalAttribute.Position);
                var source = new ShadowSession(document); var editor = new EditorSession(source); var before = source.Current;
                var options = operation == "ROTATE" ? new DraftingTransformOptions(true, new(10), new(100)) :
                    new DraftingTransformOptions(true, new(5), new(10));
                var plan = new DraftingPlanner().Plan(before, new(operation,
                    operation == "COPY" ? [new(20, 30), new(25, 35)] : [new(20, 30)], [Id(insert)],
                    Transform: operation == "COPY" ? null : options));
                Require(ReferenceEquals(before, source.Current) && document.Entities.Inserts.Count() == 1);
                plan.Execute(editor);
                var copy = document.Entities.Inserts.Single(item => !ReferenceEquals(item, insert));
                var transform = operation == "COPY" ? Affine3.Move(new(5, 5)) : Affine3.Move(new(20, 30)) *
                    (operation == "ROTATE" ? Affine3.RotateZ(Math.PI / 2) : Affine3.Scale(new(2, 2, 2))) * Affine3.Move(new(-20, -30));
                Require(plan.Created.Length == 1 && !plan.Created.Contains(Id(insert)) && editor.State.UndoCount == 1);
                Require(ReferenceEquals(copy.Block, block) && ReferenceEquals(copy.Attributes[0].Definition, originalAttribute.Definition));
                Require(!ReferenceEquals(copy.Attributes[0], originalAttribute) && copy.Attributes[0].Value?.ToString() == "Custom text");
                Near(V(copy.Attributes[0].Position), transform.Point(originalPosition)); Near(V(originalAttribute.Position), originalPosition);
                Require(insert.Scale == new Vector3(2, 3, 1) && insert.Rotation == 15);
                editor.Undo(); Require(document.Entities.Inserts.Single() == insert);
                editor.Redo();
                // Native adoption allocates a fresh DXF handle on reattachment, as
                // with COPY. History retains the source instance and shadow identity.
                Require(ReferenceEquals(document.Entities.Inserts.Single(item => item != insert), copy) &&
                    source.Current.Read(plan.Created[0])["Handle"].Text == copy.Handle);
                var copiedHandle = copy.Handle;
                foreach (var binary in new[] { false, true })
                {
                    using var stream = new MemoryStream(Save(document, binary)); var loaded = DxfDocument.Load(stream)!;
                    Require(loaded.Entities.Inserts.Count() == 2 && loaded.Blocks["Transform block"].AttributeDefinitions.Count == 1);
                    var savedCopy = (Insert)loaded.GetObjectByHandle(copiedHandle);
                    Near(V(savedCopy.Attributes[0].Position), transform.Point(originalPosition));
                    Require(savedCopy.Attributes[0].Value?.ToString() == "Custom text");
                }
            }
        }));
        tests.Add(("drafting/reference copies preserve tilted conics splines and owned polyline geometry", () =>
        {
            foreach (var operation in new[] { "ROTATE", "SCALE" })
            {
                var document = new DxfDocument(); var normal = new Vector3(1, 2, 3);
                EntityObject[] entities = [
                    new Line(new Vector3(1, 2, 3), new Vector3(8, 4, 6)),
                    new Circle(new Vector3(2, 3, 4), 5) { Normal = normal },
                    new Arc(new Vector3(3, 4, 5), 4, 20, 250) { Normal = normal },
                    new Ellipse(new Vector3(6, 7, 8), 12, 6) { Normal = normal, Rotation = 30 },
                    new Polyline2D([new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 8)]) { Normal = normal, Elevation = 4 },
                    new Polyline3D([new Vector3(0, 0, 3), new Vector3(4, 5, 6), new Vector3(10, 1, 2)]),
                    new Spline([new Vector3(0, 0, 0), new Vector3(4, 5, 2), new Vector3(9, 1, 3)], [1.0, .7, 1.0], (short)2)
                ];
                document.Entities.Add(entities); var source = new ShadowSession(document); var editor = new EditorSession(source);
                var before = source.Current; var old = new CadSceneBuilder().Build(before);
                var plan = new DraftingPlanner().Plan(before, new(operation, [new(2, 3, 4)], entities.Select(Id).ToImmutableArray(),
                    Transform: new(true, new(operation == "ROTATE" ? 25 : 5), new(operation == "ROTATE" ? 115 : 10))));
                plan.Execute(editor); var after = new CadSceneBuilder().Build(source.Current);
                var matrix = Affine3.Move(new(2, 3, 4)) *
                    (operation == "ROTATE" ? Affine3.RotateZ(Math.PI / 2) : Affine3.Scale(new(2, 2, 2))) * Affine3.Move(new(-2, -3, -4));
                for (var i = 0; i < entities.Length; i++)
                {
                    var original = old.Occurrences.Single(item => item.Entity == Id(entities[i])).Geometry.Contours.SelectMany(c => c.Curves).ToArray();
                    var copied = after.Occurrences.Single(item => item.Entity == plan.Created[i]).Geometry.Contours.SelectMany(c => c.Curves).ToArray();
                    Require(original.Length == copied.Length && original.Length > 0);
                    for (var c = 0; c < original.Length; c++)
                        for (var sample = 0; sample <= 4; sample++)
                            Near(copied[c].At(sample / 4.0), matrix.Point(original[c].At(sample / 4.0)));
                    Require(ShadowGraphPatch.Equivalent(before.Objects[Id(entities[i])], source.Current.Objects[Id(entities[i])]));
                }
                Require(editor.State.UndoCount == 1 && plan.Created.Length == entities.Length);
                editor.Undo(); Require(document.Entities.All.Count() == entities.Length);
                editor.Redo(); Require(document.Entities.All.Count() == entities.Length * 2);
            }
        }));
        tests.Add(("drafting/full turn and unit scale are exact no-ops while Copy creates coincident independent roots", () =>
        {
            foreach (var (name, value) in new[] { ("ROTATE", 0.0), ("ROTATE", 360.0), ("ROTATE", -720.0), ("SCALE", 1.0) })
            {
                var document = new DxfDocument(); var line = new Line(new Vector3(1, 2, 3), new Vector3(4, 5, 6));
                document.Entities.Add(line); var source = new ShadowSession(document); var editor = new EditorSession(source);
                var before = source.Current; var planner = new DraftingPlanner();
                planner.Plan(before, new(name, [new(1e10, -1e10, 5)], [Id(line)], Value: value)).Execute(editor);
                Require(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0);
                var plan = planner.Plan(before, new(name, [new(1e10, -1e10, 5)], [Id(line)], Value: value, Transform: new(Copy: true)));
                plan.Execute(editor); Require(editor.State.UndoCount == 1 && plan.Created.Length == 1);
                var copied = document.Entities.Lines.Single(item => item != line);
                Require(copied.StartPoint == line.StartPoint && copied.EndPoint == line.EndPoint);
            }
        }));
        tests.Add(("drafting/invalid reference receipts locked unsupported and budgeted copies refuse before publication", () =>
        {
            var document = new DxfDocument(); var line = new Line(Vector2.Zero, new Vector2(10, 0));
            var dimension = new AlignedDimension(Vector2.Zero, new Vector2(10, 0), 3);
            document.Entities.Add(line); document.Entities.Add(dimension);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var before = source.Current;
            var planner = new DraftingPlanner(); var identity = Id(line); var seed = document.DrawingVariables.HandleSeed;
            DraftingTransformOptions[] bad = [
                new(Reference: new(0), Target: new(1)), new(Reference: new(-1), Target: new(1)),
                new(Reference: new(1), Target: new(0)), new(Reference: new(1), Target: new(-1)),
                new(Reference: new(1)), new(Target: new(1)), new(Reference: new(), Target: new(1)),
                new(Reference: new(1, [Vec3.Zero, Vec3.UnitX]), Target: new(2)),
                new(Reference: new(Points: [Vec3.Zero, Vec3.Zero]), Target: new(1)),
                new(Reference: new(double.NaN), Target: new(1)), new(Reference: new(1e-9), Target: new(1e12))
            ];
            foreach (var options in bad)
                Reject(() => planner.Plan(before, new("SCALE", [Vec3.Zero], [identity], Transform: options)).Execute(editor));
            Reject(() => planner.Plan(before, new("ROTATE", [Vec3.Zero], [identity], Transform: new(
                Reference: new(Points: [Vec3.Zero, Vec3.UnitZ]), Target: new(90)))));
            Reject(() => planner.Plan(before, new("MOVE", [Vec3.Zero, Vec3.UnitX], [identity], Transform: new(true))));
            Reject(() => planner.Plan(before, new("ROTATE", [Vec3.Zero], [identity, Id(dimension)], Value: 30, Transform: new(true))).Execute(editor));
            Reject(() => new DraftingPlanner(new(MaximumClonedObjects: 1)).Plan(before,
                new("SCALE", [Vec3.Zero], [identity, Id(dimension)], Value: 2, Transform: new(true))));
            Reject(() => planner.Plan(before, new("ROTATE", [Vec3.Zero], [identity], Value: 30, Transform: new(true)),
                cancellationToken: new(true)));
            Require(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0 && document.DrawingVariables.HandleSeed == seed);
            document.Layers["0"].IsLocked = true; source.Refresh(); editor.ResetHistory(); before = source.Current;
            Reject(() => planner.Plan(before, new("ROTATE", [Vec3.Zero], [identity], Value: 45, Transform: new(true))));
            Require(ReferenceEquals(before, source.Current) && editor.State.UndoCount == 0);
        }));
    }
}
