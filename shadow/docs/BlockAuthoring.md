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
editing, attribute-definition authoring, or arbitrary 3D modeler operations.
Objects unsupported by the representation-preserving transform/copy planners
are refused; they are never flattened to sampled display lines for block creation.
Native roundtrip, deep-copy isolation, recursive/foreign/locked refusals, scene
cache separation, undo/redo and Studio save/discard have dedicated verification.
The real-origin Chromium test authors, edits, saves, undoes and independently
audits the resulting DXF using the real native backend.

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
This is a reusable foundation for the forthcoming definition-attribute manager;
it does not itself provide `BATTMAN`, `ATTDEF`, or tag renaming in Studio.

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
