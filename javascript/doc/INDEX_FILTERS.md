# Typed index and filter models

The native JavaScript port includes original-path `DxfIdBuffer`, `DxfSpatialIndex`,
`DxfLayerIndex`/`DxfLayerIndexEntry`, `DxfLayerFilter`, and `DxfSpatialFilter` models.
These extend the existing typed database-object foundations. They do not construct
or evaluate a spatial/layer index, clip INSERT geometry, or supply the missing
registered `DxfObjectDatabase`/`DxfDocument` implementation or typed DXF IO.

`DxfIdBuffer.References` is an ordered editable collection retaining duplicate and
null references. Clone shells start empty; the explicit reference-copy operation
passes every entry, including null, through the supplied mapping function.
Registered reference checks are delegated to the existing structural database
host contract, not simulated by accepting code-name strings as type identities.

`DxfLayerIndex.SetEntries` snapshots the complete iterable and validates every
entry before changing ownership. A detached index can adopt unowned buffers and
releases buffers removed by successful replacement. Each entry needs a distinct
buffer; repeated stored layer names remain valid. Cross-owner, erased-object,
registration-state, and ancestor-cycle violations are rejected. A registered
host must preserve the complete child set during ordinary replacement. This
host behavior has supplemental adapter tests, not full document qualification.
Old read-only entry views retain their original list after replacement; counts
remain derived from each referenced buffer's current collection.

Layer-name collections retain case, spacing, Unicode, unresolved names, and
repetitions. They reject empty strings, NUL/CR/LF, and unpaired UTF-16 code units.
They never resolve or rename layer-table entries. Public collection index guards
run before the item-specific validator, matching `Collection<T>` ordering.

Spatial timestamps retain finite authored doubles without calendar conversion.
Spatial filter boundaries are independent value snapshots with 2–32,767 points.
Normals retain their nonzero magnitude rather than being normalized. Optional
front/back distances retain null and signed zero separately. Transform matrices
must be finite and affine, but may be singular. Failed iterable or scalar edits
leave prior state intact. Clone shells own independent vectors and matrices.

## Verification at this increment

The additional independent corpus contains 278 scenarios and 1,307 operations;
all results are compared with the unmodified pinned C# assembly. The expanded
required database-model stage has 808 scenarios and 3,784 operations, passing
exactly in local Debug and Release. All pre-existing cases remain present.
The existing browser generator automatically includes this corpus; its required
comparison count increases from 103,199 to 103,477. Hosted execution must be
verified for the actual published commit; local differential tests alone do not
qualify Windows or HTTP-origin browser execution.

Six complete detached runtime cases (`layer-index/api/0` through `/5`) are ported
from `LayerIndexTests.cs`. Registered-document cases `/6` through `/9` and typed
wire tests remain unregistered, not shortened under passing original identities.
Eleven supplemental tests cover snapshots, mutation atomicity, ownership, finite
values, maximum boundary size, mapping and structural host behavior. The original
JavaScript suite passes 2,575 cases and the supplemental suite passes 325 cases.

The earlier eleven-file coordinate verification patch remains separately saved,
not published: the tool rejected the shared `GeometryOracle/Program.cs` upload.
This increment neither retries that blocked file through another route nor
partially activates its coordinate gate. Its production coordinate/view models
remain intact. Full C# parity, the Debug Bézier and Windows Shape numerical
boundaries, registered ownership, remaining typed APIs/tests and performance
qualification remain open.
