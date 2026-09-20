# Guarded raw LINE geometry editing, R11/R12 through R2018

The raw snapshot API now decodes and edits ordinary LINE endpoints without
loading the whole drawing into `DxfDocument` or changing its declared dialect.
This adds a selected semantic operation for the historical profiles, not full
historical typed-document support or an automatic version converter.

```csharp
using System.IO;
using System.Linq;
using netDxf;
using netDxf.IO;

using var input = File.OpenRead("drawing.dxf");
DxfRawDocument source = DxfRawDocument.Load(input);
DxfRawRecord line = source.Sections
    .Where(s => s.Name == "ENTITIES")
    .SelectMany(s => s.Records).First(r => r.Name == "LINE");
DxfRawLineGeometry geometry = source.ReadLineGeometry(line);
DxfRawDocument edited = source.WithLineEndpoints(line,
    geometry.StartPoint, new Vector3(20, 30, 40));
using var output = File.Create("edited.dxf");
edited.Save(output); // Source dialect/encoding/transport; input remains unchanged.
```

## Selected geometry and source preservation

`ReadLineGeometry` returns an independent immutable value with WCS start/end
points, signed thickness and the stored extrusion direction. Extrusion is not
renormalized. Missing endpoint Z values default to zero; thickness defaults to
zero and extrusion to (0,0,1). Required X/Y fields, duplicate geometry components,
zero extrusion, malformed application controls and ambiguous subclass layouts
reject explicitly. Classic markerless LINEs and the AcDbEntity/AcDbLine layout
are accepted. Records must belong to the exact raw snapshot and be in ENTITIES
or BLOCKS. Embedded-object tails and application-control contents cannot supply
missing core coordinates or override existing ones.

`WithLineEndpoints` changes only the six WCS coordinates. Existing slots are
replaced at the same positions; a missing Z slot is added immediately after its
Y only when the requested value is not positive zero. Negative zero and finite
subnormals are retained. Coincident endpoints are permitted. Thickness,
extrusion, handle, layer, appearance, metadata and neighboring records are not
rewritten. All untouched DxfTag objects retain identity and relative order.

Bit-identical edits return the original snapshot, including its exact original
byte output when available. Changed snapshots serialize through the existing
raw writer: numeric spelling and line endings may be normalized, so edited
output is **not** promised to be byte-identical outside the coordinate fields.
The source remains byte-identical and usable after success or failure.

## Conservative edit boundaries

Reading geometry is broader than changing it. A real endpoint change rejects
proxy graphics/count packets, group-102 application data (including reactors and
extension dictionaries), embedded tails, unrecognized ordinary fields, XData
coordinate or handle slots and nonzero/ambiguous target identity errors.

The existing raw handle index is scanned before replacement. Duplicate target
identities or **any exposed use of the target handle**, including arbitrary and
opaque slots, reject. This is deliberately conservative: even benign GROUP or
draw-order references may require explicit lower-level editing. The HEADER
HANDSEED hint is not an incoming reference. Handle-free legacy LINEs are
admitted. Unknown binary/string-embedded references are not discovered or
interpreted, and this is not proof of a dependency-complete drawing graph.

Header extents, XData-derived geometry, associative entities and private caches
are not regenerated. The original source must already be semantically suitable
for the caller's use. This API does not repair malformed unrelated objects.
Raw byte/tag/string budgets and handle-index budgets remain enforced. A missing
Z insertion that exceeds the document tag budget rejects before a new snapshot
is published. Existing stream-save, atomic-file-save and binary-comment rules
are unchanged. A caller converting text comments to binary must remove those
comments explicitly; no silent discard is added.

## Tests and independent evidence

The task exercises all nine raw families: AC1009, AC1012, AC1014, AC1015, AC1018,
AC1021, AC1024, AC1027 and AC1032. AC1009 covers the shared R11/R12 family.
The matrix covers text/binary input and output, ENTITIES/BLOCKS, omitted Z and
both endpoint packet orders. Six typed families are separately loaded through
DxfDocument after editing. Rejection cases exercise duplicate/missing geometry,
private contexts, unknown subclasses, proxies, incoming handles, stale/foreign
records, nonfinite coordinates and budgets.

Four existing pinned native legacy fixtures supply real LINE edits:
`ASCII_R12.dxf`, `bin_dxf_r12.dxf`, `bin_dxf_r13.dxf`, `bin_dxf_r14.dxf`.
The existing small R13/R14 native fixtures have no physical LINE records;
separate tests explicitly reject non-LINE records rather than counting the
independent reader's generated default arrow-block lines as source evidence.

`tools/verify_raw_line_geometry.py` checks complete before/after tag sequences,
exact endpoint values, existing tag order and missing-Z insertion. It verifies
physical $ACADVER, legacy binary group-code framing and interpreted LINE points
with ezdxf. Deliberate edits, missing/duplicate coordinate tags, changed layers
and missing/extra output inventories must fail the same positive validators.

Native R13/R14 files are upgraded in memory by ezdxf, which reports pre-existing
repairs. Before/after diagnostic codes and counts must match. This is **no new
repairs**, not zero repairs or native AutoCAD qualification. The checked-in
source fixtures and their existing provenance are not changed by this task.
The PR and delivery receipt record exact final-source execution counts.

```sh
DXF_TEST_FILTER=raw-line/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_raw_line_geometry.py artifacts/conformance
```

## Primary schema reference and remaining parity

Autodesk's [LINE DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-FCEF5726-53AE-4C43-B4EA-C84EB8686A66.htm)
defines groups 10/20/30 and 11/21/31 as WCS endpoints, optional group 39 as
thickness, and optional 210/220/230 as extrusion. The snapshot, admission and
failure policies above are explicit library contracts, not claimed Autodesk
requirements. Historical evidence comes from the retained fixtures and codec
matrix, not from assuming a modern schema establishes all legacy behavior.

No additional typed DxfDocument dialect is enabled. General version conversion,
complete private FIELD/TABLE regeneration, associative graph updates, native
AutoCAD open/AUDIT/save/reopen and installed-font/visual equivalence remain
separate work. This task is not full AutoCAD parity across all DXF versions.
