# Atomic polygon-mesh affine transforms

This C# task follows the conversion and open Bezier tasks in PRs #124 and #125.
It changes `PolygonMesh.TransformBy` only; the JavaScript port is unchanged.

## Corrected contract

Previously the method wrote each control vertex immediately and validated the
normal afterward. A failure late in the operation could leave earlier vertices
changed. Projective Matrix4 bottom rows were silently discarded, and successful
geometry changes kept stale common proxy graphics.

Both transform overloads now use the already qualified `VertexAffineTransform`
preparation helper. It computes all control points and the auxiliary normal
before publication, using the existing bounded exact affine arithmetic. Every
source, matrix and translation component must be finite, including for identity.
The stored normal must have component-based squared length within `2e-15` of one.
Matrix4 requires exactly `(0,0,0,1)` in its bottom row.

Each WCS affine coordinate is an exact binary sum rounded once to nearest
binary64, ties to even. Representable cancellation is retained even when an
intermediate ordinary product would overflow. Final coordinate overflow or a
nonzero exact coordinate rounding to zero rejects. A nonzero normalized auxiliary
component that underflows also rejects. These are conservative library admission
policies, not assertions about AutoCAD's native exceptions or tolerances.

The inherited normal retains its existing auxiliary-vector transform convention;
it is not inferred as a single surface plane normal. A zero image retains that
auxiliary normal, and a translation retains its exact components. Singular maps
may collapse the control net. Surface sampling and spline-type admission remain
separate from affine control-net transformation.

Rejected operations preserve point and normal bits, the vertex array, source
identities, metadata and proxy bytes. Publication bypasses derived virtual normal
accessors. Changed geometry clears the common parent proxy graphics; exact
identity and numerically unchanged results retain the existing components and
proxy. Positive and negative zero compare equal in the no-change decision.

The existing vertex array, loaded `VertexRecords`, their handles and the end
sequence record retain their identities. Plain loaded geometry is serialized
from the updated model coordinates by the existing writer. Closure, surface type,
density, ordinary appearance and XData are not replaced by the transform. Caller
mutation from another thread during a transform is not supported.

**This does not regenerate private associations, child proxies, XData coordinate
payloads or external FIELD/TABLE caches.** Fitted surface samples retain the
existing control-net regeneration policy documented by the Bezier feature;
arbitrary fitted child packets are not losslessly preserved by this task.
Preparing a point array and exact integer dot products has CPU/allocation cost.
No large-model speedup or performance qualification is claimed.

```csharp
var controls = new[] {
    new netDxf.Vector3(0, 0, 0), new netDxf.Vector3(1, 0, 0),
    new netDxf.Vector3(0, 1, 0), new netDxf.Vector3(1, 1, 1)
};
var mesh = new netDxf.Entities.PolygonMesh(2, 2, controls);
mesh.TransformBy(netDxf.Matrix3.Scale(2), new netDxf.Vector3(4, 5, 6));
// The existing control array is updated only after the whole result is valid.
```

## Executed evidence

The same **528 new focused cases** pass **67 before / 528 after** the production
fix. The negative run has the preceding production code and identical new test
module; it is not a stub or altered expected-value run. Cases cover plain,
quadratic, cubic and open Bezier control nets, both APIs, detached and registered
entities, all matrix/translation/source components with nonfinite values,
projective matrices, late overflow/underflow, exact cancellation, virtual
callbacks and loaded child identities. The wire corpus uses nine explicit maps
across all six existing typed profiles in text and binary.

`tools/verify_polygon_mesh_affine.py` independently calculates WCS results with
Python Fraction exact arithmetic and auxiliary normals with 1,100-digit Decimal
square roots. It checks **108 drawings / 1,728 exact WCS vertices**, rejecting
**49,296 actual ordered-packet corruptions** and two missing/extra inventory
controls. Complete selected POLYLINE/VERTEX/SEQEND packets are compared except
handle values; their framing, uniqueness and child-owner relationships are
validated separately. Normal components admit four ULP plus a `2e-15` squared
unit-length bound. Every drawing has zero ezdxf graph errors and zero repairs.

Text LF/CRLF records are accepted without changing binary input or comparison
rules. The wire corpus is unsmoothed. Affine covariance of the existing quadratic,
cubic and new Bezier samplers is tested at model level, not as native rendering
or surface-regeneration equivalence. Complete-suite and hosted results are
recorded in the PR against the executed source, separately from focused evidence.

```sh
DXF_TEST_FILTER=polygon-affine/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polygon_mesh_affine.py artifacts/conformance
# Full suites, rather than just the new module:
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance --jobs 4
```

`DXF_TEST_ARTIFACTS` selects an isolated harness output directory. Offline local
net8-only builds may use `-p:TargetFrameworks=net8.0`; that is not a substitute for
hosted netstandard2.0 build qualification.

## Primary references and remaining scope

[Autodesk VERTEX](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm)
specifies WCS storage for 3D vertices and flag 64 for polygon-mesh vertices.
[Autodesk POLYLINE](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
specifies polygon-mesh flags, counts, density, surface type and extrusion groups.
These define the stored model, not the library's transaction or numerical policy.

The synthetic corpus covers R2000/R2004/R2007/R2010/R2013/R2018, not historical
typed dialects. Native AutoCAD open/AUDIT/save/reopen, fonts/visual equivalence,
private FIELD/TABLE/cache regeneration, dependency-complete import, general
version conversion and other entity methods remain separate work. This task is
not full all-version DXF or AutoCAD parity.
