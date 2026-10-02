# Architecture

## Reference and module boundaries

The port targets the immutable C# reference in [`baseline.json`](../baseline.json),
not the current default branch. Original relative paths are preserved, changing
`.cs` to `.js`. All mapped paths exist, but an exhaustive public-member and
behavior audit is still required; current coverage is maintained in the
[README](../README.md).

Production DXF processing is native JavaScript. `index.js` is the browser-safe
entry. `node-entry.js` explicitly installs `NodeFileStream` and `NodeFileSystem`;
portable modules do not acquire filesystem capability implicitly. The optional
Windows addon performs filesystem replacement only, not DXF or geometry work.
.NET appears in development tools as a source inventory/generation host and an
independent behavioral oracle.

## Runtime and source generation

The runtime supplies synchronous streams, exceptions, collections, encoding,
formatting, ordinal comparison and explicit value-copy adapters. It is not a
CLR emulator. Binary64 values use `Number`, Int64/numeric handles use `BigInt`,
and byte payloads use `Uint8Array` and `DataView`.

`tools/NativePort` uses Roslyn semantic information and explicit source selections
to generate native JavaScript. Manifests record source/output hashes and member
mappings. Unsupported constructs must fail generation rather than produce empty
implementations. Generated bodies and handwritten code both require behavioral
verification. See [language adaptations](LANGUAGE_ADAPTATIONS.md) for overloads,
operators, generic defaults, events and value types.

## Raw and typed document paths

| Layer | Contract |
| --- | --- |
| Code/value streams | Original group-code type classification, strict value parsing, primitive output, stream lifetime and failure state. |
| `DxfRawDocument` | Immutable ordered tags and record/section indexes, with bounded input-byte retention. Unedited same-transport output can reuse the original bytes; edited or cross-transport output serializes tags. |
| Raw handle operations | Context-sensitive identity/owner/pointer classification, numeric handle indexes, collision checks and simultaneous remapping. Literal strings and opaque slots are not guessed to be references. |
| `DxfRawObjectStore` | Schema-qualified stored views and transactions over raw OBJECTS data. Unsupported private schemas remain opaque. |
| Typed `DxfDocument` | Integrated loading/saving, resource tables, blocks/layouts, entities and registered OBJECTS graphs. It reconstructs typed state rather than promising byte-identical whole-file output. |
| File hosts | Explicit synchronous path/stream operations and staged atomic save. Host and filesystem guarantees are narrower than the complete System.IO API. |

Raw preservation is not typed interpretation. A retained private payload is not
an implemented evaluator, renderer or native CAD application. Typed self-roundtrips
can hide matching reader/writer defects and cannot replace native comparisons.

## Typed state and registration

`DxfReader.js` coordinates section parsing, declared handle seeds, resources,
entity admission and deferred references. Source identity is distinct from
identities created for generated defaults. Dedicated record readers preserve
child identities for legacy polylines/meshes and INSERT sequences. Individual
body readers retain different recovery and discard policies; a generic
first-value extraction must not replace those source-defined rules.

`DxfWriter.js` performs typed preflight and output through original-path codecs.
Export refusals, version restrictions, property reads, callback order and source
mutations are part of the contract. Ordinary `Save` is not universally
side-effect-free or transactional. A failing writer can leave a prefix and
partially changed source state where the C# implementation does so.

Registered tables canonicalize resource objects and maintain counted references.
Entities, attribute definitions, INSERT attributes, retained child records and
OBJECTS payloads have distinct ownership/lifecycle paths. Event handlers may
observe partial state. Do not combine paths simply because their field names
look alike. [OBJECTS integration](OBJECT_GRAPH_IO.md) documents graph boundaries.

## Raw transaction guarantees

Raw transactions retain immutable source snapshots and staged changes. Operation
savepoints cover staged records, handle reservations, allocator state and root
identity. Validation or reference failures restore that operation's savepoint.
Commit validates the resulting sequence and produces a new document; it does not
mutate the original raw snapshot. Successful commit closes the transaction.

Owned-tree cloning/remapping and deletion use known ownership/reference schemas.
They must not reinterpret arbitrary values or silently clone unsupported private
ownership. These guarantees apply to the raw transaction API, not every typed
setter, graph operation or file save.

## Performance and verification

The implementation uses shared immutable records, cached indexes, numeric handle
maps and iterative graph walks. Raw input snapshots, defensive byte copies and
staged output deliberately consume memory to preserve ownership and destination
safety. Benchmarks are descriptive unless a separate acceptance budget is met;
correctness and exact comparisons must not be relaxed for speed.

Original conformance, supplemental tests, native differential comparisons,
browser execution, package checks and platform/performance checks are separate
categories. Runtime and verifier fingerprints bind results to executable inputs;
a historical pass does not qualify changed code. See
[verification](VERIFICATION.md), [numerics](NUMERICS.md) and
[filesystem guarantees](FILESYSTEM.md). Full parity remains incomplete.
