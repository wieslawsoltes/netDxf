# SPLINE tangent transformation

## Defect and mathematical contract

`Spline.TransformBy` transformed control/fit points but left authored start/end tangents unchanged. This also affected INSERT explosion, which uses the same transformation API. A tangent to an affine-transformed parametric curve obeys `d(M p(t) + b)/dt = M p'(t)`: apply the linear matrix, **not** its translation and not inverse-transpose normal transformation. Preserve magnitude, null versus explicit zero, and the existing point/normal/parameterization policies.

The change updates only the two nullable tangent fields inside `TransformBy`. It neither reconstructs curves nor evaluates fit constraints. Singular matrices retain the existing permissive behavior; an explicit tangent may become zero. No new finite-number policy, generic normal fix, spline grammar validation or hatch-edge metadata support is claimed.

## Version comparison

| Family | Typed operation | Text / binary |
|---|---|---|
| R11/R12 / AC1009, R13 / AC1012, R14 / AC1014 | Still not admitted to `DxfDocument` | Separate raw preservation unchanged |
| 2000 / AC1015 | Correct linear tangent transform | Original/clone round trips tested |
| 2004 / AC1018 | Same | Same |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

## Executed evidence

Baseline: merged PR55 `3dd5bf9f52626f9f428f9ad2226852fd9a7eeb65`, tree `ff227d3af1dde2be53e6b0d322469d5b5800b289`.

248 new cases cover six typed profiles, both transports, all four optional-tangent combinations and five matrices: identity plus translation, quarter-turn, nonuniform scale/reflection, three-dimensional shear and zero. Additional fit-created spline and cloned INSERT explosion cases verify sequential transforms, source isolation and vector semantics. Geometry expectations are independently calculated affine products, not values taken from the implementation under test.

Identical tests on unchanged signed production: **12,013 passed / 150 failed in both Debug and Release**. Corrected signed production: **12,163 passed / zero failures in both configurations**, local .NET 8. The separate documentation suite has 15 tests. The final CI also validates Linux/Windows SDK builds, netstandard2.0 and source audit.

The independent ezdxf 1.4.4 verifier checks twelve retained files / twenty-four original-and-clone splines: exact transformed tangents, controls, weights, knots, following LINE and zero audit errors/repairs. This does not certify native AutoCAD execution or complete SPLINE support.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_tangents.py artifacts/conformance
```

## Primary references

- [Autodesk SPLINE DXF definition](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm): optional WCS start/end tangents, groups 12/22/32 and 13/23/33.
- [ezdxf SPLINE reference](https://ezdxf.readthedocs.io/en/stable/dxfentities/spline.html): tangent vector metadata and independent development-reader API.
