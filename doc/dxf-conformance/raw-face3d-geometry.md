# Raw 3DFACE geometry and invisible edges

`DxfRawDocument.ReadFace3DGeometry(record)` returns four immutable WCS corners
and the existing `Face3DEdgeFlags` mask. `WithFace3DGeometry(record, first,
second, third, fourth, flags)` replaces those fields in a new same-version
snapshot. It does not load a historical drawing into the typed document model.

```csharp
using System.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.IO;

DxfRawRecord record = raw.Sections.SelectMany(s => s.Records)
    .First(r => r.Name == "3DFACE");
DxfRawFace3DGeometry face = raw.ReadFace3DGeometry(record);
DxfRawDocument edited = raw.WithFace3DGeometry(record,
    face.FirstVertex, face.SecondVertex, face.ThirdVertex,
    new Vector3(8, 9, 10), face.EdgeFlags | Face3DEdgeFlags.Fourth);
```

## Definition and preservation

Corner order is the DXF 3DFACE order (10, 11, 12, 13), not the swapped final
corners used by SOLID/TRACE. Coordinates are WCS. No extrusion conversion,
normal calculation, planarity assumption or triangulation is performed.
Degenerate and nonplanar input is retained, not certified as a valid surface.
A triangle explicitly repeats corner three in the fourth stored corner; the
reader does not invent a missing fourth corner. All four X/Y pairs are required.
Omitted Z values default to zero. The visibility mask accepts only bits 1, 2,
4 and 8, with omitted group 70 defaulting to zero.

Classic markerless and AcDbEntity/AcDbFace records in ENTITIES and BLOCKS are
supported. Records must belong to the exact immutable snapshot. Duplicate
coordinates or flags, undefined flag bits, malformed application controls and
ambiguous subclass layouts reject. Application-control and embedded contents
cannot replace or supply core geometry. Ordinary tags after XData reject.

Existing coordinate/flag slots retain their positions. A missing Z is inserted
after its Y only for a requested value other than positive zero. A missing flag
is inserted after the last coordinate only for nonzero flags. Negative zero and
finite subnormal values are preserved. Every other tag retains its object
identity, value and relative order. Bit-identical no-ops return the original
snapshot, retaining original-byte output. Changes use the raw serializer and
may normalize lexical number spelling and line endings; original source bytes
are unchanged in either outcome.

Actual edits use the same conservative dependency guards as LINE, CIRCLE, ARC
and POINT: proxies, application/embedded data, unknown ordinary tags,
geometry-sensitive XData and exposed incoming handle uses reject. A flags-only
edit obeys those guards too. Hidden binary/string dependencies, associations,
header extents and private caches are not regenerated. Some benign references
therefore reject. Existing raw and handle-index budgets and stream/atomic-save
contracts apply. Optional-field insertion checks the tag limit before creating
a replacement record. No transport or version-conversion policy is changed.

## Executed checks

The new focused harness has 293 cases. It tests nine raw families, input/output
text and binary, ENTITIES/BLOCKS, explicit triangles, omitted Z/flags, flags
before/after corners, all 16 edge masks, finite extremes, invalid flags,
nonfinite inputs, source/tag identity, malformed schemas, incoming dependencies
and exact tag-budget exhaustion. Six modern typed profiles separately roundtrip
edited geometry through `DxfDocument`.

The unchanged focused harness fails all 293 cases against the actual preceding
library without these methods and passes all 293 on the new implementation.
This proves availability and the tested contracts, not 293 independent old bugs.
The preceding source was reconstructed from PR150's hash-verified source archive.
An earlier local 3DFACE handoff was not available in the mounted artifacts; this
is a fresh implementation, not a claim of recovering that unprovided patch.

`tools/verify_raw_face3d_geometry.py` regenerates the expected input corner
patterns and checks the complete before/after tag sequences, selected edits,
optional-field placement, physical version and binary framing. An independent
ezdxf reader checks WCS corner order and edge visibility. Its 288 source/edit
pairs (576 drawings) pass with zero graph errors or repairs. It rejects 20,256
actual-tag corruptions, including metadata and neighboring records, and separate
missing/extra inventory controls. These fixtures are synthetic, not new native
AutoCAD producer evidence. Final full-suite/platform results are recorded in
the PR and execution receipt, not inferred from the focused run.

```sh
DXF_TEST_FILTER=raw-face3d/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_face3d_geometry.py artifacts/conformance
```

## Schema and remaining scope

Autodesk's [3DFACE group-code reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-747865D5-51F0-45F2-BEFE-9572DBC5B151.htm)
specifies four WCS corners and the four invisible-edge bits. Exception types,
strict missing-field admission and immutable snapshots are library contracts,
not claimed native application policies.

The tested raw profiles are AC1009 (shared R11/R12), AC1012, AC1014, AC1015,
AC1018, AC1021, AC1024, AC1027 and AC1032. No additional typed `DxfDocument`
dialect or pre-R11 family is enabled. Native AutoCAD open/AUDIT/save/reopen,
full historical typed loading, private FIELD/TABLE/cache regeneration,
dependency-complete imports, general version conversion and native visual/font
qualification remain separate work. Full AutoCAD/all-version parity is not
established by this increment.
