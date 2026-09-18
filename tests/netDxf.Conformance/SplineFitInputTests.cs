// Copyright (c) netDxf contributors. Licensed under the MIT License.
using System.Collections;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
namespace NetDxf.Conformance;
internal static partial class Program
{
    private sealed class FitInputSequence : IEnumerable<Vector3>
    {
        private readonly Vector3[] points;
        private readonly bool once, alternate;
        internal int Starts, Moves, Disposals;
        internal int ThrowAt = -1;
        internal readonly Exception Failure = new InvalidOperationException("fit input failure");
        internal FitInputSequence(Vector3[] points, bool once, bool alternate = false)
        { this.points = points; this.once = once; this.alternate = alternate; }
        public IEnumerator<Vector3> GetEnumerator()
        {
            Starts++;
            if (once && Starts != 1) throw new InvalidOperationException("Fit sequence was enumerated twice");
            return Enumerate(Starts).GetEnumerator();
        }
        private IEnumerable<Vector3> Enumerate(int pass)
        {
            try
            {
                for (int i = 0; i <= points.Length; i++)
                {
                    Moves++;
                    if (i == ThrowAt) throw Failure;
                    if (i == points.Length) yield break;
                    yield return alternate && pass > 1 ? points[i] + new Vector3(100, 200, 300) : points[i];
                }
            }
            finally { Disposals++; }
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    private static Vector3[] FitInputPoints(int count) => Enumerable.Range(0, count)
        .Select(i => new Vector3(i * 2 - 5, i * i - 3 * i, (i % 3) - 1)).ToArray();
    private static void FitInputConstruction(int count, int mode)
    {
        Vector3[] points = FitInputPoints(count); var expected = new Spline(points);
        var input = new FitInputSequence(points, mode == 0, mode == 2); var spline = new Spline(input);
        Equal(1, input.Starts, "One fit-input traversal"); Equal(count + 1, input.Moves, "Exact MoveNext count");
        Equal(1, input.Disposals, "Fit enumerator disposal");
        Check(spline.FitPoints.SequenceEqual(points), "Stored fit points differ from consumed points");
        Check(spline.ControlPoints.SequenceEqual(expected.ControlPoints), "Fitting used a different input snapshot");
        Check(spline.Knots.SequenceEqual(expected.Knots), "Fitting knot layout changed");
        Check(spline.Weights.SequenceEqual(expected.Weights), "Fitting weights changed");
        Equal(SplineCreationMethod.FitPoints, spline.CreationMethod, "Creation method");
        Check(spline.PolygonalVertexes(17).SequenceEqual(expected.PolygonalVertexes(17)), "Existing fitting arithmetic changed");
        var clone = (Spline)spline.Clone(); Check(clone.FitPoints.SequenceEqual(points), "Clone lost fit points");
        points[0] = new Vector3(999, 999, 999); Check(!spline.FitPoints.SequenceEqual(points), "Caller array aliases fit points");
    }
    private static void FitInputWire(DxfVersion version, bool binary)
    {
        var points = FitInputPoints(5); var input = new FitInputSequence(points, true); var spline = new Spline(input);
        var doc = new DxfDocument(version); doc.Entities.Add(spline);
        using var bytes = new MemoryStream(); Check(doc.Save(bytes, binary), "Fit input save");
        File.WriteAllBytes(Path.Combine(ArtifactDirectory, $"spline-fit-input-{version}-{binary}.dxf"), bytes.ToArray());
        bytes.Position = 0; var loaded = DxfDocument.Load(bytes) ?? throw new InvalidOperationException("Fit input load");
        var copy = loaded.Entities.Splines.Single();
        Check(copy.FitPoints.SequenceEqual(points), "Wire fit-point snapshot");
        Check(copy.ControlPoints.SequenceEqual(spline.ControlPoints), "Wire control-point snapshot");
        Check(copy.Knots.SequenceEqual(spline.Knots), "Wire knots"); Equal(1, input.Starts, "Serialization re-enumeration");
    }
    private static void RegisterSplineFitInputTests()
    {
        foreach (int count in new[] { 2, 3, 7, 32 }) for (int mode = 0; mode < 3; mode++)
        { int m = mode; Run($"spline-fit-input/sequence/{count}/{m}", () => FitInputConstruction(count, m)); }
        Run("spline-fit-input/null", () =>
        {
            try { _ = new Spline((IEnumerable<Vector3>)null!); throw new InvalidOperationException("Accepted null"); }
            catch (ArgumentNullException ex) { Equal("fitPoints", ex.ParamName, "Null argument"); }
        });
        foreach (int count in new[] { 0, 1 }) Run($"spline-fit-input/too-short/{count}", () =>
        {
            var input = new FitInputSequence(FitInputPoints(count), true);
            try { _ = new Spline(input); throw new InvalidOperationException("Accepted short input"); }
            catch (ArgumentOutOfRangeException ex) { Equal("fitPoints", ex.ParamName, "Short-input argument"); }
            Equal(1, input.Starts, "Short input traversal"); Equal(1, input.Disposals, "Short input disposal");
        });
        foreach (int at in new[] { 0, 2, 5 }) Run($"spline-fit-input/producer-failure/{at}", () =>
        {
            var input = new FitInputSequence(FitInputPoints(5), true) { ThrowAt = at }; Exception? failure = null;
            try { _ = new Spline(input); } catch (Exception ex) { failure = ex; }
            Check(ReferenceEquals(input.Failure, failure), "Producer exception changed");
            Equal(1, input.Starts, "Failure traversal"); Equal(1, input.Disposals, "Failure disposal");
        });
        Run("spline-fit-input/array-isolation", () =>
        {
            var input = FitInputPoints(7); var snapshot = (Vector3[])input.Clone(); var spline = new Spline(input);
            input[1] = Vector3.Zero; Check(spline.FitPoints.SequenceEqual(snapshot), "Array alias");
        });
        foreach (DxfVersion version in SupportedVersions) foreach (bool binary in new[] { false, true })
            Run($"spline-fit-input/wire/{version}/{binary}", () => FitInputWire(version, binary));
    }
}
