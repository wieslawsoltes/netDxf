# Remaining HATCH losses — source audit after PR #50

This is a **historical source audit after PR #50**, fixed at commit `f4c8234494a3088677a366f9575feb05b1a88a0b`, tree `365eec7a7c69da87838a2f8e7cfc286b7dc84a27`. The source-observed gaps below describe that revision, not the current branch. Gradient, fit-data and outer-count corrections are now recorded in the [14 September checkpoint](checkpoint-2026-09-14.md) and [current comparison](version-feature-matrix.md). The original findings are retained below as historical evidence; native AutoCAD was not executed.

## Spline fit points and tangents are not retained

`HatchBoundaryPath.Spline` stores degree, rational/periodic flags, knots and weighted controls, but no fit points or start/end tangent data. `DxfReader.ReadHatchSplineEdge` validates the existing 2010+ fit packet and consumes its values without storing them. `DxfWriter.WriteHatchBoundaryPathData` writes zero fit points for that profile. The 2010+ matrix cells therefore change from partial to **known lossy**, not tested-complete. Earlier typed profiles also have no fit/tangent storage; their current parser does not consume this packet. This is not proof of the field's first historical release.

Remaining implementation requires explicit optional metadata storage, counted wire retention, cloning/conversion/transform behavior and version-specific independent fixtures. A spline evaluator or a fit-to-control reconstruction must not be substituted for preserving the authored metadata.

## Fractional gradient shift is reduced to an endpoint

`ReadHatchGradientPattern` casts the group-461 double to an integer and compares it with zero, storing only `HatchGradientPattern.Centered`. `WriteGradientHatchPattern` emits `Centered ? 0.0 : 1.0`. Fractional shifts therefore cannot survive. Autodesk explicitly defines intermediate values, so a Boolean is not a complete model. A future numeric property must preserve existing endpoint-Boolean compatibility without silently changing old authoring code.

## Gradient rotation is overwritten

The gradient sub-reader reads group 460 in radians and initializes gradient Angle in degrees. The enclosing `ReadHatch` then assigns `pattern.Angle = patternAngle`, using the ordinary group-52 pattern value or default zero. The independently merged pattern-order correction retains this as an explicit separate defect; it does not fix gradient rotation. The gradient and line-pattern angle contexts must be separated before asserting rotation fidelity.

## Additional reviewed risks, not completed features

The positional gradient packet reader still assumes a fixed layout without the new edge/pattern validation discipline. Its two-color construction path reads tint but does not carry that variable into the constructor result; its single-color construction regenerates the second color. Full dormant tint, authored color-stop and ACI-fallback fidelity need dedicated fixtures and an explicit model contract. No all-gradient retention claim is justified by retaining colors in a basic example.

The outer group-91 boundary list, general geometric validity, arbitrary scalar reordering within edge packets, NURBS knot/control relations, source-reference closure and nonuniform/general affine transforms remain separate work. The six typed profiles stay partial. For untouched data, the separate raw API can retain bytes; that is not a typed semantic fallback or an evaluation engine.

## Primary references and method of verification

These findings come from inspecting the named source methods and their model assignments at the immutable PR #50 revision above. No before/after runtime result is invented for these unimplemented fixes. The edge-packet regression note separately records tests that consume valid fit/tangent packets but does not assert storage.

Autodesk Boundary Path Data (spline groups 94–97, 40, 10/20, 42, 11/21, 12/22 and 13/23):
https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm

Autodesk HATCH (gradient rotation 460, shift 461, tint 462 and color data):
https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm
