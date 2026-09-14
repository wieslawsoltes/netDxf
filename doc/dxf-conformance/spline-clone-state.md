# SPLINE clone identity: stored geometry, tolerances and visibility

## Defects and correction

The fit-created branch of `Spline.Clone` invoked the public fit-authoring constructor, rebuilding control points, weights and knots from fit positions. This silently discarded subsequent edits to the stored control representation. Both clone branches reset all three tolerance fields, and the control-created branch also omitted visibility.

A private copy constructor now copies the existing control/fit arrays, knots, weights, degree, creation/periodic/parameterization modes, tolerances and independently optional tangent vectors. It deep-clones mutable common entity state and XData and retains visibility. A clone gets no handle, owner or reactors, as before. No fit solver is invoked and no knot domain is regenerated. The constructor copies private state directly instead of triggering setters with unrelated recalculation side effects.

## Version and operation comparison

| Family | Typed clone / stored output |
|---|---|
| R11/R12, R13, R14 | No historical typed admission; raw preservation remains a separate pipeline |
| 2000 / AC1015 | Clone fix and exact stored curve/tolerance/visibility persistence tested in text and binary |
| 2004 / AC1018 | Same |
| 2007 / AC1021 | Same |
| 2010 / AC1024 | Same |
| 2013 / AC1027 | Same |
| 2018 / AC1032 | Same |

Tests include direct clones, nested INSERT clones, identity INSERT explosion and two alternating transport cycles. They exercise fit-created splines with edited control geometry, open control splines and periodic control splines. Mutable arrays, table objects, colors, transparency and XData remain isolated. The normal and optional tangents are copied in memory; existing SPLINE normal serialization and broader flag policies are not changed by this increment.

## Executed evidence

Final baseline: merged PR #61, commit `df6ad87ea5a37c61de43bc7301da26373e926e41`, tree `1ffb0690927046cef5d1e2b6e352da4e818cc760`.

The exact final 111 new cases on unchanged PR61 production report **14,212 passed / 111 failed**, Debug and Release. Corrected separately compiled signed production reports **14,323 passed / zero failed**, both local .NET 8 configurations. An initial test tried to add a still-owned nested clone directly to a new document; it was corrected to clone for detachment after checking nested-clone state, then both red and green comparisons were repeated. Assertions were not relaxed.

`tools/verify_spline_clone_state.py` independently loads **36 files / 72 splines**, checks authored control edits, both stored representations, exact rational weights/knot values/tolerances, visibility, tangent and XData state, and requires zero ezdxf 1.4.4 audit errors or repairs. Both local configurations pass. Its semantic loader discards an explicit zero end tangent; its independent ordered tag decoder is therefore used to verify the actual zero 13/23/33 packet. Zero presence is not falsely attributed to its semantic model.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_clone_state.py artifacts/conformance
```

Final-head CI must additionally execute Linux/Windows Debug/Release, the actual netstandard2.0 build, source audit and separate 15-test documentation-integrity suite before merge.

## Primary reference and explicit limits

[Autodesk SPLINE reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm) describes the independently stored control points, fit points, knot and weight sequences, optional tangents and groups 42/43/44 tolerances. Clone identity is a library operation: retaining those representations does not certify they are mutually consistent or that the original fit solver is correct.

No native AutoCAD execution, shape-fitting certification, arbitrary invalid-array repair, full SPLINE input validation, normal/flag serialization correction, knot-aware reversal or dependency-closed document cloning is claimed here.
