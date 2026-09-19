# Nonperiodic SPLINE subdivision

`Spline.SplitAt(double parameter)` returns two independent splines in traversal
order. It refines the control representation rather than fitting sampled points.
The left and right active intervals remain `[start, parameter]` and
`[parameter, end]`; they are not rescaled to zero through one.

## Admission and geometry

The source must be an ordinary, clamped, nonperiodic control-point SPLINE with
finite positive weights and a supported degree (1–10). The parameter is strictly
inside the active domain. The source validation, dependency rejection and
`MaximumRefinedControlPoints = 1_000_000` budget from
[knot insertion](spline-knot-insertion.md) apply. Additional refinement controls
are budgeted before allocation; this is not a bound on arbitrary metadata or
all exact-arithmetic execution cost.

The operation inserts enough copies of the split parameter to reach degree
multiplicity. It reuses the bounded exact homogeneous insertion helper, retaining
exact intermediate values and rounding each changed stored coefficient once.
The two outputs are clamped at the new endpoint and retain the original degree.
A continuous join has identical endpoint values and weights, but no shared
mutable arrays. An existing degree-plus-one discontinuity instead retains its
distinct left-hand and right-hand endpoints: the operation never bridges a gap.
Stored binary64 values retain ordinary rounding error. Nonzero final coordinates
that underflow to zero reject; arbitrary subnormal varying weights are not a
universal relative-accuracy guarantee.

Fit-created definitions, nonempty fit-point arrays and optional fit tangents
reject. Their subdivision would require a separate definition of how fitting
constraints are partitioned. They are not silently discarded. Unclamped and
periodic splitting, HELIX or other derived entity types, and zero/signed weights
also remain outside this API's admission contract.

Each output has independently copied ordinary appearance and XData, tolerances
and knot-parameterization preference. Source handles, ownership, proxy graphics
and unsupported private dependencies do not transfer. The source remains
unchanged on success and failure. Source mutation by another thread during the
operation is unsupported. The existing Spline endpoint-based closure convention
remains unchanged; no new topological closure inference is introduced.

```csharp
using netDxf;
using netDxf.Entities;

var source = new Spline(new[] {
    new Vector3(0, 0, 0), new Vector3(2, 4, 1), new Vector3(5, 1, 0)
}, new[] { 1.0, 0.5, 1.0 }, (short)2, false);
Spline[] parts = source.SplitAt(0.5);
// parts[0] uses [0, 0.5], parts[1] uses [0.5, 1]. Source is untouched.
```

## Executed focused evidence

The focused module contains 337 cases: degrees 1–10; three split locations;
new/existing knots; extreme knot, coordinate and weight scales; discontinuous
one-sided endpoints; invalid definitions; detached metadata isolation; and 72
text/binary drawings across the six existing typed profiles R2000–R2018.

The independent checker regenerates 240 source scenarios, performs repeated
single insertion using Python Fraction homogeneous controls, and partitions
using the first split-knot index rather than the production last-span indexing.
It compares all resulting controls, weights and knots, then evaluates 8,160
source/part parameter positions using complete rational Cox–de Boor bases. Left
limits are evaluated explicitly at discontinuities. The geometry bound is
`5e-14` times the largest source coordinate magnitude, at least the smallest
positive binary64 value.

The file checker compares complete selected SPLINE result packets to expected
geometry and source metadata. Identity values are excluded from packet equality,
but framing, uniqueness and common ownership are checked separately. All 72
drawings receive independent ezdxf graph audits. The checker rejects 20,664
changed, missing or duplicate packet values and 1,440 numerical coefficient
corruptions. This does not compare every unselected whole-document field or
establish native producer equivalence.

```sh
DXF_TEST_FILTER=spline-split/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_split.py artifacts/conformance
```

Full cumulative and hosted results are recorded separately against actual source
heads. Focused results are not substituted for Windows or netstandard2.0 runs.

## References and remaining scope

The homogeneous insertion construction follows the
[MTU NURBS course notes](https://pages.mtu.edu/~shene/COURSES/cs3621/NOTES/spline/NURBS/NURBS-knot-insert.html).
The stored representation follows Autodesk's
[SPLINE reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm).
The numerical, resource and exception contracts above are explicit library
choices, not claims that AutoCAD uses the same policy.

Native AutoCAD open/AUDIT/save/reopen, historical typed dialects, periodic or
unclamped splitting, signed-weight refinement, knot removal, degree elevation,
private graph regeneration, dependency-complete imports and general version
conversion remain separate work. The saved count-output proposal remains
separate and unpublished. These operations do not establish all-version parity.
