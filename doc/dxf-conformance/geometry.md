# Geometry and display data

Geometry operations have entity-specific contracts. A transform that is valid
for a line may not preserve a circle, bulged polyline, hatch or retained native
packet. Check the operation's XML documentation for finite-value, rank, tolerance,
source-profile and rollback requirements before applying it to a mixed selection.

## Curves and transforms

Use explicit spline operations for knot insertion, reversal, splitting, trimming,
degree elevation and supported conversions. Parameter domains, periodic seams,
control weights and endpoint behavior are part of those operations; uniform
sampling is not a replacement for the curve's parameterization.

Nonuniform scaling or shear can require an explicit conic/spline conversion.
Do not assume an entity-preserving transform will automatically change the entity
type. Positions, direction vectors, normals and extrusion planes also have
different transformation rules. Direct component mutation may bypass the guarded
methods described in [editing](editing.md).

The [entity APIs](../../netDxf/Entities) and focused regressions such as
[active spline domains](../../tests/netDxf.Conformance/SplineActiveDomainTests.cs),
[spline trimming](../../tests/netDxf.Conformance/SplineTrimTests.cs) and
[conic conversion](../../tests/netDxf.Conformance/ConicSplineTests.cs) provide
current examples. Numeric oracles and independent exported-wire checks live in
[tools](../../tools).

## Polylines, meshes and hatches

Legacy POLYLINE child records carry topology, flags and source metadata in
addition to coordinates. Polygon meshes, polyface meshes and modern MESH have
different cardinality and indexing rules. Use the appropriate topology/editing
API and validate the intended target version before export. Retaining child
records can impose source-profile restrictions that an authored mesh does not
have.

A HATCH combines boundary geometry, pattern/gradient settings, seeds, source
relationships and display data. Editing one representation does not automatically
recompute the others. Supported affine operations validate their own boundary
and pattern contracts; arbitrary private spline packets and singular conversions
are not automatically admitted. Changes to source entities also need the relevant
relationship and graphics policy.

The [hatch affine verifier](../../tools/verify_hatch_affine.py),
[polyline topology verifier](../../tools/verify_polyline_topology.py) and
[mesh topology verifier](../../tools/verify_mesh_topology_edits.py) check exported
results independently. Producer fixtures and their provenance remain under
[tests/fixtures](../../tests/fixtures), including the
[polygon-mesh corpus](../../tests/fixtures/polygonmesh-cardinality).

## Geometry is not a renderer

Keeping proxy graphics, image/underlay references, dimension blocks or embedded
ACIS data does not establish native rendering or application regeneration.
Font metrics, rich-text layout, external resources and regenerated pictures can
require explicit host services. The [Shadow modules](../../shadow/README.md)
provide separate geometry, rendering and editing facilities; their contracts
should be checked independently of core DXF storage.

Use [compatibility analysis](compatibility.md#check-a-target-before-saving) before
changing the target family, [verification](verification.md) to run the current
tests, and [history](history.md) for the detailed original geometry reviews.
