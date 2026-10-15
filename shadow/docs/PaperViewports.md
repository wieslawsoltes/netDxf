# Paper-space viewport composition

`RenderCompiler.Compile(snapshot, options)` composes orthographic model views when
a non-model layout contains active viewport entities and
`RenderCompileOptions.IncludePaperViewports` is true. The geometric projection and
graph layers remain independent of this display operation: projected model copies
never become duplicate semantic graph edges. Default viewport frames have display
contours and no semantic centerlines.

Every projected item carries its original source entity identity, insert path,
`SourceToWorld` transform and `PaperViewportProjection`. Viewport clipping masks
are immutable `RenderClip` records shared by affected render items. They retain no
mutable DXF or backend-native objects.

## Camera convention

For an orthographic viewport, the transform is:

```text
T(paperCenter - viewCenter * scale)
  * S(scale, scale, 1)
  * Rz(-twist)
  * transpose(arbitraryAxis(viewDirection))
  * T(-viewTarget)
```

`scale = viewport.Height / viewport.ViewHeight`. Camera-space depth is retained
for front/back clipping; XY is in paper units. `Affine3.TryInverse` supports the
inverse source-edit mapping with finite-value and singularity checks. Rectangular
clipping uses the viewport paper dimensions; custom boundaries are sampled with
point/tolerance budgets. Perspective views are diagnosed, not silently rendered
as orthographic views.

`FrontClipping` enables the front plane. `FrontClipNotAtEye` (bit 16) selects the
authored `FrontClipPlane`; without bit 16, the front plane passes through the eye
and the authored value is ignored. The target-to-eye direction length locates
that plane in camera depth. Enabled authored depth planes must be finite.

## Clipping and painting

Sampled line segments, painted stroke groups and filled regions are clipped to
the enabled depth slab. Text and images use separate depth masks, preserving
existing layout and raster transforms. The viewport XY mask is applied by Skia,
SVG and picking. Skia uses bounded native clip-path caching; SVG emits reusable
`clipPath` definitions. A clipped-out part cannot be picked through the mask.

Signed dash phase is resolved before depth clipping. Global `LtScale` is tracked
by occurrence/style projection; `PsLtScale` selects paper-unit versus model-scaled
dash lengths. Frozen layers include resolved entity layers and insert ancestors.
Paper annotations and viewport frames overlay model content. Full CAD stacking,
visual-style and draw-order equivalence is not claimed.

## Source editing

A normal paper-space drag is transformed through `PaperToModel` before the source
edit planner runs. Definition edits use the inverse occurrence transform, including
rotated/scaled block instances. Selecting one source through views that prescribe
incompatible displacements fails before mutation. A custom viewport-frame move
also plans a matching clip-boundary translation. Undo/redo uses the revision-checked
immutable history; retained snapshots remain detached.

## Cache and work bounds

Viewport descriptions track shadow read dependencies. Transformed items are reused
when source render-item identity, camera description and paper-linetype mode are
unchanged. Top-facing views query the model spatial index; oblique views use a
bounded candidate scan because a 2D model index cannot safely cull arbitrary 3D
camera directions.

`PaperSpaceOptions` bounds viewports, output items, candidates and clipping points.
The viewport-description and projected-item caches are bounded. Output assembly
and spatial-index construction are not claimed to be O(changed entities).

```csharp
var compiler = new RenderCompiler();
var scene = compiler.Compile(snapshot, new RenderCompileOptions("Sheet", 0.02)
{
    PaperSpace = new PaperSpaceOptions(
        MaximumViewports: 32,
        MaximumItems: 500_000,
        MaximumCandidates: 2_000_000,
        MaximumClipPoints: 65_536)
}, cancellationToken);
```

## Sample, regression coverage and boundaries

Studio's **Paper viewports** layout contains an overview and a circular, rotated
detail with annotation layers frozen. The Model, Appearance and SHX strokes layouts
are also available. The executable `ViewportVerification` suite covers inversion,
known camera coordinates, Skia/SVG masks, picking, frozen insert ancestors, oblique
depth clipping, front-at-eye flags, off/perspective diagnostics, graph separation,
projected edits, conflicting multi-view edits, block-definition movement, dash
scaling, incremental reuse, budgets/cancellation and DXF save/reload. It generates
`paper-gallery.png`, `paper-gallery.svg`, `paper-gallery.dxf` and clip fixtures.

Perspective projection/editing, all viewport-specific layer overrides and visual
styles, certified CAD stacking order, exact clipping of every spline/nonplanar
boundary, hidden-line removal and complete complex-linetype semantics remain
outside this composition layer. Source metadata is retained independently.

## Format reference

Autodesk AutoCAD VIEWMODE reference:
https://help.autodesk.com/view/ACD/2026/ENU/?caas=caas/documentation/CIV3D/2014/ENU/filesACD/GUID-01F9D6D3-0A43-4BCB-AA5C-65459D4BBC78-htm.html


## Optional projective consumer

Perspective viewports now have an explicit opt-in camera, clipping, typography and work-plane editing
pipeline. Leave `PaperSpaceOptions.Perspective` null to preserve this document's original orthographic
contract. See [Perspective.md](Perspective.md) for projective APIs, Studio controls, budgets and the
remaining projective-paint/hidden-surface boundaries.
