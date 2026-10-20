# Shadow Studio request and parity audit

This is the requirement audit for the continuing implementation goal. The earlier
[milestone checklist](ImplementationChecklist.md) records delivered increments;
it is not evidence of complete AutoCAD parity. A passing test proves only the
behavior it exercises. Items below remain open until the implementation and the
corresponding end-to-end evidence cover their stated scope.

The current increment continues workspace docking, ribbon/command discovery and
graph analysis after the earlier stable merges. Each increment is qualified and
merged independently. The remaining parity requirements below are follow-up
backlog, not claims of completion or new merge-blocking scope.

## Requested areas

| User requirement | Current implementation and evidence | Remaining work / completion evidence |
| --- | --- | --- |
| Rapid drawing creation, editing and a complete MCP toolset | Generic source/schema/collection/method tools, atomic drawing batches, typed Studio routes; MCP and AgentStudio verification programs | Build a per-native-entity/property/method coverage inventory, close unsupported authoring/editing shapes, and exercise them through actual Studio/MCP workflows. Generic reflection access alone does not prove safe full editing support. |
| Full graph analysis in Studio | All existing geometric topology analyses have MCP and Graph workspace controls, source links, picking and exports. A separate directed source-reference explorer exposes incoming/outgoing object dependencies, cyclic groups, shortest dependency paths and impact proofs with exact property paths, snapshot keys, bounded paging and exports. Explicit discipline-port networks add components, paths, traces, strongly connected groups, condensation, weak cuts, bounded loop evidence and explicit isolation/rerouting studies with before/after proofs. | Continue large/imported drawing validation and broader discipline engineering workflows. Geometric topology and source ownership/dependencies do not imply physical process connectivity or engineering simulation. |
| AutoCAD-like ribbon and command/log visibility | Four ribbon display modes, contextual Block Editor tab, command search and customizable Quick Access, independent command/history visibility, clean screen, docked/floating/anchored palettes with edge-drop previews, tab groups, split sizing and docking permissions, and named UI workspace save/restore; Studio JSON ribbon customization, floating panels, palette transparency and portable UI workspace transfer; shared roving keyboard navigation across ribbon, Quick Access, floating panels and workspace menus; browser tests | Continue command discoverability and accessibility as commands are added. CUIx interoperability and native multi-monitor windows remain open. |
| Block definition and insertion workflow | Reusable block authoring, nested units, insertion base points/scales/rotation, repeated insert, block editing, safe explode; exact attribute history; shared instance editor, BATTMAN/ATTDEF for named blocks and standalone layout definitions, ATTSYNC previews, insertion-time value/verify prompts, cursor-following insertion previews, explicit insertion attributes and DXF block-library transfer with conflict policies/previews | Command-line -ATTDEF and richer definition text settings, duplicate-tag layout copying, library catalog/version workflows and remaining transfer metadata, richer insertion/array workflows and supported attributed/nonuniform explode cases. Dynamic/parametric block behavior is not implemented. |
| MOVE and other editing tools with drafting aids | MOVE/COPY/ROTATE/SCALE/MIRROR, basic topology edits, STRETCH/BREAK/BREAKATPOINT increment, ROTATE/SCALE Reference and Copy acquisition, overlap selection cycling, object/grid snap, Ortho/polar/tracking; native and browser regressions | Extend entity/coordinate-system coverage for each command, curve trim/extend/offset, richer arrays and additional acquisition modes. Existing planar restrictions remain material. |
| Full line, text style and layer UI | Managers expose supported table fields, complex linetypes, LIN import, fixed-height/font metadata, layer color/plot/lock/freeze/transparency; native layer-state save/update/rename/delete/selective restore with history and DXF persistence; browser-local property filters/groups, native undoable isolation, saved-entry editing, persisted restore masks and previewed LAS transfer with explicit resource mappings | Native filter dictionaries, automatic resource reconstruction, individual viewport overrides, plot-style workflows, text editing/formatting and externally referenced resource behavior. |
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
| Searchable, versioned reusable symbol libraries | Immutable catalog, attributes/units/basepoints/standards metadata, bounded search and module placement; user DXF library import/export with dependency/resource policies and definition thumbnails | Persistent user-library catalogs, version migration/replacement, search/paging and broader reviewed catalogs; extend reference and attribute preservation across version updates. |
| Measured agent efficiency | Reproducible tasks, grading/receipts and durable budget reservation; offline evaluation fixtures | Actual task-success/latency/tokens/cache/cost comparisons are outstanding. Do not substitute synthetic reports for live comparison results. |
| Drawing-selection references in chat | Stable object/occurrence chips with document/workspace/revision guards and stale handling; References/browser fixtures | Exercise references across new editing commands and external-agent steering. Expand explicit resource/nested-object workflows beyond ordinary selected occurrences as needed. |
| Integrate latest netstandard, review, granular commits and PR | Earlier increments merged through PR #279; continued work starts from the current netstandard branch | Recheck upstream divergence during continued work, review each increment, commit by responsibility and push the feature branch. |
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

### Attribute foundation increment

- `Attribute.GetTextData`/`SetTextData` and the corresponding ATTDEF methods
  transfer independent stored text packets. They preserve optional flags, ordered
  embedded layout fields, binary auxiliary data and canonical style references.
  Fallback text and proxy graphics remain independent. The existing DXF writer
  still emits version marker zero for embedded content when it was absent.
- Shadow exposes `TextData` as a writable detached value. External embedded
  edits now change the snapshot; unchanged captures retain snapshot identity.
  Scalar edits, branch previews/merge and repeated undo/redo preserve the packet.
  Visible-content editors must invalidate stale proxy graphics explicitly.
- Native `Insert.Sync` rebinds retained attributes to replacement definitions
  with the same tag, retaining their identity and authored values. Geometry is
  prepared before membership changes; removal/addition invalidates INSERT proxy
  graphics. Arbitrary native event callbacks are not a rollback transaction.
  A dedicated, undoable Studio ATTSYNC command and attribute editor remain open;
  native Sync still preserves resource overrides rather than resetting them.
- Local .NET 8 results: 157 attribute conformance cases, 688 insert conformance
  cases and all 1,256 Advanced checks pass. Independent preexisting DXF fixtures
  exercise both text/binary input and output. These tests are foundations for the
  remaining UI work, not evidence of complete block parity.

## Current delivery and follow-up order

1. Preserve green CI on every new pushed increment. Process-launch and browser
   regressions were corrected; all 23 checks passed on `6b8a1482`, including the
   production block-library transfer and cross-platform installed-package matrix.
   Pages deployment was skipped.
   Each new pushed revision needs its own checks.
2. Qualify each new workspace and graph increment locally and in hosted CI before
   merging its PR with granular commits.
   Follow-up work can extend insertion/array workflows and supported attributed/
   nonuniform explode cases with native and browser history tests. Retain explicit
   library metadata, catalog/version management and dynamic-block gaps.
3. Complete remaining layers/styles and drafting workflows: native filter dictionaries,
   automatic resource reconstruction, viewport overrides,
   text formatting, curve edits, richer arrays and acquisition modes.
   Extend ribbon discovery, keyboard navigation and accessibility with each command.
4. Expand reusable symbols and toggleable P&ID/electrical/piping modules with
   discipline authoring, tags/specifications/circuits, reports and project workflows.
   Review the pump/tank drawing against authoritative standards editions and clauses.
5. Build the native entity/property/method coverage inventory; close unsupported
   Studio/MCP operations and preview paths. Expand reference-graph exploration,
   query-to-selection, large-drawing evidence and geometry/health validation.
6. Continue coding-chat editing/branching/search and long-conversation usability;
   qualify installed-agent login/reconnect/protocol compatibility and repeatable
   provider efficiency only through an explicitly permitted credential source.
   Existing fixtures do not replace live account or cache-performance evidence.
7. Review every requirement row, package boundary and supported workflow. Require
   native/browser draw-edit-undo-save-reload evidence, independent DXF checks and
   passing hosted CI on the exact final revision. Put reusable functionality in
   shared libraries, using separate projects where appropriate, with host/module glue
   in the application. Granular review, commits and CI apply throughout this sequence.

Credentials supplied in conversation are neither used nor persisted. Paid testing
remains disabled, with actual spend USD 0. Any later opt-in evaluation must retain
the user's cumulative USD 20 ceiling. There is no credential material in this audit.

### Grouped workspaces and directed-analysis increment

- Docked palettes now support persistent tab groups, independent tab/group order,
  stack resizing, lock/cancel behavior and per-palette Allow Docking. Explicitly
  showing an existing palette refreshes its contents; hide/show renders each newly
  visible palette once. Narrow screens preserve the saved desktop arrangement.
- Command search discovers registered CAD, graph and enabled discipline actions.
  Quick Access supports bounded pins, reordering, overflow and reset; named UI
  workspaces restore these choices. Keyboard navigation scrolls the focused result
  into view and restores toolbar focus after customization. Agent inspection
  respects the active modal and rejects stale background control IDs.
- The shared reference library and MCP expose strongly connected components,
  condensation links, shortest directed dependency paths and incoming impact
  proofs. The References palette provides inspection, explicit source selection
  and bounded JSON/SVG export. A Dependency analysis preset groups it with Graph
  beside Properties. These are source relationships, not process simulation.
- Exhaustive three-node graph oracles, budget/cancellation cases, invalid proof
  guards and adaptive four-stream pagination pass: 29,163 Graph checks, including
  22,989 new reference-analysis checks. MCP passes 127 checks. Browser units pass
  595 tests; the fresh native publish passes 247 CAD browser and 33 HTTP checks,
  with no uncaught JavaScript errors or CSP violations. Native and WebAssembly
  publishes pass; all 97 route contracts and 1,044 published assets validate.
- Existing topology and source-reference algorithms have MCP and UI access.
  CUIx interoperability and discipline engineering calculations remain
  explicit follow-up scope, alongside the requirement-table gaps above. Each
  pushed revision still requires hosted CI and installed-package qualification.

### Saved workspace and source-reference graph increment

- Palette edge drops now have visible targets, docking previews, Ctrl bypass and
  Escape/pointer-cancel rollback. Named UI workspaces capture palette geometry,
  ribbon and command-line display independently of drawing branches. Restore
  sanitizes data, clamps geometry and releases old asynchronous content.
- The contextual Block Editor ribbon restores the prior enabled tab after exit.
  Titles-mode hover/click now enters the panel instead of closing it, and title
  labels retain enough width. Native and WASM use the same controls.
- All previously available geometric topology algorithms remain exposed. The new
  source-reference graph adds directed incoming/outgoing exploration with exact
  property paths, root discovery, object inspection, explicit drawing selection,
  and bounded JSON/SVG export. It is separate from discipline connectivity.
- Independent review identified a pending-stack allocation gap in the reference
  index. Scheduled values and cumulative path text are now bounded before
  enqueueing; nested-wide and long-prefix fixtures cover the failure paths.
- Local verification passes 6,174 Graph checks (692 reference checks), 127 MCP,
  534 browser unit, 33 native HTTP, 201 native CAD browser, 357 WASM CAD and
  326 WASM agent checks. The native browser run reports zero uncaught errors and
  CSP violations. WASM checks cover both root and subpath hosting. All 97 route
  contracts and 1,032 published assets validate. Provider fixtures make no live
  paid calls.
- Package consumers exercise the new public reference API as well as existing
  geometry/topology APIs. Final package qualification and hosted CI must pass
  for the committed revision before merge. The remaining parity gaps in the
  requirement table continue as follow-up work rather than stable-merge blockers.

### Production block-library increment

- The reusable Drafting planner transfers one named definition or model-space
  dependency graph, with separate root/resource conflict policies and bounded
  identity remapping. Redefinition retains matching definition identities and
  existing assigned INSERT values. Anonymous dependencies are allocated separately.
- The Symbols DXF codec provides bounded browsing and independent text/binary
  library export. Native reconstruction retains ENDBLK metadata and associative
  hatch sources. Adapter history handles shared boundary sources, reordered entity
  membership and constructor-owned metadata. Contradictory collection counts are
  rejected before native adoption; arbitrary native event callbacks remain outside
  the guaranteed transaction boundary.
- Studio's shared Insert ribbon, native/WASM routes and typed agent previews expose
  import/export, resource decisions and definition thumbnails. Import applies as one
  undo entry without implicit placement. Cached library discovery omits DXF bytes;
  recorded workflows carry bounded content for replay without the upload cache.
- Local verification: 1,435 Advanced checks, 422 browser unit checks, 132 published
  native Chromium checks and 218 standalone WASM checks across root/subpath hosting.
  All 20 new native cases pass in Debug and Release, alongside 8,066 Release hatch
  regressions; both Release publishes and all five core target builds pass.
  All 96 route contracts and 987 assets validate.
  Independent DXF audits preserve exported attributes, units, base point and ENDBLK
  metadata without repairs. All 33 packages pass packing and 14 fresh installed-consumer
  scenarios pass locally. Hosted CI on `6b8a1482` passes all 23 checks, including
  qualification and the complete Windows/Linux package-runtime matrix; Pages is skipped.
- Persistent catalog/version management, user-library search/paging, additional
  transfer metadata and reviewed symbol families remain open. Nonzero handle-valued
  XData is refused until explicit remapping is implemented; external files are not
  bundled. This increment does not implement dynamic or parametric blocks.

### Standalone layout attribute increment

- Shared ATTDEF authoring accepts model/paper layout owners, the current block
  editor and named definitions. Point acquisition occurs after the dialog closes;
  cancellation leaves source geometry and history unchanged. Typed agent reads use
  `currentSpace=true`, and proposals retain the normal drawing/revision guards.
- Definitions keep their native Object classification and keyed block ownership.
  Root rendering displays their tags, selection and grips operate on the original
  objects, and BLOCK includes their defaults with the selected geometry. Converted
  references receive explicit assigned attributes in previews and adopted source.
- Affine edits and MOVE coordinate embedded multiline and fallback frames; move
  grips preserve independent offsets. Retain/convert/remove block dispositions,
  exact undo/redo, paper/model space, recorded replay and text/binary persistence
  have dedicated regressions. Embedded nonuniform frames and scaling inline height
  overrides are explicitly refused.
- Local verification passes 1,460 Advanced, 424 JavaScript, 141 fresh published
  native Chromium and 237 standalone WASM checks across root/subpath hosting.
  Both Release publishes succeed; all 96 route
  contracts and 987 assets validate. Independent saved-DXF audits require no repairs.
  An installed-only package consumer exercises standalone creation, tag preview,
  multiline grip movement and history. All 33 packages and 14 installed-consumer
  scenarios pass on clean revision `be4479c4`; each later revision requires its own
  hosted CI qualification.
- Duplicate-tag layout copying, command-line -ATTDEF, richer definition text
  settings and the other block requirements remain open. This is a bounded
  workflow increment, not evidence of complete AutoCAD block parity.

### Cursor insertion preview increment

- Reusable scene/render options select exact display roots without changing source
  ownership. A receipt-guarded read compiles only a detached proposed INSERT using
  the same units, scale, rotation, alternate base point and attribute construction
  as final placement. Model/paper space, cache isolation, cancellation and stale
  requests have dedicated verification.
- Native and WASM share the preview route and frontend. One decoded image follows
  the cursor without per-pointer requests. Cancel/session changes release pending
  graphics without adding source objects or undo entries. Agent reads can access
  the same typed route.
- Local verification passes 1,465 Advanced, 428 JavaScript, 144 fresh published
  native Chromium and 243 standalone WASM checks across root/subpath hosting.
  Both Release publishes pass; all 97 routes and 990 published assets validate.
  Independent saved-DXF audits require no repairs. Installed-consumer checks also
  exercise the root-subset renderer and detached insertion preview API; final
  package execution and hosted CI must qualify the committed revision.
- Richer acquisition/coordinate modes, arrays, dynamic blocks and other listed
  parity gaps remain follow-up work. Cursor feedback is a scaled transparent image;
  it is not a claim of identical stroke rasterization at every zoom.

### Native block exchange increment

- `Block.Create` no longer unlinks source hatch boundaries. Source associations,
  shared boundary entities, reactors, handles and active layout remain intact;
  only the copies follow the existing nonassociative hatch-clone policy.
- Reusable `Block.ToDocument`, stream Load and text/binary stream Save APIs expose
  the existing model-space drawing export without temporary files. File Save uses
  the same implementation; failed file loads now handle a null reader result.
  The caller retains stream ownership and receives documented reader/writer errors.
- Stream round-trips cover all six supported DXF versions, nested block settings,
  resources and assigned attributes. Embedded attribute packets, independent fallback
  values and linked MTEXT column ownership have additional regressions.
- Local verification passes all 25 new exchange cases in both Debug and Release,
  34 related block checks, 270 text-column regressions, all 1,402 Advanced checks,
  and Release builds for netstandard2.0, net471, net48,
  net6.0 and net8.0. Fourteen independently read DXF exports have no audit errors
  or repairs. Hosted CI still needs to qualify the next pushed revision.
- This is a native exchange foundation. Exact graph-library transfer, resource-name
  collision policies, versioned library management and Studio/agent controls remain
  open. Model-space export does not archive root definition metadata or preserve
  cloned hatch associations; those limitations are documented in BlockAuthoring.md.

### Captured block graph and COPY increment

- The shared adapter reconstructs captured INSERT attribute collections before
  binding owned sequence records. Distinct duplicate-tag instances, empty stored
  sequences and terminator metadata are retained. The native sequence materialization
  API registers new records and is idempotent for existing ones.
- COPY retains shared definition bindings while cloning assigned attributes. Its
  preview, repeated undo/redo and text/binary save/reload preserve values, geometry,
  empty-sequence presence and owned identities. Native and WASM browser regressions
  also exercise copying an inserted block and independently audit the saved result.
- Removing newly imported block graphs now detaches contained entities before
  deleting definitions and orders resource registration/removal by dependencies.
  Captured nested-definition fixtures cover shared children, attributes, layers,
  dimension styles and custom arrow blocks through repeated undo/redo.
- These changes remove adapter obstacles found while developing library transfer.
  A production import planner, resource-name policies and Studio library controls
  are still required; a bounded verification fixture does not supply those features.
- Local verification: 1,412 Advanced checks, 19 native attribute-membership cases
  in Debug and Release, 124 published native browser checks and 202 standalone WASM
  checks pass. Both Release publishes and all five native targets build. All 93
  route contracts and 984 assets validate. Hosted CI must qualify the pushed head.

### Insertion attribute increment

- `InsertAttributeConstruction` materializes explicit owned attributes in proposed
  snapshots before publication, using native text transformation and the actual
  definition origin, insertion frame, owner units and MIRRTEXT. Assigned values
  use exact definition identities and do not rewrite definitions or other INSERTs.
  Initialization preserves insertion XData; it does not apply ATTSYNC's reset policy.
  Single/multiline defaults render in detached previews and survive exact native
  adoption, undo/redo and text/binary DXF transport.
- Shared insertion UI supports ATTREQ, ATTDIA and command-line `-INSERT`, constant
  defaults, preset behavior, verification, invisible values, multiline input and
  independent repeated placement. Cancellation happens before source publication;
  each successful insertion is one undo entry. Literal END and whitespace in
  attribute input are preserved. Pending input is fenced to its drawing receipt.
- Typed `insert` operations, ordinary INSERT command previews and recorded replay
  use the same explicit construction. Native/WASM routes and agent schemas expose
  identity-bound assignments. Source-independent drafting remains independent of
  native geometry; library callers compose placement with the NetDxf construction
  plan as documented in BlockAuthoring.md.
- Local verification: 1,402 Advanced checks and 422 browser unit checks. Published
  native Chromium passes 122 checks; standalone WASM passes 199 across root and
  subpath hosting. Independent saved-DXF audits find the requested values and no
  errors or repairs. Both Release publishes pass; all 93 route contracts and 984
  assets validate. New commits still require hosted CI qualification.
- Remaining insertion work includes cursor-following block graphics, additional
  acquisition/coordinate modes and arrays. Library transfer, model-space ATTDEF,
  supported attributed/nonuniform explode and dynamic block behavior remain open.

### Definition manager increment

- Reusable `DraftingAttributeDefinitions` plans edit tags, prompts, defaults,
  verify/preset flags and supported appearance fields, create single/multiline
  definitions, and stage ordering/removal. Native identity, assigned instance
  values, optional independent definition edits and atomic synchronization are
  preserved through history. Profile checks refuse unsavable locking/multiline
  modes. Multiline creation stores paragraphs separately from its single-line
  fallback; a real-browser save regression caught and verified that distinction.
- Shared `BATTMAN` and named-block `ATTDEF` dialogs are available through commands,
  Home/Insert ribbon actions and semantic agent controls. Apply remains open;
  Cancel discards staged changes. Existing-reference updates default on with the
  synchronization effects displayed. Constants cannot be moved by the UI, and
  the last definition cannot be removed. Creation inside `BEDIT` is isolated until
  save; discard and live undo/redo are verified.
- Native and WebAssembly routes share read/edit contracts, typed previews with
  synchronization reports, request-schema discovery and portable workflow replay.
  The reusable implementation stays in Drafting/Core and shared web assets.
- Local validation: 1,390 Advanced checks, 416 browser unit checks, 112 published
  native Chromium checks and 179 standalone WASM checks across root/subpath
  hosting. Both Release publishes pass. All 92 route contracts and 978 published
  assets validate; independently read saved DXF files have no audit errors or
  repairs. Hosted CI must qualify the new commits separately from `e559aceb`.
- Remaining block scope includes model-space ATTDEF placement, insertion-time
  prompts/verification, explicit default-attribute previews, library transfer,
  annotation contexts, plot styles and the other workflows in the requirement
  table. These results do not establish full AutoCAD block parity.

### Attribute renderer and instance editor increment

- Reusable text projection now uses the embedded attribute packet's content,
  style, WCS position, normal, ordered direction/rotation, wrapping, attachment
  and spacing. Native MTEXT conversion comparisons cover three normals, both
  orientation orders and all nine attachments. SHX font lookup and projection
  cache dependencies also follow the embedded style. Unsupported backgrounds and
  vertical system-font text remain explicitly diagnosed.
- `DraftingAttributes` produces immutable, previewable instance edits with one
  undo operation. It validates ownership, selected INSERT, resource names,
  locked layers, constant values, finite dimensions and supported field ranges.
  Visible edits invalidate stale proxies; embedded edits retain fallback text.
  The shared `EATTEDIT`/`ATTEDIT`/`ATE` dialog has Attribute, Text options and
  Properties tabs, multi-attribute drafts, Apply, search and duplicate-tag notices.
- Native and WASM routes, agent read/edit controls, request schema discovery and
  portable owned-attribute workflow references use the same planner. Real browser
  checks cover Apply/OK, undo/redo, agent edits, SVG output and DXF save/reopen.
  Independent ezdxf audits report no errors or repairs and confirm separate
  fallback text and unchanged definition defaults.
- Local verification: 1,332 Advanced, 18 Shadow and 83 AgentNative checks;
  406 browser unit checks; 97 published native Chromium checks; 149 standalone
  WASM checks across root/subpath hosting without CAD API server requests.
  Both Release publishes pass, with all 90 routes and 972 published assets checked.
- This closes the dedicated instance-editor gap. Undoable Studio `ATTSYNC`,
  definition-attribute authoring, multiline transform coverage, background fills,
  annotation contexts and plot-style editing remain open. Full AutoCAD block
  parity is not established by these tests.

### Attribute membership and history increment

- Native `PrepareAttributeReplacement`/`ReplaceAttributes` validate ownership,
  duplicate objects and incoming references before edits. They preserve the
  collection and retained attribute objects, including distinct imported instances
  with duplicate tags. Prepared plans reject changed owner/document/membership.
  Reordering leaves retained handles and values intact.
- Native resource registration is independent of application subscription order.
  Attribute add/remove notifications observe complete membership; a failing
  notification is reported after commit, and reentrant membership edits are
  refused. Canonical resource adoption does not invoke authored property-edit
  callbacks. Removed attributes release layer, linetype, font and APPID uses.
- Exact same-tag definition binding and sequence-terminator restoration are public
  native operations. The source adapter journals the actual owned attribute
  collection, definition bindings and sequence identity. Empty/populated insert
  history, branch preview/merge and repeated undo/redo preserve native objects and
  stable Shadow IDs. Re-adoption can allocate new native handles; the allocator is
  monotonic. A callback failure that prevents exact graph rollback stops the source
  session and requires inspection/reattachment rather than silently continuing.
- The shared incoming-reference scan also covers exposed reactors, XData handles,
  XRECORDs and other modeled references. Referenced attributes and private owned
  extension dictionaries are refused before detachment. Sync and attribute
  transformation now restore the definition's justification. Single-line and
  multiline transformations both prepare all geometry before applying it, and
  invalidate the INSERT proxy only after successful geometry changes.
- Local verification: 174 native attribute cases, 688 INSERT cases, 526 saved-layer
  cases and all 1,338 Advanced checks pass. History tests cover both initially empty
  and populated attribute collections, repeated undo/redo, definition rebinding,
  branch preview/merge, foreign ownership and notification-failure handling.
  All five native target frameworks build. Published native Chromium passes 97
  checks; standalone WASM passes 149 across root/subpath hosting. Route/asset
  verification covers all 90 routes and the published shared assets.
- These primitives support the remaining ATTSYNC implementation; they do not yet
  provide the Studio command, all-reference reset policy or preview of dropped
  instance formatting/XData. Those requirements remain open.

### ATTSYNC planner and Studio increment

- Reusable immutable synchronization plans cover all references of a local named
  block, including layouts and nested definitions. Retained attributes keep object
  identities and assigned values; missing definitions add defaults, orphan
  attributes are removed, and duplicate tags retain distinct instances. Definition
  appearance and native insertion geometry replace instance overrides. The native
  geometry helper takes explicit MIRRTEXT policy and uses owner-unit conversion.
- `ATTSYNC` and Home/Insert ribbon controls create an edit preview. Before/after
  rendering and bounded reset/removal reports precede one explicit Apply operation.
  Reports include removed assigned values and INSERT XData. Stale proposals cannot
  apply. Native, WASM, MCP schema/tool access and portable workflow replay share the
  same planner, with no new host-specific geometry implementation.
- Unchanged text modes preserve fallback and embedded values independently. An
  explicit conversion option handles single/multiline transitions and reports
  formatting/fallback changes. That policy is documented without claiming exact
  AutoCAD conversion semantics. Nonuniform, sheared or reflected embedded MTEXT,
  protected orphan references and locked layers still cause explicit refusals.
- Local results: 1,350 Advanced checks, 409 browser units, 178 native attribute
  cases and 688 INSERT cases pass. All five native target frameworks build. The
  fresh published native browser suite passes 103 checks; standalone WASM passes
  161 across root/subpath hosting. Independent ezdxf audits accept synchronized
  drawings without repairs. All 90 route contracts and published assets validate.
- This closes the ordinary Studio ATTSYNC command/preview gap. Definition-attribute
  authoring, extended transform coverage, annotation contexts, block-library
  transfer and the other remaining scope above are still open. CI must qualify
  the next pushed revision separately from the green `4565d7d5` baseline.

### Definition ordering foundation

- Native attribute-definition order is now explicit rather than dependent on
  dictionary allocation slots. Reorder, live views, clone, new insertion and
  text/binary DXF retain the requested order. Reordering preserves definition
  objects, handles, ownership and resource references without membership events.
- Shadow applies only actual definition additions/removals, then updates order.
  Preview, branch synchronization and repeated undo/redo preserve retained native
  identities. Duplicate or mismatched tag keys are rejected before detachment.
- Local validation passes 184 attribute conformance cases, 688 INSERT cases,
  1,355 Advanced checks and Release builds for all five native target frameworks.
  Mixed addition/removal/order history and failed membership plans are covered.
  Hosted CI still needs to qualify the new pushed revision separately.
- The current focus remains the definition-attribute manager: tag rename, complete
  definition editing and shared `BATTMAN`/`ATTDEF` UI still need implementation.
  This foundation is not a claim that those workflows or full block parity are done.

### Definition tag and binding foundation

- Native prepared block tag renames support swaps and case changes while retaining
  indexed objects, order, handles, defaults and resource registrations. Plans reject
  destination collisions and changed source membership. Reversible native instance
  binding changes preserve assigned values and independent embedded text packets.
- Shadow structural patches coordinate definition keys and instance bindings after
  removals and before additions. Branch previews, all-reference synchronization,
  reuse of removed tags, insertion under a renamed definition's old tag and exact
  rollback are covered. Retained objects keep native handles across undo/redo.
- History can restore an exact stored instance binding that predates a definition-only
  rename. An unrelated mismatched binding cannot be authorized by supplying a history
  hint. New implicit INSERT attributes use proposed defaults, geometry, units, text modes,
  renamed resources and MIRRTEXT; unsupported frames fail during preparation.
- Review also repaired native block cloning so it retains insertion units, uniform-scale
  and explode settings, plus external-reference paths. INSERT clones retain their unit
  conversion. Text/binary fixtures exercise the copied metadata.
- Definition-only edits deliberately leave instance tags unchanged. Renaming while
  moving a definition between blocks is refused. These APIs are prerequisites for
  the requested shared `BATTMAN`/`ATTDEF` UI; that UI and the remaining block workflows
  are still open, as are the broader parity requirements above.
- Detached insertion previews still need explicit expansion of omitted default attribute
  nodes; source-side default construction is not evidence of complete preview geometry.
- Embedded style copies are refreshed after resource edits and before registration,
  preventing same-batch insertion from recreating a renamed style. Repeated undo/redo
  retains the original canonical style identity.
- Local verification: 1,370 Advanced checks, 190 native attribute cases, 688 INSERT
  cases and nine block-clone cases pass. All five native target frameworks build.
  Independent text/binary renamed-attribute DXF audits find no errors or repairs.
  Published native Chromium passes 103 checks; clean standalone WASM passes 161
  across root/subpath hosting. All 90 routes and 972 published assets validate.
  Shadow and AgentNative pass 18 and 83 checks. The new revision still requires
  its own hosted CI qualification.

### Graph workspace and docking increment

- [Graph workspace](GraphAnalysis.md): reusable immutable visualization pages, full scoped graph keys, source/root/occurrence links, component and feature colors, visual endpoint picking and source selection, all existing graph analyses, bounded paging and SVG/JSON export. Native/WASM share the same API and frontend.
- [Workspace layout](WorkspaceLayout.md): independent Properties and tool palettes, simultaneous opposite docks, movable/resizable floating windows, anchored auto-hide, layout lock/reset/persistence and compact palette discovery. Ribbon full/buttons/titles/tabs modes expose the same commands, including a dedicated Graph tab.
- [Coding workspace](coding-agents.md): one model/progress/context toolbar, a two-row composer, and bounded progress, task-settings and drawing-attachment overlays preserve conversation space. Approvals stay in the transcript; Send/Stop remain accessible while details are open.
- Geometric topology visualizations remain bounded XY projections of sampled curves. They do not infer piping/electrical semantics, create a force-directed graph, or claim complete AutoCAD CUI parity. Imported analysis receipts are not executable edits.

### Ribbon customization, layer isolation and discipline networks

- `CUI` edits tab/panel visibility and order, custom tabs and registered command
  panels. Studio JSON import/export and named workspaces preserve those choices.
  Panels can float across tab changes, suspend with hidden/disabled scopes, and
  return to the ribbon. Palette transparency includes normal/rollover opacity,
  apply-to-all and an opaque override; keyboard focus always restores opacity.
- Layer property filters and named groups are browser-local and follow stable
  layer IDs. `LAYISO`/`LAYUNISO` use native, undoable visibility/lock plans with a
  bounded DXF receipt. Restore preserves later conflicting property edits and
  newly added layers. Invalid receipts refuse isolation instead of overwriting
  unrelated application data. Native filter dictionaries and LAS transfer remain
  follow-up work.
- `netDxf.Shadow.Symbols` now provides explicit port/passage graphs, weak
  components, minimum-link paths and directional traces. Studio, MCP and the
  shared Modules panel use the same library with exact identity guards and five
  independently paged streams. JSON/SVG/CSV reports retain source identities,
  predecessor proofs and separate scan/declaration/finding/page coverage.
- Version `1.1.0` starter symbols declare internal passages while exact `1.0.0`
  versions and legacy drawing records retain unspecified connectivity. Geometry
  crossings do not create connections; missing declarations cannot prove a
  network disconnected. These are schematic analysis features, not hydraulic,
  switching-state or electrical-protection calculations.
- Review repaired oversized workspace saves, queued native-dialog close events,
  stale path highlights, obscured schematic direction arrows, and updates to
  imported lowercase APPIDs. XDATA spelling/order and unrelated payloads survive
  update, undo/redo and text/binary DXF reload. Failed structural transactions
  restore a changed public handle seed while keeping allocation monotonic.
- Local verification passes 1,481 Advanced checks, 1,378 discipline checks and
  663 browser unit tests. The fresh native publish passes 322 CAD browser checks
  and 35 HTTP contracts, including independent DXF audits and a 2,602-port
  imported discipline network. Native and standalone WebAssembly publishes pass,
  and all 99 server routes have browser transports. These results cover this
  increment; each pushed revision still requires its own hosted CI checks.

### Portable workspaces, saved layers and directed graph structure

- Named UI workspaces export/import a bounded, versioned Studio JSON envelope.
  Imports preview normalized docking, ribbon and palette choices, save under an
  explicit name and activate separately. Drawing identity and agent credentials
  are excluded. A Discipline analysis preset groups the analysis palettes.
- Saved-layer editing now changes individual entries and membership without
  restoring live layers. Native restore masks survive cloning and text/binary
  DXF; strict LAS transfer previews explicit missing-resource mappings and
  commits one undoable import. Saved opaque transparency retains its meaning
  across restore and DXF reload. Unsupported LAS payloads fail explicitly.
- Overlap selection cycling retains selectable roots and hit occurrences,
  previews without source edits, supports temporary Shift+Space cycling and
  fences stale requests. Keyboard Cancel retains native button activation.
- Directed discipline structure adds SCC membership, condensation, weak cut
  connections/articulation ports and bounded loop proofs. Six additional paged
  streams preserve large membership/proof sets. Source selection, inspectors
  and JSON/CSV/SVG exports share the native/WASM implementation and reusable
  iterative Graph kernels.
- Local verification passes 1,494 Advanced, 2,784 discipline, 29,163 Graph and
  698 browser unit checks. Native Chromium passes 358 full workflow checks;
  15 focused overlap checks cover the subsequent keyboard correction. HTTP
  contracts pass 38 checks. Standalone agent fixtures pass 326 checks without
  a companion or paid provider calls. Native and WebAssembly publishes pass.
  Hosted CI must still qualify the exact pushed revision.
- Remaining scope includes CUIx/native multi-monitor windows, native layer
  filter dictionaries, viewport/plot-style payloads and broader engineering
  workflows. Loop search returns at most one witness per strong group and
  reports its budgets; cut classifications describe retained connectivity,
  not simulated equipment behavior or operational risk.


### Keyboard ribbon, reference transforms and discipline workflows

- Ribbon, Quick Access and workspace menus share visible/enabled roving focus,
  spatial arrows, Home/End and scoped Escape behavior. Native inputs retain
  editing keys. Compact/floating/custom/contextual panels, customization redraws
  and viewport Shift+F10 have real browser coverage.
- ROTATE/SCALE support Reference and Copy with scalar or two-point measures,
  independent target points, snap acquisition and one undoable transaction.
  Rotation remains WCS-Z; scale is positive and uniform. Existing affine entity
  restrictions remain explicit. Copy preserves ordinary COPY metadata semantics.
- Reusable discipline isolation compares one captured graph before/after explicit
  port/connection exclusions, with exact proof paths, alternate routes, bounded
  traversal and honest unknown classifications. It adds four independent streams
  to Studio/MCP's existing graph receipt, plus shared inspectors and exports.
- `DomainSchedule.Build` and the Data manager expose searchable, grouped layout
  metadata, stable paging, units, hidden-root inspection, row editing and CSV/JSON.
  Straight source lengths and route IDs are descriptive quantities. Schedules do
  not infer purchased quantities or simulate engineering behavior.
- Review fixed stale source-row editing across document switches, non-finite
  imported block ports, optional fields accidentally required by HTML, and keyboard
  focus loss in menu/customization/floating controls. Installed-package scenarios
  now call schedule and isolation APIs and compare text/binary imported schedules.
- Local source verification passes 1,508 Advanced, 13,558 discipline and 733
  browser unit checks. Native HTTP contracts pass 40 checks. Focused native
  browser qualification covers 13 transform, 9 isolation, 10 schedule and 30
  keyboard checks with no uncaught errors or CSP violations. Full shared browser
  workflows and the exact pushed revision must pass hosted CI before merge.
- Follow-up discipline scope includes explicit connection repair/disconnect and
  reconnect authoring, richer reports/project workflows, catalog/version updates,
  and standards review. CUIx/native windows, curve/entity editing, native layer
  filters, viewport/plot-style payloads and the broader request inventory remain.
