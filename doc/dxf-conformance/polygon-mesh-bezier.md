# Open Bézier polygon-mesh surfaces

This task is a separate child of the PolygonMesh conversion audit (PR #124,
`b355017c9e774dae9d7bc7966bd5cbcf5e226bd8`, tree
`63189769945b82ce7ed53ced5f865eae805d0760`). It does not change the JavaScript port.

## Public model and geometry

`PolylineSmoothType.BezierSurface = 8` is now supported for an **open**
`PolygonMesh`. Its `U` by `V` stored controls define a tensor-product polynomial
Bézier patch of degrees `U-1` and `V-1`. `MeshVertexes(precisionU, precisionV)`
samples both endpoints of each parameter interval `[0,1]`; U varies fastest in
the returned list. The existing control-count range (2 through 256 per axis)
and minimum sampling precision of three per axis still apply.

`MeshVertexes()`, `ToMesh()` and `Explode()` use the effective densities and
appearance/dependency policy introduced by the preceding conversion task.
Ordinary output cells are quadrilaterals, not automatically triangulated planar
faces. `Clone()` preserves the surface type and independently copies controls.
Sampling does not modify the source net or register output in a document.

```csharp
using System.Linq;
using netDxf;
using netDxf.Entities;
using netDxf.Header;

var controls = Enumerable.Range(0, 16).Select(n =>
    new Vector3(n % 4, n / 4, n % 3)); // U-fast 4 by 4 control net
var surface = new PolygonMesh(4, 4, controls)
{
    SmoothType = PolylineSmoothType.BezierSurface,
    DensityU = 21,
    DensityV = 21
};
var points = surface.MeshVertexes(); // 441 samples
var mesh = surface.ToMesh();         // 400 control-grid cells
var document = new DxfDocument(DxfVersion.AutoCad2018);
document.Entities.Add(surface);
bool saved = document.Save("bezier-patch.dxf");
```

A 2D or 3D **polyline curve** cannot use the surface-only enum value. Its setter
throws before altering its prior flags/type. Open quadratic and cubic B-spline
surfaces keep their existing algorithms; this is not a replacement of those
samplers. Closing either Bézier direction currently rejects evaluation and typed
loading with an explicit exception instead of inventing periodic semantics.
The setter may hold a closed flag, but sampling and output validation reject it.

## Typed DXF representation

The existing POLYLINE writer emits group 75 value 8, the fitted polygon-mesh
flags, the original control counts in 71/72, and generated sample counts in
73/74. Control vertices have flag 80; sampled vertices have flag 72. The wire
sequence remains M-major/N-fast, distinct from the U-fast public array.

The reader now admits open group-75-8 polygon meshes and preserves their control
net, type, parent metadata and effective densities. It validates the original
control count before allocating the bounded control array. Following entities
are not consumed as part of the mesh. The old general reader had a nominal
Bézier fallback, but the polygon-specific parser still rejected value 8; this
is not described as fixing a previously successful Bézier import.

**Fitted child-record preservation is not added.** As with the existing fitted
B-spline path, stored generated samples are regenerated from the control net;
child handles, private payloads, child-specific appearance and associations are
not qualified for preservation. Arbitrary native fitted meshes are therefore
not promised lossless typed round trips. `netDxf.IO.DxfRawDocument` remains the
separate unedited same-transport preservation path; it is not an automatic
fallback inside typed loading.

## Numerical and resource policy

Evaluation uses separable de Casteljau interpolation in the cheaper of the two
axis orders. It does not compute binomial coefficients or explicitly multiply
small Bernstein weights. This matters when a basis weight underflows binary64
but its product with a large finite control is still representable.

Each blend uses `(1-t)*a + t*b`, never an overflowing `b-a`. Finite convex
interpolation is clamped to its input interval to contain rounding overshoot.
Exact endpoints and constant controls retain their component bits. Interior
values, signed zeros and high-degree surfaces are not claimed universally
correctly rounded; intermediate arithmetic is binary64 and rounding accumulates.
No rational weights, derivatives, adaptive tessellation, periodic extension or
native fitting command equivalence is supplied by this sampler.

The preceding `MaximumSurfaceSamples` limit bounds generated and intermediate
grids to 1,000,000 points. `MaximumBezierBlendOperations` additionally bounds
one call to 64,000,000 vector blends. Both evaluation orders are counted using
Int64 arithmetic before sampler allocation. Source controls must be finite.
These are sampler admission limits, not a general transactional save guarantee
or a performance benchmark. The existing `SaveAtomic` API remains separate.

## Executed focused evidence

The same **238** new harness cases pass **0 before / 238 after** this feature.
The before build uses the preceding production code and the new tests; it has
no temporary feature stub. Tests cover rectangular control grids, both chosen
axis orders, low and high degree, large/small finite coordinates, exact corners,
constant extreme controls, rejection, clone isolation, conversion and round trips.
The two previous unsupported-surface guard inputs now use unsupported value 7
instead of newly supported value 8. Their original case identities and rejection
assertions remain; no baseline case is removed.

`tools/verify_polygon_mesh_bezier.py` uses an independent **exact Fraction
Bernstein tensor product**, not de Casteljau. It checks:

- 96 direct/nested drawings across R2000/R2004/R2007/R2010/R2013/R2018,
  text and binary: 3,360 generated physical vertices, complete selected ordered
  POLYLINE/VERTEX/SEQEND packets except entity/owner handle values, with handle
  framing, child ownership and independent graph checks. Zero graph errors or
  repairs; 118,896 actual packet corruptions reject.
- 36 independently regenerated numerical scenarios containing 864 samples,
  using exponents -500, 0 and 500. Parameters are the exact rational values of
  the binary64 divisions used by the API. Controls/corners are exact; each
  ordinary sample component admits absolute error at most `2e-12` times the
  largest control magnitude on that axis, not a universal per-component ULP
  guarantee. 145 numerical and two inventory corruptions reject.
- One degree-200 case in which a tiny basis contribution remains positive and
  finite despite the explicit basis weight underflowing. It has a separate
  relative `2e-13` bound, so a zero result cannot hide behind the ordinary
  control-scale absolute bound.

The numerical loops are inside one harness case and are not counted as 864
additional test cases. These are synthetic mathematical fixtures, not new native
producer evidence. Complete-suite and hosted results are recorded against their
executed heads in the PR; a focused pass is not a substitute for those runs.

```sh
DXF_TEST_FILTER=bezier-grid/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polygon_mesh_bezier.py artifacts/conformance
# Full suite and all independent readers:
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
```

## Primary references and remaining qualification

Autodesk's [POLYLINE DXF reference](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
assigns group 75 value 8 to Bézier surfaces and defines the control/sample counts.
[SURFTYPE](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Core/files/GUID-C925860D-5C50-485D-8940-69902CE53211.htm)
lists Bézier fitting, and [PEDIT polygon-mesh options](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-88A3C094-3001-439D-8AE8-6F57412AFE70.htm)
describe the control mesh and smooth/desmooth operations. Those references do
not establish this implementation's rounding, degree/closure admission or
resource limits as native AutoCAD contracts.

Native AutoCAD open/AUDIT/save/reopen, native surface fitting and closure,
installed fonts/visual equivalence, arbitrary fitted child/private graphs,
historical typed dialects, FIELD/TABLE/cache regeneration, dependency-complete
imports and general version conversion remain unqualified. This feature does
not establish universal DXF or full AutoCAD parity.
