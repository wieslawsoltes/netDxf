# 3D polyline explosion fidelity and admission

`Polyline3D.Explode()` now shares a single output path for ordinary and smoothed
curves. Every detached LINE receives independently cloned Layer, Linetype, Color,
Transparency and ordinary XData, plus Lineweight, LinetypeScale, auxiliary Normal,
IsVisible, ColorName and ShadowMode. Source handles, ownership and stale proxies
are not copied. Source and sibling mutable appearance/XData do not alias.

Quadratic/cubic curves still use the existing PolygonalVertexes/NURBS evaluator;
no new spline-fitting algorithm is claimed. Registered objects use their document's
SPLINESEGS; detached objects, including objects in unregistered blocks, use
DefaultSplineSegs. A closed curve emits its closing segment; open curves do not.
The existing empty and single-point unsmoothed behavior is retained.

## Validation and limits

Unknown smoothing, nonfinite source/sampled geometry and malformed auxiliary
normals reject. Parent extension dictionaries/reactors, XData handle records
(including the null handle), and decorated retained child packets require explicit
dependency conversion rather than silently disappearing in the output. Ordinary
retained children with matching layer/colors and zero width fields are admitted.
Parent proxies are left untouched; output lines start without proxies. Failed
conversion does not mutate the source. This is not a dependency-complete converter.

`MaximumExplodedSegments` is 1,000,000. Generated spline sample and segment counts
are checked using Int64 arithmetic before generated arrays/output are allocated.
This bounds these allocations, not arbitrary spline CPU cost or the size of
caller-owned input/metadata. The public PolygonalVertexes API keeps its separate
contract. Negative SPLINESEGS remains unsupported by the existing header API.

HATCH boundary extraction uses an internal geometry-only path with the same
sampling/finite-input budgets. It retains the original contour object and leaves
association validation to the existing HATCH source-adoption code. It does not
invoke the detached public conversion's dependency guard. This keeps decorated
source handling and the three pre-existing foreign-source rejection tests intact;
no HATCH adoption guard is removed or relaxed.

Continuous linetype phase is not preserved across independent output LINEs.
XData coordinate payloads are copied, not interpreted or transformed; private
associations and native AutoCAD EXPLODE equivalence are not inferred. This task
does not change ToPolyline2D projection/elevation behavior.

## Qualification

The same final 74 cases pass 4 before / 74 after the correction, including six
HATCH integration scenarios. They cover ordinary,
quadratic and cubic curves, both closure states, four ownership contexts, mutable
isolation, loaded children, dependencies, invalid geometry/smoothing, sampling
limits and empty/single-point shapes. The allocation regression checks that the
new limit exists before attempting its oversized input on a negative build.

An independent physical-tag checker verifies 24 drawings / 132 LINE packets,
including every selected ordered field except handle values. Handle framing,
uniqueness, common ownership and DXF graph audit are checked separately. All
5,940 packet corruptions and two inventory corruptions reject. Wire fixtures use
unsmoothed input in six existing typed profiles, text and binary. The smoothed
cases compare against the unchanged library sampler and are model-only evidence.

```sh
DXF_TEST_FILTER=polyline-explosion/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polyline_explosion.py artifacts/conformance
```

Autodesk's OCS/WCS record model is documented at:
https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-D99F1509-E4E4-47A3-8691-92EA07DC88F5.htm
Output uses the existing LINE writer. No new historical dialect, native AutoCAD
open/AUDIT/save/reopen, font/visual qualification or private-cache regeneration
is established. Final full-suite/hosted results are recorded separately in the PR.
