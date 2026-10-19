# netDxf.Shadow.Agents.Evaluation

Reusable, credential-free evaluation infrastructure for drawing agents. The library freezes a DXF seed and a versioned task, runs an injected target against a fresh drawing and workspace, and grades the actual native result. It does not discover credentials, contact a provider by itself, start an agent process, or infer subscription prices.

## Reproducible tasks

`EvaluationCases.All` contains creation, selective editing, and topology tasks with independent native graders. `EvaluationCases.SelfTest()` checks expected drawings, negative controls, and DXF round trips without a model. `EvaluationFixture.Freeze(case)` freezes seed bytes and normalizes only seed creation/update timestamps. A SHA-256 fingerprint covers the exact seed bytes, prompt, case ID and grader version. Keep `fixture.ExportSeed()` when sharing comparisons; `new EvaluationFixture(case, savedBytes)` restores that seed. Update `GraderVersion` whenever expected behavior changes.

```csharp
using netDxf.Shadow.Agents.Evaluation;

var scenario = EvaluationCases.All.Single(item => item.Id == "edit");
var fixture = EvaluationFixture.Freeze(scenario);
EvaluationCases.SelfTest(); // Native geometry only, no inference.

// target is supplied by the application and owns its authentication/client setup.
EvaluationResult result = await new EvaluationRunner().RunAsync(fixture, target, repeat: 1, cancellationToken);
File.WriteAllBytes("seed.dxf", fixture.ExportSeed());
if (result.DrawingDxf is { } bytes) File.WriteAllBytes("drawing.dxf", bytes);
```

`IAgentEvaluationTarget` exposes explicit target/protocol/provider/model/cache settings and receives an `EvaluationContext`. The context contains the fresh native drawing, editor, memory workspace and measured MCP executor. Route actual drawing operations through `context.Tools` so tool errors, transactions and timings are measured. `AgentSessionEvaluationTarget` adapts the shared harness and an injected `Func<IChatClient>`; every run owns a fresh client and requires an explicit bounded round budget (1–100), output limit (256–8192), and at most four retries. The round count measures logical harness inferences, not hidden SDK continuation requests. Its report records actual harness budgets, mode, instructions hash, and effective tool catalog including intrinsic plan/finish controls. `agent_finish` is required for harness completion. Other protocols can implement the interface; Codex/ACP process adapters are not bundled in this package.

The runner catches target failures into failed observations, preserving their denominator and final drawing when serialization succeeds. Independent graders check geometry, protected entities, layers, topology evidence, DXF round trips and real MCP mutations. The built-in cases require 1–3 undo transactions; the edit case requires bulk editing and the graph case requires graph summary/path tools. Each invocation starts from the frozen seed; no drawing or workspace state carries into another repeat. The runner does not retry failed cases or start further provider requests itself.

## Measurements and comparisons

`EvaluationResult` includes success/checks, wall time, observed inference/first-token time, actual MCP tool latency/errors, undo count, nullable token/cache/reasoning usage, cost provenance, events and a portable checkpoint. `DrawingDxf` is an in-memory artifact excluded from JSON; save it separately. Unknown fields remain null. `UsageComplete=false` identifies failed/retried runs whose reported token totals may omit billed requests; it is not a zero-use claim. Sum of tool times can exceed wall time when independent reads overlap. `ToolCatalogHash` and the task fingerprint must match for an identical-task comparison. Harness settings remain visible because different budgets or instructions can still affect outcomes.

A requested cache mode is not a measured warm/cold cache condition. OpenAI and Google may cache automatically when harness hints are off, and providers have distinct cache eligibility, pricing and retention rules. Compare observed cache reads/writes. Do not infer an API price for Codex/ACP subscriptions or protocols lacking billing usage. Costs are optional, explicitly labeled estimates or retained reservations; none are invoices.

Studio's **Agents → Efficiency** tab imports local `report.json` and `summary.json` outputs, groups matching task/catalog fingerprints and shows all successful and failed runs. It does not start inference, read keys, or upload reports. There is no aggregate model ranking or claim of measured improvement without imported observations.

## Conservative API budget guard

`BudgetChatClient` wraps the single-request **OpenAI and Anthropic API adapters** from `ShadowAgentProviders`, with SDK retries disabled. It rejects Google/Gemini, subscription and unknown providers before dispatch: one Google logical response can contain multiple billable continuation requests, which this wrapper cannot reserve independently. A custom injected client must honor the same one-request contract; this wrapper cannot inspect arbitrary custom transport implementations.

`EvaluationBudgetOptions` requires an explicit provider/model, ledger path, caller-verified conservative input/output rates and bounded tokens. Rates must cover all applicable input tiers and cache-write premiums. No built-in pricing catalog is assumed. Before each inference, including compaction, the wrapper exclusively opens the shared ledger, reserves a conservative text-input/output upper bound, flushes it to disk, and only then invokes the client. The ceiling cannot exceed $20. Interrupted/unknown usage retains the reservation; damaged or inconsistent ledgers and unknown rates fail closed. Use the same ledger path and ceiling across provider runs. Deleting/replacing the ledger or running other clients bypasses this accounting.

`CacheTokensAdditionalToInput` declares how the injected SDK reports cache usage. `false` means input already includes cache counts; `true` adds cache read/write counts; `null` preserves the full reservation because reconciliation is unknown. The CLI uses the verified inclusive OpenAI convention and conservatively keeps Anthropic reservations. Supplied rates, tokenizer/context assumptions and the single-request client contract remain caller responsibilities. Hosted tools and image/audio inputs are rejected because their costs are undeclared.

The wrapper pins the configured model even when request options omit it, and counts per-request instructions and response schemas in its reservation. Caller-supplied raw provider options, extra provider properties, hidden conversation context and background continuations are rejected because this text-only guard cannot inspect their costs. Invalid negative usage retains the full reservation. Usage exceeding a reservation is recorded and blocks later requests until the caller audits the violated assumptions and ledger. A denied first request creates a valid zero-spend ledger; an already-existing empty ledger remains an error requiring inspection.

The opt-in [LiveEvaluation executable](../../tests/netDxf.Shadow.Agents.LiveEvaluation/README.md) owns environment-based authentication and command-line policy. The reusable library contains neither. Its live path requires explicit `--live`, provider, model, rates and ledger. Ordinary verification uses synthetic clients only:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Agents.Evaluation.Verification
dotnet run --project shadow/tests/netDxf.Shadow.Agents.LiveEvaluation -- --self-test
node --test shadow/tests/browser/evaluation-contracts.test.mjs
```
