using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using global::SkiaSharp;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;
using netDxf.Shadow.Rendering.SkiaSharp;

Options options;
try { options = Options.Parse(args); }
catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Console.Error.WriteLine(Options.Usage); return 2; }
if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
Write(new
{
    kind = "configuration", schemaVersion = 1, framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount, serverGc = GCSettings.IsServerGC,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    concurrentGc = Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") ?? "default",
    options.Case, options.Lines, options.Texts, options.Points, options.Iterations, options.Warmup,
    width = 1200, height = 900, fontFamily = "sans-serif"
});
if (options.Case is "all" or "lines")
{
    var source = LineScene(options.Lines);
    Measure("compile-connected-lines", () => RenderCompiler.Compile(source), scene =>
    {
        Require(scene.Diagnostics.IsEmpty && scene.Items.Length == source.Occurrences.Length);
        Require(scene.Items.Sum(item => item.Contours.Sum(contour => (long)contour.Points.Length)) == options.Lines + source.Occurrences.Length);
    }, new { segments = options.Lines, occurrences = source.Occurrences.Length });
}
if (options.Case is "all" or "text")
{
    var source = new CadScene(Guid.NewGuid(), 0, "Model", Enumerable.Range(0, options.Texts).Select(i =>
    {
        var text = new CadText("Valve " + i % 80 + " supply water", new(i % 30 * 16, i / 30 * 5),
            Vec3.UnitX, Vec3.UnitY, 2, "sans-serif", "BaselineLeft", false, 0);
        return new CadOccurrence("label/" + i, new("label/" + i), [], CadStyle.Default, new([], Text: text));
    }).ToImmutableArray(), []);
    Measure("cold-text-layout", () =>
    {
        using var engine = new SkiaTextLayoutEngine();
        return RenderCompiler.Compile(source, textLayout: engine);
    }, scene => Require(scene.Items.Length == options.Texts && scene.Diagnostics.IsEmpty), new { labels = options.Texts, distinctNumbers = Math.Min(80, options.Texts) });
    using var textEngine = new SkiaTextLayoutEngine();
    var scene = RenderCompiler.Compile(source, textLayout: textEngine);
    Draw("text-camera-redraw", scene, textEngine, new { labels = options.Texts });
    Raster("text", scene, textEngine, new { labels = options.Texts });
}
if (options.Case is "all" or "paths")
{
    var scene = PathScene(options.Points);
    using var textEngine = new SkiaTextLayoutEngine();
    Draw("dense-path-camera-redraw", scene, textEngine, new { points = options.Points, items = scene.Items.Length });
    using var svg = new SvgRenderBackend();
    var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 1200, 900));
    Measure("dense-path-svg", () => svg.Render(frame), image => Require(image.ContentType == "image/svg+xml" && image.Bytes.Length > 0),
        new { points = options.Points, items = scene.Items.Length });
    Raster("dense-path", scene, textEngine, new { points = options.Points, items = scene.Items.Length });
}
return 0;

void Draw(string phase, RenderScene scene, SkiaTextLayoutEngine textEngine, object counts)
{
    using var backend = new SkiaRenderBackend(textLayout: textEngine);
    using var surface = SKSurface.Create(new SKImageInfo(1200, 900, SKColorType.Rgba8888, SKAlphaType.Premul));
    var fit = Viewport.Fit(scene.Bounds, 1200, 900);
    var index = 0;
    Measure(phase, () =>
    {
        var step = index++;
        var viewport = fit with { Center = fit.Center + new Vec3(step * .003, step * .002), PixelsPerUnit = fit.PixelsPerUnit * (1 + step * .0001) };
        backend.Draw(surface.Canvas, new(scene, viewport)); surface.Canvas.Flush();
        return scene.Items.Length;
    }, count => Require(count == scene.Items.Length), counts);
}
void Raster(string prefix, RenderScene scene, SkiaTextLayoutEngine textEngine, object counts)
{
    using var backend = new SkiaRenderBackend(textLayout: textEngine);
    var frame = new RenderFrame(scene, Viewport.Fit(scene.Bounds, 1200, 900));
    Measure(prefix + "-png", () => backend.Render(frame), image => Require(image.ContentType == "image/png" && image.Bytes.Length > 0), counts);
    // Reflection keeps the same benchmark buildable when copied into an older checkout.
    if (typeof(SkiaRenderBackend).GetMethod("RenderPixels") is { } method)
    {
        var render = method.CreateDelegate<Func<RenderFrame, CancellationToken, RenderImage>>(backend);
        Measure(prefix + "-rgba", () => render(frame, default), image =>
            Require(image.ContentType == "application/x-shadow-rgba" && image.Bytes.Length == 1200 * 900 * 4), counts);
    }
    else Write(new { kind = "unsupported", phase = prefix + "-rgba" });
}
void Measure<T>(string phase, Func<T> operation, Action<T> validate, object counts)
{
    for (var i = 0; i < options.Warmup; i++) validate(operation());
    var measurements = new Measurement[options.Iterations];
    for (var i = 0; i < measurements.Length; i++)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var result = operation();
        var milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        validate(result);
        measurements[i] = new(milliseconds, allocated);
        Write(new { kind = "measurement", phase, iteration = i, counts, milliseconds, allocatedBytes = allocated });
    }
    Write(new { kind = "median", phase, counts, iterations = measurements.Length,
        milliseconds = Median(measurements.Select(m => m.Milliseconds)),
        minimumMilliseconds = measurements.Min(m => m.Milliseconds),
        allocatedBytes = Median(measurements.Select(m => (double)m.AllocatedBytes)) });
}
static double Median(IEnumerable<double> samples)
{
    var values = samples.Order().ToArray();
    var middle = values.Length / 2;
    return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
}
void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, json));
static void Require(bool condition) { if (!condition) throw new InvalidOperationException("Synthetic rendering workload verification failed."); }
static CadScene LineScene(int lines)
{
    var occurrences = ImmutableArray.CreateBuilder<CadOccurrence>();
    for (var start = 0; start < lines; start += 100)
    {
        var row = start / 100;
        var curves = Enumerable.Range(0, Math.Min(100, lines - start))
            .Select(i => (Curve3)new LineCurve(new(i, row), new(i + 1, row))).ToImmutableArray();
        occurrences.Add(new("line/" + row, new("line/" + row), [], CadStyle.Default, new([new(curves)])));
    }
    return new(Guid.NewGuid(), 0, "Model", occurrences.ToImmutable(), []);
}
static RenderScene PathScene(int points)
{
    var items = ImmutableArray.CreateBuilder<RenderItem>();
    var bounds = Bounds2.Empty;
    for (var start = 0; start < points; start += 5000)
    {
        var row = start / 5000;
        var vertices = Enumerable.Range(0, Math.Min(5000, points - start))
            .Select(i => new Vec3(i * .05, row * 2 + Math.Sin(i * .01))).ToImmutableArray();
        var box = Bounds2.Empty; foreach (var point in vertices) box = box.Include(point);
        items.Add(new("path/" + row, new("path/" + row), [], CadStyle.Default, [new(vertices, false)], false, null, null, box));
        bounds = bounds.Union(box);
    }
    var immutable = items.ToImmutable();
    return new(Guid.NewGuid(), 0, "Model", immutable, bounds, new SpatialIndex(immutable.Select(item => item.Bounds)), []);
}
internal sealed record Measurement(double Milliseconds, long AllocatedBytes);
internal sealed record Options(string Case = "all", int Lines = 200_000, int Texts = 1500, int Points = 500_000, int Iterations = 5, int Warmup = 2, bool Help = false)
{
    internal const string Usage = "Usage: --case all|lines|text|paths --lines 200000 --texts 1500 --points 500000 --iterations 5 --warmup 2";
    internal static Options Parse(string[] args)
    {
        var options = new Options();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--help" or "-h") return options with { Help = true };
            var key = args[i];
            if (++i >= args.Length) throw new ArgumentException("Missing value for " + key);
            if (key == "--case")
            {
                if (args[i] is not ("all" or "lines" or "text" or "paths")) throw new ArgumentException("Unknown rendering workload.");
                options = options with { Case = args[i] }; continue;
            }
            if (!int.TryParse(args[i], out var value)) throw new ArgumentException("Invalid integer for " + key);
            options = key switch
            {
                "--lines" when value is >= 1 and <= 1_000_000 => options with { Lines = value },
                "--texts" when value is >= 1 and <= 20_000 => options with { Texts = value },
                "--points" when value is >= 2 and <= 2_000_000 => options with { Points = value },
                "--iterations" when value is >= 1 and <= 100 => options with { Iterations = value },
                "--warmup" when value is >= 0 and <= 20 => options with { Warmup = value },
                _ => throw new ArgumentException("Unknown option or out-of-range value for " + key)
            };
        }
        return options;
    }
}
