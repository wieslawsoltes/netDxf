# Shape-preserving SPLINE knot insertion

`Spline.InsertKnot(double parameter, int times = 1)` returns a detached spline
with `times` additional copies of an interior knot. It does not mutate the input,
refit points, tessellate the curve, normalize its knot domain, or raise its degree.
This task follows merged PRs #137–138, whose combined tree is
`a27fdacb620baf066265128d3f7a338a0194bd7a`.

## Contract

The source must be an ordinary nonperiodic SPLINE, with supported degree 1–10,
finite control/fit/tangent/normal data, positive finite weights, consistent array
lengths, nondecreasing finite knots and positive finite tolerances. Clamped and
unclamped knot vectors are supported. The parameter must be strictly inside
`[Knots[Degree], Knots[ControlPoints.Length]]`. `times` is an increment, not a
target multiplicity; the final interior multiplicity may not exceed Degree.
Existing discontinuities elsewhere in the curve are retained.

The output preserves the complete active parameter domain, degree, ordinary
appearance, independent XData, fit-point representation, optional tangents,
tolerances and knot-parameterization preference. It does not reinterpret fitting.
All arrays and mutable appearance are independent of the source and siblings.
The output has no source handle, owner, proxy graphics or private dependency
graph. Associated entities, handle-valued XData, derived SPLINE types (including
HELIX), periodic definitions, and zero/signed weights reject explicitly. The
source is unchanged on both success and failure. No concurrent mutation is allowed.

At most `MaximumRefinedControlPoints = 1_000_000` output control points are admitted
before generated array allocation. Only the affected degree-plus-one controls
enter rational arithmetic. Unaffected control and weight components are copied
bit for bit. Exact homogeneous corner-cutting reuses the existing bounded
`PeriodicSplineExactEvaluation.Rational` engine, retaining cancellation and
avoiding premature overflow of weight-times-coordinate products and knot
interval differences. The evaluator itself is unchanged.

Changed homogeneous coordinates remain exact throughout the complete requested
insertion; projection and weights are then rounded once to binary64, ties to even.
Nonzero projected components that underflow to zero reject. Stored coefficients
still have rounding error: this is mathematical shape preservation before storage,
not bit-exact equality of every sampled point or universal relative accuracy for
arbitrarily ill-conditioned/subnormal weighted representations. The count limit
is not a limit on caller metadata, original storage or all exact-arithmetic cost.

```csharp
using netDxf;
using netDxf.Entities;

var source = new Spline(new[] {
    new Vector3(0, 0, 0), new Vector3(2, 4, 1), new Vector3(5, 1, 0)
}, new[] { 1.0, 0.5, 1.0 }, (short)2, false);
Spline refined = source.InsertKnot(0.5, 2);
// Degree 2, five controls, two additional knots, and no changes to source.
```

## Qualification

The focused conformance module contains 333 cases. It covers all ten degrees,
new and existing knots, multiple insertion, clamped/unclamped and extreme knot
ranges, large/subnormal weights, small/large coordinates, detached/owned and
fit-created sources, metadata isolation, invalid states, and preallocation limits.
It emits 48 drawings over six existing typed profiles in text and binary.

`tools/verify_spline_knot_insertion.py` regenerates 90 source scenarios and uses
repeated single insertion over Python Fraction homogeneous controls, rather than
the production multiple-insertion schedule. It checks stored coefficients exactly
and independently evaluates 1,530 source/result parameter positions with complete
Cox–de Boor bases. The geometric bound is `4e-14` times the largest source
coordinate magnitude, at least the smallest positive binary64 value. The file
check compares complete selected result packets to geometry-derived expectations
and source metadata, excluding identity values while separately checking handle
framing, uniqueness and common ownership. All drawings get independent graph
audits. This is not a comparison of every unselected document record.

Final cumulative test and hosted results are recorded against the actual PR head,
not inferred from this feature description. Run:

```sh
DXF_TEST_FILTER=spline-knot/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_knot_insertion.py artifacts/conformance
```

## References and remaining scope

The construction uses homogeneous NURBS knot insertion as described in the
[MTU course notes](https://pages.mtu.edu/~shene/COURSES/cs3621/NOTES/spline/NURBS/NURBS-knot-insert.html).
The stored knot/weight/control model follows Autodesk's
[SPLINE reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm).
This implementation's admission, rounding and resource contracts are library
choices, not assertions about native AutoCAD exception semantics.

No historical dialect is newly enabled. Native AutoCAD acceptance, private graph
regeneration, dependency-complete imports, generic version conversion, periodic
refinement, signed/zero-weight refinement, knot removal and degree elevation remain
separate work. The separately saved count-output proposal is not included here.
