using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterSplineCloneStateTests()
    {
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (int kind in Enumerable.Range(0, 3))
                    foreach (int operation in Enumerable.Range(0, 3))
                    {
                        DxfVersion v = version; bool b = binary; int k = kind, o = operation;
                        Run($"spline/clone-state/{v}/{b}/{k}/{o}", () => SplineCloneState(v, b, k, o));
                    }
        foreach (int kind in Enumerable.Range(0, 3))
        {
            int k = kind;
            Run($"spline/clone-state/isolation/{k}", () => SplineCloneIsolation(k));
        }
    }

    private static Spline CloneStateSpline(int kind)
    {
        var points = new[] { new Vector3(1, 2, 3), new Vector3(4, 7, 8), new Vector3(9, 5, 2), new Vector3(12, 3, 7) };
        Spline spline = kind == 0 ? new Spline(points) : new Spline(points, new[] { 1.0, 0.5, 0.75, 1.0 }, (short)2, kind == 2);
        // Fit-created splines can carry subsequently edited control geometry.
        // Cloning is an identity operation, not a request to run the fitter again.
        spline.ControlPoints[1] += new Vector3(0.125, -0.25, 0.5);
        spline.Weights[1] = 0.875;
        for (int i = 0; i < spline.Knots.Length; i++) spline.Knots[i] = spline.Knots[i] * 3.0 + 2.0;
        spline.KnotParameterization = SplineKnotParameterization.FitCustom;
        spline.KnotTolerance = 0.00125;
        spline.CtrlPointTolerance = 0.0025;
        spline.FitTolerance = 0.00375;
        spline.StartTangent = new Vector3(2, -3, 5);
        spline.EndTangent = Vector3.Zero;
        spline.Normal = new Vector3(1, 2, 3);
        spline.IsVisible = false;
        spline.Layer = new Layer("SplineCloneLayer") { Color = new AciColor(4) };
        spline.Color = new AciColor(12, 34, 56);
        spline.Lineweight = Lineweight.W25;
        spline.LinetypeScale = 2.25;
        spline.Transparency = new Transparency(25);
        var data = new XData(new ApplicationRegistry("SPLINE_CLONE_STATE"));
        data.XDataRecord.Add(new XDataRecord(XDataCode.String, "retain geometry; do not refit"));
        spline.XData.Add(data);
        return spline;
    }

    private static void SameSplineCloneState(Spline expected, Spline actual, bool normal = true)
    {
        Check(expected.ControlPoints.SequenceEqual(actual.ControlPoints), "Clone regenerated stored control geometry.");
        Check(expected.FitPoints.SequenceEqual(actual.FitPoints), "Clone changed fit metadata.");
        Check(expected.Knots.SequenceEqual(actual.Knots), "Clone regenerated knot parameterization.");
        Check(expected.Weights.SequenceEqual(actual.Weights), "Clone changed rational weights.");
        Equal(expected.CreationMethod, actual.CreationMethod, "Creation method");
        Equal(expected.IsClosedPeriodic, actual.IsClosedPeriodic, "Periodic state");
        Equal(expected.Degree, actual.Degree, "Degree");
        Equal(expected.KnotParameterization, actual.KnotParameterization, "Knot parameterization");
        SameDoubleBits(expected.KnotTolerance, actual.KnotTolerance, "Knot tolerance");
        SameDoubleBits(expected.CtrlPointTolerance, actual.CtrlPointTolerance, "Control tolerance");
        SameDoubleBits(expected.FitTolerance, actual.FitTolerance, "Fit tolerance");
        Equal(expected.IsVisible, actual.IsVisible, "Visibility");
        if (normal) AssertSplineTangent(expected.Normal, actual.Normal, "Normal");
        Equal(expected.StartTangent, actual.StartTangent, "Start tangent");
        Equal(expected.EndTangent, actual.EndTangent, "Explicit zero end tangent");
        Equal(expected.Layer.Name, actual.Layer.Name, "Layer");
        Equal(AciColor.ToTrueColor(expected.Color), AciColor.ToTrueColor(actual.Color), "Color");
        Equal(expected.LinetypeScale, actual.LinetypeScale, "Linetype scale");
        Equal(expected.Lineweight, actual.Lineweight, "Lineweight");
        Equal(expected.Transparency.Value, actual.Transparency.Value, "Transparency");
        Equal("retain geometry; do not refit", (string)actual.XData["SPLINE_CLONE_STATE"].XDataRecord.Single().Value, "XData");
    }

    private static void SplineCloneState(DxfVersion version, bool binary, int kind, int operation)
    {
        Spline original = CloneStateSpline(kind), clone;
        if (operation == 0) clone = (Spline)original.Clone();
        else
        {
            var inner = new Block("InnerClone"); inner.Entities.Add(original);
            var insert = new Insert(inner);
            if (operation == 1)
            {
                var outer = new Block("OuterClone"); outer.Entities.Add(insert);
                var nested = (Insert)new Insert(outer).Clone();
                clone = nested.Block.Entities.OfType<Insert>().Single().Block.Entities.OfType<Spline>().Single();
            }
            else clone = insert.Explode().OfType<Spline>().Single();
        }
        SameSplineCloneState(original, clone);
        Check(clone.Handle == null, "Clone copied an attached object identity.");
        // Nested clone remains owned by its cloned block; detach via another clone for serialization.
        var doc = new DxfDocument(version); doc.Entities.Add(clone.Owner == null ? clone : (Spline)clone.Clone());
        // Use a cloned source too, because an original may be attached to the nested block above.
        doc.Entities.Add((Spline)original.Clone());
        for (int cycle = 0; cycle < 2; cycle++)
        {
            using var output = new MemoryStream(); Check(doc.Save(output, cycle == 0 ? binary : !binary), "Cloned spline save failed.");
            if (cycle == 0 && operation == 0)
                File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-clone-state-{version}-{binary}-{kind}.dxf"), output.ToArray());
            output.Position = 0; doc = DxfDocument.Load(output) ?? throw new InvalidOperationException("Cloned spline cannot reload.");
            foreach (Spline current in doc.Entities.Splines)
            {
                // Existing spline normal serialization and old-profile true-color downgrade are independent.
                Check(original.ControlPoints.SequenceEqual(current.ControlPoints), "Wire control geometry changed.");
                Check(original.FitPoints.SequenceEqual(current.FitPoints), "Wire fit metadata changed.");
                Check(original.Knots.SequenceEqual(current.Knots) && original.Weights.SequenceEqual(current.Weights), "Wire spline parameter data changed.");
                SameDoubleBits(original.KnotTolerance, current.KnotTolerance, "Wire knot tolerance");
                SameDoubleBits(original.CtrlPointTolerance, current.CtrlPointTolerance, "Wire control tolerance");
                SameDoubleBits(original.FitTolerance, current.FitTolerance, "Wire fit tolerance");
                Equal(false, current.IsVisible, "Wire visibility");
            }
        }
    }

    private static void SplineCloneIsolation(int kind)
    {
        Spline original = CloneStateSpline(kind), copy = (Spline)original.Clone();
        SameSplineCloneState(original, copy);
        Check(!ReferenceEquals(original.ControlPoints, copy.ControlPoints) && !ReferenceEquals(original.Knots, copy.Knots) &&
              !ReferenceEquals(original.Weights, copy.Weights) && !ReferenceEquals(original.FitPoints, copy.FitPoints), "Clone shares spline arrays.");
        Vector3 control = original.ControlPoints[0]; double knot = original.Knots[0], weight = original.Weights[0];
        copy.ControlPoints[0] = new Vector3(-10, -20, -30); copy.Knots[0] = -15; copy.Weights[0] = 0.25;
        copy.Layer.Color.Index = 1; copy.Color.Index = 2; copy.Transparency.Value = 50;
        copy.XData["SPLINE_CLONE_STATE"].XDataRecord.Clear();
        Equal(control, original.ControlPoints[0], "Shared control memory"); Equal(knot, original.Knots[0], "Shared knot memory");
        Equal(weight, original.Weights[0], "Shared weight memory"); Equal((short)4, original.Layer.Color.Index, "Shared layer");
        Equal((short)25, original.Transparency.Value, "Shared transparency");
        Equal(1, original.XData["SPLINE_CLONE_STATE"].XDataRecord.Count, "Shared XData");
        if (original.FitPoints.Count != 0)
        {
            Vector3 point = original.FitPoints[0]; ((Vector3[])copy.FitPoints)[0] = Vector3.Zero;
            Equal(point, original.FitPoints[0], "Shared fit memory");
        }
    }
}
