# Guarded raw POINT geometry across nine DXF families

`DxfRawDocument.ReadPointGeometry(record)` returns a `DxfRawPointGeometry`
containing WCS `Position`, signed `Thickness`, unnormalized
`ExtrusionDirection` and the stored degree-valued `UcsXAxisAngle` (group 50).
`WithPointPosition(record, position)` edits only the WCS location and returns a
new same-version snapshot. The position is not converted through the extrusion
coordinate system: unlike CIRCLE/ARC centers, POINT locations are in WCS.

```csharp
using System.IO;
using System.Linq;
using netDxf;
using netDxf.IO;

using var input = File.OpenRead("drawing.dxf");
DxfRawDocument source = DxfRawDocument.Load(input);
DxfRawRecord record = source.Sections
    .Where(s => s.Name == "ENTITIES")
    .SelectMany(s => s.Records).First(r => r.Name == "POINT");
DxfRawPointGeometry before = source.ReadPointGeometry(record);
DxfRawDocument edited = source.WithPointPosition(record, new Vector3(7, 8, 9));
using var output = File.Create("edited.dxf");
edited.Save(output);
// source and before retain the original coordinates. The output retains its dialect.
```

## Schema and preservation

Classic markerless and AcDbEntity/AcDbPoint records in ENTITIES or BLOCKS are
supported. X and Y are required; omitted Z defaults to zero. Omitted thickness
and UCS angle default to zero, and omitted extrusion defaults to (0,0,1).
Extrusion must be nonzero but is not normalized. Duplicate geometry, incomplete
subclasses, malformed control groups and ordinary data after XData reject.
Opaque embedded tails and application groups cannot supply core geometry.

Bit-identical no-ops return the original snapshot and retain original-byte
output. Real edits replace existing position slots in place. Missing Z is
inserted immediately after Y only when the requested Z is not positive zero;
negative zero and subnormals are retained. Thickness, extrusion, UCS angle and
all other tags keep their values, identities and relative order. The source
remains unchanged on success and failure. The edited snapshot uses the existing
raw serializer, so lexical numeric spelling and line endings may change.

Reading and editing have different admission rules. Actual changes reject
proxies, group-102 private data, embedded tails, unknown ordinary fields,
geometry-sensitive XData and exposed incoming target-handle references.
Shared LINE/CIRCLE/ARC handle and XData guards are reused. Scalar real 1040 data
remains editable. This conservative contract does not infer an affine transform
from a new position or regenerate application-specific data. It may reject some
benign dependencies. Hidden binary/string references, header extents and private
caches are neither discovered exhaustively nor regenerated.

Snapshot membership is required: foreign or stale records reject. Raw tag and
handle-index budgets still apply. Adding Z over the tag budget rejects before
a new snapshot is published. Existing transport, binary-comment, stream and
atomic-file-save contracts are unchanged.

## Verification

The 216 focused cases cover nine existing raw families, both input/output
transports, ENTITIES/BLOCKS, omitted defaults, tilted/reversed/nonunit extrusion,
optional-field order, exact tag identity, negative zero, extreme finite values,
malformed fields, private scopes, incoming references, snapshots and budgets.
The same harness fails 216/216 against the actual preceding library where the
new APIs are absent and passes 216/216 afterward. This is API availability
coverage, not a claim of 216 separate old bugs.

The independent verifier checks 288 complete source/edit tag pairs (576 files),
physical $ACADVER, historical binary framing, WCS positions, unchanged thickness,
angle and extrusion, and graph audits through ezdxf. It rejects 13,320 actual-tag
corruptions plus missing/extra inventory controls. The synthetic fixtures have
zero graph errors or repairs. No retained legacy fixture is relabeled as a
native POINT producer sample. Six modern typed profiles separately round-trip
through DxfDocument. Final full-suite/platform results are recorded in the PR.

```sh
DXF_TEST_FILTER=raw-point/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_point_geometry.py artifacts/conformance
```

## Schema reference and parity limits

Autodesk's [POINT reference](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-9C6AD32D-769D-4213-85A4-CA9CCB5C5317.htm)
defines WCS location, signed thickness, extrusion and the optional UCS X-axis
angle used with nonzero PDMODE. The immutable edit and conservative rejection
rules above are library contracts, not prescribed AutoCAD behavior.

AC1009/AC1012/AC1014/AC1015/AC1018/AC1021/AC1024/AC1027/AC1032 are the nine
existing raw families; AC1009 is shared by R11/R12. No typed historical dialect
or pre-R11 family is enabled. Native AutoCAD open/AUDIT/save/reopen, PDMODE visual
rendering, complete private FIELD/TABLE/cache regeneration, dependency-complete
import and general version conversion remain separate work. This is not full
AutoCAD parity across every version.
