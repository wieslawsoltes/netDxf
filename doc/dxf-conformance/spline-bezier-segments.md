# Rational Bézier-span extraction

`Spline.ToBezierSegments()` returns an independent rational Bézier SPLINE for
each nonempty interval of the original active knot domain. The method does not
sample, refit, change the degree, or rescale the parameter intervals. It supports
ordinary nonperiodic control-point definitions of degree 1–10, with finite
strictly positive weights, including **unclamped** knot vectors.

Repeated knots do not produce zero-length segments. A degree-plus-one break
retains the original distinct one-sided endpoint values instead of bridging a
gap. Adjacent binary64 knot values are supported: extraction does not need a
representable floating-point parameter strictly between them. This does not
change the existing sampler's distinct-parameter admission rule.

```csharp
using netDxf.Entities;
Spline[] spans = source.ToBezierSegments();
// Each span has Degree + 1 controls and clamped copies of its own endpoints.
// source and all output spans have independent arrays and ordinary metadata.
```

## Arithmetic and mutation contract

For each nonempty interval, its homogeneous Bernstein coefficients are obtained
by evaluating the spline's blossom at repeated interval endpoints. Only the
local degree-plus-one control polygon and surrounding knots are used. The
existing bounded Rational type and homogeneous storage conversion are reused.
Coordinates and weights are rounded once at the output boundary. Already
Bézier-form spans copy their original control/weight bits without arithmetic.

The source geometry, array objects, ownership, identity, and proxy graphics are
never changed, including after a late coefficient or metadata failure. Ordinary
appearance, normal, deep XData, tolerances and parameterization preference are
independently retained. Result handles, owners, proxies and private dependencies
are not copied. Source fitting constraints, periodic definitions, derived types
(including HELIX), nonpositive weights and dependency graphs reject explicitly.

The source normal remains auxiliary state; no plane fitting is performed.
Existing endpoint-based `Spline.IsClosed` and serialization policies are not
rewritten. Returned segments are clamped even when the original is unclamped;
only the active domain, not its exterior stored knot extension, is extracted.

The output-control total is bounded by `MaximumRefinedControlPoints` (1,000,000),
and estimated local algebra by `MaximumBasisConversionSteps` (20,000,000).
Both bounds are checked before generated segment/control allocation. This is not
a bound on arbitrary caller metadata, BigInteger runtime, or total process memory.
Existing per-integer exact-arithmetic bounds also remain active. Nonzero
projected components that round to zero reject. Binary64 coefficient rounding
does not guarantee relative shape error for arbitrarily ill-conditioned weights.

## Regression and independent oracle

The initial focused module has 212 tests: 120 geometry cases across ten degrees
and twelve definition/scale families; rejected malformed definitions; independent
metadata/ownership; both output budgets; six-profile text/binary output; and a
numerical corpus. The same final harness fails all 212 cases with the preceding
library (API absent), and passes all 212 with the feature. No production stubs or
changed expectations are used. Dangerous budget tests check API presence first.

`tools/verify_spline_bezier.py` determines expected homogeneous coefficients by
solving a **Bernstein collocation system** with exact Fraction arithmetic. Its
right-hand samples use the full Cox–de Boor basis. It does not reuse the
production blossom, insertion triangle, or floating sampler. The numerical
corpus is independently regenerated: 120 scenarios, 430 extracted spans and
7,310 one-sided parameter comparisons, including discontinuous and adjacent-knot
intervals. Stored projected coefficients compare exactly; geometric comparisons
admit `8e-14` times the largest source coordinate (at least one positive subnormal).

The checker validates 72 drawings over R2000/R2004/R2007/R2010/R2013/R2018 in text
and binary. Selected complete SPLINE result packets are compared to independent
geometry plus retained source metadata. Entity/owner identity values are handled
separately through framing, uniqueness and ownership checks. This is not a
whole-document byte comparison. Every drawing receives an ezdxf graph audit.
37,728 packet corruptions, 1,290 numerical corruptions and two inventory controls
must reject through the same positive validators; zero graph errors or repairs
are allowed. Full-suite and hosted results are recorded against exact PR heads.

```sh
DXF_TEST_FILTER=spline-bezier/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_bezier.py artifacts/conformance
```

## References and boundaries

Autodesk defines the stored spline degree, knots, weights and WCS controls:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm

Michigan Tech describes decomposition into Bézier pieces as a basis for B-spline
and NURBS degree elevation:
https://pages.mtu.edu/~shene/COURSES/cs3621/LAB/curve/elevation.html

The numerical admission and resource bounds are library contracts, not Autodesk
requirements. No new historical DXF dialect is enabled. Native AutoCAD
open/AUDIT/save/reopen, fitting equivalence, private FIELD/TABLE regeneration,
dependency-complete import, general version conversion, and font/visual parity
remain unqualified. These tests do not establish full AutoCAD/all-version parity.
