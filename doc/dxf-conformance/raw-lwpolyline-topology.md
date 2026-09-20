# Raw LWPOLYLINE vertex insertion and removal

`InsertLwPolylineVertex(record, index, position, startWidth, endWidth, bulge,
identifier)` inserts before an existing index, or appends when index equals
vertex count. `RemoveLwPolylineVertex(record, index)` removes one vertex.
Both return a new immutable raw snapshot and update the declared group-90
vertex count. The existing `WithLwPolylineVertex` edit remains unchanged.

```csharp
DxfRawLwPolylineGeometry shape = raw.ReadLwPolylineGeometry(record);
DxfRawDocument inserted = raw.InsertLwPolylineVertex(record, 1,
    new Vector2(20, 30), startWidth: 1, endWidth: 2, bulge: 0.25,
    identifier: -200);
// Records belong to a snapshot: reacquire the record before the next edit.
DxfRawRecord current = inserted.Sections.SelectMany(s => s.Records)
    .First(r => r.Name == "LWPOLYLINE");
DxfRawDocument removed = inserted.RemoveLwPolylineVertex(current, 0);
```

This is structural editing, not shape-preserving segment subdivision. Every
surviving bulge and width remains attached to its original vertex and now
applies to the newly adjacent vertex. Inserting or removing points can therefore
change neighboring arc shapes and width interpolation. No arc splitting,
fitting, bulge recomputation, area conservation or automatic closure repair
is performed. Stored closure and continuous-linetype flags remain unchanged.

## Preservation and admission

The original seven raw R14–R2018 families and text/binary transports remain
supported. This adds no historical typed `DxfDocument` dialect. Positions stay
in the existing OCS. Elevation, thickness, extrusion, constant width and all
unselected metadata are retained. Source bytes are unchanged on success and
failure; edited serialization may normalize spelling and line endings.

New vertices require finite X/Y and bulge, and nonnegative finite widths.
Positive-zero optional widths/bulges remain absent. Signed zero and finite
subnormals are stored without normalization. The optional identifier is an
opaque signed Int32. A requested identifier already used by an existing vertex
rejects; omission does not generate or renumber identifiers. Surviving IDs
retain their exact values. Existing source IDs are not globally reinterpreted
or certified unique. Nonzero new per-vertex widths reject when a nonzero
constant width is present rather than choosing a precedence rule.

Removal deletes only the selected vertex's coordinate, width, bulge and group-91
slots, not an entire slice between adjacent vertices. Interleaved entity fields
and comments are retained by object identity. All surviving tags keep their
relative order and values; only count is replaced. Insertion preserves every
pre-existing tag except count and emits the new X/Y pair adjacently.

Removing the final vertex yields a raw empty definition with its prior flags.
Insertion into that empty definition is supported. These empty/degenerate
forms are raw preservation cases, not a native renderer acceptance promise.
A zero-work no-op is not offered: each accepted operation changes topology.

The existing parser validates snapshot/type/profile admission, vertex packets,
count agreement, supported flags, optional widths and private-data framing.
Actual topology changes use the shared conservative proxy/private/application/
embedded/XData guards and exposed incoming-handle checks. Some benign incoming
uses reject too. Extents, associative geometry, hidden string/binary references,
private caches and application-specific vertex-ID references are not rebuilt.
This is not dependency-complete graph editing.

The existing one-million-vertex maximum remains enforced on insertion, together
with the raw total-tag budget before constructing a replacement snapshot.
Parsing and validation still require work linear in stored tags and vertices;
there is no zero-copy or global CPU/memory bound claim. Existing raw transport,
stream ownership and atomic filename-save rules are unchanged.

## Verification

The hosted harness exercises before-first, interior and append insertion, plus
first/interior/final removal, across both input/output transports, seven raw
profiles, and ENTITIES/BLOCKS. The matrix yields 336 complete source/edit pairs
(672 drawings). Tests also cover optional attributes and identifiers, signed
zero, extremes, tag-budget refusal, exact removal/reinsertion, empty definitions,
interleaved headers/comments, constant-width conflicts, stale/foreign snapshots,
count corruption, invalid arguments and incoming dependencies. The maximum-
vertex guard is implemented but no million-vertex performance qualification is
claimed from these small fixtures.

The independent checker regenerates source packets and exact expected edits,
compares complete source/output tag sequences including binary chunk bytes,
checks physical version and transport, and loads vertex order, widths, bulges
and plane properties with ezdxf. It requires zero graph diagnostics for both
sides of the matrix. Changed/missing/duplicate selected tags, modifications to
neighboring private data, and missing/extra fixture inventories must fail the
same positive validators. Repeated replays are not additional test cases.
These drawings are synthetic, not native AutoCAD producer evidence.

```sh
DXF_TEST_FILTER=raw-lw-topology/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_lwpolyline_topology.py artifacts/conformance
```

Actual source-bound execution totals and platform results are recorded in the
PR and delivery receipt. No local C# run or measured old/new run is asserted
when a local SDK is unavailable. Test availability does not imply test success.

## Primary schema and remaining scope

Autodesk's [LWPOLYLINE schema](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
defines vertex count, OCS X/Y, optional identifier, widths, bulge and flags.
Insertion/removal policy, duplicate-ID admission, exception types and immutable
snapshot behavior here are explicit library contracts, not native AutoCAD
exception or dependency-regeneration claims.

Full historical typed loading, pre-R11 formats, full private FIELD/TABLE/cache
regeneration, dependency-complete import, general version conversion and native
AutoCAD open/AUDIT/save/reopen or font/visual qualification remain separate work.
This increment does not establish full AutoCAD parity across every DXF version.
