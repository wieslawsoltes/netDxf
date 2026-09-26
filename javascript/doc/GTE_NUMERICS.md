# GTE source recovery and numerical qualification

Runtime-only predecessor: `01252a9f1d48917b657451b62ffcb1df6d109c45`.
Reconstructed generator: `c4011285d8a4b8c7a2cde8f84fd8f0c3a9c7bc4b`.
Executable integration: `0d3e89e4df0ec051356c7c70e9da7c3e4f3ff590`.
Executable tree: `866c237b2f62183de47511bef9908ece4c513045`.
C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

**GTE exact parity is not verified.** Polynomial bit differences and native
recursive equality/assertion failures remain blocking. Source regeneration and
passing regression tests are not a substitute for the failed exact comparison.

## Recovery and source generation

The remote branch had advanced beyond the PR's codec summary. Its runtime-only
GTE commit already contained seventeen source modules, language adapters and
exports, but not their generator or qualification integration. No unpublished
checkout survived. The source archive was restored and reproduced tree
`3cd0817386dd2f4d806eea37ab395fba6194e5c1` exactly. Archive SHA-256:
`b6eacba355b548bc511a6cb25b4c9e6ff761b859c5e2cbfa5e6d9cd7d71580dd`.

The missing generator was reconstructed from the unchanged pinned C# sources.
All seventeen already-published GTE modules regenerate byte-for-byte; no generated
numerical algorithm was replaced. The existing foundation and dimension clusters
also regenerate exactly. The GTE source selection is explicit and unsupported
lowering fails rather than generating placeholder methods.

The lowering handles GTE reference/value semantics, fixed arrays, nested types,
overloaded indexers, captured ref/out locations, local functions, delegate calls
and the numerical sorted dictionary adapter. `gte-port-manifest.json` binds each
source/output hash and records the actual per-type member/signature mappings.
The manifest is included in the private package and required by verification.

The selected original files cover dynamic vectors/matrices, row/column indexing,
Gaussian elimination, banded systems, bisection, polynomial roots, integration,
interval queries, parametric/Bezier/B-spline/NURBS curves, fitting and reduction.
Original filenames are retained, including `RootsPolynominal.js` containing the
`RootsPolynomial` class. Geometric Tools notices and Boost Software License 1.0
remain in the generated modules and package.

## Runtime fix and API adapters

The independent quartic comparison exposed an incorrect exception parameter in
GteSortedDictionary.Add. A duplicate numerical key must produce ArgumentException
with no parameter name, rather than `key`. The adapter now matches that behavior,
including NaN and positive/negative-zero duplicate keys. No roots, output bits,
exception results or observed native values are normalized to hide differences.

Use `Gte` for the GTE namespace. The original `BezierCurve` export is not replaced;
`Gte.BezierCurve` and `GteBezierCurve` identify the GTE implementation. Ref/out
parameters use explicit `{ value: ... }` cells and ambiguous constructors have
source-signature selectors. CLR process-specific hash values, arbitrary reflection
and a complete managed runtime are not implemented by these adapters.

```js
import { Gte, Vector3 } from './javascript/index.js';

const matrix = new Gte.GMatrix(2, 2, [1, 2, 3, 4]);
matrix.set_Item(0, 1, 7);
console.assert(matrix.get_Item(0, 1) === 7);

const curve = new Gte.BezierCurve([
  new Vector3(0, 0, 0), new Vector3(1, 0, 0),
  new Vector3(2, 0, 0), new Vector3(3, 0, 0),
], 3);
const jet = { value: null };
curve.Evaluate(0.5, 1, jet);
console.assert(jet.value[0].X === 1.5 && jet.value[1].X === 3);
```

Gte.GTE.UseRowMajor selects the shared indexing convention; callers must coordinate
that global setting. These examples do not establish qualification of every GTE
operation. In particular, GVector equality and operations that reach its recursive
source equality path remain unusable/unqualified; they are not silently repaired
against a different behavioral baseline.

## Independent comparison and failures

The input-only corpus contains **401 distinct scenarios / 5,435 requested
operations**. A C# reflection controller invokes the unchanged native assembly;
the JavaScript controller invokes the production modules with the generated
signature map. Observations include exact double bits, arrays, mutable/ref/out
state, callback order and exception type/parameter. They contain no expected
algorithm implementation. Strict envelopes reject incomplete or malformed replies.

Native process termination remains unavailable evidence and starts a new process
for subsequent scenarios. Previously completed operations in an aborted scenario
are not fabricated from JavaScript results. Requested operations and fully
observed operations are reported separately.

Local results on Node 22.16.0, SDK 8.0.425 / .NET 8.0.31, Debian 13 x64,
September 23, 2026:

| GTE profile | Observed scenarios | Observed operations | Numerical operation differences | Unavailable native scenarios |
| --- | --- | --- | --- | --- |
| Release | 369 / 401 | 4,858 / 5,435 | 12 | 32 |
| Debug | 363 / 401 | 4,800 / 5,435 | 12 | 38 |

The twelve differences occur in six polynomial cases, each observed through the
call result and its later snapshot. Their complete inputs and bit differences
remain in the reports. The root cause is not established by this checkpoint.
Browser and Node results differ, so a browser match does not waive a Node failure.

The unchanged GVector equality operator invokes its own overloaded equality while
checking null. Native execution terminates with stack overflow in 31 vector
scenarios and the explicit equality probe. JavaScript recursion produces a host
exception rather than an equivalent completed native result; it is not counted
as a match. Debug also terminates on five fitting assertions and one invalid
matrix-index assertion. Those source failures remain required, not allowlisted.

Release matches the fully observed matrix, banded, Gaussian, integration,
interval, curve, fitting, vector-storage and callback categories. Debug matches
its fully observed cases in those categories, but its five fitting assertions
remain unavailable. Finite corpus success does not establish exhaustive numerical
or public-API equivalence.

## Required integration and other completed checks

The GTE category is mandatory in aggregate qualification and verification. The
completion requirement remains all 401 native scenarios / 5,435 operations with
zero unavailable results; it has not been reduced to the observed subset.
All 401 browser inputs are appended after every earlier comparison, and the
minimum rises to **144,719 digest checks**. Installed-package tests use only the
barrel and standalone package imports, not the checkout or native oracle.

| Check at executable integration | Observed result |
| --- | --- |
| Original unchanged C# suite | 35,309 passed in Debug and 35,309 in Release |
| Full mirrored original JavaScript suite | 3,587 passed; no new original identities claimed |
| Supplemental JavaScript suite | 1,124 passed; 22 new tests; no failures/skips/TODOs |
| Offline-installed package | Passed; 557 files |
| Foundation, dimension and GTE regeneration | All three exact checks passed |
| Release inline Chromium 144.0.7559.96 | All 144,719 checks executed; 119 failures; no page errors |
| Complete parity gate, both configurations | Failed; missing, stale and failing evidence retained |

The browser failures comprise **87 previous-model scenarios** and **32 GTE
native-unavailable scenarios**. All 369 natively observed GTE browser scenarios
have matching digests in this run. This does not waive the twelve Node polynomial
operation differences or qualify the missing native observations. Previous model
counts can differ with browser/JIT execution; no numerical fix is claimed here.

The newly enlarged 50-stage standalone differential aggregate was not rerun in
full for this checkpoint. Older standalone reports are not relabeled as current.
Debug and HTTP-origin browser modes, independent MPFR and performance tests were
also not rerun here. Historical results remain tied to their original commits.
The initial interrupted original-suite command is not counted as a completed run;
the later complete 3,587-case execution supplies the passing evidence.

Runtime fingerprint:
`bf35d830ae455398df288261d7de1528b6e5082722ec5278ca1d236c2a3f5468`.
POSIX verification fingerprint:
`0fbf9fc34979e84f7ada9de27e25161127228ec8683df40e8276cea8089a3603`.

From `javascript/`, with the exact source checkout/toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
npm run verify:gte
node tools/dotnet.mjs geometry
npm run test:gte # Currently fails; retains every numerical/native failure.
node --test tests/unit/gte-numerics.js
npm test
npm run test:unit
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

## Remaining parity work

Current ledger: **411/510 library mirrors (99 missing)**,
**65/193 conformance-file mirrors (128 missing)** and
**3,587/35,309 original cases (31,722 missing)**. The seventeen GTE mirrors were
already in the runtime-only predecessor; the new generator and tests do not add
them again. File presence is not complete API or behavioral qualification.

Complete typed DXF reading/writing, Load/Save/SaveAtomic integration, general
version conversion, missing private/TABLE/evaluator APIs and original tests,
exact numerical behavior and broad host/performance qualification remain work.
The full-source compiler probe also remains an experiment, not a working typed-IO
backend. No generated stubs, native process shortcuts, tolerance, expected-failure
allowlist or dropped comparison is used. Original C# sources/tests and shared
fixtures are unchanged. PR #98 stays draft; no merge, force push or npm publication.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/GTE_NUMERICS.md). Current published scope is maintained in the [README](../README.md).
