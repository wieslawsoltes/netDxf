# HATCH pixel-size retention (group 47)

Baseline: `7d1f3b1ae0b845e010b80e1b194dea58173ca5fa`, after merged PR #42.

`Hatch.PixelSize` is an optional `double?` sampling-density hint for CAD intersection/ray-casting operations. The old reader discarded it and the writer always emitted zero. The typed reader now retains the value before, within or after pattern data; the writer emits it only when present. New hatches default to zero for compatibility. Loaded missing fields remain null; explicit zero, signed zero, subnormal and large finite values remain distinct at the wire level.

The setter requires finite, nonnegative values. This is a library input policy, not a claimed numeric limit from Autodesk. The reader rejects duplicate group-47 fields and negative values with contextual diagnostics. Existing strict codecs reject non-finite or truncated values. Public Debug exceptions / Release null behavior is unchanged.

Clones, nested INSERT clones and explosion preserve the scalar independently. TransformBy retains the hint unchanged; this feature does not evaluate or regenerate hatch sampling, infer anisotropic sampling, render a flood fill, or change pattern/boundary geometry.

```csharp
hatch.PixelSize = 0.125; // retain a CAD computation hint
var copy = (Hatch)hatch.Clone();
copy.PixelSize = null;  // omit group 47; original remains 0.125
```

| Path | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Read/write nullable pixel size | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary | Text + binary |
| Clone, edit, alternating transport | Tested | Tested | Tested | Tested | Tested | Tested |
| Solid/pattern/gradient field retention | Tested | Tested | Tested | Tested | Tested | Tested |

The existing AC1015 gradient-to-solid export behavior is unchanged. No historical typed profile is added. Group-47 retention does not complete HATCH support.

## Executed validation

72 independent-byte regression cases against unchanged production: **6,367 passed / 60 failed**, in Debug and Release. After the fix and 13 additional API/clone/fill tests: **6,440 passed / 0 failed**, using the locally compiled signed production library on .NET 8. Tests cover seven presence/precision values at three tag positions, duplicate/negative/truncated input, source state after rejected edits, cloned independence, seed/elevation/boundary/XData separation and caller streams.

The independent ezdxf 1.4.4 verifier reads twelve exported files (24 HATCH entities), checks sampling/seed/elevation/boundary/XData values and reports zero audit errors or repairs. Run `python tools/verify_hatch_pixel_size.py artifacts/conformance` after the conformance tests. This does not execute AutoCAD. Final-head Linux/Windows Debug/Release, netstandard2.0 and coverage/source-audit CI are merge gates.

Primary definition: Autodesk HATCH group 47, https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm

Remaining adjacent work: unrelated ACAD XData preservation, counted boundary grammar, arbitrary-affine transformation fidelity and native CAD qualification.
