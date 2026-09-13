# HATCH gradient-angle preservation

## Defect and correction

The gradient reader already converts group 460 from radians to the public degree-valued `HatchPattern.Angle`. The enclosing HATCH reader subsequently replaced that value with group 52, or its default zero. Group 52 applies to pattern fills, not gradients. Consequently, an ordinary nonzero gradient angle was lost even when no conflicting field was present.

Apply the group-52 assignment only to non-gradient patterns. This does not change public angle normalization, the gradient packet parser, the writer's radians conversion, signing or target frameworks. Redundant early/late group 52 does not override group 460 for an actual gradient.

## Version and operation contract

| Family | Typed input | Typed output / evidence |
|---|---|---|
| R11/R12, R13, R14 | Still not admitted to `DxfDocument` | Raw API preservation remains separate |
| 2000 / AC1015 | Existing permissive gradient import retains angle | Existing solid-fill downgrade still omits gradient payload; no new historical legality or loss-reporting claim |
| 2004 / AC1018 | Group 460 retained | Text and binary, clone and repeated transport changes |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

Negative and wrapped angles use the existing normalized degree API. The emitted angle is semantically equivalent modulo one revolution, not a promise to preserve the original unnormalized floating-point tag bit-for-bit.

## Regression evidence

`HatchGradientAngleTests.cs` adds 362 registered cases: all nine gradient names, five gradient-capable families, both transports, four rotations, and two explicit legacy-downgrade controls. Wire bytes are independently encoded. The cases verify initial import, direct pattern clone isolation, nested INSERT clones, three alternating-transport cycles, raw group-460 radians and group-52 omission, plus seeds, elevation, XData and a following LINE.

Baseline is merged PR #49, `53f267c20bfb3e87320e1f24a029297238e05a91`, tree `ea88037d8ab3d5b2ad9c8639b217deee5315548a`. **Unchanged production: 8,612 passed / 272 failed in Debug and Release. Corrected production: 8,884 passed / zero failed in Debug and Release.** All ten independent HATCH verifiers passed on the combined Release exports; the angle verifier checked ten files / twenty angles with zero audit errors or repairs. The documentation-integrity suite is a separate check, not part of the C# case count.

```sh
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Debug
dotnet run --project tests/netDxf.Conformance/netDxf.Conformance.csproj -c Release
python tools/verify_hatch_gradient_angle.py artifacts/conformance
```

The independent verifier inspects ten exported files / twenty gradient angles before requiring zero ezdxf audit errors and repairs. It is not native AutoCAD execution and is not a full gradient appearance certificate.

## Deliberately separate remaining defects

The positional gradient packet grammar, optional ACI fields, continuous group-461 blend values, and complete color/tint semantics remain separate work. In particular, the existing `Tint` setter calls `Color2`, whose setter clears `SingleColor`; the clone initializer can therefore change single-color mode. This angle-only increment does not disguise that independent model defect as fixed. General HATCH edge grammar, arbitrary-affine transformation correctness, dependency reconciliation and explicit 2000 loss diagnostics also remain open.

## Primary references

- [Autodesk HATCH DXF reference](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm): group 52 is pattern-only; group 460 is gradient rotation in radians.
- [Autodesk common entity group-code guidance](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): dispatch by field code rather than assuming scalar table order.
