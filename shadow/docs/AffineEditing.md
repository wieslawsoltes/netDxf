# Detached affine editing

`AffineEditOperations.Plan` produces a `ShadowGraphPatch` without accessing netDxf or invoking a renderer. `EditorSession.Transform` applies it through the same canonical structural undo/redo as add/remove operations. Plans carry the source document/revision and reject stale application. Passing a `RenderScene` maps the selected occurrence through its inverse paper/insert transform; incompatible simultaneous edits of shared definitions are rejected.

The planner supports general affine lines, points, faces, 3D polylines, meshes, spline coordinates/tangents, ellipses, image frames and single-line text. Ellipse axes use a scaled two-dimensional singular-value decomposition; the adapter uses the public `Ellipse.SetAxis` compound mutator. Read-only fixed-size arrays are updated in place with cardinality/type validation and value rollback. Array membership resizing remains unsupported.

Planar circles/arcs and bulged or wide polylines require plane similarities; nonuniform scale/shear that requires changing the DXF entity type is rejected. Straight zero-width polylines support general planar affine transforms. INSERT frames must remain orthogonal and nonsingular; attached attributes are transformed together with their parent. Sheared INSERTs and array INSERTs are rejected rather than flattened. Nonzero thickness is rejected if the transformed extrusion no longer fits the target entity's normal.

Multiline text supports plane similarities; scaling text containing explicit inline height controls is conservatively rejected pending a rich-text scale planner. Dimension, viewport camera and embedded MTEXT attribute affine plans are not supplied. HATCH supports only the [exact translations](#exact-hatch-translation) described below. The source graph remains unchanged when planning rejects an unsupported transform.

Cancellation, target/coordinate budgets, finite/invertible transforms and shared-coordinate conflicts are checked before source application. Native source event callbacks are still governed by the serialized adapter contract; irrecoverable rollback fail-stops the session. Structural planning/adoption still has whole-graph work and does not claim constant-time affine edits.

```csharp
var pivot = new Vec3(100, 50);
var rotate = Affine3.Move(pivot) * Affine3.RotateZ(Math.PI / 4) * Affine3.Move(-pivot);
editor.Transform(scene, rotate); // display-space selection; outer insert by default
editor.Undo();
editor.Transform(scene, rotate, editBlockDefinition: true);
```

## Exact hatch translation

`AffineEditOperations.Plan` now admits exact translations of non-associative
HATCH entities with solid or line-family patterns. The same immutable plan is
used by source-backed `EditorSession` transactions, isolated memory stores,
MOVE/COPY/ARRAY and nonzero-base-point block authoring.

Translation is mapped from WCS into the hatch's existing OCS. It moves line
endpoints, conic centers, polyline vertex XY, spline control/fit XY, seed points
and elevation. Polyline bulges and rational weights are not spatial Z values;
knots, axes, radii, angles, tangents, pattern scale, flags and family offsets
remain unchanged. Native edge/path identities and path flags are retained.

Pattern origin moves once in OCS. Patterns and their line definitions are
copy-on-write, so another hatch sharing the original pattern is unaffected.
Unreachable retired pattern nodes are pruned from the detached target graph;
canonical source history restores original bindings during undo/redo.

Shared mutable boundary data, associative hatches, gradient patterns and affine
maps with any nonidentity linear component are refused before source mutation.
This is not a general hatch rotation/shear/scaling or associative regeneration
implementation. The existing entity/coordinate budgets remain in effect; the
coordinate limit additionally bounds owned-object/dash traversal. The reverse
reference index is built lazily once per hatch edit plan over the input graph.

Native regressions cover nonzero block base points, source and isolated stores,
pattern sharing, analytic edge data, four OCS orientations, repeated history,
ASCII/binary DXF reopening and negative controls. The real-origin browser's
existing patterned-hatch BLOCK scenario is unchanged and remains mandatory.
