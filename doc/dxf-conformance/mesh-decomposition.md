# Planar MESH decomposition

This task follows the MESH/3DFACE transform audit in PR #113. It replaces the
commented-out `Mesh.Explode` placeholder with working C# APIs. It does not change
the JavaScript port or add a subdivision-surface evaluator.

## APIs and geometry contract

`Mesh.Explode()` returns detached triangular `Face3D` entities for an unsmoothed
mesh. A nonzero `SubdivisionLevel` rejects rather than silently substituting a
control cage for a displayed smoothed surface.

`Mesh.ExplodeControlMesh(int maximumTriangles = 1000000)` explicitly selects the
stored level-zero polygons regardless of subdivision level. It does not apply
crease weights or blend-crease/smoothing rules. Source metadata is unchanged.

Every simple planar face with n ring vertices produces n-2 triangles. Concave
polygons, either winding, and ordinary collinear boundary vertices are supported.
One repeated closing index is accepted and excluded from the ring count.
Triangles use exact original WCS vertex values; no coordinates are interpolated,
projected back, welded or flattened. Their fourth corner repeats the third.
Original boundary edges remain visible. Introduced diagonals and the repeated
zero-length edge are invisible through standard 3DFACE edge flags.

Preflight checks every source vertex for finiteness, all indices and the total
output budget before triangulation. Null/short/oversized rings, zero edges,
self-intersections, nonadjacent touches, adjacent retracing, zero-area polygons,
nonplanarity and numerically unresolved planes reject. Each ring is limited to
1024 vertices (plus an optional closing repeat), and the default total output
limit is one million triangles. A negative caller budget rejects. An empty mesh
returns an empty list. These are explicit API resource/admission bounds.

The mesh is never removed from or replaced in its owning document. Its vertex,
face and edge containers, handles, owner and proxy graphics are not modified.
There is no partial return when a later face fails. Concurrent caller mutation
and side effects in user-provided resource overrides are not supported.

## Numerical decisions

A local, scale-normalized frame chooses a stable projection. Offsets are formed
before scaling when representable; an absolute-coordinate scaling fallback
handles opposite extreme finite coordinates whose direct difference overflows.
The plane test admits a maximum normalized distance of 1e-10, relative to the
largest anchored coordinate extent. Accepted near-planar vertices are retained
unchanged, not flattened. This is not exact native AutoCAD planarity policy.

Projected binary64 coordinates are represented as exact integers with a common
2^-1074 scale. Orientation, segment intersection, signed area and ear-admission
predicates therefore do not lose their sign to floating-point cancellation.
A deterministic ear-clipping pass retains collinear boundary samples by blocking
ears whose edge/triangle contains another ring vertex. It rejects rather than
returning a degenerate final triangle. BigInteger predicates and the bounded
intersection/ear searches have a cost; no large-model speed or memory claim is
made. Numerically unresolved planes can reject despite mathematical planarity.

## Appearance and dependency safety

Each detached result receives independent ordinary layer, linetype, color,
transparency and XData clones, plus lineweight, linetype scale, visibility,
auxiliary normal, color name and shadow mode. Output proxies are omitted because
the source cache is not a triangular-face cache. The original source cache is
retained. Normal DXF writer version gates still apply to copied appearance data.

Extension dictionaries, persistent/managed reactors and XData database-handle
references reject: automatically dropping or duplicating their relationships
would not be a valid graph conversion. Private references hidden in application
strings are not inferred. This is neither dependency-complete import nor an
atomic replacement of an existing source entity and its incoming references.

```csharp
var mesh = new netDxf.Entities.Mesh(
    new[] {
        new netDxf.Vector3(0, 0, 0), new netDxf.Vector3(4, 0, 0),
        new netDxf.Vector3(4, 4, 0), new netDxf.Vector3(2, 1, 0),
        new netDxf.Vector3(0, 4, 0)
    }, new[] { new[] { 0, 1, 2, 3, 4 } });
var triangles = mesh.Explode();
var destination = new netDxf.DxfDocument(netDxf.Header.DxfVersion.AutoCad2000);
destination.Entities.Add(triangles);
destination.Save("triangles.dxf");
// Explicit level-zero selection for a mesh that carries smoothing metadata:
mesh.SubdivisionLevel = 2;
var controlTriangles = mesh.ExplodeControlMesh(maximumTriangles: 1000);
```

This offers an explicit geometry decomposition usable with older supported
3DFACE writer profiles. It is not general document-version conversion and does
not enable typed R11/R12/R13/R14 output.

## Qualification

The feature tests execute through the actual public APIs, using reflection only
to allow the same tests to run against the preceding assembly where the APIs are
absent. Model checks cover ordinary/concave polygons, both windings, three WCS
planes, scales 1, 1e-200 and 1e200, closing repeats, appearance isolation, source
identity, invalid geometry/graphs, exact output budgets and explicit level-zero
selection. Subnormal and maximum-finite extent boundaries have dedicated model
checks. Full result counts and platform evidence are recorded against the tested
head in the PR rather than inferred from a focused run.

`verify_mesh_decomposition.py` does not reimplement ear clipping. It validates
unchanged input vertices, exact polygon area and winding, boundary/internal edge
incidence, interior diagonals, noncrossing edges, repeated corners and complete
selected packet metadata except entity/owner handle values. Identity framing is
checked and every drawing receives a separate independent graph audit.

The fixed corpus has 1296 drawings / 4104 triangles across R2000/R2004/R2007/
R2010/R2013/R2018 in both transports. Its 213410 packet and inventory corruption
controls reject through the positive validator, with zero graph errors/repairs.
These are synthetic geometric fixtures, not native producer or visual evidence.

```sh
DXF_TEST_FILTER=mesh-decompose/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_mesh_decomposition.py artifacts/conformance
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

## Primary references and remaining scope

Autodesk documents the [MESH level-zero polygon and crease fields](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
and [3DFACE WCS corners and invisible-edge flags](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-747865D5-51F0-45F2-BEFE-9572DBC5B151.htm).
The triangulation algorithm, numerical tolerance and exception/resource policies
are library decisions, not Autodesk-prescribed equivalence claims.

Nonplanar surface tessellation, subdivision/crease evaluation, native AutoCAD
open/AUDIT/save/reopen, historical typed dialects, full private FIELD/TABLE/cache
regeneration, complete graph imports and general version conversion remain
separate work. This task does not establish full AutoCAD or all-version parity.
