# Detached affine editing

`AffineEditOperations.Plan` produces a `ShadowGraphPatch` without accessing netDxf or invoking a renderer. `EditorSession.Transform` applies it through the same canonical structural undo/redo as add/remove operations. Plans carry the source document/revision and reject stale application. Passing a `RenderScene` maps the selected occurrence through its inverse paper/insert transform; incompatible simultaneous edits of shared definitions are rejected.

The planner supports general affine lines, points, faces, 3D polylines, meshes, spline coordinates/tangents, ellipses, image frames and single-line text. Ellipse axes use a scaled two-dimensional singular-value decomposition; the adapter uses the public `Ellipse.SetAxis` compound mutator. Read-only fixed-size arrays are updated in place with cardinality/type validation and value rollback. Array membership resizing remains unsupported.

Planar circles/arcs and bulged or wide polylines require plane similarities; nonuniform scale/shear that requires changing the DXF entity type is rejected. Straight zero-width polylines support general planar affine transforms. INSERT frames must remain orthogonal and nonsingular; attached attributes are transformed together with their parent. Sheared INSERTs and array INSERTs are rejected rather than flattened. Nonzero thickness is rejected if the transformed extrusion no longer fits the target entity's normal.

Multiline text supports plane similarities; scaling text containing explicit inline height controls is conservatively rejected pending a rich-text scale planner. Dimension, HATCH, viewport camera and embedded MTEXT attribute affine plans are not supplied by this increment. The source graph remains unchanged when planning rejects an unsupported transform.

Cancellation, target/coordinate budgets, finite/invertible transforms and shared-coordinate conflicts are checked before source application. Native source event callbacks are still governed by the serialized adapter contract; irrecoverable rollback fail-stops the session. Structural planning/adoption still has whole-graph work and does not claim constant-time affine edits.

```csharp
var pivot = new Vec3(100, 50);
var rotate = Affine3.Move(pivot) * Affine3.RotateZ(Math.PI / 4) * Affine3.Move(-pivot);
editor.Transform(scene, rotate); // display-space selection; outer insert by default
editor.Undo();
editor.Transform(scene, rotate, editBlockDefinition: true);
```
