# OBJECTS record dispatch, physical graph import and output

Runtime commit: `4de4df3fe1967660b74e36576fcbbad2d20b35cd`.
Executable qualification commit: `267d916132aa5921eb5e5032db02ee3656ef6a86`.
Executable tree: `0d5e8fc1a5d4408a1ade4b3f689ca8e7d5532db6`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This is working shared OBJECTS dispatch, relationship reconstruction and object
output, connecting the existing payload codecs. It is **not complete typed
DxfDocument.Load/Save/SaveAtomic**, all-section sequencing or native CAD evaluation.

## Preserved and published recovery

The actual starting remote was `2b8e345`, beyond the older PR description. It
already contained the recovered section runtime and verification. The remaining
three saved documentation files were recovered unchanged and published as
`389237e`, reproducing saved tree `ca663b8abd4fe93840e5165f281702764e4f0a00`.
Their historical account of an earlier blocked push does not describe current
GitHub access. No unavailable source patch is claimed to have survived.

Original C# sources, conformance tests and shared fixtures are unchanged. All
eighteen JavaScript workflow deletions remain preserved. The complete uploaded
runtime and verification trees match independent local Git indexes; reconstructed
local commit IDs differ from GitHub but their complete file trees match.

## APIs and implementation

`DxfReader.Objects.js` implements ReadDatabaseRecord,
ReadDictionaryDatabaseRecord, ReadXRecordDatabaseRecord, ApplyDatabaseMetadata
and ImportDatabaseObjects. The caller supplies a DatabaseIOContext whose chunk
reader observes the actual physical source records, plus the legacy table state
that native document loading constructs before object import.

Dispatch selects existing qualified public and private payload codecs, or retains
unsupported application data as an opaque object. Generic dictionary, dictionary
with default, dictionary variable, placeholder and XRECORD bodies are implemented
directly. XRECORD payload control-looking tags are not mistaken for object-header
metadata. Source identity is captured before advancing to the next record.

Import validates declarations, reserves source and exposed reference handles,
replaces the named root, preserves managed collection identities, assigns owners,
binds dictionary entries/defaults/extensions/reactors, and runs sixteen deferred
resolver phases in the original order. Child-first and randomized record orderings
work without inventing source acceptance for generated defaults. Repeated uses,
alias strength, opaque data and partial state after failure remain observable.

The reader's dictionary and physical-metadata maps are case-insensitive, matching
the actual main-reader initialization. The legacy DictionaryObject entry map is
separately case-sensitive. Three original legacy models are now available:
DictionaryObject, XRecord and XRecordEntry. Their mutable collections and boxed
value identity are not replaced with immutable registered-object types.

`DxfWriter.Objects.js` implements text preflight, common metadata, object envelopes,
payload dispatch, database-string encoding and CLASS preparation. Metadata uses
the canonical registered object when present. Extension output precedes ordered,
case-insensitively deduplicated reactors. Generated legacy root names and typed
entry names retain their distinct source escaping rules. FIELD source validation
runs even when binary transport bypasses text line-break checks.

The writer delegates recognized bodies to the existing original-path codecs.
Private opaque payloads are preserved rather than partially translated. CLASS
preparation retains compatible declarations and instance counts, and rejects
conflicts where the source does. Synchronous callbacks retain the completed output
prefix and source-defined property/encoding observation order.

Two new defects were exposed by independent tests and fixed: inherited JavaScript
property names such as `constructor` must not select a dispatch handler, and a
writer callback that changes the DXF version must affect subsequent outer string
encoding. Object.hasOwn checks and per-operation version reads preserve the native
behavior; expected results were not rewritten.

Five original source paths are added: the two Objects partials and three legacy
models. Selected MLEADERSTYLE and class-preparation helpers are **not counted as
complete MultiLeader, Section or StoredTable partial mirrors**. MLEADERSTYLE
references are queued here; the native later main-reader resolution phase remains
outside this object's import routine and is not claimed completed by these helpers.

## Independent verification and its blocking Debug case

The input-only corpus contains **937 scenarios / 8,715 requested operations**.
It covers text, modern binary and legacy binary; six source profiles; malformed,
private and duplicate records; source-resource admission; ownership and aliases;
common metadata; CLASS declarations; callbacks; enum boundaries; generated root
names; and 32 deterministic randomized record orderings. Native C# private methods
and production JavaScript run independently on identical input.

Forty new supplemental tests include actual text/binary/legacy graph roundtrips,
XData, shared aliases, repeated references, extension trees, callback prefixes and
source-identity rejection. No original typed-file case is shortened to claim more
original coverage. All old comparison inputs are preserved.

Release observes and matches every scenario. Debug matches all **936 fully observed
scenarios / 8,708 operations**, but native TextCodeValueWriter asserts on a null
entry in the automatic-reactor list. The scenario is retained as unavailable
native evidence, not a successful or skipped comparison:

```text
object-graph-io/metadata/automatic/["C0",null]
Process terminated. Assertion failed. Incorrect value type.
netDxf.IO.TextCodeValueWriter.Write(Int16 code, Object value)
netDxf.IO.DxfWriter.WriteDatabaseMetadata(...)
```

JavaScript is not made to abort its host process to imitate a native Debug.Assert.
The Debug differential and focused browser therefore remain failing. Their original
reports include the request and native abort stack. No result is synthesized for
the missing seven operations, and no exception allowlist or tolerance is added.

## Completed local results at 267d916

September 24, 2026; Debian 13 x64, Node 22.16.0, SDK 8.0.425 / .NET 8.0.31,
Chromium 144.0.7559.96. The [receipt](object-graph-io-local-267d916.json) retains
actual new native reports, report hashes and summaries, and original-suite metadata.
Full result arrays and browser/native failure details are in the validation archive.

| Check | Observed result |
| --- | --- |
| OBJECTS Release differential | 937 scenarios / 8,715 operations; zero differences |
| OBJECTS Debug differential | 936 observed scenarios / 8,708 operations match; one native assertion prevents complete observation |
| Focused Release Chromium | All 937 comparisons match; no page errors |
| Focused Debug Chromium | 936 observed scenarios match; the same one unavailable native observation remains blocking; no page errors |
| Five prior regression corpora, both configurations | Retained records 2,593/5,909; database payloads 1,070/4,701; source metadata 420/12,322; document ownership 188/8,130; stored dependencies 738/5,566; all pass |
| Unchanged original C# suites | 35,309 pass in each configuration |
| Complete mirrored original JavaScript subset | 3,619 pass; zero duplicate or unexpected original names |
| Supplemental JavaScript tests | 1,410 pass; no failures, skips or TODOs |
| Offline-installed package | Pass; 623 files |
| Foundation, dimension and GTE regeneration | All three exact checks pass |
| Full-parity verifiers | Both fail; incomplete coverage, unavailable observations and unrun/stale categories remain blocking |

Focused Chromium executes 628 native ESM modules and is explicitly marked
`scope: object-graph-io-only`, `fullSuite: false`. It cannot satisfy the complete
160,187-check browser gate. Both focused-browser and native OBJECTS categories
are mandatory, including the failing Debug evidence.

Several combined orchestration attempts hit execution limits. Complete isolated
reruns supplied the original JavaScript suite, all five regression categories,
and both native original suites. One interrupted Debug build left an abandoned
writer lease; its owner and all Debug readers/compiler processes were checked
before removal, then the complete native Debug suite was rerun. Initial logs and
failed orchestration statuses are retained separately, not relabeled as passes.

Runtime fingerprint:
`2853dc6e5584de449eee0fb1715bb0a50fa473caaec69bd62c944eddfd062276`.
Verifier fingerprint:
`be77fc471eb934526e0e63c3e9547dd594792f495ce2e3ca222dfd6043b07ac5`.
Source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

From javascript/, with the pinned source/toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug; the retained assertion fails.
node tools/dotnet.mjs geometry
npm run test:object-graph-io
npm run test:object-graph-browser
npm test
npm run test:unit
npm run test:package
npm run verify:complete
```

## Remaining scope

**467/510 library paths (43 missing), 66/193 original test files (127 missing),
3,619/35,309 original cases (31,690 missing).** File presence is not exhaustive
API, signature or behavioral qualification.

Complete typed document dispatch, all-section sequencing and resource reconstruction,
Load/Save/SaveAtomic, remaining private/TABLE/evaluator I/O, general version
conversion, missing original tests/examples and broad platform/performance
acceptance remain unfinished. The full 58-stage aggregate, full browser suites,
hosted/Windows CI, HTTP-origin, MPFR and performance were not rerun here.
Historical numerical and native assertion/recursion/globalization failures remain
unqualified; this increment neither waives nor claims to fix them. No native
AutoCAD open/AUDIT/save/reopen fidelity is claimed. PR #98 stays draft; no merge,
force push, original-source/fixture change or npm publication occurred.
