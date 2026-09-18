# Nonperiodic NURBS active-domain evaluation

This task follows the single-pass fit-input correction. It changes sampling,
not the stored control net, knot vector, weights, creation mode, or DXF dialect.

## Corrected geometry

The preceding nonperiodic sampler used the first and last stored knot as its
sampling interval and appended the last control point unconditionally. Those
are valid endpoint choices for clamped curves, but not general unclamped
B-splines. Outside full basis support it could fabricate origins, and its
unscaled weighted sum could overflow or underflow for finite constant curves.

Nonperiodic sampling now uses **[knots[degree], knots[controls.Length]]**. Open
output includes both evaluated endpoints. The right endpoint of an unclamped
curve is evaluated as the left limit, not copied from its control polygon.
Closed nonperiodic output retains the existing convention of omitting a repeated
terminal sample; closure does not turn its knots into a periodic representation.
Clamped endpoints with nonzero endpoint weights retain exact control components.

A local nonzero-basis recurrence uses at most degree + 1 controls per sample.
The existing periodic implementation supplies mantissa/exponent weighted sums,
coordinate evaluation, and a bounded exact rational fallback. This task does not
introduce another arbitrary-precision engine. Repeated internal knots are legal
through multiplicity degree + 1. Ordinary parameter arithmetic is retained;
overflowing interval width uses convex interpolation instead of subtracting
the endpoints first.

Finite zero and signed weights remain supported, using the shared exact path.
A sampled zero denominator rejects instead of fabricating an origin. This does
not locate every pole between samples or establish a continuously nonsingular
curve. A removable zero-denominator endpoint is not inferred automatically.
Positive-weight samples use scaled accumulation; ill-conditioned basis steps
and unclamped terminal evaluation use exact binary64 input rationals.

## Admission and compatibility

The nonperiodic public evaluator now requires degree 1 through 10, at least
degree + 1 controls, matching weight/knot counts, finite inputs, nondecreasing
knots, legal multiplicities, and positive active-domain length. Null weights
still mean unit weights; null knots still use the existing knot generator.

Requested output is bounded to **2 through 1,000,000** points, using the existing
MaximumConvertedVertices limit, before output allocation. Sampling parameters
must be distinct and representable; a request that rounds adjacent parameters
together rejects rather than returning misleading duplicates. Source arrays
are not modified. This is not a bound on caller-supplied control storage, all
allocation, or total exact-fallback CPU time. Exact rational evaluation retains
the existing per-integer arithmetic budget. No production performance or
universal correctly-rounded positive-weight accumulation claim is made.

These are deliberate library behavior corrections. Callers relying on samples
outside the active domain, forced unclamped endpoints, nonfinite results, or
more than one million nonperiodic samples must adapt. The periodic branch and
its admission rules remain unchanged; the shared exact helper's new signed-
weight option defaults to the previous positive-only behavior for periodic
callers. The spline fitter, standalone general vector APIs, and writer's
existing count-field compatibility policy are not changed.

## Executed focused evidence

The **same 193 new tests** pass **115 before / 193 after** the correction.
The before run uses the exact final harness with the preceding library assembly,
which already contains the unrelated fit-input fix. Unsafe allocation probes
first verify that the new admission check exists; no large allocation is
attempted on the old implementation.

Cases cover unclamped endpoints, signed and zero weights, extreme finite
constant controls/weights, overflowing knot-domain width, repeated knots,
invalid inputs, sampled poles, indistinguishable parameters, and source-array
retention. A fixed corpus spans degrees 1/2/3/5/10, four knot forms, three knot
scales and both endpoint conventions. Text/binary drawings retain the original
SPLINE and an explicitly sampled 3D POLYLINE across six typed profiles.

`tools/verify_spline_active_domain.py` independently reconstructs the corpus and
uses recursive exact Fraction Cox-de Boor basis values across the entire control
polygon, not the production local scaled kernel. It checks **120 scenarios /
1,080 samples** and **48 drawings**, rejects **1,080 numerical** and **3,888
physical coordinate corruptions**, and requires zero independent DXF graph
errors/repairs. Its tolerance is eight ULP of the maximum source magnitude on
each coordinate axis, not eight ULP of a potentially tiny result. Knot and
weight storage and source control coordinates are exact in the physical check.

The checker validates selected geometry and entity/vertex sequencing, not every
metadata field in a whole-document packet. Missing/extra fixture inventories
reject. Full-suite and hosted results are recorded in the PR against their
executed source rather than inferred from this focused result.

```sh
DXF_TEST_FILTER=spline-active-domain/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_spline_active_domain.py artifacts/conformance
```

The harness supports DXF_TEST_ARTIFACTS for isolated output. Pass the same output
directory to the checker.

## Primary references and remaining qualification

The [SciPy BSpline reference](https://docs.scipy.org/doc/scipy/reference/generated/scipy.interpolate.BSpline.html)
defines full basis support on t[k] through t[n]. Michigan Technological
University's [NURBS properties](https://pages.mtu.edu/~shene/COURSES/cs3621/NOTES/spline/NURBS-property.html)
distinguish clamped endpoint interpolation and the nonnegative-weight convex
hull property. Autodesk's [SPLINE DXF reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm)
defines stored knots, weights, controls and fit points. These references do not
establish this library's numerical tolerances or resource limits as native
AutoCAD behavior.

R2000/R2004/R2007/R2010/R2013/R2018 text/binary output is exercised. Historical
typed dialects, native AutoCAD open/AUDIT/save/reopen, visual/font equivalence,
private FIELD/TABLE/cache regeneration, dependency-complete import, general
version conversion, adaptive tessellation, and continuous pole analysis remain
separate work. Synthetic mathematical fixtures are not native producer evidence.
This task does not establish full AutoCAD or all-version DXF parity.
