using System.Collections.Immutable;
using netDxf;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;

internal static class LinetypeContinuityVerification
{
    private static readonly CadLinetypeDecoration Mark = new(CadLinetypeDecorationKind.Text, "A", "");
    private static readonly CadLinetype Pattern = new([new(2), new(-1, Mark), new(-1)], 1, Vec3.UnitZ);

    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-continuity/" + name, run));
        foreach (var continuous in new[] { false, true })
            Test("authored simple pattern mode/" + continuous, () =>
            {
                var (session, _) = Drawing(continuous, false);
                var item = new RenderCompiler().Compile(session.Current).Items.Single();
                var dashes = StrokePattern.GetContours(item);
                Check(item.Contours.Single().Points.Length == 3);
                Check(item.Contours.Single().PatternBreaks.IsDefaultOrEmpty == continuous);
                Near(dashes[0].Points[0].X, 0); Near(dashes[0].Points[^1].X, 2);
                Near(dashes[1].Points[0].X, continuous ? 4 : 3);
                Near(dashes[1].Points[^1].X, continuous ? 6 : 5);
            });
        foreach (var continuous in new[] { false, true })
            Test("complex symbol phase and semantic isolation/" + continuous, () =>
            {
                var (session, _) = Drawing(continuous, true);
                var scene = new RenderCompiler().Compile(session.Current);
                var symbols = scene.Items.Where(i => i.IsPresentationOnly).ToArray();
                Check(symbols.Length == 2);
                Near(symbols[0].Text!.Origin.X, 3); Near(symbols[0].Text!.Origin.Y, 0);
                Near(symbols[1].Text!.Origin.X, 5); Near(symbols[1].Text!.Origin.Y, continuous ? 2 : 3);
                var geometry = new CadSceneBuilder().Build(session.Current).Occurrences.Single().Geometry;
                Check(geometry.Contours.Length == 1 && geometry.Contours[0].Curves.Length == 2);
                Check(new TopologyBuilder().Build(session.Current).Edges.Length == 2);
                Check(Snapping.Find(scene, new(5, 5), 0.001, SnapMode.Vertex) is not null);
            });
        Test("bulge sampling never creates extra pattern restarts", () =>
        {
            var (session, line) = Drawing(false, true);
            session.MutateSource(_ =>
            {
                line.Vertexes[0].Position = new(0, 0); line.Vertexes[0].Bulge = 1;
                line.Vertexes[1].Position = new(10, 0); line.Vertexes[2].Position = new(15, 0);
            });
            foreach (var tolerance in new[] { 0.1, 0.01 })
            {
                var scene = new RenderCompiler().Compile(session.Current, new(Tolerance: tolerance));
                var path = scene.Items[0].Contours.Single();
                Check(path.Points.Length > 10 && path.PatternBreaks.Length == 1);
                Check(scene.Items.Count(i => i.IsPresentationOnly) == 5);
                var last = scene.Items.Last().Text!; Near(last.Origin.X, 13); Near(last.Origin.Y, 0);
            }
        });
        Test("toggle is one undoable source edit with retained geometry", () =>
        {
            var (session, polyline) = Drawing(false, true);
            var plain = new Line(new Vector2(0, 20), new Vector2(10, 20));
            session.MutateSource(d => d.Entities.Add(plain));
            var retained = session.Current; var compiler = new RenderCompiler(); var before = compiler.Compile(retained);
            var editor = new EditorSession(session);
            editor.SetProperty(Id(polyline), "LinetypeGeneration", ShadowValue.From(true));
            var after = compiler.Compile(session.Current);
            Check(polyline.LinetypeGeneration && editor.State.UndoCount == 1);
            Near(after.Items.Last(i => i.IsPresentationOnly).Text!.Origin.Y, 2);
            Near(before.Items.Last(i => i.IsPresentationOnly).Text!.Origin.Y, 3);
            Check(ReferenceEquals(before.Items.Single(i => i.Entity == Id(plain)), after.Items.Single(i => i.Entity == Id(plain))));
            editor.Undo(); Check(!polyline.LinetypeGeneration);
            editor.Redo(); Check(polyline.LinetypeGeneration);
            Check(ReferenceEquals(before, compiler.Compile(retained)));
        });
        Test("drawing default cannot override existing entity generation flags", () =>
        {
            var (session, _) = Drawing(false, true);
            var compiler = new RenderCompiler();
            session.MutateSource(d => d.DrawingVariables.PLineGen = 1);
            Near(compiler.Compile(session.Current).Items.Last().Text!.Origin.Y, 3);
        });
        Test("closed paths restart on the authored closing segment", () =>
        {
            var doc = new DxfDocument();
            var line = new Polyline2D(new Polyline2DVertex[] { new(0, 0), new(3, 0), new(3, 4) }, true) { Linetype = SourcePattern(), LinetypeGeneration = false };
            doc.Entities.Add(line); var scene = new RenderCompiler().Compile(new ShadowSession(doc).Current);
            var symbols = scene.Items.Where(i => i.IsPresentationOnly).ToArray();
            Check(symbols.Length == 3);
            Near(symbols[2].Text!.Origin.X, 1.2); Near(symbols[2].Text!.Origin.Y, 1.6);
        });
        Test("zero length authored segments retain valid partition boundaries", () =>
        {
            var doc = new DxfDocument();
            doc.Entities.Add(new Polyline2D(new Polyline2DVertex[] { new(0, 0), new(0, 0), new(5, 0), new(5, 0), new(5, 5) }) { Linetype = SourcePattern() });
            var scene = new RenderCompiler().Compile(new ShadowSession(doc).Current);
            Check(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            Check(scene.Items.Count(i => i.IsPresentationOnly) == 2);
        });
        foreach (var version in new[] { DxfVersion.AutoCad2000, DxfVersion.AutoCad2018 })
        foreach (var binary in new[] { false, true })
        foreach (var continuous in new[] { false, true })
            Test($"DXF roundtrip/{version}/{binary}/{continuous}", () =>
            {
                var (session, _) = Drawing(continuous, true);
                byte[] bytes = [];
                session.MutateSource(d =>
                {
                    d.DrawingVariables.AcadVer = version;
                    using var stream = new MemoryStream(); Check(d.Save(stream, binary)); bytes = stream.ToArray();
                });
                var loaded = DxfDocument.Load(new MemoryStream(bytes));
                Check(loaded.Entities.Polylines2D.Single().LinetypeGeneration == continuous);
                var scene = new RenderCompiler().Compile(new ShadowSession(loaded).Current);
                Near(scene.Items.Last().Text!.Origin.Y, continuous ? 2 : 3);
            });
        foreach (var cuts in new ImmutableArray<int>[] { [-1], [0], [2], [1, 1], [2, 1] })
            Test("malformed boundaries/" + string.Join(',', cuts), () =>
            {
                var path = new RenderContour([Vec3.Zero, new(3, 0), new(6, 0)], false) { PatternBreaks = cuts };
                Throws<ArgumentException>(() => StrokePattern.Apply([path], [2, -2]));
                Throws<ArgumentException>(() => LinetypePlanner.Plan([path], Pattern));
            });
        Test("point budget includes shared endpoint copies and applies to cached paths", () =>
        {
            var path = new RenderContour([Vec3.Zero, new(5, 0), new(10, 0)], false) { PatternBreaks = [1] };
            Check(LinetypePlanner.Plan([path], Pattern, options: new(MaximumPoints: 4)).Length == 2);
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan([path], Pattern, options: new(MaximumPoints: 3)));
            Throws<OperationCanceledException>(() => LinetypePlanner.Plan([path], Pattern, cancellationToken: new(true)));
            Check(path.Points.Length == 3 && path.PatternBreaks.Length == 1);
        });
        Test("implicit closed edge is retained when partitioning", () =>
        {
            var path = new RenderContour([Vec3.Zero, new(3, 0), new(3, 4)], true) { PatternBreaks = [1] };
            var strokes = StrokePattern.Apply([path], [100]);
            Check(strokes.Length == 2);
            Near(strokes.Sum(c => c.Points.Zip(c.Points.Skip(1), Vec3.Distance).Sum()), 12);
            Check(strokes[^1].Points[^1] == Vec3.Zero);
        });
        Test("default and empty paths remain valid", () =>
        {
            Check(LinetypePlanner.Plan([new(default, false), new([], false)], Pattern).IsEmpty);
        });
        Test("disconnected restarted curves produce a diagnostic", () =>
        {
            var geometry = new CadGeometry([new([new LineCurve(Vec3.Zero, Vec3.UnitX), new LineCurve(new(2, 0), new(3, 0))])
                { RestartLinetypeAtCurves = true }]);
            var occurrence = new CadOccurrence("test", new("test:1"), [], CadStyle.Default with { Dashes = [2, -2] }, geometry);
            var scene = RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model", [occurrence], []));
            Check(scene.Items.IsEmpty && scene.Diagnostics.Any(d => d.Code == "render.geometry"));
        });
    }

    private static (ShadowSession, Polyline2D) Drawing(bool continuous, bool complex)
    {
        var points = complex ? new Polyline2DVertex[] { new(0, 0), new(5, 0), new(5, 5) }
            : [new(0, 0), new(3, 0), new(6, 0)];
        var line = new Polyline2D(points) { LinetypeGeneration = continuous,
            Linetype = complex ? SourcePattern() : new Linetype("DASH", [new LinetypeSimpleSegment(2), new LinetypeSimpleSegment(-2)]) };
        var doc = new DxfDocument(); doc.Entities.Add(line); return (new ShadowSession(doc), line);
    }
    private static Linetype SourcePattern() => new("TEXT", [new LinetypeSimpleSegment(2),
        new LinetypeTextSegment("A", new TextStyle("LABEL", "Arial.ttf"), -1, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1),
        new LinetypeSimpleSegment(-1)]);
    private static ObjectId Id(DxfObject value) => new("H:" + value.Handle.ToUpperInvariant());
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool value, string message = "Linetype continuity assertion failed.")
    { if (!value) throw new Exception(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
