# Explicit stored-data editing, retained POLYFACE records and opaque entities

PR #94 extends the merged `173e19efc38bfe6549fd385501d151f1c71b2815`
baseline. Its qualified implementation is
`1631547bfae37dccbea900970ceaaad61c868020`, with tree
`aacbe71c64d3a2fc1998396aaae4877b617d7589`. Exact source, executable,
result and downloaded archive hashes are recorded in the
[integration receipt](editing-opaque-qualification.json).

Debug and Release each passed **33,296 unique cases with zero failures**;
their result JSON files are byte-identical. Each configuration passed **all
121 independent verifiers** against its own **6,451 generated DXF files**.
These inventories include intentionally malformed inputs and intermediate
artifacts. Each gate defines its accepted outputs and rejection controls;
the file count is not a claim that every generated input is valid.

| Increment | Qualified behavior | Net new cases |
|---|---|---:|
| [TABLEGEOMETRY replacement](table-geometry-editing.md) | Explicit replacement of loaded dimensions and ordered cell/content packets, with actual source references and atomic snapshot updates | 125 |
| [SECTION_MANAGER lifecycle](section-manager-lifecycle.md) | Creation and erasure through the actual root dictionary, ordered repeated SECTION membership and pre-allocation validation | 128 |
| [Retained POLYFACE records](polyface-records.md) | Actual VERTEX/SEQEND identities, metadata, interleaved physical order and signed face slots | 568 |
| [Explicit HATCH patterns](hatch-pattern-affine.md) | Affine transformation of qualified predefined/custom line families and independently stored pattern origin | 221 |
| [TABLECONTENT replacement](table-content-editing.md) | Snapshot-bound same-kind scalar edits and qualified header/style changes, preserving unselected native packets | 165 |
| [Standalone opaque entities](opaque-entities.md) | Complete eligible source packets, scoped common edits, exact registered references and CLASS guards | 528 |
| [POLYFACE version diagnostics](version-compatibility.md) | Source-profile diagnostics for retained coordinate, face and sequence-end records | 24 |
| [Retained child removal guards](receipts/retained-record-target-removal/qualification.json) | Actual entity and containing-block targets remain protected by current child reactors and XData references | 336 |
| [Tenth mixed graph](tenth-mixed.md) | Native table backing and declared neighboring records exercise editing, affine geometry and ordered reference release together | 16 |
| [Collection insertion](observable-insert.md) | Add-only insertion callbacks, insertion at Count and unchanged existing HATCH source occurrences | 49 |
| [Opaque HATCH source lifecycle](opaque-hatch-release-qualification.json) | Qualified common backlink release, shared occurrences, prevalidated refusal and unchanged private dependencies | 360 |
| [TABLESTYLE source fixtures](pr94-source-fixture-admission.md) | Actual valid opaque source references, exact admission negatives and cross-transport identity/removal guards | 4 |

The total increases by **2,524 cases** over PR #93. All four Linux/Windows
Debug/Release jobs passed in
[implementation run 35001265150](https://github.com/wieslawsoltes/netDxf/actions/runs/35001265150).
Every job passed all 33,296 cases; Linux Release also passed all 121 independent
gates. The downloaded Linux Debug archive matches all **2,503 source files** in
the pinned tree. Both Linux archives match their published SHA-256 digests and
contain complete passing result sets.

The first combined run passed all 2,111 then-new cases but exposed obsolete
unknown-entity fixture assumptions: Debug reported 148 failures and Release 28.
The old TABLESTYLE gate also expected four files from malformed inputs that now
reject during opaque admission. Those inputs remain precise negative tests;
separate valid opaque carriers now prove actual source identity and protect the
referenced entity from removal. Generated-identity tests still reach their
intended late-binding checks, and DATATABLE binary controls now use binary
transport. The [initial results](receipts/pr94-initial-integration) and
[fixture correction receipt](pr94-source-fixture-admission-qualification.json)
preserve the evidence.

Integration also revealed two real lifecycle defects. Collection insertion had
fired removal callbacks without removing the existing collection item, corrupting
HATCH source occurrence bookkeeping. The unchanged regression binary passes
49/49 after the correction versus 3/49 before it. Retained child metadata had
failed to protect ordinary removal targets; its unchanged regression binary
passes 336/336 after the correction versus 0/336 before it.

Opaque HATCH sources need a controlled release path because their original
common metadata was pinned. Boundary operations now validate the entire affected
HATCH before mutation, retain shared occurrences until final release, and omit
only the qualified released common reactor entries from output. Immutable
`SourceTags`, unrelated private standard pointers, XData references and unknown
application groups remain preserved. Direct dirty reactor edits reject before
mutation; new associations to an opaque source and regeneration from opaque geometry reject.
The unchanged independent harness passes **80/80 in each final configuration**
versus **12/80 before**, with all 160 final independent outputs auditing cleanly.
This qualification does not interpret the unknown source's geometry.

All five Release library targets and Debug netstandard2.0/net8.0 compile with zero
errors and the existing 561 CS1591 warnings per library target. Conformance builds
have zero warnings. The unchanged field-audit tool passes its self-test and
indexes 113 IO files, 746 methods and 268 model/header files. All 18 coverage-ledger
tests pass. The comparison contains **291 scoped rows across nine profiles**;
the two qualified native mixed-graph profile cells are now promoted with their
independent packet and world-space evidence. These counts do not measure a
percentage of DXF completeness.

TABLE layout/evaluation and complete backing authoring, automatic synchronization
or section generation, unknown private geometry/dependencies, proxy entities,
historical typed dialects and complete version conversion remain separate work.
Periodic HATCH evaluation, retained legacy 2D POLYLINE records and explicit
TABLESTYLE/CELLSTYLEMAP editing are proceeding in the following increment.
The version report lists known failures and losses; an empty report is not a
compatibility certificate. Native AutoCAD execution and full-standard completion
are not claimed.
