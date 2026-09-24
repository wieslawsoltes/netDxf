# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase names.
**Full C# parity is incomplete.** Work remains on draft PR #98, branch
`codex/javascript-port`. The package is private and full-parity publication gates
remain enabled. Original C# sources, tests and DXF fixtures are unchanged.

The reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, with SDK 8.0.425 /
runtime 8.0.31 and Node 22.16.0. Production JavaScript does not load .NET,
WebAssembly, an external DXF engine or a conversion service.

## Recovered and published entity-body checkpoint

The previously unpublished `59f72f6` recovery archive was restored and its complete
file tree reproduced exactly (`7f888a6e67b2cf05c66e34e0bf93c0899742cd15`). The
saved runtime is now published as `2fd90e8`, and its full executable verification
integration as `62c7ea2`, whose tree is exactly the saved executable tree
`5da4c38072e1519e110094f2d27af1c050b8e13d`.

This preserves LIGHT, OLEFRAME/OLE2FRAME, SAT-based ACIS and LWPOLYLINE body codecs,
shared XData handling, source exception order, copied binary packets and required
Node/browser/package checks. The [original saved contract](doc/ENTITY_BODY_IO.md)
is retained unchanged as historical evidence; its account of the earlier blocked
publication describes that earlier attempt, not the current branch status.

Fresh recovery checks pass **31 focused tests** and the complete Release
comparison (**1,107 scenarios / 3,714 operations**). Other results in the saved
contract belong to its recorded executable fingerprint, not newly rerun checks.
The saved ledger is **421/510 library mirrors**, **66/193 conformance-file mirrors**
and **3,619/35,309 original cases**. Source presence is not exhaustive API or
behavioral qualification. Full parity remains blocked.

Commit `b7a572e` removed eighteen branch-specific JavaScript workflows. Those
removals are preserved; no removed workflow is recreated by this continuation.
Local qualification scripts and their strict failure checks remain available.

## Scope

The raw layer supports text/binary transport, retained source bytes, tag/record
views, handle indexing/remapping, object-store transactions and dependency graphs.
It is separate from typed document transport; raw tests are not counted as ports
of tests requiring typed DxfDocument Load/Save.

Typed models and registered ownership cover geometry, resources, headers, XData,
blocks/INSERT, model/paper space, many entity families, dimensions, hatch, groups,
multileaders, sections, retained polylines and named-object lifecycles. Retained
TABLESTYLE/CELLSTYLEMAP, TABLEGEOMETRY, TABLECONTENT/ACAD_TABLE, FIELD/DIMASSOC/
SUNSTUDY and opaque models preserve qualified data and source dependencies.
Their internal construction adapters do not establish complete typed reader
admission, private evaluation, regeneration or native AutoCAD qualification.

Browser-safe `DxfTransport` functions expose actual code/value section and entity
body operations. They do not yet form the complete typed Load/Save/SaveAtomic
pipeline. SAT payload transport does not execute a solid modeler or convert SAB.

## Verification

From `javascript/`, select the pinned source and toolchain:

```sh
export NETDXF_SOURCE_ROOT=/path/to/netDxf-pinned
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:entity-body-io
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/browser-check.py
npm run test:package
npm run verify:complete
```

The aggregate continues independent stages after failures and retains each log.
`verify:native`, `verify:dimension-source` and `verify:gte` check source generation.
Real Chromium is required for browser checks. `DOTNET_ROOT` / `DOTNET` select the
compiler; `CHROMIUM` selects the browser. Missing/stale evidence and exact
numerical differences are blocking, not accepted as successful qualification.

The default entry is browser-safe. The explicit Node entry is
`@netdxf/javascript/node`; Windows atomic replacement requires its optional
Node-API host. See [filesystem contracts](doc/FILESYSTEM.md) and
[Windows qualification](doc/WINDOWS_HOST.md). The package is offline-tested,
not published to npm.

## Contracts and remaining work

[Architecture](doc/ARCHITECTURE.md) · [Language adaptations](doc/LANGUAGE_ADAPTATIONS.md)
· [Verification](doc/VERIFICATION.md) · [Numerics](doc/NUMERICS.md)
· [GTE](doc/GTE_NUMERICS.md) · [Stream codecs](doc/CODEC_STREAMS.md)
· [Section transport](doc/TRANSPORT_SECTIONS.md) · [Entity bodies](doc/ENTITY_BODY_IO.md)
· [Dependencies](doc/STORED_DEPENDENCIES.md) · [Table geometry](doc/TABLE_GEOMETRY.md)
· [Table styles](doc/TABLE_STYLES.md) · [Section manager](doc/SECTION_MANAGER.md)
· [Retained polylines](doc/RETAINED_POLYLINES.md)
· [Registered annotations](doc/REGISTERED_ANNOTATIONS.md)
· [Document ownership](doc/DOCUMENT_OWNERSHIP.md).

Each historical report qualifies only its named source. Complete typed document
transport, missing APIs/original tests/examples, general version conversion,
private evaluation and broad numerical/platform/performance acceptance remain
unfinished. No throwing stubs are counted as complete source mirrors.

Original netDxf remains Daniel Carvajal's MIT code. Mathematical adaptations retain
LGPL-2.1-or-later and GTE retains BSL-1.0; aggregate license:
`MIT AND LGPL-2.1-or-later AND BSL-1.0`. See [third-party notices](THIRD_PARTY_NOTICES.md)
and retained preferred sources. .NET and MPFR are not production dependencies.
