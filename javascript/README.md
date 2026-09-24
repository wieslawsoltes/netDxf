# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase API
names. **Full parity is incomplete.** Work remains on draft PR #98, branch
`codex/javascript-port`. The package is private and full-parity gates remain
strict. Original C# sources, tests and shared fixtures are unchanged.

Reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`; SDK 8.0.425 /
.NET 8.0.31 / Node 22.16.0. Production JavaScript does not load .NET, WebAssembly,
an external DXF engine or a conversion service.

## OBJECTS graph integration

`4de4df3` connects record dispatch, physical-source admission, shared object import,
metadata, common output envelopes and CLASS preparation. `267d916` adds required
independent verification and 40 supplemental regressions. The
[contract](doc/OBJECT_GRAPH_IO.md) describes actual linked graph reconstruction,
source assumptions, partial failures and remaining full-file transport boundaries.

Release matches **937 scenarios / 8,715 operations**. Debug matches **936 observed
scenarios / 8,708 operations**, but a native assertion on a null automatic reactor
makes one scenario unavailable and keeps that category failing. Focused Chromium
has the same result: all Release inputs match, while the single missing native
Debug observation remains blocking; neither profile has page errors.

Both unchanged C# suites pass **35,309 tests**. The mirrored JavaScript subset
passes **3,619 original cases** and the supplemental suite passes **1,410 tests**.
Five prior regression categories pass in both configurations, the **623-file
installed package** passes, and foundation/dimension/GTE regeneration is exact.
The [receipt](doc/object-graph-io-local-267d916.json) binds these local results to
the executable source. Focused results do not establish full parity.

The ledger is **467/510 library paths**, **66/193 original test files**, and
**3,619/35,309 original cases**. Both full-parity verifiers fail. The complete
58-stage aggregate and 160,187-check browser suites were not executed here;
Windows/hosted, HTTP-origin, MPFR and performance are not newly qualified.

## Recovery and scope

The prior section-record runtime and tests were already pushed at `4ea5ebe` and
`2b8e345`. The three remaining saved documentation files were recovered unchanged
and published at `389237e`. Their historical blocked-publication account is not
current connection status. The [saved contract](doc/RETAINED_RECORD_IO.md) and
receipt remain historical evidence for their named executable tree.

All eighteen deleted JavaScript workflows remain deleted. No workflow is
recreated by this continuation. Local strict qualification scripts remain available.

The port includes raw text/binary transport, handle indexing/remapping, object-store
transactions, extensive typed models, registered document ownership, retained
private records and explicit section/entity/object codecs. OBJECTS graph import
now joins physical record observation with the registered object database, including
aliases, defaults, extensions, reactors and ordered deferred resolution. Unsupported
private payloads remain opaque, and generated defaults cannot masquerade as accepted
source identities.

This still does **not** complete DxfDocument.Load/Save/SaveAtomic, all-section
resource reconstruction, general version conversion or private evaluation. Raw
transport and section fixtures are not substitutes for complete typed file tests.
Source-file presence is not full member/signature or behavioral qualification.

## Verification

From javascript/, select the pinned source checkout and toolchain:

```sh
export NETDXF_SOURCE_ROOT=/path/to/netDxf-pinned
export CONFIGURATION=Release # Repeat with Debug; retained failures stay blocking.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:object-graph-io
npm run test:object-graph-browser
npm run test:package
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/browser-check.py
npm run verify:complete
```

The aggregate continues independent stages after failures and retains logs.
`verify:native`, `verify:dimension-source` and `verify:gte` check source lowering.
Real Chromium is required; focused reports cannot satisfy complete-browser gates.
DOTNET_ROOT / DOTNET select the compiler and CHROMIUM selects the browser.
Missing, stale or unavailable observations are not treated as successes.

The default entry is browser-safe. The explicit Node entry is
`@netdxf/javascript/node`; raw Windows atomic replacement requires the optional
Node-API host. See [filesystem contracts](doc/FILESYSTEM.md) and
[Windows qualification](doc/WINDOWS_HOST.md). The package is offline-tested, not
published to npm. Full typed atomic saving remains a separate unfinished task.

## Contracts and license

[Architecture](doc/ARCHITECTURE.md) · [Verification](doc/VERIFICATION.md)
· [Language adaptations](doc/LANGUAGE_ADAPTATIONS.md) · [Numerics](doc/NUMERICS.md)
· [GTE](doc/GTE_NUMERICS.md) · [Stream codecs](doc/CODEC_STREAMS.md)
· [Primitive transport](doc/PRIMITIVE_IO.md) · [Entity bodies](doc/ENTITY_BODY_IO.md)
· [Database payloads](doc/DATABASE_PAYLOAD_IO.md)
· [Retained records](doc/RETAINED_RECORD_IO.md) · [OBJECTS graph](doc/OBJECT_GRAPH_IO.md).
Every historical report qualifies its named source, not every later build.

Remaining APIs, original tests/examples, typed transport and broad qualification
are unfinished. No throwing generated stubs are counted as completed mirrors.
Original netDxf is MIT licensed; mathematical adaptations retain LGPL-2.1-or-later
and GTE retains BSL-1.0. Aggregate: `MIT AND LGPL-2.1-or-later AND BSL-1.0`.
See [third-party notices](THIRD_PARTY_NOTICES.md) and retained preferred sources.
.NET and MPFR are not production dependencies.
