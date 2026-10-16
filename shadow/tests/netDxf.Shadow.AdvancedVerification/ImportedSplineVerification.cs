using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class ImportedSplineVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("spline/imported explicit periodic basis retains authored geometry", ExplicitPeriodicBasis));
        tests.Add(("spline/explicit periodic basis survives mirrored nested inserts", MirroredPeriodicBasis));
        tests.Add(("spline/compact periodic basis still expands for projection", CompactPeriodicBasis));
        tests.Add(("spline/source creation and history retain an explicit periodic basis", SourceFactoryBasis));
    }

    private static Spline ExplicitSpline() => new(
        [new Vector3(0, 0, 0), new(0, 6, 0), new(7, 10, 0), new(14, 2, 0),
            new(8, -5, 0), new(2, -5, 0), new(0, 0, 0)],
        [1.0, 0.75, 1.5, 1.0, 0.8, 1.2, 1.0],
        [0.0, 0, 0, 0, 1, 1, 2, 3, 3, 3, 3], 3, true);

    private static void ExplicitPeriodicBasis()
    {
        var spline = ExplicitSpline();
        var document = new DxfDocument(); document.Entities.Add(spline);
        var snapshot = new ShadowSession(document).Current;
        var scene = new CadSceneBuilder().Build(snapshot);
        var curve = (NurbsCurve)scene.Occurrences.Single().Geometry.Contours.Single().Curves.Single();
        Require(curve.Controls.Length == spline.ControlPoints.Length, "Explicit controls were expanded a second time.");
        Require(curve.Knots.SequenceEqual(spline.Knots), "Authored knots changed during projection.");
        for (var i = 0; i <= 96; i++) Near(curve.At(i / 96.0), EvaluateBasis(spline, i / 96.0));
        var rendered = new RenderCompiler().Compile(snapshot);
        Require(rendered.Items.Length == 1 && !rendered.Bounds.IsEmpty, "The periodic spline disappeared from the render scene.");
        Require(!rendered.Diagnostics.Any(d => d.Code is "geometry.invalid" or "render.geometry"), "Projection reported invalid spline geometry.");
        Require(rendered.Items[0].Contours.SelectMany(c => c.Points).All(p => p.IsFinite), "Spline sampling produced nonfinite points.");
    }

    private static void MirroredPeriodicBasis()
    {
        var spline = ExplicitSpline();
        var inner = new Block("SplineDefinition"); inner.Entities.Add(spline);
        var outer = new Block("NestedDefinition");
        outer.Entities.Add(new Insert(inner, new Vector3(4, 2, 0)) { Scale = new Vector3(-2, 1.5, 1) });
        var document = new DxfDocument(); document.Entities.Add(new Insert(outer, new Vector3(30, 20, 0)));
        var scene = new CadSceneBuilder().Build(new ShadowSession(document).Current);
        var occurrence = scene.Occurrences.Single();
        Require(occurrence.InsertPath.Length == 2, "Nested source identity was lost.");
        var curve = occurrence.Geometry.Contours.Single().Curves.Single();
        for (var i = 0; i <= 96; i++)
        {
            var expected = EvaluateBasis(spline, i / 96.0);
            Near(curve.At(i / 96.0), new Vec3(34 - 2 * expected.X, 22 + 1.5 * expected.Y, expected.Z));
        }
    }

    private static void CompactPeriodicBasis()
    {
        var spline = new Spline([new Vector3(0, 0, 0), new(6, 0, 0), new(7, 5, 0), new(1, 6, 0)],
            [1.0, 1.0, 1.0, 1.0], 2, true);
        var document = new DxfDocument(); document.Entities.Add(spline);
        var curve = (NurbsCurve)new CadSceneBuilder().Build(new ShadowSession(document).Current)
            .Occurrences.Single().Geometry.Contours.Single().Curves.Single();
        Require(curve.Controls.Length == spline.ControlPoints.Length + spline.Degree, "Compact periodic controls no longer expand.");
        Near(curve.Start, curve.End);
    }

    private static void SourceFactoryBasis()
    {
        var authored = ExplicitSpline();
        var template = new DxfDocument(); template.Entities.Add(authored);
        var values = new ShadowSession(template).Current.DrawingEntities().Single();
        var document = new DxfDocument(); var source = new ShadowSession(document);
        var editor = new EditorSession(source);
        var properties = new[] { "ControlPoints", "Weights", "Knots", "Degree", "IsClosedPeriodic" }
            .ToDictionary(name => name, name => values[name].Value);
        editor.CreateEntity(typeof(Spline).FullName!, properties);
        void Check()
        {
            var actual = document.Entities.Splines.Single();
            Require(actual.HasExplicitPeriodicControlPoints && actual.IsClosedPeriodic,
                "Source construction changed the periodic representation.");
            Require(actual.Knots.SequenceEqual(authored.Knots) && actual.ControlPoints.SequenceEqual(authored.ControlPoints)
                && actual.Weights.SequenceEqual(authored.Weights), "Source construction changed the authored basis.");
            var curve = new CadSceneBuilder().Build(source.Current).Occurrences.Single().Geometry.Contours.Single().Curves.Single();
            for (var i = 0; i <= 32; i++) Near(curve.At(i / 32.0), EvaluateBasis(authored, i / 32.0));
        }
        Check();
        for (var i = 0; i < 3; i++)
        {
            editor.Undo(); Require(!document.Entities.All.Any(), "Undo retained the created spline.");
            editor.Redo(); Check();
        }
    }

    // Cox-de Boor basis summation is independent of the projector's de Boor evaluator.
    private static Vec3 EvaluateBasis(Spline spline, double fraction)
    {
        var controls = spline.ControlPoints; var knots = spline.Knots; var degree = spline.Degree;
        if (fraction == 1) return V(controls[^1]);
        var u = knots[degree] + fraction * (knots[controls.Length] - knots[degree]);
        double Basis(int i, int order)
        {
            if (order == 0) return knots[i] <= u && u < knots[i + 1] ? 1 : 0;
            var left = knots[i + order] - knots[i];
            var right = knots[i + order + 1] - knots[i + 1];
            return (left == 0 ? 0 : (u - knots[i]) / left * Basis(i, order - 1))
                + (right == 0 ? 0 : (knots[i + order + 1] - u) / right * Basis(i + 1, order - 1));
        }
        var numerator = Vec3.Zero; var denominator = 0.0;
        for (var i = 0; i < controls.Length; i++)
        {
            var factor = spline.Weights[i] * Basis(i, degree);
            numerator += V(controls[i]) * factor; denominator += factor;
        }
        return numerator / denominator;
    }

    private static Vec3 V(Vector3 value) => new(value.X, value.Y, value.Z);
    private static void Near(Vec3 actual, Vec3 expected) => Require(Vec3.Distance(actual, expected) < 1e-8, "Projected spline differs from its authored basis.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
