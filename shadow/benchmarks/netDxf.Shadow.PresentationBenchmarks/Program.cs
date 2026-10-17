using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using netDxf;
using netDxf.Entities;
using netDxf.Shadow.Studio;
using netDxf.Tables;

Options options;
try { options = Options.Parse(args); }
catch (ArgumentException error) { Console.Error.WriteLine(error.Message); Console.Error.WriteLine(Options.Usage); return 2; }
if (options.Help) { Console.WriteLine(Options.Usage); return 0; }
var outputOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
Write(new
{
    kind = "configuration", schemaVersion = 1, framework = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription, architecture = RuntimeInformation.ProcessArchitecture.ToString(),
    processors = Environment.ProcessorCount, serverGc = GCSettings.IsServerGC,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default",
    concurrentGc = Environment.GetEnvironmentVariable("DOTNET_gcConcurrent") ?? "default",
    options.Entities, options.Iterations, options.Warmup
});

// Geometry, import, immutable capture and first scene construction are setup. This
// workload isolates the complete JSON response after presentation metadata is ready.
var drawing = new DxfDocument();
var layer = new Layer("Synthetic-É-日本語");
for (var index = 0; index < options.Entities; index++)
    drawing.Entities.Add(new Line(new Vector3(index * 1.25, index % 93 * 2.3456789, 0),
        new Vector3(index * 1.25 + Math.PI, index % 93 * 2.3456789 + Math.E, 0)) { Layer = index % 2 == 0 ? layer : Layer.Default });
using var stream = new MemoryStream();
if (!drawing.Save(stream)) throw new InvalidOperationException("Synthetic drawing serialization failed.");
stream.Position = 0;
using var studio = new Studio(false); studio.Load(stream);
var state = studio.CadState();
var reflection = new JsonSerializerOptions(JsonSerializerDefaults.Web);
var expected = JsonSerializer.SerializeToUtf8Bytes(state, reflection);
var expectedText = Encoding.UTF8.GetString(expected);
using (var document = JsonDocument.Parse(expected))
{
    var root = document.RootElement;
    if (root.GetProperty("itemCount").GetInt32() != options.Entities ||
        root.GetProperty("items").GetArrayLength() != Math.Min(5000, options.Entities) ||
        root.GetProperty("state").GetProperty("items").GetArrayLength() != Math.Min(2000, options.Entities))
        throw new InvalidOperationException("Synthetic presentation counts changed.");
}
Write(new { kind = "workload", entities = options.Entities, utf8Bytes = expected.Length, exactComparison = "UTF-8 byte equality" });

var variants = new List<Variant>
{
    new("reflection-string", () => JsonSerializer.Serialize(state, reflection)),
    new("reflection-utf8", () => JsonSerializer.SerializeToUtf8Bytes(state, reflection))
};
// Reflection permits copying this benchmark into an older checkout unchanged.
if (typeof(Studio).Assembly.GetType("netDxf.Shadow.Studio.StudioJson")?.GetMethod("CreateOptions") is { } factory)
{
    var generated = factory.CreateDelegate<Func<JsonSerializerOptions>>()();
    variants.Add(new("generated-string", () => JsonSerializer.Serialize(state, generated)));
    variants.Add(new("generated-utf8", () => JsonSerializer.SerializeToUtf8Bytes(state, generated)));
}
else Write(new { kind = "unsupported", phase = "generated" });

// Warm all variants together, then rotate their order. This avoids assigning all
// tier-promotion and initial buffer growth to whichever format happens to run first.
for (var warmup = 0; warmup < options.Warmup; warmup++)
    foreach (var variant in variants) Validate(variant.Run());
var samples = variants.ToDictionary(variant => variant.Name, _ => new List<Measurement>());
for (var iteration = 0; iteration < options.Iterations; iteration++)
{
    for (var offset = 0; offset < variants.Count; offset++)
    {
        var variant = variants[(iteration + offset) % variants.Count];
        var allocation = GC.GetAllocatedBytesForCurrentThread(); var started = Stopwatch.GetTimestamp();
        var result = variant.Run();
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocation;
        Validate(result);
        samples[variant.Name].Add(new(elapsed, bytes));
        Write(new { kind = "measurement", phase = variant.Name, iteration, milliseconds = elapsed, allocatedBytes = bytes });
    }
}
foreach (var (phase, measurements) in samples)
    Write(new { kind = "median", phase, iterations = measurements.Count,
        milliseconds = Median(measurements.Select(value => value.Milliseconds)),
        minimumMilliseconds = measurements.Min(value => value.Milliseconds),
        allocatedBytes = Median(measurements.Select(value => (double)value.AllocatedBytes)) });
return 0;

void Validate(object value)
{
    var same = value switch { string text => text == expectedText, byte[] bytes => bytes.AsSpan().SequenceEqual(expected), _ => false };
    if (!same) throw new InvalidOperationException("A presentation serializer changed the exact JSON output.");
}
void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, outputOptions));
static double Median(IEnumerable<double> samples)
{
    var values = samples.Order().ToArray(); var middle = values.Length / 2;
    return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
}
internal sealed record Variant(string Name, Func<object> Run);
internal readonly record struct Measurement(double Milliseconds, long AllocatedBytes);
internal sealed record Options(int Entities = 8000, int Iterations = 21, int Warmup = 15, bool Help = false)
{
    internal const string Usage = "Usage: --entities 8000 --iterations 21 --warmup 15";
    internal static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] is "--help" or "-h") return options with { Help = true };
            var name = args[index];
            if (++index >= args.Length || !int.TryParse(args[index], out var value)) throw new ArgumentException("Missing integer value for " + name);
            options = name switch
            {
                "--entities" when value is >= 1 and <= 100_000 => options with { Entities = value },
                "--iterations" when value is >= 1 and <= 1000 => options with { Iterations = value },
                "--warmup" when value is >= 0 and <= 100 => options with { Warmup = value },
                _ => throw new ArgumentException("Unknown argument or out-of-range value: " + name)
            };
        }
        return options;
    }
}
