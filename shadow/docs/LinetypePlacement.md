# Linetype placement and scale

These policies extend the [complex-linetype contracts](Linetypes.md). Periodic,
world-space placement in the authored plane remains the default. The sections
below document each opt-in policy and its limits.

- [Continuity and paper scale](#authored-continuity-and-paper-space-scaling)
- [Endpoint fitting](#endpoint-fitting)
- [Glyph frames and pivots](#glyph-frames-and-rotation-pivots)
- [Spatial transport](#spatial-transport)
- [Model annotation scale](#model-tab-annotation-context)

## Authored continuity and paper-space scaling

This builds on [complex linetypes](Linetypes.md). Snapshot/source ownership, the semantic graph, explicit font resources and source-identified editing remain unchanged.

### Authored vertices, not tessellation vertices

The stock 2D-polyline projector now reads the entity's `LinetypeGeneration` flag. A true value continues the periodic pattern around the contour. A false value restarts it at each authored line or bulge-arc segment. The `DrawingVariables.PLineGen` creation default is not substituted for an existing entity's flag.

```csharp
polyline.LinetypeGeneration = false; // Before attaching/capturing the drawing.

// An existing captured source uses the same canonical editor transaction and undo/redo.
editor.SetProperty(polylineId, "LinetypeGeneration", ShadowValue.From(true));
```

The neutral `CadContour.RestartLinetypeAtCurves` annotation does not split semantic contours. During sampling, the renderer records the authored boundaries in `RenderContour.PatternBreaks`, avoiding a second tessellation pass. A sampled arc can contain hundreds of edges but remains one pattern segment. Picking and ordinary snapping continue to use the original contour. The complex-symbol planner and signed-stroke engine consume the same partitions.

`PatternBreaks` contains strictly increasing interior point indices. A boundary point is included in both adjoining pattern paths. An implicit closing edge is preserved. Invalid indices and disconnected restarted curve chains fail explicitly rather than fabricating geometry. Partitioning uses a weak identity cache; point limits include copied shared endpoints and are checked before allocation, including on cache hits.

Studio's **Complex linetypes** layout includes continuous and per-vertex examples. Its existing inspector exposes `LinetypeGeneration`, and edits participate in the existing source/branch history.

Continuity partitions are independent of fitting. The default periodic mode does not stretch/center terminal dashes; opt-in [endpoint fitting](#endpoint-fitting) applies after partitioning. Wide-polyline ribbons retain their existing solid-width presentation; this change does not dash filled ribbons. No new 3D-polyline rendering convention is implied.

### One scale for both dashes and symbols

`SceneOptions.LinetypeScale` and `RenderCompileOptions.LinetypeScale` are positive finite, consumer-only multipliers. They do not change `LtScale`, entity scale or source geometry. They participate in existing option/cache identities.

For an ordinary entity, let `S` be authored global LTSCALE multiplied by the entity scale and the consumer multiplier. For a viewport with model-to-paper magnification `v`:

- With `PSLTSCALE=0`, the model pattern uses `S` and projection gives paper-space scale `S * v`.
- With `PSLTSCALE=1`, the model pattern uses `S / v` and projection gives paper-space scale `S`.

This compensation happens **before** compiling both stroke periods and text/SHX placements. Paper projection applies the ordinary affine scale once, then resolves dash phase and depth/viewport clipping. Symbols are no longer pre-generated at an incompatible scale while only dashes are corrected later. Lineweight and ordinary entity text are not multiplied by the consumer linetype multiplier.

```csharp
var options = new RenderCompileOptions("Sheet")
{
    LinetypeScale = 1,
    PaperSpace = new PaperSpaceOptions { MaximumPatternScales = 16 }
};
var paper = compiler.Compile(snapshot, options);
```

ByLayer/ByBlock inheritance uses the existing style resolution. Explicit component styles receive the consumer multiplier once; parent/layer component styles already contain it and must not receive it again. This includes explicit line styles in regenerated dimension components. The original two-argument component resolver and existing positional option constructors remain available.

Distinct viewport magnifications can require distinct model pattern expansions. Equal compensated scales share one expansion in a composition. `PaperSpaceOptions.MaximumPatternScales` defaults to 32 and is checked before any model expansion is requested. Model sampling uses the tightest active viewport tolerance. Each model expansion retains its own linetype work/placement limits; paper composition additionally retains viewport, candidate and final-item limits. The distinct-scale limit bounds multiplicative model work, not bytes or native allocations.

Changing viewport magnification, `PsLtScale`, source/style data, consumer scale or a provider revision invalidates the relevant output. Retained source snapshots and pinned SHX resource sets keep their previous results. Presentation flags, source entity/insert provenance, clipping and paper-to-model editing transforms survive projection. Symbols still do not become topology edges or default snap targets.

### Verification

The advanced suite adds 59 cases: 26 continuity/round-trip/partition tests, 28 model/paper scale tests and 5 component-scale/API tests. Coverage includes four powers-of-two viewport magnifications plus unity under both PSLTSCALE modes, positive consumer multipliers, global/entity composition, missing/default paths, bulge sampling, zero-length edges, closed paths, source edits and history, partition budgets, per-scale model reuse, quota preflight, SHX revision changes, source editing, snapping and native/SVG output.

```sh
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification -c Release -p:TargetFrameworks=net8.0
python shadow/tests/http_contract.py
node --test shadow/tests/browser/state.test.mjs
```

The following sections cover opt-in fitting, frames, transport and model annotation scaling. [BIGFONT](BigFont.md), [vertical SHX text](Shx.md#vertical-typography), [perspective projection](Perspective.md) and [viewport layer overrides](PaperViewports.md#immutable-viewport-layer-overrides) have separate resource and rendering contracts. Curved distances remain render-tolerance-dependent sampled arc lengths, not certified analytic solutions.

### References

Autodesk documents [polyline continuity and its per-entity setting](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-392BF13C-D9E7-47A8-8E07-435296332279.htm) separately from [PSLTSCALE's model/paper unit policy](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-23EA4D64-AE7D-41E5-A8D0-20F060313D62.htm). The numeric tests assert this implementation's explicit periodic contract; they do not certify pixel-equivalence with a proprietary CAD application.

## Endpoint fitting

`RenderCompileOptions.LinetypeAlignment = CadLinetypeAlignment.A` enables endpoint fitting for both signed strokes and complex symbols. The same option is available on `SceneOptions`; detached callers can set `CadStyle.LinetypeAlignment` directly. `Periodic` remains the default for source and binary compatibility. The original `StrokePattern.Apply` and `LinetypePlanner.Plan` overloads retain periodic behavior; new overloads take an explicit alignment.

In Studio, **Pattern ends** switches between **Periodic** and **Fit endpoint dashes**. The setting is workspace-local, inherited when forking, and does not mutate DXF properties or add history. Changing it advances the request epoch and clears presentation selection because glyph ordinals can change. Retained source snapshots are displayed using the currently selected view policy, like the current layout and camera; the snapshot and pinned SHX resources are not rewritten. Reloading a document restores the periodic default.

### Published open-path rule

For path length L, period P and nonnegative first dash d, let n be the largest integer with nP <= L. When n is zero, the path is drawn continuously and incomplete symbol motifs are omitted. Otherwise the first and last dash both have length (L - nP + d)/2. Interior descriptors retain their authored lengths, so both endpoint dashes are at least d/2 and gaps and secondary dashes are not stretched. A fused multiply-add and threshold correction handle division rounded across an integer repeat boundary.

The last terminal dash is also a completed first descriptor: a decoration authored on that descriptor is emitted at its completion, including the final open endpoint. Zero-length descriptors remain dots. Symbols remain whole; fitting never clips a glyph to a dash or to the path endpoint. Ordinary viewport clipping still applies.

This implements the endpoint rules in Autodesk's [About Simple Custom Linetypes](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Customization/files/GUID-EF1DF0A9-2088-487C-8085-16FEE6425405.htm). It is not an assertion of pixel certification against every AutoCAD release.

### Closed-path policy

The documentation does not prescribe a unique numeric closed-path residual rule. This implementation preserves every gap and secondary descriptor, distributes residual length equally over the first dash of each complete repeat, and places the seam at the center of a first dash. Start/end partial strokes therefore join continuously; duplicate seam dots and symbols are not emitted. Glyph dimensions and offsets are not stretched. A closed path shorter than one complete period uses the same solid fallback. This deterministic policy is explicit rather than claiming an undocumented proprietary algorithm.

### Shared domain and invariants

`LinetypeFitting` produces stroke intervals and symbol events from one distance-domain fit. Authored polyline partitions from `LinetypePaths` are applied first. Thus per-vertex polylines fit each authored segment while continuous polylines fit the full chain; adaptive tessellation vertices do not become fitting boundaries. Distances are sampled arc length at the requested rendering tolerance.

PSLTSCALE compensation precedes fitting and depth/viewport clipping. Phase is computed for the full source path, never for the visible clipped portion. Both native and SVG renderers consume this plan. Semantic contours, source identities, graph edges and default snapping remain unchanged; symbol selection still edits the source.

Malformed A patterns (fewer than two descriptors or a leading gap), invalid enums, nonfinite periods and unrepresentable repetition counts fail explicitly. Existing per-item and scene placement/expansion limits remain active, including terminal symbols. Positive all-dash patterns coalesce into continuous strokes. Missing fonts do not change the fitted stroke spacing.

The advanced suite covers exact and fractional repeats, short fallback, dots, closed seams, corner partitions, descriptor-attached symbols, source/cache/history isolation, viewport compensation, floating-point thresholds, 500 deterministic randomized endpoint/gap cases, Studio controls and backend output. Randomized iterations are one registered test, not 500 independently counted cases. HTTP and optional in-memory Chromium fixtures separately exercise the view control and stale-request handling.

## Glyph frames and rotation pivots

Two opt-in `LinetypePlanOptions` policies extend the shared dash/symbol engine without changing its original defaults.

### Definition-space metric

`MetricSpace = LinetypeMetricSpace.Definition` measures the sampled path after applying the inverse source
occurrence transform. It fits and places both dashes and symbols in that definition frame, then transports
the complete result through the original affine transform. Nonuniform scale, reflection, shear, rotation
and translation therefore affect stroke lengths, symbol widths/heights, skew, offsets and rotation pivots
together. Nested insert transforms are already composed in `SourceToWorld`.

The existing `World` mode remains the default and retains constant world-space pattern size. The definition
policy is explicit because CAD producers differ in how inherited pattern scales interact with inserts; it
is not an assertion that every proprietary insert convention is identical. Absolute glyph rotation in
definition mode is absolute in the definition's authored plane, then transformed with the instance.

`RenderItem.PatternToWorld` carries the metric transform until the stroke pattern has been resolved.
Paper projection resolves definition-space dashes before projection/depth clipping and resets the metric
on its already-compiled output. Symbols and dashes therefore cannot be transformed twice.
Source identities and editing transforms remain the original source-to-world transform; glyph placement
does not become an editing coordinate system.

### Text rotation pivot

`TextRotationPivot = LinetypeTextRotationPivot.NominalCapHeightCenter` rotates text around half the nominal
authored cap height instead of the baseline origin. Style obliquing and upside-down orientation contribute
to that unit-height pivot. An explicit finite, planar `TextPivot` overrides the nominal choice.
The unrotated pivot is carried to the same point after relative/absolute/upright rotation, including flips.
Tangent-relative offsets are independent of glyph scale; shape insertion origins are not reinterpreted as text pivots.

`BaselineOrigin` remains the default for existing consumers. These options are set on the render compiler:

```csharp
var compiler = new RenderCompiler(builder, textLayout,
    linetypeOptions: new()
    {
        MetricSpace = LinetypeMetricSpace.Definition,
        TextRotationPivot = LinetypeTextRotationPivot.NominalCapHeightCenter
    });
```

### Invariants and numerical limits

Coordinate copies are preflighted against the point quota, checked for finite input/output and cancellable.
Singular source transforms, invalid enum values and nonplanar/nonfinite pivot overrides fail explicitly,
rather than silently falling back to world spacing. Distances remain render-tolerance-dependent sampled
arc lengths; transforming a sampled path does not certify exact analytic arc length.
The nonplanar parallel-transport policy can be combined with definition-space metrics.

The neutral geometry, topology, source synchronization, cache keys and original public method signatures
are retained. Twenty unique advanced cases cover cap-height and explicit pivots, reversed/upright text,
shape origins, multiple affine matrices, patterned curves, native text, source topology, paper projection
and malformed/bounded input.

Autodesk's [Text in Custom Linetypes](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT-MAC-Customization/files/GUID-FEDCE7EB-4919-43AE-A54E-F3A293DD60CA.htm)
documents nominal cap-height pivots and independent offset scaling. This consumer exposes those choices
explicitly; undocumented producer-specific text fitting and raster-level certification are separate concerns.

## Spatial transport

Set `LinetypePlanOptions.FrameMode = LinetypeFrameMode.ParallelTransport` when constructing `RenderCompiler` or calling `LinetypePlanner.Plan` to enable complex symbols on nonplanar sampled paths. The default `AuthoredPlane` mode preserves existing output and explicit nonplanar refusal.

`LinetypeTransport.Create` produces immutable rotation-minimizing edge frames. Distances and pattern-event scheduling still use the shared periodic/A-fitting engine. Authored polyline boundaries restart paths before transport. Duplicate vertices consume no length or rotation. A least-aligned-axis seed handles an authored normal parallel to the initial tangent. Minimal tangent-to-tangent rotations transport the side vector, followed by reorthogonalization; at an antiparallel cusp, the previous side is the deterministic half-turn axis.

Closed paths optionally distribute residual seam roll by sampled arc length (`CloseFrameSeam`, enabled by default). This makes the transported seam orientation close without imposing a discontinuous twist at one point. The policy is deterministic, not a claim of a unique frame at a geometric cusp or an exact analytic Frenet frame. At an exact vertex the incoming edge defines the glyph frame, matching existing placement semantics.

Relative glyph orientation follows transported tangent/side vectors. Absolute and upright orientations use the local normal's object-coordinate basis. Offsets remain tangent/side-relative. Both render backends consume the same generated geometry. Source identities, presentation flags, picking and semantic topology remain unchanged.

Frames are bounded by the point quota and cancellation checks. Zero-length paths, nonfinite coordinates, invalid seed normals and unrepresentable accumulated distances are rejected before output. Curved paths use the existing sampling tolerance; tighter tolerances improve the polyline approximation, not an analytic certification.

The executable verification covers helix continuity, finite orthonormality, planar equivalence, cusps, duplicate points, closed explicit/implicit seams, fitting, rendering, budgets, cancellation and legacy behavior.

## Model-tab annotation context

`RenderCompileOptions.ModelAnnotation` carries an immutable, source-neutral `ModelAnnotationContext`.
The Model layout multiplies the existing entity/global/consumer linetype scale by drawing units per paper unit
when model annotation scaling is active. This affects signed dashes and complex symbols together, before
endpoint fitting. It does not scale geometric entity coordinates, semantic graph edges or ordinary TEXT entities.

### Resolution

`Drawing` respects captured `$MSLTSCALE` (group 70, 0 or 1). Missing switches default to disabled for
pre-2008 DXF versions and enabled for modern drawings. `Disabled` bypasses this layer; `Enabled` overrides
the drawing switch. Explicit `AnnotationScale(PaperUnits, DrawingUnits)` takes precedence over captured
scale selection. All units and the resulting multiplier must be positive and finite.

Otherwise a captured `$CANNOSCALEVALUE` real ratio supplies paper units per drawing unit (the multiplier is its
reciprocal). A captured `$CANNOSCALE` name is looked up in a host-provided immutable `NamedScales` catalog.
Display names are never parsed as ratios: a name such as `1:50` need not describe its actual stored units.
The standard absent/default context is 1:1. An unresolved named context produces a `linetype.annotation`
diagnostic and multiplier 1 rather than guessing units. Duplicate or malformed relevant custom headers are rejected.

The [custom-header adapter](CustomHeaders.md) supplies the detached `CustomVariables` collection and source-backed
edits/undo. Native SCALE dictionary objects are not automatically decoded by this consumer; hosts can supply
their authoritative named catalog or explicit units. This is a model-tab linetype context, not a complete
annotative entity/context-object implementation.

```csharp
var options = new RenderCompileOptions
{
    ModelAnnotation = new(new AnnotationScale(1, 50), ModelAnnotationMode.Enabled)
};
var scene = compiler.Compile(snapshot, options);
```

All context values participate in immutable option cache identity. Source-header edits create new snapshots.
Retained snapshots and old options continue to produce their own output. Context resolution never writes headers.

### Paper-space isolation

Paper layout entities are unaffected. Internal model compilation for paper viewports explicitly uses
`ModelAnnotation = null`; only the existing PSLTSCALE and viewport compensation apply there. Model-tab
annotation and paper compensation therefore cannot multiply each other accidentally.

### Studio

The Model annotation selector, paper/drawing units and Apply scale button are workspace-local view controls.
The guarded `/api/annotation-view` mutation checks document/workspace/view/revision identities through the
existing request boundary. A real change advances the view epoch and clears stale selection without changing
the source revision or undo history. A repeated value is a no-op. Forks inherit the current view setting;
switching workspaces restores each workspace's setting. Loading a new document resets the live context.

### Verification and references

`ModelAnnotationVerification` adds 22 unique advanced cases for explicit/named/drawing context, modern/legacy
defaults, invalid values, source history, cache retention, paper isolation and Studio workspaces. The HTTP
contract suite exercises the actual guarded route, stale epochs and source-neutral no-ops.

Autodesk references: [MSLTSCALE](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-023B046C-56EA-463C-A867-DF713666A69E.htm),
[CANNOSCALE](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-Core/files/GUID-C7111BEE-76AA-481D-AC51-8600A31FA23A.htm).
This implementation preserves the existing source codec and does not claim native SCALE-dictionary or
proprietary annotation-object certification.
