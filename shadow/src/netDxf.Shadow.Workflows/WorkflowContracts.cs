using System.Collections.Immutable;
using System.Text.Json;

namespace netDxf.Shadow.Workflows;

/// <summary>A local baseline payload. Drivers must validate kind and checksum before executing any step.</summary>
public sealed record WorkflowBaseline(string Kind, string Data, string Sha256);
public sealed record WorkflowStep(string Operation, JsonElement Arguments, JsonElement Expected);
public sealed record WorkflowScenario(int Version, string Name, WorkflowBaseline Baseline, ImmutableArray<WorkflowStep> Steps);
public sealed record WorkflowLimits(int MaximumSteps = 1_000, int MaximumJsonBytes = 32 * 1024 * 1024,
    int MaximumArgumentBytes = 256 * 1024, int MaximumSeconds = 120, int MaximumDifferences = 50, double NumberTolerance = 1e-9);
public sealed record WorkflowDifference(string Path, string Expected, string Actual);
public sealed record WorkflowStepResult(int Index, string Operation, bool Passed, ImmutableArray<WorkflowDifference> Differences, string? Error = null);
public sealed record WorkflowRunReport(string Name, bool Passed, bool Complete, ImmutableArray<WorkflowStepResult> Steps, string? Error = null);

/// <summary>Replay drivers own an isolated application instance and execute only their supported typed operations.</summary>
public interface IWorkflowDriver : IAsyncDisposable
{
    ValueTask InitializeAsync(WorkflowBaseline baseline, CancellationToken cancellationToken = default);
    ValueTask<JsonElement> ExecuteAsync(string operation, JsonElement arguments, CancellationToken cancellationToken = default);
}

/// <summary>A bounded successful-operation recorder; it cannot silently drop a step when full.</summary>
public sealed class WorkflowRecorder
{
    private readonly List<WorkflowStep> steps = [];
    private readonly WorkflowLimits limits;
    private long bytes;
    public string Name { get; }
    public WorkflowBaseline Baseline { get; }
    public int Count => steps.Count;
    public WorkflowRecorder(string name, WorkflowBaseline baseline, WorkflowLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(baseline); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        this.limits = limits ?? new(); WorkflowJson.ValidateLimits(this.limits);
        if (name.Length > 200) throw new ArgumentException("Workflow names are limited to 200 characters.");
        Name = name; Baseline = baseline; bytes = JsonSerializer.SerializeToUtf8Bytes(baseline, WorkflowJson.Options).Length;
        if (bytes > this.limits.MaximumJsonBytes) throw new ArgumentException("Workflow baseline exceeds the document budget.");
    }
    public void Record(string operation, JsonElement arguments, JsonElement actual)
    {
        if (string.IsNullOrWhiteSpace(operation) || operation.Length > 100) throw new ArgumentException("Invalid workflow operation.");
        var argumentBytes = JsonSerializer.SerializeToUtf8Bytes(arguments).Length;
        var resultBytes = JsonSerializer.SerializeToUtf8Bytes(actual).Length;
        if (steps.Count >= limits.MaximumSteps || argumentBytes > limits.MaximumArgumentBytes || bytes + argumentBytes + resultBytes + 256 > limits.MaximumJsonBytes)
            throw new InvalidOperationException("The workflow recording budget was reached. The latest operation was not recorded.");
        steps.Add(new(operation, arguments.Clone(), actual.Clone())); bytes += argumentBytes + resultBytes + 256;
    }
    public WorkflowScenario Snapshot() => new(1, Name, Baseline, steps.ToImmutableArray());
}
