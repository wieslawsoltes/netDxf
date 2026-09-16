# Architecture of the native JavaScript port

## Contract and boundaries

The source oracle is the fixed C# tree declared in `baseline.json`, not whatever currently happens to be on `netstandard`. A SHA-256 fingerprint covers the Git blob identities and paths under `netDxf`, `tests`, and `TestDxfDocument`, including support files. Roslyn supplies the source/type/member inventory; JavaScript does not infer the C# API with a regular-expression transpiler. Library files retain relative paths and names, changing only `.cs` to `.js`.

The implementation is native JavaScript. The .NET process appears only in `tools/`, where it acts as an independent behavioral oracle compiled directly from the pinned C# files. The portable import graph from `index.js` has no `node:` imports, network requests, native dependencies, generated-code execution, or .NET bridge. Explicit `node.js` imports the isolated `runtime/NodeFileStream.js` and `NodeFileSystem.js` host adapters; portable modules do not import those files. Browser verification exercises those same modules.

The raw and typed products must remain distinct. `DxfRawDocument` models immutable ordered tags and exact input-byte retention. `DxfRawObjectStore` interprets a qualified subset of OBJECTS schemas and references. Neither is a replacement for the unfinished typed `DxfDocument`, styles/tables/entity-ownership graph, and automatic typed serialization engine. The separate typed-foundation layer now supplies selected geometry and model dependencies; randomized geometry bit equivalence remains unqualified.

## Layers

**Runtime adapters.** `MemoryStream`, immutable list views, typed exceptions, numeric formatting, encoding, and ordinal casing make the unavoidable language differences explicit. This is a small support layer, not a general CLR emulator. Handles and Int64 use `BigInt`; binary64 uses `Number`; byte data uses `Uint8Array` and `DataView`.

**Code/value transport.** The text and binary readers/writers preserve the original type classification, handle kinds, finite-value rules, sentinel checks, legacy group-code framing, and primitive method names. The numeric writer is compared with the actual .NET production writer, not another copy of the same algorithm. Strict legacy code-page tables and Unicode scalar mappings are generated from the pinned runtime, then used without that runtime.

**Raw document.** Loading owns a bounded byte snapshot, resolves the declared version/encoding, and parses ordered immutable tags. Same-transport unedited saving uses retained bytes; edited or cross-transport output uses the native writer. Serialization preflights values and stages the output within the budget before writing to a caller stream. Immutable section/record indexes retain repeated records and unknown sections. A record edit validates snapshot ownership and replaces only its indexed range. Input preservation does not imply typed interpretation of unknown data.

**Raw handles.** Context-sensitive classification separates object identity, owners, pointers, reactors, extension dictionaries, header values, XData, arbitrary handles, and opaque slots. Dictionaries keyed by exact numeric handles back definition/reference lookups. Iterative traversal and owner-cycle detection avoid recursion on long chains. Simultaneous remapping rejects colliding destinations, unresolved-reference capture, ambiguous identities, affected opaque slots, invalid common framing, and HANDSEED overflow.

**Raw OBJECTS.** `DxfRawObjectModel.js` contains the same stored-view classes and option types as its C# counterpart. `DxfRawObjectStore.js` discovers schema-qualified views, retains opaque ones, indexes names/handles, and opens transactions. No private schema is guessed. Dictionary names use .NET-compatible ordinal comparison, not locale comparison or JavaScript's expanding uppercase conversion.

The three C# transaction partial files are mirrored by `DxfRawObjectTransaction.js`, `DxfRawObjectGraph.js`, and `DxfRawObjectCommit.js`. The public transaction holds private state; module-local/internal helpers implement common operations, graph changes, and final commit without exporting mutable state through the package root. Store state is in a private WeakMap. The source document remains immutable.

## Typed source lowering and collection layer

The development-only `NativePort` tool binds the original C# source using Roslyn. Its explicit file selection is lowered to standalone native JavaScript, with no C# interpreter or .NET bridge in the runtime. The source/output manifest retains overload signatures and member mappings. Unsupported constructs stop generation rather than emitting empty implementations. Value-type copying, default initialization, operators, constructor delegation, and indexers have explicit adapters; public API spelling remains PascalCase. Reproduction checks fail on generated-source drift.

`ObservableCollection` preserves the original event and enumeration lifecycle. Subscription snapshots allow handlers to add/remove handlers without changing the current invocation; cancelled insertion and replacement preserve the source event order. Sorting uses an iterative-depth-bounded introsort to retain the .NET integer-list ordering, including ties in qualified comparator tests. `DxfClassCollection` maintains ordered items and maps for unique DXF/CPP names. Generic defaults and overload ambiguities are explicit adaptations rather than implicit guesses.

See [typed foundations and numeric qualification](TYPED_FOUNDATIONS.md). Source-level formula fidelity is not sufficient for exact result bits: a strict randomized corpus currently finds native trigonometric differences. That qualification stays failing and blocks full completion even when the deterministic baseline suite passes.

## Transaction guarantees

Each mutating operation takes an internal savepoint covering staged changes, allocated/reserved handles, allocator position, root identity, and output-setting state. Failed argument validation, exhausted budgets, reentrant mutation during enumeration, or schema/reference errors restore that savepoint. A failed operation does not partially consume its handle allocation. Iterators are closed on failure.

`Commit` builds a new tag sequence while preserving untouched tag objects and ordering. It updates relevant class declarations, handle seed, and sort-order declarations, and validates changed schemas and references before returning a new document. Failure does not mutate the original document; the staging transaction remains available for repair where the original API allows it. A successful commit closes the transaction. An empty transaction returns its original snapshot.

Owned-tree cloning is iterative and maintains an old-to-new handle map. Known pointers are remapped; arbitrary handles and literal strings are not treated as dependencies. Unsupported private/opaque ownership is rejected rather than silently cloned. Deletion checks external incoming references and affected opaque handle slots. Extension-dictionary control groups, default dictionaries, aliases, and draw-order references have explicit handling. These rules reproduce the pinned raw OBJECTS API, not a claim of complete dependency import for all DXF schemas.

## Performance choices and tradeoffs

Stable object layouts, shared immutable tags, cached indexes, `Map`/`Set` membership, exact `BigInt` keys, little-endian `DataView` reads, and index-based work queues avoid repeated parsing and array-shift costs. Dictionary duplicate-name checks are linear; dictionary name lookup uses a precomputed ordinal key and a map. Pure ASCII comparisons use a fast path before Unicode scalar lookup. Clone and owner traversal are iterative; tests include a 1,200-level owned hierarchy and a 12,000-node owner chain.

Raw input buffering and staged output intentionally trade memory for exact-byte retention, defensive ownership, and destination protection before final copying. Binary chunk getters return defensive copies. Transactions retain source snapshots and staged edits; this is not a streaming typed database. These costs are measured rather than hidden.

The benchmark records environment, warmups, samples, median/p95, input cardinality, and process-level memory observations. It is descriptive. There is no demonstrated “faster than .NET” result, cross-machine ranking, or complete regression budget yet. Performance is not allowed to relax exact output, lifecycle, or malformed-input behavior.

## Verification architecture

Original conformance tests, supplemental JS tests, direct .NET differential tests, browser execution, and package smoke tests are different evidence categories. Their counts are never added together as a fabricated original-suite completion percentage. The case-coverage ledger compares exact original test identities and rejects duplicate, renamed, failing, skipped, or TODO cases.

The JSON-lines oracle preserves float bits, Int64 decimal strings, binary data, section/record positions, schema fields, and operation outcomes. Direct output comparison includes both text and binary byte sequences with no handle renumbering, metadata deletion, tolerances, or rounding. Typed .NET writer/reader controls verify some raw JS output, but do not count as a JavaScript typed API implementation.

Evidence binds both production and verifier file bytes to the results. Oracle crash, malformed response, deadline, nonzero exit, code drift, or incomplete enumeration prevents a successful result. Generated files and shared fixture hashes are checked separately. CI has a passing/failing implemented-scope check and a separate full-port completion check that remains blocked during this partial port.

The baseline geometry and collection corpora are also executed by the browser harness using complete .NET-result digests. The randomized geometry qualification is a separate mandatory job; it retains exact failing inputs and is not normalized into a passing implemented-scope count.


## Atomic filesystem host boundary

`DxfRawDocument.AtomicSave.js` mirrors the C# partial file and delegates to `DxfAtomicFile.js`. The latter implements path validation, cancellation checkpoints, sibling staging, real flush, revalidation, publication and cleanup through a captured synchronous host adapter. `node.js` registers the Node host; default browser imports leave filesystem capability unconfigured. Existing caller streams remain owned by callers.

The Node adapter rejects destination symlinks, nonregular files and readonly destinations. It publishes existing destinations with same-directory rename; absent destinations use an exclusive hard-link publication followed by unlinking the staging name, because portable Node does not expose a rename-no-replace primitive. Unsupported publication fails rather than falling back to copying. These are explicit host adaptations, not a general CLR filesystem emulator. See [FILESYSTEM.md](FILESYSTEM.md).

The recovered older source is archived rather than discarded. Its distinct stateful oracle corpus now runs against the canonical NativePort-generated types using exact signature and value-copy adapters. The browser baseline preserves complete original oracle request batches, including static Epsilon mutations, instead of silently resetting state per assertion. Aggregate verification retains all failures and unavailable evidence in its report before failing.
