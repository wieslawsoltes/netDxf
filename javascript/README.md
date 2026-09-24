# netDxf JavaScript port

Native ECMAScript modules with original C# relative paths and PascalCase names.
**Full C# parity is incomplete.** Work remains on draft PR #98, branch
`codex/javascript-port`. The package is private and full-parity publication gates
remain enabled. Original C# sources, tests and DXF fixtures are unchanged.

The reference is `3496ab91893a1e4ec9261b4833479f1799149cdc`, with SDK 8.0.425 /
runtime 8.0.31 and Node 22.16.0. Production JavaScript does not load .NET,
WebAssembly, an external DXF engine or a conversion service.

## Local retained-record and section I/O checkpoint (not pushed)

Local commits `cb33807` and `88a5944` add four original SECTIONSETTINGS and
SECTION_MANAGER reader/writer partials and reconstruct qualification for the
retained-object codecs already published at `e494c18`. The actual current remote
source was preserved, including earlier output/GEODATA/SUN work and all eighteen
workflow deletions. No unpublished checkout survived; the complete remote file
tree was restored and verified before editing.

This session exposes no GitHub write actions and direct Git cannot resolve the
host, so these changes are committed locally and supplied as patches, **not
published to the branch**. See the [record I/O contract](doc/RETAINED_RECORD_IO.md)
and [actual local receipt](doc/retained-record-io-local-88a5944.json).

Both Debug and Release match **2,593 scenarios / 5,909 operations**, and both
focused real-Chromium runs match all 2,593 scenario digests without page errors.
All **1,370 supplemental tests**, **3,619 mirrored original cases**, both unchanged
**35,309-case C# suites**, the **616-file offline package**, and all three source
regeneration checks pass. Four preceding regression categories pass both profiles.
The 46 new supplemental tests do not inflate original-case coverage.

Current local presence is **462/510 library mirrors**, **66/193 original test-file
mirrors**, and **3,619/35,309 original cases**. Four library paths are newly added;
24 further paths were already in the newer remote head, beyond the older README.
File presence does not establish complete behavior. Typed document Load/Save,
remaining I/O, original tests and broad qualification are still incomplete.
Both full-parity gates fail. The complete 57-stage aggregate, 159,250-check browser
suites and hosted/Windows CI were not rerun; focused successes do not replace them.

## Database payload and physical-source I/O checkpoint (historical)

`57ae692` adds twelve original-path database reader/writer partials and corrects
primitive cast diagnostics. `f1777d4` requires independent payload, source-identity,
browser and installed-package verification. The [contract](doc/DATABASE_PAYLOAD_IO.md)
covers IDBUFFER, SORTENTSTABLE, SPATIAL_FILTER, DATATABLE, LIGHTLIST, layer filters,
object pointers and layer indexes without claiming complete typed document I/O.

Both configurations match **1,490 scenarios / 17,023 operations** and all
**1,490 focused Chromium comparisons**. Existing reader, primitive, entity-body
and section comparisons also pass both profiles. All **1,276 supplemental tests**,
**3,619 mirrored originals**, both **35,309-case C# suites**, the **588-file
installed package**, and all three source-regeneration checks pass. The
[local receipt](doc/database-io-local-f1777d4.json) preserves the actual evidence.
There are **52 new supplemental tests**, not additional original-case identities.

Coverage is **434/510 library mirrors**, **66/193 original test-file mirrors** and
**3,619/35,309 original cases**. Full-parity verification still fails. The complete
55-stage aggregate and 154,897-check browser suites were not rerun; focused
success does not replace them. Deleted workflows remain deleted; no hosted or
Windows qualification is claimed for this checkpoint.

## Primitive geometry, SPLINE and HELIX checkpoint (historical)

`7df3337` implements body readers/writers for ten primitives plus SPLINE/HELIX;
`02017a3` adds callback/error-order fixes and required verification. The
[transport contract](doc/PRIMITIVE_IO.md) distinguishes these working adapters
from the incomplete full typed document pipeline. Selected main-reader/writer
methods are not counted as whole source mirrors.

Local Debug and Release each match **6,416 scenarios / 25,569 operations**.
Both focused Chromium runs match all **6,416 scenario digests** with no page
errors. The recovered body and prior section corpora also pass in both profiles.
All **1,224 supplemental tests**, **3,619 mirrored originals**, both **35,309-case
unchanged C# suites**, the **573-file installed package**, and all three source
regeneration checks pass. The [receipt](doc/primitive-io-local-02017a3.json) retains
actual source-bound reports. These are local checks, not hosted CI results.

Current presence: **422/510 library files**, **66/193 original test files**, and
**3,619/35,309 original cases**. Both full-parity gates remain failing. The complete
53-stage aggregate and 153,407-check browser suites were not rerun; the focused
browser report cannot satisfy full qualification. Deleted workflows stay deleted.

## Recovered and published entity-body checkpoint (historical)

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
