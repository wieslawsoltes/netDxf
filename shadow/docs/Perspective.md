# Explicit perspective paper viewports

This feature adds a projective paper-viewport consumer without changing the source DXF camera,
semantic geometry, topology or the original orthographic behavior. It builds on the immutable
viewport-layer profiles in PR #253. Perspective remains an explicit opt-in: older applications
that do not provide `PerspectiveOptions` retain the existing unsupported-view diagnostic.

## Camera policy and equations

The source viewport contributes its view direction, target, twist, center, width/height, depth
clipping flags, lens length and authored model view height. The relevant source fields and flags
are defined in Autodesk's [VIEWPORT DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-2602B0FB-02E4-4B9A-B03C-B1D904753D34.htm).
The field values are read from detached snapshots; this implementation does not rewrite them.

`PerspectiveFieldOfView.TargetPlaneHeight` uses the authored `ViewHeight` to size the target plane.
`PerspectiveFieldOfView.LensLength` instead uses `eyeDistance * filmHeight / lensLength` as that
height. The film-height default is an explicit 24 mm consumer convention, not an inferred camera
sensor or a claim that every CAD producer uses the same lens framing. `eyeDistance` is the length
of the source view-direction vector. Singular frames, invalid lens dimensions and a near distance
that cannot be represented at the chosen scale are rejected.

The existing world-to-target transform includes paper scale, view-center offset and camera twist
in XY, and unscaled camera depth Z. For a target-frame point `q`, projection is:

```text
w = 1 - q.z / eyeDistance
paper.x = principalPoint.x + (q.x - principalPoint.x) / w
paper.y = principalPoint.y + (q.y - principalPoint.y) / w
```

The principal point includes the authored view-center offset, so panning does not translate the
physical eye. Camera depth is retained alongside divided XY for provenance, not used as painter
order. The default near-eye distance is 1/64 of the eye-to-target distance. This is a numerical
safety policy; authored front/back clipping can further restrict the visible interval.

## Homogeneous clipping and shared rendering

Lines and polygons are clipped to the near/front/back and four viewport-bound half-spaces **before**
division by `w`. Crossing the eye plane therefore cannot create an infinite screen coordinate.
Nonrectangular viewport masks remain shared with native rendering, SVG and hit testing. Line and
polygon work consumes deterministic operation/output-point budgets; repeated cached output is
charged to the scene's point quota too. Cancellation is propagated, not reported as partial success.

The model sampler uses a conservative visible-frustum amplification bound when choosing its
world-space tolerance. The original sampler retains its own point/depth limits. Distances remain
sampled curve distances, not certified analytic conic/NURBS arc lengths.

Linetype partitioning, endpoint fitting, definition-space transforms, transport and symbol creation
all happen before projective clipping. Layer profiles resolve before inherited styles. PSLTSCALE
compensation uses the target-plane magnification and is shared by dashes and symbols; a physical
pattern away from that plane naturally becomes larger or smaller with depth. It is not forced to
have constant screen spacing over a spatial curve. Model-tab annotation context is still disabled
when compiling model content for a paper viewport.

Native text parallel to the camera plane keeps the existing shaped layout with its exact depth
scale. Tilted native text uses `ITextOutlineProvider`: managed glyph regions in text-local Y-down
layout coordinates. `SkiaTextLayoutEngine` implements this capability using the same font/run
shaping as native drawing, detaching line, quadratic, cubic and rational-conic paths. Underlines,
fractions and other layout rules become closed strips. Per-run colors remain independent. No native
font/path handle enters a snapshot or escapes the provider. Visible glyphs without scalable paths
are diagnosed rather than silently disappearing; blank glyphs need no outlines.

The outline provider contract accepts primitive `Regions` and `Strokes`, not nested components,
embedded text/images or semantic contours. Hosts with another typography engine can implement the
same capability. A consumer without outlines gets a diagnostic for tilted text instead of a false
affine approximation. SHX/BIGFONT text is already neutral geometry and needs no native outline step.

## API usage

```csharp
using var typography = new SkiaTextLayoutEngine();
var compiler = new RenderCompiler(textLayout: typography);
var options = new RenderCompileOptions("Perspective sheet")
{
    PaperSpace = new PaperSpaceOptions
    {
        Perspective = new PerspectiveOptions(
            FieldOfView: PerspectiveFieldOfView.TargetPlaneHeight,
            NearDistanceFraction: 1.0 / 64,
            MaximumPoints: 1_000_000,
            MaximumWork: 8_000_000)
    }
};
var rendered = compiler.Compile(snapshot, options, cancellationToken);
```

`PaperViewportProjection.Perspective` distinguishes projective provenance. Its existing affine
`ModelToPaper` and `PaperToModel` members describe **only the target plane** in that case. They are
not a 3D perspective transform and inverse. `RenderItem.SourceToWorld` remains the actual
source-to-model affine transform, including insert transforms; no projective matrix is disguised
as an affine edit mapping.

`PerspectiveCamera.TryProject`, `Unproject(paper, cameraDepth)` and `Ray(paper)` provide the actual
mapping. `PickingRay.TryIntersectPlane` requires a finite, nondegenerate caller-selected model plane;
parallel or backward intersections return false. A point selected on a drawing does not by itself
identify an editing depth.

```csharp
editor.Select(new[] { projectedItem.Key });
editor.TranslatePerspective(rendered,
    fromPaper: new Vec3(startX, startY),
    toPaper: new Vec3(endX, endY),
    planeOrigin: new Vec3(0, 0, workPlaneZ),
    planeNormal: Vec3.UnitZ);
```

The editor computes ray/plane intersections for each selected occurrence, checks source receipts,
deduplicates compatible shared-source movements and executes one canonical transaction. Conflicting
movements through different views are rejected before mutation. Insert definitions use the inverse
source affine transform only **after** the model-space movement is known. Undo and redo use the
ordinary source-aware editor history. Generic affine translation/transform APIs reject perspective
selections; they do not guess a depth from a paper delta.

## Studio controls and guarded endpoints

The **Perspective** control selects Off, authored target-plane height or lens-length framing.
**Drag work plane Z** selects an explicit model XY plane for the sample's drag tool. Changing these
settings affects only the active workspace, advances the guarded view epoch, clears selection and
creates no source/history edit. Exact no-ops do not advance the epoch. Forks inherit the settings
but retain independent subsequent choices.

`POST /api/perspective-view` accepts `revision`, `mode` and `workPlaneZ`.
`POST /api/translate-perspective` accepts `revision`, `fromX`, `fromY`, `toX`, `toY` and optional
`definition`. Both retain the existing loopback, mutation-header, document/workspace/view guards.
The browser sends two paper points for projective drags, not a supposed affine delta. Arrow-key
nudges of perspective selections are refused with an instruction to use the explicit work-plane
drag. Shift constrains the paper drag displacement; it is not a claim of model-space object snaps.
The sample is still a local single-user host. This does not turn a GitHub Pages gallery into a live
editor or add a new public hosting mode.

## Boundaries

This is a pinhole projection, bounded clipping, shared glyph rendering and source-safe interaction
implementation, not a general solid renderer. Painter order remains unchanged; there is no depth
buffer, hidden-line removal, visual-style shader or material lighting. Parallel raster images and
parallel gradients retain exact affine-at-depth mapping. **Tilted rasters and tilted gradient paints
are explicitly diagnosed** because their rendering needs projective texture/paint sampling in both
backends; a wrong affine substitution is not used. Unsupported paint is reported per source item.
Font-specific color/bitmap glyph projection and proprietary CAD camera framing/pixel certification
are not claimed. Provider parsing/curve complexity and native allocations remain host responsibilities;
operation quotas are not a byte or wall-clock guarantee.

Tests cover randomized camera/ray roundtrips, clipping, source camera roundtrips, native glyph paths,
layer and linetype interaction, retained caches, work quotas, explicit failure cases, source-safe
dragging/undo, Studio workspace controls, real private-process HTTP and in-memory browser interactions.
Generated examples are `perspective-wireframe.png/svg` and `perspective-typography.png/svg` in the
verification artifacts. Test fixtures are not claimed to be independent native AutoCAD exports.
