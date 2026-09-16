# TABLESTYLE stored data/unit values and explicit STYLE reassignment

This increment follows PR #96's [border and version-zero header support](table-style-borders.md)
and reuses `DxfTableStyle.ReplaceStyle`. It edits stored records, not evaluated
cell values or table geometry. It does not synchronize duplicated formats in
CELLSTYLEMAP, TABLECONTENT, display blocks or TABLE entities.

## Public fields and source evidence

The [Autodesk TABLESTYLE reference](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-0DBCA057-9F6C-4DEB-A66F-8A9B3C62FB1A.htm)
identifies group 7 as a STYLE name, group 90 as the cell data type and group 91
as the cell unit type. `DxfTableStyleRow.DataTypes` projects the last two into
an immutable `DxfTableStyleRowDataTypes` only when each occurs exactly once in
the public row packet. Both are retained as signed 32-bit stored codes. No enum
meaning, unit conversion, format evaluation or universal validity is inferred
from a numeric value. Unknown values remain representable.

The existing five pinned native files are described in the [source assessment](table-style-assessment.md).
The R2004 native TABLESTYLE has no row 90/91 fields; its `DataTypes` stays null,
and this edit API does not synthesize the absent pair. The four later native
files contain the pair. Synthetic R2004 packets with explicitly present fields
also have storage/edit tests; that is not a historical-legality claim. Group 1
format strings remain unchanged and uninterpreted. The previous independent
reader's group-91 interpretation is not copied into this API.

Private 102 groups, later unknown subclasses and duplicate fields cannot supply
an otherwise absent projection. Missing or ambiguous data/unit pairs do not
disable independently qualified scalar, border or STYLE edits.

## Composable, atomic changes

```csharp
DxfTableStyle style = document.Objects.Items.OfType<DxfTableStyle>().Single();
DxfTableStyleRow row = style.Rows[0];
TextStyle target = document.TextStyles["ExistingStyle"];

DxfTableStyleRowEdit edit = row.WithTextStyle(target);
if (row.DataTypes != null)
    edit = edit.WithDataTypes(new DxfTableStyleRowDataTypes(
        storedDataType: 4, storedUnitType: 2));
style.ReplaceStyle(header: null, rows: new[] { edit });
```

`WithValues`, `WithBorders`, `WithDataTypes` and `WithTextStyle` can be chained
on an edit in any order. Each call returns a new immutable request and retains
its other selected changes. Scalars, borders and data/unit values require their
corresponding projection. Passing null rejects; omission preserves the original
component. As before, one request may edit multiple distinct current rows and
an optional recognized header.

A STYLE target is checked after caller enumeration and disposal finish. Its
owner, exact registered handle identity and actual source TextStyles table entry
must agree. Foreign same-name resources, detached clones and removed targets
are rejected rather than imported, looked up by name or registered implicitly.
The caller may explicitly add a resource before validation, but that independent
caller action is not part of the style transaction and is not rolled back.

Unresolved source group-7 names remain unbound when a same-name resource is later
added. An explicit `WithTextStyle` request can bind such a row to the exact new
registered resource, including when the stored spelling is unchanged. Selecting
an already bound identity is a no-op and preserves original wire spelling, even
after that resource has been renamed. A changed selection stores the target's
current name with the ordinary DXF Unicode escaping rules. Later target renames
still follow its bound identity on output.

The named binding dictionary and reference membership list are built on detached
candidates, then published with the new Tags/Header/Rows snapshots. Earlier
reference lists and row bindings remain unchanged. The referenced resource
objects themselves remain ordinary mutable objects; snapshots freeze membership,
not future resource names, handles or lifetimes. No event callbacks, new handles
or resource registrations occur during the final state swap. Thread-concurrent
mutation is not supported.

Reference order is preserved: exposed bound handles, with repetitions, followed
by named public STYLE slots. Reassigning all named rows releases only those uses.
A repeated group-340 handle, CELLSTYLEMAP reference, or another entity can still
protect the former STYLE. The new target is protected immediately and after
save/reload; erasing an eligible map-free style releases its current uses. Unbound
handle fields are never re-resolved merely because a new object exists.

## Profile comparison for the post-PR95 additions

The aggregate [297-row comparison](version-feature-matrix.md) remains a pinned
PR95 qualification snapshot. These later scoped operations supplement it; the
broad TABLESTYLE and overall DXF families remain partial.

| Operation | R2000 | R2004 | R2007 | R2010 | R2013 / R2018 |
|---|---|---|---|---|---|
| Complete stored border triple edits (PR96) | Opaque only | Present qualified packets | Present qualified packets | Present qualified packets | Present qualified packets |
| Fixed leading `(280,0)` header projection/edit (PR96) | No | No | No | Recognized prefix | Recognized prefix |
| Existing unique data/unit pair edits | Opaque only | Present synthetic packets; native pair absent | Present qualified packets | Present qualified packets | Present qualified packets |
| Explicit registered row STYLE selection | Opaque only | Qualified three-row packets | Qualified three-row packets | Qualified three-row packets | Qualified three-row packets |

R12/R13/R14 remain raw-preservation profiles, not typed TABLESTYLE APIs. None of
these operations changes the source DXF version or performs schema conversion.

## Regression and independent checks

The `table-row-settings/` test group covers native carriers and complete drawings,
all five modern style profiles and both transports, snapshot/no-op behavior,
partial and final reference release, repeated handle dependencies, independent
entity consumers, explicit unresolved-name binding, source/target changes during
enumeration, malformed/missing/scoped pairs and signed extreme values.

The independent gate `tools/verify_table_row_settings.py` requires exactly 72
before/after pairs: 30 synthetic and 42 native/full-drawing pairs. It derives
expected public edits itself and compares all ordered physical records. It
preserves the fixed header prefix, raw format strings and complete unrelated
TABLE/CELLSTYLEMAP/STYLE records. Only the existing writer's separately verified
empty ACAD_LAYERSTATES identity is normalized, following the prior scalar gate.
HEADER time/seed fields are not physical records in that comparison.

Every requested field, protected row/style/owner field and selected unrelated
record is independently corrupted and must be rejected by the same gate. Its
self-test additionally covers 18 model pairs and 528 corruptions. The CI runner
discovers this verifier automatically; actual execution results, not declared
case inventories, establish qualification.

```sh
DXF_TEST_FILTER=table-row-settings/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_table_row_settings.py --self-test
python tools/verify_table_row_settings.py artifacts/conformance
```

The [implementation-stage receipt](table-row-settings-qualification.json) pins
all changed production/test sources and this independent verifier. The downloaded
Windows Release artifact at implementation commit
`6179754f2132a61e75b2f0d916530fcafd6158e2` contains 35,309 unique passing cases,
including all 216 additions, and 7,575 DXF fixtures. Its SHA-256 digest matches
GitHub's published artifact digest. The independently executed new gate passes
72 pairs and rejects 2,372 actual-output corruptions. Final-head matrix results
are recorded separately in PR #97; this receipt is explicitly implementation-stage
rather than a substitute for that final execution.

The continuation recovered PR96 from final Linux Debug artifact 10437594933,
SHA-256 `c3806c36c996ecc6aef026915b00234bda6364528e44093a469b0b01da421184`.
Its source reconstructs merged tree `8321aa6c0652a61fe759b7d5b6fd480951b78493`;
35,093 unique baseline cases passed. This historical baseline is not substituted
for running the new implementation. C# execution uses actual GitHub Linux/Windows
CI runners; parallel Python verifier processes provide additional local checks.
No native AutoCAD process or sub-agent execution is claimed.
