# TABLE ownership prerequisites and independent reader scope

## Implemented ownership structure

The database can adopt children declared by an internal object schema. It visits
every declared child during the same registration preflight as dictionaries and
extension dictionaries. Validation checks reciprocal `Owner` identities,
registration, duplicate slots, cycles, and children outside a declared owner's
slots before allocating handles.

The first binding covers the single-section XRECORD envelope beginning with
`ACAD_ROUNDTRIP_2008_TABLE_ENTITY`: group 360 owns a `TABLECONTENT` object and group
361 owns a `TABLEGEOMETRY` object. Both children declare that XRECORD as their
common owner. The binding checks both candidates before assigning either owner.
It exposes both targets through the existing database-reference hook, registers
the complete detached graph, and then materializes the assigned handles. The
graph-copy hook remaps child identities independently of stored source handles.

`DxfXRecord.IsSchemaManaged` tells callers that the ownership schema controls the
payload. Such records expose their tags for inspection and reject generic
`Data` insert, replace, remove, and clear operations. Ordinary XRECORD payloads
remain editable. No public operation accepts an arbitrary child-owner assignment.

The imported children remain opaque in this prerequisite. Their subclass payloads
are preserved; their cell/style semantics are not represented by a new public
model. Full graph cloning continues to reject opaque children before destination
registration. The reference-copy test exercises the schema binding separately
from that intentional opaque-clone boundary.

## Native envelope variants

The complete, hashed source corpus contains eight owning envelopes:

| Source profile | Envelope count | Owned children | Reader behavior |
| --- | ---: | --- | --- |
| R2004 | 2 | TABLECONTENT, TABLEGEOMETRY, DATATABLE | Bind the exact observed composite ownership schema |
| R2007 | 2 | TABLECONTENT, TABLEGEOMETRY | Bind the single-section ownership schema |
| R2010 | 2 | TABLECONTENT, TABLEGEOMETRY | Bind the single-section ownership schema |
| R2013 | 1 | TABLECONTENT, TABLEGEOMETRY | Bind the single-section ownership schema |
| R2018 | 1 | TABLECONTENT, TABLEGEOMETRY | Bind the single-section ownership schema |

Both R2004 examples append `ACAD_ROUNDTRIP_PRE2007_TABLE` and
`ACAD_ROUNDTRIP_PRE2007_TABLECELL` sections, including another group-360 owner
slot. The complete observed grammar is now bound by the
[composite ownership module](composite-table-ownership.md), including the native
DATATABLE's 41 row-XRECORD descendants. Unknown additional sections remain unbound.
A duplicate owner slot within the recognized
single-section envelope is rejected as malformed. The exact source packets,
upstream revisions, and hashes are in
[the fixture manifest](../../tools/table_oracle/fixtures.json).

The relationship is grounded in those actual DXF records. Autodesk's published
[TABLE entity group-code reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-D8CCD2F0-18A3-42BB-A64D-539114A07DA0.htm)
describes the flat entity fields, including TABLESTYLE and display-block pointers.
It does not supply a complete public grammar for every roundtrip object or
private extension. The presence of the marker alone is therefore insufficient to
claim every payload variant.

## Executable independent reader proof

Pinned ACadSharp revision `f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11` and its exact
CSUtilities submodule build without source edits using .NET 8 and a compiler
language-version override. With `Failsafe=false`, the reader loads all five
original source drawings and exposes all eight flat TABLE grids. A separate
ezdxf 1.4.4/raw-tag check passes 100 string/numeric comparisons, including legacy
empty strings, plus stored row/column sizes, value flags and display strings,
style/block handles, and all eight native ownership envelopes. All five originals
have zero ezdxf audit errors or repairs.

The reader does not retain the registered TABLECONTENT targets: all lookups by
their actual handles return null, while `TableEntity.Content` is a detached
fallback with handle zero. TABLEGEOMETRY and CELLSTYLEMAP support is incomplete.
Consequently this oracle qualifies flat entity values only. It does not qualify
backing-object association, native cell/style semantics, formulas, dates, block
content, or rendering. Reproduction instructions and executable source are in
[tools/table_oracle](../../tools/table_oracle/README.md).

## netDxf qualification

The structural suite checks all six supported profiles and both transports,
schema-managed payload protection, atomic binding/adoption failures, foreign
ownership, reciprocal registration, case-insensitive handles, reference-copy
isolation, malformed single-section input, preserved composite envelopes, and
the opaque-clone boundary.

Additional tests decompress the five unchanged original fixtures, verify their
source SHA-256 hashes, and extract all eight real XRECORD owning envelopes and
their original child records. Each extracted graph is loaded and saved through
text and binary DXF. The wrapper's parent dictionary is the sole rewritten common
relationship. Its payload and each opaque child's record, identity, and owner
remain source-exact. These minimal carriers exercise ownership and structural
preservation; they do not reconstruct external cell/style resource dependencies.
The independent
[ownership verifier](../../tools/verify_declared_ownership.py)
checks 12 authored structural outputs plus 16 extracted native envelopes.

Whole-file load/save was attempted separately against exact baseline `7d1e92b`
and prerequisite checkpoint `9c47d2d`. All five drawings stopped at identical
pre-existing gates before TABLE ownership fixup, so no whole-file save completed:

| Source | Identical baseline/prerequisite gate |
| --- | --- |
| ezdxf R2013 and R2018 samples | Unsupported MLEADERSTYLE group 298 |
| ACadSharp R2004 sample | MULTILEADER input restricted to qualified R2007+ |
| ACadSharp R2007 sample | MULTILEADER payload begins with 300 instead of required 270 |
| ACadSharp R2010 sample | Proxy graphics use group 160 below the qualified profile gate |

These results are recorded in
[qualification.json](../../tools/table_oracle/qualification.json).
Extracted-packet success is a separate result from whole-drawing compatibility.

## Remaining structured TABLE module

The next useful module is an editable fixed grid with literal string/integer/real
cell values, explicit empty cells, row heights, column widths, TABLESTYLE
references, and synchronized flat/backing stored values. Initial output
qualification is planned for R2010/R2013/R2018; the wider structural transport
tests above do not establish TABLE entity export eligibility.

Immutable typed TABLECONTENT and TABLESTYLE storage is now implemented, with
qualified source-reference enumeration, packet validation and independent
preservation checks. Editable cells still need complete backing schemas,
clone/resource mapping and independent assertions of synchronized cell values.
CELLSTYLEMAP and TABLEGEOMETRY require separately grounded subsets. Native display
regeneration remains a distinct concern: Autodesk's
[AcDbTable.generateLayout documentation](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbTable__generateLayout.html)
describes deriving layout from the definition and styles. This prerequisite does
not invent an undocumented regeneration flag or claim native rendering.
