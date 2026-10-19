using System.Text.Json;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Studio;
using netDxf.Units;

internal static class InsertionPreviewVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("insertion-preview/root subsets preserve ownership, order and cache separation", () =>
        {
            var doc = new DxfDocument(); var a = new Line(Vector2.Zero, new Vector2(10, 0)); var b = new Circle(new Vector2(30, 0), 5);
            doc.Entities.Add(a); doc.Entities.Add(b); var source = new ShadowSession(doc); var snapshot = source.Current;
            var first = new ObjectId("H:" + a.Handle); var second = new ObjectId("H:" + b.Handle); var builder = new CadSceneBuilder();
            Check(builder.Build(snapshot).Occurrences.Length == 2, "Default root view changed");
            Check(builder.Build(snapshot, new() { RootEntities = [second] }).Occurrences.Single().Entity == second, "Subset rendered unrelated geometry");
            Check(builder.Build(snapshot, new() { RootEntities = [] }).Occurrences.IsEmpty, "Empty subset rendered all roots");
            Check(builder.Build(snapshot).Occurrences.Length == 2, "Subset polluted full-scene cache");
            var compiler = new RenderCompiler(builder);
            Check(compiler.Compile(snapshot, new() { RootEntities = [second, first] }).Items.Select(i => i.Entity).SequenceEqual([second, first]), "Subset source order changed");
            Reject(() => builder.Build(snapshot, new() { RootEntities = [first, first] }));
            Reject(() => builder.Build(snapshot, new() { RootEntities = [new("missing")] }));
            Check(ReferenceEquals(snapshot, source.Current) && doc.Entities.All.Count() == 2, "Read-only subset changed source membership");
        }));
        foreach (string layout in new[] { "Model", "Layout1" })
        foreach (bool alternate in new[] { false, true })
            tests.Add(($"insertion-preview/native image matches placed units rotation and attributes {layout}/{alternate}", () =>
            {
                var doc = new DxfDocument(netDxf.Header.DxfVersion.AutoCad2018); doc.DrawingVariables.InsUnits = DrawingUnits.Centimeters;
                if (layout != "Model") doc.Layouts.Add(new netDxf.Objects.Layout(layout));
                var block = new Block("PREVIEW") { Origin = new(3, 4, 0) }; block.Record.Units = DrawingUnits.Millimeters;
                block.Entities.Add(new Line(new Vector2(3, 4), new Vector2(23, 4)));
                block.AttributeDefinitions.Add(new("TAG") { Value = "P-101", Position = new(4, 7, 0), Height = 2 });
                doc.Blocks.Add(block); doc.Entities.Add(new Line(new Vector2(100_000, 100_000), new Vector2(100_010, 100_000)));
                using var bytes = new MemoryStream(); Check(doc.Save(bytes), "Seed save failed"); bytes.Position = 0;
                using var studio = new Studio(false); studio.Load(bytes); studio.SetLayout(new(studio.CadRevision, layout));
                var state = Json(studio.CadState());
                var id = state.GetProperty("blocks").EnumerateArray().Single(b => b.GetProperty("name").GetString() == "PREVIEW").GetProperty("id").GetString()!;
                var operation = new CadInsertOperation(new("INSERT", [Vec3.Zero], Reference: new(id), Scale: new(-2, 3, 1), Rotation: 37, BasePoint: alternate ? new(13, 4) : null));
                var preview = studio.CadInsertPreview(new(studio.CadRevision, operation));
                Check(Json(studio.CadState()).GetRawText() == state.GetRawText(), "Preview changed source, selection, history or view");
                Check(preview.Anchor == Vec3.Zero && preview.Items == 2 && preview.ImageBounds.Width < 50 && preview.Svg.Contains("P-101"), "Preview lost default attributes or included unrelated geometry");
                Check(preview.Svg.Contains("fill-opacity=\"0\""), "Preview background was not transparent");
                var token = new CancellationToken(true); Reject(() => studio.CadInsertPreview(new(studio.CadRevision, operation), token));
                Reject(() => studio.CadInsertPreview(new(studio.CadRevision, operation, Width: 10)));
                studio.CadInsert(new(studio.CadRevision, operation));
                Reject(() => studio.CadInsertPreview(new(preview.Revision, operation)));
                studio.CadSelect(new(studio.CadRevision, "clear"));
                var box = preview.ImageBounds;
                var final = studio.CadRender(512, 512, box.Center.X, box.Center.Y, 512 / box.Width, "svg", false, studio.CadRevision, null, default);
                var expected = Painted(preview.Svg); var actual = Painted(System.Text.Encoding.UTF8.GetString(final.Bytes));
                Check(expected.SequenceEqual(actual), "Transient geometry/text differs from the final placed INSERT\n" + string.Join("\n", expected) + "\nActual:\n" + string.Join("\n", actual));
                studio.History("undo", studio.CadRevision);
                Check(Json(studio.CadState()).GetProperty("state").GetProperty("entities").GetInt32() == state.GetProperty("state").GetProperty("entities").GetInt32(), "Preview added an undoable source object");
            }));
    }
    private static string[] Painted(string svg)
    {
        var root = XDocument.Parse(svg).Root!;
        // Reconstructing the camera from returned world bounds can change the last
        // floating-point bit. Compare all painted content, including text's parent
        // transform, at subnanopixel precision; only the background differs by design.
        root.Elements().First(e => e.Name.LocalName == "rect").Remove();
        foreach (var attribute in root.Descendants().Attributes().Where(a => a.Name.LocalName is
            "d" or "transform" or "x" or "y" or "width" or "height" or "font-size" or "textLength"))
            attribute.Value = Regex.Replace(attribute.Value, @"[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?", match =>
            {
                var value = Math.Round(double.Parse(match.Value, CultureInfo.InvariantCulture), 9);
                return value == 0 ? "0" : value.ToString("R", CultureInfo.InvariantCulture);
            });
        return root.Elements().Select(e => e.ToString(SaveOptions.DisableFormatting)).ToArray();
    }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, StudioJson.CreateOptions());
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or OperationCanceledException) { return; }
        throw new InvalidOperationException("Expected preview refusal");
    }
}
