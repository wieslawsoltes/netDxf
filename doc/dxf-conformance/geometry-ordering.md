# Same-size vector and matrix ordering

`GVector` relational operators now compare components lexicographically: the
first unequal component determines the result. Previously `<` and `>` reversed
the comparison and inspected all components, `<` could be true for equal
vectors, `<=` read one past the end, and `>=` rejected equal components.
`GMatrix` delegates its equal-shape comparison to these vector operators,
so it receives the correction without a separate comparison implementation.

Component comparison uses `Double.CompareTo`, consistent with the existing
`Double.Equals`-based geometry equality. Both signed zeros tie; all NaN payloads
tie and precede numeric values; finite values and infinities retain numerical
ordering. This deliberately defines coherent nonfinite ordering rather than
retaining accidental results from the broken comparisons. It is not the raw
IEEE relational treatment of NaN or universal C++ nonfinite behavior. No epsilon
is used, and no input or backing array is changed.

The existing shape policy remains: all four relational operators return false
for unequal vector lengths or unequal matrix row/column counts. Null operands
continue to throw `NullReferenceException`. Matrices compare their stored entry
sequence, not a new layout-independent coordinate ordering. Changing
`GTE.UseRowMajor` does not convert matrix storage. Sorted-container comparers
built from these operators must restrict keys to a common concrete runtime type
and dimension or shape; mutable keys must not be edited while indexed. As before,
relational operators compare coordinates even across subclasses, while `Equals`
retains its exact-runtime-type restriction. No type ordering, `IComparable`
interface or new public signature is introduced.

```csharp
var a = new GVector(new[] { 1.0, 100.0 });
var b = new GVector(new[] { 2.0, -100.0 });
// a < b and a <= b; b > a and b >= a.
// Opposing suffixes do not override the first component.
```

## Verification

The new harness contains 540 exported comparison cases and nine additional API
cases. The input-only corpus covers all pairs of sixteen binary64 patterns,
leading equal components and opposing tails, six vector lengths, and explicit
empty, equal, NaN, signed-zero and crossed-coordinate examples. Each exported
case checks all four relations and both equality operators on independent
vectors and row/column matrices, reverse relations, reflexivity and unchanged
source bits. Additional cases cover mismatched shapes, both storage layouts,
nulls, transitivity, antisymmetry and fixed-dimension sorted-key lookup.

The independent checker regenerates the corpus and derives numeric ordering
from binary64 encodings with integer keys, not the production comparator or
captured expected outputs. It checks inputs, result fields and complete
inventory and rejects boolean mutations, missing fields, changed source-state
flags, extra coordinates, missing/duplicate records and duplicate JSON keys.
Only the explicit `Double.NaN` symbolic fixture permits a runtime-selected NaN
payload; the sixteen-pattern matrix requires its exact input bits.

The shared installed-package consumer adds crossed-coordinate, empty-vector,
NaN/zero-equivalence and matrix-shape assertions before its existing twelve DXF
scenarios. Both ordinary selection and all eight exact-asset runtime profiles
use this same body; their scenario count is not inflated. The two workflow
files are unchanged, and no preceding test or verifier is removed.

Executed local and hosted results belong in the PR/report, not inferred from
these test definitions. The original GTE reference delegates comparison to its
ordered tuple; the chosen .NET NaN contract is explicitly separate. References:

- [Geometric Tools GVector](https://github.com/davideberly/GeometricTools/blob/master/GTE/Mathematics/GVector.h).
- [Microsoft Double comparison](https://github.com/microsoft/referencesource/blob/master/mscorlib/system/double.cs).

This is a geometry-kernel correction, not native AutoCAD open/AUDIT/save/reopen,
complete historical typed dialects, FIELD/TABLE/private cache regeneration,
transactional document operations, or full DXF/AutoCAD parity. The separate
JavaScript port and its pinned comparison reference are unchanged.
