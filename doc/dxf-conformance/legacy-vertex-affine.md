# Atomic 3D polyline and polyface affine transforms

`Polyline3D` and `PolyfaceMesh` now prepare all WCS vertices and the inherited
auxiliary normal before publishing a transform. Both overloads reuse the existing
`VertexAffineTransform` and exact dyadic affine-point implementation. No separate
numeric engine is added. Matrix4 input must be finite and have bottom row
(0, 0, 0, 1); projective inputs reject instead of discarding that row.

Every stored vertex and normal must be finite. The auxiliary normal must have
component-based squared length within 2e-15 of one. Retained polyline cardinality
and smoothing, and polyface active indices and retained face identities, are
validated before mutation. Rejection leaves vertex bits, containers, child
identities, metadata and proxy bytes unchanged. Derived Normal accessors are not
called during publication. Concurrent edits and callbacks from unrelated APIs
are not synchronized; this does not add document-level concurrency.

Successful changed geometry clears the parent proxy. Exact identity and
numerically unchanged output retain prior vectors and proxy bytes. Signed zero
is equivalent for the unchanged decision. Singular maps may collapse vertices;
an exactly collapsed auxiliary normal image retains the old auxiliary normal.
That vector is not a geometric plane normal for a nonplanar curve or mesh.

Final affine coordinates are correctly rounded from exact finite binary64 dot
products with translation. Final origin overflow, or a nonzero final coordinate
or auxiliary direction component that rounds to zero, rejects. Intermediate
product overflow does not lose a representable cancellation. Normalization uses
floating-point square roots and is not universally correctly rounded. Exact
arithmetic and prepared arrays impose CPU/allocation costs; no speedup is claimed.

## Tests and reproduction

The same 698 focused cases pass 114 before / 698 after the correction. Cases cover
both entities and APIs, nine matrix families, all six existing output profiles,
registered/detached and loaded sources, retained child XData and identity,
nonfinite components, projective rows, malformed source state, late range failure,
exact cancellation and derived Normal callbacks. Smoothed polyline control-net
transforms are exercised without changing the existing spline evaluator.

The independent checker regenerates 216 drawings / 1,296 WCS vertices using
Python Fraction exact sums and Decimal normal calculations. Complete selected
ordered POLYLINE/VERTEX/SEQEND packets are compared except handle values, whose
framing, uniqueness and child ownership are separately validated. Four ULP per
normal component and the 2e-15 squared-unit bound apply. All 48,480 changed/missing
packet controls and two inventory controls reject; all drawings have zero ezdxf
graph audit errors or repairs. Wire fixtures are unsmoothed and synthetic.

```sh
DXF_TEST_FILTER=legacy-vertex-affine/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_legacy_vertex_affine.py artifacts/conformance
```

The checker reuses independently maintained Python parser/arithmetic primitives
from verify_polygon_mesh_affine.py, not the C# implementation. Complete final
Debug/Release, hosted platforms and all-verifier results are recorded in the PR.

## Scope

Autodesk identifies 3D polyline/mesh vertices as WCS coordinates:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-D99F1509-E4E4-47A3-8691-92EA07DC88F5.htm
The VERTEX record defines its coordinate and flag fields:
https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm
These references do not prescribe this library's numeric admission/exception rules.

Loaded public coordinate slots are rewritten by the existing retained-record
writer. Child proxies, arbitrary private/header packets, XData coordinate payloads,
associations and external FIELD/TABLE caches are not regenerated or qualified.
This is not full private-graph transform equivalence or lossless fitted-child
regeneration. Historical typed dialects, general version conversion and native
AutoCAD open/AUDIT/save/reopen remain separate work. The JavaScript port is unchanged.
