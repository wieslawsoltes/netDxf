using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Objects;
using netDxf.Shadow;
using netDxf.Shadow.Dimensions;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Graph;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using netDxf.Tables;

internal static class DimensionVerification
{
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static void Near(Vec3 a, Vec3 b) => Require(Vec3.Distance(a, b) < 1e-7, $"Expected {b}; got {a}");
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static CadSceneBuilder Builder(DimensionProjectionOptions? options = null) => new([new DimensionEntityProjector(options)]);
    private static void Valid(CadScene scene)
    {
        Require(!scene.Occurrences.IsEmpty, string.Join("; ", scene.Diagnostics.Select(d => d.Message)));
        Require(!scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error), string.Join("; ", scene.Diagnostics.Select(d => d.Message)));
    }
    private static string Labels(CadScene scene) => string.Join("|", scene.Occurrences.Where(o => o.Geometry.Text is not null).Select(o => o.Geometry.Text!.Content));
    private static DimensionStyle Style() => new("Test dimensions")
    {
        TextStyle = new TextStyle("Test dimension text", "Arial.ttf"), TextHeight = 2,
        ArrowSize = 1, TextOffset = 0.5, LengthPrecision = 1, AngularPrecision = 1
    };
    private static Dimension[] Families(DimensionStyle style) =>
    [
        new AlignedDimension(new(0, 0), new(40, 0), 10, style),
        new LinearDimension(new(0, 0), new(40, 5), 12, 0, style),
        new Angular2LineDimension(new(0, 0), new(40, 0), new(0, 0), new(30, 30), 15, style),
        new Angular3PointDimension(new(0, 0), new(40, 0), new(30, 30), 15, style),
        new RadialDimension(new Vector2(0, 0), new Vector2(20, 10), style),
        new DiametricDimension(new Vector2(0, 0), new Vector2(20, 10), style),
        new OrdinateDimension(new Vector2(0, 0), new Vector2(30, 10), new Vector2(30, 30), OrdinateDimensionAxis.X, style),
        new ArcLengthDimension(new Vector2(0, 0), 20, 0, 70, 25, style)
    ];
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        foreach (var family in Families(Style()))
        {
            var prototype = family;
            tests.Add(("dimension/regenerate " + family.GetType().Name + " from detached data", () =>
            {
                var doc = new DxfDocument { BuildDimensionBlocks = false }; var dimension = (Dimension)prototype.Clone(); doc.Entities.Add(dimension);
                var store = new ShadowSession(doc); var old = store.Current; var reference = dimension.TextReferencePoint;
                var scene = Builder().Build(old); Valid(scene);
                Require(scene.Occurrences.All(o => o.Entity == Id(dimension)), "Generated fragments lost their source identity.");
                Require(scene.Occurrences.Any(o => o.Geometry.Text is not null), "No dimension label.");
                Require(scene.Occurrences.Any(o => !o.Geometry.DisplayContours.IsDefaultOrEmpty), "No dimension linework.");
                Require(dimension.Block is null && dimension.TextReferencePoint == reference, "Projection mutated live source.");
                Require(ReferenceEquals(old, store.Refresh()), "Projection changed the source graph.");
                Require(new TopologyBuilder().Build(scene).Edges.Length == 0, "Annotation strokes leaked into semantic topology.");
            }));
        }
        tests.Add(("dimension/translation preserves placement mode and projected fragments for every family", () =>
        {
            foreach (var dimension in Families(Style()))
            {
                dimension.Normal = new Vector3(0, 1, 1); dimension.Elevation = 4;
                var doc = new DxfDocument { BuildDimensionBlocks = false }; doc.Entities.Add(dimension);
                var store = new ShadowSession(doc); var builder = Builder(); var first = builder.Build(store.Current); Valid(first);
                var editor = new EditorSession(store); var movement = new Vec3(9, -7, 3);
                editor.Execute("Move dimension", EditOperations.Translate(store.Current, [Id(dimension)], movement));
                var next = builder.Build(store.Current); Valid(next); Require(!dimension.TextPositionManuallySet);
                Require(next.Occurrences.Length == first.Occurrences.Length);
                for (var j = 0; j < first.Occurrences.Length; j++)
                {
                    var a = first.Occurrences[j].Geometry; var b = next.Occurrences[j].Geometry;
                    if (a.Text is { } text) Near(b.Text!.Origin, text.Origin + movement);
                    if (!a.DisplayContours.IsDefaultOrEmpty) Near(b.DisplayContours[0].Curves[0].Start, a.DisplayContours[0].Curves[0].Start + movement);
                }
                editor.Undo(); Require(Labels(builder.Build(store.Current)) == Labels(first));
            }
        }));
        tests.Add(("dimension/branch and source edits regenerate labels without replacing retained geometry", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, Style()); doc.Entities.Add(dimension);
            var store = new ShadowSession(doc); var old = store.Current; var builder = Builder(); var first = builder.Build(old); Valid(first);
            var branch = new ShadowMemoryStore(old); var branchEditor = new EditorSession(branch);
            branchEditor.SetProperty(Id(dimension), "SecondReferencePoint", ShadowValue.From(new Vec3(75, 0)));
            var changed = builder.Build(branch.Current); Valid(changed); Require(Labels(first) != Labels(changed));
            Require(dimension.Measurement == 40, "Branch mutated source.");
            var editor = new EditorSession(store); editor.SetProperty(Id(dimension), "SecondReferencePoint", ShadowValue.From(new Vec3(75, 0)));
            Require(Labels(builder.Build(store.Current)) == Labels(changed)); editor.Undo(); Require(Labels(builder.Build(store.Current)) == Labels(first));
            Require(Labels(builder.Build(old)) == Labels(first), "Old snapshot changed.");
        }));
        tests.Add(("dimension/existing stale generated block is superseded without source mutation", () =>
        {
            var doc = new DxfDocument(); var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, Style()); doc.Entities.Add(dimension);
            var block = dimension.Block; Require(block is not null);
            var store = new ShadowSession(doc); var old = store.Current; var builder = Builder(); var a = builder.Build(old); Valid(a);
            new EditorSession(store).SetProperty(Id(dimension), "SecondReferencePoint", ShadowValue.From(new Vec3(80, 0)));
            var b = builder.Build(store.Current); Valid(b); Require(Labels(a) != Labels(b));
            Require(ReferenceEquals(block, dimension.Block), "Read-only regeneration replaced a source block.");
        }));
        tests.Add(("dimension/normal elevation and mirrored insert occurrence transforms", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var block = new Block("Dimensions");
            var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, Style()) { Normal = new Vector3(0, 1, 1), Elevation = 7 };
            block.Entities.Add(dimension); var insert = new Insert(block, new Vector3(40, 30, 10)) { Rotation = 25, Scale = new Vector3(-2, 3, 1) }; doc.Entities.Add(insert);
            var store = new ShadowSession(doc); var projector = new DimensionEntityProjector();
            Require(projector.TryProject(store.Current.Read(Id(dimension)), out var local, out _));
            var scene = Builder().Build(store.Current); Valid(scene); Require(scene.Occurrences.Length == local.Components.Length);
            for (var i = 0; i < scene.Occurrences.Length; i++)
            {
                var part = local.Components[i].Geometry; var occurrence = scene.Occurrences[i];
                Require(occurrence.InsertPath.SequenceEqual([Id(insert)]));
                if (!part.DisplayContours.IsDefaultOrEmpty)
                    Near(occurrence.Geometry.DisplayContours[0].Curves[0].Start, occurrence.SourceToWorld.Point(part.DisplayContours[0].Curves[0].Start));
            }
            var normal = new Vec3(0, 1, 1).Normalized;
            foreach (var point in local.Components.SelectMany(c => c.Geometry.DisplayContours).SelectMany(c => c.Curves).Select(c => c.Start))
                Require(Math.Abs(Vec3.Dot(point, normal) - 7) < 1e-7, "Dimension OCS/elevation was lost.");
        }));
        tests.Add(("dimension/symbolic ByBlock and ByLayer resolve per shared instance", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var style = Style(); style.ExtLineColor = AciColor.ByLayer;
            var block = new Block("D"); var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, style) { Color = AciColor.ByBlock }; block.Entities.Add(dimension);
            var red = new Insert(block, Vector2.Zero) { Color = AciColor.Red, Layer = new Layer("Green") { Color = AciColor.Green } };
            var blue = new Insert(block, new Vector2(80, 0)) { Color = AciColor.Blue, Layer = new Layer("Yellow") { Color = AciColor.Yellow } };
            doc.Entities.Add(red); doc.Entities.Add(blue); var scene = Builder().Build(new ShadowSession(doc).Current); Valid(scene);
            var a = scene.Occurrences.Where(o => o.InsertPath.Contains(Id(red))).Select(o => o.Style.Argb & 0xffffff).ToHashSet();
            var b = scene.Occurrences.Where(o => o.InsertPath.Contains(Id(blue))).Select(o => o.Style.Argb & 0xffffff).ToHashSet();
            Require(a.Contains(0xff0000) && a.Contains(0x00ff00), "First instance lost inherited colors.");
            Require(b.Contains(0x0000ff) && b.Contains(0xffff00), "Second instance reused another instance's colors.");
        }));
        tests.Add(("dimension/override colors and alternate-unit dependencies invalidate cached projection", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, Style());
            dimension.Style.AlternateUnits.Enabled = true; dimension.Style.AlternateUnits.Multiplier = 2; dimension.Style.AlternateUnits.Suffix = " ALT";
            dimension.StyleOverrides.Add(new DimensionStyleOverride(DimensionStyleOverrideType.TextColor, AciColor.Red)); doc.Entities.Add(dimension);
            var store = new ShadowSession(doc); var builder = Builder(); var first = builder.Build(store.Current); Valid(first);
            Require(first.Occurrences.Where(o => o.Geometry.Text is not null).All(o => (o.Style.Argb & 0xffffff) == 0xff0000));
            var alternate = store.Current.Read(Id(dimension))["Style"]["AlternateUnits"].Id!.Value;
            new EditorSession(store).SetProperty(alternate, "Multiplier", ShadowValue.From(3.0));
            var next = builder.Build(store.Current); Valid(next); Require(Labels(next) != Labels(first), "Alternate units did not invalidate.");
        }));
        tests.Add(("dimension/custom arrow dependencies and text font data are captured", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var arrow = new Block("Custom arrow");
            var line = new Line(new Vector2(-1, -1), new Vector2(0, 0)) { Color = AciColor.ByBlock }; arrow.Entities.Add(line);
            var style = Style(); style.DimArrow1 = arrow; style.DimArrow2 = arrow;
            var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, style); doc.Entities.Add(dimension);
            var store = new ShadowSession(doc); var builder = Builder(); var first = builder.Build(store.Current); Valid(first);
            new EditorSession(store).SetProperty(Id(line), "StartPoint", ShadowValue.From(new Vec3(-6, -8)));
            var next = builder.Build(store.Current); Valid(next);
            Require(next.Occurrences.Where(o => !o.Geometry.DisplayContours.IsDefaultOrEmpty).SelectMany(o => o.Geometry.DisplayContours).SelectMany(c => c.Curves).Any(c => c is LineCurve l && (l.B - l.A).Length >= 9.9), "Arrow edit was not regenerated.");
        }));
        tests.Add(("dimension/budgets fail without partial components and culture is restored", () =>
        {
            var doc = new DxfDocument { BuildDimensionBlocks = false }; var dimension = new AlignedDimension(new(0, 0), new(40, 0), 10, Style()); doc.Entities.Add(dimension);
            var snapshot = new ShadowSession(doc).Current; var before = CultureInfo.CurrentCulture;
            var scene = Builder(new(MaximumSourceObjects: 1, CultureName: "fr-FR")).Build(snapshot);
            Require(scene.Occurrences.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            Require(CultureInfo.CurrentCulture.Equals(before));
            scene = Builder(new(MaximumGeneratedOccurrences: 1)).Build(snapshot);
            Require(scene.Occurrences.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
            scene = Builder().Build(snapshot, new(MaximumOccurrences: 2));
            Require(scene.Occurrences.IsEmpty && scene.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error));
        }));
        tests.Add(("sample/dimension gallery rendering and DXF roundtrip", () =>
        {
            using var studio = new Studio();
            JsonElement State() => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            studio.SetLayout(new(State().GetProperty("revision").GetInt64(), "Dimensions"));
            var state = State(); Require(state.GetProperty("occurrences").GetInt32() > 40);
            foreach (var d in state.GetProperty("diagnostics").EnumerateArray()) Require(d.GetProperty("severity").GetString() != "Error", d.ToString());
            var png = studio.Render(1400, 920, null, null, null, "skia", null, default);
            var svg = studio.Render(1400, 920, null, null, null, "svg", null, default);
            Require(png.Bytes.Length > 2000); Require(XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes)).Descendants().Any(e => e.Name.LocalName == "text"));
            var bytes = studio.Save(); studio.Load(new MemoryStream(bytes)); studio.SetLayout(new(State().GetProperty("revision").GetInt64(), "Dimensions"));
            Require(State().GetProperty("occurrences").GetInt32() == state.GetProperty("occurrences").GetInt32());
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/dimension-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/dimension-gallery.svg", svg.Bytes); File.WriteAllBytes("artifacts/dimension-gallery.dxf", bytes);
        }));
    }
}
