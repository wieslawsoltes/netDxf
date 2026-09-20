# Raw lightweight-polyline reversal

`DxfRawDocument.ReverseLwPolyline(record)` reverses the traversal of an ordinary
LWPOLYLINE in the existing R14–R2018 raw profiles. Records must belong to the
exact snapshot. Valid zero/one-vertex definitions return the source unchanged.
For larger definitions the method returns a new same-version snapshot.

```csharp
DxfRawDocument reversed = raw.ReverseLwPolyline(record);
```

Positions and opaque vertex identifiers reverse together. Each reversed edge
negates the previous edge's bulge and exchanges its start/end widths. Optional
presence travels with the segment attribute: absent values remain absent,
explicit zero stays explicit, and signed-zero bulges have their sign reversed.
No arc fitting, coordinate transform, angle computation or endpoint rounding
occurs in the production operation. The first new vertex is the last old vertex,
including for closed curves. Open curves' unused final outgoing attributes use
the same reversible cyclic mapping as typed Polyline2D.Reverse; they are not lost.

The count, closure/linetype flags, constant width, elevation, thickness and
extrusion are unchanged. Variable-width fields are reversed even under nonzero
constant width: their raw segment values are retained, not newly resolved or
rewritten according to a precedence assumption. Point/identifier DxfTag objects
move by identity. Nonvertex tags remain the same objects in the same relative
order, even when interleaved with the old vertex slots. Width code changes and
bulge sign changes necessarily create replacement tags.

Generated vertex packets use adjacent X/Y followed by optional identifier,
start width, end width and bulge. Thus two reversals restore geometry values,
identifier order, signed bits and field presence, not arbitrary original
per-vertex tag ordering, lexical spelling or original-byte eligibility. The
original snapshot remains byte-identical and usable throughout. No extra tags
are needed, so the existing total-tag budget is not increased. Parsing and
working storage remain linear in source size; no million-vertex performance
acceptance or whole-process allocation bound is asserted.

The existing topology guards reject actual edits involving proxies, application
control groups, embedded tails, unknown ordinary fields, geometry-sensitive
XData, or exposed incoming references. Hidden vertex-ID references, associations,
header extents and private caches are not regenerated. Linetype phase and
native rendering are not certified equivalent merely because the centerline
and width progression are preserved in reverse. No historical typed dialect
or pre-R11 support is introduced.

## Qualification

The harness exercises seven profiles, both input/output transports, ENTITIES
and BLOCKS, open/closed curves, variable/constant widths, alternate packet
ordering, omitted fields, opaque IDs, signed zero and finite extremes. It checks
source-byte and tag-identity preservation, double reversal, exact tag budgets,
interleaved comments/headers, malformed records and dependency refusal.

The independent checker regenerates source fields and exact expected tag
sequences, uses an independent reader for actual vertices, and compares WCS
samples on reversed line/arc segments and their linear width progression.
Wrong, missing and duplicate selected fields and altered metadata must fail
the same positive validator. The corpus is synthetic rather than native
AutoCAD producer evidence. Hosted execution totals are recorded in the PR;
no local C# execution is inferred without an installed SDK.

Autodesk's [LWPOLYLINE reference](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm)
defines stored vertex and segment fields. Reversal, admission, identity and
exception contracts here are library choices. Full historical typed loading,
FIELD/TABLE/private-cache regeneration, dependency-complete imports, general
version conversion and native AutoCAD/font/visual qualification remain separate.
