# SOLID and TRACE affine geometry review

This C# task follows the [entity clone review](entity-clone-review.md) on the
merged PR #106 baseline. It changes existing `Solid.TransformBy` and
`Trace.TransformBy` behavior; JavaScript PR #98 is unchanged.

## Stored geometry and corrected behavior

SOLID and TRACE store four OCS corners on one plane, a common elevation,
an extrusion normal and optional signed thickness. Autodesk's
[SOLID DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-E0C5F04E-D0C5-48F5-AC09-32733E8848F2.htm)
describes the corner groups 10–13, thickness 39 and normal 210. The fourth corner
of a triangular entity repeats the third. This implementation preserves stored
corner order and repetition; it does not infer polygon winding or triangulate.

The previous transforms used `A * normal` as the target plane normal, left
thickness unchanged, derived elevation from the last transformed corner, and
mutated corners before the entire result had been checked. Those operations
could silently distort a sheared face or partially update an invalid result.

The shared internal candidate builder now derives the target oriented normal
from the cross product of the two transformed OCS basis vectors. It transforms
the plane origin separately from in-plane offsets, then computes all four target
corners and their shared elevation before publishing anything. Nonuniform scale,
reflection, supported shear, rotation, translation and an oblique source plane
are covered. Max-scaled normalization avoids squaring unscaled extreme values.

For nonzero thickness, the transformed extrusion must remain parallel or
antiparallel to the target normal. Its length and sign become the new thickness.
A rank-two map may collapse extrusion to zero while preserving a valid entity
plane. An oblique extrusion cannot be represented by these entities and rejects;
a zero-thickness face can still accept the corresponding plane shear. The
implementation does not invent extra thickness or independent corner-Z fields.

Both Matrix3-plus-translation and Matrix4 APIs validate all input elements.
Matrix4 requires an exactly affine bottom row `(0, 0, 0, 1)`; projective matrices
reject instead of silently dropping perspective terms. Nonfinite source or
result values, a collapsed or numerically degenerate plane, and underflow of a
nonzero transformed thickness reject before any entity mutation. Successful
changed geometry clears stale common proxy graphics. Exact identity preserves
stored values and proxy data, including the tested signed-zero bit patterns.

```csharp
var face = new Solid(new Vector2(0, 0), new Vector2(4, 0),
    new Vector2(0, 3), new Vector2(4, 3)) { Elevation = 5, Thickness = 0 };
face.TransformBy(new Matrix3(1, 0.5, 0, 0, 1, 0, 0.25, 0, 1),
    new Vector3(2, 3, 4));
// The tilted, sheared face keeps an actual plane normal and a shared elevation.
```

### Numerical and compatibility boundaries

The cross product of independently normalized transformed basis vectors must
have a maximum absolute component greater than `1e-14`. The allowed cross product
between the transformed extrusion direction and target normal is at most
`1e-12`. These are explicit binary64 admission tolerances, not claims of exact
symbolic arithmetic. Finite intermediate-overflow or underflow cases may reject
even when a differently factored mathematical expression could be represented.
Callers which previously relied on silently accepted degenerate, projective or
obliquely extruded results must now handle the exception or use an appropriate
different entity representation. No setter-wide validation change is made.

## Executed regression evidence

The exact new tests were first run against the previous production transform:
**54 passed and 248 failed**. The corrected implementation passes all **302** new
cases. Coverage includes both entity kinds, both transform APIs, oblique OCS,
signed/reflected thickness, four independent corners, triangular repetition,
large translations, `1e-150` and `1e150` scales, all matrix elements, rejected
projective matrices and bitwise rollback checks including proxy data.

Local Linux SDK 8.0.425 / runtime 8.0.31 runs pass **39,158 unique cases in both
Debug and Release**, zero failures, retaining all 38,856 clone-task cases and
adding 302. Each configuration emits **9,657 DXF files**. Result JSON bytes match:
SHA-256 `9866e606b98e9c4b67454002e070bab5c4c8964c33660383195612b6605c50a8`.
All **147 independent verifiers** pass against the full Debug output. Local
builds target net8.0; other platforms/targets require actual CI execution.

The new `verify_planar_transform_review.py` requires exactly **156 drawings**,
containing 312 transformed entities across R2000/R2004/R2007/R2010/R2013/R2018 in
text and binary. It independently derives world-space source geometry and matrix
images, checks all corners, orientation, elevation, signed extrusion and common
metadata, and rejects **5,928 actual-output corruptions**. Small-coordinate
checks scale their tolerance with the geometry instead of imposing a unit-sized
absolute floor. Every drawing passes ezdxf audit with zero errors and repairs.
The source inputs are synthetic mathematical cases, not native producer samples.

```sh
DXF_TEST_FILTER=planar-review/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_planar_transform_review.py artifacts/conformance
python tools/run_independent_verifiers.py artifacts/conformance --jobs 4
```

The existing historical coverage ledger is not promoted to universal compliance.
No native AutoCAD open/AUDIT/save/reopen or visual qualification was performed.
This task does not add historical typed dialects, complete TABLE/private cache
regeneration, native/private FIELD evaluation, recursive dependency import,
general version conversion, or equivalent corrections to every other entity's
transform method. LINE, POINT, RAY/XLINE and 3DFACE remain separate audit areas.
