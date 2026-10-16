# Exact hatch translation

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
