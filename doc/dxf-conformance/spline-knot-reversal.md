# Knot-aware SPLINE reversal

## Corrected contract

`Spline.Reverse()` must reverse parameter direction without changing the curve locus.
For the active interval `[a,b]`, the exact relation is `C_new(t) = C_old(a+b-t)`.
Reversing only control positions and weights does not satisfy this relation for
nonuniform knots. The correction also reflects the full knot vector:

```
a = knots[degree]
b = knots[knotCount-degree-1]
newKnots[i] = a + b - oldKnots[knotCount-1-i]
```

Active-domain endpoints, including nonzero and negative domains, remain fixed.
The reflected values are prepared before mutation; nonfinite, descending, or
unrepresentably reflected knots reject with `InvalidOperationException` and leave
controls, weights, tangents and fit data unchanged. Reflection avoids unnecessary
`a+b` overflow on large same-sign domains. Knot-array identity remains intact.
Ordinary floating-point rounding remains possible; universal bitwise involution
is not claimed.

For netDxf's periodic representation, evaluation/output prepend the last `degree`
stored controls. Therefore reversal rotates the reversed stored polygon and
weights left by `degree`, making the *expanded* polygon exactly the reverse of
the former expanded polygon. Fit positions reverse in order, and end tangents
swap and negate. There is no refitting or knot-domain normalization.

## Profile comparison

| Profile | Typed reversal and text/binary persistence |
|---|---|
| AC1009/R11–R12, AC1012/R13, AC1014/R14 | Typed admission unchanged; separate raw API only |
| AC1015/2000 | Tested |
| AC1018/2004 | Tested |
| AC1021/2007 | Tested |
| AC1024/2010 | Tested |
| AC1027/2013 | Tested |
| AC1032/2018 | Tested |

## Executed evidence and publication status

Base: merged PR #68, `cf533ba32d6c732e475192ee021b78f938eda0cb`,
source tree `34d5583bd5a27aa297dd4c07e5ee03fcec760bdb`.
This increment is **local and unmerged**, not an existing remote PR.

The recovered earlier patch omitted its test registration. Registration was restored
before both unchanged-production and corrected runs reported here.

327 new cases cover linear/quadratic/cubic, open/closed/periodic, three knot domains,
six profiles, both transports, clones, double reversal, nested INSERT/explosion,
failed-operation nonmutation and finite large-domain reflection. They use an
independent rational de Boor evaluator rather than the production evaluator.

Identical tests on unchanged production: **15,593 passed / 326 failed**, Debug
and Release. Corrected production: **15,919 passed / zero failed**, both local
signed-library .NET 8 configurations. This execution is not an SDK/MSBuild,
Windows or netstandard2.0 CI claim.

`verify_spline_reversal.py` uses ezdxf 1.4.4 on 36 retained drawings / 72 splines,
performing 1,188 independent reversed-locus comparisons. All pass with zero
AUDIT errors or repairs. The script explicitly accounts for that evaluator's
normalization of nonzero-origin knot vectors.

## Boundaries and references

This does not certify the complete SPLINE schema, the existing general evaluator,
arbitrary periodic representations, unknown flags, all degenerate knot vectors,
periodic fit-solver behavior, or native AutoCAD interoperability. PR #66 already
admits standard periodic flags for the model's exact-overlap representation;
that merged behavior and the inherited HELIX model from PR #67 are preserved.
Reversing a HELIX's stored spline remains independent of its parameter metadata;
this does not implicitly solve or rewrite a helix constraint system.

The knot/weight/control/fit/tangent fields and standard flags are described by
[Autodesk's SPLINE DXF reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm).
The reversal identity and periodic index transformation above are mathematical
requirements, not additional proprietary DXF flags.
