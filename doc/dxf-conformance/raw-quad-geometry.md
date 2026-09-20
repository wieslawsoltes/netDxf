# Raw SOLID and TRACE geometry

`ReadSolidGeometry` and `ReadTraceGeometry` expose an immutable
`DxfRawQuadGeometry`: four corners explicitly named `*InObjectCoordinates`,
signed thickness, and the unchanged extrusion vector. `WithSolidGeometry` and
`WithTraceGeometry` replace the four corners and thickness together in a new
same-version snapshot. These APIs do not load an old drawing into `DxfDocument`.

```csharp
DxfRawQuadGeometry quad = raw.ReadSolidGeometry(record);
DxfRawDocument edited = raw.WithSolidGeometry(record,
    quad.FirstVertexInObjectCoordinates,
    quad.SecondVertexInObjectCoordinates,
    quad.ThirdVertexInObjectCoordinates,
    quad.FourthVertexInObjectCoordinates, -2.5);
```

## Coordinates and preservation

The order is the physical DXF order 10,11,12,13, not perimeter order 10,11,13,12.
Coordinates remain in the existing OCS; extrusion is not normalized or changed.
All four X/Y pairs are required. A triangle explicitly repeats the third corner
as the fourth. Each omitted Z defaults independently to zero. Unlike the typed
SOLID/TRACE model's common elevation, this raw view retains every corner's stored
Z value, even if they differ. It neither flattens such data nor certifies it as a
valid planar solid. Degenerate inputs are not repaired or triangulated.

Existing coordinate/thickness slots are replaced in place. Missing Z is added
after its Y, and missing thickness after the last corner component, only when
the requested value differs bitwise from positive zero. Negative zero and finite
subnormals are preserved. No extrusion or other default field is synthesized.
Untouched tags retain object identity, values and relative order. Exact no-ops
return the original snapshot, preserving original-byte output. Edited output
uses the existing serializer and may normalize spelling or line endings; source
bytes are unchanged after both accepted and rejected edits.

Classic markerless and AcDbEntity/AcDbTrace layouts in ENTITIES and BLOCKS are
accepted. Duplicate/missing components, malformed scopes, unknown subclasses and
zero extrusion reject. Private application groups and embedded tails cannot
supply or overwrite core geometry. Ordinary fields after XData reject.
Actual edits use the existing raw guards against proxy packets, private or
unknown ordinary fields, geometry-sensitive XData and exposed incoming handle
uses. Thickness-only edits use the same guards. Hidden binary/string references,
associations, extents and private caches are not regenerated. Existing byte/tag
and handle-index budgets and save/transport rules remain active.

## Qualification

The conformance matrix covers both kinds, nine existing raw families, both input
and output transports, ENTITIES/BLOCKS, triangles, omitted defaults, reversed and
nonunit extrusion, and independently stored corner elevations. Six modern typed
profiles separately round-trip common-elevation quads. Tests exercise exact
source/tag preservation, finite extremes, malformed fields, dependencies and tag
budget exhaustion. The independent checker regenerates source corners and
compares complete source/edit tag sequences, physical version/binary framing,
and ezdxf's decoded OCS corners, extrusion and thickness. Mutation controls
change/remove/duplicate selected and unselected tags and neighboring records.
Synthetic fixtures do not establish native producer equivalence.

```sh
DXF_TEST_FILTER=raw-quad/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_quad_geometry.py artifacts/conformance
```

Exact executed results are recorded in the PR, not inferred from this document.
No new claim of local .NET execution is made when qualification uses hosted CI.

## References and limitations

Autodesk's [SOLID reference](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-E0C5F04E-D0C5-48F5-AC09-32733E8848F2.htm)
and [TRACE reference](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-EA6FBCA8-1AD6-4FB2-B149-770313E93511.htm)
describe the corners, AcDbTrace marker, optional thickness and extrusion. The
strict field admission, immutable snapshots and dependency guards are explicit
library contracts, not claimed native AutoCAD exception policies.

The nine raw families are AC1009 (shared R11/R12), AC1012, AC1014, AC1015, AC1018,
AC1021, AC1024, AC1027 and AC1032. Full historical typed loading, pre-R11 formats,
native AutoCAD open/AUDIT/save/reopen, private FIELD/TABLE/cache regeneration,
dependency-complete imports and general version conversion remain separate work.
The common-elevation typed model and its reader/writer are not changed here.
