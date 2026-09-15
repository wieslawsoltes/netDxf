# Fifth mixed module qualification

`FifthMixedModuleTests.cs` exercises the stored TABLE, DATATABLE and LAYER_INDEX
modules together with declared ownership, IDBUFFER, dictionary aliases,
OBJECT_PTR, LAYER_FILTER, extension dictionaries, XRECORD, persistent reactors,
APPID/XData, mapped cloning and terminal erasure. The twelve scenarios cover
R2000, R2004, R2007, R2010, R2013 and R2018 in ASCII and binary transports.
DATATABLE and the pinned native TABLE packet enter from R2004. R2000 is a
graph-only scenario.

Each scenario emits the source graph, its mapped copy, and each graph after
teardown. The mandatory independent gate requires all **48 DXFs and 12 actual
clone maps**. It uses ezdxf 1.4.4 and complete low-level records, checks ordered
payloads and ownership separately, compares every graph identity against its
actual sidecar, checks canonical CLASS metadata and counts, and audits every
output with no errors or repairs. Its ninety controls corrupt parsed emitted
records rather than synthesizing another expected library object: they change
an index count, a buffer owner, an arbitrary 320 value, a semantic 340 target,
an XData 1005 target, a DATATABLE owner code, a declared child's owner, and a
native TABLE dimension where applicable. Each must fail for the intended
record and reason. Missing outputs or maps fail the exact inventory check.

## Mixed graph and lifecycle

The soft-owner dictionary flag and a soft alias are retained independently
from the actual declared child graph. LAYER_INDEX owns three IDBUFFERs, with
six, three and zero references. The lists contain repeated references, nulls,
cross-buffer references, the index itself, a DATATABLE or OBJECT_PTR, and an
external LINE and XRECORD. Index names preserve repeated, case-distinct,
unresolved strings without adding LAYER records. Index timestamps preserve
double bits.

The DATATABLE has three rows and seven columns, including repeated column
names, signed integer extremes, Unicode, a literal DXF escape-looking string,
empty text, nulls, hard and soft owned children, hard and soft pointers, and
object IDs. Both owner kinds give reciprocal ownership to their children.
Pointers retain the original target owners. A failed attempt to adopt an
index-owned buffer as a DATATABLE child leaves the table's immutable column
snapshot and the buffer's ownership intact.

The mapped copy follows every declared child, including DATATABLE children,
index buffers and the OBJECT_PTR extension dictionary. It requires explicit
mappings for the external LINE and XRECORD. Unmapped clone rejection leaves
the destination allocation seed and root dictionary unchanged. Sidecars
contain the actual source and destination handles for eleven R2000 graph
members or fifteen later-profile members, plus the two external mappings.
Aliases, reactors, XData 1005 targets and semantic XRECORD targets follow these
maps. XRECORD 320 and 329 values retain their original literal source spelling.
Changing the clone's APPID, table name and filter name leaves the source intact.

The source LINKS XRECORD writes a zero-padded lowercase semantic pointer, and
a separate incoming blocker uses the same spelling convention during erasure
preflight. Numeric identity controls therefore exercise the shared identity
registry and incoming-reference detection. External XRECORDs also retain
arbitrary 320 and 329 values equal to graph handles. Those values survive
successful graph erasure. A semantic incoming pointer rejects erasure without
changing ownership, identity registration or the allocation seed; removing the
blocker permits the exact declared closure to enter terminal erased state.
The external LINE and XRECORD survive with unchanged handles and payloads.
After reload, class counts match the empty application-object inventory.

## Exact native TABLE scope

For each admitted profile, the test decompresses a pinned source file,
verifies its decoded SHA-256 against `tools/table_oracle/fixtures.json`, and
extracts the first ENTITIES-section `ACAD_TABLE` packet beginning at
`AcDbBlockReference` and ending before XData. The independent Python gate
repeats this extraction directly from the source gzip rather than accepting
expected subclass bytes from the sidecar.

| Profile | Pinned source |
| --- | --- |
| R2004 | `sample_AC1018_ascii.dxf` |
| R2007 | `sample_AC1021_ascii.dxf` |
| R2010 | `sample_AC1024_ascii.dxf` |
| R2013 | `acad_table_simple.dxf` |
| R2018 | `acad_table_with_blk_ref.dxf` |

The original native subclass packet is placed under generated common entity
metadata. Generated handles are moved into a separate numeric range. One
native group-342 pointer resolves to the graph's SHARED XRECORD by assigning
that synthetic target the pointer's numeric identity. **This substitution is
a generic semantic-reference test. It does not qualify the XRECORD as a
TABLESTYLE or claim fidelity of the original source document's resource graph.**
The sidecar records the source file, SHA-256, original source entity, emitted
carrier, exact pointer spelling, profile and this limitation. Other native
stored pointers remain exactly as extracted.

The C# checks compare the full ordered native subclass packet before and after
allowed layer, color-name and XData/APPID edits, then after save/reload. The
independent gate compares emitted packets to the original source extraction,
including escaped string spelling, binary data and floating-point bits. Common
entity metadata is generated and then edited; it is deliberately checked as
common metadata, not described as an exact copy of the original native entity.
The TABLE semantic pointer protects the shared graph from erasure. After the
TABLE is removed, teardown succeeds. Direct TABLE cloning, cross-document
adoption, profile conversion and reattachment of a removed TABLE all reject.
Profile conversion rejects before emitting bytes.

The clone and both erased outputs contain no stored TABLE. These scenarios do
not author TABLE layouts, evaluate formulas or fields, regenerate cells, run
layer indexing, or qualify full native ACIS/history object graphs.

## Reproduction

Run conformance with `DXF_TEST_ARTIFACTS` naming the output folder; the focused
entry point is `RegisterFifthMixedModuleTests`. After generating the artifacts:

```sh
python tools/verify_fifth_mixed_modules.py /path/to/artifacts
```

The independent gate accepts no missing or extra `fifth-mixed-*` artifacts.
Run the same registration and gate for Debug and Release builds.

## Qualification receipt

On 2026-09-15, the strengthened graph cases passed **12/12 in Debug and 12/12
in Release**. Each build generated a fresh set of 48 DXFs and 12 maps. Each
independent gate passed all sixty artifacts, ninety parsed-output corruption
controls, and all forty-eight ezdxf audits with zero errors or repairs.
Separately omitting one required source DXF or one required clone map from
the emitted set caused the exact inventory gate to reject that missing file.

This qualification includes the TABLE public-field refinements through
`bb1005e` and the numeric handle fix `168ea90`. Before that fix, the source
XRECORD's valid noncanonical group-340 pointer caused all twelve strengthened
cases to fail writer OBJECTS validation as an unresolved reference. The same
graph with canonical pointer spelling had passed. After the fix, the emitted
source spelling remains unchanged, its mapped copy names the actual target,
and both semantic erasure protection and arbitrary-handle controls pass.
The independent gate checks this before/after spelling distinction directly
in the emitted records.
