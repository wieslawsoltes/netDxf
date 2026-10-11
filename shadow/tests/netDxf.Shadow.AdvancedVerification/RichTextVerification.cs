using System.Collections.Immutable;
using System.Text;
using System.Xml.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.NetDxf;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;
using SkiaSharp;
using Camera = netDxf.Shadow.Rendering.Viewport;

internal static class RichTextVerification
{
    public static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("text/scoped styles, relative heights and authored fonts", () =>
        {
            var parsed = RichText.Parse(@"a{\H2x;\fserif|b1|i1;\c16711680;b{\H0.5x;c}d}e", true, new("sans-serif", 10));
            Require(parsed.Inlines.Select(i => i.Text).SequenceEqual(new[] { "a", "b", "c", "d", "e" }));
            Require(parsed.Inlines.Select(i => i.Style.Height).SequenceEqual(new[] { 10.0, 20.0, 10.0, 20.0, 10.0 }));
            Require(parsed.Inlines[1].Style.Bold && parsed.Inlines[1].Style.Italic && parsed.Inlines[1].Style.FontFamily == "serif");
            Require(parsed.Inlines[1].Style.Color == 0xffff0000 && parsed.Inlines[^1].Style.Color is null);
            Require(parsed.Diagnostics.IsEmpty);
        }));
        tests.Add(("text/unicode escapes, literal braces and percent symbols", () =>
        {
            var parsed = RichText.Parse(@"\{\U+03A9\} %%d %%p %%c \U+D83D\U+DE00\~x", true, new("sans-serif", 10));
            Require(string.Concat(parsed.Inlines.Select(i => i.Text)) == "{Ω} ° ± ∅ 😀\u00a0x");
            Require(parsed.Inlines.All(i => i.SourceStart >= 0 && i.SourceLength > 0));
        }));
        tests.Add(("text/stacked fractions and all decoration rules", () =>
        {
            var layout = RichTextLayout.Layout(Text(@"\Lunder\l \Oover\o \Kstrike\k \S1/2; \S3#4; \S+5^-6;"), RichTextLayout.ApproximateMetrics);
            Require(layout.Rules.Length == 5);
            Require(layout.Runs.Any(r => r.Text == "+5") && layout.Runs.Any(r => r.Text == "-6"));
            Require(layout.Runs.Where(r => r.Text is "1" or "2").All(r => r.Style.Height == 7));
        }));
        tests.Add(("text/unrecognized fields and malformed controls remain explicit", () =>
        {
            var parsed = RichText.Parse(@"%<dangerous(%<nested>%)>% \Zkeep \HNaN;visible \W2", true, new("sans-serif", 10));
            Require(parsed.Diagnostics.Any(d => d.Code == "text.field"));
            Require(parsed.Diagnostics.Any(d => d.Code == "text.format"));
            Require(parsed.Diagnostics.Any(d => d.Code == "text.control"));
            Require(parsed.Diagnostics.Any(d => d.Code == "text.terminator"));
            var result = string.Concat(parsed.Inlines.Select(i => i.Text));
            Require(result.Contains("dangerous", StringComparison.Ordinal) && result.Contains(@"\HNaN;visible", StringComparison.Ordinal));
        }));
        tests.Add(("text/budgets and invalid metrics are rejected", () =>
        {
            Throws<ArgumentException>(() => RichText.Parse("{{{a}}}", true, new("sans-serif", 10), new(MaximumDepth: 2)));
            Throws<ArgumentException>(() => RichText.Parse("abcd", true, new("sans-serif", 10), new(MaximumCharacters: 3)));
            Throws<ArgumentException>(() => RichTextLayout.Layout(Text("a"), (_, _) => new(double.NaN, 1, 1)));
            Throws<InvalidOperationException>(() => RichTextLayout.Layout(Text("a b c"), RichTextLayout.ApproximateMetrics, maximumRuns: 2));
        }));
        tests.Add(("text/tracking does not split grapheme clusters", () =>
        {
            var layout = RichTextLayout.Layout(Text("\\T1.5;e\u0301😀"), RichTextLayout.ApproximateMetrics);
            Require(layout.Runs.Length == 2); Require(layout.Runs[0].Text == "e\u0301" && layout.Runs[1].Text == "😀");
            Require(layout.Runs[1].X > layout.Runs[0].Advance);
        }));
        tests.Add(("text/word wrapping preserves style boundaries and nonbreaking spaces", () =>
        {
            var layout = RichTextLayout.Layout(Text("aa {\\C1;bb}cc dd", width: 30), RichTextLayout.ApproximateMetrics);
            var b = layout.Runs.Single(r => r.Text == "bb"); var c = layout.Runs.Single(r => r.Text == "cc");
            Near(b.Y, c.Y); Require(b.X < c.X);
            var noBreak = RichTextLayout.Layout(Text(@"a\~b", width: 6), RichTextLayout.ApproximateMetrics);
            Require(noBreak.Runs.Length == 1 && noBreak.Runs[0].Text == "a\u00a0b");
        }));
        tests.Add(("text/MTEXT attachment anchors the column, not each row", () =>
        {
            var layout = RichTextLayout.Layout(Text(@"long\Px", width: 100, alignment: "TopCenter"), RichTextLayout.ApproximateMetrics);
            Near(layout.Runs.Single(r => r.Text == "long").X, -50); Near(layout.Runs.Single(r => r.Text == "x").X, -50);
        }));
        tests.Add(("text/linear measurement work for many words", () =>
        {
            var count = 0; var source = string.Join(' ', Enumerable.Repeat("word", 10000));
            _ = RichTextLayout.Layout(Text(source), (style, value) => { count++; return RichTextLayout.ApproximateMetrics(style, value); });
            Require(count == 19999, "Measure each word/space once, not every growing row prefix.");
        }));
        tests.Add(("text/font revision invalidates only text render geometry", () =>
        {
            var document = new DxfDocument(); var line = new Line(new Vector2(0, 0), new Vector2(10, 0));
            var text = new MText("abcd", new Vector2(0, 0), 10); document.Entities.Add(line); document.Entities.Add(text);
            var session = new ShadowSession(document); var metrics = new MutableMetrics(); var compiler = new RenderCompiler(textLayout: metrics);
            var first = compiler.Compile(session.Current); var statistics = compiler.CacheStatistics;
            metrics.Advance = 2; metrics.Revision++;
            var next = compiler.Compile(session.Current);
            Require(!ReferenceEquals(first, next));
            Require(ReferenceEquals(first.Items.Single(i => i.Text is null), next.Items.Single(i => i.Text is null)));
            Require(compiler.CacheStatistics.GeometryCompilations - statistics.GeometryCompilations == 1);
            Near(next.Items.Single(i => i.Text is not null).Bounds.Width, first.Items.Single(i => i.Text is not null).Bounds.Width * 2);
        }));
        tests.Add(("text/native glyph bounds contain rendered pixels", () =>
        {
            using var metrics = new SkiaTextLayoutEngine();
            var text = Text(@"{\fserif|b1|i1;\Q15;AV Ω} \S1/2;", height: 10);
            var scene = Compile(text, metrics); Require(scene.Items.Length == 1);
            var frame = new RenderFrame(scene, Camera.Fit(scene.Bounds.Inflate(2), 500, 220)) { Background = 0xff000000 };
            using var renderer = new SkiaRenderBackend(textLayout: metrics);
            using var bitmap = SKBitmap.Decode(renderer.Render(frame).Bytes);
            var bounds = scene.Bounds;
            var topLeft = frame.Viewport.ToScreen(new(bounds.MinX, bounds.MaxY)); var bottomRight = frame.Viewport.ToScreen(new(bounds.MaxX, bounds.MinY));
            var pixels = 0;
            for (var y = 0; y < bitmap.Height; y++) for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y); if (pixel.Red < 20 && pixel.Green < 20 && pixel.Blue < 20) continue;
                pixels++;
                Require(x >= topLeft.X - 2 && x <= bottomRight.X + 2 && y >= topLeft.Y - 2 && y <= bottomRight.Y + 2, "Native text escaped its compiled glyph bounds.");
            }
            Require(pixels > 100);
        }));
        tests.Add(("text/shared layout, native/SVG formatting and borrowed engine lifetime", () =>
        {
            using var metrics = new SkiaTextLayoutEngine();
            var scene = Compile(Text(@"{\fserif|b1|i1;\H1.5x;Rich text}\P\C1;RED \c255;BLUE \Lunderline\l \S1/2; <script>& Ω"), metrics);
            var frame = new RenderFrame(scene, Camera.Fit(scene.Bounds.Inflate(2), 1000, 320));
            using var host = new RenderHost(new Dictionary<string, Func<IRenderBackend>>
            {
                ["skia"] = () => new SkiaRenderBackend(textLayout: metrics), ["svg"] = () => new SvgRenderBackend()
            }, "skia");
            var png = host.Render(frame); host.Swap("svg"); var svg = host.Render(frame);
            var xml = XDocument.Parse(Encoding.UTF8.GetString(svg.Bytes));
            Require(xml.Descendants().Any(e => e.Attribute("font-weight")?.Value == "700"));
            Require(xml.Descendants().Any(e => e.Attribute("font-style")?.Value == "italic"));
            Require(xml.Descendants().Any(e => e.Attribute("fill")?.Value == "#ff0000"));
            Require(xml.Descendants().Any(e => e.Attribute("fill")?.Value == "#0000ff"));
            Require(!xml.Descendants().Any(e => e.Name.LocalName == "script"));
            Require(xml.Descendants().Any(e => e.Name.LocalName == "line"));
            host.Swap("skia"); Require(host.Render(frame).Bytes.Length > 1000);
            var before = metrics.Layout(Text("retained")); metrics.InvalidateFonts(); Require(!before.Runs.IsEmpty);
            Directory.CreateDirectory("artifacts"); File.WriteAllBytes("artifacts/rich-text.png", png.Bytes); File.WriteAllBytes("artifacts/rich-text.svg", svg.Bytes);
        }));
    }
    private static CadText Text(string value, double height = 10, double width = 0, string alignment = "TopLeft")
        => new(value, Vec3.Zero, Vec3.UnitX, Vec3.UnitY, height, "sans-serif", alignment, true, width);
    private static RenderScene Compile(CadText text, ITextLayoutEngine engine) => RenderCompiler.Compile(new CadScene(Guid.NewGuid(), 0, "Model",
        [new("text", new("text:1"), [], CadStyle.Default, new([], Text: text))], []), textLayout: engine);
    private sealed class MutableMetrics : ITextLayoutEngine
    {
        public long Revision { get; set; }
        public double Advance { get; set; } = 1;
        public RichTextLayoutResult Layout(CadText text) => RichTextLayout.Layout(text, (style, value) =>
            RichTextLayout.ApproximateMetrics(style, value) with { Advance = value.Length * style.Height * 0.6 * Advance });
    }
    private static void Require(bool value, string message = "Assertion failed") { if (!value) throw new InvalidOperationException(message); }
    private static void Near(double a, double b) => Require(double.IsFinite(a) && Math.Abs(a - b) < 1e-7, $"Expected {b:R}, got {a:R}");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
}
