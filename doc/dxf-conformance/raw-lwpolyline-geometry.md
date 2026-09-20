# Raw LWPOLYLINE vertex editing

`ReadLwPolylineGeometry(record)` exposes immutable OCS positions, stored widths,
bulges, optional signed vertex identifiers, closed/continuous-linetype flags,
constant width, elevation, thickness and extrusion. `WithLwPolylineVertex(record,
vertexIndex, position, startWidth, endWidth, bulge)` edits one existing vertex.
Topology, group-90 count, all identifiers, closure, the plane and other vertices
remain unchanged. This does not insert or delete vertices or resolve constraints.

```csharp
var geometry = raw.ReadLwPolylineGeometry(record);
var vertex = geometry.Vertices[1];
var edited = raw.WithLwPolylineVertex(record, 1, new Vector2(20, 30),
    vertex.StartWidth, vertex.EndWidth, vertex.Bulge);
```

Positions are in the current OCS, not WCS. Widths are stored values without
constant-width resolution. Bulges describe outgoing segments, including the
closing segment when enabled; no tessellation, clamping or fitted-curve update
occurs. Zero-width/bulge presence is exposed separately from its default value.
Signed group-91 identifiers are opaque; they are neither allocated nor remapped.

The selected API supports seven raw profiles, AC1014 through AC1032. Earlier
profiles reject without changing generic raw tag preservation. Classic
markerless and AcDbEntity/AcDbPolyline packets in ENTITIES/BLOCKS are supported.
Each group 10 opens a vertex; Y, widths, bulge and identifier attach only to that
vertex. Duplicate per-vertex/header fields, orphan attributes, missing Y,
count mismatch, negative widths, undefined flags and zero extrusion reject.
Private control and embedded packets cannot provide geometry; ordinary fields
after XData reject. Finite DxfTag admission remains unchanged.

Both declared and actual counts are limited to 1,000,000. Allocation grows with
actual packets, not the untrusted declared count; the immutable view is built
only after count agreement. Existing raw tag/byte and handle-index budgets also
apply. Empty definitions are readable but cannot be vertex-edited; no claim of
surface validity or native acceptance of degenerate input is made.

Bit-identical no-ops return the original snapshot and original-byte output.
Actual edits replace existing slots in place. Missing optional fields are added
after the selected vertex's last field only when their requested value is not
positive zero. Negative zero remains explicit. Existing optional fields are not
removed when changed to zero. Untouched DxfTag identities, values and relative
order are retained. Changed serialization may normalize spelling/line endings;
the original snapshot remains unchanged on success or failure.

Changing per-vertex widths while constant width is nonzero rejects rather than
choosing between conflicting width interpretations. Position and bulge edits can
retain the existing widths unchanged. Real changes reuse the conservative proxy,
private/application/embedded, geometry-sensitive XData and exposed incoming
handle guards. Hidden dependencies, header extents, private caches, linetype phase
and application constraints are not regenerated.

## Verification

The matrix covers all seven profiles, both input/output transports, ENTITIES and
BLOCKS, optional fields, markerless packets, attribute ordering, signed vertex
IDs, bulges, constant widths and tilted/reversed extrusion. Independent Python
checks regenerate input packets, compare complete before/after tag sequences,
and independently load vertex coordinates, widths, bulges and plane properties.
Corruption tests change, omit and duplicate actual tags. Inventory controls reject
missing/extra drawings. The corpus is synthetic, not native producer evidence.
Six modern typed profiles separately round-trip edited geometry. Exact execution
results are recorded in the PR; no local .NET run is implied without an SDK.

```sh
DXF_TEST_FILTER=raw-lwpolyline/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_lwpolyline_geometry.py artifacts/conformance
```

## Schema and scope

Autodesk's [LWPOLYLINE schema](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
describes OCS vertices, widths, bulges, identifiers, flags and extrusion. The
[polyline overview](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-392BF13C-D9E7-47A8-8E07-435296332279.htm)
distinguishes the single-object lightweight form from legacy vertex sequences.
Strict admission, immutable snapshots and width-conflict rejection are library
contracts, not claimed native AutoCAD exception behavior. Historical typed
loading, full topology editing, pre-R11 support, private FIELD/TABLE regeneration,
dependency-complete import, general version conversion and native AutoCAD
open/AUDIT/save/reopen remain outside this task's qualification.
