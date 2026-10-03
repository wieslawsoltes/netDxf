# netDxf JavaScript port

Native ECMAScript modules preserving netDxf's C# relative paths and PascalCase
API names. **The port is incomplete, the package is private, and PR #98 remains
draft.** DXF processing runs in JavaScript without .NET, WebAssembly, a conversion
service, or an external DXF engine. Node filesystem access is an explicit host
adapter, not a dependency of the browser-safe entry.

## Scope

[`baseline.json`](baseline.json) pins C# commit
`3496ab91893a1e4ec9261b4833479f1799149cdc` and the reference toolchain. Against that
reference, the current source contains **510/510 library-path mirrors**, with
**22,784/35,309 original cases** in **148/193 original conformance-file paths**.
File presence does not establish exhaustive API or behavioral parity. There are
12,525 remaining original cases, including missing cases in present modules.

Implemented areas include raw text/binary transport and handle operations; typed
`DxfDocument.Load/Save/SaveAtomic` and `Block.Create/Load/Save`; geometry and entity
models; tables, blocks and layouts; retained child records; HATCH, MESH, OLE,
MTEXT and annotation transport; and typed/opaque OBJECTS graphs. Individual
contracts and original tests, not this feature list, define the implemented scope.

Read-only database/dependency/TABLE views expose collection flags, lookup and
copy operations, and reject mutations with `NotSupportedException`. SECTION,
manager membership/lifecycle, producer and composite TABLE ownership tests are
registered. The manager's 32 constructor-reflection schema cases remain unported;
functional test coverage is not a substitute for their original reflection assertion.

Opaque-entity output passes the prepared CLASS collection and transport flag in
the correct order; typed input refuses discarded ACDSDATA when opaque entities
need raw preservation. Embedded plot settings require their immediate AcDbLayout
terminator before values or shade references are processed.
Resource lookup includes 17 original cases; three native observer/concurrency/
path-oracle cases remain unported. Public opaque-load checks separately verify
Release null versus Debug exceptions without weakening the raw preservation path.

Remaining work includes the original tests and examples, exhaustive public-member
coverage, native wire/behavior comparisons, version-conversion and private-graph
qualification, allocation/reflection assertions, numerical/platform acceptance,
and the unfinished completion decision in `tools/verify.mjs`. AutoCAD fidelity
and parity with C# changes after the frozen reference are not established.

## Use the source

Node 22 or later is required for the development scripts. From the repository's
`javascript/` directory:

```js
import { DxfDocument, Line, Vector3, MemoryStream } from './index.js';

const document = new DxfDocument();
document.Entities.Add(new Line(Vector3.Zero, new Vector3(10, 20, 0)));
const stream = new MemoryStream();
try {
  if (!document.Save(stream, false)) throw new Error('DXF serialization failed.');
  stream.Position = 0;
  const loaded = DxfDocument.Load(stream);
  if (loaded === null) throw new Error('DXF load failed.');
} finally {
  stream.Dispose(); // Caller owns the stream.
}
```

For a locally installed package, import `@netdxf/javascript`. Original-path
imports are exposed under `@netdxf/javascript/netDxf/*`. Import
`@netdxf/javascript/node` to register the synchronous filesystem host; the
portable entry does not implicitly gain disk access. Existing-file atomic
replacement on Windows additionally requires the optional built Windows host.

## Validate

```sh
cd javascript
npm test
npm run test:unit
npm run test:package
# Requires the exact pinned C# checkout and development toolchain:
npm run inventory
npm run test:differential
npm run verify:complete
```

`npm test` runs the complete **currently mirrored subset**, not every original
C# case. Debug/Release executions and supplemental tests are separate evidence,
not additional original identities. The full-parity command remains failing;
missing, stale, filtered, skipped, failed, or unavailable evidence is not success.
See [verification](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/VERIFICATION.md) for setup, commands and report locations.

## Engineering references

[Architecture](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/ARCHITECTURE.md) ·
[Language/API adaptations](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/LANGUAGE_ADAPTATIONS.md) ·
[Codec and stream contracts](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/CODEC_STREAMS.md) ·
[OBJECTS graphs](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/OBJECT_GRAPH_IO.md) ·
[Numerics](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/NUMERICS.md) ·
[Globalization](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/GLOBALIZATION.md) ·
[Filesystem](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/FILESYSTEM.md) ·
[Windows host](https://github.com/wieslawsoltes/netDxf/blob/codex/javascript-port/javascript/doc/WINDOWS_HOST.md)

Keep generated execution reports in ignored `artifacts/` or external validation
archives. Historical per-module checkpoint reports remain in Git history; they
are not verification inputs. Production manifests, fixtures, mathematical source
files and licenses are required and must not be treated as disposable reports.

Original netDxf is MIT licensed. Mathematical adaptations retain
LGPL-2.1-or-later, and GTE retains BSL-1.0. See
[third-party notices](THIRD_PARTY_NOTICES.md) and the retained licenses and
preferred source files. The package is not published to npm.
