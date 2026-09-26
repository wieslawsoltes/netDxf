# Typed foundations and exact geometry qualification

## Native source implementations

`tools/NativePort/selection.json` selects 29 original C# files. The development-only Roslyn lowerer in `tools/NativePort/Program.cs` emits executable native ES modules at the same relative paths. It walks the semantic model of the complete pinned library, resolves overloads and operators from symbols, and records source hashes, output hashes, constructor signatures, properties, and method mappings in `native-port-manifest.json`. Unsupported syntax or an unresolved operation stops generation before output files are replaced. This is an audited subset lowerer, not a claim of a general-purpose C# compiler.

The selected implementations include `Vector2`, `Vector3`, `Vector4`, `Matrix2`, `Matrix3`, `Matrix4`, `MathHelper`, Bézier curves, bounding/clipping geometry, `AciColor`, `Transparency`, `DxfClass`, `UnitStyleFormat`, hatch-line definitions, mesh edges, tolerance values, header variables, MTEXT formatting/paragraph/background settings, and source constants. The original formulas and operation order are retained. Separate native files implement `ObservableCollection`, its event arguments, and `DxfClassCollection`.

Generate with `node tools/dotnet.mjs native-port`; verify byte-for-byte reproducibility with the same command followed by `--check`. These tools require the pinned development SDK. Consumers import already emitted JavaScript and do not require Roslyn, .NET, WebAssembly, a server, or dynamic code evaluation.

## Calling the adapted APIs

```js
import {
  Vector3, Matrix3, AciColor, CloneValue,
  ObservableCollection, DxfClass, DxfClassCollection,
} from './javascript/index.js';

const point = new Vector3(3, 4, 0);
const copy = CloneValue(point); // C# struct assignment is explicit in handwritten JS.
copy.Normalize();
const translated = Vector3.Add(point, new Vector3(1, 2, 3));
const rgb = AciColor.CreateOverload('byte,byte,byte', 1, 0, 0);
const normalizedRgb = AciColor.CreateOverload('double,double,double', 1, 0, 0);

const items = new ObservableCollection(0, 'int');
items.BeforeAddItem.Add((sender, event) => { event.Cancel = event.Item < 0; });
items.Add(4);
items.Insert(0, 2);
console.log(items.Count, items.get_Item(0));

const classes = new DxfClassCollection();
classes.Add(new DxfClass('CUSTOM', 'VendorCustom', 'Application'));
console.log(classes.get_Item('CUSTOM').CppClassName);
```

Use the original PascalCase properties and methods. Operators use their CLR-style `op_Addition`/`op_Multiply` names; familiar source methods such as `Add` and `Multiply` remain available. Indexers use `get_Item`/`set_Item`; `ref`/`out` parameters use `{ value: ... }` boxes. `CreateOverload` resolves distinctions such as the byte and normalized-double color constructors that one JavaScript numeric primitive cannot express unambiguously. Internal `$` helpers/backing fields are compiler implementation details, not public migration APIs.

Generated code inserts C# value copies at assignment/argument/return boundaries and preserves intentional reference aliases. Handwritten JavaScript still has reference assignment: use `CloneValue` to emulate a C# struct copy. This is particularly important for mutable vectors, cached matrix identity state, and collection/event payloads. Bézier `ControlPoints` deliberately remains an array of mutable vector elements, as in the original C# array contract; it is not presented as a read-only list.

`ObservableCollection` retains cancellation, replacement event order, duplicate subscription/removal behavior, mutation-sensitive enumerators, and the source's nontransactional `AddRange`/`Clear` behavior. The optional second constructor argument records the erased generic element type when a default value is required (for example, an `int` enumerator's initial `Current`). Generic reference collections default to `null`. `RemoveOverload` explicitly selects item versus sequence removal. Default culture-sensitive string sorting, every inherited `KeyedCollection` overload, and arbitrary user-defined generic equality/comparers still require qualification. They are not established by the integer collection corpus.

## Independent evidence, not file-count completion

`geometry-differential.mjs` invokes the actual compiled .NET methods and constructors through a reflection oracle and compares native JavaScript results, every exposed state property, changed arguments, exception types, parameter names, and binary64 bits. Its baseline corpus currently contains 4,254 calls. `collection-differential.mjs` compares 282 integer-collection scenarios containing 5,242 operations, including randomized mutations, cancellation, invalid indexes, enumerator invalidation, and long unstable sorts. Expected values come from .NET, not a second JavaScript implementation.

The newly mirrored original tests are the two standalone CLASS model/collection cases and 13 standalone observable-insertion/removal cases. HATCH-integrated insertion tests and typed-document CLASS I/O cases remain in the missing original-case ledger. Supplemental and differential counts do not increase original-suite coverage.

## Unresolved exact numeric differences

A separate reproducible 2,000-call randomized corpus (`tools/geometry-stress.mjs`, seed 1297) checks `Vector3.AngleBetween` and `Matrix3.RotationZ`. The initial local run found 142 exact-bit mismatches, generally in trigonometric results. The same pinned .NET SDK/runtime does not imply that the operating system's math library and the JavaScript engine use identical trigonometric algorithms. The precise mismatch count must be read from the current platform's execution report.

**The passing baseline is not complete geometry parity.** `npm run test:geometry:exact` is a strict failing qualification while any mismatch remains. It records every request, expected result, actual result, and numeric bit pattern. There is no epsilon, tolerance, ULP allowance, expected-failure list, case filtering, or normalization. The separate CI qualification job is required by full-port completion and is not marked `continue-on-error`.

The current numeric adapters preserve selected C# conversion, rounding, signed-zero, and NaN semantics, but do not solve all native math-library differences. General culture-sensitive formatting and cross-platform hash/casing/math semantics are also not universally qualified. These limits are release blockers, not a reason to omit the tests.

## Remaining typed engine

These foundations are dependencies for the full typed `DxfDocument`, entity ownership/events, tables/styles, complete reader/writer, and original examples. Those layers are not implemented by loading raw records or returning a generic object under a typed class name. Missing source files remain absent, and the full-port gate remains blocked until their implementations and original tests actually run.
