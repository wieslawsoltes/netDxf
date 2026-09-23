# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase API
names. **This is an incomplete port, not full DxfDocument or AutoCAD parity.**
Work remains on draft PR #98, branch `codex/javascript-port`. The package is
private and its publication gate rejects incomplete parity.

The behavioral reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, with SDK
8.0.425 / runtime 8.0.31 and Node 22.16.0. Original C# sources, original tests and
shared DXF fixtures remain unchanged. Production JavaScript does not load .NET,
WebAssembly, a native DXF engine or a server-side conversion service.

## Current checkpoint

`38e48f1` adds preview, common entity metadata, MTEXT background and mesh output
helpers. `688b6d0` adds independent verification, fixes Node Buffer packet aliasing
and requires the new category across package, browser, aggregate and hosted checks.
[Transport contracts and source-bound evidence](doc/TRANSPORT_SECTIONS.md) explain
the supported operations and explicit boundaries. These section helpers are
**not the still-missing complete typed DxfDocument Load/Save pipeline**.

The new **1,165 scenarios / 3,473 operations** match unchanged C# in Debug and
Release. Local checks pass **3,619 mirrored original cases**, **1,153 supplemental
tests**, both **35,309-case C# suites**, a **564-file installed package**, and all
three source-regeneration checks. The increment adds **32 complete original
thumbnail tests** and **29 separately counted supplemental tests**.

The hosted Ubuntu/Windows Debug/Release matrix passes all four profiles, each
running three codec suites, 69 focused tests and every mirrored original case.
Its [receipt](doc/transport-sections-hosted-688b6d0.json) retains all twelve actual
comparison reports and full mirrored-suite metadata/counts/hashes.

All **51 differential stages** were rerun after generating the required source
inventory: **45 pass / 6 fail in Debug**, **44 pass / 7 fail in Release**. Release
inline Chromium executes **145,884 checks**, with no new section failures or page
errors, but retains **119 other failures**. Both full-port gates remain failing.
Unexecuted Debug/HTTP-origin browser, MPFR and performance categories are not
counted as current successes. Older evidence remains tied to its named commit.

The ledger is **416/510 library mirrors**, **66/193 original conformance-file
mirrors**, and **3,619/35,309 original cases**. File presence is not complete API
or behavioral qualification. Numerical mismatches, native assertion/recursion
failures, platform differences and remaining typed IO continue to block parity.

## Implemented areas and boundaries

The raw layer supports text/binary transport, retained source bytes, immutable
tag/record views, record edits, handle indexing/remapping, dependency traversal,
object-store transactions, dictionary trees, extension dictionaries and draw
order. Unsupported private schemas remain opaque. Raw tests do not count as
ports of original cases requiring typed DxfDocument authoring or transport.

Typed APIs include geometry/constants, colors, units/formatting, headers and tick
adapters, styles, observable collections, reference accounting, entity metadata,
many primitive/curve/surface models, hatch boundaries, attributes, blocks/INSERT,
groups, MLINE, layouts/viewports, GEODATA/VBA, dimensions and block generation,
leaders and tolerance annotations. Registered ownership connects admitted models,
resource tables, layouts, attributes and XData. Database lifecycles include
adoption, validation, guarded erasure and explicit graph cloning.

Later increments add registered MULTILEADER/SECTION, retained polyline records,
SECTION_MANAGER, TABLESTYLE/CELLSTYLEMAP, TABLEGEOMETRY, TABLECONTENT/ACAD_TABLE,
FIELD/DIMASSOC/SUNSTUDY dependencies, opaque models, header probes and version
analysis. Retained constructor adapters do not establish typed file admission,
private evaluator behavior or native CAD regeneration. Source-derived GTE modules
have reproducible generation but still fail exact numerical qualification.

New browser-safe `DxfTransport` helpers cover preview sections, common metadata,
MTEXT background and mesh preflight. Their explicit chunk/version/document
arguments adapt private C# partial methods without inventing a second reader.

## Usage

```js
import {
  DxfDocument, DxfVersion, Line, Vector3,
  DxfThumbnailImage, DxfTransport,
} from './javascript/index.js';

const document = new DxfDocument(DxfVersion.AutoCad2018);
const line = new Line(Vector3.Zero, new Vector3(10, 5, 0));
document.Entities.Add(line);
console.assert(document.GetObjectByHandle(line.Handle) === line);
DxfTransport.ValidateEntityCommonDataVersions(document);
DxfTransport.ValidateDocumentMeshOutput(document);

// An explicit code/value writer can receive a complete preview section.
const tags = [];
DxfThumbnailImage.Write({Write: (code, value) => tags.push([code, value])},
  Uint8Array.of(1, 2, 3));
```

`DxfRawDocument` remains the separate text/binary transport API; it is not a
substitute for unfinished typed Load/Save. The default entry is browser-safe and
requires explicit hosts for filesystem operations. The Node entry is
`@netdxf/javascript/node`. Its raw SaveAtomic stages beside the destination and
never falls back to delete-and-copy. Windows replacement requires the optional
Node-API host. See [filesystem contracts](doc/FILESYSTEM.md) and
[Windows host qualification](doc/WINDOWS_HOST.md). The package is offline-tested,
not published to npm.

## Verification

From `javascript/`, select the exact pinned source checkout and toolchain:

```sh
export NETDXF_SOURCE_ROOT=/path/to/netDxf-pinned
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:transport-sections
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/browser-check.py
npm run test:package
npm run verify:complete
```

The aggregate executes independent stages after failures and retains every log.
`verify:native`, `verify:dimension-source` and `verify:gte` check reproducible
source lowering. Browser checks require real Chromium, not a mocked DOM.
`DOTNET_ROOT` / `DOTNET` select an isolated compiler; `CHROMIUM` selects the browser.
See [verification](doc/VERIFICATION.md) for additional numerical and performance
checks. `verify:complete` writes missing coverage and failed/unavailable evidence
before returning failure. Focused green jobs do not waive full-port requirements.

## Historical contracts and license

[Architecture](doc/ARCHITECTURE.md) · [Language adaptations](doc/LANGUAGE_ADAPTATIONS.md)
· [Numerics](doc/NUMERICS.md) · [GTE](doc/GTE_NUMERICS.md)
· [Codecs](doc/CODEC_STREAMS.md) · [Dependencies](doc/STORED_DEPENDENCIES.md)
· [Table geometry](doc/TABLE_GEOMETRY.md) · [Table styles](doc/TABLE_STYLES.md)
· [Section manager](doc/SECTION_MANAGER.md) · [Retained polylines](doc/RETAINED_POLYLINES.md)
· [Registered annotations](doc/REGISTERED_ANNOTATIONS.md)
· [Document ownership](doc/DOCUMENT_OWNERSHIP.md) · [Drawing utilities](doc/DRAWING_UTILITIES.md).
Their results belong to the commits named in each report, not all later builds.

Complete typed IO, missing APIs/original tests/examples and broad qualification
remain unfinished. No generated throwing stubs are counted as complete mirrors.
Original netDxf code retains Daniel Carvajal's MIT license. Mathematical
adaptations retain LGPL-2.1-or-later and GTE retains BSL-1.0; the aggregate license
is `MIT AND LGPL-2.1-or-later AND BSL-1.0`. See
[third-party notices](THIRD_PARTY_NOTICES.md) and retained preferred sources.
.NET and MPFR are not distributed as production dependencies.
