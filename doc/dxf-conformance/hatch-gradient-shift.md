# Continuous HATCH gradient shift

## Defect, model and compatibility

Autodesk group 461 is a double-valued blend between non-shifted and shifted gradient definitions. The former reader cast it to an integer and stored only a Boolean. The writer then emitted either zero or one, so ordinary fractional blends could not survive even a load/save cycle.

`HatchGradientPattern.Shift` now stores that blend directly. The typed API accepts finite values in the inclusive range [0, 1], without rounding, clamping or tolerance-based endpoint detection. Read and write retain the numeric value, and clone/INSERT explosion retain the same blend. The existing `Centered` Boolean API remains present: its getter is true exactly at Shift == 0; assigning true selects 0 and assigning false selects 1. This matches the **previous writer's endpoint convention**, correcting the old XML comment which had described the Boolean mapping backwards. Assigning the Boolean intentionally replaces an intermediate blend with an endpoint; reading the Boolean does not mutate it.

```csharp
var pattern = new netDxf.Entities.HatchGradientPattern
{
    Shift = 0.375,
    Angle = 37.0
};
// pattern.Centered is false, but pattern.Shift is still exactly 0.375.
var copy = (netDxf.Entities.HatchGradientPattern)pattern.Clone();
copy.Centered = true; // copy.Shift = 0; original remains 0.375.
```

Invalid API assignments throw `ArgumentOutOfRangeException` before changing state. Out-of-range wire values have a contextual HATCH/group-461/position diagnostic in Debug; the existing Release Load-null policy is unchanged. Primitive nonfinite rejection remains in the shared codec. This is a typed-model domain policy, not a universal permissive repair or opaque-preservation policy.

## Version contract

| Format family | Typed import | Typed export |
|---|---|---|
| R11/R12 / AC1009 | Still rejected by DxfDocument | Raw preservation only, unchanged |
| R13 / AC1012 | Still rejected | Same |
| R14 / AC1014 | Still rejected | Same |
| 2000 / AC1015 | Existing permissive out-of-profile gradient import retains blend | Existing lossy solid-fill downgrade omits the gradient packet; no new legality claim |
| 2004 / AC1018 | Numeric blend retained | Text/binary, exact tested binary64 values |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

## Executed regression evidence

Baseline: merged PR #51, `b2226fabca6752055c50fa45c7b74f5af464679f`, exact tree `c29035e272d319b9e6e5149ed8e32b4484a457e0`. The 512 new independently encoded behavioral cases run against unchanged production: **9,682 passed / 332 failed**, in both Debug and Release. They cover all nine gradient kinds across all five gradient-capable families and both transports, fractional and endpoint values, subnormal and near-one precision, invalid finite input, three alternating-transport cycles, nested clone/explode, adjacent metadata and the unchanged 2000 downgrade.

Another 15 API/default/clone/transform/invalid-setter cases accompany the implementation. Final local signed-library results: **10,029 passed / zero failed**, in both Debug and Release. The independent development-only ezdxf 1.4.4 verifier checks ten files / twenty numeric shifts and adjacent HATCH/LINE structure, then requires zero audit errors and repairs. Verification of rendered interpolation and native AutoCAD execution are not claimed.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_gradient_shift.py artifacts/conformance
```

## Explicit remaining scope

This increment does not repair positional gradient packet grammar, optional ACI/color-stop metadata, tint/single-color setter interactions, authored-color retention, general affine gradient evaluation or centralized downgrade reporting. The independent angular defect was merged first as PR #51. The broad HATCH and gradient rows remain partial; raw and typed preservation remain separate pipelines.

Primary definition: [Autodesk HATCH DXF reference, group 461](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm).
