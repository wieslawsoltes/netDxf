using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using netDxf;
using netDxf.Blocks;
using netDxf.Entities;
using netDxf.Header;
using netDxf.Tables;

const string RegistryName = "SYNTHETIC_METADATA";
int entities = 10000, references = 64, iterations = 5, warmup = 2;
string encoding = "both";
for (int index = 0; index < args.Length; index += 2)
{
    if (index + 1 >= args.Length) throw new ArgumentException("Every option requires a value.");
    string value = args[index + 1];
    switch (args[index])
    {
        case "--entities": entities = int.Parse(value); break;
        case "--references": references = int.Parse(value); break;
        case "--iterations": iterations = int.Parse(value); break;
        case "--warmup": warmup = int.Parse(value); break;
        case "--encoding": encoding = value; break;
        default: throw new ArgumentException($"Unknown option: {args[index]}");
    }
}
if (entities is < 1 or > 100000 || references < 0 || references > entities
    || iterations is < 1 or > 100 || warmup is < 0 or > 100
    || encoding is not ("text" or "binary" or "both"))
    throw new ArgumentException("Require 1..100000 entities, 0..entities references, 1..100 iterations, 0..100 warmups, and text/binary/both encoding.");

Write(new { kind = "configuration", runtime = RuntimeInformation.FrameworkDescription,
    architecture = RuntimeInformation.ProcessArchitecture.ToString(), entities, references, iterations, warmup,
    tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "default" });

var fixture = CreateDocument(entities, references);
foreach (bool binary in encoding switch { "text" => new[] { false }, "binary" => new[] { true }, _ => new[] { false, true } })
{
    using var serialized = new MemoryStream();
    if (!fixture.Save(serialized, binary)) throw new InvalidOperationException("Synthetic serialization failed.");
    byte[] data = serialized.ToArray();
    string format = binary ? "binary" : "text";
    Write(new { kind = "fixture", encoding = format, bytes = data.Length });
    var samples = new List<Sample>();
    for (int iteration = -warmup; iteration < iterations; iteration++)
    {
        DxfDocument? loaded = null;
        using var stream = new MemoryStream(data, writable: false);
        var import = Measure("import", () => loaded = DxfDocument.Load(stream)
            ?? throw new InvalidOperationException("Synthetic import failed."));
        Validate(loaded!, entities, references);
        List<DxfObjectReference>? found = null;
        var metadata = Measure("metadata-references", () => found = loaded!.ApplicationRegistries.GetReferences(RegistryName));
        if (found == null || found.Sum(item => item.Uses) != 1
            || !ReferenceEquals(found.Single().Reference, loaded!.Entities.Inserts.Single().Attributes[0]))
            throw new InvalidOperationException("Metadata reference identity or uses changed.");
        if (iteration < 0) continue;
        foreach (var sample in new[] { import, metadata })
        {
            samples.Add(sample);
            Write(new { kind = "sample", encoding = format, iteration, phase = sample.Phase,
                milliseconds = sample.Milliseconds, allocatedBytes = sample.AllocatedBytes });
        }
    }
    foreach (var phase in samples.GroupBy(sample => sample.Phase))
        Write(new { kind = "median", encoding = format, phase = phase.Key,
            milliseconds = Median(phase.Select(sample => sample.Milliseconds)),
            allocatedBytes = Median(phase.Select(sample => (double)sample.AllocatedBytes)) });
}

static DxfDocument CreateDocument(int count, int linked)
{
    var document = new DxfDocument(DxfVersion.AutoCad2018);
    var block = new Block("SYNTHETIC_METADATA_BLOCK");
    block.AttributeDefinitions.Add(new AttributeDefinition("FIRST") { Value = "One", Height = 1 });
    block.AttributeDefinitions.Add(new AttributeDefinition("SECOND") { Value = "Two", Height = 1 });
    var insert = new Insert(block);
    document.Entities.Add(insert);
    var registry = document.ApplicationRegistries.Add(new ApplicationRegistry(RegistryName));
    insert.Attributes[0].XData.Add(new XData(registry)
        { XDataRecord = { new XDataRecord(XDataCode.String, "owned metadata") } });
    for (int index = 0; index < count; index++)
    {
        var line = new Line(new Vector3(index, 0, 0), new Vector3(index + 0.5, 1, 0));
        document.Entities.Add(line);
        if (index < linked)
        {
            // These attributes are retained by their INSERT rather than the
            // public handle index. Import must resolve both actual live records.
            line.PersistentReactors.Add(insert.Attributes[index % 2]);
            line.PersistentReactors.Add(insert.Attributes[(index + 1) % 2]);
        }
    }
    return document;
}

static void Validate(DxfDocument document, int count, int linked)
{
    var lines = document.Entities.Lines.ToArray();
    if (lines.Length != count) throw new InvalidOperationException("Entity count changed.");
    var targets = document.Entities.Inserts.Single().Attributes;
    var seen = new bool[count];
    int resolved = 0;
    foreach (var line in lines)
    {
        int index = (int)line.StartPoint.X;
        if (index < 0 || index >= count || seen[index]
            || line.StartPoint.X != index || line.StartPoint.Y != 0 || line.StartPoint.Z != 0
            || line.EndPoint.X != index + 0.5 || line.EndPoint.Y != 1 || line.EndPoint.Z != 0)
            throw new InvalidOperationException("Line coordinates changed.");
        seen[index] = true;
        if (index >= linked)
        {
            if (line.PersistentReactors.Count != 0) throw new InvalidOperationException("An unlinked line acquired reactors.");
            continue;
        }
        if (line.PersistentReactors.Count != 2
            || !ReferenceEquals(line.PersistentReactors[0], targets[index % 2])
            || !ReferenceEquals(line.PersistentReactors[1], targets[(index + 1) % 2]))
            throw new InvalidOperationException("Reactor order or target identity changed.");
        resolved++;
    }
    if (resolved != linked) throw new InvalidOperationException("Persistent references were lost.");
}

static Sample Measure(string phase, Action action)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long allocation = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    action();
    long stopped = Stopwatch.GetTimestamp();
    long allocated = GC.GetAllocatedBytesForCurrentThread() - allocation;
    return new Sample(phase, (stopped - started) * 1000.0 / Stopwatch.Frequency, allocated);
}

static double Median(IEnumerable<double> samples)
{
    double[] values = samples.Order().ToArray();
    int middle = values.Length / 2;
    return values.Length % 2 == 0 ? (values[middle - 1] + values[middle]) / 2 : values[middle];
}

static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value));
internal sealed record Sample(string Phase, double Milliseconds, long AllocatedBytes);
