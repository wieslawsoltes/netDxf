# Primitive geometry, SPLINE and HELIX transport

Runtime commit: `7df333701e62d8bc821b8be6d134e74dee82a6d2`.
Final executable commit: `02017a3a46b16a4294401c22d8c0267cfdf5ffee`.
Executable tree: `04e86767e9ddb5e1b98b23ae8f73817e882e060f`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This increment implements actual entity-body readers and writers for twelve
families: ARC, CIRCLE, ELLIPSE, LINE, POINT, RAY, XLINE, 3DFACE, SOLID, TRACE,
SPLINE and HELIX. It does **not** complete typed document Load/Save/SaveAtomic,
common entity envelopes, dispatch, resource reconstruction or native CAD fidelity.

## Recovery and source accounting

The initial local files were recovery archives, not an unpublished checkout.
Their tracked source was restored, workflow removals preserved, and the current
README fetched from GitHub. The complete restored tree matched published
`60ff979` tree `274c2a1eea759e155c0dcdeb420f25934278fad4` exactly.
Previously recovered OLE/LIGHT/ACIS/LWPOLYLINE codecs remain intact. Local synthetic
commit identities differ from remote commits; full file-tree equality is checked
before each non-forced publication.

`runtime/PrimitiveEntityIO.js` and `runtime/SplineIO.js` adapt selected methods
from the pinned `netDxf/IO/DxfReader.cs` and `DxfWriter.cs`. They are **not counted
as complete mirrors of those large main source files**. `netDxf/IO/DxfHelix.js`
implements the dedicated HELIX partial, including its validation and CLASS helper.
Only that dedicated original path adds to the source-presence ledger.

The source, original conformance tests and shared fixtures are unchanged.
The eighteen JavaScript workflows removed by `b7a572e` stay removed. This
continuation does not recreate CI or claim hosted results.

## Primitive geometry behavior

The browser-safe DxfTransport namespace exposes Read/Write pairs for all ten
primitives. ReadFace3d preserves the native method spelling; ReadFace3D is an
explicit JavaScript alias. Readers accept a code/value reader positioned at the
subclass marker and a destination document for XData registry binding. Writers
accept the code/value writer, version and entity. Bodies omit the common entity
envelope and are not complete file records.

Per-call state preserves source defaults, repeated fields, advancement and final
record position. ARC/CIRCLE coordinates use the original object/world transforms.
ELLIPSE preserves the source major-axis vector and start/end parameter conversion
through the existing native-math adapters. SOLID/TRACE retain first-vertex elevation
instead of introducing a new planarity rule. POINT retains the source rotation
convention. Invalid geometric properties fail at the original assignment step;
XData registry additions before that failure are not silently rolled back.

Output retains field ordering, scalar bits and negative zero where the native
operations retain them. Writer callbacks are observable: LINE components read
updated model properties after a synchronous callback, whereas the transformed
CIRCLE center remains the source's local snapshot. Callback failures preserve
only completed output. No expected native values or correction tables are embedded.

## SPLINE and inherited HELIX data

SPLINE reads degree, flags, knots, control/fit points, weights, nullable tangents,
tolerances and XData. The original cosmetic count fields do not cause allocations.
Partial triples finish when their Z field arrives. The source degree clamp,
nonpositive tolerance defaults, weight-count fallback and parameterization priority
remain intact. An ordinary SPLINE ignores the normal as the native reader does;
a HELIX inherits it through its spline portion.

Periodic input requires the exact degree-fold control and weight overlap used by
the source. The leading overlap is compacted without refitting, resampling or
numerical tolerance. Writing restores the periodic prefix, retains the knot
sequence and honors optional XData emission. HELIX emits inherited XData once.

The expanded callback corpus exposed two nullable-tangent failures: clearing a
tangent during output initially produced JavaScript TypeError instead of the
native InvalidOperationException. Every subsequent component access now preserves
the native nullable-value error and the completed output prefix. Additional
malformed periodic-degree cases exposed List.RemoveRange validation occurring
before constructor degree validation; the source's `count` parameter is retained.
These cases remain in the corpus and supplemental regressions.

HELIX validates required subclasses, duplicate fields, complete vectors, wrapped
property errors and its source version restriction. PrepareHelixClass counts
entities in all block definitions, including inactive layouts and unused blocks;
multiple INSERTs do not multiply that count. Existing compatible CLASS identity
and metadata are retained while counts update. A conflicting live HELIX class
fails instead of being silently overwritten.

The original Debug orphan-XData assertions in the primitive/SPLINE default
branches have no JavaScript process-abort equivalent. Those branches retain
Release advancement and are **not claimed as qualified Debug process behavior**.
The HELIX explicit orphan-XData error is implemented and compared normally.

## Independent verification

The final deterministic input corpus contains **6,416 scenarios / 25,569
operations**, covering text, modern binary and legacy binary, six profiles,
finite extremes, signed zero, missing/repeated fields, malformed splines,
periodicity, XData, CLASS conflicts and synchronous writer callbacks. Earlier
6,309-scenario results were preliminary; final counts include the added failures.

The test-only C# harness invokes the unchanged private native methods. The
JavaScript harness invokes production adapters. Results compare exact emitted
bytes, model values, reader positions, exceptions and inner errors, registry side
effects and optional CLASS snapshots. Identical inputs are supplied independently;
no result normalization or new comparison waiver was introduced.

**40 supplemental regression tests** are added. Original cases requiring complete
typed document transport are not shortened, so mirrored original-case coverage
remains 3,619. All 6,416 browser inputs are appended after prior categories.
The full browser minimum rises to 153,407; the focused suite is explicitly marked
`scope: primitive-io-only`, `fullSuite: false` and cannot satisfy full browser gates.

The new differential stage, focused browser report and installed-package checks
are mandatory. The offline test imports the actual package barrel and standalone
HELIX module and exercises primitive body transport and CLASS preparation.

### Completed local checks at the final executable tree

September 24, 2026; Debian 13 x64, SDK 8.0.425, .NET 8.0.31, Node 22.16.0 and
Chromium 144.0.7559.96. The [local receipt](primitive-io-local-02017a3.json) retains
ten actual result reports and hashes/metadata for all three original-suite runs.

| Check | Actual result |
| --- | --- |
| Primitive/SPLINE/HELIX, Debug and Release | Each: 6,416 scenarios / 25,569 operations, zero differences or unavailable native observations |
| Recovered entity-body corpus, both configurations | Each: 1,107 scenarios / 3,714 operations, zero differences |
| Prior section-helper corpus, both configurations | Each: 1,165 scenarios / 3,473 operations, zero differences |
| Focused real Chromium, Debug and Release | Each: all 6,416 scenario digests match; 569 native ESM modules; zero page errors |
| Full unchanged original C# suite | Each configuration: 35,309 passed |
| Full mirrored original JavaScript subset | 3,619 passed; zero duplicate or unexpected identities |
| Supplemental tests | 1,224 passed; no failures, skips or TODOs |
| Offline-installed package | Passed, 573 files |
| Source regeneration | Foundation, dimension and GTE checks all match exactly |
| Complete parity verifier | Both configurations fail, retaining missing/stale categories and incomplete coverage |

An interrupted preliminary Release build left a stale writer lease. Its owner
had exited; no Release compiler/reader remained. Only then was the abandoned lock
removed, and all affected Release comparisons, original tests and browser checks
were rerun completely. Initial failure logs are retained separately. Preliminary
or interrupted runs are not counted as completed final successes.

Runtime fingerprint: `cf673f3ac89cd0980fbbe354bf19abf00899743fd7d4baa34915450ce13952f6`.
Verifier fingerprint: `1b4f8d02b895f36b58e51d4de897a659030d098bf31773f17855738093c27306`.
Source fingerprint: `97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

From javascript/, with the pinned source and toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:primitive-io
npm run test:primitive-browser
npm test
npm run test:unit
npm run test:package
npm run verify:complete
```

## Remaining parity work

The ledger is **422/510 library mirrors (88 missing)**, **66/193 original test-file
mirrors (127 missing)** and **3,619/35,309 original cases (31,690 missing)**.
Presence is not exhaustive public-member/signature or behavioral qualification.

Complete typed reader/writer dispatch, document Load/Save/SaveAtomic, resource and
reference reconstruction, remaining private/TABLE/evaluator IO, general version
conversion and broad numerical/host/performance acceptance remain unfinished.
The full 53-stage aggregate and full 153,407-check browser suites were not rerun
at this checkpoint. Neither were hosted CI, HTTP-origin qualification, MPFR or
performance. Missing current evidence stays blocking; historical numerical,
assertion/recursion and globalization failures are not waived or claimed fixed.

No original C#/fixture edits, restored workflows, force push, merge or npm
publication occurred. PR #98 remains a draft.
