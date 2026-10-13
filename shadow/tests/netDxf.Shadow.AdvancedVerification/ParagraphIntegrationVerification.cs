using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Tables;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.Shx;
using netDxf.Shadow.Rendering.SkiaSharp;
using netDxf.Shadow.Studio;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class ParagraphIntegrationVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("paragraph/integration/source spacing is detached and invalidates only text", () =>
        {
            var document = new DxfDocument();
            var text = new MText(@"A\PB", Vector2.Zero, 10, 100)
            { LineSpacingFactor = 2, LineSpacingStyle = MTextLineSpacingStyle.Exact };
            var line = new Line(new Vector2(150, 0), new Vector2(170, 0));
            document.Entities.Add(text); document.Entities.Add(line);
            var source = new ShadowSession(document);
            var editor = new EditorSession(source);
            var compiler = new RenderCompiler();
            var before = source.Current;
            var first = compiler.Compile(before);
            var original = TextItem(first);
            Require(original.Text!.LineSpacing == new CadLineSpacing(2, true));
            Near(Distance(original.TextLayout!), 100.0 / 3);
            editor.SetProperty(Id(text), "LineSpacingFactor", ShadowValue.From(3.0));
            var changed = compiler.Compile(source.Current);
            Near(Distance(TextItem(changed).TextLayout!), 50);
            Require(ReferenceEquals(first.Items.Single(i => i.Text is null), changed.Items.Single(i => i.Text is null)));
            Require(before.Read(Id(text))["LineSpacingFactor"].Number() == 2);
            Near(Distance(original.TextLayout!), 100.0 / 3);
            editor.Undo(); Near(Distance(TextItem(compiler.Compile(source.Current)).TextLayout!), 100.0 / 3);
            editor.Redo(); Near(Distance(TextItem(compiler.Compile(source.Current)).TextLayout!), 50);
        }));
        tests.Add(("paragraph/integration/DXF roundtrip preserves controls and baseline settings", () =>
        {
            var document = new DxfDocument();
            var text = new MText(@"\pl1,r1;A\PB\P\pqc;C", new Vector2(4, 8), 10, 100)
            { LineSpacingFactor = 1.5, LineSpacingStyle = MTextLineSpacingStyle.Exact };
            document.Entities.Add(text);
            var before = TextItem(new RenderCompiler().Compile(new ShadowSession(document).Current)).TextLayout!;
            using var stream = new MemoryStream();
            Require(document.Save(stream)); stream.Position = 0;
            var loaded = DxfDocument.Load(stream) ?? throw new InvalidOperationException("DXF reload failed.");
            var reloaded = loaded.Entities.MTexts.Single();
            Require(reloaded.Value == text.Value && reloaded.LineSpacingFactor == text.LineSpacingFactor);
            var after = TextItem(new RenderCompiler().Compile(new ShadowSession(loaded).Current)).TextLayout!;
            Require(before.Lines.SequenceEqual(after.Lines) && before.LayoutBounds == after.LayoutBounds);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/paragraph-roundtrip.dxf", stream.ToArray());
        }));
        tests.Add(("paragraph/integration/native and SVG consume the same prepared paragraph layout", () =>
        {
            var document = new DxfDocument();
            document.Entities.Add(new MText(@"\pqc;{\LShared paragraph\l}\P\pl1,qj;Wrapped words and {\C1;colored text} use a single layout across both rendering engines.\P\ptr12;A^I42", Vector2.Zero, 10, 160));
            using var metrics = new SkiaTextLayoutEngine();
            var scene = new RenderCompiler(textLayout: metrics).Compile(new ShadowSession(document).Current);
            var layout = TextItem(scene).TextLayout!;
            Require(layout.Lines.Length >= 4 && layout.Diagnostics.IsEmpty);
            var frame = new RenderFrame(scene, Camera.Fit(scene.Bounds.Inflate(3), 1000, 700)) { Background = 0xff000000 };
            using var skia = new SkiaRenderBackend(textLayout: metrics);
            using var svg = new SvgRenderBackend();
            var png = skia.Render(frame); var vector = svg.Render(frame);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(vector.Bytes));
            Require(xml.Descendants().Any(e => e.Attribute("fill")?.Value == "#ff0000"));
            Require(xml.Descendants().Any(e => e.Name.LocalName == "line"));
            Require(ReferenceEquals(layout, TextItem(scene).TextLayout));
            using var bitmap = SKBitmap.Decode(png.Bytes);
            var top = frame.Viewport.ToScreen(new(scene.Bounds.MinX, scene.Bounds.MaxY));
            var bottom = frame.Viewport.ToScreen(new(scene.Bounds.MaxX, scene.Bounds.MinY));
            var painted = 0;
            for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.Red < 20 && pixel.Green < 20 && pixel.Blue < 20) continue;
                painted++;
                Require(x >= top.X - 2 && x <= bottom.X + 2 && y >= top.Y - 2 && y <= bottom.Y + 2,
                    "Native paragraph ink escaped compiled bounds.");
            }
            Require(painted > 100);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/paragraph-native.png", png.Bytes);
            File.WriteAllBytes("artifacts/paragraph-native.svg", vector.Bytes);
        }));
        tests.Add(("paragraph/integration/SHX uses shared indentation and retained source state", () =>
        {
            var font = ShxFontReader.ReadAscii("*0,4,Paragraph synthetic\n10,2,0,0\n*65,0,A\n9,0,10,6,0,0,-10,-6,0,0,0,2,8,8,0,0");
            var catalog = new ShxFontCatalog(); catalog.Register("paragraph-test.shx", font);
            var projector = new ShxEntityProjector(catalog.Current);
            var document = new DxfDocument();
            var text = new MText(@"A\PA", Vector2.Zero, 10, 100, new TextStyle("Paragraph", "paragraph-test.shx"));
            document.Entities.Add(text);
            var source = new ShadowSession(document); var before = source.Current;
            Require(projector.TryProject(before.Read(Id(text)), out var first, out _));
            new EditorSession(source).SetProperty(Id(text), "Value", ShadowValue.From(@"\pl2;A\PA"));
            Require(projector.TryProject(source.Current.Read(Id(text)), out var second, out _));
            var original = first.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            var shifted = second.Strokes.SelectMany(s => s.Contours).SelectMany(c => c.Curves).ToArray();
            Require(original.Length > 0 && original.Length == shifted.Length);
            for (var i = 0; i < original.Length; i++)
            {
                Near(shifted[i].At(0).X - original[i].At(0).X, 20);
                Near(shifted[i].At(0).Y, original[i].At(0).Y);
            }
            Require(first.Contours.IsEmpty && second.Contours.IsEmpty, "Presentation glyphs must not become topology edges.");
            Require(before.Read(Id(text))["Value"].Text == @"A\PA");
        }));
        tests.Add(("paragraph/integration/Studio gallery renders edits retains and reloads", () =>
        {
            using var studio = new Studio();
            var initial = State(studio); var revision = initial.GetProperty("revision").GetInt64();
            Require(initial.GetProperty("layouts").EnumerateArray().Any(l => l.GetString() == "Paragraphs"));
            studio.SetLayout(new(revision, "Paragraphs"));
            var state = State(studio); var count = state.GetProperty("occurrences").GetInt32();
            Require(count >= 20);
            Require(state.GetProperty("diagnostics").EnumerateArray().All(d => d.GetProperty("severity").GetString() != "Error"));
            var png = studio.Render(1400, 850, null, null, null, "skia", null, default);
            var svg = studio.Render(1400, 850, null, null, null, "svg", null, default);
            Require(png.Bytes.Length > 1000);
            _ = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
            studio.Retain(revision);
            studio.Add(new(revision, "line", 360, 170));
            Require(State(studio).GetProperty("occurrences").GetInt32() == count + 1);
            studio.History("undo", State(studio).GetProperty("revision").GetInt64());
            Require(State(studio).GetProperty("occurrences").GetInt32() == count);
            Require(studio.Render(1400, 850, null, null, null, "skia", revision, default).Bytes.SequenceEqual(png.Bytes));
            var dxf = studio.Save(); studio.Load(new MemoryStream(dxf));
            studio.SetLayout(new(State(studio).GetProperty("revision").GetInt64(), "Paragraphs"));
            Require(State(studio).GetProperty("occurrences").GetInt32() == count);
            Directory.CreateDirectory("artifacts");
            File.WriteAllBytes("artifacts/paragraph-gallery.png", png.Bytes);
            File.WriteAllBytes("artifacts/paragraph-gallery.svg", svg.Bytes);
            File.WriteAllBytes("artifacts/paragraph-gallery.dxf", dxf);
        }));
    }
    private static RenderItem TextItem(RenderScene scene) => scene.Items.Single(i => i.Text is not null);
    private static double Distance(RichTextLayoutResult layout) => layout.Lines[1].Baseline - layout.Lines[0].Baseline;
    private static ObjectId Id(DxfObject source) => new("H:" + source.Handle.ToUpperInvariant());
    private static JsonElement State(Studio studio) => JsonSerializer.SerializeToElement(studio.State(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static void Require(bool value, string message = "Paragraph integration contract failed.")
    { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double actual, double expected) => Require(double.IsFinite(actual) && Math.Abs(actual - expected) < 1e-7,
        $"Expected {expected:R}, got {actual:R}.");
}
