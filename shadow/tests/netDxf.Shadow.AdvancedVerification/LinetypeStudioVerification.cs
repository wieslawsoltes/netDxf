using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;

internal static class LinetypeStudioVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        void Test(string name, Action run) => tests.Add(("linetype-integration/" + name, run));
        Test("work preflight rejects before any repeated geometry transforms", () =>
        {
            var provider = new CountingProvider(3);
            var render = RenderCompiler.Compile(Semantic(1), linetypeResources: provider,
                linetypeOptions: new(MaximumExpansionWork: 9));
            Check(render.Items.Length == 1 && provider.Transformations == 0);
            Check(render.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("exact work allowance emits a complete group", () =>
        {
            var provider = new CountingProvider(3);
            var render = RenderCompiler.Compile(Semantic(1), linetypeResources: provider,
                linetypeOptions: new(MaximumExpansionWork: 10));
            Check(render.Items.Length == 3 && provider.Transformations == 6);
            Check(!render.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("scene work ledger charges rejected groups and skips later resources", () =>
        {
            var provider = new CountingProvider(1);
            var render = RenderCompiler.Compile(Semantic(3), linetypeResources: provider,
                linetypeOptions: new(MaximumSceneExpansionWork: 7));
            Check(render.Items.Length == 5 && provider.Transformations == 2 && provider.Calls == 2);
            Check(render.Diagnostics.Count(d => d.Code == "render.linetype") == 2);
        });
        Test("cached expansions cannot bypass the scene work ledger", () =>
        {
            var document = new DxfDocument(); var type = SourcePattern();
            document.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 0, 0)) { Linetype = type });
            document.Entities.Add(new Line(new Vector3(0, 5, 0), new Vector3(10, 5, 0)) { Linetype = type });
            var session = new ShadowSession(document); var provider = new CountingProvider(1);
            var compiler = new RenderCompiler(linetypeResources: provider, linetypeOptions: new(MaximumSceneExpansionWork: 7));
            var first = compiler.Compile(session.Current);
            Check(first.Items.Length == 4 && provider.Transformations == 2);
            session.MutateSource(d => d.Entities.Add(new Point(new Vector3(100, 100, 0))));
            var next = compiler.Compile(session.Current);
            Check(next.Items.Length == 5 && provider.Transformations == 2 && provider.Calls == 3);
            Check(ReferenceEquals(first.Items[1], next.Items[1]));
            Check(next.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("repeated text characters participate in work preflight", () =>
        {
            var original = Semantic(1); var occurrence = original.Occurrences[0];
            var pattern = occurrence.Style.Linetype!;
            var elements = pattern.Elements.SetItem(1, pattern.Elements[1] with
            { Decoration = pattern.Elements[1].Decoration! with { Content = new string('A', 100) } });
            var scene = original with { Occurrences = [occurrence with { Style = occurrence.Style with { Linetype = pattern with { Elements = elements } } }] };
            var render = RenderCompiler.Compile(scene, linetypeOptions: new(MaximumExpansionWork: 150));
            Check(render.Items.Length == 1 && render.Diagnostics.Any(d => d.Code == "render.linetype"));
        });
        Test("invalid expansion budgets are rejected at construction", () =>
        {
            Throws<ArgumentOutOfRangeException>(() => new RenderCompiler(linetypeOptions: new(MaximumExpansionWork: 0)));
            Throws<ArgumentOutOfRangeException>(() => new RenderCompiler(linetypeOptions: new(MaximumSceneExpansionWork: 0)));
        });
        Test("symbol and base selection translate their shared source exactly once", () =>
        {
            var document = new DxfDocument();
            var line = new Line(Vector3.Zero, new Vector3(10, 0, 0)) { Linetype = SourcePattern() };
            document.Entities.Add(line); var session = new ShadowSession(document); var editor = new EditorSession(session);
            var render = new RenderCompiler(linetypeResources: new CountingProvider(1)).Compile(session.Current);
            var hit = RenderCompiler.HitTest(render, new(3, 0.5), 0.01);
            Check(hit is not null && hit.Entity == render.Items[0].Entity);
            editor.Select(render.Items.Select(item => item.Key));
            editor.Translate(render, new(5, 6));
            Near(line.StartPoint.X, 5); Near(line.StartPoint.Y, 6); Near(line.EndPoint.X, 15);
            Check(editor.State.UndoCount == 1);
            editor.Undo(); Near(line.StartPoint.X, 0); Near(line.EndPoint.X, 10);
            editor.Redo(); Near(line.StartPoint.X, 5); Near(line.EndPoint.X, 15);
        });
        foreach (var definition in new[] { false, true })
            Test($"symbol selection preserves insert/definition transforms/{definition}", () =>
            {
                var child = new Line(Vector3.Zero, new Vector3(10, 0, 0)) { Linetype = SourcePattern() };
                var block = new Block("LT_EDIT"); block.Entities.Add(child);
                var insert = new Insert(block, new Vector3(10, 20, 0)) { Rotation = 90, Scale = new Vector3(2, 2, 2) };
                var document = new DxfDocument(); document.Entities.Add(insert);
                var session = new ShadowSession(document); var editor = new EditorSession(session);
                var render = new RenderCompiler(linetypeResources: new CountingProvider(1)).Compile(session.Current);
                var symbol = render.Items.First(item => item.Key.Contains("/linetype[", StringComparison.Ordinal));
                editor.Select([symbol.Key]); editor.Translate(render, new(0, 2), definition);
                Near(child.StartPoint.X, definition ? 1 : 0); Near(child.StartPoint.Y, 0);
                Near(insert.Position.X, 10); Near(insert.Position.Y, definition ? 20 : 22);
                editor.Undo(); Near(child.StartPoint.X, 0); Near(insert.Position.Y, 20);
            });
        Test("Studio gallery renders both backends and saves numeric SHX references", () =>
        {
            using var studio = new Studio(); var initial = State(studio);
            Check(initial.GetProperty("layouts").EnumerateArray().Any(v => v.GetString() == "Complex linetypes"));
            studio.SetLayout(new(initial.GetProperty("revision").GetInt64(), "Complex linetypes"));
            var state = State(studio); var count = state.GetProperty("occurrences").GetInt32();
            Check(count > 30);
            foreach (var diagnostic in state.GetProperty("diagnostics").EnumerateArray())
                Check(diagnostic.GetProperty("severity").GetString() != "Error", diagnostic.ToString());
            var png = studio.Render(1300, 800, null, null, null, "skia", null, default);
            var svg = studio.Render(1300, 800, null, null, null, "svg", null, default);
            Check(png.Bytes.Length > 1000 && XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes)).Root is not null);
            _ = JsonSerializer.Serialize(studio.Graph(default));
            var dxf = studio.Save();
            var loaded = DxfDocument.Load(new MemoryStream(dxf)) ?? throw new Exception("Gallery DXF load failed.");
            Check(loaded.Linetypes["DEMO_SHAPES"].Segments.OfType<LinetypeShapeSegment>().Single().ShapeNumber == 65);
            studio.Load(new MemoryStream(dxf));
            studio.SetLayout(new(State(studio).GetProperty("revision").GetInt64(), "Complex linetypes"));
            Check(State(studio).GetProperty("occurrences").GetInt32() == count);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/complex-linetype-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/complex-linetype-gallery.svg", svg.Bytes);
            File.WriteAllBytes("artifacts/complex-linetype-gallery.dxf", dxf);
        });
    }

    private static CadScene Semantic(int count)
    {
        var style = CadStyle.Default with
        {
            Dashes = [2, -1, -1],
            Linetype = new([new(2), new(-1, new(CadLinetypeDecorationKind.Text, "A", "")), new(-1)], 1, Vec3.UnitZ)
        };
        return new(Guid.NewGuid(), 0, "Model", Enumerable.Range(0, count).Select(i =>
            new CadOccurrence("test/" + i, new ObjectId("test:" + i), [], style,
                new CadGeometry([new([new LineCurve(new(0, i * 5), new(10, i * 5))])]))).ToImmutableArray(), []);
    }
    private static Linetype SourcePattern() => new("LT_EDIT_PATTERN", new LinetypeSegment[]
    {
        new LinetypeSimpleSegment(2),
        new LinetypeTextSegment("A", new TextStyle("LT_EDIT_TEXT", "Arial.ttf"), -1, Vector2.Zero,
            LinetypeSegmentRotationType.Relative, 0, 1), new LinetypeSimpleSegment(-1)
    });
    private sealed class CountingProvider(int curves) : ILinetypeDecorationProvider
    {
        public long Revision => 0;
        public int Calls { get; private set; }
        public int Transformations { get; private set; }
        public bool TryProject(CadLinetypeDecoration decoration, out CadGeometry geometry, out string? diagnostic)
        {
            Calls++;
            var values = Enumerable.Range(0, curves).Select(_ => (Curve3)new CountingCurve(() => Transformations++)).ToImmutableArray();
            geometry = new([new(values)]); diagnostic = null; return true;
        }
    }
    private sealed record CountingCurve(Action Changed) : Curve3
    {
        public override Vec3 At(double parameter) => new(0, parameter);
        public override Curve3 Transform(Affine3 transform)
        { Changed(); return new LineCurve(transform.Point(Vec3.Zero), transform.Point(Vec3.UnitY)); }
    }
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Near(double actual, double expected) => Check(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-8, $"Expected {expected}, got {actual}.");
    private static void Check(bool condition, string message = "Linetype integration assertion failed.")
    { if (!condition) throw new Exception(message); }
    private static void Throws<T>(Action run) where T : Exception
    { try { run(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
}
