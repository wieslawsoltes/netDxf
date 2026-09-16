# netDxf JavaScript port

Native ECMAScript modules, with the original C# relative paths and PascalCase names. **Work in progress: this is not yet a complete port of `DxfDocument` or the original test suite.** Development remains on PR #98. The package is private and its publication gate rejects incomplete parity.

The behavioral baseline is `3496ab91893a1e4ec9261b4833479f1799149cdc`. The original C# implementation and all DXF fixtures remain unchanged. Production JavaScript does not load .NET, WebAssembly, an external DXF library, or a server-side conversion service.

## Implemented scope

The native raw layer includes text/binary codecs, exact unedited same-transport preservation, immutable tag/section/record views, record replacement/removal, contextual handle indexing, dependency traversal, and guarded simultaneous remapping. The OBJECTS layer adds public dictionary/default-dictionary, XRECORD, dictionary-variable, placeholder, ID-buffer, and SORTENTSTABLE schemas; immutable object views; staged edits; owned-tree cloning/deletion; extension dictionaries; draw order; and commit-time graph/handle validation. Unsupported/private schemas remain explicitly opaque.

There are also 77 mirrored enum files, strict code-page tables generated from the pinned .NET runtime, a G17 numeric formatter, exact 64-bit integer/handle storage, and ordinal dictionary-name comparison. See [architecture](doc/ARCHITECTURE.md), [language adaptations](doc/LANGUAGE_ADAPTATIONS.md), and [verification](doc/VERIFICATION.md).

**New typed foundations:** 29 semantically lowered source files now provide vectors, matrices, Bézier/bounding geometry, colors, CLASS metadata, formatting/settings models, and constants. Three collection files provide observable insertion/removal/event behavior and CLASS indexing. See [typed foundations](doc/TYPED_FOUNDATIONS.md) for overload/value-copy adapters and exact verification. A separate randomized geometry gate currently exposes unresolved trigonometric bit differences; the passing baseline must not be described as full geometry parity.

**Still incomplete:** the JavaScript typed `DxfDocument`, full entities/tables/styles, typed reader/writer, all original tests/examples, typed filesystem integration and full atomic-save platform semantics, full arbitrary-stream adapters, exact native-math equivalence, and exhaustive platform/performance qualification. A raw byte-preservation or raw OBJECTS test is not counted as a port of a test that constructs the typed JavaScript API.

**Recovered and reconciled:** `UnitHelper`, `XDataRecord`, 625 exact Decimal-derived conversion factors, four original raw test files, and the stateful 5,185-scenario foundations corpus. The newer generated geometry remains canonical; the older complete alternative patch is retained under [recovery](recovery/README.md).

**Raw atomic file saves:** the Node entry adds `DxfRawDocument.SaveAtomic` and a synchronous `FileStream` adapter. Eighty-two original raw/helper cases and direct real-filesystem comparisons are included; typed atomic-save tests remain unported. See [filesystem contracts](doc/FILESYSTEM.md).

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
npm run test:package
npm run benchmark
npm run verify
```

`npm run test:differential` executes every independent comparison and retains each log even when an earlier category fails. It includes the recovered stateful foundations corpus, randomized geometry, filesystem operations and conversion-factor reproduction. The exact numeric categories currently **fail** with counterexamples; no tolerance or expected-failure allowlist is applied. Individual scripts `test:foundations`, `test:geometry:exact` and `test:filesystem` remain available. Continue the remaining verification commands after a failure to collect all evidence; CI does so automatically.

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

The default entry stays browser-safe and rejects filesystem operations without a registered host. Node publication is synchronous and stages beside the destination; it never uses delete-and-copy fallback. This does not establish complete System.IO parity, metadata retention, concurrent-writer isolation, or power-loss durability.

## License

MIT; original netDxf copyright Daniel Carvajal. The port retains the original license and attribution. Generated .NET-derived development tables are committed native JavaScript data; .NET itself is not distributed in the package.
