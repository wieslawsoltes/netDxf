using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace netDxf.Shadow.Workflows;

public static class WorkflowRunner
{
    /// <summary>Runs real operations serially, stops on the first failure, and never retries a mutation.</summary>
    public static async Task<WorkflowRunReport> RunAsync(WorkflowScenario scenario, IWorkflowDriver driver,
        WorkflowLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(driver); limits ??= new(); _ = WorkflowJson.Write(scenario, limits);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(limits.MaximumSeconds));
        var token = timeout.Token; var results = ImmutableArray.CreateBuilder<WorkflowStepResult>();
        try
        {
            await driver.InitializeAsync(scenario.Baseline, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            for (var index = 0; index < scenario.Steps.Length; index++)
            {
                token.ThrowIfCancellationRequested(); var step = scenario.Steps[index];
                try
                {
                    var actual = await driver.ExecuteAsync(step.Operation, step.Arguments, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested(); var differences = Compare(step.Expected, actual, limits);
                    results.Add(new(index, step.Operation, differences.IsEmpty, differences));
                    if (!differences.IsEmpty) return new(scenario.Name, false, false, results.ToImmutable(), "Replay stopped at the first semantic mismatch.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                { results.Add(new(index, step.Operation, false, [], error.GetBaseException().Message)); return new(scenario.Name, false, false, results.ToImmutable(), "Replay stopped after a failed operation; it was not retried."); }
            }
            return new(scenario.Name, true, true, results.ToImmutable());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(scenario.Name, false, false, results.ToImmutable(), "The workflow time budget was reached."); }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return new(scenario.Name, false, false, results.ToImmutable(), error.GetBaseException().Message); }
    }
    public static ImmutableArray<WorkflowDifference> Compare(JsonElement expected, JsonElement actual, WorkflowLimits? limits = null)
    {
        limits ??= new(); WorkflowJson.ValidateLimits(limits); var differences = ImmutableArray.CreateBuilder<WorkflowDifference>();
        void Difference(string path, JsonElement a, JsonElement b) { if (differences.Count < limits.MaximumDifferences) differences.Add(new(path, Summary(a), Summary(b))); }
        void Visit(string path, JsonElement a, JsonElement b, int depth)
        {
            if (differences.Count >= limits.MaximumDifferences) return;
            if (depth > 64) { Difference(path, a, b); return; }
            if (a.ValueKind != b.ValueKind) { Difference(path, a, b); return; }
            switch (a.ValueKind)
            {
                case JsonValueKind.Object:
                    var aa = a.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    var bb = b.EnumerateObject().ToDictionary(p => p.Name, p => p.Value, StringComparer.Ordinal);
                    foreach (var key in aa.Keys.Union(bb.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal)) Visit(path + "." + key, aa.GetValueOrDefault(key), bb.GetValueOrDefault(key), depth + 1);
                    break;
                case JsonValueKind.Array:
                    if (a.GetArrayLength() != b.GetArrayLength()) { Difference(path + ".length", a, b); break; }
                    for (var i = 0; i < a.GetArrayLength(); i++) Visit(path + "[" + i + "]", a[i], b[i], depth + 1); break;
                case JsonValueKind.Number:
                    var av = a.GetDouble(); var bv = b.GetDouble();
                    if (!double.IsFinite(av) || !double.IsFinite(bv)) { Difference(path, a, b); break; }
                    if (a.TryGetDecimal(out var da) && b.TryGetDecimal(out var db) && (double)da == av && (double)db == bv)
                    {
                        // Preserve integer/decimal precision before falling back to binary floating point.
                        // Adjacent integers above 2^53 can have the same double representation.
                        try
                        {
                            var delta = Math.Abs(da - db);
                            var exactTolerance = decimal.TryParse(limits.NumberTolerance.ToString("R", CultureInfo.InvariantCulture),
                                NumberStyles.Float, CultureInfo.InvariantCulture, out var tolerance) && (double)tolerance == limits.NumberTolerance;
                            if (exactTolerance ? delta > tolerance : (double)delta > limits.NumberTolerance) Difference(path, a, b);
                            break;
                        }
                        catch (OverflowException) { /* The difference exceeds decimal range; use the finite double operands. */ }
                    }
                    if (Math.Abs(av - bv) > limits.NumberTolerance) Difference(path, a, b); break;
                case JsonValueKind.String:
                    if (!string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal)) Difference(path, a, b); break;
                default: if (a.ValueKind != JsonValueKind.Undefined && a.GetRawText() != b.GetRawText()) Difference(path, a, b); break;
            }
        }
        Visit("$", expected, actual, 0); return differences.ToImmutable();
    }
    private static string Summary(JsonElement value)
    { var text = value.ValueKind == JsonValueKind.Undefined ? "(missing)" : value.GetRawText(); return text.Length <= 256 ? text : text[..256] + "…"; }
}
