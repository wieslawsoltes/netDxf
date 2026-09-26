# Database payload and physical-source I/O

Runtime commit: `57ae692059501eb6345fb149171ad07428f148fe`.
Final executable commit: `f1777d4a7cd3714c0ee22b90babb57966d3130c6`.
Executable tree: `68e734818589ca1b659559bbab030080aad84aba`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This increment implements twelve original-path reader/writer partials for
IDBUFFER, SORTENTSTABLE, SPATIAL_FILTER, DATATABLE, LIGHTLIST, LAYER_FILTER,
OBJECT_PTR and LAYER_INDEX, plus physical-source identity and common metadata
observation. These are working payload operations and deferred-reference helpers,
**not complete DxfDocument.Load/Save/SaveAtomic or whole-file reconstruction**.

## Recovery and adapters

The supplied recovery archives reproduced published `0cc4e32` tree
`1294a13097e71f608e67683542ee10d58119e338` exactly. No surviving unpublished
checkout was found. All previously published source, tests and fixtures were
preserved before implementation. Synthetic local commit identities differ from
GitHub; complete runtime and executable trees were compared before publication.

Private C# methods are exposed as explicit JavaScript functions through
`DxfTransport` and `runtime/DatabasePayloadIO.js`. DatabaseIOContext carries the
actual document, accepted physical identities and deferred reference lists;
DatabaseRecord carries a parsed object and its pending header relationships.
The helper adapting ReadDatabaseXData is not counted as a complete mirror of
its original main reader source. All twelve dedicated original paths are counted
as present, not as proof of exhaustive public-member or whole-file parity.

The eighteen JavaScript workflows removed by `b7a572e` remain removed. Original
C# library sources, original tests and shared fixtures are unchanged. No native
DXF engine, .NET runtime or WebAssembly is loaded by the production port.

## Payload grammar and retained private data

IDBUFFER preserves repeated references, explicit null slots and completed binding
before a later resolution failure. SORTENTSTABLE resolves its actual BlockRecord
and graphical entities, retaining redraw keys as values rather than pointers.
Collection uniqueness and block ownership validation use the existing models.
SPATIAL_FILTER validates boundary counts, two-dimensional coordinates, complete
vectors, Boolean fields, optional clip distances and two affine matrices. Writing
preserves field order, negative zero and the completed prefix when a callback fails.

DATATABLE supports all eleven modeled cell types: integers, doubles, strings,
points, vectors, Booleans and five reference/ownership forms. Dimension and total
cell bounds are checked before column allocation. Public fields require the
source order and value grammar. Unsupported versions, cell types and private
extensions retain the entire payload as opaque data, including unbound XData;
malformed recognized public data remains an error, not an opaque-success fallback.
References bind later against accepted source identities. Loaded columns commit
through the actual model's ownership validation; failing one table does not
silently roll back earlier completed tables. Output retains typed groups,
column snapshots, text escaping, reference order and null-handle conventions.

LIGHTLIST retains signed stored versions, ordered LIGHT/name pairs, repeated
references and source failure timing. Its native resolver deliberately uses
registered document identity rather than the stricter accepted-source lookup;
this source distinction is retained, not homogenized. LAYER_FILTER preserves
repeated names and one-pass decoded strings. An empty public OBJECT_PTR remains
distinct from an object carrying private extensions.

LAYER_INDEX preserves timestamp/subclass framing, declared IDBUFFER counts and
reciprocal owned-buffer identity. The undocumented leading group-90 variant
remains opaque instead of receiving an invented interpretation. Output reads the
current buffer counts. CLASS preparation updates compatible declarations and
counts opaque instances without overwriting conflicting private declarations;
a live typed conflict remains an error.

Shared stored headers separate identity, owner, extension dictionaries and
persistent reactors. Unknown header material is retained. Long private headers
are copied iteratively rather than through a spread call that could overflow
the JavaScript argument stack. Tests include 70,000 private header tags.

## Physical-source identity and metadata

The metadata observer recognizes actual record identities only in qualified
TABLES, BLOCKS, ENTITIES and OBJECTS scopes. Generated defaults cannot stand in
for physical source records. DIMSTYLE group 105 is distinguished from ordinary
group 5. Duplicate physical declarations mark earlier retained SourceRecord
objects ambiguous retroactively, so an earlier accepted reference becomes invalid.
Discarded records, absent acceptance proof and same-handle replacement objects
cannot satisfy source-bound lookup.

Nested common control groups track extension and reactor metadata at the native
scope. Once a subclass or XData begins, payload groups are not reinterpreted as
common headers. In particular, an XRECORD payload containing control-group-looking
tags does not create synthetic common object relationships.

Independent comparisons exposed missing UInt64 trailing-NUL behavior in the
new source lookup adapter. Leading zeroes and trailing NUL padding now follow
the unchanged native parser; overflow and invalid lexical forms remain rejected.
This does **not** broaden the raw/code-value DXF handle grammar, whose existing
validation remains unchanged. The source's exact string `"0"` null sentinel is
not replaced everywhere by numeric canonicalization.

The new metadata comparisons also exposed generic primitive cast diagnostics in
the existing text and binary readers. Both now report the native CLR source and
destination type names, for example:

```text
Unable to cast object of type 'System.Int16' to type 'System.String'.
```

The previous reader state remains intact after a failed cast. Messages are
compared directly against the pinned native profile, not normalized or waived.
This does not claim all CLR type-system, localized-message or stream behavior.

## Tests and source-bound evidence

The input-only corpora contain **1,490 scenarios / 17,023 operations**:
**1,070 payload scenarios / 4,701 operations** and **420 physical-metadata
scenarios / 12,322 operations**. They cover six profiles, text/modern/legacy binary,
public/private boundaries, malformed counts, all cell types, Unicode, XData,
reference/ownership failures, CLASS conflicts, callback failures and 80
randomized DATATABLE inputs.

The C# harness invokes unchanged native private methods; the JavaScript harness
invokes production adapters. Payload fixtures explicitly supply source-acceptance
state and do not masquerade as complete typed file admission. Separate metadata
fixtures use real code/value readers and the actual physical record observer.
Snapshots compare exact emitted bytes, models, pending references, source
identity state, registry side effects and errors. Incomplete observations fail.

**52 supplemental tests** were added. Original tests requiring unfinished typed
Load/Save were not shortened, so original-case coverage remains 3,619. Both new
stages are mandatory in aggregate verification and installed-package checks.
All 1,490 new browser inputs are appended after prior inputs. The focused browser
report is marked `scope: database-io-only`, `fullSuite: false` and cannot satisfy
the separate complete browser gates.

## Remaining parity work

Current coverage: **434/510 library mirrors (76 missing)**, **66/193 original
conformance-file mirrors (127 missing)** and **3,619/35,309 original cases
(31,690 missing)**. Presence does not establish complete APIs or behavior.

Complete typed document dispatch, common envelopes and relationship reconstruction,
Load/Save/SaveAtomic, remaining private/TABLE/evaluator I/O, general version
conversion and broad numerical/platform/performance acceptance remain unfinished.
The complete **55-stage aggregate** and full **154,897-check browser suites**
were not rerun at this checkpoint. Hosted/Windows CI, HTTP-origin, MPFR and
performance checks were not run. Older numerical, assertion/recursion and
globalization failures are neither waived nor claimed fixed by these results.
No AutoCAD open/AUDIT/save/reopen fidelity is claimed.

No original C#/fixture edits, restored workflows, comparison removal, tolerance,
expected-failure waiver, merge, force push or npm publication occurred. PR #98
remains draft.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/DATABASE_PAYLOAD_IO.md). Current published scope is maintained in the [README](../README.md).
