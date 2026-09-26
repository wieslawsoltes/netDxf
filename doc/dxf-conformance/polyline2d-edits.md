# Explicit 2D-polyline editing and parent graphics coherence

This extends the 3D-polyline edit work in PR #212 to the `Polyline2D` model used
for LWPOLYLINE and retained ordinary legacy 2D POLYLINE records. It is not a new
DXF-version converter or a native rendering implementation.

## Public operations

`SetVertex(int index, Vector2 position)` changes one OCS position while retaining
the same vertex object, list, vertex identifier, bulge and optional widths.
`SetVertexBulge(int index, double bulge)` changes only the outgoing bulge.
`SetVertexWidths(int index, double? startWidth, double? endWidth)` changes the two
raw outgoing width overrides together. Null removes an override; explicit zero
remains present. The latter method does not change ConstantWidth or legacy
header defaults. A nonzero lightweight ConstantWidth may still mask raw widths;
an absent legacy override continues to inherit its corresponding parent default.

```csharp
polyline.SetVertex(1, new Vector2(20, 30));
polyline.SetVertexBulge(1, 0.75);
polyline.SetVertexWidths(1, null, 0.0); // inherit start; explicit zero end
```

The complete current vertex sequence and every candidate are validated before
mutation. Coordinates and bulges must be finite; widths must be finite and
nonnegative. Null vertices, repeated object identities, invalid indices and
incompatible retained vertex mappings reject without changing geometry, list
identity, records or the parent cache. Identity comparisons do not call a
derived vertex's Equals/GetHashCode. Both width candidates are checked before
assigning either. A late invalid vertex likewise cannot leave an early edit
committed. These methods allocate no drawing handles and preserve child metadata.

Retained chains reuse existing ordinary-topology, width-representation and
source-packet admission checks. A candidate which adds optional width/bulge
fields is checked against the 4,096-tag record and 1,048,576-tag chain budgets
without modifying the live vertex. Source-version and document-registration
checks remain at their existing read/adoption/save boundaries. Detached plain
retained clones can receive count-preserving edits; this does not make
externally referenced graphs dependency-complete clones.

## Graphics invalidation

Changes to Elevation, Thickness, IsClosed, LinetypeGeneration or SmoothType now
clear parent proxy graphics. Existing scalar admission and smoothing refusal
rules remain unchanged. In particular, the legacy header scalar setters have
not acquired a new finite-value contract; later save validation still applies.
Floating-point changes compare stored bits, including signed zeros and NaN
payloads, rather than geometric tolerance. Changes to smoothing flags or the
stored entity code are also observed.

The three explicit vertex methods clear the parent cache only for changed
stored bits or width presence. Exact no-ops preserve absent, empty and nonempty
caches and do not replace a vertex or invalidate a List enumerator. Changed
vertex properties likewise leave the collection's enumeration version alone.
Reader hydration's late common-data attachment and clone copying are unchanged.
No replacement graphics are fabricated.

Reverse retains its existing identity/finite-sequence validation, segment
orientation, inherited-default swapping and cache behavior. The added prospective
packet validation described below runs before any list reversal or default edit.
Existing exception wording for existing validation stays unchanged; no serializer
or curve algorithm is modified.

## Bulk packet preflight

`SetConstantWidth` can add two previously absent fields to every retained
VERTEX. `Reverse` can move outgoing width/bulge fields onto a different retained
record with a different metadata size. Checking only the *current* packet sizes
allowed either operation to mutate successfully into a state later rejected by
Save. Both operations now preflight each final record and the final aggregate
tag count before changing a live vertex, ordering, defaults, ConstantWidth or
proxy bytes. Single-vertex and bulk edits share the same candidate-count helper;
the bulk traversal is linear, not a full-chain scan per point.

The unchanged limits are library admission budgets, not Autodesk DXF format
limits: 4,096 tags per retained record and 1,048,576 for the retained chain.
Counts use the existing GeometryTags optional-field policy and include existing
child metadata/XData. Both exact boundaries are admitted. Adding one tag beyond
a boundary rejects without partially applying the requested edit. The existing
current-state validation still runs first, so this is not an API for repairing
already inadmissible source records.

No-op SetConstantWidth at a full packet preserves state, cache and active list
enumerators. Invalid scalar candidates retain their existing rejection. The
ordinary non-retained paths, unsupported smoothing modes, existing source
ownership spellings and removal/reference guards are unchanged. Reversal already
swapped legacy start/end defaults; this continuation preserves and tests that
behavior rather than reimplementing it.

## Verification

The original shared edit harness defines 408 cases: 90 header cases, 81 vertex changes,
27 exact no-ops, 171 refusal cases, 15 grouped scalar/packet-budget/clone/mapping/
identity cases, and 24 version/transport/representation matrices. It is included
in conformance, ordinary installed-package consumption and each existing exact
package-asset/runtime consumer. Existing smoke scenario counts are not inflated.

Legacy inputs are explicitly assembled from a valid lightweight carrier, with
independent VERTEX/SEQEND packets, mixed observed parent/block-record owner
forms, inherited defaults, optional overrides, vertex identifiers and child
XData. The intermediate lightweight carrier respects the existing 2013+ group91
writer profile; earlier lightweight cases omit identifiers, while retained
legacy cases inject and preserve them independently. This does not relax the
existing down-save check or claim general pre-2013 lightweight identifier support.

Six typed versions, both transports, both representations, four placements,
three cache states and fourteen operations produce 72 drawings and 12,096
parent records, with four points each. Source, edited save and opposite-format
resave are checked for exact geometry bits, optional widths, identifiers,
parent/child/terminator identities, metadata, placement, following LINE geometry,
source bytes and stream lifetime.

The independent checker derives the expected values from specified inputs, not
captured netDxf results. It checks physical packets before independent-reader
normalization, exact optional presence, ordered proxy packets, unselected
metadata and every cross-save identity. It also requires independent ezdxf
interpretation and zero database audit errors/repairs. Corrupted flags, scalar
bits, missing/extra widths, stale/missing proxies, owners, record identities and
fixture inventories must reject. Passing definitions are not execution evidence;
actual runs and source hashes belong in the accompanying qualification record.

The bulk continuation adds 47 shared cases, for 455 combined cases. Eighteen
per-record cases cover both operations, three cache states and sizes immediately
below/at/above the prospective limit; three grouped no-op cases exercise all
representations; two 256-vertex cases cover exact aggregate admission and a
one-tag overflow while every individual candidate fits. Twenty-four matrices
exercise six versions, both transports and both bulk operations, generating
72 additional source/output/opposite-format-resave drawings. Each contains one
parent, four retained vertices and one terminator at a real packet boundary.

The separate bulk verifier checks all 72 parents / 288 VERTEX records, inherited
defaults, optional widths, exact coordinate and signed-bulge bits, unchanged
metadata, packet sizes, ordered identities, source-versus-edited proxy bytes and
following LINE geometry. Cross-save identities are never normalized. The
same-compiled-test comparison and actual output replay belong in PR evidence;
these definitions do not imply hosted or native AutoCAD qualification.

## Boundaries and primary references

The public mutable Vertexes list and directly mutable vertex objects remain
compatible escape hatches. They cannot notify the parent automatically. Sharing
one mutable vertex across *different* polylines is not detected; callers must
isolate such state or invalidate every affected parent. The new methods reject
sharing within the edited sequence. This is entity-local validation, not a
whole-document transaction, concurrent-edit lock or callback rollback mechanism.

Existing optional-field serialization remains unchanged. For example, a legacy
bulge with an originally absent group42 and a newly assigned signed zero can
still be omitted as a default on output. In-memory bit-sensitive invalidation
must not be represented as universal optional-field bit-preserving serialization.
Child private graphics, native font/linetype/smoothing display, proxy regeneration,
native AutoCAD open/AUDIT/save/reopen, historical typed dialects, private FIELD/
TABLE caches, dependency-complete imports and general version conversion remain
outside this increment. The JavaScript port is separate. Full AutoCAD parity is
not established by these changes.

Primary field references:
- Autodesk LWPOLYLINE: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm
- Autodesk POLYLINE: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm
- Autodesk VERTEX: https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm
