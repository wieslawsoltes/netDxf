# Verification of the JavaScript port

## Reference and acceptance

`baseline.json` pins the C# source, SDK, runtime, reference pack and baseline
counts. The source fingerprint covers paths and Git blob identities under
`netDxf`, `tests` and `TestDxfDocument`, including fixtures and support assets.
Never substitute the current `netstandard` branch or a newer source archive for
that reference when claiming native differential qualification.

Different checks answer different questions:

- Original conformance tests retain their C# source identities and complete
  assertions. Supplemental tests and differential scenarios do not increase
  original-case coverage. Filtered runs cannot qualify the full mirrored suite.
- Independent .NET comparisons execute the pinned production implementation.
  JavaScript roundtrips and tests using doubles are not substitutes for those
  comparisons. Wire comparisons retain handles, metadata and binary64 bits.
- Browser, package, platform and performance checks have separate requirements.
  Focused browser results do not replace complete HTTP-origin and inline suites.
  Descriptive benchmarks are not performance acceptance.

`tools/verify.mjs` is the executable authority for required categories, counts,
source inventory, generated-file hashes and evidence freshness. Both implemented
scope and full parity require all applicable evidence; missing or stale reports,
nonzero exits, native assertion/crash failures and unavailable observations remain
blocking. Do not lower counts, delete comparisons, introduce expected-failure
waivers or normalize numerical differences to make the gate pass. An original
C# test's own tolerance is preserved only for that test; exact differential
comparisons remain exact.

Source-file presence alone does not establish member/signature or behavioral
parity. Consult the README for published scope, and generated reports for actual
results at a specific runtime and verifier fingerprint.

## Native setup and generation

From `javascript/`, use an unchanged checkout of the pinned C# reference and the
exact toolchain listed in `baseline.json`. Production consumers do not need .NET.
`DOTNET` or `DOTNET_ROOT` selects the development host; builds use SDK Roslyn and
the pinned reference assemblies without a NuGet fallback.

```sh
export NETDXF_SOURCE_ROOT=/path/to/pinned/netDxf
export CONFIGURATION=Release # Repeat native comparisons with Debug.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm run verify:native
npm run verify:dimension-source
npm run verify:gte
npm run verify:reference-math
npm run verify:exp-log
npm run test:unit-factors
```

The source inventory is generated under `artifacts/inventory/`. NativePort and
reference-math manifests are active build/verification inputs, not disposable
historical reports. Generated production files must continue to regenerate
exactly. Keep original fixtures, licenses and preferred mathematical sources.

## JavaScript, differential and package checks

```sh
npm test
npm run test:unit
npm run test:differential
npm run test:package
npm run verify:complete
```

`npm test` writes original-case results and metadata under `artifacts/conformance/`.
`DXF_TEST_FILTER` selects a diagnostic subset; such evidence is explicitly filtered.
`DXF_JS_TEST_ARTIFACTS` can select its output directory. Supplemental results are
under `artifacts/unit/`; installed-package checks are under `artifacts/package/`.
The package check installs an offline pack, verifies required source/license
files, and exercises actual exported APIs. The package remains private.

The differential aggregate continues independent stages after failures and
retains their exit statuses and logs. Domain-specific scripts are listed in
`package.json`; run `npm run` to inspect them. Complete native builds before
starting readers of their oracle assemblies. Build leases protect those files;
do not remove a lease without checking its owner and live readers.

## Browser and host checks

```sh
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/browser-check.py
```

Install development requirements from `tools/requirements-browser.txt` and select
real Chromium with `CHROMIUM` where required. The browser uses the native ESM
implementation, not a Node substitute. Preserve stateful native request batches:
splitting a batch can change static state such as MathHelper.Epsilon.

Windows atomic replacement requires its optional Node-API host and separate
qualification; see [WINDOWS_HOST.md](WINDOWS_HOST.md) and [FILESYSTEM.md](FILESYSTEM.md).
The native globalization profile is pinned independently of the SDK/runtime;
see [GLOBALIZATION.md](GLOBALIZATION.md). Do not treat an incompatible host profile
as a successful comparison.

MPFR checks target the development reference, not a replacement for .NET's
production results. They must not rewrite native expected values. Numerical
adaptations and platform limitations are documented in [NUMERICS.md](NUMERICS.md).

## Evidence storage

Fresh machine-generated results and logs belong under ignored `artifacts/` or in
external CI/validation archives. Runtime and verifier fingerprints bind reports
to the actual code tested; documentation-only edits are excluded from those hashes.
Record incomplete attempts separately from completed reruns. Keep full failures,
not just passing summaries.

Historical committed run receipts and recovery reports remain accessible in
[the pre-cleanup tree](https://github.com/wieslawsoltes/netDxf/tree/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc).
They are not inputs to the active verifier and do not qualify later source trees.
