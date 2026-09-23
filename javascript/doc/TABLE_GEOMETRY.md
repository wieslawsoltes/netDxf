# Retained TABLEGEOMETRY and owner-held dependencies

Runtime checkpoint: `11351a527cc3e296608a6f505b056cb2379137e3`.
Executable verification checkpoint: `198d9e4a8706dfa5e0f42ffad6133f578b4ac53c`.
Executable tree: `1f4a663e6c961967c0d01382b23b1a1068f77a2e`.
C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

The preceding published `e7d3b6e` file tree was restored exactly, including
ignored tracked fixtures. No surviving uncommitted project checkout or unpublished
patch was found. The implementation below is new work on that verified tree.
Original C# sources, original tests and shared DXF fixtures remain unchanged.

## Values, framing and stored identities

Two original-path source mirrors implement DxfStoredTableGeometry and its editing
partial, including DxfStoredTableGeometryCell and DxfStoredTableCellGeometry.
Their exports are available through the package barrel, Node entry and standalone
module paths. The shared WeakMap state connects the partials without maintaining
a second document graph.

Content geometry preserves both three-dimensional points, content width/height,
cell width/height and the stored signed group-95 integer. Cell packets preserve
flags, width/height with gaps, a reference identity and ordered content geometry.
Public numeric constructors reject nonfinite values but retain finite negative
values and signed zero. Vector arguments and getters have independent value-copy
semantics. Containers and public scalar values are immutable snapshots.

The retained packet parser checks the exact ordered AcDbTableGeometry subclass,
row count, column count, cell count and each cell/content group sequence. Counts
are bounded and checked against available tags; reordered, missing, extra or
nonfinite geometry data is rejected. Row/column counts remain separate from the
number of stored cells. The implementation does not infer cell addresses or
pretend to regenerate an application-defined grid from those counts.

Resolve retains the original source document, profile, owner and ancestry.
Reference spelling, padding and case remain available separately from the actual
referenced objects. Ordered repeated references are not collapsed. Registered
STYLE resources and owner-held ATTRIB, ENDBLK and layout VIEWPORT identities use
the document's existing stored-handle lookup.

**Construction of retained geometry and Resolve are explicit adapters for internal
C# reader APIs. They are not a new public authoring factory in the original API,
and do not provide typed DXF loading or qualify reader admission.** The independent
fixtures invoke the real C# internal constructor and compare the same supplied
packets, rather than substituting fixture bytes for an unimplemented reader.

## ReplaceGeometry

ReplaceGeometry accepts row/column counts and an enumerable of immutable cells.
It consumes and disposes the caller's enumerator before validating the live source
state and committing. Null packets, size excesses, iterator failures, disposal
failures and source/resource mutations retain their source exception behavior.
Caught recursive replacement still invalidates the outer edit. A later independent
operation can proceed after the failed edit unwinds.

All checks precede the geometry-state swap. A valid replacement creates a new
payload, cell snapshot and reference view without allocating new object handles.
Old containers retain their previous values; referenced document objects remain
live identities. A bit-identical no-op preserves payload/cell snapshot identity.
Changing positive zero to negative zero is not an approximate no-op.

When an existing retained cell still names the same numeric handle, replacement
keeps that handle's source spelling. A newly supplied cell names its actual
registered target. Detached identities, foreign-document resources and same-name
substitutes do not satisfy the identity check. Caller mutations are revalidated,
not silently undone or treated as part of a rollback transaction.

The maximum record size is 1,048,576 tags. Admission accounts for the subclass
header, five cell tags, eleven tags per content packet, object identity/owner,
extension dictionary, distinct persistent reactors, APPID prefixes and split
binary XData chunks. Bounding the geometry payload alone cannot waive metadata
cost. Generic cloning and generic erasure remain rejected until the complete
application-specific lifecycle is available.

The following operates on a source-bound retained object already resolved and
registered by the loader adapter; it does not imply typed Load is implemented:

```js
import {
  DxfStoredTableGeometryCell, DxfStoredTableCellGeometry, Vector3,
} from '@netdxf/javascript';

function replaceRetainedGeometry(geometry, registeredTextStyle) {
  const content = new DxfStoredTableCellGeometry(
    new Vector3(-0, 1, 2), Vector3.Zero, 10, 4, 12, 6, 0,
  );
  const cell = new DxfStoredTableGeometryCell(
    0, 12, 6, registeredTextStyle, [content],
  );
  geometry.ReplaceGeometry(1, 1, [cell]);
}
```

## Corrected removal guards

Independent owner-held reference scenarios exposed an existing document bug:
StoredTableReferencesRemoval consulted DatabaseReferences, whose projection
intentionally excludes identities outside the top-level registry. A retained
TABLEGEOMETRY reference to an INSERT attribute or a layout's internal viewport
could therefore fail to prevent removal of its owning entity/layout.

The document now inspects the complete retained References projection. Where a
known stored schema is still opaque, exposed reference tags are resolved through
the same stored-handle lookup before removal checks. The fix does not register
owner-held objects twice or relax database validation. Tests retain actual failed
pre-fix observations, and compare the corrected behavior against unchanged C#.
Removing an INSERT or paper-space layout is rejected until its retained geometry
dependency is explicitly released. The pre-existing retained-polyline, annotation,
manager, table-style and document corpora remain unchanged and passing.

## Independent tests and required integration

The input-only geometry corpus contains **270 scenarios / 3,966 operations**.
It covers finite and nonfinite constructor slots, signed zero, exact limits,
strict framing, source versions, snapshots/no-ops, repeated and lexical references,
foreign/detached resources, owner-held dependencies, every enumerator phase,
re-entrancy, caller mutations and 32 deterministic randomized edit sequences.

**13 complete original EditableTableGeometryTests cases** are now mirrored:
twelve nonfinite-value cases and the complete immutable-value snapshot case.
Original serialization and independent-fixture cases were not shortened to omit
missing typed IO. The separate **24 supplemental tests** do not inflate original
case coverage. All 2,920 mirrored original identities were checked against the
35,309-case unchanged C# result set; none is unexpected or duplicated.

The geometry category is mandatory in run-qualification.mjs and verify.mjs.
Every new browser input is appended after all prior corpora, with the minimum
raised to 141,735 digest checks. Installed-package tests use only package exports,
including the standalone module. The existing workflow adds the new corpus and
tests without removing earlier stages or the optional Windows replacement-host
build needed by the full mirrored suite.

### Completed local evidence

The following describes the exact executable `198d9e4` tree on Debian 13 x64,
Node 22.16.0, SDK 8.0.425 / .NET 8.0.31, September 23, 2026:

| Check | Observed result |
| --- | --- |
| Geometry differential, Debug and Release | Each: 270 scenarios / 3,966 operations; zero mismatches or unavailable native observations |
| Five earlier lifecycle corpora, both configurations | All pass: table styles 279/5,829; manager 174/5,406; retained polylines 181/5,419; ownership 188/8,130; annotations 112/6,633 |
| Full unchanged C# conformance | Each configuration: 35,309 passed, zero failed |
| Full mirrored original JavaScript conformance | 2,920 passed; no unexpected original identities |
| Supplemental JavaScript suite | 994 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; 512 files |
| Source-derived foundations and dimensions | Both exact regeneration checks pass |
| Complete differential refresh | All 44 stages executed in each configuration; Debug 39 passed / 5 failed; Release 38 passed / 6 failed; no stage timeout |
| Release inline Chromium 144.0.7559.96 | All 141,735 digest checks executed; no new geometry mismatches, page errors or unavailable native observations; 83 previous-category failures remain |

The first combined local unit/conformance command was interrupted before the
original suite completed. The complete successful original-suite rerun is separate
evidence; no interrupted attempt is counted as a passing full run.

Debug inline Chromium also executed all 141,735 digest checks with no page errors
or new TABLEGEOMETRY mismatches. It retains 31 failing entries: 22 unavailable
native observations from source assertions, plus eight concrete-dimension digest
differences and one cubic Bezier comparison. The unavailable observations are
reported distinctly; their digest checks are not passing native comparisons.

HTTP-origin navigation was attempted separately and failed before test execution
with ERR_BLOCKED_BY_ADMINISTRATOR. Neither an input count nor the completed
inline executions is used as evidence of HTTP-origin qualification. Both full-port
verification commands returned failure and retained all missing or failed
categories, including unavailable high-precision and benchmark evidence.

### Completed hosted evidence

[Run 35831919148](https://github.com/wieslawsoltes/netDxf/actions/runs/35831919148)
at `198d9e4` passed all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs.
Each job executed six lifecycle corpora (**1,204 scenarios / 35,383 operations**),
**168 focused supplemental tests**, and **all 2,920 mirrored original cases**.
All four ZIP archives were downloaded, checked against GitHub SHA-256 digests and
inspected; all 24 differential reports show zero failures and complete native
observations.

The [retained hosted receipt](table-geometry-hosted-198d9e4.json) contains those
actual result documents plus original-suite metadata, counts and file/archive
hashes. Complete original-result arrays remain in the hash-identified archives
and the downloadable validation bundle. The full mirrored suite is not all
35,309 original C# cases, and these focused jobs do not qualify the broad failing
JavaScript workflow.

Runtime fingerprint:
`016b889e41c75f7863c1d17aa3a2b3b790ad23f7fceb2c7109275fa9370cd686`.
POSIX verifier:
`8d1fcb4fae05e63318558231c2f7e71e5990031f8e8990c3cbebcccd8baa8c0e`.
Windows verifier:
`1c23930faab982c8fa4703ecd0f9d1dd26a53df992248adcfcd7ac9da1bbf8e8`.
Source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

The verifier's pre-existing host-path sorting explains the platform-specific
verification hashes. Both were reproduced independently from identical executable
file bytes; actual reports were retained without rewriting those values.

From javascript/, with the pinned source checkout/toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:table-geometry
npm test
npm run test:unit
npm run test:differential
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

## Remaining failures and missing parity

The complete differential refresh retains Release differences in concrete
dimensions (26 operations / 12 scenarios), LEADER (6 / 2), blocks (8 / 4),
coordinates (3 / 1) and entities (128 / 64). Debug retains one exact cubic Bezier
tangent NaN-sign comparison and 22 unavailable native scenarios caused by original
assertions: six dimensions, twelve tolerances and four layout/viewports.

Both casing stages reject the Debian native globalization profile before pairwise
comparison. Their precomputed comparison count is not an executed comparison
count. The required Ubuntu-profile casing tables were not regenerated or relaxed.
Raw transport, handles/objects, filesystem, foundations and exact randomized
geometry pass in both final configurations, without establishing typed IO or
all-platform guarantees. No numerical implementation or comparison tolerance was
changed in this continuation.

The Release browser's 83 failures comprise 64 entity, 12 concrete-dimension, four
block, two classic-LEADER and one coordinate scenario. None belongs to the added
TABLEGEOMETRY corpus. Other incomplete high-precision, benchmark/performance and
platform evidence is not counted as passing.

Current ledger: **377/510 library mirrors (133 missing), 59/193 conformance-file
mirrors (134 missing), and 2,920/35,309 original cases (32,389 missing)**. File
presence is not exhaustive API/signature or behavioral equivalence.

Complete typed DXF reading/writing, Load/Save/SaveAtomic integration, profile
conversion, ACAD_TABLE/TABLECONTENT and other stored/private schemas, remaining
APIs, original tests/examples and broad numerical/filesystem/performance acceptance
remain unfinished. This geometry editor does not regenerate native TABLE layout,
interpret all private geometry, or establish AutoCAD open/AUDIT/save/reopen
fidelity. The separate raw API is not a replacement for typed transport.

Both full-port verification gates remain failing. PR #98 stays a draft and the
package stays private. No original C#/fixture edit, removed comparison, failure
allowlist, merge, force push or npm publication was performed.
