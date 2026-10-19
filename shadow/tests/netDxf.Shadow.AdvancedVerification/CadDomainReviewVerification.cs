using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Drafting;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Units;

internal static class CadDomainReviewVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("cad-review/nested block explode preserves geometry across distinct insertion units", () =>
        {
            foreach (var units in new (DrawingUnits Inner, DrawingUnits Outer, DrawingUnits Drawing)[]
            {
                (Inner: DrawingUnits.Millimeters, Outer: DrawingUnits.Centimeters, Drawing: DrawingUnits.Meters),
                (DrawingUnits.Unitless, DrawingUnits.Centimeters, DrawingUnits.Meters),
                (DrawingUnits.Millimeters, DrawingUnits.Unitless, DrawingUnits.Meters),
                (DrawingUnits.Millimeters, DrawingUnits.Centimeters, DrawingUnits.Unitless),
                (DrawingUnits.Millimeters, DrawingUnits.Centimeters, DrawingUnits.Millimeters)
            })
            {
                var document = new DxfDocument(); document.DrawingVariables.InsUnits = units.Drawing;
                var inner = new Block("Inner") { Origin = new(2, 3, 0) }; inner.Record.Units = units.Inner;
                inner.Entities.Add(new Line(new Vector3(2, 3, 0), new Vector3(1002, 3, 0)));
                var outer = new Block("Outer") { Origin = new(4, 5, 0) }; outer.Record.Units = units.Outer;
                outer.Entities.Add(new Insert(inner, new Vector3(20, 30, 0)) { Scale = new(2), Rotation = 15 });
                document.Entities.Add(new Insert(outer, new Vector3(5, 10, 0)) { Scale = new(3), Rotation = 45 });
                var source = new ShadowSession(document); var editor = new EditorSession(source);
                (Vec3 Start, Vec3 End) Endpoints()
                {
                    var curve = new CadSceneBuilder().Build(source.Current).Occurrences.Single().Geometry.Contours.Single().Curves.Single();
                    return (curve.Start, curve.End);
                }
                var before = Endpoints();
                new DraftingPlanner().Plan(source.Current, new("EXPLODE", Entities: [source.Current.DrawingEntities().Single().Id!.Value])).Execute(editor);
                var after = Endpoints();
                if (Vec3.Distance(before.Start, after.Start) > 1e-8 || Vec3.Distance(before.End, after.End) > 1e-8)
                    throw new InvalidOperationException($"Nested INSERT changed geometry: {before} -> {after}.");
                if (document.Entities.Inserts.Single().Block != inner) throw new InvalidOperationException("Nested shared definition was lost.");
                editor.Undo(); editor.Redo();
                var replayed = Endpoints();
                if (Vec3.Distance(before.Start, replayed.Start) > 1e-8 || Vec3.Distance(before.End, replayed.End) > 1e-8)
                    throw new InvalidOperationException("Nested INSERT geometry changed during undo/redo.");
            }
        }));
    }
}
