# Exact matrix identity in arithmetic

`Matrix3.IsIdentity` and `Matrix4.IsIdentity` remain tolerance-based queries using
`MathHelper.Epsilon`. They are not sufficient predicates for skipping arithmetic:
a tiny shear, translation or scale difference can produce a substantial effect
on large coordinates. Previously multiplication (operators and named methods),
transpose, determinant and inverse treated an approximately identity matrix as
exact identity and discarded those effects.

Both structs now expose `IsIdentityExact`, which checks every stored entry against
one on the diagonal and zero elsewhere. Arithmetic fast paths use only this query.
Signed zeros compare as zero, and exact-identity vector multiplication retains the
existing bit-preserving shortcut, including nonfinite vector components. This does
not add general finite-input validation to matrix operations.

The approximate `IsIdentity` cache now remembers the epsilon used to calculate it
and is recomputed when that global setting changes. Entry mutations retain their
existing cache invalidation. No change is made to general approximate equality,
inverse singularity admission, cofactor arithmetic, projective transform APIs or
public vector normalization. Callers deliberately relying on approximately
identity arithmetic being a no-op will see corrected results. The extra private
cache field changes private struct layout; unsafe/native binary layout is not
qualified. This is not a performance-improvement claim.

## Regression proof

The unchanged final focused module has 82 harness cases. It passes 2 before the
fix and 82 afterward. Seventy-five scenarios perturb every Matrix3/Matrix4 entry
with positive/negative small values and exercise both multiplication API forms,
left/right matrix multiplication, transpose, inverse and determinant. Other cases
check the public query, epsilon cache transitions, signed zero and exact identity.
The numerical emission loop is one harness case, not 75 additional identities.

`tools/verify_matrix_identity.py` independently regenerates all 75 inputs and uses
Python Fraction products for the expected output. All selected product sums have
at most two nonzero terms. Inverse components allow four ULP for the existing
cofactor algorithm; no unit-sized absolute tolerance hides small off-diagonal
terms. Altered actual output components and missing/extra corpus rows must reject.
The full-suite and hosted results are recorded in the task PR against its exact
source. Prior tests are retained rather than replaced by these focused cases.

```sh
DXF_TEST_FILTER=matrix-identity/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_matrix_identity.py artifacts/conformance
```

This corrects reusable arithmetic, not a new DXF dialect. Native AutoCAD numerical
or visual equivalence, all historical typed profiles, general conversion and
complete private FIELD/TABLE regeneration remain unqualified. The separate OCS
arbitrary-axis task uses this correction to avoid losing real small tilts.
