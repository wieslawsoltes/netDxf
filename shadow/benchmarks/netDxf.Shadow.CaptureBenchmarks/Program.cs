using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow.NetDxf;

Options options;
try { options = Options.Parse(args); }
catch (ArgumentException error)
{
    Console.Error.WriteLine(error.Message);
    Console.Error.WriteLine(Options.Usage);
    return 2;
}
if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

Console.WriteLine(JsonSerializer.Serialize(new
{
    kind = "configuration",
    schemaVersion = 1,
    framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount,
    serverGc = GCSettings.IsServerGC,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    entities = options.Entities,
    iterations = options.Iterations,
    warmup = options.Warmup,
    encoding = options.Encoding
}, json));

foreach (var binary in options.Encoding switch
{
    "text" => new[] { false },
    "binary" => new[] { true },
    _ => new[] { false, true }
})
{
    var data = CreateDrawing(options.Entities, binary);
    for (var index = 0; index < options.Warmup; index++) _ = Measure(data, options.Entities);
    var results = new Measurement[options.Iterations];
    for (var index = 0; index < results.Length; index++)
    {
        results[index] = Measure(data, options.Entities);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            kind = "measurement", encoding = binary ? "binary" : "text", iteration = index,
            sourceBytes = data.Length, entities = options.Entities, results[index].Objects, results[index].CapturedEntities,
            results[index].Load, results[index].Capture, results[index].Refresh
        }, json));
    }
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        kind = "median", encoding = binary ? "binary" : "text", iterations = results.Length,
        load = Median(results.Select(static result => result.Load)),
        capture = Median(results.Select(static result => result.Capture)),
        refresh = Median(results.Select(static result => result.Refresh))
    }, json));
}
return 0;

static byte[] CreateDrawing(int count, bool binary)
{
    var document = new DxfDocument();
    for (var index = 0; index < count; index++)
        document.Entities.Add(new Line(new Vector2(index % 100, index / 100),
            new Vector2(index % 100 + .75, index / 100 + .75)));
    using var stream = new MemoryStream();
    if (!document.Save(stream, binary)) throw new InvalidOperationException("Synthetic drawing serialization failed.");
    return stream.ToArray();
}

static Measurement Measure(byte[] data, int entityCount)
{
    // Fixture generation and forced collection are outside the measured phases.
    // Each sample owns a new live document and session; no source/snapshot survives
    // in the measurement record, and allocation counters are thread-local.
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    using var stream = new MemoryStream(data, writable: false);
    var allocation = GC.GetAllocatedBytesForCurrentThread();
    var started = Stopwatch.GetTimestamp();
    var document = DxfDocument.Load(stream) ?? throw new InvalidOperationException("Synthetic drawing import failed.");
    var load = Finish(started, allocation);

    allocation = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
    var session = new ShadowSession(document);
    var capture = Finish(started, allocation);
    var snapshot = session.Current;

    allocation = GC.GetAllocatedBytesForCurrentThread(); started = Stopwatch.GetTimestamp();
    var refreshed = session.Refresh();
    var refresh = Finish(started, allocation);

    // The detached graph also contains layout-owned infrastructure such as the
    // paper-space viewport. Count the generated line entities independently.
    if (document.Entities.All.Count() != entityCount
        || snapshot.Objects.Values.Count(static value => value.TypeName == "netDxf.Entities.Line") != entityCount)
        throw new InvalidOperationException("Import or capture changed the synthetic entity count.");
    if (!snapshot.Diagnostics.IsEmpty)
        throw new InvalidOperationException("Synthetic capture produced unreadable public-state diagnostics.");
    if (!ReferenceEquals(snapshot, refreshed))
        throw new InvalidOperationException("An unchanged source did not retain the previous snapshot.");
    return new(snapshot.Objects.Count, snapshot.EntityIds.Count, load, capture, refresh);
}

static Phase Finish(long started, long allocation) => new(
    Stopwatch.GetElapsedTime(started).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - allocation);

static object Median(IEnumerable<Phase> phases)
{
    var values = phases.ToArray();
    static double Middle(double[] items)
    {
        Array.Sort(items);
        var midpoint = items.Length / 2;
        return items.Length % 2 == 0 ? (items[midpoint - 1] + items[midpoint]) / 2 : items[midpoint];
    }
    return new
    {
        elapsedMilliseconds = Middle(values.Select(static phase => phase.ElapsedMilliseconds).ToArray()),
        allocatedBytes = Middle(values.Select(static phase => (double)phase.AllocatedBytes).ToArray())
    };
}

internal readonly record struct Phase(double ElapsedMilliseconds, long AllocatedBytes);
internal readonly record struct Measurement(int Objects, int CapturedEntities, Phase Load, Phase Capture, Phase Refresh);
internal sealed record Options(int Entities, int Iterations, int Warmup, string Encoding, bool Help)
{
    internal const string Usage = "Usage: --entities 10000 --iterations 5 --warmup 2 --encoding text|binary|both [--help]";

    internal static Options Parse(string[] arguments)
    {
        var entities = 10000; var iterations = 5; var warmup = 2; var encoding = "both";
        for (var index = 0; index < arguments.Length; index++)
        {
            var option = arguments[index];
            if (option == "--help") return new(entities, iterations, warmup, encoding, true);
            if (++index >= arguments.Length) throw new ArgumentException($"Missing value for {option}.");
            var value = arguments[index];
            switch (option)
            {
                case "--entities": entities = Integer(value, 1, 100000, option); break;
                case "--iterations": iterations = Integer(value, 1, 100, option); break;
                case "--warmup": warmup = Integer(value, 0, 100, option); break;
                case "--encoding" when value is "text" or "binary" or "both": encoding = value; break;
                default: throw new ArgumentException($"Unknown option or invalid value: {option}.");
            }
        }
        return new(entities, iterations, warmup, encoding, false);
    }

    private static int Integer(string value, int minimum, int maximum, string option)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number >= minimum && number <= maximum) return number;
        throw new ArgumentException($"{option} requires an integer from {minimum} to {maximum}.");
    }
}
