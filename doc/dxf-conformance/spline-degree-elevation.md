# Rational SPLINE degree elevation

`Spline.ElevateDegree(int times = 1)` returns a detached ordinary SPLINE with
`Degree + times` degree, up to the existing maximum of 10. `times` is positive;
it is not a requested target degree. The source is unchanged. No sampled fitting,
tessellation or automatic parameter normalization is performed.

## Supported representation

As with `ToBezierSegments`, the operation admits ordinary nonperiodic,
strictly-positive-weight control-point definitions. Unclamped input is supported:
only its original active interval is retained, and the result is clamped at its
actual active endpoints. Inactive exterior knots are not retained.

Every nonempty span is elevated in exact homogeneous coordinates, then combined
into one piecewise-Bézier SPLINE. Continuous joins have multiplicity equal to the
target degree; a source multiplicity-degree-plus-one discontinuity has target
degree-plus-one multiplicity and preserves its two distinct one-sided endpoints.
Geometric continuity already present in the curve is preserved mathematically,
but redundant knots are not removed. This is **not minimum-knot degree elevation**:
it can have more controls and a less economical knot vector than another CAD
implementation while representing the same parameterized curve before rounding.

The output retains ordinary independent appearance, XData, tolerances and the
parameterization preference, without source handles, ownership or stale proxies.
Fit-created definitions, retained fit points and optional tangents reject rather
than discarding or reinterpreting fitting constraints. Periodic, zero/signed-weight,
HELIX/derived entities and unsupported private dependencies reject. The operation
does not modify the existing general sampler, public normalization, reader or
writer. The SPLINE count-field proposal remains separate and unapplied.

## Arithmetic and resources

The exact blossom from Bézier extraction feeds the exact homogeneous degree-
elevation recurrence. Neither extracted controls nor intermediate degrees are
rounded. In particular, `i/q` is formed as an exact rational quotient, not from
an already-rounded double quotient. Projection and ties-to-even binary64 rounding
happen only when storing the final control coordinates and weights. Neighboring
continuous spans must agree on their stored endpoint and weight before joining.

The inherited nonzero-to-zero projection rejection and bounded integer engine
apply. Stored coefficients retain ordinary floating-point error; this does not
promise universal relative shape accuracy for ill-conditioned or varying
subnormal weights, exact native AutoCAD numbers, or universal correct rounding
of subsequent curve evaluation.

Before generated allocation, `spanCount * (targetDegree+1)` must not exceed
`MaximumRefinedControlPoints` (1,000,000). This is a conservative bound on the
unjoined Bernstein controls; the final joined result may have fewer controls.
The estimated blend count is the extraction work plus `q-1` per span per degree
increment, bounded by `MaximumDegreeElevationBlendOperations` (5,000,000). Existing integer
component checks also apply. Arbitrary caller metadata/storage and all wall-clock
costs are not globally bounded by those limits.

## Verification

`SplineDegreeElevationTests.cs` adds 277 cases: 170 degree/scale/domain scenarios,
ownership/metadata and source isolation, invalid inputs, adjacent binary64 knot
endpoints, late underflow rejection, output/work bounds, conversion of full and
partial conics, and 72 six-profile text/binary round trips.

The independent checker does not repeat the production elevation recurrence.
It evaluates the source homogeneous polynomial using exact full Fraction
Cox–de Boor bases, solves directly for the target-degree Bernstein coefficients
at rational sites, and independently joins them according to source multiplicity.
Stored coefficients and selected ordered physical packets compare exactly;
separate one-sided curve checks admit `4e-14` times the largest source coordinate
magnitude, with a smallest-positive-binary64 floor. This is not whole-document
comparison or native producer evidence.

The focused run passes 277/277. The independent checker validates 170 systems,
5,814 one-sided curve samples and 72 drawings. It rejects 510 numerical, 23,220
packet and two modeled inventory corruptions, with zero graph errors or repairs.
Complete-suite and before/after execution records are delivered separately.

```csharp
using netDxf.Entities;
Spline cubic = quadratic.ElevateDegree();
Spline degreeFive = quadratic.ElevateDegree(3);
// The quadratic and its parameter interval are unchanged.
```

Reproduction uses the existing conformance and all-verifier runners:

```sh
DXF_TEST_FILTER=spline-degree/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_degree_elevation.py artifacts/conformance
```

Autodesk's stored SPLINE model:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm

Degree elevation through Bézier spans is described by Michigan Technological
University's geometric-computing material:
https://pages.mtu.edu/~shene/COURSES/cs3621/LAB/curve/elevation.html

Wire qualification covers R2000/R2004/R2007/R2010/R2013/R2018 text and binary.
Historical typed dialects, periodic/signed-weight elevation, redundant-knot
removal, native AutoCAD open/AUDIT/save/reopen, private FIELD/TABLE regeneration,
dependency-complete import and general version conversion remain separate work.
This task does not establish full AutoCAD or all-DXF-version parity.

## Recovery and integration

Recovered from the saved degree task `dd8d7bc182b4da343bd8aeb21e36f277e8d76faa` and adapted to the newer, separately published Bezier extractor in PR #141. The extractor and all its 212 cases are retained unchanged. This task reuses its exact `BezierBlossom` and definition checks; its stricter five-million-blend elevation budget does not narrow the existing extraction API. The original 277 degree test identities and independent expectations are retained. Fresh final-source execution, rather than the saved archive alone, qualifies this adaptation.
