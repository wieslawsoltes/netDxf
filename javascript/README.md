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
