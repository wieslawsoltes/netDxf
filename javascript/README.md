# netDxf JavaScript port

Native ECMAScript modules, with the original C# relative paths and PascalCase names. **Work in progress: this is not yet a complete port of `DxfDocument` or the original test suite.** Development remains on PR #98. The package is private and its publication gate rejects incomplete parity.

The behavioral baseline is `3496ab91893a1e4ec9261b4833479f1799149cdc`. The original C# implementation and all DXF fixtures remain unchanged. Production JavaScript does not load .NET, WebAssembly, an external DXF library, or a server-side conversion service.

## Current checkpoint

**Observable dictionaries — runtime `6675a4a`, verification `f6ac4dd`:** original-path `ObservableDictionary` and event arguments now include source-ordered cancellation/re-entrancy, reference-sensitive pair removal, copied structs, custom equality, live read-only views and generic enumeration. [Dictionary contracts and source-bound results](doc/OBSERVABLE_DICTIONARIES.md) records 427 exact scenarios in local Debug/Release, 24 added supplemental tests, mandatory Node/browser/package checks and the passing Linux/Windows Debug/Release matrix (run `35647382493`).

The local suite passes **2,820 original JavaScript cases and 750 supplemental tests**; unchanged C# passes **35,309 cases in each configuration**. Inline Chromium executes **137,604 comparisons**, including all dictionary scenarios without mismatches, but retains **75 failures elsewhere**. The ledger is **311/510 library source mirrors and 2,820/35,309 original cases**. File presence is not complete API/behavioral qualification. The full-port gate remains failing and the package remains private.

**Historical drawing-header checkpoint — `c2d3581`:** [header contracts](doc/HEADERS.md) retain the 665-scenario / 6,596-operation checkpoint. Its test/source counts and the historical results below do not qualify later code or supersede the current ledger.

## Implemented scope

The native raw layer includes text/binary codecs, exact unedited same-transport preservation, immutable tag/section/record views, record replacement/removal, contextual handle indexing, dependency traversal, and guarded simultaneous remapping. The OBJECTS layer adds public dictionary/default-dictionary, XRECORD, dictionary-variable, placeholder, ID-buffer, and SORTENTSTABLE schemas; immutable object views; staged edits; owned-tree cloning/deletion; extension dictionaries; draw order; and commit-time graph/handle validation. Unsupported/private schemas remain explicitly opaque.

There are also 77 mirrored enum files, strict code-page tables generated from the pinned .NET runtime, a G17 numeric formatter, exact 64-bit integer/handle storage, and ordinal dictionary-name comparison. See [architecture](doc/ARCHITECTURE.md), [language adaptations](doc/LANGUAGE_ADAPTATIONS.md), and [verification](doc/VERIFICATION.md).

**New typed foundations:** 29 semantically lowered source files now provide vectors, matrices, Bézier/bounding geometry, colors, CLASS metadata, formatting/settings models, and constants. Three collection files provide observable insertion/removal/event behavior and CLASS indexing. See [typed foundations](doc/TYPED_FOUNDATIONS.md) for overload/value-copy adapters and exact verification. The separate exact randomized gate passes the current local corpus after the reference-math reconciliation; that remains distinct from exhaustive geometry/API/platform parity.

**Still incomplete:** the JavaScript typed `DxfDocument`, full entities/tables/styles, typed reader/writer, all original tests/examples, typed filesystem integration and full atomic-save platform semantics, full arbitrary-stream adapters, exact native-math equivalence, and exhaustive platform/performance qualification. A raw byte-preservation or raw OBJECTS test is not counted as a port of a test that constructs the typed JavaScript API.

**Recovered and reconciled:** `UnitHelper`, `XDataRecord`, 625 exact Decimal-derived conversion factors, four original raw test files, and the stateful 5,185-scenario foundations corpus. The newer generated geometry remains canonical; the older complete alternative patch is retained under [recovery](recovery/README.md).

**Raw atomic file saves:** the Node entry adds `DxfRawDocument.SaveAtomic` and a synchronous `FileStream` adapter. Eighty-two original raw/helper cases and direct real-filesystem comparisons are included; typed atomic-save tests remain unported. See [filesystem contracts](doc/FILESYSTEM.md).

**Typed lifecycle:** detached `DxfObject`, `XData`, application registries, name-binding events and cyclic cloning now have native original-path implementations. See [typed lifecycle](doc/TYPED_LIFECYCLE.md) for the exact differential corpus, API adaptations, and remaining registered-document boundaries.

**Hatch patterns and gradients:** detached `HatchPattern` and `HatchGradientPattern` models now include presets, deep clones, independent RGB/ACI metadata, finite tint/shift editing, portable PAT text parsing and an explicit Node append-file host. Fifty-two additional complete original model cases are ported; typed HATCH/document IO is not. See [hatch patterns and PAT contracts](doc/HATCH_PATTERNS.md).

**Styles and primitives:** detached layer/text/shape/linetype models and seven geometric primitives include common metadata, proxy storage, reactors, transforms and clone isolation. The standalone `Polyline2DVertex` retains optional widths and identifiers. See [primitive contracts](doc/PRIMITIVE_ENTITIES.md).

**Text and display models:** `Text`, `Shape`, `Mesh`, `MText` and `MTextColumns` provide detached formatting, layout and transformation APIs. The earlier display batch fixes optimized-browser text-angle storage and adds PDF/DGN/DWF underlay definitions and placement, raster-image definitions and placement, and wipeout boundaries. These models do not open or render referenced files. See [underlays](doc/UNDERLAYS.md), [raster images](doc/RASTER_IMAGES.md), and the [display checkpoint](doc/DISPLAY_CHECKPOINT.md).

**Published numerical continuation:** the earlier local `a14fc00`/`3e46a00` files were published as `593138a`/`e30eea1`. Their reference-math backend and 61,876-case audit remain intact, alongside 30,904 additional exact .NET comparisons and the separate development-only high-precision/MPFR audit. The fixed foundations and randomized-geometry corpora pass in the documented historical CI and current local Debug/Release runs. See [numerical methodology and historical evidence](doc/NUMERICS.md).

**Historical HATCH checkpoint — `b8b1931`:** HATCH boundary/entity verification now covers 1,075 scenarios / 20,990 exact operations, including associations, events, periodic spline conversion and staged affine transforms. The erroneous identity-transform test was corrected to match the pinned C# unlink contract; production behavior was not changed. There are 147 additional complete original cases: **2,782 originals and 479 supplemental tests pass locally**. The full entity stage passes **7,391 scenarios / 57,725 operations** in Debug and Release. Chromium executes all **124,216 inline comparisons** per configuration; existing UCS/Bézier NaN-sign failures remain blocking. The ledger is **246/510 library mirrors, 50/193 conformance-file mirrors and 2,782/35,309 original cases**. [Current HATCH contracts, evidence and remaining failures](doc/HATCH_ENTITIES.md).

**Historical surface checkpoint — `21678de`:** B-spline/NURBS evaluation and polygon-mesh sampling, conversion, retained records and clone semantics remain implemented. [The surface report](doc/POLYGON_MESH_SURFACES.md) retains its completed historical hosted evidence; its counts are not the current ledger.

**Historical HELIX checkpoint — `6fed62c`:** [HELIX and exp/log](doc/HELIX.md) retains the earlier source-bound model, numerical and hosted evidence. Its counts and platform results are historical, not qualification of later commits.

**Historical display checkpoint — `5b63748`:** [the source-bound report](doc/DISPLAY_CHECKPOINT.md) records that earlier Linux Release CI success and its then-current coverage. It does not qualify later commits or replace current numerical, browser and platform results.

## Source layout

| Original | Native JavaScript |
|---|---|
| `netDxf/IO/DxfRawDocument.cs` | `javascript/netDxf/IO/DxfRawDocument.js` |
| `netDxf/IO/DxfRawObjectTransaction.cs` | `javascript/netDxf/IO/DxfRawObjectTransaction.js` |
| `tests/netDxf.Conformance/RawRecordTests.cs` | `javascript/tests/netDxf.Conformance/RawRecordTests.js` |
| `TestDxfDocument/` | Reserved matching `javascript/TestDxfDocument/` paths; examples still unported |
| `tests/fixtures/`, original sample DXFs | Shared unchanged inputs; hashes checked before comparison |

Files that have not been ported are absent and reported as missing. There are no generated throwing stubs masquerading as completed implementations. C# partial classes remain split across their original filenames; private shared implementation state connects those files.

## Native usage

```js
import {
  DxfRawDocument, DxfRawObjectStore, DxfTag,
} from './javascript/index.js';

// Explicit raw authoring, not the not-yet-ported typed DxfDocument factory.
const tags = [
  [0, 'SECTION'], [2, 'HEADER'], [9, '$ACADVER'], [1, 'AC1032'],
  [0, 'ENDSEC'], [0, 'EOF'],
].map(([code, value]) => new DxfTag(code, value));

const source = DxfRawDocument.Create(tags);
const edit = DxfRawObjectStore.Open(source).BeginEdit();
const root = edit.EnsureRootDictionary();
const folder = edit.CreateDictionary(root, 'Application');
edit.CreateVariable(folder, 'Project', 'Zażółć 東京');
edit.CreateXRecord(folder, 'Measurements', [
  new DxfTag(10, 1.2345678901234567),
  new DxfTag(160, 9223372036854775807n),
]);
const updated = edit.Commit();
const bytes = updated.ToBytes(true); // Uint8Array, binary DXF
const loaded = DxfRawObjectStore.Open(DxfRawDocument.Load(bytes));
console.log(loaded.RootDictionary.Find('application').Handle);
```

For existing files, pass a `Uint8Array` to `DxfRawDocument.Load`; browser `await file.arrayBuffer()` is also accepted. File/network access belongs to the caller. `Save(stream, binary, cancellationToken)` keeps the C# method name; the included synchronous `MemoryStream` adapter preserves caller ownership. `ToBytes` is an explicitly documented JavaScript convenience method.

Node uses native ESM. Browser applications can import `javascript/index.js` from an HTTP(S) origin. No bundler or package installation is needed for the source modules. The internal npm tarball is also tested through an offline install; it is **not published**.

## Run the tests

From this directory, with the pinned .NET 8.0.425 SDK / 8.0.31 runtime and Node 22.16.0 available:

```sh
# NETDXF_SOURCE_ROOT must point at the exact pinned C# checkout if the main branch has advanced.
# Example: export NETDXF_SOURCE_ROOT=/path/to/netDxf-pinned
export CONFIGURATION=Release
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs native-port --check
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:differential
node tools/ordinal-casing.mjs --check
node tools/browser-corpus.mjs
python -m pip install -r tools/requirements-browser.txt
python -m playwright install chromium
python tools/browser-check.py
python tools/browser-inline-check.py
npm run test:package
npm run test:math:independent # Installed development MPFR library required.
npm run benchmark
npm run benchmark:math
npm run verify
```

`npm run test:differential` executes every independent comparison and retains each log even when an earlier category fails. It includes a required hatch/PAT model and text comparison stage, the recovered stateful foundations corpus, randomized geometry, filesystem operations and conversion-factor reproduction. The reconciled reference-math, added math, fixed-foundations and randomized-geometry corpora pass locally; other gates still retain failures or unavailable evidence. No tolerance or expected-failure allowlist is applied. Individual scripts `test:reference-math`, `test:math`, `test:exp-log`, `test:nurbs`, `test:coordinates`, `test:surfaces`, `test:database-models`, `test:entities`, `test:styles`, `test:hatch`, `test:foundations`, `test:geometry:exact` and `test:filesystem` remain available. Continue the remaining verification commands after a failure to collect all evidence; CI does so automatically.

`DOTNET_ROOT` or `DOTNET` can select an isolated toolchain. `CHROMIUM` can select an already installed browser executable. Browser checks require a real Chromium process, not a mocked DOM. Repeat with `CONFIGURATION=Debug` to compare with the Debug .NET oracle.

`npm run verify` writes the missing-source/original-test ledger and every negative or unavailable evidence category before returning failure. It does not describe the enlarged implementation as qualified while exact foundations or geometry checks fail. **`npm run verify:complete` fails while the full-port gates remain unmet.** A successful subset does not turn the completion gate green. See [the verification contract](doc/VERIFICATION.md) for report locations and the distinction between preserved input bytes, normalized writer bytes, object semantics, and typed-reader controls.

## Node filesystem usage

After an internal/offline package installation, import the explicit Node entry:

```js
import { DxfRawDocument, FileStream } from '@netdxf/javascript/node';
const input = new FileStream('input.dxf');
try {
  const document = DxfRawDocument.Load(input);
  document.SaveAtomic('output.dxf'); // Same transport, exact retained source bytes.
  document.SaveAtomic('output-binary.dxf', true);
} finally {
  input.Dispose(); // The library does not close the caller's input stream.
}
```

Existing-file replacement on Windows requires the separately built, optional Node-API host; run `npm run build:windows-host` before filesystem tests or packing for Windows. An unbuilt Windows package rejects replacement without touching the destination. See [Windows host requirements and tests](doc/WINDOWS_HOST.md). The portable DXF runtime remains JavaScript.

The default entry stays browser-safe and rejects filesystem operations without a registered host. Node publication is synchronous and stages beside the destination; it never uses delete-and-copy fallback. This does not establish complete System.IO parity, metadata retention, concurrent-writer isolation, or power-loss durability.

## License

Original netDxf code remains MIT, copyright Daniel Carvajal. The pinned production reference-math modules retain their upstream LGPL-2.1-or-later material; the GTE surface code also retains Boost Software License 1.0. Package metadata is `MIT AND LGPL-2.1-or-later AND BSL-1.0`. See [third-party notices](THIRD_PARTY_NOTICES.md) and the included preferred sources. The separate high-precision development reference is independently written MIT code and is not imported by the package entry. .NET and MPFR are not distributed as runtime dependencies.
