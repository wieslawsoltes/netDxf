# Numerical compatibility and independent mathematical auditing

## Reconciled checkpoint

Local code commit `a14fc00909d8e2f9acccf71dc8095a94393460ed` directly advances upstream `6bedd1bfba314d39fad3254b12f13fb4d10a7749` on PR #98. It has not been pushed or qualified by a new hosted workflow. The upstream archive was hash-checked and its Git tree reproduced exactly: `2a77d4f4ae97897c56b081164c482364c8db0b3d`.

The original C# remains pinned to `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425 and runtime/reference pack 8.0.31. Original source and shared fixture bytes are unchanged. The full source/member ledger remains **168/510 library mirrors, 24/193 conformance-file mirrors and 2,543/35,309 original cases**. Numerical test additions do not count as newly ported original cases.

### Preserve the newer production backend

While this continuation was being tested against `8ee09bb`, upstream added the pinned `runtime/reference-math` implementation, preferred sources, generator, notices, packaging checks and a 61,876-case direct .NET comparison. This continuation preserves all of that newer work. It does **not** overwrite it with the separately developed high-precision evaluator, remove existing cases, or change the mixed license metadata.

Production `DotNetMath` continues using the upstream reference-math modules. `tools/HighPrecisionMath.mjs` is a **development-only mathematical reference**; the public package entry does not import it and the package file whitelist does not ship it. Its independent MIT implementation does not replace or relicense the upstream LGPL modules. See `THIRD_PARTY_NOTICES.md` and the retained preferred sources for the production backend's existing attribution and license material.

## New production correction

`RemainderDouble` now distinguishes invalid dividends/zero divisors from NaN divisors. Invalid dividends and zero divisors return the canonical .NET NaN through a binary-backed read. A NaN divisor is quieted while retaining its sign and payload. Finite remainder still uses the native JavaScript remainder operation; no epsilon, tolerance, expected-result table or per-input correction was added.

The added direct corpus covers these cases, signed signaling and quiet NaNs, normal and subnormal boundaries, large angles, inverse-function domains, signed axes and exponent-scaled atan2 quadrants. The previous local Debug browser Point/Infinity discrepancy is no longer observed with this remainder correction; the unrelated Bézier NaN-sign difference remains a separate gate.

## Three distinct kinds of evidence

**Production compatibility:** `npm run test:reference-math` retains all **61,876 upstream comparisons**, including software fused-multiply-add. `npm run test:math` adds **30,904 comparisons** from a different seeded/boundary corpus and includes remainder. Every expected result comes from the actual pinned .NET runtime; complete binary64 bits, including NaN payload/sign, are compared. Any difference still fails the relevant stage.

**Independent mathematical reference:** `npm run test:math:independent` compares `HighPrecisionMath` against an installed MPFR development library at **512 and 1,024 bits**. This is explicitly not a mathematical pass claim for the production reference-math backend. Finite values and signed zeros are compared after rounding to binary64; NaNs are compared as a class because MPFR does not implement .NET NaN sign/payload semantics. The production .NET stages continue checking those bits exactly. Missing libraries, incomplete/duplicate input identities, result mismatches and disagreement between the two MPFR precisions all fail this independent stage. No library is downloaded, installed, bundled or loaded by the production entry.

**Real browser execution:** the complete corpus retains the original 7,664 comparisons, the 61,876 upstream reference-math comparisons and the 30,904 added direct comparisons: **100,444 in total**. HTTP-origin and inline native-ESM lanes remain separate required evidence. Both retain all actual counterexamples; neither stops after a small number of failures. Browser expected digests come from .NET, never from the JavaScript implementation being tested.

No passing mathematical experiment overrides a production compatibility failure. No original or upstream comparison was removed, normalized, rounded, relabelled as an expected failure or hidden by an allowlist.

## Development reference implementation

`HighPrecisionMath` uses 1,536-bit fixed-point argument reduction, 256-bit working precision and nearest-even binary64 conversion. Pi is calculated with Machin's identity and integer arctangent series. Sine/cosine Taylor coefficients are calculated from factorial reciprocals and evaluated with integer Horner arithmetic. A bounded one-entry sine/cosine cache avoids duplicate pair evaluation. Inverse functions use arctangent reductions and an integer square root; small-input paths preserve subnormals and signed zero.

An expanded atan2 audit caught an over-aggressive shortcut in the first local prototype: rounding a large finite ratio directly to pi/2 before selecting the negative-x quadrant can lose a correction that crosses a binary64 midpoint. The reference now retains the correction until final rounding. The corpus adds **520 ratio-neighbor and scaled-quadrant cases**, not an input-specific fix.

The first high-precision prototype reduced the old fixed foundation mismatch count from 166 to 5 and randomized geometry from 142 to 2, but still disagreed with .NET on 19 direct last-bit results. Those sampled results matched MPFR. This is precisely why mathematical accuracy must not be substituted for the user's requested .NET compatibility. The prototype was retained as a development reference and the newer upstream production backend was preserved.

Agreement at the tested precisions is **not an exhaustive correct-rounding proof for every binary64 input**. `Pow`, `Exp`, `Log` and `Log10` remain outside the new trigonometric audit. This does not establish arbitrary target-runtime, operating-system or hardware equivalence.

## Local qualification

Measurements use Debian GNU/Linux 13 x64, Node 22.16.0, .NET 8.0.31 and Chromium 144.0.7559.96. The complete original C# suites were executed in both configurations against the unchanged pinned source; those source-bound results are retained. JavaScript, numerical, package and browser evidence is regenerated for the reconciled executable bytes.

| Check | Result |
| --- | --- |
| Complete original C# suite | 35,309 passed in each Debug and Release configuration |
| Mirrored original JavaScript suite | 2,543 passed; zero unexpected identities |
| Supplemental JavaScript suite | 199 passed; no failed/skipped/TODO cases; 12 added tests |
| Upstream direct reference-math corpus | 61,876 exact .NET comparisons per configuration; zero mismatches |
| Added direct production math corpus | 30,904 exact .NET comparisons per configuration; zero mismatches |
| Fixed foundations | All 5,185 scenarios / 49,421 operations pass in each configuration; the old 166 mismatches are absent |
| Exact randomized geometry | All 2,000 comparisons pass in each configuration; the old 142 mismatches are absent |
| Independent development reference vs MPFR | 28,296 finite bit comparisons and 2,608 NaN-class comparisons pass at both precisions; zero precision disagreements |
| Entity / style / hatch models | 479 / 541 / 376 scenarios respectively; zero mismatches in each configuration |
| Raw / handles / OBJECTS / detached lifecycle / Linux filesystem | Separate exact regression stages pass in each configuration |
| Offline package | 244 files; offline import and binary-object roundtrip pass; preferred-source reproduction remains enforced |
| Baseline geometry | Release: 4,254 comparisons pass. Debug: existing Bézier NaN-sign difference remains |
| Inline native ESM Chromium / Release | All 100,444 comparisons pass; no page errors |
| Inline native ESM Chromium / Debug | All 100,444 comparisons executed; only the existing Bézier NaN-sign difference; no page errors |
| HTTP-origin Chromium | Local navigation is blocked; no passing HTTP-origin result is inferred from inline execution |

The default local ICU profile remains rejected by the unchanged profile guard: 1,453 mappings rather than the pinned 1,448. An explicitly selected invariant-globalization comparer experiment reproduces the pinned scalar map and passes 3,045 ordinal comparisons. It is retained separately, not substituted for the default failing environment or represented as a fully qualified globalization mode.

Full verification still fails while the Debug Bézier result, default local casing profile, HTTP-origin qualification and full-port gates remain unresolved. New Windows and hosted Chromium 152 results are not inferred from local evidence. The upstream PR workflows for the observed `6bedd1b` snapshot reported `action_required`; generation success alone is not a passing full qualification run.

## Performance and packaging

`npm run benchmark:math` reports 12 warmed samples over 1,024 reproducibly generated input values after three warmups. It separates native host timings, the **production reference backend**, and the **development high-precision reference**. Host math is only a timing control, never a substitute expected result. Import startup is excluded; the results are descriptive Node timings, not .NET performance comparisons or release acceptance.

The final local Release measurement was **16.936 ms** for 1,024 production sine/cosine pairs, compared with **0.103 ms** using host math and **7.108 ms** for the development reference. Production atan took **1.444 ms**, versus **0.056 ms** for host math. These costs are workload/host-specific; exact reference arithmetic is not being presented as a speedup.

The development reference is not on the production import path. Exact reference arithmetic can nevertheless be materially slower than host trigonometry; measured costs are retained rather than claiming a speedup. Inspect `artifacts/math-benchmark/results.json` for all samples and environment details. The existing raw/vector benchmark remains independent.

The original model code remains MIT and the upstream production math files retain their existing LGPL-2.1-or-later notices and preferred sources. Package metadata remains `MIT AND LGPL-2.1-or-later`. This continuation neither removes that material nor claims a legal compliance certification. The package remains private and no publication was performed.

## Reproduce

From `javascript/`, with the exact pinned toolchain installed:

```sh
export CONFIGURATION=Release
# export DOTNET_ROOT=/path/to/exact-dotnet-8.0.425
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
npm test
npm run test:unit
npm run verify:reference-math
npm run test:reference-math
npm run test:math
npm run test:math:independent  # Explicit installed development MPFR library required.
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-check.py
npm run test:browser:inline
npm run test:package
npm run benchmark
npm run benchmark:math
npm run verify               # Writes negative/missing evidence; full parity is not complete.
```

Repeat with `CONFIGURATION=Debug`. `MPFR_LIBRARY` selects an explicit development library. The ctypes ABI is exercised on Linux x64; other hosts require separate qualification. Reports retain exact case counts, fingerprints, completed/fatal state, full counterexamples and their configuration. Generated corpora, toolchain binaries and test result files are not production runtime inputs.

Runtime fingerprint: `061ee5964e151ed9f4fe273ee20414a662726546f93bb0d78b63c1c1c91c3225`.
Verifier fingerprint (POSIX): `fe83c9cfaefaf4de14d657f1bf25903fe22be40825be291de1f2455953422242`.
Pinned C# / shared-input fingerprint: `97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

## Mathematical references

The development evaluator uses standard identities, with independently written code; these are documentation, not runtime or expected-result providers.

- NIST DLMF trigonometric series: https://dlmf.nist.gov/4.19
- NIST DLMF inverse identities and series: https://dlmf.nist.gov/4.23 and https://dlmf.nist.gov/4.24
- Microsoft .NET native-runtime accuracy remarks: https://learn.microsoft.com/en-us/dotnet/api/system.math.sin?view=net-8.0
- MPFR rounding and transcendental contracts: https://www.mpfr.org/mpfr-current/mpfr.html
- ECMAScript Math.sin contract: https://tc39.es/ecma262/multipage/numbers-and-dates.html#sec-math.sin
