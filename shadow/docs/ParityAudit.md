# Shadow Studio request and parity audit

This is the requirement audit for the continuing implementation goal. The earlier
[milestone checklist](ImplementationChecklist.md) records delivered increments;
it is not evidence of complete AutoCAD parity. A passing test proves only the
behavior it exercises. Items below remain open until the implementation and the
corresponding end-to-end evidence cover their stated scope.

## Requested areas

| User requirement | Current implementation and evidence | Remaining work / completion evidence |
| --- | --- | --- |
| Rapid drawing creation, editing and a complete MCP toolset | Generic source/schema/collection/method tools, atomic drawing batches, typed Studio routes; MCP and AgentStudio verification programs | Build a per-native-entity/property/method coverage inventory, close unsupported authoring/editing shapes, and exercise them through actual Studio/MCP workflows. Generic reflection access alone does not prove safe full editing support. |
| Full graph analysis in Studio | Nodes, edges, components, path/traversal, cut vertices/bridges, nearest node, diagnostics, fundamental cycles and minimum spanning forests share reusable algorithms and MCP tools | Broaden source-reference graph exploration and query-to-selection workflows; validate large real drawings and export/import analysis receipts. Geometric topology does not imply discipline connectivity. |
| AutoCAD-like ribbon and command/log visibility | Adaptive ribbon panels, keyboard command workflow, independent command/history visibility, clean screen; browser tests | Continue command discoverability, keyboard navigation, focus/accessibility and small-screen validation as commands are added. |
| Block definition and insertion workflow | Reusable block authoring, nested units, insertion base points/scales/rotation, repeated insert, block editing, safe explode; Advanced native fixtures | Dedicated attribute editor and definition synchronization, block-library import/export, richer insertion previews, array references and supported attributed/nonuniform explode cases. Dynamic/parametric block behavior is not implemented. |
| MOVE and other editing tools with drafting aids | MOVE/COPY/ROTATE/SCALE/MIRROR, basic topology edits, STRETCH/BREAK/BREAKATPOINT increment, object/grid snap, Ortho/polar/tracking; native and browser regressions | Extend entity/coordinate-system coverage for each command, curve trim/extend/offset, reference/copy variants of transforms, richer arrays, selection cycling and additional acquisition modes. Existing planar restrictions remain material. |
| Full line, text style and layer UI | Managers expose supported table fields, complex linetypes, LIN import, fixed-height/font metadata, layer color/plot/lock/freeze/transparency; native layer-state save/update/rename/delete/selective restore with history and DXF persistence | Layer filters/isolation, saved-layer editing, persisted restore masks, LAS transfer, unresolved-resource repair, individual viewport overrides, plot-style workflows, text editing/formatting and externally referenced resource behavior. |
| Token-efficient coding harness and provider caching | Bounded context/tool batches, caching configuration and usage, stable prefixes, checkpoint/compaction behavior; provider/agent fixtures | Verify supported provider/model combinations with opt-in real accounts, measure cache hits on repeated identical tasks, and compare quality/latency/cost. Fixtures do not establish actual provider cache performance. |
| Coding-chat UI and reusable Markdown | Conversations, streaming/tool activity, approvals, execution modes, references, external-agent panels and reusable Markdown assets; browser fixtures | Continue conversation editing/branching/search and accessibility/workspace usability; measure long conversation rendering under realistic tool-output loads. |
| Reusable sample/application projects | 33 packages, extracted sample factories/Core/hosting/browser/assets/agent/Markdown/domain libraries; package installed-consumer checks | Preserve library dependency boundaries and package consumer evidence for each new feature. App-specific glue remains in hosts; no duplicate implementations between native/browser. |
| Tank-and-pump P&ID to EN ISO 10628 / EN 62424 | Original reusable symbols, tagged process network, flow/instrument references, drawing gallery and text/binary DXF tests | Review each displayed symbol and drafting convention against authoritative standards material and document exact clauses/editions. Starter artwork and reference links alone do not prove standards conformance. |
| Codex app login and ACP coding agents | Reusable app-server/ACP clients, managed host, login/session/approval/terminal/workspace protocols, Codex steering and compaction controls; local process and HTTP fixtures | Real installed-agent login/reconnect/session interoperability remains unverified. Publish tested protocol/version compatibility and extend context controls for other agents where supported. Do not invent unsupported Claude/Gemini subscription-login methods. |
| Toggleable P&ID, electrical and piping modules | Independent module registry, reusable domain contracts, original versioned starter symbols and explicit port/route metadata; domain/module fixtures | These are starter schematic modules. Expand discipline catalogs and authoring, tag/spec/circuit management, reports/schedules and project-level workflows. Electrical protection calculations, hydraulic analysis, 3D piping and isometrics are not implemented. |
| CAD draw/edit/undo/save/reload regressions | Isolated workflow recorder/replayer, native DXF baseline, semantic source/render comparison; workflow fixtures | Grow recorded scenarios across remaining commands and entity/resource families. Add rendered regression coverage where semantic hashes omit clipping/paint/external-resource behavior. |
| P&ID disconnected-pipe/tag/flow/instrument validation | Explicit connectivity/ports, duplicate tags, process flow and PCE loop/reference rules; discipline/sample fixtures | Expand rules to the new discipline data and symbol families. Test fault cases on representative imported drawings. Geometry crossings are intentionally not automatic connectivity. |
| Agent edit previews and atomic undo | Exact identity-bound proposals, before/after images, approval flow and one undoable batch; Editor/Core/browser fixtures | Broaden proposal operations with new command families and ensure all agent mutation surfaces offer an appropriate preview path. |
| Drawing health | Duplicate primitives, endpoint gaps, source/domain checks, unused resources and supplied-font-inventory checks; health fixtures | Extend invalid-geometry coverage to splines, self-intersections and hatch boundaries, external resources, and additional native entities. A clean bounded report must not be presented as full drawing validity. |
| Searchable, versioned reusable symbol libraries | Immutable catalog, attributes/units/basepoints/standards metadata, bounded search and module placement | User-managed library import/export, version migration/replacement, thumbnails and broader reviewed catalogs; verify reference and attribute preservation across updates. |
| Measured agent efficiency | Reproducible tasks, grading/receipts and durable budget reservation; offline evaluation fixtures | Actual task-success/latency/tokens/cache/cost comparisons are outstanding. Do not substitute synthetic reports for live comparison results. |
| Drawing-selection references in chat | Stable object/occurrence chips with document/workspace/revision guards and stale handling; References/browser fixtures | Exercise references across new editing commands and external-agent steering. Expand explicit resource/nested-object workflows beyond ordinary selected occurrences as needed. |
| Integrate latest netstandard, review, granular commits and PR | Upstream integrated; focused commits on PR #272 | Recheck upstream divergence during continued work, review each increment, commit by responsibility and push to the existing branch. |
| CI passes | Windows/Linux build and package jobs plus cross-platform Shadow, conformance and real-browser jobs | All required checks must pass for the exact pushed revision. A local pass, older green revision or skipped job is not a substitute. Track and fix actual failures. |

## October 19 review evidence

- Native verification: 1,231 Advanced checks, 5,338 Graph checks, 493 provider
  checks, 61 ACP checks, 38 Codex checks, and 78 external-agent host checks.
- Browser unit verification: 392 checks. Fresh native Chromium verification:
  77 checks, including independent saved-DXF audit of STRETCH/BREAK geometry,
  cancellation during crossing selection, and cycle/forest palette workflows.
- Standalone WebAssembly verification: CAD workflows at root and subpath,
  134 capability checks, and 156 agent checks using intercepted provider fixtures.
  Large tool results are retrieved through their advertised bounded archive paging.
- Native and WebAssembly Release publishes pass. Route/asset parity verifies
  87 routes and 960 published assets.

These are local results. The CI row above still requires verification on the
exact pushed revision, including each operating-system and package-consumer job.

### Layer-state increment

- Reusable native saved-state plans and the shared `LAYERSTATE`/`LAS` manager
  cover save/update, rename/delete and selective global property restoration.
  Text/binary DXF, branch merge, recorded workflow replay and resource rename
  history are exercised. Missing resources are refused before mutation.
- Review found compound layer renames rekey saved-state dictionaries before the
  source adapter's collection phase. The adapter now uses actual intermediate
  values and membership, avoiding duplicate key edits during undo; a later
  rejected collection update also restores the original identities and session.
- Fresh .NET 8 verification: 1,243 Advanced, 18 Shadow and 83 AgentNative checks.
  Browser units: 397. Published native Chromium: 86. Published WebAssembly:
  127 CAD checks across root/subpath hosting, with no API server requests.
  Independent ezdxf audit accepts the saved layer-state drawing without repairs.
- Agent request schemas expose restore flags/defaults; semantic UI tools can
  open the manager and submit the same identity-fenced layer-state transaction.
  Graph analysis and command/ribbon visibility controls are also exposed.
- Native and browser publishes and all 88 route contracts pass. These local
  results supplement the prior CI baseline; the next pushed revision requires
  its own complete hosted checks.

## Current implementation order

1. Preserve green CI on every new pushed increment. Process-launch and browser
   regressions were corrected; all 23 checks passed on `377291ce`.
2. STRETCH/BREAK, graph cycle/forest access and external-agent context controls
   have landed. Extend their coverage as related workflows are implemented.
3. Continue block attributes, remaining layer workflows, drafting command/entity coverage and reusable
   symbol/module workflows against the remaining rows above.
4. Broaden per-feature regressions and provider interoperability evidence; repeat
   the requirement audit before claiming overall completion.

Credentials supplied in conversation are neither used nor persisted. Paid testing
remains disabled, with actual spend USD 0. Any later opt-in evaluation must retain
the user's cumulative USD 20 ceiling. There is no credential material in this audit.
