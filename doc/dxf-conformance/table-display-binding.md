# Explicit TABLE display-block selection

`StoredTable.DisplayBlock` exposes the actual source-bound block identity.
`StoredTable.ReplaceDisplayBlock(Block)` selects a different already registered
flat display block for a qualified source TABLE. It updates the block-reference
name and the corresponding BLOCK_RECORD handle together and clears stale proxy
graphics. The preceding [calculation and layout APIs](table-calculation-layout.md)
can generate the replacement block; registration and selection are explicit.

## Usage

```csharp
// layout is a DxfTableLayout built with explicit styles, literal text and metrics.
Block display = layout.BuildDisplayBlock("UpdatedTableDisplay");
document.Blocks.Add(display);            // Separate ordinary registration.
table.ReplaceDisplayBlock(display);     // No further handle allocation.
```

The new operation does not infer missing native cell data from the display.
Backing TABLECONTENT values, inline TABLE cells, dimensions, TABLEGEOMETRY,
formatting caches and native regeneration policies stay unchanged. Registering
a block before calling this API is not part of its transaction: a rejected
selection does not undo the caller's prior registration. The source insertion
point, normal and other transformation fields also remain fixed; generated
layout coordinates use the documented upper-left local origin.

## Positive schema and identity checks

Autodesk's [TABLE DXF reference](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-D8CCD2F0-18A3-42BB-A64D-539114A07DA0.htm)
identifies the block name in AcDbBlockReference, the group-343 BLOCK_RECORD
pointer in AcDbTable, and common proxy-graphics length and chunks. The five
previously pinned native families supply actual corresponding records. Only the
qualified two-subclass stored TABLE shape with an existing flat grid projection
is admitted here. Both original selectors must identify the same actual block.
Unknown/private subclass data, private group-102 packets, missing selectors,
unresolved source block names and conflicting source pointers reject.

The replacement must be the exact registered source-document block and record,
with matching collection owner, name entry and handles. Detached, foreign and
removed blocks reject rather than being adopted or imported. A different target
must be a local, non-layout block with no attribute definitions and only LINE,
SOLID and MTEXT entities. In particular, nested INSERT/TABLE graphs cannot create
a recursive block-reference cycle through this API. These admission rules fit
the generated display geometry; they are not a general arbitrary-block rebinding
contract. Block/resource objects remain ordinary mutable document objects.

Selecting the current block is a no-op after source identity and schema checks.
It preserves the payload and proxy graphics, even if the block has been renamed.
Renaming a selected resource later still updates the emitted block name through
its bound identity. A new selection stores the current name with the existing
source-profile Unicode/backslash escaping and string-length limits.

## Publication and dependency behavior

All source/target/graph checks, string encoding, packet allocation and dependency
list construction finish before publication. The final swaps do not call user
code or allocate handles. Both selectors change in the same immutable payload
snapshot. Existing semantic dependencies and named text-style bindings are
retained; the new block record receives both its explicit pointer use and its
independent name-based use. Reassignment releases those old uses only. Other
TABLEs, INSERTs and resource consumers retain their ordinary removal guards.
Previously returned payloads and reference-membership lists remain unchanged.

A successful different-block selection clears the common proxy bytes and their
presence flag. It does not preserve a stale graphics cache that describes the
former display. Rejected or same-identity requests do not clear proxy data.
Native record-specific binary packets outside AcDbEntity are not removed.

The former block is not deleted automatically. It may be removed separately
when ordinary dependency checks establish that no consumers remain. The new
block is protected from removal, both immediately and after save/reload.
Concurrent-thread mutation, process failure and out-of-memory recovery are not
transactional guarantees.

## Verification

`table-display/` adds 120 C# cases: ten positive native-family text/binary paths
and 110 rejection paths. Coverage includes generated layouts, no-op selection,
block name/pointer coherence, proxy invalidation, snapshots, no allocation,
repeated selection, rename, successive display selection, reference release,
foreign/detached/removed blocks, layout or nested targets, attributes, source
profile changes, invalid graphs and private/mismatched source packets.

`tools/verify_table_display_binding.py` requires exactly twenty before/after
files. The target block is already registered in each before file, isolating
selection from registration. The independent ezdxf parser derives the two
requested selector changes and removal of only a complete, length-checked
AcDbEntity proxy packet. Every other ordered physical record must remain exact,
including both display blocks, source geometry, inline cells, TABLECONTENT,
CELLSTYLEMAP and resource records. The existing strict empty ACAD_LAYERSTATES
identity normalization is the sole unrelated writer normalization; HEADER
clock/seed values and CLASS records are outside this physical-record comparison.

Each selected-table tag is corrupted independently. A proxy is reintroduced as
a negative control, each unrelated record is changed, and the selected TABLE
is removed. The same whole-record comparator must reject those variants. All
before and after files also undergo independent DXF audit without repairs.
Missing and extra fixture inventories must reject. The source families remain
two complete original drawings and three documented native-record carriers,
not five whole unchanged originals. The new display styles and text metrics
are explicit synthetic inputs, not native font-measurement evidence.

The initial display-binding checkpoint `1fa60e5948f69d67003b1c489f58c9f0f070548a`
passed 37,229 unique Linux Debug cases, including all 120 new display cases.
The independent checker passed ten pairs and 7,204 corruption controls, with
380 stale proxy tags removed across both transports. Final-head results and
artifact hashes are recorded separately in PR #103; these initial results do
not substitute for executing the final formula-depth and documentation changes.

## Native regeneration remains distinct

Autodesk's [RecomputeTableBlock contract](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Table_RecomputeTableBlock__MarshalAsUnmanagedType_U1__bool.html)
reconstructs the referenced block to reflect changes to the table object.
This API is deliberately named *ReplaceDisplayBlock*, not RecomputeTableBlock:
it selects caller-prepared geometry without pretending that every native inline,
backing, format and private cache has been recomputed. A native application may
regenerate the display from retained table data. Native open/AUDIT/save/reopen,
visual equivalence, installed-font metrics and retention after native regeneration
have not been executed or established by this increment.
