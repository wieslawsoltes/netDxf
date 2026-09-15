# Stored CELLSTYLEMAP

`DxfStoredCellStyleMap` preserves the complete loaded `AcDbCellStyleMap` payload
and exposes immutable ordered entries. Each entry provides its stored `Id`, raw
`StoredType`, decoded `Name`, and complete immutable `FormatPayload`. The object
remains in its source document and DXF profile. Formatting packets are retained
without evaluating their fields or assigning fixed title, header or data roles.
There is no public construction, editing or style regeneration API.

## Evidence and admitted grammar

Five actual CELLSTYLEMAP objects occur in the five originals pinned by
[`table_oracle/fixtures.json`](../../tools/table_oracle/fixtures.json). They cover
R2004, R2007, R2010, R2013 and R2018, with three entries per object. The two ezdxf
drawings are loaded whole and unchanged. The three ACadSharp drawings use the
existing [complete native carriers](../../tests/fixtures/table-content/README.md)
with 136, 90 and 90 selected records. Every map payload, identity and owner stays
exact; the carrier manifest discloses the permitted adaptations to other
records' common ownership context.

The older scoped TABLESTYLE test helper now also carries the actual source
LTYPE records referenced by its map. Their source handles and payloads are
retained; only the common owning table handle is mapped to the carrier's LTYPE
table, as already done for its source STYLE record. External entity reactors in
that older helper remain explicit placeholders. It does not replace the
complete native carriers used to qualify this module.

The independently maintained [LibreDWG CELLSTYLEMAP wire specification](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec)
(source blob `2f7cbbe8b2b2c7bfb5019080cb4acfaca873a5b3`) confirms the observed
outer envelope and framing. Its secondary version comment describes R2008/R2007
backward compatibility; the pinned R2004 drawing provides direct positive
evidence of this DXF storage shape in R2004. This module qualifies the observed
DXF packets, without making a claim about DWG introduction dates. The parser is
handwritten; no external implementation code is incorporated.

| Packet | Stored groups | Public projection |
|---|---|---|
| Object | 100 `AcDbCellStyleMap`, 90 | Subclass and entry count |
| Entry prefix | 300 `CELLSTYLE` | Ordered entry boundary |
| Formatting | 1 `TABLEFORMAT_BEGIN` through matching 309 `TABLEFORMAT_END` | Complete immutable tags |
| Entry fields | 1 `CELLSTYLE_BEGIN`, 90, 91, 300, 309 `CELLSTYLE_END` | Raw identifier, raw type, decoded name |

The outer count must be nonnegative, bounded by the 1,048,576-tag storage limit,
and consistent with the available entry packets. The reader checks a minimum
packet size before growing lists. Recognized format frames are TABLEFORMAT,
CONTENTFORMAT, CELLMARGIN and GRIDFORMAT; their group-1/group-309 delimiters must
balance, with at most 64 simultaneous frames. A CELLSTYLE frame cannot be nested
inside a format packet. Inner formatting values remain uninterpreted stored
tags, including arbitrary scalar fields and literal marker strings in group
300. Entry names can also contain marker strings. Identifiers and types retain
the complete signed integer range; duplicate identifiers and names are retained
in source order. These facts do not establish formatting semantics or role
uniqueness.

One known subclass and recognized delimiters select the stored grammar.
Missing, reordered, duplicated or surplus outer fields and mismatched known
frames are rejected. A different/additional subclass, an unknown frame marker,
private common metadata, private body group-102 data, or an R2000 input retains
the complete object opaquely. Extended data in the recognized public payload
must follow a genuine application registry. Unknown private bodies are
preserved without projecting entries.

## Identity and lifecycle

All five native maps are hard-owned by the dictionary entry
`ACAD_ROUNDTRIP_2008_TABLESTYLE_CELLSTYLEMAP` in a TABLESTYLE extension dictionary.
The map's persistent reactor points to that same dictionary. TABLESTYLE keeps
the exact child identity whether the child is stored or opaque.

Nonzero semantic handles bind actual accepted source records, including
retained metadata objects. Generated defaults, discarded duplicates and
coincidental handles cannot supply targets. The native formatting packets have
group-340 STYLE and LTYPE dependencies; null handles remain null. Arbitrary
groups 320–329 do not become semantic references. `References` retains repeated
semantic targets in packet order without inferring a narrower field-specific
target type. Additional target kinds used by identity tests are explicitly
synthetic controls.

The actual registered owner and every ancestor must exist without an ownership
cycle. Stored maps retain their source owner, dependencies and DXF profile.
Removal of a referenced resource or an owning entity/block is refused. Cloning
and erasure of a map or a containing ownership graph reject before mutation.
Common metadata and XData retain their established interfaces and validation.
Compatible CLASS definitions track the stored and opaque instance count;
missing definitions are synthesized when stored instances exist, while a
conflicting class causes save to reject before writing output.

## API migration

`DxfTableStyle.CellStyleMap` now returns `DxfDatabaseObject`, permitting the same
property to expose a `DxfStoredCellStyleMap` or a retained `DxfOpaqueObject`.
`DxfTableStyle.StoredCellStyleMap` is the nullable typed convenience property.
This return-type change affects both source compatibility and the CLR getter
signature: clients compiled against the earlier API must rebuild. Code that
previously accessed `.CellStyleMap.Tags` must use `.StoredCellStyleMap.Payload`
for the stored shape or pattern-match `DxfOpaqueObject` for a private shape.
The earlier `BackingContent` widening has the same recompilation requirement.

```csharp
if (style.StoredCellStyleMap is DxfStoredCellStyleMap map)
{
    foreach (DxfStoredCellStyleMapEntry entry in map.Entries)
        Console.WriteLine($"{entry.Id}: {entry.Name}");
}
else if (style.CellStyleMap is DxfOpaqueObject privateMap)
{
    IReadOnlyList<DxfTag> privateTags = privateMap.Tags;
}
```

## Verification

`RegisterStoredCellStyleMapTests` covers all native packets through both input
and output transports, raw fields and custom names, empty maps, known malformed
envelopes, opaque variants, actual source identities and lifecycle guards.
`verify_stored_cell_style_map.py` requires all 36 exported artifacts. It compares
exact native payloads, the complete TABLESTYLE ownership chain and formatting
dependency identities; checks all 316 selected carrier identities in both
output transports; checks synthetic entry fields and complete opaque bodies;
and rejects 212 actual output corruptions. Every output is independently opened
and audited with ezdxf. The qualification receipt records measured final results.
The separately frozen independent review passed 58 runtime cases and audited
26 outputs with no errors or repairs. The unchanged
[review receipt](cell-style-map-review/summary.json) and
[reproducible harness](../../tools/cell_style_map_review/README.md) are retained.

Native AutoCAD open/AUDIT/save/reopen remains unexecuted. Format evaluation,
editable table styles, role assignment, cell regeneration, arbitrary private
schema interpretation and full cross-document dependency import remain outside
this stored module.
