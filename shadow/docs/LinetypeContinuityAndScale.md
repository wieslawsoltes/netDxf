# Authored linetype continuity and paper-space scaling

This builds on [complex linetypes](Linetypes.md). Snapshot/source ownership, the semantic graph, explicit font resources and source-identified editing remain unchanged.

## Authored vertices, not tessellation vertices

The stock 2D-polyline projector now reads the entity's `LinetypeGeneration` flag. A true value continues the periodic pattern around the contour. A false value restarts it at each authored line or bulge-arc segment. The `DrawingVariables.PLineGen` creation default is not substituted for an existing entity's flag.

```csharp
polyline.LinetypeGeneration = false; // Before attaching/capturing the drawing.

// An existing captured source uses the same canonical editor transaction and undo/redo.
editor.SetProperty(polylineId, "LinetypeGeneration", ShadowValue.From(true));
```

The neutral `CadContour.RestartLinetypeAtCurves` annotation does not split semantic contours. During sampling, the renderer records the authored boundaries in `RenderContour.PatternBreaks`, avoiding a second tessellation pass. A sampled arc can contain hundreds of edges but remains one pattern segment. Picking and ordinary snapping continue to use the original contour. The complex-symbol planner and signed-stroke engine consume the same partitions.

`PatternBreaks` contains strictly increasing interior point indices. A boundary point is included in both adjoining pattern paths. An implicit closing edge is preserved. Invalid indices and disconnected restarted curve chains fail explicitly rather than fabricating geometry. Partitioning uses a weak identity cache; point limits include copied shared endpoints and are checked before allocation, including on cache hits.

Studio's **Complex linetypes** layout includes continuous and per-vertex examples. Its existing inspector exposes `LinetypeGeneration`, and edits participate in the existing source/branch history.

**This is the continuity part of polyline generation, not full CAD end fitting.** The engine remains periodic: it does not stretch/center the terminal dash to force both ends of every short segment to be painted. Wide-polyline ribbons retain their existing solid-width presentation; this change does not dash filled ribbons. No new 3D-polyline rendering convention is implied.

## One scale for both dashes and symbols

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

## Verification

The advanced suite adds 59 cases: 26 continuity/round-trip/partition tests, 28 model/paper scale tests and 5 component-scale/API tests. Coverage includes four powers-of-two viewport magnifications plus unity under both PSLTSCALE modes, positive consumer multipliers, global/entity composition, missing/default paths, bulge sampling, zero-length edges, closed paths, source edits and history, partition budgets, per-scale model reuse, quota preflight, SHX revision changes, source editing, snapping and native/SVG output.

Two focused old-API probes were also run against the parent and changed source. On the parent, the second dash on a restarted polyline began at x=4 instead of x=3, and a half-scale viewport produced text axes of length 0.5 instead of 1 under PSLTSCALE=1. Both probes pass with this implementation. They are differential checks, not extra registered-suite cases.

```sh
dotnet run --project shadow/tests/netDxf.Shadow.AdvancedVerification -c Release -p:TargetFrameworks=net8.0
python shadow/tests/http_contract.py
node --test shadow/tests/browser/state.test.mjs
```

Remaining boundaries include A-alignment end fitting, MSLTSCALE/annotation-scale behavior, full character rotation-pivot and nonuniform-insert glyph fitting rules, arbitrary nonplanar transport, BIGFONT/vertical layout, perspective projection and specialized viewport overrides. Curved distances remain render-tolerance-dependent sampled arc lengths, not certified analytic solutions.

## References

Autodesk documents [polyline continuity and its per-entity setting](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-392BF13C-D9E7-47A8-8E07-435296332279.htm) separately from [PSLTSCALE's model/paper unit policy](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-23EA4D64-AE7D-41E5-A8D0-20F060313D62.htm). The numeric tests assert this implementation's explicit periodic contract; they do not certify pixel-equivalence with a proprietary CAD application.
