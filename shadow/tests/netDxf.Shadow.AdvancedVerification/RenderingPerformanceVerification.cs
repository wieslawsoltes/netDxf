using System.Collections.Immutable;
using System.Runtime.InteropServices;
using global::SkiaSharp;
using global::SkiaSharp.HarfBuzz;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;

internal static class RenderingPerformanceVerification
{
    internal static void Register(List<(string Name, Action Run)> tests)
    {
        tests.Add(("render-performance/connected line compilation preserves points and pattern restarts", () =>
        {
            var geometry = new CadGeometry([new([
                new LineCurve(new(0, 0), new(2, 0)), new LineCurve(new(2, 0), new(2, 0)),
                new LineCurve(new(2, 0), new(4, 0))]) { RestartLinetypeAtCurves = true }]);
            var scene = Compile([geometry]);
            var contour = scene.Items.Single().Contours.Single();
            Require(contour.Points.AsSpan().SequenceEqual(new Vec3[] { new(0, 0), new(2, 0), new(2, 0), new(4, 0) }));
            Require(contour.PatternBreaks.AsSpan().SequenceEqual(new[] { 1, 2 }));
            Require(scene.Bounds == new Bounds2(0, 0, 4, 0) && scene.Diagnostics.IsEmpty);
            foreach (var sampling in new[] { new SamplingOptions(0), new SamplingOptions(MaximumPoints: 1), new SamplingOptions(MaximumDepth: 0) })
            {
                var rejected = RenderCompiler.Compile(Source([geometry]), sampling);
                Require(rejected.Items.IsEmpty && rejected.Diagnostics.Any(d => d.Code == "render.geometry" && d.Severity == DiagnosticSeverity.Error));
            }
        }));
        tests.Add(("render-performance/mixed curve samples retain exact values and disconnected boundaries", () =>
        {
            Curve3[] curves = [new LineCurve(new(-7, 4, 1), new(-5, 4, 1)),
                new EllipseCurve(new(2, 3), new(4, 1), new(-2, 5), .3, 4),
                new NurbsCurve([new(8, 0), new(9, 7), new(15, -3), new(20, 0)], [1, .8, 1.5, 1], [0, 0, 0, 0, 1, 1, 1, 1], 3),
                new LineCurve(new(31, -4), new(32, -4))];
            var expected = curves.SelectMany(c => CurveSampler.Sample(c)).ToArray();
            var scene = Compile([new([new(curves.ToImmutableArray())])]);
            Require(scene.Diagnostics.IsEmpty);
            Require(scene.Items.Single().Contours.Single().Points.AsSpan().SequenceEqual(expected));
        }));
        foreach (var alpha in new uint[] { 0xff, 0x91 })
        {
            var opacity = alpha;
            tests.Add(($"render-performance/batched native paths preserve scalar raster output alpha={opacity}", () =>
            {
                var scene = PathScene(opacity);
                using var backend = new SkiaRenderBackend();
                foreach (var camera in new[] { new Viewport(new(3, 2), 15.37, 480, 320), new Viewport(new(-2.1, 4.3), 9.83, 480, 320) })
                {
                    foreach (var selection in new ImmutableHashSet<string>?[] { null, ImmutableHashSet.Create("item/0", "item/2") })
                    {
                        var frame = new RenderFrame(scene, camera, selection, Background: 0x31596317, SelectionColor: 0x87fe3471);
                        var actual = backend.RenderPixels(frame).Bytes;
                        var expected = ReferencePaths(frame);
                        Require(actual.AsSpan().SequenceEqual(expected), "Batched paths changed crossing, joined, filled, clipped or selected pixels.");
                    }
                }
            }));
        }
        tests.Add(("render-performance/batched paths retain double camera precision at large coordinates", () =>
        {
            const double offset = 1e12;
            var points = Enumerable.Range(0, 5000).Select(i => new Vec3(offset + i * .003, offset + Math.Sin(i * .03))).ToImmutableArray();
            var contour = new RenderContour(points, false);
            var box = new Bounds2(offset, offset - 1, offset + 15, offset + 1);
            var item = new RenderItem("large", new("large"), [], CadStyle.Default, [contour], false, null, null, box);
            var scene = new RenderScene(Guid.NewGuid(), 0, "Model", [item], box, new SpatialIndex([box]), []);
            var frame = new RenderFrame(scene, new(new(offset + 7, offset), 23.137, 480, 320));
            using var backend = new SkiaRenderBackend();
            Require(backend.RenderPixels(frame).Bytes.AsSpan().SequenceEqual(ReferencePaths(frame)));
        }));
        tests.Add(("render-performance/repeated labels share shaping across layouts and camera changes", () =>
        {
            using var engine = TextEngine();
            var scene = Compile(Enumerable.Range(0, 120).Select(i => TextGeometry("Valve supply water", new(i % 12 * 20, i / 12 * 8))), engine);
            var before = engine.CacheStatistics;
            Require(before.ShapeCompilations == 4 && before.ShapeHits > 100, "Repeated words or spaces were reshaped.");
            using var backend = new SkiaRenderBackend(textLayout: engine);
            var fit = Viewport.Fit(scene.Bounds, 480, 320);
            for (var i = 0; i < 6; i++)
                backend.RenderPixels(new(scene, fit with { Center = fit.Center + new Vec3(i * .1, i * -.2) }));
            Require(engine.CacheStatistics.ShapeCompilations == before.ShapeCompilations, "A camera-only redraw reshaped text.");
            Require(engine.CacheStatistics.ShapeHits > before.ShapeHits);
        }));
        tests.Add(("render-performance/retained glyphs match independent HarfBuzz drawing", () =>
        {
            using var engine = TextEngine();
            var text = new CadText("ffi AV e\u0301 Å\u00df\\Pمرحبا שלום\\P{\\Ftest|b1|i1;Bold} {\\W1.4;wide} {\\Q19;shear} \\Lrule\\l \\S3/8;",
                new(4, 8), new(1, .2), new(.15, 1), 3.5, "test", "TopLeft", true, 130);
            var scene = Compile([new([], Text: text)], engine);
            using var backend = new SkiaRenderBackend(textLayout: engine);
            foreach (var selected in new[] { false, true })
            {
                var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 640, 360),
                    selected ? ImmutableHashSet.Create(scene.Items[0].Key) : null, Background: 0x23ffffff, SelectionColor: 0x965badef);
                var expected = ReferenceText(frame);
                Require(backend.RenderPixels(frame).Bytes.AsSpan().SequenceEqual(expected), "Retained glyphs changed shaped text or decorations.");
            }
        }));
        tests.Add(("render-performance/font invalidation clears native shapes and refreshes pictures", () =>
        {
            using var engine = TextEngine();
            var text = TextGeometry("WMW iii 123", Vec3.Zero);
            var scene = Compile([text], engine);
            var oldLayout = scene.Items[0].TextLayout;
            using var backend = new SkiaRenderBackend(textLayout: engine);
            var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 480, 320));
            backend.RenderPixels(frame);
            var version = engine.Revision;
            engine.InvalidateFonts();
            Require(engine.Revision == version + 1 && engine.CacheStatistics.CachedRuns == 0 && engine.CacheStatistics.CachedUnits == 0);
            var refreshed = Compile([text], engine);
            Require(!ReferenceEquals(oldLayout, refreshed.Items[0].TextLayout));
            using var independent = TextEngine();
            using var independentBackend = new SkiaRenderBackend(textLayout: independent);
            var oracle = Compile([text], independent);
            Require(backend.RenderPixels(frame with { Scene = refreshed }).Bytes.AsSpan().SequenceEqual(
                independentBackend.RenderPixels(frame with { Scene = oracle }).Bytes));
        }));
        tests.Add(("render-performance/font resolver replacement and native lifetimes remain isolated", () =>
        {
            using var shared = TextEngine();
            using var unrelated = TextEngine();
            var source = new[] { TextGeometry("Wide WMW label", Vec3.Zero) };
            var scene = Compile(source, shared);
            Compile(source, unrelated);
            var unrelatedVersion = unrelated.Revision;
            var unrelatedShapes = unrelated.CacheStatistics.ShapeCompilations;
            var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 320, 180));
            using var first = new SkiaRenderBackend(textLayout: shared);
            using var second = new SkiaRenderBackend(textLayout: shared);
            var renders = Task.WhenAll(Task.Run(() => first.RenderPixels(frame).Bytes), Task.Run(() => second.RenderPixels(frame).Bytes)).GetAwaiter().GetResult();
            Require(renders[0].AsSpan().SequenceEqual(renders[1]));
            first.Dispose();
            Require(second.RenderPixels(frame).Bytes.AsSpan().SequenceEqual(renders[0]));
            shared.SetTypefaceResolver(style => Typeface(style, "monospace"));
            Require(shared.CacheStatistics.CachedRuns == 0);
            var next = Compile(source, shared);
            using var oracleEngine = new SkiaTextLayoutEngine();
            oracleEngine.SetTypefaceResolver(style => Typeface(style, "monospace"));
            using var oracleBackend = new SkiaRenderBackend(textLayout: oracleEngine);
            var oracle = Compile(source, oracleEngine);
            Require(second.RenderPixels(frame with { Scene = next }).Bytes.AsSpan().SequenceEqual(oracleBackend.RenderPixels(frame with { Scene = oracle }).Bytes));
            Require(unrelated.Revision == unrelatedVersion && unrelated.CacheStatistics.ShapeCompilations == unrelatedShapes);
        }));
        tests.Add(("render-performance/native shaping cache evicts without changing layout", () =>
        {
            using var engine = TextEngine();
            var initial = TextGeometry("retain-me", Vec3.Zero).Text!;
            var expected = engine.Layout(initial);
            for (var i = 0; i < 4300; i++) engine.Layout(TextGeometry("label" + i, Vec3.Zero).Text!);
            var before = engine.CacheStatistics;
            Require(before.CachedRuns <= 4096 && before.CachedUnits <= 262_144);
            var actual = engine.Layout(initial with { Origin = new(5, 6) });
            Require(engine.CacheStatistics.ShapeCompilations > before.ShapeCompilations, "The oldest run was not evicted.");
            Require(expected.Bounds == actual.Bounds && expected.Runs.SequenceEqual(actual.Runs));
        }));
        tests.Add(("render-performance/raw RGBA matches decoded PNG including transparent antialiasing", () =>
        {
            using var engine = TextEngine();
            var source = PathScene(0x79);
            using var backend = new SkiaRenderBackend(textLayout: engine);
            foreach (var background in new uint[] { 0, 0x3f793120, 0xffffffff })
            {
                var frame = new RenderFrame(source, new(new(1, 2), 10.731, 480, 320), ImmutableHashSet.Create("item/1"), background, 0x976521fe);
                var encoded = backend.Render(frame);
                var raw = backend.RenderPixels(frame);
                Require(encoded.ContentType == "image/png" && raw.ContentType == "application/x-shadow-rgba");
                Require(raw.Bytes.Length == frame.Viewport.Width * frame.Viewport.Height * 4);
                Require(raw.Bytes.AsSpan().SequenceEqual(DecodeRgba(encoded.Bytes)), "Raw and PNG pixel transports differ.");
            }
            var texts = Compile([TextGeometry("Antialiased ffi e\u0301 مرحبا", new(4, 6))], engine);
            var textFrame = new RenderFrame(texts, Viewport.Fit(texts.Bounds, 480, 320), Background: 0);
            Require(backend.RenderPixels(textFrame).Bytes.AsSpan().SequenceEqual(DecodeRgba(backend.Render(textFrame).Bytes)));
        }));
        tests.Add(("render-performance/raster host honors backend changes cancellation and disposal", () =>
        {
            using var host = new RenderHost(new Dictionary<string, Func<IRenderBackend>>
                { ["skia"] = () => new SkiaRenderBackend(), ["svg"] = () => new SvgRenderBackend() }, "skia");
            var scene = PathScene(0xff);
            var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 160, 120));
            Throws<OperationCanceledException>(() => host.RenderPixels(frame, new(true)));
            Require(host.RenderPixels(frame).Bytes.Length == 160 * 120 * 4);
            host.Swap("svg"); Throws<NotSupportedException>(() => host.RenderPixels(frame));
            Require(host.Render(frame).ContentType == "image/svg+xml");
            host.Swap("skia"); Require(host.RenderPixels(frame).ContentType == "application/x-shadow-rgba");
            host.Dispose(); Throws<ObjectDisposedException>(() => host.RenderPixels(frame));
        }));
        tests.Add(("render-performance/image resolver reentrancy preserves leased paint state", () =>
        {
            var image = new CadImage("unresolved-raster", [new(-4, -3), new(4, -3), new(4, 3), new(-4, 3)]);
            var scene = Compile([new([], Image: image)]);
            var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 320, 240));
            var nested = PathScene(0x75);
            SkiaRenderBackend? backend = null;
            var resolver = new CallbackImageResolver(() => backend!.RenderPixels(new(nested, Viewport.Fit(nested.Bounds, 80, 60))));
            using var actual = backend = new SkiaRenderBackend(resolver);
            using var oracle = new SkiaRenderBackend();
            Require(actual.RenderPixels(frame).Bytes.AsSpan().SequenceEqual(oracle.RenderPixels(frame).Bytes));
        }));
    }

    private static CadGeometry TextGeometry(string text, Vec3 position) =>
        new([], Text: new(text, position, Vec3.UnitX, Vec3.UnitY, 2, "test", "BaselineLeft", false, 0));
    private sealed class CallbackImageResolver(Action callback) : ISkiaImageResolver
    {
        public SKImage? Resolve(string reference) { callback(); return null; }
    }
    private static CadScene Source(IEnumerable<CadGeometry> geometries) => new(Guid.NewGuid(), 0, "Model",
        geometries.Select((geometry, i) => new CadOccurrence("item/" + i, new("item/" + i), [], CadStyle.Default, geometry)).ToImmutableArray(), []);
    private static RenderScene Compile(IEnumerable<CadGeometry> geometries, ITextLayoutEngine? text = null) =>
        RenderCompiler.Compile(Source(geometries), textLayout: text);
    private static SkiaTextLayoutEngine TextEngine()
    {
        var engine = new SkiaTextLayoutEngine(); engine.SetTypefaceResolver(style => Typeface(style)); return engine;
    }
    private static SKTypeface Typeface(RichTextStyle style, string family = "sans-serif") =>
        SKTypeface.FromFamilyName(family, style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal, style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    private static RenderScene PathScene(uint alpha)
    {
        RenderContour[] contours = [new([new(-8, -5), new(8, 5), new(-8, 5), new(8, -5)], false),
            new([new(-8, 0), new(8, 0)], false), new([new(0, -6), new(0, 6)], false),
            new([new(-4, -3), new(4, -3), new(4, 3), new(-4, 3)], true), new([], true),
            new([new(2, 2), new(2, 2)], false)];
        var bounds = new Bounds2(-8, -6, 8, 6);
        var items = Enumerable.Range(0, 3).Select(i => new RenderItem("item/" + i, new("item/" + i), [],
            CadStyle.Default with { Argb = alpha << 24 | (uint)(0x13b8a9 + i * 0x452317), Lineweight = .47 + i * .23 },
            contours.ToImmutableArray(), i == 2, null, null, bounds)
        {
            Regions = i == 1 ? [new([contours[3]], CadFillRule.NonZero, null) { Argb = 0x834bf271 }] : [],
            Strokes = i == 0 ? [new([new([new(-7, -1), new(7, -1)], false)], 0x759142fc)] : [],
            Clips = i == 1 ? [new([new([new(-6, -4), new(6, -4), new(6, 4), new(-6, 4)], true)], new Bounds2(-6, -4, 6, 4))] : []
        }).ToImmutableArray();
        return new(Guid.NewGuid(), 0, "Model", items, bounds, new SpatialIndex(items.Select(i => i.Bounds)), []);
    }
    private static byte[] ReferencePaths(RenderFrame frame)
    {
        using var surface = Surface(frame);
        var canvas = surface.Canvas; canvas.ClipRect(new(0, 0, frame.Viewport.Width, frame.Viewport.Height)); canvas.Clear(new(frame.Background));
        foreach (var item in frame.VisibleItems()) Draw(item, item.Style.Argb, false);
        if (frame.Selection is { Count: > 0 }) foreach (var item in frame.VisibleItems())
            if (frame.Selection.Contains(item.Key)) Draw(item, frame.SelectionColor, true);
        return ReadRgba(surface);
        void Draw(RenderItem item, uint color, bool selected)
        {
            canvas.Save();
            try
            {
                foreach (var clip in item.Clips) { using var path = Path(clip.Contours, CadFillRule.EvenOdd); canvas.ClipPath(path, SKClipOperation.Intersect, true); }
                using var paint = new SKPaint { IsAntialias = true, Color = new(color), Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)Math.Max(1, item.Style.Lineweight * 96 / 25.4), StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round };
                foreach (var region in item.Regions)
                {
                    using var path = Path(region.Contours, region.FillRule); paint.Style = SKPaintStyle.Fill;
                    paint.Color = new(selected ? color : RenderPaint.Resolve(region.Argb, color)); canvas.DrawPath(path, paint);
                }
                paint.Color = new(color);
                if (item.Filled) { using var path = Path(item.Contours, CadFillRule.EvenOdd); paint.Style = SKPaintStyle.Fill; canvas.DrawPath(path, paint); }
                paint.Style = SKPaintStyle.Stroke;
                foreach (var group in item.Strokes)
                {
                    using var path = Path(group.Contours, CadFillRule.EvenOdd); paint.Color = new(selected ? color : RenderPaint.Resolve(group.Argb, color)); canvas.DrawPath(path, paint);
                }
                paint.Color = new(color); var contours = StrokePattern.GetContours(item);
                using (var path = Path(contours, CadFillRule.EvenOdd)) canvas.DrawPath(path, paint);
                foreach (var contour in contours) if (contour.Points.Length == 2 && contour.Points[0] == contour.Points[1])
                {
                    var point = frame.Viewport.ToScreen(contour.Points[0]); paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle((float)point.X, (float)point.Y, Math.Max(.5f, paint.StrokeWidth / 2), paint); paint.Style = SKPaintStyle.Stroke;
                }
            }
            finally { canvas.Restore(); }
        }
        SKPath Path(IEnumerable<RenderContour> contours, CadFillRule rule)
        {
            var path = new SKPath { FillType = rule == CadFillRule.NonZero ? SKPathFillType.Winding : SKPathFillType.EvenOdd };
            foreach (var contour in contours)
            {
                for (var i = 0; i < contour.Points.Length; i++)
                {
                    var p = frame.Viewport.ToScreen(contour.Points[i]);
                    if (i == 0) path.MoveTo((float)p.X, (float)p.Y); else path.LineTo((float)p.X, (float)p.Y);
                }
                if (contour.Closed) path.Close();
            }
            return path;
        }
    }
    private static byte[] ReferenceText(RenderFrame frame)
    {
        using var surface = Surface(frame); var canvas = surface.Canvas; canvas.Clear(new(frame.Background));
        foreach (var item in frame.VisibleItems()) Draw(item, item.Style.Argb, false);
        if (frame.Selection is { Count: > 0 }) foreach (var item in frame.VisibleItems())
            if (frame.Selection.Contains(item.Key)) Draw(item, frame.SelectionColor, true);
        return ReadRgba(surface);
        void Draw(RenderItem item, uint color, bool selected)
        {
            var text = item.Text!; var layout = item.TextLayout!; var origin = frame.Viewport.ToScreen(text.Origin); var scale = frame.Viewport.PixelsPerUnit;
            var matrix = new SKMatrix { ScaleX = (float)(text.XAxis.X * scale), SkewY = (float)(-text.XAxis.Y * scale),
                SkewX = (float)(-text.YAxis.X * scale), ScaleY = (float)(text.YAxis.Y * scale), TransX = (float)origin.X, TransY = (float)origin.Y, Persp2 = 1 };
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.Save();
            try
            {
                canvas.Concat(in matrix);
                foreach (var run in layout.Runs)
                {
                    using var face = Typeface(run.Style); using var shaper = new SKShaper(face);
                    using var font = new SKFont(face, (float)run.Style.Height) { LinearMetrics = true, Subpixel = true, Hinting = SKFontHinting.None, Edging = SKFontEdging.Antialias };
                    paint.Color = new(Resolve(run.Style.Color));
                    var local = new SKMatrix { ScaleX = (float)run.Style.WidthFactor, SkewX = (float)(-Math.Tan(run.Style.ObliqueDegrees * Math.PI / 180)), ScaleY = 1, TransX = (float)run.X, TransY = (float)run.Y, Persp2 = 1 };
                    canvas.Save();
                    try { canvas.Concat(in local); canvas.DrawShapedText(shaper, run.Text, 0, 0, SKTextAlign.Left, font, paint); }
                    finally { canvas.Restore(); }
                }
                paint.Style = SKPaintStyle.Stroke;
                foreach (var rule in layout.Rules)
                {
                    paint.Color = new(Resolve(rule.Color)); paint.StrokeWidth = (float)rule.Thickness;
                    canvas.DrawLine((float)rule.X1, (float)rule.Y1, (float)rule.X2, (float)rule.Y2, paint);
                }
            }
            finally { canvas.Restore(); }
            uint Resolve(uint? authored) => selected || authored is null ? color :
                (authored.Value & 0xffffffu) | ((authored.Value >> 24) * (color >> 24) / 255) << 24;
        }
    }
    private static SKSurface Surface(RenderFrame frame) => SKSurface.Create(new SKImageInfo(frame.Viewport.Width, frame.Viewport.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
    private static byte[] ReadRgba(SKSurface surface)
    {
        using var image = surface.Snapshot(); return ReadRgba(image);
    }
    private static byte[] DecodeRgba(byte[] encoded)
    {
        using var image = SKImage.FromEncodedData(encoded); return ReadRgba(image);
    }
    private static byte[] ReadRgba(SKImage image)
    {
        var info = new SKImageInfo(image.Width, image.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[info.Width * info.Height * 4]; var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try { Require(image.ReadPixels(info, pinned.AddrOfPinnedObject(), info.Width * 4, 0, 0)); }
        finally { pinned.Free(); }
        return bytes;
    }
    private static void Require(bool condition, string message = "Assertion failed.") { if (!condition) throw new InvalidOperationException(message); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }
}
