using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Text.Json;
using netDxf.Shadow.Editor;
using netDxf.Shadow.Mcp;

namespace netDxf.Shadow.Agents.Evaluation;

public sealed record EvaluationCheck(string Name, bool Passed);
/// <summary>A versioned task and independent grader. Change GraderVersion when expected behavior changes.</summary>
public sealed record EvaluationCase(string Id, string Description, string Prompt, Func<DxfDocument> Seed,
    Func<DxfDocument> Expected, Func<DxfDocument, IAgentWorkspace, IEnumerable<EvaluationCheck>> Validate, string GraderVersion = "1");
/// <summary>One immutable seed shared by all compared targets. Its fingerprint includes the exact DXF bytes, prompt and grader version.</summary>
public sealed class EvaluationFixture
{
    private readonly byte[] seed;
    public EvaluationFixture(EvaluationCase scenario, ReadOnlySpan<byte> seedDxf)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        if (string.IsNullOrWhiteSpace(scenario.Id) || string.IsNullOrWhiteSpace(scenario.GraderVersion) || string.IsNullOrWhiteSpace(scenario.Prompt)
            || seedDxf.Length is 0 or > 16_777_216) throw new ArgumentException("A bounded seed, task and grader version are required.");
        Scenario = scenario; seed = seedDxf.ToArray();
        SeedHash = Hash(seed);
        Fingerprint = Hash(JsonSerializer.SerializeToUtf8Bytes(new { scenario.Id, scenario.Prompt, scenario.GraderVersion, SeedHash }, McpJson.Options));
        using var input = new MemoryStream(seed, writable: false);
        if (DxfDocument.Load(input) is null) throw new ArgumentException("The frozen seed is not a readable DXF drawing.");
    }
    public EvaluationCase Scenario { get; }
    public string SeedHash { get; }
    public string Fingerprint { get; }
    public byte[] ExportSeed() => (byte[])seed.Clone();
    public DxfDocument CreateDrawing() { using var input = new MemoryStream(seed, writable: false); return DxfDocument.Load(input); }
    public static EvaluationFixture Freeze(EvaluationCase scenario)
    {
        var drawing = scenario.Seed();
        // Ignore wall-clock creation time so built-in task bytes are reproducible between processes.
        var epoch = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        drawing.DrawingVariables.TdCreate = epoch; drawing.DrawingVariables.TduCreate = epoch;
        drawing.DrawingVariables.TdUpdate = epoch; drawing.DrawingVariables.TduUpdate = epoch;
        using var output = new MemoryStream(); if (!drawing.Save(output)) throw new InvalidOperationException("Could not freeze the evaluation seed.");
        return new(scenario, output.ToArray());
    }
    internal static string Hash(ReadOnlySpan<byte> value) => Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}

/// <summary>Explicit target identity and requested cache mode; requested mode does not prove a cold or warm cache.</summary>
public sealed record EvaluationTarget(string Id, string Protocol, string Provider, string Model, string CacheMode,
    IReadOnlyDictionary<string, string>? Settings = null);
/// <summary>A target owns its client/authentication. The evaluation library never discovers credentials or starts agent processes.</summary>
public interface IAgentEvaluationTarget
{
    EvaluationTarget Target { get; }
    Task<EvaluationExecution> RunAsync(EvaluationContext context, CancellationToken cancellationToken = default);
}
public sealed record EvaluationContext(EvaluationFixture Fixture, DxfDocument Document, EditorSession Editor,
    IAgentWorkspace Workspace, IMcpToolExecutor Tools);
/// <summary>All token and latency fields may be null when a protocol does not report them. Cost is never inferred for subscription agents.</summary>
public sealed record EvaluationTelemetry(long? InputTokens = null, long? OutputTokens = null, long? CacheReadTokens = null,
    long? CacheWriteTokens = null, long? ReasoningTokens = null, int? Inferences = null, int? Retries = null,
    double? InferenceMilliseconds = null, double? FirstTokenMilliseconds = null, bool? UsageComplete = null);
public sealed record EvaluationCost(string Basis, decimal? EstimatedUsd = null, decimal? ReservedUsd = null, string? RateSource = null);
public sealed record EvaluationExecution(string Status, EvaluationTelemetry? Telemetry = null,
    IReadOnlyList<AgentEvent>? Events = null, string? Checkpoint = null, EvaluationCost? Cost = null, string? ToolCatalogHash = null);
public sealed record EvaluationToolCall(string Name, bool IsError, double Milliseconds);
public sealed record EvaluationResult(int SchemaVersion, string CaseId, string Description, string Fingerprint, string SeedHash,
    string GraderVersion, string ToolCatalogHash, EvaluationTarget Target, int Repeat, string Status, bool Passed,
    double ElapsedMilliseconds, int ToolCalls, int ToolErrors, double ToolMilliseconds, int UndoTransactions,
    EvaluationTelemetry Telemetry, EvaluationCost? Cost, IReadOnlyList<EvaluationCheck> Checks,
    IReadOnlyList<EvaluationToolCall> Tools, IReadOnlyList<AgentEvent> Events, string? Checkpoint)
{
    /// <summary>Final drawing bytes, including failed attempts, for a separate DXF artifact; never embedded in JSON reports.</summary>
    [JsonIgnore] public byte[]? DrawingDxf { get; init; }
}
