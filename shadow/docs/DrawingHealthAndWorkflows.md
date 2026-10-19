# Drawing health, edit previews and workflow regressions

These components share immutable source snapshots and the existing native transactional editor. Health reports findings without repairs; previews prepare reviewable edits without source writes; workflows record successful operations and replay them against an isolated drawing.

## Library boundaries

| Library | Public entry points | Responsibility |
| --- | --- | --- |
| `netDxf.Shadow.Health` | `DrawingHealth.Analyze`, `DrawingHealthOptions`, `DrawingHealthResources` | Bounded source diagnostics; no native document or rendering dependency |
| `netDxf.Shadow.Editor` | `EditorSession.Preview`, `ApplyPreview`, `ShadowEditPreview` | Detached graph proposal, change summary, exact-target guard and one transactional undo command |
| `netDxf.Shadow.Workflows` | `WorkflowRecorder`, `WorkflowJson`, `WorkflowRunner`, `IWorkflowDriver` | Versioned bounded scenarios, deterministic JSON comparison and serial replay |
| `netDxf.Shadow.Studio.Core` | `CadHealth*`, `CadEditPreview*`, `CadWorkflow*`, `StudioWorkflowDriver` | Selection/focus, native acceptance, rendering, receipt guards, DXF baseline and real operation driver |
| `netDxf.Shadow.Studio.Web` | `cad/analysis-ui.mjs` | Shared Health, Edit previews and Workflows palettes for native and browser hosts |

## Health checks

```csharp
var report = DrawingHealth.Analyze(snapshot,
    new DrawingHealthOptions(TinyLength: 1e-7, GapTolerance: .01),
    resources: new DrawingHealthResources(availableShxAliases),
    cancellationToken: cancellationToken);
```

`availableShxAliases` is an optional immutable set of host resource names. `null` means availability is unknown; an empty set means the host supplies none. Health never probes the filesystem or operating system for fonts. The host can also supply presentation diagnostics. Tolerances use drawing units and preserve Z coordinates.

| Family | Actual scope |
| --- | --- |
| Invalid source | Missing root/reference targets, capture faults, entity-index mismatches, entities listed under multiple block owners, nonfinite float/double atoms |
| Geometry domains | Zero normals, nonpositive circle/arc radii, invalid ellipse axes, zero INSERT scale, absent INSERT block, nonpositive text heights, polylines with fewer than two vertices |
| Tiny geometry | LINE length at or below `TinyLength` |
| Duplicates | Exact LINE/CIRCLE/ARC/ELLIPSE/POINT geometry under the same block owner; reversed LINE endpoints compare equally |
| Tiny gaps | LINE/ARC/open POLYLINE endpoints under the same owner, separated by more than `TinyLength` and at most `GapTolerance` |
| Blocks | Recursive INSERT references; unreferenced nonreserved definitions |
| Resources | Missing current layer/linetype/text-style names; unused nonreserved linetypes and text/shape styles; SHX availability and unused host aliases when inventory is supplied |

Duplicate geometry may have different appearance. Nearby endpoints may intentionally be disconnected. Block definition coordinates are checked without flattening all instances. Unused findings are suggestions and never delete resources. TrueType installation, external-reference availability, spline intersections, self-intersections, and hatch boundary validity are not checked. A clean health report is not native edit validation.

Default limits are 250,000 source objects, 5,000,000 work units, 2,000 issues and 64 nested value levels. `Complete=false` means checks stopped at a bound or endpoint-grid coordinates exceeded the safe integer range. Returned issues remain useful; do not interpret an incomplete report as clean. Caller cancellation throws. `Coverage` lists enabled families and `Limitations` accompanies every report.

Studio retains the latest health receipt. `CadHealthSelect` requires its exact snapshot, workspace/view and font generation, then selects visible dependents of the chosen finding. A style issue can select affected text occurrences; an uninstantiated block has no visible occurrence. The result includes optional bounds for focus and a message when only the source resource can be inspected. Re-run checks after edits or resource changes.

## Edit previews and atomic application

At the reusable Editor layer, prepare either a `ShadowEditBatch` or `ShadowGraphPatch`:

```csharp
ShadowEditPreview preview = editor.Preview("Review proposed edits", patch);
Console.WriteLine(preview.Validation);
// Render or inspect preview.Target and preview.Proposed without editing the source.
editor.ApplyPreview(preview);
```

The result exposes the target/proposed snapshots, a detached patch and added/removed/changed counts. Default bounds are 250,000 objects, 100,000 changes, 500 detail rows, and 128 changed-property names per row. `DetailsTruncated` reports truncated rows. Preparing a preview validates the graph, not the native setter/collection transaction: `Validation` explicitly says native validation has not run. Application delegates to the existing source transaction and rollback path. A rejected native mutation leaves source and undo history unchanged. An accepted nonempty proposal is one undo command.

Studio additionally plans 1–128 typed `CadEditOperation` values in a memory-backed branch. Kinds are `command`, `resource`, `layer`, `property`, and `module`, with their matching request payloads. Newly created entities become the selection for subsequent operations, allowing create-then-move in one proposal. Property targets must be selected. Module operations require an enabled registered module and are unavailable while editing a block definition. Arbitrary scripts/routes are not preview operations.

```csharp
using var studio = new Studio(false);
var request = new CadEditPreviewRequest(studio.CadRevision, "Create and move", [
    new("command", new("LINE", [new(0, 0), new(10, 0)])),
    new("command", new("MOVE", [new(0, 0), new(5, 7)]))
]);
var receipt = JsonSerializer.SerializeToElement(studio.CadEditPreview(request), StudioJson.CreateOptions());
string token = receipt.GetProperty("previewId").GetString()!;
var before = studio.CadEditPreviewRender(token, after: false);
var after = studio.CadEditPreviewRender(token, after: true);
studio.CadEditPreviewApply(new(studio.CadRevision, token));
```

Both images use the union of before/after bounds and the same fitted camera. SVG is the default; host-supported raster backends can be requested. Frames are limited to 16–2048 pixels per axis and 4 megapixels. Render/health diagnostics describe the proposed graph; successful rendering does not guarantee native acceptance.

Studio keeps at most eight proposals. A token binds the exact snapshot object, document/workspace/view, selection, view options, font revision, and module configuration generation. Edits followed by undo, workspace switching, changing selection or resources, and disabling then re-enabling a module can invalidate it. Apply rechecks these guards and never silently replans or retries. Successful application consumes the token. Discard removes a proposal without changing source.

### Agent approval path

`studio_edit_preview` is read-only with respect to the drawing; it creates a bounded in-memory proposal receipt. `studio_apply_edit_preview` is classified as a write and follows the agent's configured policy. ReadOnly and Plan modes block writes, Ask requests exact-argument approval, and FullAccess permits them. The MCP registry itself is a tool executor, not an authentication/approval service; direct hosts must enforce their own access policy.

Both tools require `{identity, request}`. `identity` contains the exact `documentId`, `workspaceId`, and `viewVersion`; `request` contains the current `revision` plus the typed proposal or its `previewId`. Discover full nested contracts with `studio_request_schema` for `StudioEditPreviewToolRequest`, `CadEditOperation`, and `StudioApplyEditPreviewToolRequest`. The preparation result includes bounded change/health details and before/after render parameters. The agent approval card loads the images before enabling approval. Approval does not bypass stale guards.

## Recording and replay

```csharp
using var studio = new Studio(false);
studio.CadWorkflowStart(new(studio.CadRevision, "Line regression"));
studio.CadCommand(new(studio.CadRevision, new("LINE", [new(0, 0), new(10, 0)])));
studio.CadWorkflowStop(new(studio.CadRevision));
WorkflowScenario scenario = studio.CadWorkflowExport();
byte[] json = WorkflowJson.Write(scenario);
WorkflowRunReport result = studio.CadWorkflowReplay(new(studio.CadRevision, WorkflowJson.Read(json)));
```

Starting requires the live workspace and captures an actual DXF baseline, up to 8 MiB. It uses ordinary DXF save synchronization, including dimension-picture preparation, so source revision can change; read the returned CAD state/current revision before the next command. Invalid names are rejected before saving. Baseline metadata captures layout, selection, enabled modules, font aliases, and supported render settings. Its SHA-256 detects corruption; it is not a trust signature.

Successful operations record at the Core boundary, so the same supported native, browser and MCP calls are recorded. Failed edits are not steps. A record stores the typed operation and observed source/render state after it succeeds. Recording failure never rolls back a successful user edit: it stops the recording, reports the error, and preserves earlier exportable steps. Changes without a supported recording hook also stop recording explicitly. Undo before the recording baseline is unsupported because earlier native history is not in the file.

Supported operation names are `command`, `layer`, `resource`, `select`, `property`, `grip`, `assign-layer`, `block-create`, `block-begin`, `block-finish`, `block-define`, `block-rename`, `hatch-create`, `hatch-pattern`, `module`, `history`, `layout`, `fork`, `switch`, `discard`, `merge`, `edit-preview`, `save`, and `roundtrip`. Each invokes the real typed Core operation. Merge and edit-preview steps reconstruct fresh plans; old opaque preview tokens are not replayed. New/import/demo source replacement, unsupported generic graph changes, font changes, module toggles and unsupported view changes stop recording rather than pretending to be reproducible.

Portable references use resource table/name or direct block entity ordinal, not capture IDs or DXF handles. Unsupported nested object references stop recording. Scenario ordering is significant. Branch names must resolve unambiguously during replay. Custom font files are not embedded: standalone drivers need the same required SHX aliases supplied by a factory. Studio's own replay shares its immutable decoded font resources with the isolated instance.

Replay owns a separate Studio drawing, native history and branch collection; it does not replace the visible drawing. Baseline type/checksum and required resources are checked before operations. Unknown operation names fail explicitly. The runner stops at the first mismatch/error and never retries a mutation. Default bounds are 1,000 steps, 32 MiB scenario JSON, 256 KiB arguments per step, 120 seconds and 50 reported differences. Cancellation is cooperative; a synchronous native operation can finish before the deadline is observed.

Observations include canonical source geometry/resource hashes, parsed SVG geometry hash, entity type counts, occurrence count, bounds, selected entities, layout/workspace and current layer/linetype/text style. Source hashes omit handles/opaque capture IDs; SVG hashes omit generated XML IDs. This checks actual geometry and rendering rather than only screenshots or success flags. It is not a complete DXF byte-equivalence check: block metadata beyond origin/units, unobserved document variables, external image content and full SVG clipping/paint relationships are not guaranteed. Hash comparison is exact; numeric observation fields use absolute tolerance `1e-9`.

`CadWorkflowRoundTrip` deliberately saves and reloads the active live drawing. Like ordinary reload it clears selection and undo/redo history; the UI labels that effect before execution. Its recorded step performs the same native roundtrip during isolated replay. `save` serializes without reloading. Workflow exports contain local drawing content; no agent prompt execution or provider credentials are required.

## Studio routes

All routes are under `/api/cad/{id}` in both native and WebAssembly adapters and use the normal Studio identity guards. JSON request names are the camel-case forms of the corresponding public request records.

| Method | Path | Request/result |
| --- | --- | --- |
| POST | `/health` | `CadHealthRequest` → report receipt |
| POST | `/health/select` | `CadHealthSelectRequest` → CAD state, optional focus bounds |
| GET | `/edit-previews` | Pending proposal receipts |
| POST | `/edit-preview` | `CadEditPreviewRequest` → proposal receipt |
| GET | `/edit-preview/render` | Query `previewId`, `after`, `width`, `height`, optional `backend` → image |
| POST | `/edit-preview/apply`, `/edit-preview/discard` | `CadEditPreviewTokenRequest` |
| GET | `/workflow/status`, `/workflow/export` | Recording status or `WorkflowScenario` |
| POST | `/workflow/start` | `CadWorkflowStartRequest` → CAD state and recording status |
| POST | `/workflow/stop`, `/workflow/roundtrip` | `RevisionRequest` |
| POST | `/workflow/replay` | `CadWorkflowReplayRequest` → `WorkflowRunReport` |

The shared palette factory is `analysisTools({client, edit, viewport, report, editable})`; it returns `health`, `previews`, `workflows`, `propose`, and `destroy`, and loads its own stylesheet. Health offers per-issue selection/focus. Previews show paired images and one-edit apply. Workflows expose recording, export/import, isolated replay, and the explicitly destructive-to-history roundtrip check.
