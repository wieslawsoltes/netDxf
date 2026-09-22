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

`361b895` extends typed ownership to registered MULTILEADER and SECTION, including
style adoption, live references, settings, mapped graph cloning and guarded
erasure. [Registered annotation contracts and evidence](doc/REGISTERED_ANNOTATIONS.md)
describe the supported scope and remaining boundaries.

Local Debug and Release match **112 annotation scenarios / 6,633 operations**,
plus the existing **188 ownership scenarios / 8,130 operations** and detached
multileader corpus. The suites pass **2,905 original JavaScript cases**, **886
supplemental tests**, and **35,309 unchanged C# cases in each configuration**.
This continuation adds 24 complete original cases and 19 supplemental tests.
The offline-installed package passes with **490 files**. The expanded hosted
Ubuntu/Windows Debug/Release matrix also passed all four profiles, including the
entire mirrored suite. `5451075` supplies the Windows bridge-build prerequisite;
[the receipt](doc/registered-annotations-hosted-5451075.json) retains the initial
failure and corrected results.

Release inline Chromium executes **140,831 comparisons**, with no new annotation
mismatch or page error, but **87 failures in other categories remain**. Both
full-port gates still fail. The HTTP-origin and Debug browser modes and other
unexecuted qualification categories are not counted as passing.

The ledger is **361/510 library mirrors**, **56/193 conformance-file mirrors**,
and **2,905/35,309 original cases**. Presence is not complete API or behavioral
qualification. Earlier [document ownership](doc/DOCUMENT_OWNERSHIP.md),
[drawing utility](doc/DRAWING_UTILITIES.md) and other reports retain historical
results for their named commits.

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
snapshot/restore and explicit LAS adapters.
Typed DXF IO and specialized stored-entity adoption remain unfinished.

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
each log. Individual `test:registered-annotations`, `test:document-ownership`,
`test:drawing-time` and
`test:string-enum` commands run those comparisons. Generated foundations and concrete dimensions are checked using
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
