using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;

internal static class LinetypeComponentScaleVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-component-scale/" + name, run));
        CadStyle Style(double scale) => CadStyle.Default with
        {
            Dashes = [scale, -scale],
            Linetype = new([new(1), new(-1, new(CadLinetypeDecorationKind.Text, "A", ""))], scale, Vec3.UnitZ)
        };
        foreach (var source in Enum.GetValues<CadStyleSource>())
            Test(source.ToString(), () =>
            {
                var component = new CadComponentStyle(Style(1), Dashes: source, DashScale: 3);
                var result = component.Resolve(Style(2), Style(4), 2);
                var expected = source == CadStyleSource.Layer ? 12 : 6;
                Near(result.Dashes[0], expected); Near(result.Linetype!.Scale, expected);
            });
        Test("regenerated dimension components receive the consumer scale once", () =>
        {
            var document = new DxfDocument();
            var dimension = new AlignedDimension(Vector2.Zero, new Vector2(20, 0), 5, new DimensionStyle("Linetype scale test"));
            dimension.Style.DimLineLinetype = Linetype.Dashed;
            dimension.Style.ExtLine1Linetype = Linetype.Dashed;
            dimension.Style.ExtLine2Linetype = Linetype.Dashed;
            document.Entities.Add(dimension); document.DrawingVariables.LtScale = 3;
            var snapshot = new ShadowSession(document).Current;
            var builder = new CadSceneBuilder([new DimensionEntityProjector()]);
            var before = builder.Build(snapshot);
            var after = builder.Build(snapshot, new() { LinetypeScale = 2 });
            Check(!after.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            var source = before.Occurrences.Where(i => !i.Style.Dashes.IsDefaultOrEmpty).ToDictionary(i => i.Key);
            Check(source.Count > 0);
            foreach (var result in after.Occurrences.Where(i => !i.Style.Dashes.IsDefaultOrEmpty))
                Near(result.Style.Dashes[0], source[result.Key].Style.Dashes[0] * 2);
        });
        Test("original component resolver signature stays available", () =>
        {
            Check(typeof(CadComponentStyle).GetMethod("Resolve", [typeof(CadStyle), typeof(CadStyle)]) is not null);
            Near(new CadComponentStyle(Style(1)).Resolve(Style(2), Style(4)).Dashes[0], 1);
        });
    }
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-9);
    private static void Check(bool value) { if (!value) throw new Exception("Component linetype scale assertion failed."); }
}
