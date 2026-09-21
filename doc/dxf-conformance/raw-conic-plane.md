# Raw CIRCLE/ARC plane and thickness editing

`DxfRawDocument.WithCircleGeometryAndPlane` and `WithArcGeometryAndPlane` add
complete raw geometric-definition editing across the nine existing raw profiles:
AC1009 (the R11/R12 family) through AC1032. Both transports and ENTITIES/BLOCKS
are supported. This does not enable nine typed document dialects.

```csharp
DxfRawDocument edited = raw.WithArcGeometryAndPlane(record,
    new Vector3(-8, 16, 32), // Center in the replacement OCS, NOT WCS
    3.75,                  // Radius
    350, 35,               // Stored start/end degrees, without normalization
    -3,                    // Signed thickness
    new Vector3(0, 2, 0));  // Nonunit extrusion retained exactly
```

This is definition editing, not an affine transform or shape-preserving plane
change. The center is in the replacement object coordinate system. Changing its
extrusion changes the WCS center and curve even when OCS coordinates remain the
same. Coordinates are not converted, and angles are not swapped on normal
reversal. For the explicit example, the WCS center is `(8, 32, 16)` under the
standard arbitrary-axis construction; extrusion magnitude is retained in tags.
The older geometry-only APIs retain their existing-plane/thickness behavior.

## Validation and packet preservation

The existing parser validates snapshot identity, entity type, private-data
framing, required fields and geometry. Every replacement component must be
finite; radius is positive and extrusion is nonzero. Signed thickness and raw
angles are otherwise retained exactly, including negative and out-of-range
angles. Unnormalized nonunit extrusion is not silently repaired or rescaled.
A bit-identical no-op returns the original snapshot and original bytes before
edit-dependency checks. A real edit retains the existing conservative proxy,
application/embedded, unknown-field, geometry-sensitive XData and incoming-handle
reference guards. No private relationship or cache is regenerated.

Center, radius and angle slots update in place. A missing center Z is added after
Y only when different from positive zero. Changed thickness is placed after
radius; changed extrusion is placed there as a complete adjacent 210/220/230
vector, after changed thickness when both change. All default-valued extrusion
components are emitted together when the vector changes. Partial, reordered or
separated stored vectors are regrouped; unchanged component tags are reused by
identity. Unchanged thickness and extrusion retain original presence/order.
All remaining tags retain relative order, and unchanged values retain their tag
objects. Source snapshots stay immutable; edited serialization may normalize
spelling and line endings.

Absent default thickness stays absent unless changed; explicit defaults are not
removed. Signed zero differs from positive zero. The existing maximum-tag budget
counts every added component before constructing the replacement. No new global
CPU or process-memory guarantee is asserted.

## Qualification

The added harness covers all nine raw profiles, both input/output transports,
ENTITIES/BLOCKS, absent/default/explicit plane data, raw view and tag identities,
source-byte isolation, no-ops, dependency refusal, nonfinite values, signed zero,
exact budgets, old-API equivalence, six-profile typed reload and reversed/extreme
normal magnitudes. The
regular matrix emits 288 source/edit pairs (576 drawings); sixteen further
fixtures exercise partial/reordered/separated/absent extrusion packets. The
independent checker regenerates expected packets and derives WCS curve samples
from explicit bases rather than production conversion code. Corrupted fields,
metadata and missing/extra inventories must reject. Exact-head executed evidence
is recorded in the PR; source or Python syntax validation is not C# execution.

```sh
DXF_TEST_FILTER=conic-plane/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_conic_plane.py artifacts/conformance
```

Autodesk's [CIRCLE](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-8663262B-222C-414D-B133-4A8506A27C18.htm)
and [ARC](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-0B14D8F1-0EBA-44BF-9108-57D8CE614BC8.htm)
references define OCS center, radius, signed thickness, extrusion and ARC degree
angles. The generated fixtures are not native producer files. Extreme raw-value
acceptance does not qualify native or independent geometric rendering at those
extremes. Native AutoCAD open/AUDIT/save/reopen, pre-R11 formats, historical typed
loading, private FIELD/TABLE/cache regeneration, dependency-complete import,
general version conversion and native font/visual parity remain separate work.
