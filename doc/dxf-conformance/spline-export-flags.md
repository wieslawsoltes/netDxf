# SPLINE closed, periodic and creation-method export flags

## Defects and correction

The writer added the numeric `SplineCreationMethod` API enum to group 70 as though it were a bit mask. `ControlPoints` is 1, so an open control-point spline was exported as closed. For an actually closed control-point curve, addition carried the two closure bits into bit 2 instead, marking it periodic. Fit-point creation mode was never mapped to the existing high-bit compatibility marker. The periodic branch also replaced rather than combined the rational flag and omitted the standard periodic bit.

Compose the mask with bitwise OR. Emit standard closed bit 1 for geometrically closed or periodic curves, periodic bit 2 for the periodic model, and retain rational bit 4 (the existing representation writes explicit weights). Keep the existing 2048 periodic compatibility marker and map fit-point creation to the existing reader's 1024 marker. Preserve the selected knot-parameterization flags without adding the ordinal creation enum. No public enum value is renumbered and no controls, weights, knots or fit points are regenerated.

Autodesk documents the standard low bits. The library's higher creation/parameterization/periodicity bits are **compatibility conventions already recognized by its reader**, not a claim that the public Autodesk table documents their full historical semantics. All six typed profile exports retain the library's existing higher-bit convention. Native validation across historical AutoCAD releases has not been performed.

## Version and operation contract

AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032: text/binary exports, clones, following XData and repeated alternating-transport reloads are tested. Typed historical admission and raw preservation are unchanged. Already stored malformed legacy flags are not automatically repaired based on guessed author intent. Reader admission of standard-bit-only periodic layouts, unknown high-bit preservation, knot/count grammar and complete spline geometry validation remain separate scopes.

## Executed evidence

Baseline: merged PR64, commit `1043b32a3abfaa223e197c4ff1a6795eb6a79039`, tree `2a076c9dd31c304425802c02b9c92fb86f462d14`.

240 new registered cases cover five authored states (open/closed control, periodic control, open/closed fit), all four knot-parameterization modes, six profiles and both transports. Independent raw flag assertions run before typed reload so a matching reader cannot mask the malformed writer. Each case exercises direct clones, three alternating transport cycles, exact stored geometry, creation mode, tangents, XData and a following LINE.

Unchanged production: **14,503 pass / 240 fail**, Debug and Release. Corrected full signed-library suites: **14,743 pass / zero fail**, both configurations. The 15 Python coverage-integrity tests remain separate.

`verify_spline_flags.py` uses independent ezdxf 1.4.4 to check 60 exported files / 120 splines, standard closed/periodic/rational bits, the whole retained compatibility mask, fit/tangent data, independent construction of the control geometry, XData and the following entity. Both Debug and Release exports have zero audit errors and zero repairs. This is not native AutoCAD execution or a rendering certificate.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_spline_flags.py artifacts/conformance
```

Primary reference: [Autodesk SPLINE group codes](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-E1F884F8-AA90-4864-A215-3182D47A9C74.htm), especially group 70 bits 1, 2 and 4. The main ledger is source-pinned and is refreshed after implementation merges.
