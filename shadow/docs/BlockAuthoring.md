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

Studio `ATTSYNC`, definition-attribute authoring and the all-reference policy for
resetting instance formatting/XData remain separate unfinished work. See the
[parity audit](ParityAudit.md) for those requirements and verification evidence.
The target is Autodesk's documented
[ATTSYNC behavior](https://help.autodesk.com/cloudhelp/2025/ENU/AutoCAD-Core/files/GUID-56B14079-250B-4C99-AB3D-F95BA1C32AB7.htm):
update all references while retaining assigned values and reviewing the removal
of instance overrides and extended data.
