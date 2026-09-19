# Verification, evidence, and completion gates

**Current checkpoint:** [HELIX and exp/log](HELIX.md) records the current code and verifier, the 121,817-comparison browser corpus, 6,058 entity scenarios / 34,745 operations, and exact remaining failures. [The earlier display report](DISPLAY_CHECKPOINT.md) is historical; its coverage and CI status are not the current completion ledger.

## Fixed reference

`baseline.json` pins C# commit `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425, runtime/reference pack 8.0.31, 510 library source files, 193 conformance source files, and 399 original DXF fixtures. Its source fingerprint includes support assets. Changing the oracle requires a reviewed baseline update; the tools do not silently roll forward to another runtime or current branch.

`tools/dotnet.mjs` builds the actual C# source through the SDK Roslyn compiler, without NuGet restore or a replacement DXF implementation. Debug and Release use their respective conditional symbols and optimization setting. This qualifies the .NET 8 oracle, not every original target framework. The original test suite contains 35,309 runtime case identities at this pin.

## Numerical reconciliation history

Local code `a14fc00` and its documentation `3e46a00` were fully published as `593138a` and `e30eea1` over the exact upstream `6bedd1b` tree. They preserve the reference-math backend, preferred-source generation, license material and all 61,876 existing math cases, and add 30,904 exact production .NET comparisons plus a distinct MPFR audit of a development-only high-precision evaluator. [NUMERICS.md](NUMERICS.md) retains that historical source-bound evidence; [HELIX.md](HELIX.md) records the current completed hosted run. Neither establishes full-port completion.

## Evidence categories

| Evidence | What it proves | What it does not prove |
|---|---|---|
| Source inventory | Exact paths, hashes, declared types/members, enum values, fixture list | That an absent or partial JS implementation is complete |
| Mirrored conformance | The original named test cases actually ported and executed in JS | Coverage of missing cases or CLR-only adaptations |
| Supplemental JS units | Transaction rollback, failure injection, lifecycle, Unicode, deep graphs, packaging/gate behavior | More original C# test identities |
| Raw differential | Same accept/reject outcome, float bits, tag order, indexes, and exact serialized bytes | Typed JavaScript document/entity support |
| Handle differential | Same contextual occurrences/diagnostics, traversal, remap outcomes, and resulting bytes | Interpretation of opaque/private references |
| OBJECTS differential | Same schema views, transaction results, aliases/ownership, errors, and emitted bytes | General typed OBJECTS or private-schema parity |
| Detached entity differential | Constructors, mutable state, event substitutions, cloning and exact transform results for the implemented primitives, curves, splines, HELIX, text/layout, mesh, underlay and raster models | Registered-document ownership, the absent entity APIs, typed DXF IO or external-content rendering |
| Detached hatch/PAT differential | Model/clone/edit state, exact double bits, rejection behavior and PAT text against the pinned C# APIs | Typed HATCH boundaries, entity ownership, typed DXF IO or complete filesystem parity |
| Typed foundation differential | Actual constructor/method/property/operator results, mutation state, exceptions and exact numeric bits for the selected baseline | Complete geometry/libm equivalence or a typed document engine |
| Observable collection differential | Actual event order, mutation results, invalid operations, enumerators and sorting in integer-list scenarios | Arbitrary generic comparer/collation and inherited overload parity |
| Direct production math audits | All 61,876 upstream cases, 30,904 additional math cases and 13,512 exp/log cases against actual .NET output bits | Every input, native runtime or target platform |
| Independent MPFR audit | Finite rounding and NaN-class agreement for the development-only HighPrecisionMath evaluator at two precisions | A claim that production replaces its .NET reference with mathematical expected values |
| Randomized geometry qualification | Reproducible exact-bit counterexamples; fails on any mismatch | Permission to round or ignore small differences |
| Typed .NET controls | JS raw edits/remaps can be read back by the .NET typed reader in those scenarios | A port of the typed JS construction/editing API |
| Browser differential | Native browser ESM execution matches .NET digests for the selected raw APIs and all shared fixtures | Every browser, worker/thread model, or missing typed feature |
| Offline package install | The npm tarball contains required production modules and runs without fetching dependencies | Publication, semver/API stability, or full completion |
| Benchmarks | Reproducible measured costs for stated operations/workloads | A .NET speedup claim or comprehensive performance qualification |

Both successful parsing and intentional rejections are counted and reported. A rejected fixture is not silently omitted from the corpus. Original files are never rewritten to make a port pass.

## Exact output versus normalized output

The raw API has two different paths: unchanged same-transport output reproduces original bytes; edited/cross-transport output invokes serialization and may change lexical formatting. Tests label these paths separately. For serialization, .NET and JavaScript receive identical input tags/bytes/operations and their resulting **full byte sequences must agree**.

There is no comparator that hides differences by sorting tags, renumbering handles, deleting dates/GUIDs, rounding doubles, stripping whitespace, or ignoring unknown data. Typed .NET fixtures are created once and the exact resulting bytes feed both raw implementations; their original nondeterministic metadata is not independently regenerated for an equality claim. Float values travel as binary64 bit strings and Int64 as decimal strings. Semantic views are compared independently of byte output.

## Commands and report paths

Run the commands in the parent README with `CONFIGURATION=Release` and repeat with `Debug`. A separate pinned checkout is selected with `NETDXF_SOURCE_ROOT`. Important output paths are relative to `javascript/`:

- `artifacts/inventory/source-inventory.json`: complete Roslyn inventory and unchanged fixture hashes.
- `artifacts/dotnet-release/` or `dotnet-debug/`: original results and pin/configuration metadata.
- `artifacts/conformance/`: JS original-case results and execution metadata.
- `artifacts/differential/<configuration>/`, `handles-differential/`, `objects-differential/`, `casing-differential/`, `geometry-differential/`, and `collection-differential/`: completion flag, exact-comparison counts, failure evidence, and fingerprints.
- `artifacts/entity-differential/<configuration>/results.json`: all 6,058 current detached-entity scenarios and 34,745 operations, including retained platform counterexamples.
- `artifacts/hatch-differential/<configuration>/results.json`: detached hatch/gradient state and PAT text comparisons, with input hashes and all failure evidence.
- `artifacts/geometry-exact/<configuration>/results.json`: strict randomized geometry comparisons and every failing exact-bit input/output. This is a separate required qualification, not part of an implied all-geometry baseline pass.
- `artifacts/unit/`, `browser/`, `browser-inline/<configuration>/`, and `package/`: supplemental and separate browser execution reports.
- `artifacts/benchmark/results.json`: input shape, environment, warmups, all samples, median/p95, and memory observations.
- `artifacts/verification/<configuration>/report.json`: implemented-scope result, every missing original case/source path, and outstanding completion gates.

No report with a fatal error, incomplete run, missing corpus, stale runtime/verifier fingerprint, failing/duplicate/skipped/TODO case, or filtered original suite satisfies verification. The JSONL client rejects oracle exits, malformed or unsolicited output, and timeouts. Its shutdown promise is registered before execution so early process termination cannot leave cleanup indefinitely pending or hide the original failure. Supplemental tests deliberately inject those failures.

Browser evidence is derived from .NET outputs, not JavaScript expected values. The corpus contains exact result digests for raw fixtures, authored transactions, foundation calls, observable collections, detached lifecycle, hatch/PAT, styles/LIN/SHX, entity models, coordinate systems, database models and NURBS evaluation: 15,525 comparisons. It additionally includes all 61,876 upstream reference-math, 30,904 additional production math and 13,512 exp/log comparisons, totaling 121,817. Original 256-request geometry batch boundaries are preserved. HTTP and inline browser evidence remain separate requirements and actual result details are retained for every mismatch. The independent randomized geometry corpus still has its own mandatory CI lane; it is not omitted from completion. Code hashes are checked after execution.

## Gates

**Implemented-scope verification** is `node tools/verify.mjs`. It requires the complete pinned .NET case inventory, all currently mirrored JS cases, all shared fixtures in each applicable differential runner, successful browser/package/supplemental checks, generated-file integrity, and current performance evidence. It reports original-suite coverage and missing source paths; it does not claim full completion.

**Full-port completion** is `node tools/verify.mjs --require-complete`, also used by `prepublishOnly`. It remains a failing gate during this partial port, including when implemented-scope checks pass. The current gate explicitly records the outstanding typed API, test/example, signature/semantic, filesystem/atomic-save, and complete runtime/performance qualifications. Before enabling it, replace the remaining-gate declarations with actual executable evidence; do not simply flip `fullParityVerified` in a JSON file.

CI runs Debug and Release implemented-scope jobs with read-only repository permissions and uploads evidence even after failure. A separate strict `geometry-exact-qualification` job retains and fails on randomized bit mismatches. The `full-port completion` job requires that qualification as well as both baseline configurations, checks the Release report, and fails unless all full-parity evidence is present. Its blocked status is expected during this draft PR and is **not** reported as a passing check. The package remains private, the PR remains a draft, and no npm publication is performed.

## Remaining work

The typed `DxfDocument`, full entity/table/style/collection model and typed writer/reader remain major missing areas. Selected geometry/models and collections have native implementations, and the current local fixed randomized corpus passes after numerical reconciliation, but exhaustive native-runtime and platform equivalence remains unqualified. Original typed test factories, all sample scenarios, exhaustive API/member/signature migration, file save/atomic replacement, broad external stream behavior, and every platform/performance acceptance threshold still need implementation and qualification. Raw stored schemas preserve their documented limitations. None of this evidence is a native AutoCAD interoperability certificate.


## Reconciliation and raw filesystem qualification

The source at `72e84d0` already contained a newer generated geometry port. Recovery keeps those implementations and adds the previous stateful corpus as `foundations-differential`, rather than overwriting them with the archived alternatives. Its fixed 5,185 scenarios/49,421 operations remain required evidence. Their historical numeric failures are retained in the old checkpoint reports; the current reconciled local corpus passes without removing cases. Source-file and original-case coverage therefore cannot be inferred from the older historical summary.

Raw `AtomicSaveTests.js` registers only the 82 original raw/helper identities. Typed factory/path tests are still absent; analogous host checks live in supplemental tests instead of inflating original coverage. `filesystem-differential` executes the unchanged .NET SaveAtomic implementation and the JS host on real isolated directories, comparing all 399 fixtures across both requested transports and destination existence states. It compares rejection classes/parameters, exact destination bytes, held-reader contents, cleanup and cancellation behavior. The complete corpus contains 1,782 scenarios. CI also runs this corpus on Linux and Windows; each platform's actual result is retained independently.

The browser corpus now preserves each 256-request geometry batch exactly as supplied to the .NET oracle. Static Epsilon mutations remain visible to later requests in the batch; only result objects are hashed separately. A regression test demonstrates that splitting this stateful batch changes the answer. That batching fix preserved the then-current 5,745 assertions; it neither removed cases nor rewrote expected results. The later 523 detached lifecycle scenarios brought that historical browser corpus to 6,268; subsequent model and numerical extensions are counted separately in the current checkpoint.

`tools/run-qualification.mjs` executes all independent comparisons without short-circuiting after one mismatch and returns nonzero if any fails. `verify.mjs` now writes its report even for negative or missing evidence, including the full missing-source and original-case lists where available. Both exact numerical corpora and the filesystem corpus are required. A passing raw or deterministic geometry subset cannot override a failed exact qualification. CI continues browser/package/report steps after failures while keeping the job unsuccessful.

## Detached hatch/PAT qualification

`test:hatch` currently requires 376 scenarios, 2,559 operations and at least 174 exact PAT text comparisons, including the later 13 terminal-NUL syntax cases. The production C# `HatchPattern`/`HatchGradientPattern` APIs provide every expected value. All 155 listed patterns in the two unchanged PAT support files are included. The stage persists complete positive/negative evidence with source/runtime/verifier fingerprints in `artifacts/hatch-differential/<configuration>/results.json`. It is not a replacement for any foundation, geometry, filesystem or browser gate.

The 52 additional original identities are complete detached-model cases. Unimplemented typed document, nested entity and wire cases are not registered under shortened bodies. See [hatch model scope, host contracts and accounting](HATCH_PATTERNS.md).

At implementation commit `bd1a42f`, the new hatch stage passed in both Debug and Release. Run `35210680084` also passed all 35,309 original .NET cases and 2,017 mirrored JavaScript cases in each configuration. At that earlier checkpoint, 166 foundation mismatches, 142 randomized-geometry mismatches, and the Debug Bézier NaN-sign failure remained blocking. The fixed foundation and randomized corpora pass in the latest Linux CI; the Debug Bézier result is still unresolved. Downloaded historical artifact hashes, runtime/verifier fingerprints and the independently passing Windows regression results are retained in the [hatch checkpoint](HATCH_PATTERNS.md#completed-checkpoint-evidence--bd1a42f). A later documentation-only commit does not substitute its own workflow status for that completed executable evidence.


## Historical display browser, environment and performance gates

At the `5b63748` display checkpoint, the complete browser corpus had 102,394 comparisons: 9,614 model/raw regression comparisons, the retained 61,876 direct reference-math cases, and the additional 30,904 direct .NET cases. HTTP-origin and inline-native-ESM results remain separate required lanes; local HTTP navigation rejection is not overridden by inline execution. Actual browser counterexamples are retained without an early failure-count cutoff.

`test:math:independent` explicitly identifies its subject as `HighPrecisionMath development reference`. MPFR is not a production dependency and a passing independent result never changes the .NET comparison's expected values. The default production entry continues to use the upstream reference-math modules.

At that historical checkpoint, fixed foundations and randomized geometry passed in both Linux configurations. Hosted Chromium 152 passed all 102,394 Release comparisons; Debug retains its Bézier NaN-sign failure. Windows retains two exact result mismatches in `display/seeded/7`, identical to the starting checkpoint; all newly added underlay/raster cases pass there. The default local ICU profile remains rejected even though pinned CI globalization passes. An explicitly selected invariant-mode comparer experiment remains separate from the default casing result. None of this completes the missing typed APIs/tests or broad platform/performance qualification.

The math benchmark measures host, production reference and development reference costs separately, retaining all samples. Neither a generation workflow success nor improved numerical coverage is a full parity or performance acceptance claim. Methodology and historical measurements are in [NUMERICS.md](NUMERICS.md); historical display evidence is in [DISPLAY_CHECKPOINT.md](DISPLAY_CHECKPOINT.md), and current hosted evidence is in [HELIX.md](HELIX.md).
