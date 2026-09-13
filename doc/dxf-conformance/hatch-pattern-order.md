# HATCH pattern metadata ordering and deferred normalization

Implementation base: merged PR #48, `ad2ad829b804644223cd7bd8902d7d837be119c7`, source tree `8ef3dabb5001d31c9dc430d30ed669eb7a715bff`. This retains the independently developed boundary-classification correction, all its registrations and its verifier.

## Defect and supported contract

The former reader treated group 75 as the start of a greedy pattern suffix. Pattern metadata before that field was ignored, while unrelated HATCH geometry after it could be consumed by the wrong parser. It also converted each global pattern line into PAT-local coordinates immediately at group 78. A later group 52 angle or group 41 scale therefore changed the final metadata without reconciling line origins, offsets, angles or dash lengths. Export could then apply that transform a second time.

Pattern metadata is now dispatched by the owning HATCH reader throughout the record. Group 75 is a style field, not a section delimiter. The reader collects the final name, fill, style, type, double flag, angle and scale independently of their placement relative to complete boundary, pattern and seed packets. An omitted style uses the existing Normal default rather than preventing pattern parsing. Elevation, normal, pixel size, seeds, XData and following entities retain their own parsing scopes.

A private wire-data carrier retains the unnormalized line angle, global origin/offset and ordered dash lengths. After the HATCH record is consumed, the existing global-to-PAT-local arithmetic is applied once using the final global angle and scale. The public angle setter is not used as intermediate storage, so negative and greater-than-one-turn input angles are not normalized prematurely. Public angle normalization and nonpositive-scale import fallback remain unchanged.

This is not unrestricted tag shuffling. Counted boundary vertices, group-53 pattern lines, group-79 dash packets and group-98 seeds retain their packet grammar; moving a scalar into an unrelated counted packet is not admitted. Existing malformed-pattern count and group-code rejection remains active. No production target, strong-name setting or public API changes.

| DXF family | Typed transport | Pattern metadata order / deferred conversion |
|---|---|---|
| AC1009, AC1012, AC1014 | Not admitted by typed reader | No change; separate raw preservation only |
| AC1015 (2000) | Text and binary | Tested |
| AC1018 (2004) | Text and binary | Tested |
| AC1021 (2007) | Text and binary | Tested |
| AC1024 (2010) | Text and binary | Tested |
| AC1027 (2013) | Text and binary | Tested |
| AC1032 (2018) | Text and binary | Tested |

## Reproducible evidence

552 new registered cases use independently encoded wire tags rather than the production writer to construct input. They cover twelve placement modes (comments only in text), four nonzero rotation/scale pairs, two nonparallel pattern lines, positive/negative/zero dashes, cloning, three alternating-transport round trips, exact component counts and numerical geometry comparisons. Angles include 37, 90, -45 and 450 degrees; scales include 0.25, 2, 4 and 0.5. Counted packets and following data are explicitly checked.

The identical tests against unchanged merged PR47 production produced **7,642 passes / 456 failures** in both local Debug and Release. The original corrected checkpoint produced **8,098 passes / 0 failures** in both builds. Integration of the subsequently merged PR48 adds its 424 cases without removing or changing them; the final combined source passes **8,522 cases / 0 failures** in both local Debug and Release.

The development-only verifier `tools/verify_hatch_pattern_order.py` reads twelve retained exports with ezdxf 1.4.4. It independently computes expected global coordinates for the 37-degree, 0.25-scale fixture, checks all 24 original/cloned HATCH entities and 48 pattern lines, adjacent metadata, counted boundaries and the following LINE, then requires zero audit errors and zero repairs. It does not modify fixtures.

```sh
python tools/verify_hatch_pattern_order.py artifacts/conformance
```

The generated coverage check and 15 ledger tests remain separate. Final-head Linux/Windows Debug/Release, netstandard2.0 and source-audit CI must succeed before merge. No native AutoCAD execution is claimed.

## References and remaining work

Autodesk identifies group 75 as the hatch style, group 52 as pattern angle, group 41 as pattern scale, and group 78 as line count: https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm

Autodesk's entity parsing guidance explicitly warns against depending on the displayed scalar group order: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm

Gradient packet validation/rotation fidelity, general counted boundary-edge validation, arbitrary affine HATCH geometry, historical typed admission and native AutoCAD interoperability remain separate scopes. HATCH as a complete family and the full DXF standard remain only partially qualified.
