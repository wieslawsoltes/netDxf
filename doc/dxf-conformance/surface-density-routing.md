# Independent SURFU, SURFV and SPLINESEGS header state

Baseline: `16cb15b34cbc7ccf1ae2467cb795ee8ff47e9d5a`, after merged PR #22.

The HEADER reader assigned both `$SURFU` and `$SURFV` to `DrawingVariables.SplineSegs`. That discarded distinct M/N surface densities and replaced the unrelated spline-patch segment setting according to variable order. Route each value to its corresponding `SurfU` or `SurfV` property. The production correction is exactly two assignments; no serializer, geometry, public API or validation policy is changed.

## Version coverage

| Tested path | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Independent density and spline settings | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| All six variable orders; omitted variables | Tested | Tested | Tested | Tested | Tested | Tested |
| Every reader-admitted density 2..200 | Tested | Tested | Tested | Tested | Tested | Tested |
| Exact group 70 output; cross-transport reload | Tested | Tested | Tested | Tested | Tested | Tested |

108 new registered cases include independent minimal HEADER records, interleaved comments, unequal U/V settings, a distinct spline setting, all six variable-order permutations, omitted/default values, and all 199 values accepted by the existing reader in two alternating-transport cycles. The first minimal test fixture omitted DWGCODEPAGE and hit the existing Debug assertion; adding that declaration corrected the fixture without changing production behavior. Final identical tests: old reader **3,077 passed / 108 failed**; corrected signed library **3,185 passed / 0 failed**, Debug and Release, compiled locally with Roslyn against .NET 8 reference assemblies. Normal Linux/Windows SDK, netstandard2.0 and source-audit CI are additional merge gates.

## Deliberately separate boundaries

The existing reader normalizes values below 2 or above 200 to 6. This remains unchanged here, including 0/1 values accepted by the public property setters; preserving those values safely through downstream mesh-density fallback is separate work. Negative SPLINESEGS semantics and short.MinValue overflow are also not changed. No claim of complete HEADER support, geometry conformance, older file dialects or AutoCAD execution is made.

## Primary specification

Autodesk defines three distinct group-70 variables: `$SPLINESEGS` is the segment count per spline patch; `$SURFU` and `$SURFV` are the PEDIT Smooth densities in M and N directions.

https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
