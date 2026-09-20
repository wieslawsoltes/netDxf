# Guarded raw CIRCLE and ARC geometry

`DxfRawDocument.ReadCircleGeometry` and `ReadArcGeometry` read immutable conic
geometry. `WithCircleGeometry` and `WithArcGeometry` return same-version edited
snapshots. Classic markerless records and ordinary AcDbEntity/AcDbCircle[/AcDbArc]
layouts in ENTITIES or BLOCKS are supported. These are selected raw-schema APIs,
not new typed `DxfDocument` dialects or a document-version converter.

```csharp
using System;
using System.IO;
using System.Linq;
using netDxf;
using netDxf.IO;

using var input = File.OpenRead("input.dxf");
DxfRawDocument raw = DxfRawDocument.Load(input);
DxfRawRecord record = raw.Sections.SelectMany(s => s.Records)
    .First(r => r.SectionName == "ENTITIES" && r.Name == "ARC");
DxfRawArcGeometry geometry = raw.ReadArcGeometry(record);
// The center argument is in the EXISTING entity's OCS, not in world coordinates.
DxfRawDocument changed = raw.WithArcGeometry(record,
    geometry.CenterInObjectCoordinates, geometry.Radius, 350, 35);
using var output = File.Create("output.dxf");
changed.Save(output);
```

## Coordinate and mutation contract

Center properties are explicitly named `CenterInObjectCoordinates`: CIRCLE/ARC
centers use OCS, unlike the raw LINE endpoint API. Extrusion is returned exactly
as stored; no normalization or OCS/WCS conversion is performed. Missing center Z
defaults to positive zero, thickness to zero and extrusion to (0,0,1). Center X,
center Y, positive radius and both ARC angles are required. Duplicate geometry,
zero extrusion and unknown or ambiguous subclass layouts reject. Validated raw
tags already require finite doubles; new API arguments enforce finiteness too.

Angles are in DXF degrees and are not normalized, clamped or used to infer a
full circle. Negative, multi-turn and equal endpoint angles retain their literal
finite scalar values. The API does not certify every consumer's interpretation
of such values. Radius must be strictly positive; representable subnormals and
large finite values are allowed without constructing derived points.

Only the selected scalar slots change. An omitted center Z is inserted after
its Y when a value other than positive zero is requested, including negative
zero. All unselected `DxfTag` objects retain identity, value and relative order.
Thickness, extrusion, identities, style data and neighboring records remain
unchanged. A bit-identical edit returns the original snapshot, including its
original-byte save behavior. Changed output uses the existing raw serializer;
numeric spelling or line endings may therefore change. Source bytes remain
unchanged on success and failure. The record must belong to the exact snapshot.

## Dependency and framing boundaries

Application-control scopes and embedded tails cannot supply core geometry.
An embedded marker after XData begins is malformed, not a way to suppress the
remaining schema checks. Actual changes reject proxies, application/embedded
data, unknown ordinary fields and XData coordinate, handle, distance or scale
slots. Other XData is retained without interpreting its private meaning.

The existing raw LINE common-code and incoming-handle helpers are shared under
entity-neutral names. Their admission behavior is unchanged: duplicate target
identities and exposed incoming handle uses reject; HEADER HANDSEED does not
count as a dependency. Legacy handle-free targets remain possible. Hidden
binary/string references are not discovered. No associative entity, header
extent, field, private cache or application geometry is regenerated. These
checks do not prove dependency completeness or validate every unrelated tag.

The existing raw/tag/handle-index resource limits remain in force. The extra
Z tag is budget-checked before a new snapshot is published. No transaction API,
file replacement policy, comment removal or implicit version conversion is
introduced. A valid no-op can retain decorated records that reject real changes.

## Verification

The focused module has 282 harness cases covering the nine existing raw families
AC1009/AC1012/AC1014/AC1015/AC1018/AC1021/AC1024/AC1027/AC1032, both input and output
transports, ENTITIES/BLOCKS, omitted Z, reversed/tilted extrusion, exact source
retention, defaults, subnormals, negative zero, finite angle spelling, rejection
and six modern typed round trips. All 282 fail against the preceding actual
production DLL without these APIs and pass on this implementation. Existing
raw LINE tests remain enabled after the helper renaming.

The independent verifier checks 288 complete source/edit pairs (576 drawings),
physical version fields, R12 binary framing, complete unselected tag sequences,
slot order, and independent interpreted CIRCLE/ARC geometry. OCS-to-WCS positions
are checked against separately specified bases for the two exercised normals.
All 5,760 actual-record corruption controls and two inventory controls reject.
The synthetic corpus has zero source/edit graph errors or repairs. R13/R14
in-memory importer upgrades do not serve as proof of native application support.

These are new synthetic fixtures, not new native-producer evidence. The retained
legacy fixtures used for the preceding LINE task contain no physical CIRCLE/ARC
records and are not counted as conic coverage. Full-suite and hosted-platform
results are recorded against the exact tested head in the PR, not inferred from
this focused module.

```sh
DXF_TEST_FILTER=raw-conic/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_conic_geometry.py artifacts/conformance
```

## Primary references and remaining scope

Autodesk documents the stored fields in
[CIRCLE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-8663262B-222C-414D-B133-4A8506A27C18.htm)
and [ARC](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0B14D8F1-0EBA-44BF-9108-57D8CE614BC8.htm).
The [group-code reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm)
specifies degree angles in DXF and radian angles in application APIs. These
references support field meanings, not all historical producer layouts or this
library's immutable editing and exception policies.

No pre-R11 profile, full historical typed loading, private FIELD/TABLE/cache
regeneration, dependency-complete import, general version conversion, native
AutoCAD open/AUDIT/save/reopen or native visual/font qualification is added.
This selected raw operation does not establish full AutoCAD/all-version parity.
