# MTEXT drawing direction survives cloning

Baseline: `1aee4bcebd9e760b583db89bc3430f3e8f29005f`, after merged PR #17.

`MText.Clone()` omitted `DrawingDirection`, so copying explicit left-to-right or top-to-bottom text silently reset DXF group 72 to the ByStyle default. Block/INSERT cloning and INSERT explosion inherited that defect. Copy the stored drawing direction without modifying geometry, style ownership, validation or serializer policy.

## Coverage

All three declared drawing directions are tested through direct cloning, nested block/INSERT cloning, INSERT explosion, and clone/save/load in all six admitted formats (AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032), text and binary. Geometry, formatting properties, Unicode content and independent source/copy state are checked. This is a clone-fidelity correction, not complete MTEXT implementation or text-layout/rendering certification.

45 new registered cases. Old implementation: **2,368 passed / 30 failed**. Corrected signed production assembly: **2,398 passed / 0 failed**, Debug and Release. Final-head cross-platform CI and netstandard2.0 builds remain required before merge.

## Primary reference

Autodesk MTEXT group 72: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-5E5DB93B-F8D3-4433-ADF7-E92E250D2BAB.htm

Remaining MTEXT work includes background fill, columns, and target-version fidelity. This fix does not add those features.
