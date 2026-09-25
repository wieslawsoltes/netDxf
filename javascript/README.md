# netDxf JavaScript port

Native ECMAScript modules preserving the original C# relative paths and PascalCase
API names. The package is private and PR #98 remains draft. **Full C# parity is
not complete.** Production JavaScript does not require .NET, WebAssembly, an
external DXF engine or a conversion service.

## Published scope

The source reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, recorded in
`baseline.json`: 510 library paths, 193 original conformance-file paths, 399 DXF
fixtures and 35,309 original cases. Development tools pin SDK 8.0.425, .NET 8.0.31
and Node 22.16.0.

The published implementation includes raw text/binary transport, handle operations,
object-store transactions, typed models and document ownership, retained records,
and section/entity/OBJECTS codecs. It contains 467/510 library-path mirrors and
3,619/35,309 original test cases. These counts describe source/test presence,
not exhaustive API or behavioral qualification.

Whole-document typed Load/Save/SaveAtomic, complete resource reconstruction,
remaining original tests/examples, general version conversion and private
TABLE/evaluator behavior still require integration or qualification. Separate
unpublished continuation bundles are not part of this branch. Numerical, browser,
Windows, performance and native AutoCAD qualification remain incomplete.

## Usage and verification

Import the browser-safe entry from `./index.js` or `@netdxf/javascript` when
installed locally. The explicit Node host entry is `@netdxf/javascript/node`;
original-path imports are available under `@netdxf/javascript/netDxf/*`.

```sh
cd javascript
npm test
npm run test:unit
npm run test:package
# With the pinned C# checkout and .NET toolchain available:
npm run inventory
npm run test:differential
npm run verify:complete
```

The full-parity gate must remain failing until every required category passes.
Missing, failed, stale, skipped or unavailable evidence is not success. See
[verification](doc/VERIFICATION.md) for native builds, source regeneration,
browser commands and result locations.

## Engineering references

[Architecture](doc/ARCHITECTURE.md), [language adaptations](doc/LANGUAGE_ADAPTATIONS.md),
[numerics](doc/NUMERICS.md), [filesystem](doc/FILESYSTEM.md),
[Windows host](doc/WINDOWS_HOST.md), [codec contracts](doc/CODEC_STREAMS.md),
and [OBJECTS integration](doc/OBJECT_GRAPH_IO.md) describe implementation boundaries.
Module-specific contracts remain under `doc/`. Historical recovery narratives and
run receipts are available in Git history; they are not current verification
inputs. Fresh reports belong under ignored `artifacts/`, not in source documents.

Original netDxf is MIT licensed; mathematical adaptations retain LGPL-2.1-or-later
and GTE retains BSL-1.0. See [third-party notices](THIRD_PARTY_NOTICES.md), retained
licenses and preferred mathematical sources. Nothing is published to npm.
