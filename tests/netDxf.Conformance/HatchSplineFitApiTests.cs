using System.Collections;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Objects;

namespace NetDxf.Conformance;

internal static partial class Program
{
    private static void RegisterHatchSplineFitApiTests()
    {
        Run("hatch/spline-fit/api/default-and-validation", HatchSplineFitApiValidation);
        Run("hatch/spline-fit/api/edit-clone-convert", HatchSplineFitApiEdit);
        Run("hatch/spline-fit/api/insert", HatchSplineFitInsert);
        foreach (Vector3 normal in new[] { Vector3.UnitZ, -Vector3.UnitZ, new Vector3(1, 2, 3) })
            foreach (int transform in Enumerable.Range(0, 4))
            {
                Vector3 n = normal; int t = transform;
                Run($"hatch/spline-fit/api/transform/{n}/{t}", () => HatchSplineFitTransform(n, t));
            }
        foreach (DxfVersion version in SupportedVersions)
            foreach (bool binary in new[] { false, true })
                foreach (int placement in Enumerable.Range(0, 4))
                    foreach (int metadata in Enumerable.Range(0, 3))
                    {
                        DxfVersion v = version; bool b = binary; int p = placement, m = metadata;
                        Run($"hatch/spline-fit/api/export-profile/{v}/{b}/{p}/{m}", () => HatchSplineFitExportProfile(v, b, p, m));
                    }
    }

    private static Hatch NewHatchSplineFit()
    {
        using var input = new MemoryStream(RawFixtureBytes(HatchSplineFitTags(DxfVersion.AutoCad2018, 3, 3, false), false));
        return (DxfDocument.Load(input) ?? throw new InvalidOperationException("Fit API fixture rejected.")).Entities.Hatches.Single();
    }

    private static HatchBoundaryPath.Spline HatchFitEdge(Hatch hatch) => hatch.BoundaryPaths.Single().Edges.OfType<HatchBoundaryPath.Spline>().Single();

    private static void HatchSplineFitApiValidation()
    {
        var empty = new HatchBoundaryPath.Spline();
        Equal(0, empty.FitPoints.Count, "New fit collection not empty");
        Check(empty.StartTangent == null && empty.EndTangent == null, "New spline invented tangents.");
        var edge = HatchFitEdge(NewHatchSplineFit());
        foreach (double bad in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            foreach (Vector2 value in new[] { new Vector2(bad, 0), new Vector2(0, bad) })
            {
                Throws<ArgumentOutOfRangeException>(() => edge.FitPoints.Add(value));
                Throws<ArgumentOutOfRangeException>(() => edge.FitPoints.Insert(0, value));
                Throws<ArgumentOutOfRangeException>(() => edge.FitPoints[0] = value);
                Throws<ArgumentOutOfRangeException>(() => ((IList)edge.FitPoints).Add(value));
                Throws<ArgumentOutOfRangeException>(() => edge.StartTangent = value);
                Throws<ArgumentOutOfRangeException>(() => edge.EndTangent = value);
                AssertHatchFitEntity((Spline)edge.ConvertTo(), 3, 3);
            }
        edge.StartTangent = Vector2.Zero; edge.EndTangent = null;
        Equal<Vector2?>(Vector2.Zero, edge.StartTangent, "Zero tangent normalized away");
        Check(edge.EndTangent == null, "Cleared tangent retained.");
    }

    private static void HatchSplineFitApiEdit()
    {
        Hatch hatch = NewHatchSplineFit(); var edge = HatchFitEdge(hatch);
        double[] knots = (double[])edge.Knots.Clone(); Vector3[] controls = (Vector3[])edge.ControlPoints.Clone();
        edge.FitPoints.Insert(1, new(7, 8)); edge.FitPoints.Add(new(7, 8)); edge.FitPoints[0] = new(9, 10); edge.FitPoints.RemoveAt(2);
        var expected = new[] { new Vector2(9, 10), new Vector2(7, 8), new Vector2(10, 0), new Vector2(7, 8) };
        Check(expected.SequenceEqual(edge.FitPoints), "Fit edits lost order/duplicates.");
        Check(knots.SequenceEqual(edge.Knots) && controls.SequenceEqual(edge.ControlPoints), "Fit edits refitted the curve.");
        var clone = (Hatch)hatch.Clone(); var copied = HatchFitEdge(clone);
        Check(!ReferenceEquals(edge.FitPoints, copied.FitPoints), "Fit clone aliases source collection.");
        copied.FitPoints.Clear(); copied.StartTangent = null; copied.EndTangent = new(1, 2);
        Check(expected.SequenceEqual(edge.FitPoints), "Clone edit changed source fits.");
        Equal<Vector2?>(new(10, 20), edge.StartTangent, "Clone edit changed source tangent");
        var entity = (Spline)edge.ConvertTo(); var converted = HatchBoundaryPath.Spline.ConvertFrom(entity);
        Check(expected.SequenceEqual(converted.FitPoints), "Conversion regenerated fit metadata.");
        Check(knots.SequenceEqual(converted.Knots) && controls.SequenceEqual(converted.ControlPoints), "Conversion regenerated control geometry.");
        Equal(edge.StartTangent, converted.StartTangent, "Converted start tangent");
        Equal(edge.EndTangent, converted.EndTangent, "Converted end tangent");
        converted.FitPoints.Clear(); Check(expected.SequenceEqual(edge.FitPoints), "Converted collection aliases source.");
        var authored = new Spline(new[] { new Vector3(0, 0, 0), new Vector3(1, 2, 0), new Vector3(4, 0, 0) })
        { StartTangent = new Vector3(3, 4, 0), EndTangent = new Vector3(5, 6, 0) };
        var authoredEdge = new HatchBoundaryPath.Spline(authored);
        Check(authored.FitPoints.Select(p => new Vector2(p.X, p.Y)).SequenceEqual(authoredEdge.FitPoints), "Fit-authored entity conversion lost fits.");
        Equal<Vector2?>(new(3, 4), authoredEdge.StartTangent, "Fit-authored entity lost tangent");
    }

    private static Vector3 FitWorld(Hatch hatch, Vector2 point, bool vector) =>
        MathHelper.ArbitraryAxis(hatch.Normal) * new Vector3(point.X, point.Y, vector ? 0.0 : hatch.Elevation);

    private static void NearFitVector(Vector3 expected, Vector3 actual, string what)
    {
        Near(expected.X, actual.X, what + " X"); Near(expected.Y, actual.Y, what + " Y"); Near(expected.Z, actual.Z, what + " Z");
    }

    private static void CheckWorldFitData(Hatch hatch, Vector3[] fit, Vector3 start, Vector3 end)
    {
        var edge = HatchFitEdge(hatch);
        Equal(fit.Length, edge.FitPoints.Count, "Transformed fit count");
        for (int i = 0; i < fit.Length; ++i) NearFitVector(fit[i], FitWorld(hatch, edge.FitPoints[i], false), "Transformed fit");
        Check(edge.StartTangent.HasValue && edge.EndTangent.HasValue, "Transform dropped tangents.");
        NearFitVector(start, FitWorld(hatch, edge.StartTangent!.Value, true), "Transformed start tangent");
        NearFitVector(end, FitWorld(hatch, edge.EndTangent!.Value, true), "Transformed end tangent");
        var boundary = hatch.CreateBoundary(false).OfType<Spline>().Single();
        Equal(fit.Length, boundary.FitPoints.Count, "CreateBoundary dropped fits");
        for (int i = 0; i < fit.Length; ++i) NearFitVector(fit[i], boundary.FitPoints[i], "Boundary world fit");
        NearFitVector(start, boundary.StartTangent!.Value, "Boundary world start");
        NearFitVector(end, boundary.EndTangent!.Value, "Boundary world end");
    }

    private static void HatchSplineFitTransform(Vector3 normal, int operation)
    {
        Hatch hatch = NewHatchSplineFit(); hatch.Normal = normal;
        var edge = HatchFitEdge(hatch);
        Matrix3 matrix = operation switch
        {
            0 => Matrix3.Identity,
            1 => Matrix3.RotationZ(0.7),
            2 => Matrix3.RotationX(0.7) * Matrix3.RotationY(-0.3) * Matrix3.Scale(2),
            _ => Matrix3.Reflection(Vector3.UnitX)
        };
        Vector3 translation = new(17, -23, 31);
        var originalFit = edge.FitPoints.Select(p => FitWorld(hatch, p, false)).ToArray();
        Vector3 start = FitWorld(hatch, edge.StartTangent!.Value, true), end = FitWorld(hatch, edge.EndTangent!.Value, true);
        CheckWorldFitData(hatch, originalFit, start, end);
        hatch.TransformBy(matrix, translation);
        CheckWorldFitData(hatch, originalFit.Select(p => matrix * p + translation).ToArray(), matrix * start, matrix * end);
        Check(edge.FitPoints.SequenceEqual(HatchFitValues), "Transform changed a detached original edge.");
    }

    private static void HatchSplineFitInsert()
    {
        Hatch hatch = NewHatchSplineFit();
        var block = new Block("SplineFitBlock"); block.Entities.Add((Hatch)hatch.Clone());
        var insert = new Insert(block, new Vector3(10, 20, 30)) { Scale = new Vector3(2, 3, 1), Rotation = 30 };
        var copy = (Insert)insert.Clone();
        HatchFitEdge(copy.Block.Entities.OfType<Hatch>().Single()).FitPoints.Clear();
        Check(HatchFitEdge(block.Entities.OfType<Hatch>().Single()).FitPoints.SequenceEqual(HatchFitValues), "INSERT clone aliases fits.");
        Matrix3 matrix = Matrix3.RotationZ(Math.PI / 6) * Matrix3.Scale(2, 3, 1);
        var edge = HatchFitEdge(hatch);
        var expected = edge.FitPoints.Select(p => matrix * FitWorld(hatch, p, false) + insert.Position).ToArray();
        Vector3 start = matrix * FitWorld(hatch, edge.StartTangent!.Value, true), end = matrix * FitWorld(hatch, edge.EndTangent!.Value, true);
        Hatch exploded = insert.Explode().OfType<Hatch>().Single();
        CheckWorldFitData(exploded, expected, start, end);
        HatchFitEdge(exploded).FitPoints.Clear(); Check(HatchFitEdge(hatch).FitPoints.SequenceEqual(HatchFitValues), "Explosion aliases source.");
    }

    private static void HatchSplineFitExportProfile(DxfVersion version, bool binary, int placement, int metadata)
    {
        Hatch hatch = (Hatch)NewHatchSplineFit().Clone(); var edge = HatchFitEdge(hatch);
        if (metadata == 0) { edge.StartTangent = null; edge.EndTangent = null; }
        else { edge.FitPoints.Clear(); if (metadata == 1) edge.EndTangent = null; else edge.StartTangent = null; }
        var doc = new DxfDocument(version);
        switch (placement)
        {
            case 0: doc.Entities.Add(hatch); break;
            case 1:
                doc.Layouts.Add(new Layout("FitPaper")); doc.Entities.ActiveLayout = "FitPaper";
                doc.Entities.Add(hatch); doc.Entities.ActiveLayout = "Model"; break;
            case 2:
                var inner = new Block("FitInner"); inner.Entities.Add(hatch);
                var outer = new Block("FitOuter"); outer.Entities.Add(new Insert(inner)); doc.Entities.Add(new Insert(outer)); break;
            default:
                var unused = new Block("FitUnused"); unused.Entities.Add(hatch); doc.Blocks.Add(unused); break;
        }
        using var output = new MemoryStream(); byte[] original = { 11, 22, 33, 44 }; output.Write(original); output.Position = 2;
        string handles = doc.DrawingVariables.HandleSeed; string? identity = hatch.Handle;
        int apps = doc.ApplicationRegistries.Count, layouts = doc.Layouts.Count;
        if (version < DxfVersion.AutoCad2010)
        {
#if DEBUG
            try { doc.Save(output, binary); throw new InvalidOperationException("Lossy legacy fit export accepted."); }
            catch (NotSupportedException error)
            {
                Check(error.Message.Contains("HATCH", StringComparison.Ordinal) && error.Message.Contains("2010", StringComparison.Ordinal), "Missing fit export-profile diagnostic.");
            }
#else
            Check(!doc.Save(output, binary), "Lossy legacy fit export accepted.");
#endif
            Check(original.SequenceEqual(output.ToArray()), "Fit preflight modified destination bytes.");
            Equal(2L, output.Position, "Fit preflight advanced destination");
            Equal(handles, doc.DrawingVariables.HandleSeed, "Fit preflight allocated handles");
            Equal(identity, hatch.Handle, "Fit preflight changed entity identity");
            Equal(apps, doc.ApplicationRegistries.Count, "Fit preflight registered APPIDs");
            Equal(layouts, doc.Layouts.Count, "Fit preflight created a layout");
        }
        else
        {
            output.SetLength(0); output.Position = 0; Check(doc.Save(output, binary), "Supported fit export rejected.");
            output.Position = 0; var loaded = DxfDocument.Load(output) ?? throw new InvalidOperationException("Supported fit export invalid.");
            var actual = HatchFitEdge(loaded.Blocks.SelectMany(b => b.Entities).OfType<Hatch>().Single());
            Check(actual.FitPoints.SequenceEqual(edge.FitPoints), "Profile export changed fits.");
            Equal(edge.StartTangent, actual.StartTangent, "Profile start tangent"); Equal(edge.EndTangent, actual.EndTangent, "Profile end tangent");
        }
    }
}
