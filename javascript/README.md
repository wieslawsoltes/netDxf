# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase API
names. **This is an incomplete port, not full DxfDocument or AutoCAD parity.**
Development remains on draft PR #98, branch `codex/javascript-port`. The package
is private and its publication gate rejects incomplete parity.

The behavioral reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK
8.0.425 / runtime 8.0.31 and Node 22.16.0. Original C# sources, original tests and
shared fixtures remain unchanged. The production JavaScript package does not
load .NET, WebAssembly, a native DXF engine or a conversion service.

## Current checkpoint

`c401128` reconstructs the missing GTE source generator; `0d3e89e` adds its
manifest, independent numerical checks, a duplicate-key exception fix and required
verification. The previously pushed runtime-only `01252a9` modules are preserved:
all **17 GTE files regenerate byte-for-byte**, as do the foundation and dimension
clusters. [GTE contracts and evidence](doc/GTE_NUMERICS.md) record the actual scope.

**Exact GTE parity is not verified.** Release observes **369/401 scenarios and
4,858/5,435 operations**, retaining twelve polynomial operation differences and
32 native recursion failures. Debug observes **363/401 scenarios and 4,800/5,435
operations**, retaining twelve differences and 38 native recursion/assertion
failures. These failures remain mandatory; unexecuted operations are not passes.

Local checks pass **3,587 mirrored original cases**, **1,124 supplemental tests**,
both **35,309-case unchanged C# suites**, and the **557-file installed package**.
The 22 new tests are supplemental, not original-case identities. Release Chromium
executes **144,719 checks**, retaining 119 failures: 87 previous-model scenarios
and 32 GTE missing-native observations. No page error occurs; Node numerical
failures are not waived by matching browser digests. Full-parity gates still fail.

The ledger is **411/510 library mirrors**, **65/193 conformance-file mirrors**, and
**3,587/35,309 original cases**. File presence is not complete parity. The full
50-stage standalone refresh, other browser modes and wider platform/performance
acceptance were not rerun locally in this continuation. Complete typed DXF IO and
other documented gaps are still unfinished.

The [hosted GTE receipt](doc/gte-hosted-0d3e89e.json) records all four completed
Ubuntu/Windows Debug/Release profiles. Every job passes generation, 22 focused
tests and all mirrored originals, but **fails the exact numerical comparison**.
The downloaded archives and original identities were checked; no failure is waived.

## Previous codec checkpoint (historical)

`1601130` corrects reader state/diagnostics; `fc0735f` corrects writer framing,
re-entrant output and required verification. [Codec contracts and source-bound
results](doc/CODEC_STREAMS.md) distinguish low-level transport from unfinished
typed document IO. The published opaque/version/header and actual-stream work
at `90d5a57` was restored and preserved; no unpublished checkout survived.

The independent reader/writer corpora match **1,422 scenarios / 32,902 executed
commands**, plus **40 constructor rejections**, in each configuration. This adds
**564 complete original codec cases** and **40 supplemental tests** without
shortening typed Load/Save-dependent cases. All **3,587 mirrored originals**,
**1,102 supplemental tests**, both **35,309-case unchanged C# suites**, the
**537-file installed package** and exact source-regeneration checks pass locally.

All **49 differential stages** were executed per configuration: **44 pass in
Debug and 43 in Release**. The required browser corpus executes **144,318 checks**;
Release retains **83 earlier-category failures**, with no new codec mismatch or
page error. Debug also executes all checks and retains 31 failures, including
22 unavailable native observations, with no new codec mismatch or page error.
HTTP-origin navigation is blocked by the local browser policy.
Both full-parity verification gates remain failing.

The hosted [codec matrix](doc/codec-streams-hosted-fc0735f.json) passed all four
Ubuntu/Windows Debug/Release profiles, each running both corpora, 40 focused
tests and all 3,587 mirrored original cases. The retained receipt includes the
eight actual comparison reports and verified archive/result hashes.

The ledger is **394/510 library mirrors**, **65/193 conformance-file mirrors**,
and **3,587/35,309 original cases**. Source-file presence does not establish full
API or behavioral equivalence. Typed DXF reading/writing and full document
Load/Save/SaveAtomic, version conversion, private/evaluator APIs and wider
platform acceptance remain unfinished.

## Implemented areas

GTE numerical modules provide source-derived matrix/linear-system, integration,
interval and curve/fitting APIs through the separate `Gte` namespace. They retain
their documented numerical/source-failure limits; the core `BezierCurve` identity
is not replaced by `Gte.BezierCurve`.


The raw layer supports text/binary transport, exact retained bytes, immutable
record/tag views, edits, handle indexing/remapping, dependency traversal, object
transactions, extension dictionaries and draw order. Raw tests do not count as
tests of typed document transport. Unqualified private schemas remain opaque.

Typed models cover geometry, styles, headers, units, collections, many entities,
blocks and INSERT, attributes, layouts/viewports, dimensions and annotations,
retained tables and dependencies. Registered document ownership, resource
canonicalization, XData, guarded graph cloning/erasure and selected object
lifecycles use those actual models. The newly improved codecs preserve native
stream callbacks, failure state and exact scalar transport; they are not the
missing complete typed reader/writer pipeline.

[Architecture](doc/ARCHITECTURE.md), [language adaptations](doc/LANGUAGE_ADAPTATIONS.md),
[verification](doc/VERIFICATION.md), [document ownership](doc/DOCUMENT_OWNERSHIP.md),
[retained dependencies](doc/STORED_DEPENDENCIES.md), [numerics](doc/NUMERICS.md)
and [filesystem contracts](doc/FILESYSTEM.md) document their specific boundaries.
Earlier source-bound counts remain historical, not qualification of later commits.
The [previous complete overview](https://github.com/wieslawsoltes/netDxf/blob/90d5a575194db314cfbe073c1baa07769d79d80a/javascript/README.md)
retains the earlier checkpoint descriptions.

## Usage and verification

Source modules are native ESM. Browser applications can import `javascript/index.js`
from their HTTP origin. Node file access uses the explicit `@netdxf/javascript/node`
entry after an internal/offline package install; nothing is published to npm.
Raw SaveAtomic uses the explicit host and no delete-and-copy fallback. Windows
replacement requires the optional bridge described in [Windows host requirements](doc/WINDOWS_HOST.md).

From javascript/, select the exact pinned source checkout and toolchain:

```sh
export NETDXF_SOURCE_ROOT=/path/to/netDxf-pinned
export CONFIGURATION=Release # Repeat qualification with Debug.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/browser-check.py
npm run test:package
npm run verify:complete
```

Individual `test:codec-readers` and `test:codec-writers` commands run the new
categories. Full differential qualification continues after failures and retains
each log. Real Chromium is required; a mocked DOM does not qualify browser use.
`verify:complete` writes the missing-source/test ledger and failed/unavailable
categories before failing. Focused successes do not waive full-port requirements.

## License

Original netDxf portions retain Daniel Carvajal's MIT license. Mathematical
adaptations retain LGPL-2.1-or-later; Geometric Tools portions retain Boost 1.0.
The aggregate expression is `MIT AND LGPL-2.1-or-later AND BSL-1.0`.
[Third-party notices](THIRD_PARTY_NOTICES.md) and preferred sources remain included.
.NET and MPFR are not distributed as production dependencies.
