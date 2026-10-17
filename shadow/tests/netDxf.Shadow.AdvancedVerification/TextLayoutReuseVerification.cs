using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using global::SkiaSharp;
using global::SkiaSharp.HarfBuzz;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;

internal static class TextLayoutReuseVerification
{
    private static readonly JsonSerializerOptions LayoutJson = new() { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("text-layout-reuse/positioned copies share complete local layout", () =>
        {
            using var engine = Engine();
            var text = Text("Valve supply water");
            var first = engine.Layout(text);
            for (var i = 0; i < 200; i++)
            {
                var moved = text with { Origin = new(i * 17, -i * 5, i), XAxis = new(1, i * .01), YAxis = new(-i * .02, 1) };
                Require(ReferenceEquals(first, engine.Layout(moved)), "Placement changed the local layout cache key.");
            }
            var statistics = engine.CacheStatistics;
            Require(statistics.LayoutCompilations == 1 && statistics.SharedLayoutHits == 200 && statistics.CachedLayouts == 1);
            Require(ReferenceEquals(first, engine.Layout(text)) && engine.CacheStatistics.SharedLayoutHits == statistics.SharedLayoutHits,
                "The per-object identity lookup should remain the first path.");
        }));
        tests.Add(("text-layout-reuse/all local inputs remain part of the key", () =>
        {
            using var engine = Engine();
            var text = Text("A B\\PC D") with { Multiline = true, Width = 16 };
            var initial = engine.Layout(text);
            CadText[] variants = [text with { Content = "A B\\PC E" }, text with { Height = 3 },
                text with { FontFamily = "alternate" }, text with { Alignment = "TopRight" },
                text with { Multiline = false }, text with { Width = 7 },
                text with { LineSpacing = new(1, false) }, text with { LineSpacing = new(1, true) },
                text with { LineSpacing = new(1.5, false) }, text with { Width = 0 },
                text with { Width = BitConverter.Int64BitsToDouble(long.MinValue) }];
            foreach (var variant in variants)
            {
                var actual = engine.Layout(variant);
                Require(!ReferenceEquals(initial, actual), "A local layout input was omitted from the key.");
                EqualLayout(ReferenceLayout(variant), actual);
                Require(ReferenceEquals(actual, engine.Layout(variant with { Origin = new(17, -3) })));
            }
            Require(engine.CacheStatistics.LayoutCompilations == variants.Length + 1,
                "Distinct spacing states or signed-zero widths were combined.");
            foreach (var invalid in new[] { text with { Width = double.NaN }, text with { Height = double.NaN },
                text with { Width = -1 }, text with { LineSpacing = new(double.NaN) } })
            {
                Throws<ArgumentOutOfRangeException>(() => engine.Layout(invalid));
                Throws<ArgumentOutOfRangeException>(() => engine.Layout(invalid with { Origin = new(1, 2) }));
            }
        }));
        tests.Add(("text-layout-reuse/rich text remains equal to uncached native layout", () =>
        {
            using var engine = Engine();
            foreach (var content in new[] { "ffi e\u0301 Åß مرحبا שלום", "{\\W1.4;wide} {\\Q21;shear} \\Lrule\\l \\S3/8;",
                "\\pqc,l1,r0.5,t4,c9,r14;A\tB\tC\\P{\\T1.25;tracked} \\S5#9;", "\\pqd;Alpha beta gamma delta\\P\\pql;End" })
            {
                var text = Text(content) with { Multiline = true, Width = 30, Alignment = "MiddleCenter", LineSpacing = new(1.25, true) };
                var expected = ReferenceLayout(text);
                var actual = engine.Layout(text);
                EqualLayout(expected, actual);
                var copy = engine.Layout(text with { Origin = new(21, -6), XAxis = new(-1, .4), YAxis = new(.3, .8) });
                Require(ReferenceEquals(actual, copy)); EqualLayout(expected, copy);
            }
        }));
        tests.Add(("text-layout-reuse/rendered text preserves transforms selection and opacity", () =>
        {
            using var engine = Engine();
            using var oracleEngine = Engine();
            var texts = Enumerable.Range(0, 24).Select(i => Text(i % 2 == 0 ? "ffi \\LValve\\l \\S3/8;" : "{\\Q19;مرحبا} {\\W1.3;AB}") with
            {
                Origin = new(i % 6 * 20, i / 6 * 12), XAxis = new(i % 3 == 0 ? -.8 : 1, .13),
                YAxis = new(.17, i % 5 == 0 ? -.9 : 1), Multiline = true, Width = 17, Alignment = "MiddleCenter"
            }).ToArray();
            var source = Source(texts);
            var actual = RenderCompiler.Compile(source, textLayout: engine);
            var expected = RenderCompiler.Compile(source, textLayout: new ReferenceEngine());
            Require(actual.Bounds == expected.Bounds && actual.Diagnostics.SequenceEqual(expected.Diagnostics));
            for (var i = 0; i < actual.Items.Length; i++) EqualLayout(expected.Items[i].TextLayout!, actual.Items[i].TextLayout!);
            using var backend = new SkiaRenderBackend(textLayout: engine);
            using var oracle = new SkiaRenderBackend(textLayout: oracleEngine);
            var fit = Viewport.Fit(actual.Bounds, 480, 320);
            for (var i = 0; i < 3; i++)
            {
                var frame = new RenderFrame(actual, fit with { Center = fit.Center + new Vec3(i * .23, i * -.17) },
                    ImmutableHashSet.Create("text/3", "text/8"), 0x293b8f14, 0x916ef374);
                Require(backend.RenderPixels(frame).Bytes.AsSpan().SequenceEqual(oracle.RenderPixels(frame with { Scene = expected }).Bytes),
                    "Local layout reuse changed rendered text pixels.");
            }
        }));
        tests.Add(("text-layout-reuse/font invalidation clears identity and shared layouts", () =>
        {
            using var engine = Engine();
            var text = Text("WMW iii 123");
            var first = engine.Layout(text);
            engine.InvalidateFonts();
            Require(engine.CacheStatistics.CachedLayouts == 0 && engine.CacheStatistics.CachedLayoutUnits == 0);
            var refreshed = engine.Layout(text);
            Require(!ReferenceEquals(first, refreshed)); EqualLayout(first, refreshed);
            engine.SetTypefaceResolver(style => Typeface(style, "monospace"));
            Require(engine.CacheStatistics.CachedLayouts == 0 && engine.CacheStatistics.CachedLayoutUnits == 0);
            var changed = engine.Layout(text with { Origin = new(2, 3) });
            Require(!ReferenceEquals(refreshed, changed)); EqualLayout(ReferenceLayout(text, "monospace"), changed);
        }));
        tests.Add(("text-layout-reuse/empty content preserves unused nullable font input", () =>
        {
            using var engine = Engine();
            var text = Text("") with { FontFamily = null! };
            var result = engine.Layout(text);
            Require(result.Runs.IsEmpty); EqualLayout(ReferenceLayout(text), result);
            Require(ReferenceEquals(result, engine.Layout(text with { Origin = new(2, 4) })));
        }));
        tests.Add(("text-layout-reuse/native font eviction invalidates shared layout inputs", () =>
        {
            using var engine = new SkiaTextLayoutEngine();
            var firstCalls = 0;
            engine.SetTypefaceResolver(style => Typeface(style, style.FontFamily == "first-family" && ++firstCalls > 1 ? "monospace" : "sans-serif"));
            var text = Text("WMW iii") with { FontFamily = "first-family" };
            var first = engine.Layout(text);
            for (var i = 0; i < 64; i++) engine.Layout(Text("other") with { FontFamily = "family/" + i });
            Require(engine.CacheStatistics.CachedLayouts == 0, "A layout spanning font eviction entered the shared cache.");
            var next = engine.Layout(text with { Origin = new(2, 4) });
            Require(!ReferenceEquals(first, next) && firstCalls == 2);
            EqualLayout(ReferenceLayout(text, "monospace"), next);
            Require(ReferenceEquals(first, engine.Layout(text)), "Native font eviction changed existing identity-cache semantics.");
        }));
        tests.Add(("text-layout-reuse/bounded eviction retains independent identity entries", () =>
        {
            using var engine = Engine();
            var text = Text("oldest-label");
            var first = engine.Layout(text);
            for (var i = 0; i < 600; i++) engine.Layout(Text("label " + i));
            var before = engine.CacheStatistics;
            Require(before.CachedLayouts <= 512 && before.CachedLayoutUnits <= 262_144);
            Require(ReferenceEquals(first, engine.Layout(text)), "Shared eviction invalidated a retained immutable text instance.");
            var rebuilt = engine.Layout(text with { Origin = new(1, 2) });
            Require(!ReferenceEquals(first, rebuilt) && engine.CacheStatistics.LayoutCompilations == before.LayoutCompilations + 1);
            EqualLayout(first, rebuilt);
            engine.Dispose();
            Require(engine.CacheStatistics.CachedLayouts == 0 && engine.CacheStatistics.CachedLayoutUnits == 0);
            Throws<ObjectDisposedException>(() => engine.Layout(text));
        }));
        tests.Add(("text-layout-reuse/oversized complete layout bypasses shared retention", () =>
        {
            using var engine = Engine();
            var text = Text(string.Concat(Enumerable.Repeat("A ", 9000)));
            var result = engine.Layout(text);
            Require(result.Runs.Sum(run => run.Text.Length) == text.Content.Length);
            Require(engine.CacheStatistics.CachedLayouts == 0 && engine.CacheStatistics.CachedLayoutUnits == 0);
            Require(ReferenceEquals(result, engine.Layout(text)));
            var moved = engine.Layout(text with { Origin = new(1, 2) });
            Require(!ReferenceEquals(result, moved)); EqualLayout(result, moved);
        }));
        tests.Add(("text-layout-reuse/shared and independent engines retain separate lifetimes", () =>
        {
            using var engine = Engine();
            using var independent = Engine();
            var text = Text("shared-label");
            var first = engine.Layout(text);
            var unrelated = independent.Layout(text);
            Require(!ReferenceEquals(first, unrelated));
            var results = Task.WhenAll(Enumerable.Range(0, 20).Select(i => Task.Run(() => engine.Layout(text with { Origin = new(i, i) })))).GetAwaiter().GetResult();
            Require(results.All(result => ReferenceEquals(first, result)));
            engine.InvalidateFonts();
            Require(ReferenceEquals(unrelated, independent.Layout(text with { Origin = new(4, 5) })));
            Require(independent.CacheStatistics.LayoutCompilations == 1 && independent.CacheStatistics.CachedLayouts == 1);
        }));
        tests.Add(("text-layout-reuse/reentrant resolver does not publish interrupted layouts", () =>
        {
            using var engine = new SkiaTextLayoutEngine();
            var text = Text("reentrant-label");
            var entered = false;
            RichTextLayoutResult? nested = null;
            engine.SetTypefaceResolver(style =>
            {
                if (!entered) { entered = true; nested = engine.Layout(text with { Origin = new(3, 5) }); }
                return Typeface(style);
            });
            var result = engine.Layout(text);
            Require(nested is not null); EqualLayout(nested!, result);
            Require(engine.CacheStatistics.CachedLayouts == 0 && engine.CacheStatistics.LayoutCompilations == 2);
            var ordinary = engine.Layout(text with { Origin = new(5, 7) });
            EqualLayout(result, ordinary);
            Require(engine.CacheStatistics.CachedLayouts == 1 && engine.CacheStatistics.LayoutCompilations == 3);
        }));
        tests.Add(("text-layout-reuse/font epoch change cannot fill the new shared cache", () =>
        {
            using var engine = new SkiaTextLayoutEngine();
            var entered = false;
            engine.SetTypefaceResolver(style =>
            {
                if (!entered) { entered = true; engine.InvalidateFonts(); }
                return Typeface(style);
            });
            var text = Text("epoch-label");
            var revision = engine.Revision;
            var first = engine.Layout(text);
            Require(engine.Revision == revision + 1 && engine.CacheStatistics.CachedLayouts == 0);
            var second = engine.Layout(text);
            Require(!ReferenceEquals(first, second), "A result from the previous font epoch entered the new identity cache.");
            EqualLayout(first, second);
            Require(engine.CacheStatistics.CachedLayouts == 1);
        }));
    }

    private static CadText Text(string content) => new(content, Vec3.Zero, Vec3.UnitX, Vec3.UnitY, 2, "test", "BaselineLeft", false, 0);
    private static CadScene Source(IEnumerable<CadText> texts) => new(Guid.NewGuid(), 0, "Model",
        texts.Select((text, i) => new CadOccurrence("text/" + i, new("text/" + i), [],
            CadStyle.Default with { Argb = 0x8fab3571 }, new([], Text: text))).ToImmutableArray(), []);
    private static SkiaTextLayoutEngine Engine()
    {
        var engine = new SkiaTextLayoutEngine(); engine.SetTypefaceResolver(style => Typeface(style)); return engine;
    }
    private static SKTypeface Typeface(RichTextStyle style, string family = "sans-serif") =>
        SKTypeface.FromFamilyName(family, style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    private sealed class ReferenceEngine : ITextLayoutEngine
    {
        public long Revision => 0;
        public RichTextLayoutResult Layout(CadText text) => ReferenceLayout(text);
    }
    private static RichTextLayoutResult ReferenceLayout(CadText text, string family = "sans-serif") =>
        RichTextLayout.Layout(text, (style, content) =>
        {
            using var face = Typeface(style, family); using var shaper = new SKShaper(face);
            using var font = new SKFont(face, (float)style.Height)
                { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
            var shape = shaper.Shape(content, font);
            var glyphs = shape.Codepoints.Select(value => checked((ushort)value)).ToArray();
            var rectangles = new SKRect[glyphs.Length]; font.GetGlyphWidths(glyphs, Span<float>.Empty, rectangles);
            var bounds = Bounds2.Empty;
            for (var i = 0; i < rectangles.Length; i++)
            {
                var rectangle = rectangles[i]; var point = shape.Points[i];
                if (!rectangle.IsEmpty) bounds = bounds.Include(new(rectangle.Left + point.X, rectangle.Top + point.Y))
                    .Include(new(rectangle.Right + point.X, rectangle.Bottom + point.Y));
            }
            var metrics = font.Metrics;
            return new(Math.Max(0, shape.Width), Math.Max(0, -metrics.Ascent), Math.Max(0, metrics.Descent), bounds);
        });
    private static void EqualLayout(RichTextLayoutResult expected, RichTextLayoutResult actual) =>
        Require(JsonSerializer.Serialize(expected, LayoutJson) == JsonSerializer.Serialize(actual, LayoutJson), "Local layout fields differ from the uncached oracle.");
    private static void Require(bool condition, string message = "Assertion failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
