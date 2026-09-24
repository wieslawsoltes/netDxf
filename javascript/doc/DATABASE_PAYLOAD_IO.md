# Database payload and physical-source I/O

Runtime commit: `57ae692059501eb6345fb149171ad07428f148fe`.
Final executable commit: `f1777d4a7cd3714c0ee22b90babb57966d3130c6`.
Executable tree: `68e734818589ca1b659559bbab030080aad84aba`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This increment implements twelve original-path reader/writer partials for
IDBUFFER, SORTENTSTABLE, SPATIAL_FILTER, DATATABLE, LIGHTLIST, LAYER_FILTER,
OBJECT_PTR and LAYER_INDEX, plus physical-source identity and common metadata
observation. These are working payload operations and deferred-reference helpers,
**not complete DxfDocument.Load/Save/SaveAtomic or whole-file reconstruction**.

## Recovery and adapters

The supplied recovery archives reproduced published `0cc4e32` tree
`1294a13097e71f608e67683542ee10d58119e338` exactly. No surviving unpublished
checkout was found. All previously published source, tests and fixtures were
preserved before implementation. Synthetic local commit identities differ from
GitHub; complete runtime and executable trees were compared before publication.

Private C# methods are exposed as explicit JavaScript functions through
`DxfTransport` and `runtime/DatabasePayloadIO.js`. DatabaseIOContext carries the
actual document, accepted physical identities and deferred reference lists;
DatabaseRecord carries a parsed object and its pending header relationships.
The helper adapting ReadDatabaseXData is not counted as a complete mirror of
its original main reader source. All twelve dedicated original paths are counted
as present, not as proof of exhaustive public-member or whole-file parity.

The eighteen JavaScript workflows removed by `b7a572e` remain removed. Original
C# library sources, original tests and shared fixtures are unchanged. No native
DXF engine, .NET runtime or WebAssembly is loaded by the production port.

## Payload grammar and retained private data

IDBUFFER preserves repeated references, explicit null slots and completed binding
before a later resolution failure. SORTENTSTABLE resolves its actual BlockRecord
and graphical entities, retaining redraw keys as values rather than pointers.
Collection uniqueness and block ownership validation use the existing models.
SPATIAL_FILTER validates boundary counts, two-dimensional coordinates, complete
vectors, Boolean fields, optional clip distances and two affine matrices. Writing
preserves field order, negative zero and the completed prefix when a callback fails.

DATATABLE supports all eleven modeled cell types: integers, doubles, strings,
points, vectors, Booleans and five reference/ownership forms. Dimension and total
cell bounds are checked before column allocation. Public fields require the
source order and value grammar. Unsupported versions, cell types and private
extensions retain the entire payload as opaque data, including unbound XData;
malformed recognized public data remains an error, not an opaque-success fallback.
References bind later against accepted source identities. Loaded columns commit
through the actual model's ownership validation; failing one table does not
silently roll back earlier completed tables. Output retains typed groups,
column snapshots, text escaping, reference order and null-handle conventions.

LIGHTLIST retains signed stored versions, ordered LIGHT/name pairs, repeated
references and source failure timing. Its native resolver deliberately uses
registered document identity rather than the stricter accepted-source lookup;
this source distinction is retained, not homogenized. LAYER_FILTER preserves
repeated names and one-pass decoded strings. An empty public OBJECT_PTR remains
distinct from an object carrying private extensions.

LAYER_INDEX preserves timestamp/subclass framing, declared IDBUFFER counts and
reciprocal owned-buffer identity. The undocumented leading group-90 variant
remains opaque instead of receiving an invented interpretation. Output reads the
current buffer counts. CLASS preparation updates compatible declarations and
counts opaque instances without overwriting conflicting private declarations;
a live typed conflict remains an error.

Shared stored headers separate identity, owner, extension dictionaries and
persistent reactors. Unknown header material is retained. Long private headers
are copied iteratively rather than through a spread call that could overflow
the JavaScript argument stack. Tests include 70,000 private header tags.

## Physical-source identity and metadata

The metadata observer recognizes actual record identities only in qualified
TABLES, BLOCKS, ENTITIES and OBJECTS scopes. Generated defaults cannot stand in
for physical source records. DIMSTYLE group 105 is distinguished from ordinary
group 5. Duplicate physical declarations mark earlier retained SourceRecord
objects ambiguous retroactively, so an earlier accepted reference becomes invalid.
Discarded records, absent acceptance proof and same-handle replacement objects
cannot satisfy source-bound lookup.

Nested common control groups track extension and reactor metadata at the native
scope. Once a subclass or XData begins, payload groups are not reinterpreted as
common headers. In particular, an XRECORD payload containing control-group-looking
tags does not create synthetic common object relationships.

Independent comparisons exposed missing UInt64 trailing-NUL behavior in the
new source lookup adapter. Leading zeroes and trailing NUL padding now follow
the unchanged native parser; overflow and invalid lexical forms remain rejected.
This does **not** broaden the raw/code-value DXF handle grammar, whose existing
validation remains unchanged. The source's exact string `"0"` null sentinel is
not replaced everywhere by numeric canonicalization.

The new metadata comparisons also exposed generic primitive cast diagnostics in
the existing text and binary readers. Both now report the native CLR source and
destination type names, for example:

```text
Unable to cast object of type 'System.Int16' to type 'System.String'.
```

The previous reader state remains intact after a failed cast. Messages are
compared directly against the pinned native profile, not normalized or waived.
This does not claim all CLR type-system, localized-message or stream behavior.

## Tests and source-bound evidence

The input-only corpora contain **1,490 scenarios / 17,023 operations**:
**1,070 payload scenarios / 4,701 operations** and **420 physical-metadata
scenarios / 12,322 operations**. They cover six profiles, text/modern/legacy binary,
public/private boundaries, malformed counts, all cell types, Unicode, XData,
reference/ownership failures, CLASS conflicts, callback failures and 80
randomized DATATABLE inputs.

The C# harness invokes unchanged native private methods; the JavaScript harness
invokes production adapters. Payload fixtures explicitly supply source-acceptance
state and do not masquerade as complete typed file admission. Separate metadata
fixtures use real code/value readers and the actual physical record observer.
Snapshots compare exact emitted bytes, models, pending references, source
identity state, registry side effects and errors. Incomplete observations fail.

**52 supplemental tests** were added. Original tests requiring unfinished typed
Load/Save were not shortened, so original-case coverage remains 3,619. Both new
stages are mandatory in aggregate verification and installed-package checks.
All 1,490 new browser inputs are appended after prior inputs. The focused browser
report is marked `scope: database-io-only`, `fullSuite: false` and cannot satisfy
the separate complete browser gates.

### Completed local checks at f1777d4

September 24, 2026; Debian 13 x64, SDK 8.0.425, .NET 8.0.31, Node 22.16.0,
Chromium 144.0.7559.96. The [receipt](database-io-local-f1777d4.json) contains
eight actual new/browser/unit/package reports, hashes and statistics for eight
regression reports, and original-suite metadata and hashes. Complete reports,
original result arrays and logs are included in the validation archive.

| Check | Actual result |
| --- | --- |
| Database payloads, Debug and Release | Each: 1,070 scenarios / 4,701 operations; zero differences or unavailable observations |
| Physical-source metadata, both configurations | Each: 420 scenarios / 12,322 operations; zero differences or unavailable observations |
| Focused real Chromium, both configurations | Each: all 1,490 scenario digests match; 588 native ESM modules; zero page errors |
| Existing reader comparisons, both configurations | 913 scenarios; 27,407 executed commands and 35 constructor rejections per run; zero differences |
| Previous primitive, entity-body and section comparisons | All three complete corpora pass in both configurations |
| Full unchanged original C# suite | 35,309 passed in each configuration |
| Full mirrored original JavaScript subset | 3,619 passed; no duplicate or unexpected identities |
| Supplemental suite | 1,276 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; 588 files |
| Foundation, dimension and GTE regeneration | All three exact checks pass |
| Full-parity verifier | Both configurations fail on incomplete coverage and missing/stale evidence |

Initial local command attempts interrupted before finishing the original JS or
combined regression runs. All affected checks were rerun completely; only the
completed final reports provide passing evidence. Initial logs remain separate.
The reader corpus requests 27,962 commands; commands unreachable after constructor
rejections are not counted as executed comparisons.

Runtime fingerprint: `0b850a3aefe1037d55e8e60da58b23e7348ccefd84146e8012e515760438c9bb`.
Verifier fingerprint: `0d087a60bf044ced6a4b6b2d06530e718b7e68d0f86a8b961bd445c674567efc`.
Source fingerprint: `97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

With the pinned toolchain and source selected, run from javascript/:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:database-payload
npm run test:source-metadata
npm run test:database-browser
npm test
npm run test:unit
npm run test:package
npm run verify:complete
```

## Remaining parity work

Current coverage: **434/510 library mirrors (76 missing)**, **66/193 original
conformance-file mirrors (127 missing)** and **3,619/35,309 original cases
(31,690 missing)**. Presence does not establish complete APIs or behavior.

Complete typed document dispatch, common envelopes and relationship reconstruction,
Load/Save/SaveAtomic, remaining private/TABLE/evaluator I/O, general version
conversion and broad numerical/platform/performance acceptance remain unfinished.
The complete **55-stage aggregate** and full **154,897-check browser suites**
were not rerun at this checkpoint. Hosted/Windows CI, HTTP-origin, MPFR and
performance checks were not run. Older numerical, assertion/recursion and
globalization failures are neither waived nor claimed fixed by these results.
No AutoCAD open/AUDIT/save/reopen fidelity is claimed.

No original C#/fixture edits, restored workflows, comparison removal, tolerance,
expected-failure waiver, merge, force push or npm publication occurred. PR #98
remains draft.
