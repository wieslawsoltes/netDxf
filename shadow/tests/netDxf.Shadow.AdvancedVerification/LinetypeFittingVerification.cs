using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using netDxf.Shadow.Studio;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using DxfViewport = netDxf.Entities.Viewport;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class LinetypeFittingVerification
{
    private static readonly CadLinetypeDecoration Label = new(CadLinetypeDecorationKind.Text, "A", "");
    private static CadLinetype Pattern() => new([new(2), new(-1, Label), new(-1)]);
    private static ImmutableArray<RenderContour> Path(double length) => [new([Vec3.Zero, new(length, 0)], false)];
    private static ImmutableArray<RenderContour> Fit(double length, ImmutableArray<double> pattern)
        => StrokePattern.Apply(Path(length), pattern, CadLinetypeAlignment.A);

    internal static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-fitting/" + name, run));
        Test("one exact repeat starts and ends with a half dash", () =>
            Intervals(Fit(4, [2, -2]), (0, 1), (3, 4)));
        Test("two exact repeats retain full interior dash", () =>
            Intervals(Fit(8, [2, -2]), (0, 1), (3, 5), (7, 8)));
        Test("residual is distributed only to terminal dashes", () =>
            Intervals(Fit(11, [2, -2]), (0, 2.5), (4.5, 6.5), (8.5, 11)));
        Test("short path is continuous", () => Intervals(Fit(3, [2, -2]), (0, 3)));
        Test("secondary dashes dots and gaps retain authored dimensions", () =>
            Intervals(Fit(8, [2, -1, 0, -1]), (0, 1), (2, 2), (3, 5), (6, 6), (7, 8)));
        Test("zero first descriptor retains endpoint dots", () =>
            Intervals(Fit(4, [0, -2]), (0, 0), (2, 2), (4, 4)));
        Test("adjacent pen-down descriptors are continuous", () =>
            Intervals(Fit(20, [2, 1]), (0, 20)));
        Test("legacy overload remains periodic", () =>
            Intervals(StrokePattern.Apply(Path(8), [2, -2]), (0, 2), (4, 6)));
        Test("closed path shares first dash across the seam", () =>
        {
            var contours = StrokePattern.Apply([new([Vec3.Zero, new(3, 0), new(3, 2), new(0, 2)], true)], [2, -2], CadLinetypeAlignment.A);
            Check(contours.Length == 3);
            Near(Length(contours[0]), 1.5); Near(Length(contours[1]), 3); Near(Length(contours[2]), 1.5);
            Near(contours.Sum(Length), 6);
        });
        Test("closed circles do not duplicate a zero-length seam dot", () =>
        {
            var contours = StrokePattern.Apply([new([Vec3.Zero, new(1, 0), new(1, 1), new(0, 1)], true)], [0, -2], CadLinetypeAlignment.A);
            Check(contours.Length == 2 && contours.All(c => c.Points[0] == c.Points[^1]));
        });
        Test("symbols share exactly the fitted stroke phase", () =>
        {
            var plan = LinetypePlanner.Plan(Path(9), Pattern(), CadLinetypeAlignment.A);
            Check(plan.Length == 2); Near(plan[0].Distance, 2.5); Near(plan[1].Distance, 6.5);
            Near(plan[0].Frame.X.Length, 1);
        });
        Test("short solid fallback does not invent a partial symbol motif", () =>
            Check(LinetypePlanner.Plan(Path(3), Pattern(), CadLinetypeAlignment.A).IsEmpty));
        Test("first descriptor decorations include the final terminal dash", () =>
        {
            var pattern = new CadLinetype([new(2, Label), new(-2)]);
            var plan = LinetypePlanner.Plan(Path(8), pattern, CadLinetypeAlignment.A);
            Check(plan.Select(p => p.Distance).SequenceEqual(new[] { 1.0, 5.0, 8.0 }));
        });
        Test("closed fitting does not stretch symbol dimensions", () =>
        {
            var plan = LinetypePlanner.Plan([new([Vec3.Zero, new(3, 0), new(3, 2), new(0, 2)], true)], Pattern(), CadLinetypeAlignment.A);
            Check(plan.Length == 2); Near(plan[0].Frame.X.Length, 1); Near(plan[1].Frame.X.Length, 1);
            Near(plan[0].Distance, 2.5); Near(plan[1].Distance, 7.5);
        });
        Test("fitting is performed per authored partition not per tessellation edge", () =>
        {
            var partitioned = new RenderContour([Vec3.Zero, new(3, 0), new(3, 3), new(3, 6)], false)
                { PatternBreaks = [1] };
            var plan = LinetypePlanner.Plan([partitioned], Pattern(), CadLinetypeAlignment.A);
            Check(plan.Length == 1 && plan[0].ContourIndex == 1); Near(plan[0].Distance, 3);
            var output = StrokePattern.Apply([partitioned], [2, -1, -1], CadLinetypeAlignment.A);
            Near(Length(output[0]), 3); Near(Length(output[1]), 2); Near(Length(output[2]), 2);
        });
        Test("translation and reversal preserve interval lengths", () =>
        {
            var source = new RenderContour([new(100, 50), new(89, 50)], false);
            var output = StrokePattern.Apply([source], [2, -2], CadLinetypeAlignment.A);
            Check(output.Length == 3); Near(output[0].Points[0].X, 100); Near(output[^1].Points[^1].X, 89);
            Near(Length(output[0]), 2.5); Near(Length(output[1]), 2); Near(Length(output[2]), 2.5);
        });
        Test("zero length contour stays pickable", () => Check(Fit(0, [2, -2]).Length == 1));
        foreach (var values in new[] { ImmutableArray.Create(-2.0, 2), ImmutableArray.Create(2.0), ImmutableArray.Create(0.0, 0), ImmutableArray.Create(double.NaN, -1.0) })
            Test("invalid A descriptor shape/" + tests.Count, () => Throws<ArgumentException>(() => Fit(10, values)));
        Test("unknown alignment is rejected in every public path", () =>
        {
            var invalid = (CadLinetypeAlignment)123;
            Throws<ArgumentOutOfRangeException>(() => StrokePattern.Apply(Path(8), [2, -2], invalid));
            Throws<ArgumentOutOfRangeException>(() => LinetypePlanner.Plan(Path(8), Pattern(), invalid));
            var (session, _) = Drawing();
            Throws<ArgumentOutOfRangeException>(() => new CadSceneBuilder().Build(session.Current, new() { LinetypeAlignment = invalid }));
            Throws<ArgumentOutOfRangeException>(() => new RenderCompiler().Compile(session.Current, new() { LinetypeAlignment = invalid }));
        });
        Test("stroke and symbol budgets reject before long repeat traversal", () =>
        {
            Throws<InvalidOperationException>(() => StrokePattern.Apply(Path(1e10), [2, -2], CadLinetypeAlignment.A, 8));
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan(Path(1e10), Pattern(), CadLinetypeAlignment.A, options: new(MaximumPlacements: 8)));
        });
        Test("terminal decoration participates in exact placement quota", () =>
        {
            var pattern = new CadLinetype([new(2, Label), new(-2)]);
            Throws<InvalidOperationException>(() => LinetypePlanner.Plan(Path(8), pattern, CadLinetypeAlignment.A, options: new(MaximumPlacements: 2)));
            Check(LinetypePlanner.Plan(Path(8), pattern, CadLinetypeAlignment.A, options: new(MaximumPlacements: 3)).Length == 3);
        });
        Test("fitting cancellation is not converted into fallback geometry", () =>
            Throws<OperationCanceledException>(() => LinetypePlanner.Plan(Path(8), Pattern(), CadLinetypeAlignment.A, cancellationToken: new(true))));
        Test("fit options invalidate style caches but preserve old scenes", () =>
        {
            var (session, _) = Drawing(); var compiler = new RenderCompiler(); var snapshot = session.Current;
            var periodic = compiler.Compile(snapshot); var options = new RenderCompileOptions { LinetypeAlignment = CadLinetypeAlignment.A };
            var fitted = compiler.Compile(snapshot, options);
            Check(!ReferenceEquals(periodic, fitted));
            Near(periodic.Items[1].Text!.Origin.X, 3); Near(fitted.Items[1].Text!.Origin.X, 2.5);
            Check(ReferenceEquals(periodic, compiler.Compile(snapshot)) && ReferenceEquals(fitted, compiler.Compile(snapshot, options)));
            Check(ReferenceEquals(snapshot, session.Current));
        });
        Test("fitted glyph selection translates the source once and undoes", () =>
        {
            var (session, line) = Drawing(); var compiler = new RenderCompiler();
            var render = compiler.Compile(session.Current, new() { LinetypeAlignment = CadLinetypeAlignment.A });
            var editor = new EditorSession(session); editor.Select(render.Items.Select(i => i.Key)); editor.Translate(render, new(2, 3));
            Near(line.StartPoint.X, 2); Near(line.EndPoint.X, 11); editor.Undo(); Near(line.StartPoint.X, 0);
            Check(new TopologyBuilder().Build(session.Current).Edges.Length == 1);
        });
        foreach (var scale in new[] { 0.5, 1.0, 2.0 })
            Test("paper compensation and clipping preserve fitted phase/" + scale, () =>
            {
                var (session, line) = Drawing();
                var viewport = new DxfViewport { Center = new(100, 0, 0), Width = 40, Height = 40, ViewHeight = 40 / scale, ViewTarget = new(4.5, 0, 0) };
                session.MutateSource(d => { var sheet = new Layout("Sheet"); d.Layouts.Add(sheet); sheet.AssociatedBlock.Entities.Add(viewport); d.DrawingVariables.PsLtScale = 1; });
                var options = new RenderCompileOptions("Sheet") { LinetypeAlignment = CadLinetypeAlignment.A };
                var scene = new RenderCompiler().Compile(session.Current, options);
                var symbols = scene.Items.Where(i => i.IsPresentationOnly).ToArray(); Check(symbols.Length > 0);
                foreach (var symbol in symbols) Near(symbol.Text!.XAxis.Length, 1);
                var model = new RenderCompiler().Compile(session.Current, new() { LinetypeScale = 1 / scale, LinetypeAlignment = CadLinetypeAlignment.A });
                var expected = model.Items.Where(i => i.IsPresentationOnly).Select(i => i.Text!.Origin.X).ToArray();
                Check(expected.Length == symbols.Length);
                for (var i = 0; i < expected.Length; i++) Near(symbols[i].Projection!.PaperToModel.Point(symbols[i].Text!.Origin).X, expected[i]);
            });
        Test("original public method signatures remain available", () =>
        {
            Check(typeof(StrokePattern).GetMethods().Any(m => m.Name == "Apply" && m.GetParameters().Length == 3));
            Check(typeof(LinetypePlanner).GetMethods().Any(m => m.Name == "Plan" && m.GetParameters().Length == 5));
        });
        Test("randomized open paths meet documented endpoint and gap invariants", () =>
        {
            var random = new Random(9241);
            for (var i = 0; i < 500; i++)
            {
                var dash = 0.01 + random.NextDouble() * 8; var gap = 0.01 + random.NextDouble() * 4;
                var period = dash + gap; var length = period * (1 + random.Next(20) + random.NextDouble());
                var output = Fit(length, [dash, -gap]); Check(output.Length >= 2);
                Near(output[0].Points[0].X, 0); Near(output[^1].Points[^1].X, length);
                Check(Length(output[0]) >= dash / 2 - 1e-10); Near(Length(output[0]), Length(output[^1]));
                for (var j = 1; j < output.Length; j++) Near(output[j].Points[0].X - output[j - 1].Points[^1].X, gap);
                for (var j = 1; j < output.Length - 1; j++) Near(Length(output[j]), dash);
            }
        });
        Test("adjacent representable values at repeat thresholds stay valid", () =>
        {
            foreach (var period in new[] { 0.3, 4.0, 11.7, 1e-10, 1e10 })
            foreach (var count in new[] { 1, 2, 13, 1000 })
            foreach (var length in new[] { Math.BitDecrement(period * count), period * count, Math.BitIncrement(period * count) })
            {
                var fitted = Fit(length, [period * 0.5, -period * 0.5]);
                Check(fitted.Length > 0 && fitted[0].Points[0] == Vec3.Zero);
                Check(Math.Abs(fitted[^1].Points[^1].X - length) <= Math.Max(1e-18, Math.Abs(length) * 1e-13));
            }
        });
        Test("Studio fitting view is workspace-local and invalidates request epochs", () =>
        {
            using var studio = new Studio(); var before = State(studio); var revision = before.GetProperty("revision").GetInt64();
            var sourceId = before.GetProperty("documentId").GetGuid(); var oldEpoch = before.GetProperty("viewVersion").GetInt64();
            studio.SetLinetypeView(new(revision, "A")); var after = State(studio);
            Check(after.GetProperty("linetypeAlignment").GetString() == "A" && after.GetProperty("revision").GetInt64() == revision);
            Check(after.GetProperty("viewVersion").GetInt64() > oldEpoch && after.GetProperty("undoCount").GetInt32() == 0);
            Throws<ShadowConflictException>(() => studio.ExecuteRequest(new(sourceId, "live", oldEpoch), () => studio.State()));
            studio.Fork(new(revision, "Fitted view")); Check(State(studio).GetProperty("linetypeAlignment").GetString() == "A");
            studio.SetLinetypeView(new(revision, "Periodic")); studio.SwitchWorkspace(new(revision, "live"));
            Check(State(studio).GetProperty("linetypeAlignment").GetString() == "A");
        });
        Test("Studio invalid or no-op view requests preserve state", () =>
        {
            using var studio = new Studio(); var before = State(studio); var revision = before.GetProperty("revision").GetInt64();
            studio.SetLinetypeView(new(revision, "Periodic"));
            Check(State(studio).GetProperty("viewVersion").GetInt64() == before.GetProperty("viewVersion").GetInt64());
            Throws<ArgumentException>(() => studio.SetLinetypeView(new(revision, "1")));
            Throws<ShadowConflictException>(() => studio.SetLinetypeView(new(revision + 1, "A")));
            Check(State(studio).GetProperty("linetypeAlignment").GetString() == "Periodic");
        });
        Test("Studio fitted gallery renders both backends", () =>
        {
            using var studio = new Studio(); var revision = State(studio).GetProperty("revision").GetInt64();
            studio.SetLayout(new(revision, "Complex linetypes")); studio.SetLinetypeView(new(revision, "A"));
            Check(!State(studio).GetProperty("diagnostics").EnumerateArray().Any(d => d.GetProperty("severity").GetString() == "Error"));
            var png = studio.Render(1300, 800, null, null, null, "skia", null, default);
            var svg = studio.Render(1300, 800, null, null, null, "svg", null, default);
            Check(png.Bytes.Length > 1000 && XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes)).Root is not null);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/linetype-fitted-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/linetype-fitted-gallery.svg", svg.Bytes);
        });
        Test("fitted native and SVG regression output", () =>
        {
            var (session, _) = Drawing();
            using var text = new SkiaTextLayoutEngine();
            var render = new RenderCompiler(textLayout: text).Compile(session.Current, new() { LinetypeAlignment = CadLinetypeAlignment.A });
            var frame = new RenderFrame(render, Camera.Fit(render.Bounds, 900, 240));
            using var svg = new SvgRenderBackend(); using var skia = new SkiaRenderBackend(textLayout: text);
            var vector = svg.Render(frame); var raster = skia.Render(frame);
            Check(XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes)).Root is not null); Check(raster.Bytes.Length > 500);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/linetype-fitted.svg", vector.Bytes); File.WriteAllBytes("artifacts/linetype-fitted.png", raster.Bytes);
        });
    }

    private static (ShadowSession Session, Line Line) Drawing()
    {
        var pattern = new Linetype("FITTED", new LinetypeSegment[] { new LinetypeSimpleSegment(2),
            new LinetypeTextSegment("A", new TextStyle("FIT_TEXT", "Arial.ttf"), -1, Vector2.Zero, LinetypeSegmentRotationType.Relative, 0, 1), new LinetypeSimpleSegment(-1) });
        var line = new Line(Vector3.Zero, new Vector3(9, 0, 0)) { Linetype = pattern };
        var doc = new DxfDocument(); doc.Entities.Add(line); return (new(doc), line);
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static double Length(RenderContour contour) => contour.Points.Zip(contour.Points.Skip(1), Vec3.Distance).Sum();
    private static void Intervals(ImmutableArray<RenderContour> actual, params (double From, double To)[] expected)
    {
        Check(actual.Length == expected.Length, $"Expected {expected.Length} intervals, got {actual.Length}.");
        for (var i = 0; i < actual.Length; i++) { Near(actual[i].Points[0].X, expected[i].From); Near(actual[i].Points[^1].X, expected[i].To); }
    }
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message = "Linetype fitting assertion failed.") { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run) where T : Exception { try { run(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
