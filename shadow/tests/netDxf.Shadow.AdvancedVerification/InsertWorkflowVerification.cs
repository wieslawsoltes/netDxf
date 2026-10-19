using System.Collections.Immutable;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Tables;
using netDxf.Units;

internal static class InsertWorkflowVerification
{
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vector3 actual, Vector3 expected, string message) => Check(Vector3.Distance(actual, expected) < 1e-7, message);
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
    private static ObjectId BlockId(ShadowSnapshot snapshot, string name) => snapshot.Document["Blocks"].Items.Single(b => b["Name"].Text == name).Id!.Value;

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("insert-workflow/signed XYZ scale and alternate base honor native drawing units", () =>
        {
            var document = new DxfDocument(); document.DrawingVariables.InsUnits = DrawingUnits.Centimeters;
            var block = new Block("Part") { Origin = new(3, 4, 0) }; block.Record.Units = DrawingUnits.Millimeters;
            block.Entities.Add(new Line(new Vector3(3, 4, 0), new Vector3(13, 4, 0))); document.Blocks.Add(block);
            var source = new ShadowSession(document); var editor = new EditorSession(source); var planner = new DraftingPlanner();
            var blockId = BlockId(source.Current, "Part"); var before = source.Current;
            var plan = planner.Plan(before, new("INSERT", [new(20, 50, 2)], Reference: blockId,
                Scale: new(2, -3, 4), Rotation: 90, BasePoint: new(5, 6, 0)));
            Check(ReferenceEquals(before, source.Current) && !document.Entities.Inserts.Any(), "Insertion planning changed source.");
            plan.Execute(editor); var insert = document.Entities.Inserts.Single();
            var matrix = insert.GetTransformation();
            Near(insert.Position + matrix * (new Vector3(5, 6, 0) - block.Origin), new(20, 50, 2), "Alternate local base did not land on the selected point.");
            var projected = new CadSceneBuilder().Build(source.Current).Occurrences.Single().Geometry.Contours.Single().Curves.Single();
            var expectedEnd = insert.Position + matrix * (new Vector3(13, 4, 0) - block.Origin);
            Near(new(projected.End.X, projected.End.Y, projected.End.Z), expectedEnd, "Rendered INSERT units disagree with native placement.");
            Check(insert.Scale == new Vector3(2, -3, 4) && insert.Rotation == 90 && block.Origin == new Vector3(3, 4, 0) && editor.State.UndoCount == 1,
                "Insertion options altered definition or lost signed scaling.");
            editor.Undo(); Check(!document.Entities.Inserts.Any() && document.Blocks["Part"] == block, "Insertion undo removed the definition.");
            editor.Redo(); Check(document.Entities.Inserts.Single() == insert, "Insertion redo lost native identity.");
            using var stream = new MemoryStream(); Check(document.Save(stream), "Insertion DXF save failed."); stream.Position = 0;
            var loaded = DxfDocument.Load(stream)!; var reloaded = loaded.Entities.Inserts.Single();
            Near(reloaded.Position, insert.Position, "Insertion position failed DXF roundtrip.");
            Check(reloaded.Scale == insert.Scale && reloaded.Rotation == 90 && reloaded.Block.Origin == block.Origin, "Insertion options failed DXF roundtrip.");
        }));
        tests.Add(("insert-workflow/default attributes receive complete transform and document units", () =>
        {
            var document = new DxfDocument(); document.DrawingVariables.InsUnits = DrawingUnits.Millimeters;
            var block = new Block("Valve") { Origin = new(4, 5, 0) }; block.Record.Units = DrawingUnits.Inches;
            block.Entities.Add(new Circle(Vector3.Zero, 2));
            var definition = new AttributeDefinition("TAG") { Position = new(10, 8, 0), Height = 2, Value = "V-001", Rotation = 10 };
            block.AttributeDefinitions.Add(definition); document.Blocks.Add(block);
            var source = new ShadowSession(document); var editor = new EditorSession(source);
            new DraftingPlanner().Plan(source.Current, new("INSERT", [new(100, 200, 0)], Reference: BlockId(source.Current, "Valve"),
                Scale: new(2, 3, 1), Rotation: 30)).Execute(editor);
            var insert = document.Entities.Inserts.Single(); var attribute = insert.Attributes.Single();
            var expected = new netDxf.Entities.Attribute(definition);
            var matrix = insert.GetTransformation(); expected.TransformBy(matrix, insert.Position - matrix * block.Origin);
            Near(attribute.Position, expected.Position, "Default attribute position ignored INSERT options or units.");
            Check(attribute.Value == "V-001" && Math.Abs(attribute.Height - expected.Height) < 1e-8 && Math.Abs(attribute.Rotation - expected.Rotation) < 1e-8,
                "Default attribute text geometry or value was changed incorrectly.");
            var unchanged = source.Current;
            Throws<NotSupportedException>(() => new DraftingPlanner().Plan(source.Current,
                new("EXPLODE", Entities: [source.Current.DrawingEntities().Single().Id!.Value])));
            Check(ReferenceEquals(source.Current, unchanged) && attribute.Value == "V-001", "Attributed EXPLODE refusal changed source or values.");
            editor.Undo(); editor.Redo();
            Near(document.Entities.Inserts.Single().Attributes.Single().Position, expected.Position, "Attribute geometry failed insertion undo/redo.");
        }));
        tests.Add(("insert-workflow/explode keeps definitions and materializes inherited properties", () =>
        {
            var document = new DxfDocument(); var layer = new Layer("Equipment"); document.Layers.Add(layer);
            var block = new Block("Assembly") { Origin = new(1, 2, 0) };
            block.Entities.Add(new Line(new Vector3(1, 2, 0), new Vector3(11, 2, 0)) { Color = AciColor.ByBlock });
            block.Entities.Add(new Circle(new Vector3(6, 7, 0), 2)); document.Blocks.Add(block);
            var insert = new Insert(block, new Vector3(30, 40, 0)) { Scale = new(2, 2, 2), Rotation = 90, Layer = layer, Color = AciColor.Red };
            document.Entities.Add(insert);
            var expected = insert.Explode(); var expectedLine = expected.OfType<Line>().Single(); var expectedCircle = expected.OfType<Circle>().Single();
            var source = new ShadowSession(document); var editor = new EditorSession(source); var planner = new DraftingPlanner(); var before = source.Current;
            var insertId = before.DrawingEntities().Single().Id!.Value;
            planner.Plan(before, new("EXPLODE", Entities: [insertId])).Execute(editor);
            var line = document.Entities.Lines.Single(); var circle = document.Entities.Circles.Single();
            Near(line.StartPoint, expectedLine.StartPoint, "Exploded line start disagrees with native INSERT transform.");
            Near(line.EndPoint, expectedLine.EndPoint, "Exploded line end disagrees with native INSERT transform.");
            Near(circle.Center, expectedCircle.Center, "Exploded circle center disagrees with native INSERT transform.");
            Check(circle.Radius == expectedCircle.Radius && line.Layer == layer && line.Color.Index == insert.Color.Index && block.Entities.Count == 2,
                "EXPLODE lost circle radius, inherited properties, or reusable definition geometry.");
            Check(!document.Entities.Inserts.Any() && editor.State.UndoCount == 1, "EXPLODE did not atomically replace the reference.");
            editor.Undo(); Check(document.Entities.Inserts.Single() == insert && !document.Entities.Lines.Any(), "EXPLODE undo did not restore original native reference.");
            editor.Redo(); Check(document.Entities.Lines.Count() == 1 && !document.Entities.Inserts.Any(), "EXPLODE redo failed.");
            using var stream = new MemoryStream(); Check(document.Save(stream), "Exploded drawing save failed."); stream.Position = 0;
            var loaded = DxfDocument.Load(stream)!;
            Check(loaded.Entities.Lines.Count() == 1 && loaded.Blocks["Assembly"].Entities.Count == 2, "Exploded drawing roundtrip changed definition reuse.");
        }));
        tests.Add(("insert-workflow/unsupported explode and invalid scales leave the whole source unchanged", () =>
        {
            var document = new DxfDocument(); var block = new Block("Round"); block.Entities.Add(new Circle(Vector3.Zero, 2)); document.Blocks.Add(block);
            var insert = new Insert(block) { Scale = new(2, 3, 1) }; document.Entities.Add(insert);
            var source = new ShadowSession(document); var before = source.Current; var planner = new DraftingPlanner();
            var id = before.DrawingEntities().Single().Id!.Value; var definition = BlockId(before, "Round");
            Throws<NotSupportedException>(() => planner.Plan(before, new("EXPLODE", Entities: [id])));
            foreach (var scale in new[] { new Vec3(0, 1, 1), new Vec3(double.NaN, 1, 1), new Vec3(1e13, 1, 1) })
                Throws<ArgumentException>(() => planner.Plan(before, new("INSERT", [Vec3.Zero], Reference: definition, Scale: scale)));
            Check(ReferenceEquals(source.Current, before) && document.Entities.Inserts.Single() == insert && block.Entities.Count == 1,
                "Unsupported operations changed the source.");
        }));
        tests.Add(("insert-workflow/nested block explode remains one level and shares inner definition", () =>
        {
            var document = new DxfDocument(); var inner = new Block("Inner"); inner.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
            var outer = new Block("Outer"); outer.Entities.Add(new Insert(inner, new Vector3(3, 0, 0)) { Rotation = 20 }); document.Blocks.Add(outer);
            var reference = new Insert(outer, new Vector3(20, 30, 0)) { Scale = new(2, 2, 2), Rotation = 40 }; document.Entities.Add(reference);
            var source = new ShadowSession(document); var editor = new EditorSession(source);
            new DraftingPlanner().Plan(source.Current, new("EXPLODE", Entities: [source.Current.DrawingEntities().Single().Id!.Value])).Execute(editor);
            var nested = document.Entities.Inserts.Single();
            Check(nested.Block == inner && outer.Entities.OfType<Insert>().Single().Block == inner && Math.Abs(nested.Rotation - 60) < 1e-9,
                "Nested EXPLODE flattened definitions or changed shared references.");
        }));
        tests.Add(("insert-workflow/native unit conversion and scene cache follow INSUNITS", () =>
        {
            foreach (var from in Enum.GetValues<DrawingUnits>()) foreach (var to in Enum.GetValues<DrawingUnits>())
            {
                var expected = UnitHelper.ConversionFactor(from, to); var actual = DrawingUnitConversion.Factor(from.ToString(), to.ToString());
                Check(Math.Abs(actual - expected) <= Math.Abs(expected) * 1e-12, $"Unit conversion disagrees with native {from} to {to}.");
            }
            var document = new DxfDocument(); document.DrawingVariables.InsUnits = DrawingUnits.Centimeters;
            var block = new Block("Units"); block.Record.Units = DrawingUnits.Millimeters;
            block.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0))); document.Entities.Add(new Insert(block));
            var source = new ShadowSession(document); var builder = new CadSceneBuilder();
            double End() => builder.Build(source.Current).Occurrences.Single().Geometry.Contours.Single().Curves.Single().End.X;
            Check(Math.Abs(End() - 1) < 1e-10, "Initial layout insertion did not use drawing units.");
            source.MutateSource(d => d.DrawingVariables.InsUnits = DrawingUnits.Millimeters);
            Check(Math.Abs(End() - 10) < 1e-10, "INSUNITS change did not invalidate cached INSERT geometry.");
            source.MutateSource(d => { d.DrawingVariables.InsUnits = DrawingUnits.Unitless; block.Record.Units = DrawingUnits.Inches; });
            Check(Math.Abs(End() - 10) < 1e-10, "Unitless destination incorrectly applied physical conversion.");
        }));
        tests.Add(("insert-workflow/block authoring constraints and cancellation reject before source adoption", () =>
        {
            var document = new DxfDocument(); var block = new Block("Protected"); block.Entities.Add(new Line(Vector3.Zero, Vector3.UnitX));
            block.Record.AllowExploding = false; block.Record.ScaleUniformly = true; document.Entities.Add(new Insert(block));
            var source = new ShadowSession(document); var before = source.Current; var planner = new DraftingPlanner();
            Throws<InvalidOperationException>(() => planner.Plan(before, new("EXPLODE", Entities: [before.DrawingEntities().Single().Id!.Value])));
            Throws<ArgumentException>(() => planner.Plan(before, new("INSERT", [Vec3.Zero], Reference: BlockId(before, "Protected"), Scale: new(2, 3, 1))));
            Throws<OperationCanceledException>(() => planner.Plan(before, new("INSERT", [Vec3.Zero], Reference: BlockId(before, "Protected")), cancellationToken: new(true)));
            Check(ReferenceEquals(source.Current, before) && document.Entities.Inserts.Count() == 1, "Rejected block operation changed source.");
        }));
    }
}
