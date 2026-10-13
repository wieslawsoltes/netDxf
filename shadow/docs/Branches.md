# Immutable branches, merges and live Studio workspaces

The branch model is source- and renderer-independent. A `ShadowMemoryStore` starts from a retained snapshot without cloning the mutable DXF database. Subsequent immutable edits share unchanged nodes with the ancestor. `ShadowMerge` plans reconciliation; a store or editor applies an accepted patch through its normal transaction rules.

## Core merge API

```csharp
using netDxf;
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

var document = new DxfDocument();
var line = new Line(new Vector3(0, 0, 0), new Vector3(10, 0, 0));
document.Entities.Add(line);
var session = new ShadowSession(document);
var live = new EditorSession(session);
var ancestor = session.Current;
var branch = new EditorSession(new ShadowMemoryStore(ancestor));
var id = new ObjectId("H:" + line.Handle.ToUpperInvariant());

branch.SetProperty(id, "EndPoint", ShadowValue.From(new Vec3(10, 5, 0)));
live.SetProperty(id, "StartPoint", ShadowValue.From(new Vec3(-5, 0, 0)));
var plan = live.PreviewMerge(ancestor, branch.Snapshot);
if (!plan.CanApply)
    throw new InvalidOperationException($"Resolve {plan.Conflicts.Length} merge conflicts.");

// Preview is detached. The source adapter still validates setters and ownership on apply.
var preview = plan.Preview!;
live.ApplyMerge(plan, "Merge alternate routing");
live.Undo(); // Restores live StartPoint=-5 while removing the branch EndPoint change.
live.Redo();
```

Independent object properties and matching typed-structure fields merge recursively by ordinal name. Equal edits converge. Missing fields are distinct from `ShadowValue.Null`. Competing scalar/reference values, concurrent identity additions, delete/modify operations and metadata changes produce deterministic conflict records. Paths are immutable segments, not ambiguous dotted strings.

Ordered `ShadowValue.Sequence` values are deliberately **atomic** for competing edits. The planner does not guess a collection ordering, splice indices or resolve identity collisions. `ShadowMergeOptions.ConflictChoice` is `Unresolved`, `Target` or `Incoming`. Preferences choose a side only at actual conflicts; they do not discard independent edits. A dangling-reference combination stays unresolved even with a preference. No patch or preview is exposed while an unresolved conflict exists.

Inputs must have the same document/root and consistent object/entity indexes. Existing identities cannot change runtime type. The caller supplies a genuine common ancestor; revision numbers do not establish ancestry. `ApplyTo` and editor application require the exact target snapshot, not an unrelated branch with the same revision. Source-backed application can still reject a structurally valid speculative preview because of a netDxf setter, construction, adoption or ownership constraint. Such failure must not become a successful history entry.

Default limits cover a union of 1,000,000 objects, 50,000,000 planner work units, 64 nested value levels and 10,000 conflicts. Work units are visits/comparisons, not a byte or CPU-time quota. The planner validates inputs and final reference integrity, supports cancellation, preserves shared nodes, and does not claim O(1) or changed-object-only end-to-end merge cost.

## Studio workflow

From **Live document**, enter a name and choose **Fork branch**. Each of at most eight branches has its own editor, undo/redo, selection, layout, retained views and pinned SHX font set. Drawing tools, inspector, SkiaSharp/SVG switching and topology operate on the selected branch. Saving DXF or mutating the live source directly is refused while a branch is active.

Choose an incoming branch and select **Preview merge**. Unresolved conflicts show ancestor/live/branch summaries. Prefer-live or prefer-branch policies are explicit selections; a clean preview is read-only until **Apply as one undo step**. The server binds the preview token to the exact live snapshot, incoming snapshot and resource set. Edits, document replacement, discarded branches or a replaced preview invalidate old tokens. Successful source adoption rebases that branch onto the canonical source snapshot and resets its isolated history. Failed source application leaves the branch available for correction.

Retained views are scoped to the current workspace. Branches and previews are in memory and disappear on document replacement or host shutdown. This is a local single-user sample: workspace switching is host-global, not a separate per-browser collaborative session. The core API does not impose this sample ownership policy.

## Saving is source normalization, not a read callback

The DXF writer can update public document housekeeping while saving. Studio therefore uses `EditorSession.SynchronizeSource` around `ShadowSession.MutateSource`, not `ReadSource`. The session reconciles the resulting public graph. The editor adopts its current revision without adding a save command or discarding prior undo/redo entries. Writer failure is rethrown after reconciliation; successful snapshot publication is not invented on failure.

The synchronization callback is trusted, explicitly selected host code. It must return the store's exact `Current` and must reconcile even if it fails. Reentrant editor transactions/history resets are rejected. Arbitrary prior external edits are not automatically accepted. Document replacement/backward revisions reset incompatible history. Structural undo restores its recorded graph; normalization metadata is not a separate collaborative change stream.

`POST /api/save` requires the current revision and identity headers. `GET /api/save` never runs the writer. Opening a DXF uses `POST /api/load?revision=...`; replacement validates the revision under the same gate before reading the supplied stream through the guarded overload. All save/load operations remain explicit user actions.

## Request receipts and coalescing notifications

After `GET /api/state`, clients supply `X-Shadow-Document`, `X-Shadow-Workspace` and `X-Shadow-View`, plus `X-Shadow-Studio: 1` for mutations. Revision-bearing bodies protect content changes; the view epoch protects equal-revision branch switches and switch-away/back races. State-dependent reads are guarded as well. A conflict returns HTTP 409. Requests are not automatically retried after conflicts or lost responses.

`GET /api/events` is an SSE invalidation feed, **not a lossless transaction log**. Each of at most 32 subscribers retains one pending notice, replacing old pending notices for slow readers. Registration and initial publication share the Studio gate. Event IDs combine a host-process ID with a monotonic change version; reconnect always receives a current notice and marks mismatched cursors as reset. Clients fetch coherent state after invalidation. Heartbeats and channel waits are reused and canceled on disconnect rather than accumulated.

The browser raises its minimum accepted change version as soon as a notice arrives. State, render, inspector and graph replies from obsolete inputs cannot overwrite newer views. Request identity includes server/document/workspace/view/revision, layout, font revision and change version where appropriate. Render images are decoded before publication and obsolete blob URLs are released. Displayed-frame identity also includes camera position/scale, viewport dimensions, backend and preview selection. Camera or view changes invalidate in-flight images immediately; picking is blocked until the matching frame is published. A disconnected browser shows reconnection status; SSE does not make arbitrary direct mutable-source writes observable without adapter reconciliation.

The sample keeps its loopback/Host checks, custom mutation header, CSP, explicit file/resource policy and request budgets. These are not authentication or hardening for untrusted Internet deployment. Branches pin SHX resources, not arbitrary changing system-font installations.

## Verification

From the repository root, after building Studio and the executable suites:

```sh
dotnet run --project shadow/tests/netDxf.Shadow.Verification -c Release -p:TargetFrameworks=net8.0
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification -c Release -p:TargetFrameworks=net8.0
python shadow/tests/http_contract.py
node --test shadow/tests/browser/state.test.mjs
```

The HTTP suite starts its own private port-zero Studio process. It never mutates an existing user-selected server. It checks the real static module delivery/CSP, identity guards, source-backed edits, conflicts, PNG/SVG previews, merge/save/undo, upload preconditions and SSE reconnect behavior. Reports and the server log are retained under ignored `artifacts/`.

Optional DOM tests require Playwright and Chromium:

```sh
python shadow/tests/browser/test_studio_ui.py --executable /path/to/chromium
```

Those seven scenarios execute the actual client logic against a static in-memory transport fixture. They cover branch/merge controls, safe conflict text, delayed inspector responses, stale command rejection, retained-view locks and POST download behavior. They do not claim live browser/server end-to-end coverage, real-browser CAD pixel equivalence or multi-user collaboration. The existing C# allocation/reuse gates and cross-platform workflow remain in force.
