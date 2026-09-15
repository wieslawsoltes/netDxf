# Stored TABLEGEOMETRY

`DxfStoredTableGeometry` preserves the complete loaded `AcDbTableGeometry`
payload and exposes immutable projections of its rows, columns, cells and
content-geometry packets. It remains in its source document and DXF profile.
It does not calculate table layout, evaluate cell contents or regenerate display
geometry. The object has no public constructor. Its immutable packet snapshots
can now be explicitly replaced through the bounded
[loaded-packet editing API](table-geometry-editing.md).

## Evidence and admitted grammar

Eight actual TABLEGEOMETRY objects occur in the five originals already pinned by
[`table_oracle/fixtures.json`](../../tools/table_oracle/fixtures.json). They cover
R2004, R2007, R2010, R2013 and R2018. The two ezdxf example drawings are loaded
whole and unchanged. The three ACadSharp drawings use the existing
[complete native carriers](../../tests/fixtures/table-content/README.md): 136
selected records in R2004 and 90 each in R2007 and R2010. Every unedited geometry payload,
identity and owner stays exact; the carrier manifest discloses the permitted
changes to other records' common ownership context.

The independently maintained [LibreDWG TABLEGEOMETRY wire specification](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec)
(source blob `2f7cbbe8b2b2c7bfb5019080cb4acfaca873a5b3`) confirms the observed
counts and packet order. That project marks its DWG implementation unstable;
this module qualifies the observed DXF storage grammar. No external
implementation code is incorporated. An Autodesk public TABLEGEOMETRY DXF
schema was not located during this increment.

| Packet | Ordered groups | Public projection |
|---|---|---|
| Object | 100, 90, 91, 92 | Subclass, row count, column count, cell count |
| Cell | 93, 40, 41, 330, 94 | Raw flags, width/height including gap, exact source pointer, content count |
| Content geometry | 10/20/30, 11/21/31, 43, 44, 45, 46, 95 | Two stored vectors, four dimensions, uninterpreted integer |

Rows, columns and repeated-packet counts must be nonnegative and bounded by
1,048,576. Declared repeated counts must match the available packet, and all
stored geometric scalars must be finite. The reader checks minimum available
packet size before allocating lists. It does not infer a row/column address
for a cell, require the cell count to equal rows multiplied by columns, assign
meanings to flags or group 95, or infer nonnegative dimensions.

The one known subclass and recognized field set select the typed grammar.
Missing, reordered, duplicated or surplus fields in that grammar are rejected.
Private common metadata, a different/additional subclass, an unknown field or
an R2000 input retains the complete object opaquely. An extended-data field in
the recognized public payload must follow a genuine application registry.
Private variants preserve their bodies without inferring cell semantics.

## Identity and lifecycle

At load, all exposed nonzero semantic handles bind actual accepted physical source
records. Generated defaults, discarded records and coincidental handles cannot
supply targets. Cell group 330 exposes `GeometryReference` as `DxfObject`;
its geometry semantics and a narrower target type are not inferred. All eight
native packets store null cell pointers, so nonzero identity and removal cases
are explicitly synthetic controls. Arbitrary groups 320–329 are not promoted
to semantic references.

The actual registered owner and every registered ancestor must exist without
an ownership cycle. The model retains its source owner and DXF profile, and
tracks its current exact dependencies. Explicit packet replacement can select
currently registered source-document identities as described in the editing
contract. Removal of a referenced resource or owning entity/block is refused.
Cloning and erasure of the object or a containing ownership graph require the
complete application schema and reject before mutation. Common metadata and
XData retain the existing interfaces and validation rules.

The existing XRECORD declared-ownership and composite TABLE envelopes continue
to bind the same geometry identity. Their synthetic ownership-only helper now
uses the explicitly private `PrivateOwnershipTableGeometry` subclass because
its two-tag dummy body never represented complete public geometry. Native
fixtures are not changed by that compatibility adjustment.

## Verification

`RegisterStoredTableGeometryTests` covers all eight native packets through both
input and output transports, profile-specific synthetic projections, empty
records, malformed counts and frames, unknown variants, source references and
lifecycle operations. `verify_stored_table_geometry.py` requires all 36 exported
artifacts. It compares exact native payloads and native wrapper identity, checks
all 316 selected carrier identities in both output transports, checks synthetic
projections and complete opaque bodies, and rejects 192 actual output
corruptions. Every output is opened and audited independently with ezdxf.
The [qualification receipt](table-geometry-qualification.json) records 387 unique passing cases in each configuration, including 142 new geometry cases, with all five related independent gates passing. An independently written runtime review passes 72 additional checks against the frozen production assembly.

Native AutoCAD open/AUDIT/save/reopen remains unexecuted. Full TABLE authoring,
regeneration, cell layout, arbitrary private payload interpretation, complete
cross-document dependency import and native application acceptance remain
outside this stored module.
