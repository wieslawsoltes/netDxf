# GVector null safety and robust normalization

The public GTE vector's equality operators previously tested `vec == null` by
calling the same overloaded operator recursively. The result could be process
stack overflow, not a catchable ordinary validation error. Arithmetic, dot
products, length and normalization also passed through those null guards.

Equality now uses reference checks for null. Two null references compare equal;
one-null comparisons differ; inequality is the complement. Typed Equals(null)
is false. Existing non-null component-equality arithmetic is unchanged. This
fix is not a redesign of tolerance-based equality, ordering, hashing, or every
GTE operation, and does not update the JavaScript port's pinned native reference.

The robust normalization branch used the sum of squared scaled components where
the original algorithm requires its square root. It now normalizes by the scaled
Euclidean length. Robust length and normalization divide each component directly
by the maximum absolute component, avoiding an overflowing reciprocal when the
input scale is subnormal. Empty/zero vectors have zero robust length. The positive
normalization branch still replaces the ref argument with a separate vector;
zero vectors retain the previous in-place behavior. General scalar division,
nonrobust arithmetic and nonfinite-input policies are not redesigned here.

For finite nonzero inputs the resulting direction stays finite and unit length
even when the returned mathematical length exceeds binary64 range. Tests cover
3/4/5 vectors at twelve power-of-two scales and four sign combinations, zero and
empty vectors, overflowing lengths, mixed magnitudes, null sentinels, arithmetic,
source aliases and a Gram-Schmidt basis. An independent Python checker derives
all 52 expected inputs separately and calculates norms/components using
2,000-digit Decimal arithmetic. Its declared comparison envelope is 32 ULP;
input bits, dimensions, record inventories and required fields are exact.
Altered observations and incomplete/duplicate/extra inventories must reject.
These are geometry-kernel observations, not synthetic DXF drawings counted as
native application evidence.

The installed-package consumer includes null, arithmetic and finite scaled-norm
assertions so the existing eight packaged-target runtime jobs exercise the actual
shipped implementation. Exact .NET/Windows execution claims belong to the run
receipts, not to these definitions. A baseline stack overflow must be tested in
an isolated process and reported as a process failure, never converted into a
passing or unavailable ordinary C# test case.

Primary references: [C# reference equality with overloaded operators](https://learn.microsoft.com/dotnet/csharp/language-reference/operators/equality-operators)
and [Geometric Tools GVector normalization](https://github.com/davideberly/GeometricTools/blob/master/GTE/Mathematics/GVector.h).
No native AutoCAD open/AUDIT/save/reopen, full GTE numerical equivalence, historical
DXF profiles, private cache regeneration or complete CAD parity is established.
