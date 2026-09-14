# HATCH gradient packet dispatch and validation

## Correction

The old gradient reader advanced through a presumed sequence without checking group codes. A missing optional group 63, shuffled scalar, missing color, or unexpected count could select the wrong value type, silently change the gradient or lose synchronization. Unknown names silently selected the enum default.

The reader now accumulates gradient fields across the complete HATCH record. Singleton scalar order is independent of the documentation table and of group 450. Each group 463 begins a color stop; RGB and optional ACI components stay associated with that stop even when scalars intervene. Text comments and unrelated HATCH metadata are not color components. Binary comments remain forbidden by the existing transport codec. The accumulator stores at most two stops and never allocates from group 453.

Exactly one each of 450/451/452/453/460/461/462/470 is required when any gradient field occurs. Typed gradient input requires kind 1, reserved 451=0, mode 0/1, color count 2, stop markers 0 then 1 and one RGB component per stop. Missing/duplicate fields, orphan colors, third stops, unsupported names and inconsistent counts receive contextual `InvalidDataException` diagnostics in Debug. Public Release `Load` still returns null. Existing shift/tint diagnostics and finite primitive validation are retained. Solid kind 0 requires the scalar packet and zero stops; the remaining dialog values are ignored, as documented, and normalize away on save.

Writer ordering is unchanged. This adds permissive **reading** of scalar order, not a promise that other readers accept every noncanonical order. The nine known gradient names remain the typed model's boundary; unknown names/reserved meanings must use raw preservation rather than silently selecting Linear.

## Version comparison

| Family | Typed input | Typed output |
|---|---|---|
| R11/R12 / AC1009 | Not admitted | Raw preservation remains separate |
| R13 / AC1012 | Not admitted | Same |
| R14 / AC1014 | Not admitted | Same |
| 2000 / AC1015 | Existing permissive gradient import gains checked field dispatch | Existing solid-fill downgrade still loses gradient metadata |
| 2004 / AC1018 | Checked scalars/two-stop packet; optional group 63 accepted | Existing canonical gradient output, text and binary |
| 2007 / AC1021 | Same | Same |
| 2010 / AC1024 | Same | Same |
| 2013 / AC1027 | Same | Same |
| 2018 / AC1032 | Same | Same |

## Executed evidence

Baseline: merged PR #54, `762e78bde4f2bb7a340d8b102a67b28804576504`, tree `e21854d7587cd6b9f4ac89babf8e6ac508ade58f`.

1,284 new registered cases cover all six typed families, both transports, all nine names, eight packet arrangements, optional ACI combinations, reversed ACI/RGB order, 34 malformed-packet cases per profile/transport, and solid packets. Each successful gradient case checks clone and alternating transport, RGB, tint, shift, angle, seeds, elevation, following XData and LINE. AC1015 controls explicitly check its unchanged downgrade. Forged counts are checked against a small allocation ceiling.

Final identical tests against unchanged production: **10,751 passed / 1,164 failed in Debug; 10,895 passed / 1,020 failed in Release.** Corrected signed production: **11,915 passed / zero failures in both configurations**. Local tests use the .NET 8 compiler/runtime; the final repository CI additionally checks actual SDK builds, netstandard2.0, Linux/Windows, documentation integrity and source audit.

The first development fixture draft inserted group-999 comments in binary input. The binary codec correctly rejects these. Final fixtures add comments only in text; both red and green comparisons were rerun. This correction does not relax the binary codec or any production validation.

`tools/verify_hatch_gradient_packets.py` independently reads ten normalized exports / twenty original-and-clone gradients using ezdxf 1.4.4, checks authored state and adjacent structure, and requires zero audit errors and zero repairs. It is not native AutoCAD execution or a rendered appearance certificate.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_gradient_packets.py artifacts/conformance
```

## Limits and primary sources

Optional ACI **input acceptance** is not exact ACI metadata retention: the existing public RGB model still derives output indices, including when group 63 was absent. Full ACI identity/presence, packed-color high bytes, solid-packet byte retention, future reserved meanings, general affine evaluation and explicit downgrade reporting are separate. No full-standard qualification is inferred.

- [Autodesk HATCH definition](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm): conditional scalar packet, count/kind/mode, reserved fields, tint, shift and stop identifiers.
- [Autodesk entity group-code guidance](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): readers must dispatch by code rather than rely on the displayed table order.
- [ezdxf gradient implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/gradient.py): development interoperability evidence for optional per-color ACI group 63; not an Autodesk normative claim.
