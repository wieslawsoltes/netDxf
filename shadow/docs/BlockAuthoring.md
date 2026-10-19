# Block definition authoring

Studio supports reusable native BLOCK creation and isolated definition editing.
The BLOCK command takes an ordered source selection, one base point and a name.
`BLOCK 10,20 Part A` converts the selection into one native INSERT referencing the
new definition. The Blocks palette's Create block dialog also supports Retain
(leave source objects) and Remove (remove source objects without inserting).
Coordinates are localized by subtracting the specified base point. The new
block's origin is zero and the reference is placed at the base point, so visible
geometry is unchanged by conversion. A complete operation is one undo entry.

`BEDIT` opens a named definition in a detached workspace. Construction, selection,
grips and admitted modification commands operate in definition coordinates, not
in an arbitrarily selected INSERT's transformed display space. `BSAVE` closes the
editor and adopts changes into the live document as a canonical transaction.
Every reference observes the new shared definition. `BCLOSE` discards the branch;
modified branches require explicit confirmation. Live/source revision checks and
unresolved merge conflicts prevent silent overwrites. The generic Workspaces
palette remains available for explicit branch review and conflict resolution.

The Blocks palette also provides undoable definition renaming. Case-insensitive
name collisions, native reserved names, external references, internal/layout
blocks, recursive inserts, foreign-owner selections and locked source edits are
refused before source publication. Recursion validation has bounded depth, visits
and cancellation. Selection copying preserves shared tables and referenced blocks,
while detaching entity-owned structures. An associated hatch requires its boundary
sources to be included in a new block selection; cross-owner associations are not
silently redirected.

## Reusable interfaces

`DraftingPlanner.CreateBlock` and `CreateBlockInDefinition` return immutable
`DraftingPlan` instances. The same plans work with source-backed `ShadowSession`
and `ShadowMemoryStore`. `PlanInBlock` exposes normal drafting against an explicit
named definition. `DraftingBlocks` contains name/reference validation and rename
planning independently of the application and rendering backend.

`SceneOptions.RootBlock` and `RenderCompileOptions.RootBlock` are opt-in immutable
presentation roots. Null retains the original layout behavior. Definition views
are cached separately, retain source identities and geometry, and do not compound
model annotation context or paper viewport projection. Root cycle detection and
scene budgets remain active. No drawing layout or source owner is temporarily
rewritten for a preview.

Studio's definition editor uses the existing detached branch, pinned resources,
merge planner and editor transaction implementation. New receipt-guarded routes
are `block/create`, `block/rename`, `block/begin`, and `block/finish` under each
`/api/cad/{id}`. Requests use the same identity, mutation-header and loopback rules
as other editing operations.

## Scope

This implements ordinary named definitions and references, not AutoCAD dynamic
block action graphs, constraints/visibility states, external-reference in-place
editing, standalone model-space ATTDEF placement, or arbitrary 3D modeler operations.
Objects unsupported by the representation-preserving transform/copy planners
are refused; they are never flattened to sampled display lines for block creation.
Native roundtrip, deep-copy isolation, recursive/foreign/locked refusals, scene
cache separation, undo/redo and Studio save/discard have dedicated verification.
The real-origin Chromium test authors, edits, saves, undoes and independently
audits the resulting DXF using the real native backend.

Native block and INSERT clones preserve block insertion units, uniform-scale and
explode settings. Block clones also retain external-reference paths and modes;
cloning does not attach the copy to the original document or layout.

## Integration contracts

The existing `BlockSourceDisposition` numeric values remain Retain=0,
ConvertToInsert=1 and Delete=2. The command API adds Convert and Remove as
aliases, not new wire values. Creation shares one planner implementation; the
existing redefinition API and source-preserving origin behavior remain available.
Both `BLOCK name base-point` and `BLOCK base-point name` are accepted, with the
existing name-first interactive prompt retained. `BEDIT name` can open a named
definition directly. The existing rename route keeps its structured ObjectId
payload; it is registered only once.

Existing block and hatch authoring dialogs target the active definition while
its editor is open. They do not write hidden geometry to the live model layout.
Definition workspaces inherit perspective settings without projecting their local
geometry; returning to the live workspace restores its independent view context.
Save As, DWG exchange, hatch pattern editing and the main CAD landing page remain
unchanged. Source collection adoption orders owned payload, shared tables and
entity membership, then detaches in reverse order. Rollback is prepared from
actual membership and does not clear unchanged reserved table entries.

## Instance attributes

Native consumers can change definition order with
`block.AttributeDefinitions.Reorder(definitions)`. Supply every current definition
exactly once. Enumeration, tag/value views, cloned blocks and new insertions follow
that order. Existing definitions retain their objects, handles, owners and resource
registrations; existing INSERT attributes keep their current order until explicitly
synchronized. No membership notifications are raised for a reorder. The native API
allows constant definitions in the ordering; a prompt-oriented UI can restrict them.

Shadow collection edits use this operation after applying only actual additions
and removals. Pure reordering therefore preserves native handles through preview,
branch merge and undo/redo, including definitions referenced by other objects.
Duplicate or mismatched dictionary tags are rejected before native membership edits.
The shared `BATTMAN` manager uses this foundation for staged prompt ordering.

`block.PrepareAttributeDefinitionRenames(renames)` accepts current-to-new tag
pairs and returns a single-use `Apply()` plan. `RenameAttributeDefinitions` prepares
and applies directly. The complete destination index is checked before changing
anything, so tag swaps and case changes preserve definition objects, order,
handles, resource references and defaults. Preparation refuses unknown, empty or
colliding tags; applying refuses changed definition membership, order or tags.
Membership notifications are not raised for renaming.

For an instance that should follow a renamed definition, prepare
`attribute.PrepareDefinitionBinding(newTag, definition)`, rename the definition,
then call the binding change's `Apply()`. Assigned values, independent embedded
text, appearance, object identity and handles remain intact. `Rollback()` restores
the exact captured binding, including a binding that predates a definition-only
rename. Changed instance bindings are refused. Definition-only renames leave
instance tags unchanged; DXF reload resolves definition bindings by their tags.
Native import/history consumers can explicitly set `allowTagMismatch` when preparing
a stored binding. Shadow permits this only for the exact tag and definition pair
in a source-owned historical snapshot; a history hint does not authorize arbitrary
mismatched bindings.

The source adapter handles these compound identities in structural graph patches.
Update definition tags together with the owning dictionary's keys and tag view;
explicitly update instance tags/bindings when those instances should follow. The
adapter validates proposed names, releases actual removed membership, renames
retained definitions and updates bindings, then adopts additions. This supports
swaps, reuse of removed tags and adding a definition under a retained definition's
old tag in one history operation. Undo restores the corresponding native objects.
Renaming a definition while moving it to another block is currently refused.
The shared definition manager combines this tag/binding work with appearance and
membership changes, optionally synchronizing existing references in the same batch.

New source INSERTs with omitted attributes prepare their defaults from the proposed
definition state. A single batch can edit tags, defaults, geometry, text modes,
resource names or units and then insert the result. Preparation uses the proposed
owner units and drawing MIRRTEXT policy and rejects unsupported text frames before
live mutation. Attributes bind to the retained definitions before registration;
custom registered INSERT factories keep their authored defaults. Unused definition
XData, extension dictionaries and proxy payloads are not cloned for this preparation.
Embedded styles are refreshed after resource edits, before registration, so their
detached text packets do not recreate an old style name after a same-batch rename.
This low-level source construction step does not expand omitted attribute nodes
in a detached preview. Studio insertion now uses the explicit construction planner
described below so its previews contain those nodes before native publication.

## Insertion values and previews

`INSERT`, the block picker and repeated placement collect values after positioning
the reference. `ATTREQ 0` uses definition defaults; `ATTREQ 1` enables value entry.
`ATTDIA 1` opens the shared attribute dialog, while `ATTDIA 0` uses command prompts.
Both settings default to 1 and persist in the local UI preferences. `-INSERT`
uses command prompts regardless of ATTDIA, while still honoring ATTREQ.

Constant definitions retain their defaults and are not prompted. Command entry
skips ordinary preset prompts; dialog entry includes editable preset values.
Verify definitions receive a second opportunity to confirm or correct their
values. Invisible attributes remain editable during insertion. Command-line Enter
keeps the displayed default, two quotation marks clear it, and other input retains
literal spaces and words such as END rather than treating them as snap overrides.
Multiline values support MTEXT controls and normalize newlines to paragraph breaks.
Their independent single-line fallback remains the definition's fallback value.

The complete reference and its attributes publish as one undo operation only after
value collection succeeds. Cancel/Escape discards the pending insertion. Each
repeated placement starts from current definition defaults and gets its own values
and undo entry. Drawing/workspace/revision changes invalidate pending value entry.
These controls follow Autodesk's [ATTREQ](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-Core/files/GUID-7A7229F8-C99D-42C5-8784-4231767611F8.htm),
[ATTDIA](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Core/files/GUID-DD7AE7D3-D9D7-49E7-948E-079D8451FFA2.htm)
and [attribute-mode documentation](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-Core/files/GUID-92A7CE9D-2E1C-415E-A1BF-5CB35D5E2D38.htm).

`InsertAttributeConstruction.Plan` in `netDxf.Shadow.NetDxf` expands an empty
reference in an immutable proposed snapshot into explicit owned attributes. It
uses the same native transformation as synchronization, including definition
origin, insertion frame, owner units, MIRRTEXT and embedded text. It never changes
another reference or clears insertion XData. Optional `InsertAttributeAssignment`
values identify exact definitions; duplicates, foreign definitions, changed
constant defaults and invalid text are rejected before publication.

Library callers first apply the source-independent `DraftingPlanner` placement
patch to a snapshot, then apply the construction patch for the created INSERT.
Compose the result with `ShadowGraphPatch.Between` and execute once. Studio does
this for ordinary INSERT commands, `CadInsert`, recorded workflows and typed edit
previews. `CadInsertOperation` combines a command and optional assignments through
the native/WASM `insert` route. An edit-preview operation with `kind: "insert"`
and its `insert` payload uses the same plan; a normal command preview also includes
explicit defaults. These are complete drawing previews; cursor-following block
graphics and richer insertion acquisition remain future work.

`EATTEDIT`, `ATTEDIT` and `ATE` open the shared instance editor. Its Attribute,
Text options and Properties tabs edit values and supported appearance fields;
Apply submits all authored differences as one undo operation. Multiline content
uses the captured embedded packet and retains the separate single-line fallback.
`DraftingAttributes` supplies the same immutable plans to native, browser and
agent workflows. Editing instance values does not change block defaults.

Native consumers can prepare an exact membership change with
`insert.PrepareAttributeReplacement(attributes)` and call the returned plan's
`Apply()`, or use `insert.ReplaceAttributes(attributes)` directly. These methods
preserve surviving objects and the owned collection; they do not generate or
transform text. `attribute.SetDefinition(definition)` rebinds a matching tag
without changing values. `SetAttributeSequenceEnd` supports restoring a previously
captured terminator, including history for a previously empty INSERT.

The source adapter journals these operations with stable Shadow identities.
Re-adopting a removed object can allocate a new native handle. Native membership
notifications occur after commit; if an application callback fails, the native
call reports that failure. A source journal that cannot restore the full public
graph stops further edits until inspection and reattachment.

## Definition attributes

`BATTMAN` and Manage attributes in the Home/Insert ribbon edit the selected
reference's block, the active `BEDIT` definition, or a named block chosen from the
drawing. The shared manager edits tags, prompts, defaults, verify/preset flags and
supported text/property fields. Constant defaults remain editable; constant and
multiline modes are read-only after creation. Ordering and removals stay staged
until Apply or OK. Apply keeps the manager open, and OK after Apply does not create
another history entry. The last definition cannot be removed through this manager.

Apply changes to existing references is selected initially. It combines definition
edits with the all-reference synchronization policy below: retained assignments
survive tag renames, while formatting resets, orphan removal and INSERT XData
removal are undoable together. Clear that option to edit definitions independently.
Sync references explicitly runs synchronization even without staged field changes.
Use `ATTSYNC` or the typed edit-preview operation when a separate review is needed.

`ATTDEF` and Define attribute add a definition directly to an existing local named
block. Specify its tag, prompt, default, definition coordinates, height, style,
layer and modes. Position locking requires DXF 2010 or later; multiline content
requires DXF 2018. Unsupported modes are disabled for older drawings and rejected
by the planner. Newline characters become embedded MTEXT paragraphs; the initial
single-line fallback uses spaces. Existing references acquire new definitions only
when synchronized. Inside `BEDIT`, authoring stays in the branch until save, and
discard leaves the live drawing unchanged.

`DraftingAttributeDefinitions.Read` and `Plan` live in the reusable Drafting library.
They validate identity, ownership, tag uniqueness, layers, resource names, profile
capabilities and bounded geometry before publishing changes. Studio composes the
plan with `AttributeSynchronization.Plan` when requested. Native and WebAssembly
hosts share GET/POST `attribute-definitions` routes, agent request schemas and
portable workflow references. Typed `attribute-definitions` preview operations
carry `attributeDefinitions: { block, changes, updateExisting, convertTextModes }`;
previews include synchronization reports and require the normal identity guards.

The dialogs follow the ordinary [Block Attribute Manager workflow](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Core/files/GUID-16555B43-D4FA-4C8E-BDF5-4256E9166A6F.htm).
Standalone model-space ATTDEF placement, manager
column settings/automatic previews, annotation contexts and plot-style editing
remain outside this increment. This is not complete AutoCAD block parity.

## Synchronize attributes

`ATTSYNC` and the Home/Insert ribbon's Synchronize attributes action select a
local block by name, defaulting to the selected reference's definition. Create
synchronization preview prepares every native reference, including other layouts
and nested definitions. The Edit previews palette shows before/after images,
reset counts, removed attributes and their values, and INSERT XData removal.
Apply adopts the exact reviewed proposal as one undo operation. A changed drawing,
workspace, selection, view or resource receipt invalidates the proposal.

`AttributeSynchronization.Plan` in `netDxf.Shadow.NetDxf` is reusable without
Studio or a live document. It returns an immutable patch and complete per-reference
reports. The same planner supports source sessions, detached branches and workflow
replay. Studio and MCP return bounded summaries with explicit truncation; MCP's
`studio_edit_preview` accepts a typed `attribute-sync` operation with
`attributeSync: { block: { value: "<object-id>" }, convertTextModes: false }`.
Existing preview/apply routes work in native and standalone WebAssembly hosts.

Retained tags keep their instance identities and assigned values. Imported
instances with duplicate tags remain distinct and are grouped in definition
order. Missing attributes receive definition defaults; orphan attributes are
removed. Definition appearance and native transformed geometry replace instance
overrides. Both fallback and embedded values remain independent when the text
mode is unchanged. Native `Attribute.CreateTransformed` calculates detached
attribute geometry using an explicit insertion frame and MIRRTEXT policy, without
changing global settings. Block origins, owner units and non-world normals use
native calculations.

Changing text mode requires explicitly enabling conversion in the dialog or
request. Single-line values become literal escaped multiline text. Converting
multiline to single-line uses visible text, removes inline formatting and replaces
paragraph breaks with spaces; it replaces the independent fallback. The review
reports that conversion, and Undo restores both original stored values. This is
an explicit Studio conversion policy, not a claim of identical AutoCAD conversion
semantics for every imported text packet.

The reset follows the scope of Autodesk's documented
[ATTSYNC behavior](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Core/files/GUID-56B14079-250B-4C99-AB3D-F95BA1C32AB7.htm).
Only INSERT XData is cleared; definition and retained attribute metadata are not
silently deleted. Native incoming references and extension-dictionary ownership
can prevent orphan removal. Locked references/attribute layers, external blocks,
inconsistent ownership and exceeded budgets are refused. Native embedded MTEXT
currently rejects nonuniform scaling, shear and reflection; a reference requiring
one of those transformations rejects the entire proposal before source changes.
Definition-attribute authoring, annotation contexts and complete dynamic/parametric
block parity remain open in the [parity audit](ParityAudit.md).
