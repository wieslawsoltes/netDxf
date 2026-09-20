# Exact parameter-interval trimming for rational SPLINEs

`Spline.Trim(double startParameter, double endParameter)` returns a detached,
clamped rational SPLINE on the requested interval of the original active domain.
The output keeps the source degree and the original parameter values. It is not
sampled refitting, tessellation, implicit parameter normalization, or an in-place
edit. The source, including its arrays, ownership and proxy graphics, is unchanged
on success and failure.

## Endpoint and representation contract

The finite parameters must satisfy
`Knots[Degree] <= startParameter < endParameter <= Knots[ControlPoints.Length]`.
A start at a discontinuity selects the **right-hand limit**; an end at a
discontinuity selects the **left-hand limit**. Internal degree-plus-one breaks
retain separate one-sided endpoints; no line or other segment bridges a gap.
Empty knot intervals do not generate pieces.

The operation supports ordinary nonperiodic, positive-weight control-point
splines of degrees 1–10, including unclamped active endpoints. Output is clamped
at the requested endpoints. Continuous internal joins use degree multiplicity,
while retained discontinuities use degree-plus-one multiplicity. Redundant knots
are not removed. Even trimming the full active domain can therefore produce a
piecewise-Bézier knot vector rather than the original minimum-knot definition.
Inactive exterior knots of an unclamped input are not retained.

Fit-created definitions, stored fit points, optional fit tangents, periodic
curves, zero/signed weights, derived entities such as HELIX, and unsupported
private dependencies reject explicitly rather than losing or reinterpreting
constraints. Existing shared finite/unit-state, count, knot and ownership
validation is retained. Ordinary appearance, independent XData, tolerances and
parameterization preferences use the existing conversion policy. Output does not
inherit source handles, private graphs, or stale proxies. Existing endpoint-based
approximate `Spline.IsClosed` classification is unchanged; this task does not
create an independently stored topological closure flag.

## Exact algebra and bounded generation

For each retained nonempty span, its Bernstein control coefficients are the
source's homogeneous blossom evaluated at the requested left and right bounds.
The original source controls and weights enter exact rational arithmetic;
restriction does not first project a full-span extraction to binary64. This
avoids an additional rounding stage. Final coordinates and weights are projected
and rounded to binary64 once. A whole already-Bézier span retains the existing
bit-preserving extraction fast path. Continuous adjacent spans must agree on the
stored shared endpoint and weight before joining.

The implementation extends the existing local blossom helper with an internal
endpoint overload. Existing extraction and degree-elevation callers pass their
original knot endpoints and retain their algorithms. No second arbitrary-
precision engine, reader/writer rewrite, or general vector arithmetic change is
introduced.

The inherited projection contract rejects a nonzero component that would round
to zero. Exact zero cancellation is allowed. Finite positive stored weights and
nonzero projective coordinates are not a universal condition-number bound:
rounding output coefficients does not guarantee arbitrarily small relative shape
error for every ill-conditioned or varying-subnormal input. The method does not
claim native AutoCAD coefficient identity or universal correct rounding of later
curve evaluation.

Before allocating generated control arrays, the conservative unjoined output
count `retainedSpanCount * (Degree + 1)` must not exceed
`MaximumRefinedControlPoints` (1,000,000), and the estimated blossom blend count
`retainedSpanCount * Degree * (Degree + 1)^2 / 2` must not exceed
`MaximumTrimBlendOperations` (5,000,000). Bounds apply to **retained spans**, not
the discarded interval. Input validation still scans the complete definition.
These are not global bounds on caller-owned storage, metadata copying, process
memory, or all exact-integer CPU cost. The existing exact-integer component
limits still apply.

## Example

```csharp
using netDxf;
using netDxf.Entities;

var source = new Spline(
    new[] { Vector3.Zero, new Vector3(2, 4, 1), new Vector3(5, 1, 0) },
    new[] { 1.0, 0.5, 1.0 }, (short)2, false);
Spline middle = source.Trim(0.25, 0.75);
// middle keeps the parameter interval [0.25, 0.75] and degree 2.
// source is unchanged; metadata and control arrays are not shared.
```

## Regression and independent validation

`SplineTrimTests.cs` adds 809 cases covering degrees 1–10, five interval choices,
clamped/unclamped and mixed/full knot multiplicities, positive extreme weights,
adjacent binary64 knot endpoints, source/metadata isolation, invalid definitions
and parameters, late projection failure, retained-span work bounds and six-
profile text/binary round trips. Its unchanged harness fails all 809 cases when
used with the preceding production DLL without the API, then passes all 809 with
this implementation. No production stub or altered expected value is used.

`tools/verify_spline_trim.py` derives expected coefficients independently from
exact Fraction homogeneous Cox–de Boor source values and solved Bernstein
collocation systems on the requested intervals. It does not repeat the
production blossom recurrence. It checks 180 systems, 3,960 one-sided curve
positions and 216 DXF drawings. Stored coefficients and selected ordered SPLINE
packets compare exactly; identity framing, uniqueness, common ownership and graph
audits are checked separately. One-sided geometric checks admit `8e-14` times
the largest source-coordinate magnitude, with a smallest-positive-binary64 floor.

The focused independent run rejects 540 numerical corruptions, 37,044 actual
ordered-packet changes/omissions/duplications, and two modeled inventory
corruptions. All 216 drawings have zero graph-audit errors or repairs. This is
selected-entity evidence, not whole-document byte equivalence, native producer
qualification, or a tighter unqualified numerical guarantee. Complete-suite,
actual filesystem controls and hosted results are recorded separately against
their executed source.

```sh
DXF_TEST_FILTER=spline-trim/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_trim.py artifacts/conformance
# Full regression and independently maintained file validators:
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance --jobs 4
```

The existing `DXF_TEST_ARTIFACTS` environment variable selects an isolated output
directory; pass the same directory to the verifier. Local net8.0 checks are not a
substitute for Windows or netstandard2.0 execution.

## Remaining DXF qualification

Wire testing covers R2000/R2004/R2007/R2010/R2013/R2018 in text and binary. No
historical typed dialect is newly enabled. Periodic/signed-weight trimming,
minimum-knot output, fitting-constraint subdivision, native AutoCAD
open/AUDIT/save/reopen, private FIELD/TABLE/cache regeneration, dependency-complete
imports and general version conversion remain separate work. Full AutoCAD and
all-version DXF parity is not established by this task.
