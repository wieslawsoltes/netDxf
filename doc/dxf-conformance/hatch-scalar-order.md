# Order-independent HATCH scalar edge and list headers

## Defect and correction

The existing bounded boundary reader consumed LINE, circular ARC and ELLIPSE fields in a fixed table order. Reordered scalar fields were rejected even when all required fields and the surrounding counted framing were present. POLYLINE and SPLINE scalar headers had the same limitation. Autodesk's common entity guidance explicitly warns against assuming the printed scalar order.

The corrected reader dispatches unique scalar fields by group code. A fixed bit mask records required/present fields, avoiding per-edge dictionaries or scratch arrays. Coordinates of the two unique LINE/ELLIPSE points may be separated; angle, radius/ratio and direction fields may move within their scalar edge packet. POLYLINE header groups 72/73/93 and SPLINE header groups 94/73/74/95/96 may be reordered before the repeated vertex/knot/control lists.

This does **not** make repeated list grammar arbitrary. Edge type 72 still selects an edge packet, source count 97 remains a framing boundary, repeated vertex/knot/control/weight/fit lists keep their existing ordered consumption rules, and field groups cannot cross an entity or packet boundary to repair incomplete input. Existing finite-number, flag, count, degree-range, comments and EOF checks are retained. Duplicate, incomplete and surplus scalar packets fail contextually. Producer counts never become speculative allocation sizes.

## Version and operation comparison

| Format | Typed import | Typed export |
|---|---|---|
| AC1009 / R11-R12, AC1012 / R13, AC1014 / R14 | Typed document still rejected | Separate raw pipeline unchanged |
| AC1015 / 2000 | Unordered unique edge fields and pre-list scalar headers | Existing canonical text/binary writer |
| AC1018 / 2004 | Same | Same |
| AC1021 / 2007 | Same | Same |
| AC1024 / 2010 | Same; existing fit-data profile retained | Same |
| AC1027 / 2013 | Same | Same |
| AC1032 / 2018 | Same | Same |

Public API, geometry representation, writer ordering, down-save policy and signing are unchanged. No radius/ratio normalization, curve refitting, boundary repair, containment inference or association reconciliation is performed. Existing broader HATCH/NURBS models remain partial.

## Executed regression evidence

132 registered cases exercise all six typed profiles and both transports. The simple-edge cases include 432 cyclic/reversed wire permutations, separated point components, text comments, direct clones, exact nontrivial binary64 coordinates, repeated persistence, each missing/duplicate field, invalid flags and next-edge/next-entity isolation. List-header cases add 96 reordered polyline/spline headers plus per-field missing/duplicate checks; repeated payload values and adjacent seeds remain unchanged.

Identical final tests on unchanged PR #76 production: **16,801 passed / 96 failed**, both Debug and Release. All 96 failures are positive scalar/header ordering cases. The other validation cases retain their baseline behavior. With the corrected reader and merged PR #77 LIGHT support, the combined signed-library .NET 8.0.31 suite reports **17,145 passed / zero failed**, both configurations. LIGHT and HATCH development used separate worktrees; shared integration was checked before publication. Fifteen Python documentation-integrity tests are a separate count.

`verify_hatch_scalar_order.py` independently loads 36 exported version/transport/edge-type drawings using ezdxf 1.4.4, checks exact LINE coordinates and ARC/ELLIPSE data, seeds, XData and record structure, and requires zero audit errors/repairs. Some fixtures intentionally isolate one primitive edge, including an open LINE; successful audit is not a claim of geometric area closure or rendered-fill correctness.

All 41 independent verifier scripts pass against both complete local configurations: **82 successful processes**. Some scripts inspect ordered tags rather than semantic objects; these are not native AutoCAD runs or uniform full-schema certificates. Native AutoCAD open/AUDIT/save/reopen remains unexecuted.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_scalar_order.py artifacts/conformance
```

## Primary references and remaining work

- [Autodesk Boundary Path Data](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm): scalar fields, list headers and repeated data boundaries.
- [Autodesk Common Group Codes for Entities](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm): group-driven dispatch rather than assuming printed table order.

Remaining HATCH work includes broader list grammar, spline knot/degree/periodicity relationships, geometric validity, full affine plane/normal handling and source-reference closure. Full DXF support is not inferred from these scoped ordering tests.
