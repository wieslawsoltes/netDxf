# Staged ordinary Polyline2D affine transformations

The ordinary (authored/lightweight, without retained legacy VERTEX records)
`Polyline2D.TransformBy` path now prepares the complete result before publishing
any changes. The existing `PlanarEntityTransform` computes the plane from the
image of its in-plane basis and transforms elevation and signed thickness.
This replaces deriving a normal directly from `A * normal` and inferring
one elevation from the last vertex. A translated, shared vertex object is no
longer transformed repeatedly: each candidate is computed from the original
position before any shared object is assigned.

```csharp
var polyline = new Polyline2D(new[] {
    new Vector2(1, 2), new Vector2(4, 5), new Vector2(7, 1)
}, true) { Elevation = 3, Thickness = -2 };
polyline.TransformBy(Matrix3.Scale(2, 2, 4), new Vector3(10, 20, 30));
// Elevation is 42, signed thickness -8; original vertex/list objects remain.
```

## Admission, geometry and state

All coordinates, bulges, matrix components, translation, elevation, thickness
and widths are validated before mutation, including identity calls. The
Matrix4 overload checks a finite affine bottom row and still dispatches to
virtual Matrix3 transformation. The ordinary implementation reads/writes
`base.Normal`; user overrides cannot mutate or reject its publication midway.
Other custom `TransformBy` policies remain overrideable.

A shared planar preparation is used rather than a new numeric engine. Narrow
straight polylines, including spline-control definitions, can use nonuniform
in-plane maps that leave a representable plane and extrusion. Nonzero bulges
require a circular in-plane image. The existing circular admission helper
provides a fixed 1e-12 similarity bound, independent of MathHelper.Epsilon.
The existing conservative wide-polyline restrictions remain: perpendicular
transformed auxiliary normal, uniform in-plane scale, and rejection of
orientation-reversing wide arcs. Thin reflected arcs use the oriented image
normal and retain their bulge signs, representing the reflected WCS arc.
Inactive final bulges are conservatively included in admission.

All widths and their presence are prepared before publication. Null remains
absent; signed-zero widths are retained. Nonzero widths that overflow or round
to zero reject. Constant and per-vertex widths retain their existing precedence.
Failed operations preserve positions, proxy bytes, metadata, ownership, IDs,
list/vertex identity and stored fields. Exact identities retain signed bits and
valid proxies. Changed geometry clears the parent proxy after successful
publication. Proxies and private dependencies are not regenerated.

The existing retained-legacy 3x3 transform path is deliberately unchanged; its
full arithmetic/publication audit remains separate work. Matrix4 validation
also protects entry to that path. Geometry setters, parent notifications for
direct vertex edits, private FIELD/TABLE relationships and concurrent mutation
are outside this task.

## Numerical and resource limits

Plane preparation uses the already qualified floating-point planar helper,
not correctly rounded exact affine sums. Rank-deficient or nearly degenerate
planes and oblique nonzero thickness reject under that helper's bounds.
Intermediate overflow can reject mathematically representable images; extreme
relative-error and subnormal-coordinate preservation are not newly qualified.
Storage and work are linear in vertex count, including prepared position and
width arrays. No new whole-process memory or wall-clock guarantee is asserted.
The broad width helper's existing magnitude restrictions remain in effect.

## Executed verification

The final focused harness has 191 cases and runs unchanged against the actual
preceding production library: 82 passed before, 109 failed before, and all 191
pass after the correction. This includes late invalid coordinates/overflow,
width underflow, exact identities, aliases, getter/setter callbacks, projective
matrices, tilted/oblique planes, signed thickness, ordinary and smoothed
control definitions, narrow reflections and unsupported arc stretching.

The independent checker reads 84 drawings in the six existing typed profiles
and both transports, checks 4,284 directly computed WCS line/arc samples,
extrusion/widths and selected physical fields, and rejects 1,092 corrupted
packets plus inventory controls. The drawings have zero graph errors/repairs.
Group-91 fixture IDs are only written in the existing R2013+ eligible profiles;
no writer eligibility rule or old regression expectation was weakened.

Full-suite and hosted platform results are recorded in the task PR and receipt.
A local net8.0 run does not establish a Windows or netstandard2.0 result.

```sh
DXF_TEST_FILTER=polyline-affine-safety/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polyline_affine_safety.py artifacts/conformance
```

Autodesk's [LWPOLYLINE reference](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
defines OCS coordinates, elevation, thickness, widths, bulges and extrusion.
The admission/transaction/tolerance policies here are library contracts, not
native AutoCAD behavior claims. No new typed historical dialect, pre-R11
support, version conversion, native AutoCAD open/AUDIT/save/reopen or private
cache/dependency completion is established.
