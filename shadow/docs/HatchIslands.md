# Hatch island semantics

DXF HATCH group 75 distinguishes `Normal` (0), `Outer` (1), and `Ignore` (2). The shadow geometry layer resolves their presentation before handing geometry to a renderer. Skia, SVG, fill-aware picking and pattern scan conversion therefore consume the same selected boundaries rather than implementing independent island policies.

## Immutable presentation contract

`CadGeometry.Contours` always retains **all original semantic boundary curves**. `Regions` and `DisplayContours` are presentation-only. Changing island style does not erase holes from the source snapshot, remove graph edges, or turn hatch pattern strokes into graph edges.

`HatchIslandResolver.Analyze` produces a detached `HatchBoundaryTopology`. Each `HatchBoundaryInfo` stores the original input index, immediate parent index (`-1` for a root), nesting depth, and sampled local-XY bounds. The topology retains immutable input contours; `Select` returns those analytic contours by reference, not sampled replacements. Input order and winding are not used to infer parentage.

| Mode | Presentation boundaries retained | Result |
| --- | --- | --- |
| `Normal` | All depths | Alternate filled and empty levels using even/odd fill. |
| `Outer` | Roots and their immediate children (depths 0 and 1) | Fill the outer ring; never restart inside deeper islands. |
| `Ignore` | Roots only (depth 0) | Fill each outer region without internal islands. |

Disjoint roots form separate trees. A concave boundary's centroid need not lie inside it; containment uses a boundary vertex after excluding sampled inter-loop contacts and crossings. Sorting by decreasing absolute area assigns depths iteratively, avoiding recursion proportional to the number of nested loops.

The convenience `HatchIslandResolver.Select(..., Normal)` returns the original immutable array without classifying a forest. This preserves the existing even/odd treatment of complex Normal boundaries and avoids classification cost for the default mode. Calling `Analyze` explicitly validates simple-loop topology for every style.

## Geometry-neutral API

```csharp
using System.Collections.Immutable;
using netDxf.Shadow;
using netDxf.Shadow.Geometry;

static CadContour Rectangle(double min, double max) => new([
    new LineCurve(new(min, min), new(max, min)),
    new LineCurve(new(max, min), new(max, max)),
    new LineCurve(new(max, max), new(min, max)),
    new LineCurve(new(min, max), new(min, min))
], Closed: true);

ImmutableArray<CadContour> semantic = [Rectangle(0, 10), Rectangle(2, 8), Rectangle(3, 7)];
var topology = HatchIslandResolver.Analyze(semantic);
var selected = topology.Select(CadHatchIslandStyle.Outer);
var geometry = new CadGeometry(semantic)
{
    DisplayContours = [],
    Regions = [new CadRegion(selected)]
};
// Three semantic contours; two fill boundaries. Geometry remains renderer-agnostic.
```

For patterned output, pass the selected array to `HatchPatternGenerator.Generate`. Keep the original array as semantic contours. The standard entity projector already does this for source hatches, including gradient regions and the boundary-only fallback for patterns without line definitions. Selection happens in local XY before elevation, OCS and insert transforms are applied.

## Source editing, snapshots and caches

`HatchPattern.Style` now exposes its existing setter publicly. This is an additive netDxf API change; it does not change the existing enum storage, clone behavior, DXF group or target frameworks. Undefined enum values remain representable as they were through the reader/internal setter; the default shadow projector diagnoses an unknown style and does not invent a fill.

To participate in transactional source edits and undo/redo, edit the pattern object's property through the shadow editor:

```csharp
using netDxf.Entities;
using netDxf.Shadow;
using netDxf.Shadow.Editor;
using netDxf.Shadow.NetDxf;

static void SetOuter(ShadowSession session, EditorSession editor, ObjectId hatchId)
{
    var patternId = session.Current.Read(hatchId)["Pattern"].Id
        ?? throw new InvalidOperationException("Hatch has no captured pattern object.");
    editor.SetProperty(patternId, "Style",
        new ShadowValue.Atom(typeof(HatchStyle).FullName!, nameof(HatchStyle.Outer)));
}
```

Projection observes `Pattern.Style`, so changing it invalidates dependent hatch geometry while unrelated render items remain reusable. Retained source and render snapshots keep their previous style and paint. External source mutations still require the session's serialization/refresh discipline; exposing the setter does not create a notification stream.

## Numerical model, budgets and failure behavior

This implementation classifies **sampled planar boundaries**, not exact analytic boolean geometry. Default `BoundaryTolerance` is 0.01 drawing units; curves use the existing bounded `CurveSampler`. Classification uses local XY, leaving original analytic curve coordinates untouched. Neighbouring curve endpoints may be joined in the classification copy within the separate `ContactTolerance` of 1e-9 drawing units. This never repairs a crossing, closes an arbitrarily open chain, or mutates the source curve.

Segment and loop BVHs prune spatially unrelated candidates. Contact tests use scaled edge directions; absolute polygon areas use translation-relative compensated summation. Unresolved numeric comparisons fail explicitly. These techniques do not provide exact predicates or prove that unsampled curve features cannot intersect. Features at or below the sampling resolution need a tighter caller-selected tolerance or a different geometry kernel.

The default budgets are 100,000 contours, 1,000,000 generated boundary samples, 10,000,000 candidate segment comparisons and 10,000,000 containment edge visits. `SampledPointCount` reports the retained unique classification vertices, while the sample budget counts generated points including duplicated curve endpoints. Cancellation is checked between contours, curves, segment queries and during long sample/containment loops; the underlying individual sampling/BVH operations remain bounded synchronous calls.

Self-crossing, touching, coincident, disconnected, degenerate or numerically unresolved sampled loops are rejected by forest analysis. Near-touching loops within the contact tolerance are also rejected. For `Outer` and `Ignore`, the standard projector retains semantic geometry but emits no presentation paint when analysis fails, together with an explanatory diagnostic. There is **no even/odd fallback disguised as the requested mode**. Normal continues to use its pre-existing even/odd behavior.

Sparse disjoint workloads avoid all-pairs predicates. Deeply nested or overlapping bounding boxes can still produce quadratic candidate work; explicit budgets cap expensive predicate visits rather than claiming universal linear complexity. A regression constructs 10,000 disjoint four-edge squares: 40,000 retained vertices, 40,000 segment comparisons and zero containment edge visits. This is a deterministic test workload, not a throughput guarantee.

## Studio and verification

Run Studio and select **Hatch islands**. The gallery shows all nine mode/paint combinations with deliberately unordered paths: Normal, Outer and Ignore across solid, patterned and gradient hatches. The graph overlay retains all four nested boundaries for every panel. The regression suite renders PNG/SVG, retains a revision, adds/undoes an entity and saves/reloads the DXF.

`HatchIslandVerification` adds 76 executable cases to the prior 259-test advanced suite. They cover ordering, winding, concavity, circles/NURBS, disjoint and deep nesting, numeric placement, endpoint tolerances, invalid data, budgets/cancellation, actual Skia pixels, SVG paths, picking, semantic graph cycles, source edits, undo/redo, cache reuse, DXF persistence and the Studio gallery. Existing tests and allocation/reuse assertions are unchanged. The initial integrated Linux Release run passed 18/18 original and 335/335 advanced tests.

Proprietary gradient transfer/shift functions remain display approximations. This increment does not implement an exact CAD region/boolean kernel, malformed-boundary repair, automatic associative hatch rebuilding, or all specialized CAD rendering and editing features.

## Primary format references

- [Autodesk DXF HATCH group codes](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm), group 75.
- [Autodesk ObjectARX HatchStyle](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbHatch__HatchStyle1.html), alternating/outermost/ignore behavior.
- [Autodesk Hatch and Gradient More Options](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-Core/files/GUID-A3276CE4-CDFA-45E4-AE15-EDD75DDD5124.htm), island display modes.
