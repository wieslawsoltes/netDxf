# Remaining exact-identity consumers

A follow-up search after the Matrix3/Matrix4 correction found the same unsafe
fast path in `Matrix2` and in the unclipped `Viewport.TransformBy` branch.

`Matrix2` now exposes the same strict `IsIdentityExact` query and uses it in both
multiplication APIs, transpose, determinant and inverse. Its public approximate
`IsIdentity` still uses the caller's `MathHelper.Epsilon`, with a cache keyed to
that setting. The compatibility, private-layout and numerical boundaries of the
[Matrix3/Matrix4 task](matrix-identity-review.md) apply equally here.

An unclipped viewport may take its translation-only shortcut only for an exact
identity linear matrix. A small real in-plane shear or scale now follows the
existing clipping-polygon transform path rather than translating its center and
dropping the linear part. Exact translations still do not create a boundary.
No new viewport plane, projective, atomic rollback, ownership or proxy-graphics
policy is introduced; general viewport transform behavior remains separate work.

The same 41 focused cases pass 2 before / 41 after these corrections. They include every Matrix2 entry with three perturbations, both
matrix/vector API forms, inverse/transpose/determinant, epsilon-cache transitions,
exact-query/nonfinite checks and signed-zero identity preservation. Viewport model
cases use both transform overloads with small real in-plane linear changes and
large centers; all four resulting boundary corners and exact-translation behavior
are checked. These viewport checks are model-level, not new native/wire evidence.

The independent Matrix2 checker reuses the separately implemented Fraction oracle
from the preceding matrix task. It regenerates 12 scenarios and rejects altered
actual result components and changed corpus lengths. It does not use C# output as
its expected value. Full-suite and hosted results are recorded in the PR against
the exact tested source.

```sh
DXF_TEST_FILTER=identity-consumers/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_matrix2_identity.py artifacts/conformance
```

Native AutoCAD execution, all historical typed DXF profiles, visual/font identity,
private FIELD/TABLE regeneration and general conversion remain unqualified. This
is a bounded continuation of arithmetic correctness, not universal DXF parity.
