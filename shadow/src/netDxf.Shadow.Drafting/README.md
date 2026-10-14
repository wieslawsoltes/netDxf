# Reusable CAD drafting

`netDxf.Shadow.Drafting` depends on the immutable shadow/editor/rendering contracts,
not netDxf, ASP.NET, SkiaSharp, or a particular application. A `DraftingPlanner`
validates detached command input and returns a `DraftingPlan`. Planning has no side
effects. Execute the plan through an `EditorSession` for one canonical undo entry,
or apply its patch to a `ShadowMemoryStore` for an isolated preview.

```csharp
var planner = new DraftingPlanner();
var plan = planner.Plan(editor.Snapshot,
    new DraftingCommand("RECTANG", [new(0, 0), new(120, 80)], Layer: "0"));
plan.Execute(editor);
editor.Undo();
editor.Redo();
```

Every plan carries document identity and expected revision. Stale plans fail before
source adoption. Entity edits reject locked layers; creation rejects locked/frozen
layers. The host must additionally guard document/workspace identity and serialize
its own scene/selection context. Immutable plans do not grant authorization.

## Construction and modification

| Command | Contract |
| --- | --- |
| LINE / PLINE | Point chain; LINE is one transaction even when it creates many entities |
| RECTANG / POLYGON | Closed XY polyline; polygon uses center/radius and 3–1024 sides |
| CIRCLE / ARC / ELLIPSE | Analytic geometry; ARC is three-point, ELLIPSE is center/major endpoint/minor point |
| POINT / TEXT / MTEXT | Explicit location, text height, text content and optional rotation/MTEXT width |
| SPLINE | Clamped cubic control-point NURBS, not a sampled polyline or fit-point spline |
| DIMALIGNED / DIMLINEAR | Native dimension definitions; two references plus dimension-line point |
| INSERT | Existing named block, position, uniform scale and rotation |
| MOVE / ROTATE / SCALE / MIRROR | Explicit base point/axis; representation-preserving existing affine planners |
| COPY / ARRAY | Deep entity-owned data copies, shared table/block definitions; rectangular array |
| ERASE | Structural removal with canonical undo/redo |
| OFFSET | Lines, circles/arcs, and simple straight polylines; side point plus positive distance |
| TRIM / EXTEND | LINE endpoint editing against an intersecting finite LINE or CIRCLE boundary |
| FILLET / CHAMFER | Two intersecting XY lines; trim to a tangent circular arc or equal-distance bevel |
| JOIN | Unbranched connected LINE chain; endpoint adjacency rather than repeated edge scans |
| EXPLODE | XY Polyline2D to analytic lines/arcs, including bulges |

Coordinates use drawing units; angles use degrees. ARRAY uses `Count` columns,
`Value` integral rows, and the delta of its two points as X/Y cell spacing. The
original occupies the first cell. COPY uses its two points as displacement.

## Explicit boundaries

This is not an AutoCAD compatibility claim or a solid-modeling kernel. Unsupported
combinations fail explicitly without changing the source: general curve trimming,
self-intersecting offsets, tapered-polyline explosion, arbitrary block explosion,
non-XY planar commands, and automatic metadata redistribution for JOIN/EXPLODE.
COPY preserves captured owned values rather than copying tessellated render output.
The source adapter can reject a data-model shape for which no public reconstruction
contract exists; the transaction is not partially accepted.

Drafting budgets are configurable. The default bounds point/entity/clone counts and
coordinates; OFFSET self-intersection verification has an additional 1024-edge cap.
Source-normalization, arbitrary file/font resolution, render fidelity and numerical
conditioning retain their separate contracts in the respective infrastructure.

`DraftingVerification` runs in the existing cross-platform advanced verification
suite and exercises source/memory stores, DXF round trips, undo, copies, construction,
modification, stale input, locked layers and negative input controls.
