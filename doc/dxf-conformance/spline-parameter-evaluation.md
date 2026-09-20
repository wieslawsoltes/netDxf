# SPLINE point and parameter-derivative evaluation

`Spline.PointAt(parameter, side)` returns a world-coordinate position.
`Spline.EvaluateDerivatives(parameter, order, side)` returns position at index
zero and derivative k at index k, through order ten. Derivatives are with respect
to the original knot parameter: they are not unit directions and do not imply
arc-length parameterization.

```csharp
using netDxf;
using netDxf.Entities;

var spline = new Spline(
    new[] { Vector3.Zero, new Vector3(1, 2, -1) },
    new[] { 1.0, 2.0 }, (short)1);
Vector3 point = spline.PointAt(0.5);
Vector3[] jet = spline.EvaluateDerivatives(0.5, 3);
// This degree-one rational curve has nonzero second and third derivatives.
```

## Domain and representation contract

Nonperiodic degrees 1–10 support clamped or unclamped knot vectors, repeated
knots and degree-plus-one discontinuities. The active domain is
`[Knots[Degree], Knots[ControlPoints.Length]]`. Finite signed and zero weights
are admitted when the denominator at the selected parameter is nonzero.
A zero denominator rejects, including a potentially removable singularity:
this method does not infer a cancellation limit or locate poles between calls.

`SplineParameterSide.Automatic` uses the right-hand limit at an interior knot
and the left-hand limit at the active end. `Left` and `Right` select those limits
explicitly. An outward-facing limit at either domain boundary rejects.
No averaging bridges a discontinuity, and parameters are never clamped,
normalized, extrapolated or wrapped.

Compact periodic curves use the existing `PeriodicSplineData` admission rules:
strictly increasing cyclic extended knots and positive finite weights with the
existing representable weight-range restriction. Their active domain ends at
`Knots[ControlPoints.Length + Degree]`. The compact controls are indexed locally;
evaluation does not allocate a second expanded control polygon. Boundary-side
rules are the same as for nonperiodic curves, even at a periodic seam.

The stored control definition is evaluated. Fit points and optional start/end
fit tangents are not fitting commands and are not regenerated. This also applies
to the stored SPLINE representation inherited by derived entities; it is not
an analytic HELIX evaluator. Layer visibility, XData, extension dictionaries,
normal and proxy contents do not affect rational curve evaluation.

## Arithmetic and mutation

A local homogeneous de Boor recurrence propagates Taylor coefficients.
Formal series division produces rational curve derivatives, including orders
greater than the polynomial degree. The existing bounded exact `Rational`
engine performs all intermediate arithmetic. Each component is rounded once
to binary64, ties to even, by that engine; no new approximate arithmetic
backend or finite-difference step is introduced.

Final overflow rejects. Subnormals are retained. A nonzero result below the
rounding threshold becomes signed zero, while exact zero is positive zero.
These are evaluation semantics, deliberately distinct from editing methods
which may reject projected underflow. No claim is made about native AutoCAD's
rounding choices, parameter conventions for fitting, or visual equivalence.

Returned arrays are independent. Source arrays, identities, ownership, fit
metadata, proxy bytes and configurable `MathHelper.Epsilon` are unchanged.
Every call validates the stored controls, weights and knots, taking linear
time in source size. The exact kernel uses at most eleven local controls and
orders zero through ten under the shared 65,536-byte integer-component budget.
This is not a global process-memory or wall-clock bound. Concurrent writes
to the caller-visible source arrays are unsupported.

## Verification and reproduction

`SplineParameterTests.cs` tests all public derivative orders, rational
derivatives above degree, signed/zero weights, poles, explicit discontinuity
limits, periodic seams, unclamped endpoints, constants, extreme domains,
overflow, malformed input, input identity, owned proxies, fit metadata and
epsilon independence. No existing sampler or fitting algorithm is changed.

The generated corpus contains 300 scenarios over five degrees, four knot
forms, three scale combinations and five parameter locations. The independent
Python checker regenerates the inputs, differentiates *global Cox-de Boor
bases*, and applies the binomial rational-derivative recurrence. It does not
repeat the production local Taylor/de Boor algorithm. All 3,960 component
results compare bit-for-bit with independently rounded Fraction values.

The wire corpus has 48 drawings across R2000/R2004/R2007/R2010/R2013/R2018 in
text and binary. Checks cover stored SPLINE controls/weights/knots/degree and
five evaluated POINT positions, plus independent graph audits. They do not
compare all document metadata or every unselected tag. Mutation controls
change, omit and duplicate selected tags; missing and extra outputs reject.

```sh
DXF_TEST_FILTER=spline-parameter/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_parameters.py artifacts/conformance
```

Final executed test counts and platform results are recorded in the task PR.
A local Linux run is not substituted for hosted Windows/netstandard2.0
qualification. The mathematical corpus is synthetic, not native producer evidence.

## References and remaining scope

Autodesk's [SPLINE group-code reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm)
defines stored degrees, knots, weights and WCS control points. The exact
arithmetic, exception types, side selection and rounding policies here are
library contracts, not claims that Autodesk mandates this implementation.

No new DXF dialect is enabled. Historical typed read/write support, native
AutoCAD open/AUDIT/save/reopen, complete private FIELD/TABLE regeneration,
dependency-complete import, general version conversion and native font/visual
qualification remain separate from this parameter-evaluation feature.
