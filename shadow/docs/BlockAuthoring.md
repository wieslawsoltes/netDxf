# Block definition authoring

Studio supports reusable native BLOCK creation and isolated definition editing.
The BLOCK command takes an ordered source selection, one base point and a name.
`BLOCK 10,20 Part A` converts the selection into one native INSERT referencing the
new definition. The Blocks palette's Create definition dialog also supports Retain
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
