# POINT affine location and signed extrusion

POINT transforms now update both the WCS position and the independent signed
extrusion vector. For a linear map A, unit normal n and signed thickness h,
the new direction is normalize(A n) and the new thickness is h times |A n|.
This corrects the previous unchanged thickness after scale/shear/reflection.
A true collapse of A n sets thickness to zero and retains the old normal as
unused metadata, matching the established LINE collapse policy.

The implementation reuses the existing exact affine-point and scale-safe
direction helpers and extracts LINE's existing signed-extrusion calculation
without changing its arithmetic. Bounded dyadic dots avoid intermediate
overflow/cancellation and distinguish true collapse from underflow. Results
outside representable finite storage reject rather than silently losing a
nonzero coordinate, normal component or thickness.

All source and transform components are validated before mutation. Matrix4
must have a finite affine bottom row (0,0,0,1). Staging finishes before point
fields or common proxy graphics change. Publication uses the actual base normal
rather than subclass virtual callbacks. Identity preserves every stored bit;
pure translations preserve normal, rotation and thickness bits. Actual edits
invalidate common proxy graphics; rejected operations preserve fields/proxies.
This is primitive-local atomic publication, not transaction-wide rollback.

## Marker and representation boundaries

The existing projected marker-axis convention is retained, now using scale-safe
direction calculations. The old OCS X axis at Rotation is transformed, projected
into the new OCS, and converted back through the existing POINT angle admission.
A collapsed marker axis chooses zero as the former Atan2(0,0) path did. A shear
cannot generally be represented as an identically sheared native PDMODE glyph:
these tests qualify stored orientation, not native marker shape or viewport
appearance. MathHelper.NormalizeAngle and direct POINT Rotation are unchanged.

POINT coordinates remain WCS; this is not inverse-transpose planar-normal
handling. The writer keeps its existing group-50 convention of 360 minus local
Rotation, including an explicit 360 for zero. Direct property nonfinite admission
is unchanged; using nonfinite stored values in an affine operation now rejects.
No new typed version, field, external dependency or serializer is introduced.

## Qualification

The 424-case harness contains 216 model/clone/owned cases, 108 invalid-input and
representability cases, four boundary/callback cases and 96 wire cases. It covers
both affine entry points, translation/rotation/scale/shear/reflection, rank loss,
tiny/huge finite maps, signed/zero extrusion, exact cancellation, metadata,
identity and rejection rollback. Eight wire transforms, three thickness values,
two planes, four ownership containers, six typed versions and both transports
produce 288 source/output drawings with 6,912 POINT records.

The independent checker derives WCS positions, extrusion and projected angles
from the fixture inputs; it does not capture production output as expectations.
Bounded fixture geometry is compared with 2e-12 relative / 1e-10 absolute
tolerances, while record count, presence, ownership and proxy bytes are exact.
It rejects changed/missing/duplicate/mistyped geometry and stale proxy packets,
and requires the complete corpus. All preceding regressions remain registered.

An initial fixture formatted row IDs with a non-padding numeric pattern, causing
lexical order to differ from its expected handle order. Correcting the new
fixture to D2 retains every assertion. The first independent checker expected
zero rather than the existing explicit 360 group-50 convention; that expectation
was corrected without changing production IO. Final old/new comparisons use
the identical final compiled harness. Actual executed counts belong to the PR.

Primary schema: [Autodesk POINT group codes](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-9C6AD32D-769D-4213-85A4-CA9CCB5C5317.htm).
Independent reference: [ezdxf signed extrusion transform](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/math/transformtools.py).
The latter's POINT transform does not update its marker angle; it is not evidence
of marker orientation equivalence. Native AutoCAD open/AUDIT/save/reopen,
private cache regeneration, all-version parity and general glyph rendering
are not established. Whole-document transactions, historical typed dialects,
dependency-complete imports and general version conversion remain separate work.
