# C# to JavaScript migration rules

The intended public spelling is the original spelling: `DxfRawDocument.Load`, `Tags`, `DxfRawObjectStore.Open`, `BeginEdit`, `CreateXRecord`, `CloneDictionaryTree`, `Commit`, and so on. Relative filenames and namespaces-as-directories are retained. Missing typed classes are not represented by generic wrappers or throwing placeholders.

| C# feature | JavaScript representation and current boundary |
|---|---|
| Namespace | Original relative directories and ESM named exports. Public implemented types are also exported from `index.js`. |
| Enum | Frozen named integer object with the same members and values. Flag combinations use the same numeric flags. Generated enum files come from the actual compiled source inventory. |
| `double` | Primitive `Number`; IEEE-754 bits and negative zero are compared exactly. No tolerance in the exact-output gate. |
| `short`, `int` | Integral `Number` within the original range. CLR boxing distinctions between `Single`, `Decimal`, and integral objects cannot be reproduced by one JS primitive. Boxed `Number` objects are rejected where primitive tags are expected. |
| `long`, `ulong` handles | `BigInt`, never a lossy `Number`. Human-readable handles remain strings; numeric comparison/collision checks use `BigInt`. |
| `byte[]` | `Uint8Array`; immutable stored tags defensively copy inputs and returned byte values. |
| Properties | PascalCase fields or ES accessors. Immutable public views are frozen; private implementation fields are not exposed as mutable collections. |
| Overloads/defaults | Explicit argument dispatch and defaults where implemented. No automatic overload selection based on arbitrary duck typing. The detailed typed API overload port remains unfinished. |
| `out` parameter | Mutable `{ value: ... }` result box, for example `DxfGroupCode.TryGetValueType(code, result)`. |
| Indexer | `get_Item(index)`; returned read-only arrays also support JS numeric indexing. Zero-copy slices expose `get_Item`/`at` and iteration. |
| `IReadOnlyList<T>` | Immutable sequence with `Count`, `get_Item`, `GetEnumerator`, and `Symbol.iterator`. A JS iterator is not a CLR `IEnumerator` object. |
| `IDictionary` mapping argument | `Map` or an explicitly supported plain string-keyed object. Handle/name indexes use `Map`, not prototype-bearing property lookup. |
| C# partial class | Same original split filenames, with internal ESM helpers and private state joining them. Those helpers are not public package-root API. |
| `internal` constructors/helpers | Some low-level module exports exist to join mirrored files. Only the documented public package exports are the migration API. JavaScript does not reproduce C# compile-time accessibility. |
| Exceptions | Error subclasses retain .NET names and applicable `ParamName`/`ActualValue`. Differential gates compare exception type/outcome, not incidental stack traces or every exception message. Internal diagnostics are compared where included in the oracle. |
| `CancellationToken` | Token with `ThrowIfCancellationRequested`, or an `AbortSignal`/object with `aborted`. Work is synchronous; cancellation is observed at documented copy/parse/traversal boundaries. |
| `Stream` | Synchronous `Read`/`Write`/capability adapter; included `MemoryStream` retains casing and caller ownership. Raw `Load` additionally accepts JS buffer sources; `ToBytes` is an explicit convenience. Low-level arbitrary external streams and filesystem atomic replacement remain unqualified. |
| `using` / `IDisposable` | Explicit `Dispose` in `finally`, or an API that closes on successful `Commit`. The library does not close caller-owned streams. |
| `CurrentCulture` | Invariant format/parse operations are explicit. JS has no ambient CLR current culture. Locale exercises in mirrored tests document this difference. |
| `StringComparer.OrdinalIgnoreCase` | Scalar mappings qualified against the pinned .NET runtime; no multi-character case expansion and no locale-dependent sorting. |
| `Parallel.For` over shared immutable objects | Interleaved readers within one JS isolate. This does not establish CLR shared-thread equivalence or transferable object identity between workers. |

## Test migration policy

A C# partial `Program` is represented by the same method names contributed by mirrored test modules. Supplemental test adapters live in `TestHarness.js`; they are not counted as original production source. Test names in `Run` are preserved verbatim, including version and boolean spellings.

Do not replace an original typed construction test with a raw fixture under the original name. Such tests remain absent from the completed case set and are enumerated as missing. In this checkpoint, the typed controls in `RawDocumentTests`, `RawRecordTests`, the handle test modules, and most `RawObjectBoundaryTests` methods remain unported. Separate raw fixtures and .NET typed-reader controls provide additional evidence under supplemental categories only.

The source inventory includes original methods/signatures. File presence and a matching method spelling are necessary but not sufficient for parity: value semantics, errors, ownership, serialization, cloning, and side effects still need execution evidence. The observable event/iterator adapter and selected operator-heavy geometry are now implemented; typed entity ownership, the rest of the class hierarchy, and exhaustive generic/BCL semantics still require explicit adaptations. See [typed foundations](TYPED_FOUNDATIONS.md) for the new contracts and the unresolved strict numeric gate.

## Typed source and generic collection adapters

Generated constructor and method bodies keep the original operation order. `CreateOverload(signature, ...args)` explicitly selects a C# numeric overload; `op_*` methods represent source operators; `CloneValue(value)` makes value-type assignment explicit for handwritten JavaScript. Generated source inserts copies using semantic type information, but JavaScript callers do not acquire C# assignment semantics automatically. Generated `$` backing fields and implementation methods are internal compiler details and are not qualified public members.

Observable events expose `.Add(handler)` and `.Remove(handler)` in place of C# `+=`/`-=`. `GetEnumerator` supplies `MoveNext`, `Current`, `Reset`, `Dispose`, and JavaScript iteration with mutation checks. For erased generic value types, pass the type descriptor in the optional second collection constructor argument: `new ObservableCollection(0, 'int')`. It supplies default(T) without inspecting the first runtime item. `RemoveOverload('T', value)` and `RemoveOverload('IEnumerable<T>', values)` resolve item-versus-sequence ambiguity. Ordinary `Remove(value)` preserves the item API.

The current collection differential qualifies integer-list operations and selected comparers, not arbitrary CLR equality/collation. Default culture-sensitive string sorting is deliberately unsupported pending a comparable collation contract. `DxfClassCollection` is not a claim that every inherited `KeyedCollection` overload and null-dispatch case has been exhaustively adapted.

`System.Drawing.Color` is represented by an explicit ARGB component adapter sufficient for the selected `AciColor` conversion methods, not all `System.Drawing` APIs. Geometry display formatting has an invariant default and explicit culture providers; this is separate from the already qualified DXF G17 writer. Exact trigonometric and all-platform display/hash semantics remain blocked by their qualification gates.


## Reconciled values and synchronous filesystem operations

`UnitHelper.ConversionFactor(from,to,fromType,toType)` defaults to the DrawingUnits overload. Supply `ImageUnits` explicitly for an image-unit argument, since integer-valued enums do not preserve their CLR type in JavaScript. All 625 factors come from the actual C# Decimal operations. `XDataRecord` deliberately retains its distinct string, handle, unknown-enum and mutable-byte-array rules; it must not be replaced with the stricter immutable `DxfTag` rules.

`DxfClassCollection` adds `TryGetValue(name, {value:null})`. The nullable DxfClass overloads of Contains/Remove use the optional explicit `DxfClass` selector rather than confusing a null item with a null dictionary key.

The portable `SaveAtomic` method requires an explicit synchronous filesystem host; import the Node entry to register one. The host `FileStream` adapter supports Open/CreateNew/Create and Read/Write/ReadWrite, byte operations, Position/Length, SetLength, Flush and Dispose/Close. It is not every System.IO overload or FileShare/FileOptions combination. Physical destinations never receive a serialization prefix before commit. Existing-file rename and exclusive new-file hard-link publication are qualified as documented in FILESYSTEM.md; no browser filesystem adapter, metadata/ACL parity or power-loss guarantee is implied.
