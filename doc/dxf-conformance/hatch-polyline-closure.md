# HATCH polyline closure fidelity

Baseline: `98ed11ef41c4bfe181b3db77f916d4c1477b6338`, after merged PR #44.

## Corrected data loss

The HATCH polyline reader ignored group 73, leaving IsClosed false even when the DXF requested a closed boundary. Independently, HatchBoundaryPath.Polyline.Clone omitted IsClosed. This lost the closing segment on conversion/explosion and could turn a closed hatch contour into an open one during INSERT cloning or transformation.

Read the closure flag and copy it during cloning. Preserve the existing vertex-array deep copy, path-type policy, entity conversion and serialization. No assumption is made that every boundary must be closed: an explicit open flag remains open. Existing TransformBy behavior expands open polylines to line/arc edges; tests check that no closing segment is introduced, rather than requiring an unchanged edge representation.

| Path | AC1015 | AC1018 | AC1021 | AC1024 | AC1027 | AC1032 |
|---|---|---|---|---|---|---|
| Read and emit group 73 | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary | Text/binary |
| Closed/open round trips and cloned state | Tested | Tested | Tested | Tested | Tested | Tested |
| Nested INSERT clone and explosion | Tested | Tested | Tested | Tested | Tested | Tested |

## Executed evidence

50 new registered C# cases. Final tests against unchanged production: **6,621 passed / 25 failed** in both Debug and Release. Corrected signed production assembly: **6,646 passed / 0 failed** in both local .NET 8 configurations. The first test draft wrongly required an open polyline to stay a polyline after INSERT explosion; its expectation was corrected to the existing three-segment representation before the final red/green comparison.

Tests cover independently encoded 0/1 flags, exact emitted group 73, three alternating-transport cycles, original and cloned hatches, independent vertex storage, closing-segment endpoints, conversion, nested cloning/explosion, elevation, XData and caller-owned streams. The production diff is limited to the two missing assignments.

The optional ezdxf verifier checks twelve files containing 24 closed HATCH contours, confirms their vertex order and closed flags before audit, and requires zero audit errors or repairs. Run `python tools/verify_hatch_polyline_closure.py artifacts/conformance`. No native AutoCAD process is executed. Final-head Linux/Windows Debug/Release, netstandard2.0 and documentation/source checks are required before merge.

## Scope boundary

This corrects closure preservation, not the entire HATCH boundary grammar. Sparse optional bulges, counted-list validation, path-flag preservation, spline fit/tangent data and broader geometric transformation fidelity remain separate increments. Historical typed profiles are unchanged.

Primary reference: Autodesk Boundary Path Data, group 73 (Is closed) and group 42 (optional bulge): https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm
