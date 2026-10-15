# Native block and hatch authoring

This extends the merged CAD workspace with source-neutral plans in
`netDxf.Shadow.Drafting`. Every accepted operation executes through
`EditorSession`: source synchronization, canonical undo/redo, isolated memory
branches and DXF save continue to use the same infrastructure.

## Block definitions

Select layout-root entities, then use **Home / Block / Create block**, the
**Insert** ribbon or **Blocks** palette. Supply a name, WCS base point,
description and one source disposition:

- Retain leaves the original entities and adds only the reusable definition.
- Convert replaces the selected roots with one native INSERT at the base point.
- Delete removes the selected roots while retaining the named definition.

The planner copies entity-owned data, retains shared layers/styles/nested
block references, and translates copied geometry into definition coordinates.
It does not explode or approximate curves into rendering segments.

`BLOCKREDEFINE` or the Redefine dialog replaces a named definition from selected
layout-root entities. The definition identity and origin remain stable, so all
existing INSERTs display the replacement without moving. Omitted descriptions
are preserved; an explicitly supplied description replaces the previous value.
`BLOCKRENAME` updates the native block table while preserving reference identity.
These are each one history entry, including structural source restoration.

```text
REC 10,20 50,60
B Symbol 10,20
I Symbol 100,20 1 0
BRENAME Symbol Assembly
C 200,100 8
BREDEFINE Assembly 200,100
U
```

The example creates a rectangle, groups it into a block, inserts another
reference, renames the shared definition, replaces its contents with a circle,
and undoes that replacement. Quote block names containing spaces in inline
commands. The interactive command line also accepts names directly at prompts.

### Reusable API

```csharp
var planner = new DraftingPlanner();
var request = new BlockDefinitionRequest(
    Name: "Valve",
    BasePoint: new Vec3(10, 20),
    Entities: selectedSourceIds,
    Disposition: BlockSourceDisposition.ConvertToInsert,
    InsertLayer: "0");
var plan = planner.PlanBlockDefinition(store.Current, request, "Model", token);
var preview = plan.Patch.Apply(store.Current); // Detached, immutable preview.
plan.Execute(editor);                        // One canonical transaction.
```

Redefinition uses the same method with `Redefine` set to an existing definition
ID. Sources must be direct roots of the specified layout. Name collisions,
recursive definitions, incomplete external entity dependencies, locked source
layers for destructive dispositions and bounded-work failures are rejected
before publication. Xrefs and implicit attribute-definition replacement are
not admitted. This is not a dedicated BEDIT canvas or dynamic-block authoring.

The source allocator now distinguishes real owned references from inverse
`Owner` links and shared `INSERT.Block` constructor dependencies. Read-only
metadata alone is not sufficient evidence of ownership. Explicit addition-order
regressions exercise this distinction without changing the linear metadata
index or its existing allocation/identity tests.

## Hatch construction and editing

Select closed XY source boundaries and run `HATCH` (`H`) or open **Hatch**.
Admitted boundaries are circles, full ellipses, and closed zero-width 2D
polylines, including analytic bulges. Separate nested or disjoint loops can be
selected together. Arbitrary open chains should be joined into a valid closed
polyline first. `HATCHPOLY` (`HP`) constructs a solid hatch directly from three
or more prompted polygon vertices, without leaving extra editable outlines.

```text
REC 0,0 80,60
H LINE 4 30 Normal
HE CROSS 6 45 Outer
HP 100,0 140,0 140,30 100,30 C
```

Patterns use a finite positive scale, angle in degrees and explicit WCS origin.
Normal alternates nested islands; Outer keeps the outer band; Ignore fills
through internal loops. The existing bounded island resolver validates contour
simplicity, contact and nesting before publication. The analytic boundary data
is retained; its topological classification uses the resolver's documented
sampling/contact tolerances, not a claim of exact algebraic Boolean geometry.

The built-in generic patterns are `SOLID`, `LINE`, `CROSS`, `DOTS`, and `DASH`.
The Hatch dialog can import custom PAT files with explicit line families:

```text
*SYNTHETIC, Example line family
30,0,0,0.5,2,1,-1
```

Each row is angle, origin X/Y, along-line delta, perpendicular delta, followed
by optional signed dash lengths. Positive lengths draw strokes, negative lengths
are gaps, and zero lengths are dots. The parser accepts decimal/scientific
numbers, semicolon comments, BOM and CRLF; it never evaluates expressions,
loads includes, follows resource URLs or accesses implicit paths. Imports are
atomic and bounded to 1 MiB, 128 patterns, 128 families per pattern and 128 dash
items per family. Names must be unique and cannot replace the reserved SOLID
pattern. The renderer's row/segment/work budgets are preflighted separately.

`HATCHEDIT` (`HE`) and **Edit hatch** replace pattern data copy-on-write; another
hatch sharing the old pattern is unaffected. The dialog reads the complete
current source pattern, including custom families, description, origin and
island policy. A native receipt and explicit source IDs protect delayed dialog
completion from switching silently to another selection or revision.

New hatches are **non-associative** analytic copies. Editing the original
boundary later does not regenerate their shape. Pattern edits preserve native
boundary paths. Automatic boundary discovery from an interior point, gradient
authoring, full spline-boundary authoring and associative boundary maintenance
are separate features, not approximations hidden behind these controls.

### Reusable API

```csharp
var request = new HatchAuthoringRequest(
    Boundaries: selectedSourceIds,
    Pattern: new DraftingHatchPattern("CROSS", Scale: 4, Angle: 30),
    Layer: "0");
DraftingHatches.Create(store.Current, request, "Model", token).Execute(editor);

var current = DraftingHatches.ReadPattern(store.Current, selectedHatchId);
DraftingHatches.ChangePattern(store.Current, [selectedHatchId],
    current with { Scale = 8 }, token).Execute(editor);
```

Provide explicit `Polygons` instead of `Boundaries` for host-generated closed
regions. Provide immutable `DraftingHatchLine` descriptors for custom patterns.
The planner and query contracts contain no netDxf instances, HTTP requests,
Skia objects or browser state. The adapter constructs public native boundary
objects through audited factories.

## Verification

The native authoring tests are registered in the existing advanced suite.
They cover source and memory stores, dispositions, insertion/base-point
semantics, rename/redefine identity, constructor-order independence,
ASCII/binary DXF round trips, analytic circle/ellipse/bulge boundaries, islands,
pattern sharing, metadata, cancellation, quotas and atomic refusals.

`cad-authoring.test.mjs` covers command routing, source identities, PAT grammar
and limits, and serialized stale-dialog rejection. `cad_authoring_browser.py`
is called by the existing real-origin Chromium workflow: it exercises command
and dialog authoring against the actual published host, reads back pattern
values, verifies undo and shared references, audits the downloaded DXF with
an independent reader, and reopens it in a new document. Existing base
conformance, runtime/package and security gates remain active.

This increment implements the described authoring operations. It is not a
claim of complete AutoCAD command, visual, DWG, 3D-solid or plot-style parity.
