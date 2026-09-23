# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase API
names. **This is an incomplete port, not full DxfDocument or AutoCAD parity.**
Development remains on draft PR #98, branch `codex/javascript-port`. The package
is private and its publication gate rejects incomplete parity.

The behavioral baseline is `3496ab91893a1e4ec9261b4833479f1799149cdc`, with SDK
8.0.425 / runtime 8.0.31 and Node 22.16.0. Original C# sources, original tests and
shared DXF fixtures remain unchanged. Production JavaScript does not load .NET,
WebAssembly, a native DXF engine or a server-side conversion service.

## Current checkpoint

`14d8727` adds stored TABLESTYLE/CELLSTYLEMAP models and qualified edits;
`7b2249b` adds independent C# comparisons and mandatory verification. The
[table-style contract](doc/TABLE_STYLES.md) describes source-bound headers, rows,
six-slot borders, stored data/unit codes, STYLE bindings, immutable snapshots,
bounded entry-name edits and callback/Unicode behavior. Internal retained
constructors do not substitute for the missing typed DXF reader/writer.

The new **279 scenarios / 5,829 operations** match in Debug and Release. Local
checks pass **2,907 mirrored original cases**, **970 supplemental tests**, both
**35,309-case unchanged C# suites**, and the **509-file offline package**. This
adds two complete original constructor cases and 37 supplemental regressions;
original serialization cases are not shortened to claim extra coverage.

Release inline Chromium executes all **141,465 comparisons**, with no new table
mismatches or page errors, but **83 other failures remain**. The complete parity
gates still fail. The current ledger is **375/510 library mirrors**, **58/193
conformance-file mirrors**, and **2,907/35,309 original cases**. Presence is not
complete API or behavioral qualification. See the contract for current hosted
and aggregate evidence and explicit remaining TABLE/private-schema boundaries.

The hosted Ubuntu/Windows Debug/Release matrix passed all four profiles, each
running all five lifecycle corpora, 144 focused tests and all mirrored originals.
Its [receipt](doc/table-styles-hosted-7b2249b.json) retains the actual 20 differential
reports and full-suite metadata/hashes. The broader 43-stage local refresh passed
38 stages in Debug and 37 in Release; the remaining failures and the corrected
initial missing-Debug-oracle attempt are documented, not waived.

## Previous checkpoint (historical)

`9cf0351` adds the source-bound SECTION_MANAGER lifecycle: creation, ordered
membership replacement, retained-packet validation and guarded explicit erasure.
[Manager contracts and source-bound evidence](doc/SECTION_MANAGER.md) document
callback/disposal ordering, re-entrancy, the 65,536-member bound, CLASS handling,
original identities and explicit typed-transport limitations.

The new **174 scenarios / 5,406 operations** match C# in Debug and Release.
Both full unchanged C# suites pass **35,309 cases**; JavaScript passes **2,905
mirrored original cases** and **933 supplemental tests**, including 25 new manager
tests. The 499-file offline package and both source-regeneration checks pass.
The hosted Ubuntu/Windows Debug/Release matrix passes all four profiles,
including four lifecycle corpora, 107 focused tests and every mirrored case;
[the receipt](doc/section-manager-hosted-9cf0351.json) retains actual reports.

Release inline Chromium executes all **141,186 comparisons** with no manager
mismatch or page error, but **87 earlier-category failures remain**. HTTP-origin
navigation is blocked by the local browser policy, and both complete gates fail.
The ledger is **368/510 library mirrors, 56/193 conformance-file mirrors, and
2,905/35,309 original cases**. Presence does not establish complete behavior.
Typed reading/writing, general profile conversion and other listed work remain
unfinished. The newer retained-polyline documentation was preserved before this
increment was pushed; no unpublished source patch was found or lost.

## Previous retained-polyline checkpoint

`89c7cc8` connects retained Polyline3D, PolygonMesh, PolyfaceMesh and legacy
Polyline2D chains to the typed document registry. `fae312d` supplies independent
qualification and required integration. [Retained polyline contracts and
source-bound results](doc/RETAINED_POLYLINES.md) describe stable child handles,
resource binding, guarded removal and registered 3D vertex editing.

Local Debug and Release each match **181 retained scenarios / 5,419 operations**,
plus **188 ownership scenarios / 8,130 operations** and **112 annotation scenarios
/ 6,633 operations**. The suites pass **2,905 original JavaScript cases**, **908
supplemental tests** and **35,309 unchanged C# cases in each configuration**.
There are 22 new supplemental tests, not additional original-case identities.
The offline-installed package passes with **496 files**. The hosted
Ubuntu/Windows Debug/Release matrix passes all four profiles, including the full
mirrored original suite; its [receipt](doc/retained-polylines-hosted-fae312d.json)
retains all 12 actual differential reports and original-suite metadata/hashes.

Release inline Chromium executes all **141,012 comparisons**, with no retained
case mismatch or page error, but **87 earlier-category failures remain**. Both
full-port gates still fail. The synthetic internal retained fixtures qualify
registration/topology, not typed DXF reading, writing or round-trip fidelity.
Unexecuted or unavailable qualification categories are not counted as passing.

The ledger is **366/510 library mirrors**, **56/193 conformance-file mirrors**,
and **2,905/35,309 original cases**. Presence is not complete API or behavioral
qualification. Earlier [registered annotations](doc/REGISTERED_ANNOTATIONS.md),
[document ownership](doc/DOCUMENT_OWNERSHIP.md), [drawing utilities](doc/DRAWING_UTILITIES.md)
and other reports retain historical results for their named commits.

## Implemented areas and boundaries

The raw layer supports text/binary transport, retained source bytes, immutable
record/tag views, record edits, handle indexing and remapping, dependency
traversal, raw object-store transactions, dictionary trees, extension dictionaries
and draw order. Unsupported private schemas remain opaque. Raw tests do not count
as ports of tests requiring typed DxfDocument authoring or typed IO.

Detached typed APIs include generated geometry and constants, colors, units and
formatting, headers and exact tick adapters, styles, observable collections,
reference accounting, common entity metadata, many primitive/display/curve/surface
entities, hatch boundaries, attributes, blocks/INSERT, groups, MLINE,
layouts/viewports, GEODATA/VBA, dimensions and their source-derived block builder,
multileaders, classic leaders, and tolerance annotations. These do not establish
complete registration for every entity family, typed transport or rendering of
referenced resources. Each model's contract and source-bound evidence is documented in `doc/`.

The typed document core now connects admitted entities, registered tables, blocks,
INSERT attributes, model/paper-space layouts, XData and dimension-generated
blocks. Named-object ownership supports adoption, validation, cloning and guarded
erasure; added lifecycles cover draw order, spatial filters, plot settings,
GEODATA and SUN. Registered MULTILEADER/SECTION now share document ownership,
including mapped SECTION graph cloning and erasure. Layer states support
snapshot/restore and explicit LAS adapters. Retained polyline/mesh chains now
share document registration, stable child identities and dependency guards;
3D retained vertex editing uses the actual document allocator. Typed DXF IO and
remaining specialized stored-entity adoption are still unfinished.

Detailed earlier implementation descriptions remain in the
[historical overview](https://github.com/wieslawsoltes/netDxf/blob/6852f80d3ca39fb38a5af249a339b6dc3d1db55c/javascript/README.md).
Its older coverage numbers are historical, not the current ledger. See also
[architecture](doc/ARCHITECTURE.md), [language adaptations](doc/LANGUAGE_ADAPTATIONS.md),
[verification](doc/VERIFICATION.md), [numerics](doc/NUMERICS.md), and
[filesystem contracts](doc/FILESYSTEM.md).

## Usage

```js
import {
  DxfRawDocument, DxfTag, DrawingTime, HeaderDateTime,
  StringEnum, StringComparison, DxfVersion,
} from './javascript/index.js';

const source = DxfRawDocument.Create([
  [0, 'SECTION'], [2, 'HEADER'], [9, '$ACADVER'], [1, 'AC1032'],
  [0, 'ENDSEC'], [0, 'EOF'],
].map(([code, value]) => new DxfTag(code, value)));
const binary = source.ToBytes(true);
const loaded = DxfRawDocument.Load(binary);
const julian = DrawingTime.ToJulianCalendar(HeaderDateTime.MinValue);
const Versions = StringEnum.For(DxfVersion);
const version = Versions.Parse('ac1032', StringComparison.OrdinalIgnoreCase);
```

The default entry is browser-safe and does not perform filesystem operations
without an explicit host. The Node entry is `@netdxf/javascript/node`; its raw
SaveAtomic stages beside the destination and never uses delete-and-copy fallback.
Windows replacement requires the optional built Node-API host. See
[Windows host qualification](doc/WINDOWS_HOST.md). The package is tested via an
offline install, not published to npm.

## Verification

From `javascript/`, select the exact pinned checkout and toolchain:

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

`test:differential` continues through independent stages after a failure and keeps
each log. Individual `test:retained-polylines`, `test:registered-annotations`,
`test:document-ownership`, `test:drawing-time` and `test:string-enum` commands run
those comparisons. Generated foundations and concrete dimensions are checked using
`verify:native` and `verify:dimension-source`. Browser checks require real
Chromium, not a mocked DOM. `DOTNET_ROOT` or `DOTNET` selects an isolated compiler;
`CHROMIUM` selects a browser executable. See the verification contract for the
additional source-generation, high-precision and performance checks.

`verify:complete` writes the missing-source/test ledger and failed or unavailable
categories before returning failure. Passing focused matrices do not waive
numeric, filesystem, HTTP-origin, performance or full-port requirements.

## Remaining work and license

Complete typed DxfDocument APIs, specialized entity/database ownership, typed
reading/writing, original tests/examples and broad platform/performance
qualification remain unfinished. There are no throwing generated stubs counted as completed mirrors.

Original netDxf code retains Daniel Carvajal's MIT license. The mathematical
adaptations retain LGPL-2.1-or-later, and GTE portions retain Boost Software License
1.0. The aggregate expression is `MIT AND LGPL-2.1-or-later AND BSL-1.0`.
See [third-party notices](THIRD_PARTY_NOTICES.md) and retained preferred sources.
.NET and MPFR are not distributed as production dependencies.
