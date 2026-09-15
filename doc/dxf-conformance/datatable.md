# DATATABLE stored columns and object ownership

`DxfDataTable` reads and writes the exact public stored-version-2 DATATABLE
column envelope in R2004, R2007, R2010, R2013 and R2018, in text and binary DXF.
This object is distinct from the graphical ACAD_TABLE entity and does not render,
evaluate formulas, format cells, transform coordinates or regenerate a table.

`Name` preserves the exact table name. `SetColumns(rowCount, columns)` installs an
immutable rectangular snapshot. Empty names, duplicate column names, zero-row
columns and a positive row count with zero columns are preserved. Every supplied
column contains exactly `rowCount` values. Integer values are signed 32-bit;
numeric coordinates must be finite; text must be valid single-line UTF-16
without NUL. The admission limit is 1,048,576 columns, rows or total cells; it
bounds untrusted input allocation and is not an AutoCAD format limit.

| Stored type | Public enum | Value | DXF packet |
| --- | --- | --- | --- |
| 1 | Integer | `int` | 93 |
| 2 | Double | finite `double` | 40 |
| 3 | String | `string` | 3 |
| 4 | Point | `Vector3`, all coordinates preserved | 10 / 20 / 30 |
| 5 | ObjectId | `DxfObject` or null | 331 |
| 6 | HardOwner | `DxfDatabaseObject` or null | 360 |
| 7 | SoftOwner | `DxfDatabaseObject` or null | 350 |
| 8 | HardPointer | `DxfObject` or null | 340 |
| 9 | SoftPointer | `DxfObject` or null | 330 |
| 10 | Boolean | `bool` | 71, exactly 0 or 1 |
| 11 | Vector | `Vector3`, all coordinates preserved | 11 / 21 / 31 |

The enum numbers and names come from Autodesk's
[AcDbDataCell::CellType reference](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbDataCell__CellType.html).
The group codes come from the
[DATATABLE DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-D09D0650-B926-40DD-A2F2-4FD5BDDFC330.htm).
The latter calls the 10/20/30 and 11/21/31 packets “2D Point” and “3D Point”,
whereas ObjectARX calls types 4 and 11 `AcGePoint3d` and `AcGeVector3d`. The
implementation retains all three coordinates with explicit packet identities.
Those labels do not establish native CAD geometric interpretation. Native
point/vector DATATABLE cells have not been independently observed in the corpus.

Owner cells establish actual reciprocal ownership, including group 350 soft
ownership. They are traversed during graph adoption, remapped during graph
cloning and included in terminal erasure. Pointer cells never become owners.
Null references encode `0`. A non-null owner target cannot occupy two owner
slots, belong to another owner, form an ownership cycle or reference an erased
object. Existing object identity is required for live table references.

Imported references resolve only to the actual object accepted from a physical
source record, including explicit source dictionary mappings to managed
collections. Matching a generated object's handle number is insufficient. An
absent target, a discarded entity, an ignored section, a CLASS field, a private
control-group field or a payload handle cannot authorize an unrelated runtime
default. The accepted object and its common identity must come from the same
physical record, even when a legacy parser reads another group 5 in its body.
Managed collections require the explicit reserved entry in the source named
dictionary. Handles normalize hexadecimal case and leading zeroes; forward
references and the distinct DIMSTYLE group 105 identity remain supported.

`SetColumns` snapshots caller enumerators before validating current graph state.
Failed validation leaves the table, children, handles and database unchanged.
A detached table can acquire and release children. A registered table may edit
scalar or pointer values and reorder or change the strength of its owner slots,
but it must retain the same owned-object set. Construct a detached replacement
graph to change that set; bulk column editing does not silently orphan or erase
live owned objects. Erasing the whole table through its owning dictionary
cascades through both hard and soft owner cells, after incoming-reference checks.

Unknown stored versions, unknown cell types, private fields or private subclasses
preserve the complete DATATABLE as `DxfOpaqueObject`, including terminal XData.
Recognized malformed fields, wrong dimensions, incomplete cells, duplicate
public markers, invalid text/numbers, unresolved references and nonreciprocal
owner slots reject. R2000 inputs remain opaque and typed R2000 export rejects.
Existing compatible CLASS metadata is preserved with current physical instance
counts; conflicting private CLASS metadata is preserved when no typed instance
exists. New typed declarations use the observed `AcDbDataTable`, `ObjectDBX
Classes`, flags 0 and nongraphical classification.

## Evidence and limits

The checked-in [focused qualification receipt](datatable-qualification.json)
records 467 DATATABLE cases, 35 existing ownership cases and the mandatory
34-output independent gate, with exact source/test commits and hashes.

The pinned original `sample_AC1018_ascii.dxf.gz` in `tests/fixtures/table-oracle`
contains DATATABLE handles `143D` and `145B`, with 21 and 20 rows respectively.
Each has four columns of stored types 2, 1, 1 and 6, with empty table and column
names. Their 41 hard-owned XRecords preserve their complete original packets.
The tests verify the decompressed source SHA-256 against the shared corpus
manifest, extract the two tables with those exact descendants, and rewrite only
the table's external common owner to an extraction dictionary. These are scoped
native object-subgraph tests, not full original-file compatibility claims.

Authored tests exercise all 11 types, mixed null/reference cells, independent
names, aliases, reactors, extension dictionaries, XData, cross-document clone
maps, erasure protection, zero dimensions, malformed packets and opaque input.
Source-identity cases include missing and discarded targets, lexical decoys,
ignored sections, normalized forward references, null references, symbol tables
and 25 retained entity, resource and managed-collection identities, including
nested INSERT/ATTRIB/SEQEND parsing.
`tools/verify_datatable.py` requires every output and reads it independently with
ezdxf 1.4.4. It checks raw column packets, actual object graph identities, native
record fidelity, CLASS counts and ancillary audits. Ezdxf has no typed DATATABLE
implementation; a successful generic audit does not verify native evaluation.
The independent verifier includes deliberate Boolean and dimension corruption
controls. Documentation-derived type packets and netDxf-authored outputs are
identified as such, not attributed to a native producer.

Other inspected implementations do not provide a suitable independent typed
oracle for all cell kinds: IxMilia.Dxf's column-type values differ from the
ObjectARX enum, and LibreDWG's DATATABLE cell implementation is incomplete.
Full native CAD execution and complete private application schemas remain
unqualified.
