# Coordinated CELLSTYLEMAP identifiers and TABLECONTENT consumers

`DxfStoredCellStyleMap.ReplaceStructureAndRemapConsumers` replaces a qualified
map structure and remaps all recognized column, row and cell style-ID slots in
its owning TABLESTYLE's typed TABLECONTENT objects in one single-threaded
transaction. Unlike `ReplaceStructure`, it does not leave those public ID
references dangling when an entry is renamed or removed.

## API

```csharp
DxfStoredCellStyleMap map = style.StoredCellStyleMap;
if (map == null)
    throw new NotSupportedException("No typed CELLSTYLEMAP is present.");

// Rename the entry ID 3 to 30 and retain its formatting and metadata.
var definitions = map.Entries.Select(entry =>
{
    var definition = DxfCellStyleMapEntryDefinition.FromEntry(entry);
    return new DxfCellStyleMapEntryDefinition(
        definition.Id == 3 ? 30 : definition.Id,
        definition.StoredType, definition.Name, definition.Format);
}).ToArray();

int changedOccurrences = map.ReplaceStructureAndRemapConsumers(
    definitions,
    new[] { new KeyValuePair<int, int>(3, 30) });
```

Mappings are simultaneous, so swaps and cycles are supported without sequential
replacement collisions. Omitted mapping keys retain their existing IDs. Old and
new map IDs must be positive and unique; source keys must exist, and targets must
exist in the new map or be zero. Duplicate keys and negative targets reject.
Removing a referenced entry without a surviving replacement rejects. A deliberate
mapping to zero clears that slot to the source's default/unselected sentinel;
this does not resolve or assign the effective inherited style. Existing zero
slots remain zero. Clearing the map is possible when all nonzero consumers have
explicit zero mappings.

The returned count is the number of changed occurrences, not the number of
entries, tables or distinct IDs. Multiple consumers and repeated selections are
all updated. An identity request preserves original map and content snapshots
when canonical map serialization is unchanged. Structural serialization can
normalize equivalent lexical spellings, as documented for `ReplaceStructure`.

`DxfStoredTableContent.GetCellStyleReferences()` returns immutable occurrence
snapshots with `Kind` (Column, Row or Cell), `StoredId` and `PayloadIndex`. The
index belongs to the associated immutable content payload, not to an arbitrary
later version. This is reference discovery, not an effective-style resolver.

## Source and framing evidence

Autodesk documents [TableContent as formatted data associated with table and
cell styles](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_TableContent.html).
The [pinned LibreDWG TABLECONTENT wire description](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec)
labels the column/row/cell style identifiers and the merged-rectangle coordinate
fields. Its framing is not used as an executable oracle. The actual
TABLECOLUMN, TABLEROW and TABLECELL delimiters and their group-90 slots are
present in the five independently pinned native fixture families. The parser
and transaction implementation are original code.

The five families cover R2004, R2007, R2010, R2013 and R2018. Two inputs are whole
original drawings; three are the previously documented [complete source
carriers](../../tests/fixtures/table-content/manifest.json). Carrier ownership
adaptations remain explicit; these are not represented as five whole originals.

Occurrence discovery checks the column/row/cell frame ordering, placement and
counts against declared dimensions. It distinguishes actual ID slots from other
group-90 fields. Modern AcValue, qualified compact scalar/binary values and
DATAMAP values are isolated so literal marker-like text does not become a style
reference. Supported cell envelopes either omit geometry or carry the observed
linked geometry handle with zero inline geometry items. Extended inline geometry
requires separate qualification and rejects this operation.

Every encountered TABLEFORMAT must have a complete qualified public shape.
The AcDbFormattedTableData subclass must contain one such format and exactly
its declared merged-rectangle inventory, with ordered, in-bounds coordinates.
It is insufficient merely to find no familiar ID marker in an unknown private
packet. Extra fields, unfamiliar format data, malformed merges and hidden
subclass extensions therefore reject synchronization while remaining available
under the existing storage contract. Local override values and merge rectangles
are not changed by remapping.

## Consumer discovery and atomicity

The map must be the actual object in its TABLESTYLE owner's known extension
slot. Consumer selection uses registered object identity, not a matching style
name or guessed map ID. All typed TABLECONTENT objects targeting that style are
included. Opaque TABLECONTENT, TABLETEMPLATE or TABLE entities conservatively
block discovery rather than being skipped. Modern inline/private TABLE subclass
schemas are not treated as the legacy two-subclass TABLE shape. A selected
TABLE and its recognized backing content must agree on TABLESTYLE identity.

All definition and mapping enumeration, including disposal, finishes before
source and target validation. Map edits share the existing reentry guard. A
coordinated edit attempted during a content transaction rejects and marks that
outer content transaction as reentered even when its callback catches the inner
exception. Invalid source IDs, missing replacement targets, unqualified late
consumers and invalid source graphs reject before any map or consumer is
published. A first valid consumer is not committed before the last is checked.

New map and content payloads are prepared and parsed as detached candidates.
Only after every candidate passes are their immutable snapshots exchanged. There
are no caller callbacks or resource registrations in that publication phase.
No handles are allocated. Non-ID content tags, values, formats, resource handles,
common metadata and object membership remain unchanged. Old map/content/value
snapshots stay immutable; value edits based on replaced content snapshots are
stale and cannot accidentally target the new packet.

As with other edit APIs, independent changes made by caller callbacks are not
rolled back. Thread-concurrent mutation, process failure and out-of-memory
recovery are not transactional guarantees. The low-level `ReplaceStructure`
method intentionally retains its stored-only behavior; applications must select
the coordinated API when they require this public consumer consistency.

## Verification

The recovered draft's initial 214 cases are retained. A further 70 cases exercise
nonzero IDs in every column/row/cell scope, stale scalar edits after remapping,
private formatted-data extensions, malformed merge counts/order/bounds, extended
local formats and TABLE/backing-style mismatches. These all-scope inputs are
explicit synthetic modifications of native-family packets, not new native
producer observations. The final CI result, not a declared inventory, is the
execution evidence recorded in PR #102.

The independent `verify_cell_style_consumers.py` builds a frame tree rather than
using production payload indices. It derives requested edits independently and
checks exactly 70 before/after pairs: 60 native-family operation pairs and ten
all-scope cycles. It compares all ordered physical records, including unselected
TABLE geometry, styles, map resources and content fields. It also checks that
every resulting positive consumer ID exists in the resulting map.

Individual map fields, selected IDs, content values/formatting, metadata and
resource links are corrupted in parsed outputs and must be rejected by the same
complete-record comparator. Every other physical record is independently
corrupted, and each participating record is removed as another negative control.
The exact fixture inventory, transport and source profile are checked; all
producer source hashes are verified. All before/after files undergo ezdxf audit
without repairs. Only the existing writer's verified exact-empty ACAD_LAYERSTATES
dictionary identity is normalized. HEADER time/seed fields and CLASS records
are outside that physical-record comparison.

```sh
DXF_TEST_FILTER=cell-style-consumers/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_cell_style_consumers.py artifacts/conformance
```

## Remaining boundaries

This closes qualified public style-ID synchronization, not complete visual
TABLE regeneration. Inherited formatting, duplicated classic TABLESTYLE row
settings, private ID references, modern inline TABLE schemas, display blocks,
layout and geometry are not rebuilt by this operation. General DXF audits and
passing packet tests are not a native AutoCAD open/AUDIT/save/reopen certificate.
The earlier [scalar format evaluator](value-format-evaluation.md) handles its
bounded documented modes; fields/formulas, dates, angles, private controls and
full native option/rounding qualification remain outside it. Full private/color
schema interpretation, recursive dependency import and general document-version
conversion also remain open. The historical PR95 coverage matrix is unchanged;
this guide supplements it with the narrower current operation and its evidence.
