# HATCH spline fit points and optional tangents

## Defect and representation

The existing 2010+ reader validated spline fit data but discarded it. The writer emitted a zero fit count, and boundary conversion/clone paths had no storage for those fields. This change retains authored metadata without reconstructing the control polygon or fitting a new curve.

`HatchBoundaryPath.Spline` now exposes an editable `IList<Vector2> FitPoints` and nullable `Vector2? StartTangent` / `EndTangent`. Fit points are ordered OCS positions; tangents are OCS vectors with their original magnitudes. Empty lists, independently omitted tangents and explicit zero vectors remain distinct. New instances have an empty list and null tangents. The list and setters reject nonfinite components before mutation, including non-generic collection access. Duplicate fit coordinates and edits do not regenerate knots or controls.

The reader consumes fit count 97 and ordered 11/21 point pairs incrementally. Tangent pairs 12/22 and 13/23 may appear in either order, but duplicate or incomplete tangents are rejected contextually. This spline-local group 97 remains separate from the subsequent boundary-source-reference group 97. Existing counted-input, finite-number, comment and EOF policies remain in force.

## Version and operation comparison

| DXF profile | Typed fit/tangent read | Typed fit/tangent output |
|---|---|---|
| AC1009 / R11–R12 | Historical typed input still rejected | Separate raw preservation only |
| AC1012 / R13 | Same | Same |
| AC1014 / R14 | Same | Same |
| AC1015 / 2000 | Existing control-spline grammar unchanged | Populated fit metadata is rejected before stream writes; empty metadata retains the old control-only output |
| AC1018 / 2004 | Same | Same |
| AC1021 / 2007 | Same | Same |
| AC1024 / 2010 | Retained | Text/binary, editing, clones and repeated transport changes |
| AC1027 / 2013 | Retained | Same |
| AC1032 / 2018 | Retained | Same |

This preserves the library's existing **2010+ fit-packet profile**, not a claim about the first historical appearance of every fit field. The API does not enable earlier typed dialects. Export preflight checks all registered model/paper/nested/unreferenced blocks before preprocessing, handle allocation, APPID registration or writes to the supplied stream. The file-path `Save` wrapper is still nontransactional and may already have truncated its file. Public Debug exceptions / Release false-return conventions are unchanged.

## Cloning, conversion and coordinates

Edge, boundary, HATCH and nested INSERT clones have independent fit collections. `ConvertTo` uses the internal standalone SPLINE constructor that retains both control geometry and fit metadata, rather than the public fit-authoring constructor that computes controls. `ConvertFrom` projects authored WCS fit positions and tangent vectors into the entity's OCS as it already does for control positions. Out-of-plane input follows the existing projection contract.

`Hatch.CreateBoundary` and `Hatch.TransformBy` now route spline OCS-to-WCS conversion through the existing corrected `Spline.TransformBy`. Positions receive linear transformation plus translation; tangent vectors receive only the linear part. Tests cover arbitrary initial normals with translation, rigid rotation, uniform scale and reflection, plus coplanar nonuniform INSERT scaling. General affine plane-normal correction and global HATCH transform validity are not claimed by these cases.

```csharp
var edge = new HatchBoundaryPath.Spline
{
    Degree = 2,
    Knots = new[] { 0.0, 0.0, 0.0, 1.0, 1.0, 1.0 },
    ControlPoints = new[]
    {
        new Vector3(0, 0, 1), new Vector3(5, 10, 1), new Vector3(10, 0, 1)
    },
    StartTangent = new Vector2(10, 20),
    EndTangent = new Vector2(10, -20)
};
edge.FitPoints.Add(new Vector2(0, 0));
edge.FitPoints.Add(new Vector2(5, 5));
edge.FitPoints.Add(new Vector2(10, 0));
// Use AutoCad2010 or later when saving this metadata in a HATCH.
// Clearing FitPoints and both nullable tangents explicitly opts into control-only export.
```

## Executed evidence

Baseline: merged PR #56, commit `3c704331145c61fe6b6f6817f9ef8ba186005b91`, tree `609a715e44479a0ae0341cb8d85d0b24cf766a36`.

The 177 wire/grammar cases in `HatchSplineFitTests.cs` use independently encoded input bytes. With unchanged production they report **12,208 passed / 132 failed**, in both Debug and Release. The initial fixture accidentally inserted comments into binary DXF; comments were restricted to valid text fixtures before repeating both final red runs. No binary-comment acceptance or relaxed validation was added.

The fixed production library passes those identical cases. Another 159 API/default/clone/conversion/OCS/INSERT/profile cases in `HatchSplineFitApiTests.cs` bring the signed-library full suite to **12,499 passed / zero failed**, both local .NET 8 configurations. The 15 Python ledger-integrity tests remain a separate count. CI must execute the final PR head on Linux/Windows Debug/Release, compile netstandard2.0 and retain its source audit before merge.

`verify_hatch_spline_fit.py` independently loads six exported text/binary drawings with ezdxf 1.4.4, checking 12 spline fit packets, exact binary64 fit values/order, tangent magnitudes, unchanged knots/controls, closing LINE edges, seeds, elevation and following XData. All six outputs have **zero audit errors and zero repairs**.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_spline_fit.py artifacts/conformance
```

## Primary reference and remaining work

Autodesk, [AutoCAD 2012 DXF Reference](https://images.autodesk.com/adsk/files/autocad_2012_pdf_dxf-reference_enu.pdf), Boundary Path Data, printed pages 93–94 (PDF pages 101–102): spline fit count/points and OCS tangent vectors. The published table was inspected directly. This is metadata and transport evidence, not native AutoCAD execution, fit/control consistency validation, spline evaluation, full knot/degree/periodicity validation, automatic boundary repair or a full-standard certificate. The outer HATCH path-count grammar and dependency-closed source references remain separate work.
