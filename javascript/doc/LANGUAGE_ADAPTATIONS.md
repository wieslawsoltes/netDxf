# C# to JavaScript migration rules

Public spellings and relative paths follow the pinned C# API. Internal helpers
join partial classes but do not recreate C# compile-time accessibility. A
present file or matching member name does not establish complete semantics.

| C# feature | JavaScript representation and boundary |
| --- | --- |
| Namespace / partial class | Original directories and named ESM exports; helpers join original split files. |
| Enum | Frozen named integer object; erased enum type distinctions may require explicit selectors. |
| `double` | Primitive `Number`; exact gates compare binary64 bits, including negative zero. |
| `short`, `int` | Range-checked integral `Number`; CLR boxing distinctions are not inferred from one JS primitive. |
| `long`, numeric handles | `BigInt`, never a lossy `Number`; human-readable handles remain strings. |
| `byte[]` | `Uint8Array`; ownership is API-specific. Immutable DxfTag copies are not interchangeable with mutable XDataRecord values. |
| Properties | PascalCase fields/accessors and explicit immutable collection views. |
| Overloads | Argument dispatch where implemented; `CreateOverload(signature, ...args)` resolves numeric ambiguity. |
| Operators / value types | `op_*` methods and explicit value copying. Ordinary JS assignment does not acquire C# struct semantics. |
| `out` | Mutable result box such as `{ value: null }`. |
| Indexer | `get_Item` / `set_Item`; some read-only views also support numeric indexing or `at`. |
| Collections | `Count`, explicit enumerators and `Symbol.iterator`; erased generics can require a type descriptor. |
| Events | `.Add(handler)` / `.Remove(handler)`; in-flight subscription snapshots and source callback ordering matter. |
| Exceptions | Error subclasses preserve applicable .NET names, parameters and values; stack/localized-message parity is not implied. |
| Cancellation | A synchronous token callback or AbortSignal checked at the implementation's cancellation boundaries. |
| Streams / disposal | Synchronous adapters and `Dispose` in `finally`; callers retain ownership of supplied streams. |
| Ordinal comparison | Pinned scalar mappings, not locale-sensitive sorting or expanding JavaScript uppercase conversion. |
| Shared-thread tests | Interleaving in one isolate is not qualification of CLR threading or cross-worker identity. |

## Values, collections and callbacks

Generated source uses Roslyn type information to insert value copies and retain
operation order. Handwritten code must make equivalent copies deliberately.
`CloneValue`, explicit vector snapshots and source-order property reads are
important when output callbacks mutate the graph. Generated `$` backing members
are compiler details, not qualified public API.

For a collection whose generic value type is erased, a supported descriptor such
as `new ObservableCollection(0, 'int')` supplies `default(T)` independently of the
first inserted item. `RemoveOverload('T', value)` and
`RemoveOverload('IEnumerable<T>', values)` distinguish item and sequence removal.
Enumerators implement MoveNext/Current/Reset/Dispose with mutation checks.
Default culture-sensitive string sorting is not silently substituted with
JavaScript sorting.

`UnitHelper.ConversionFactor(from, to, fromType, toType)` defaults to DrawingUnits;
pass ImageUnits explicitly for an image-unit argument. Likewise, explicit
selectors distinguish nullable DxfClass arguments from null dictionary keys.
Supported ARGB adapters cover selected AciColor operations, not all System.Drawing.

## Raw, typed and filesystem APIs

Raw immutable tags and typed mutable models have different ownership contracts.
Byte-array, event, source-registration and clone behavior must be ported for each
API rather than generalized from a similar-looking type. Typed and raw framing
errors also remain distinct. See [codec contracts](CODEC_STREAMS.md).

The Node entry installs explicit synchronous file adapters. It does not implement
every System.IO overload, FileShare mode, asynchronous operation or browser file
handle. Atomic destination publication has its own narrower contract, documented
in [FILESYSTEM.md](FILESYSTEM.md).

## Test migration and completion

Original partial Program methods are contributed by original-path test modules;
`TestHarness.js` provides language adapters and is not an original library path.
Preserve original case names, assertions and source tolerances. A shortened raw
fixture test must not replace an original typed construction test under its name.
Incomplete allocation, reflection, platform or threading assertions remain
incomplete original cases, even when supplemental functional checks pass.

Debug/Release executions, supplemental scenarios and independent differential
cases are not added to original-identity coverage. Complete API inventories must
include overloads, inheritance, properties, events, exceptions, value semantics
and side effects. [VERIFICATION.md](VERIFICATION.md) defines evidence categories;
[numerics](NUMERICS.md) and [globalization](GLOBALIZATION.md) describe separate
host-dependent constraints.
