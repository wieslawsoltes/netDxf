# Retained polyline registration and topology

Runtime checkpoint: `89c7cc80bb7b3cc43c665dc8ce63d9ea8a4a1001`.
Executable verification checkpoint: `fae312d67d41a06c7227866a8104d268726f50d1`.
Executable tree: `a6a6fa38cf27efc7a7a25d04f85b56968b92a64e`.
C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This extends the existing typed in-memory document. It does **not** add a typed
DXF reader/writer, or turn synthetic retained records into a file-round-trip test.
The original C# implementation, original test identities and shared fixtures are
unchanged. The previous published source tree was restored exactly; no surviving
uncommitted checkout or unpublished patch was found at the start of this work.

## Registration and stable child identities

Five original-path document partials now implement retained-chain registration,
removal checks and the document side of 3D vertex editing:

- DxfDocument.PolylineRecords and DxfDocument.PolylineTopology;
- DxfDocument.PolygonMeshRecords and DxfDocument.PolyfaceMeshRecords;
- DxfDocument.Polyline2DRecords.

A shared internal adapter connects the existing Polyline3D, PolygonMesh,
PolyfaceMesh and legacy Polyline2D metadata models to the actual AddedObjects
registry. It is not a second document graph. The parent is registered before its
children, and source-document/profile checks occur before adoption. Retained
layer/linetype resource slots are canonicalized to the document's registered
objects, including the source's special polyface face-layer handling.

Removal unregisters a chain without retiring each child. A supported
same-document move allocates a new parent identity but preserves existing child
and SEQEND handles. Child structural ownership stays with its parent. When the
retained owner convention refers to the block record, StoredOwner follows the
new block. A bound chain is not silently moved to a different source document.

The new registration hooks do not manufacture physical source identities. The
existing retained models still distinguish original source records from authored
records and enforce their own validation and unsupported-schema boundaries.

## 3D vertex editing

The already exposed InsertVertex, MoveVertex and RemoveVertexAt methods now have
their missing registered-document implementation. Insertion allocates exactly
one authored VERTEX, uses the current parent layer and SEQEND source version, and
preserves sibling and SEQEND identities. Its source-derived ten-tag record has
explicit identity, owner, resource and coordinate slots. Private metadata,
reactors and extension dictionaries are not copied onto a newly authored vertex.

Allocation accounts for handles held by retained metadata that is not indexed as
an independent graphical object, including INSERT attributes. The source's
signed handle range, per-record 4,096-tag budget and global 1,048,576-tag budget
remain enforced. The pre-existing 65,536-vertex admission bound remains intact.

Moving a vertex changes ordering, not its identity. Removing a retained vertex
first checks source registration, incoming dependencies and APPID bookkeeping.
Successful removal unregisters it, detaches its owner and marks it removed, but
keeps its old handle as a tombstone. A retained polyline must keep at least two
vertices. Public editing of a fresh non-retained Polyline3D still follows its
existing direct-geometry path.

## Removal and adoption guards

Removal checks retained child references, exposed handle tags, persistent/entity
reactors, XData 1005, dictionary defaults/entries, typed database references and
custom header handles. Child-to-parent raw XData is also checked inside the
removed group: raw text cannot follow a reassigned parent handle during a move.
Private retained payloads, private legacy/polyface headers and owned extension
payloads keep their explicit rejection boundaries.

Numeric handle comparison uses exact integer arithmetic. Arbitrarily many
leading zeroes are accepted without extending the UInt64 significant range;
whitespace, signs, prefixes and malformed or oversized values are not treated as
valid reference handles. Rejected topology operations leave geometry, handles
and registration unchanged in the tested rollback cases.

## Independent verification and its limits

The input-only corpus has **181 scenarios / 5,419 operations**. It covers all four
families, six source-version profiles, fresh and preserved child handles, both
owner conventions, resource maps, document binding, supported same-document
moves, rejected cross-document moves, private payloads, indices, nonfinite input,
allocation boundaries, tag budgets and incoming references. Twenty-four
randomized scenarios each perform 32 deterministic editing operations with state
observations after every edit.

C# and JavaScript have separate fixture adapters. The C# adapter invokes the
unchanged internal retained constructors; the JavaScript adapter constructs its
production equivalents. Both call actual registration and editing methods.
These synthetic internal-constructor tests qualify that behavior, **not typed
loading of external DXF files, typed writing or file-format preservation**.
No expected native results are embedded in the runtime.

The 22 new focused tests are supplemental, not additional original conformance
identities. The new category is mandatory in run-qualification and verify. All
181 browser inputs are appended after previous inputs; neither existing cases
nor failure checks are removed. Both browser modes now require at least
**141,012 comparisons**. The offline package smoke test uses only installed
barrel/standalone modules and explicitly identified internal fixture adapters.

From javascript/, select the pinned source and toolchain, then run:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:retained-polylines
npm run test:document-ownership
npm run test:registered-annotations
npm test
npm run test:unit
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

## Remaining parity work

The ledger is **366/510 library mirrors (144 missing)**, **56/193 original
conformance-file mirrors (137 missing)**, and **2,905/35,309 original cases
(32,404 missing)**. Five source-path mirrors were added; file presence is not a
complete member/signature or behavioral audit. Original-case coverage is
unchanged in this continuation.

Typed DXF reading/writing, Load/Save/SaveAtomic integration, general version
conversion, ACAD_TABLE and other missing specialized schemas, APIs, examples
and original tests remain unfinished. Retained registration does not remove
private payload or cross-document ownership restrictions. HTTP-origin and Debug
browser modes, broader standalone comparisons, filesystem guarantees and
performance acceptance were not rerun here; unavailable evidence is not green.

The inline browser's 87 failures are 64 entity, 12 concrete-dimension, six
classic-LEADER, four block and one coordinate scenario. They remain blocking.
No original source/fixture changes, tolerance, expected-failure waiver, merge,
force push or npm publication occurred. PR #98 stays a draft.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/RETAINED_POLYLINES.md). Current published scope is maintained in the [README](../README.md).
