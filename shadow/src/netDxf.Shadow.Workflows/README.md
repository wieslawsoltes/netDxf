# netDxf.Shadow.Workflows

Portable, bounded recording and serial semantic replay. This library uses only the .NET base class libraries. A host implements `IWorkflowDriver` and owns the actual drawing/editor operations; the runner has no provider, HTTP, filesystem, or arbitrary-code execution dependency.

```csharp
using System.Text.Json;
using netDxf.Shadow.Workflows;

var recorder = new WorkflowRecorder("Create a line", baseline);
// Invoke the real host operation successfully before recording its observed result.
recorder.Record("command", JsonSerializer.SerializeToElement(arguments),
    JsonSerializer.SerializeToElement(observation));
byte[] json = WorkflowJson.Write(recorder.Snapshot());
WorkflowScenario scenario = WorkflowJson.Read(json);

await using IWorkflowDriver driver = createIsolatedDriver();
WorkflowRunReport report = await WorkflowRunner.RunAsync(scenario, driver,
    cancellationToken: cancellationToken);
```

The example's baseline, arguments, observation and driver factory are host-defined. `WorkflowBaseline` contains `Kind`, `Data`, and `Sha256`; the driver must validate its kind/checksum and allowed operations before execution. `InitializeAsync` prepares an isolated instance; `ExecuteAsync` applies one typed operation and returns its detached JSON observation. The caller disposes the driver, including when initialization or replay fails.

Version 1 scenarios contain a name, baseline, and ordered `WorkflowStep` values (`Operation`, `Arguments`, `Expected`). The recorder clones JSON values. A rejected record leaves earlier steps intact and throws instead of dropping a step silently. The runner stops at the first operation error, semantic mismatch, cancellation, or deadline; it never retries a mutation. Caller cancellation propagates; expiration of the runner's own time budget returns an incomplete report. Drivers must cooperate with cancellation—synchronous native work cannot be forcibly interrupted.

Defaults: 1,000 steps, 32 MiB JSON, 256 KiB per argument, 120 seconds, 50 reported differences, and absolute numeric tolerance `1e-9`. Comparison ignores object-property order, preserves array order, and reports JSON paths. Numeric tolerance applies to numeric JSON values; hash strings and text compare exactly.

`netDxf.Shadow.Studio.Core` supplies `StudioWorkflowDriver` and a recording facade with actual DXF baseline load, CAD edits, source/parsed-SVG observations, and save/reload operations. Studio scenario files include the local drawing content and can be large; they contain no provider calls or provider credentials. See [the integration guide](../../docs/DrawingHealthAndWorkflows.md) for supported operations, selection portability, resource dependencies, and regression limits.
