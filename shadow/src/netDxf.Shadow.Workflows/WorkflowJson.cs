using System.Text.Json;

namespace netDxf.Shadow.Workflows;

public static class WorkflowJson
{
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { MaxDepth = 64 };
    public static byte[] Write(WorkflowScenario scenario, WorkflowLimits? limits = null)
    {
        limits ??= new(); Validate(scenario, limits); var bytes = JsonSerializer.SerializeToUtf8Bytes(scenario, Options);
        if (bytes.Length > limits.MaximumJsonBytes) throw new ArgumentException("Workflow JSON exceeds the byte budget."); return bytes;
    }
    public static WorkflowScenario Read(ReadOnlySpan<byte> bytes, WorkflowLimits? limits = null)
    {
        limits ??= new(); ValidateLimits(limits);
        if (bytes.Length > limits.MaximumJsonBytes) throw new ArgumentException("Workflow JSON exceeds the byte budget.");
        var scenario = JsonSerializer.Deserialize<WorkflowScenario>(bytes, Options) ?? throw new ArgumentException("Missing workflow."); Validate(scenario, limits); return scenario;
    }
    internal static void Validate(WorkflowScenario scenario, WorkflowLimits limits)
    {
        ArgumentNullException.ThrowIfNull(scenario); ValidateLimits(limits);
        if (scenario.Version != 1 || string.IsNullOrWhiteSpace(scenario.Name) || scenario.Name.Length > 200 || scenario.Baseline is null
            || scenario.Steps.IsDefault || scenario.Steps.Length > limits.MaximumSteps) throw new ArgumentException("Invalid or unsupported workflow document.");
        foreach (var step in scenario.Steps)
            if (step is null || string.IsNullOrWhiteSpace(step.Operation) || step.Operation.Length > 100 || step.Arguments.ValueKind == JsonValueKind.Undefined
                || step.Expected.ValueKind == JsonValueKind.Undefined || JsonSerializer.SerializeToUtf8Bytes(step.Arguments).Length > limits.MaximumArgumentBytes)
                throw new ArgumentException("Workflow step is invalid or exceeds the argument budget.");
    }
    internal static void ValidateLimits(WorkflowLimits limits)
    {
        if (limits.MaximumSteps is < 1 or > 10_000 || limits.MaximumJsonBytes is < 1024 or > 64 * 1024 * 1024
            || limits.MaximumArgumentBytes is < 1 or > 4 * 1024 * 1024 || limits.MaximumSeconds is < 1 or > 600
            || limits.MaximumDifferences is < 1 or > 1000 || !double.IsFinite(limits.NumberTolerance) || limits.NumberTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(limits));
    }
}
