# OBJECTS record dispatch, physical graph import and output

Runtime commit: `4de4df3fe1967660b74e36576fcbbad2d20b35cd`.
Executable qualification commit: `267d916132aa5921eb5e5032db02ee3656ef6a86`.
Executable tree: `0d5e8fc1a5d4408a1ade4b3f689ca8e7d5532db6`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This is working shared OBJECTS dispatch, relationship reconstruction and object
output, connecting the existing payload codecs. It is **not complete typed
DxfDocument.Load/Save/SaveAtomic**, all-section sequencing or native CAD evaluation.

## APIs and implementation

`DxfReader.Objects.js` implements ReadDatabaseRecord,
ReadDictionaryDatabaseRecord, ReadXRecordDatabaseRecord, ApplyDatabaseMetadata
and ImportDatabaseObjects. The caller supplies a DatabaseIOContext whose chunk
reader observes the actual physical source records, plus the legacy table state
that native document loading constructs before object import.

Dispatch selects existing qualified public and private payload codecs, or retains
unsupported application data as an opaque object. Generic dictionary, dictionary
with default, dictionary variable, placeholder and XRECORD bodies are implemented
directly. XRECORD payload control-looking tags are not mistaken for object-header
metadata. Source identity is captured before advancing to the next record.

Import validates declarations, reserves source and exposed reference handles,
replaces the named root, preserves managed collection identities, assigns owners,
binds dictionary entries/defaults/extensions/reactors, and runs sixteen deferred
resolver phases in the original order. Child-first and randomized record orderings
work without inventing source acceptance for generated defaults. Repeated uses,
alias strength, opaque data and partial state after failure remain observable.

The reader's dictionary and physical-metadata maps are case-insensitive, matching
the actual main-reader initialization. The legacy DictionaryObject entry map is
separately case-sensitive. Three original legacy models are now available:
DictionaryObject, XRecord and XRecordEntry. Their mutable collections and boxed
value identity are not replaced with immutable registered-object types.

`DxfWriter.Objects.js` implements text preflight, common metadata, object envelopes,
payload dispatch, database-string encoding and CLASS preparation. Metadata uses
the canonical registered object when present. Extension output precedes ordered,
case-insensitively deduplicated reactors. Generated legacy root names and typed
entry names retain their distinct source escaping rules. FIELD source validation
runs even when binary transport bypasses text line-break checks.

The writer delegates recognized bodies to the existing original-path codecs.
Private opaque payloads are preserved rather than partially translated. CLASS
preparation retains compatible declarations and instance counts, and rejects
conflicts where the source does. Synchronous callbacks retain the completed output
prefix and source-defined property/encoding observation order.

Two new defects were exposed by independent tests and fixed: inherited JavaScript
property names such as `constructor` must not select a dispatch handler, and a
writer callback that changes the DXF version must affect subsequent outer string
encoding. Object.hasOwn checks and per-operation version reads preserve the native
behavior; expected results were not rewritten.

Five original source paths are added: the two Objects partials and three legacy
models. Selected MLEADERSTYLE and class-preparation helpers are **not counted as
complete MultiLeader, Section or StoredTable partial mirrors**. MLEADERSTYLE
references are queued here; the native later main-reader resolution phase remains
outside this object's import routine and is not claimed completed by these helpers.

## Independent verification and its blocking Debug case

The input-only corpus contains **937 scenarios / 8,715 requested operations**.
It covers text, modern binary and legacy binary; six source profiles; malformed,
private and duplicate records; source-resource admission; ownership and aliases;
common metadata; CLASS declarations; callbacks; enum boundaries; generated root
names; and 32 deterministic randomized record orderings. Native C# private methods
and production JavaScript run independently on identical input.

Forty new supplemental tests include actual text/binary/legacy graph roundtrips,
XData, shared aliases, repeated references, extension trees, callback prefixes and
source-identity rejection. No original typed-file case is shortened to claim more
original coverage. All old comparison inputs are preserved.

Release observes and matches every scenario. Debug matches all **936 fully observed
scenarios / 8,708 operations**, but native TextCodeValueWriter asserts on a null
entry in the automatic-reactor list. The scenario is retained as unavailable
native evidence, not a successful or skipped comparison:

```text
object-graph-io/metadata/automatic/["C0",null]
Process terminated. Assertion failed. Incorrect value type.
netDxf.IO.TextCodeValueWriter.Write(Int16 code, Object value)
netDxf.IO.DxfWriter.WriteDatabaseMetadata(...)
```

JavaScript is not made to abort its host process to imitate a native Debug.Assert.
The Debug differential and focused browser therefore remain failing. Their original
reports include the request and native abort stack. No result is synthesized for
the missing seven operations, and no exception allowlist or tolerance is added.

## Remaining scope

**467/510 library paths (43 missing), 66/193 original test files (127 missing),
3,619/35,309 original cases (31,690 missing).** File presence is not exhaustive
API, signature or behavioral qualification.

Complete typed document dispatch, all-section sequencing and resource reconstruction,
Load/Save/SaveAtomic, remaining private/TABLE/evaluator I/O, general version
conversion, missing original tests/examples and broad platform/performance
acceptance remain unfinished. The full 58-stage aggregate, full browser suites,
hosted/Windows CI, HTTP-origin, MPFR and performance were not rerun here.
Historical numerical and native assertion/recursion/globalization failures remain
unqualified; this increment neither waives nor claims to fix them. No native
AutoCAD open/AUDIT/save/reopen fidelity is claimed. PR #98 stays draft; no merge,
force push, original-source/fixture change or npm publication occurred.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/OBJECT_GRAPH_IO.md). Current published scope is maintained in the [README](../README.md).
