# Explicit stored TABLECONTENT edits

`DxfStoredTableContent.ReplaceContent` changes the recognized linked-data name and
description, selected scalar values, and a standalone content object's terminal
TABLESTYLE reference in one atomic operation. It operates only on loaded content
in its original document and DXF profile. It does not evaluate a table, regenerate
its display block, synchronize the ACAD_TABLE entity, or author a complete table.

```csharp
var value = content.StoredValues.First(v =>
    v.Kind == DxfStoredTableContentValueKind.String);
var edit = value.WithValue("New literal", value.FormattedText == null
    ? null : "Explicit stored display text");
content.ReplaceContent(content.Name, content.Description,
    content.TableStyle, new[] { edit });
```

The caller supplies the newer value's stored display text explicitly. The
library preserves its format flags, unit value and format string. For the R2004
compact encoding, the display-text argument must be null. This API does not
calculate a display string from the scalar, a format string or a formula.

## Qualified wire boundary

The five pinned source files contain eight TABLECONTENT objects. Their native
scalar inventory is recorded in
[`table-content-editing-native-evidence.json`](table-content-editing-native-evidence.json).
The whole R2013 and R2018 examples and the complete R2004/R2007/R2010 source
carriers remain the fixtures described in [table-content.md](table-content.md).
There are 86 editable scalar frames: 7 and 20 in the two whole examples; 8 and 13
in R2004; and 8 and 11 in each of R2007 and R2010. The packets include multiple
contents per cell, dates and omitted values as well as the selected scalars.

Autodesk describes linked table data as heterogeneous cells which can hold
multiple values, fields or blocks in its
[AcDbLinkedTableData reference](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbLinkedTableData.html).
The pinned independent
[LibreDWG scalar mapping](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg_spec_shared.h)
corroborates the primitive type/group-code pairs. Its conditional envelopes do
not exactly describe all of these DXF bodies; the actual retained native frames
control this implementation's admission. Only group-code facts were used; no
external implementation source was copied.

| Stored type | Public kind | Exact scalar tags |
|---|---|---|
| 1 | `Integer` | group 91, `int` |
| 2 | `Double` | group 140, finite `double` |
| 4 | `String` | one group 1, decoded `string` |
| 32 | `Point3D` | groups 11/21/31, finite `Vector3` |

A projected frame has `CELLCONTENT_BEGIN`, group 90 content type 1, and group 300
`VALUE`, under a recognized linked row and linked cell, preceded by the `CONTENT`
marker. Its tail has group 91 zero attributes and `CELLCONTENT_END`. In R2004 the
scalar follows its group 90 value type directly. In R2007 and later it also has
group 93 flags exactly 2, 4 or 6, followed by the scalar, group 94 unit value,
group 300 format string, group 302 display text, and group 304 `ACVALUE_END`.
An exact three-tag `AcDbLinkedData` header is required for replacement.

The projection uses packet order and `PayloadIndex`, with no inferred row/column
address. The current outer count checks are retained. Scalar type changes,
insertion/deletion of cells or contents, dates, unknown/omitted values, fields,
blocks, attributes, data maps, arbitrary handle remapping and unrecognized
envelopes remain outside the editable boundary. A known scalar's string may
literally name a framing marker; it remains data in both encodings. An unknown
nested framing marker suppresses the linked packet's editable projections.
Existing whole-object opaque fallback remains unchanged.

## Atomicity, identity and snapshots

`WithValue` returns a sealed immutable request carrying the exact source value
snapshot. The request must retain the original CLR type. `ReplaceContent` copies
the caller's sequence, including its enumeration and disposal, before validating
the current source object, database, owner graph, root links, profile and requested
style identity. Duplicate, foreign and stale requests reject. A nested call
poisons the outer attempt even when the caller catches the nested exception.
The API does not undo independent document changes made by caller callbacks.

All replacement tags, projections and dependency lists are prepared before the
state swap. Existing `Payload`, `Subclasses`, `StoredValues` and `References`
snapshots remain unchanged. A value-equivalent request preserves the original
snapshots and their exact loaded tag values. Double comparison distinguishes
negative zero from positive zero. A successful change invalidates every earlier
value token, including tokens for unchanged scalar frames. A no-op still validates
the current graph and source profile.

For standalone content, the requested terminal TABLESTYLE must be an actual
registered object in the same document, typed or opaque. Null encodes a null
handle. There is no name or handle-based substitution. All other semantic
references retain their exact identities and order, including repetitions and
retained owner-held metadata. Changing this reference is prohibited when an
ACAD_TABLE is an owner ancestor, because this operation cannot atomically
synchronize that entity's retained style state.

The owning TABLE's payload and grid remain unchanged by scalar edits.
`StoredTable.BackingLiteralValuesAgree` now compares the current backing values
when read, so a successful content edit cannot leave a cached agreement result.
This comparison remains nullable and does not qualify rendering, formula
evaluation or style agreement. Source migration, cloning, erasure, changing
owners and regenerating cell layouts retain their existing restrictions.

## Admission and transports

The complete emitted object is limited to 1,048,576 tags, including common
identity/owner tags, extension-dictionary metadata, distinct persistent reactors
and emitted XData chunks. This is checked by both edit validation and save
validation, including no-op edit attempts after metadata changes. Group 0 is
excluded to match the reader's existing record limit.

Each newly encoded string is limited to 1,048,576 UTF-16 code units after Unicode
and literal-backslash escaping. This is an explicit API admission limit, rather
than a claim about a universal DXF field limit. Null, NUL and invalid UTF-16
strings reject. For R2004 non-ASCII code units are encoded as `\U+hhhh`; literal
backslashes are escaped in every profile so a literal Unicode-looking sequence
roundtrips without being decoded into another value. CR/LF remain valid for the
binary transport. Text save rejects them before emitting bytes.

## Qualification

Run the `table-content-edit/` conformance prefix and then
`python tools/verify_editable_table_content.py <artifact-directory>`. The gate
requires all 52 outputs, covering every native packet through both input and
output transports, before it checks exact requested scalar/header changes,
unchanged nested data, TABLE and related stored object packets, metadata, CLASS
records and all 316 selected source-carrier identities. It also corrupts actual
parsed outputs to demonstrate the oracle rejects changes. The final execution
[receipt](table-content-editing-qualification.json) records 926 passing cases,
including all 165 new cases, and nine passing gates in each of Debug and Release.
The new gate checks 64 native packet appearances, 344 explicit scalar changes,
2,528 carrier record appearances and 500 actual corruptions per configuration.
The [independent review](table-content-editing-review/summary.json) passes 40
cases in each configuration and audits all 64 emitted DXFs without errors or
repairs. Its unchanged harness, receipts and exact output archive are retained.

Native source identities are qualified separately from explicitly synthetic
scalar and style carriers. Ordinary resource writers may normalize optional
STYLE or BLOCK_RECORD fields as documented in the existing storage receipt;
this change does not expand those resource packet guarantees. No commercial
application rendering or table regeneration is claimed.

The TABLE comparison starts with its `AcDbBlockReference` subclass, matching
`StoredTable.Payload`, and separately verifies every retained proxy byte. Its
common `AcDbEntity` packet uses the existing writer's ordinary defaults. The
R2010 sources' proxy byte-count group 160 is written as group 92, with the same
declared length and bytes, under the existing common-entity profile rule. This
normalization is present on the no-op carriers as well as the edited carriers.
Owner, reactor and extension-dictionary metadata are compared exactly for every
companion TABLE, TABLEGEOMETRY, TABLESTYLE, CELLSTYLEMAP and owning XRECORD. For
the three selected-record carriers, the expected metadata incorporates only the
existing verified manifest substitutions: TABLE model-space owner `1F` becomes
`F00017`, and TABLESTYLE owner/reactor `86` becomes `F0000A`. These adaptations
belong to the input carrier and are not performed by `ReplaceContent`.
