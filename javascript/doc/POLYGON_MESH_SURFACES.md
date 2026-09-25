# Polygon meshes and parametric surfaces

This checkpoint extends the native JavaScript port on PR #98 from
`c87ab47daf7ea65557fa02618fb8712fa07a6a03`. The behavioral reference remains
C# commit `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425 and runtime
8.0.31. Original C# sources, fixtures, support assets and the existing reference
math implementation are unchanged. No merge or npm publication was performed.

## Standalone surface evaluation

```js
import {
  BasisFunctionInput, BSplineSurface, NURBSSurface, Vector3,
} from './javascript/index.js';

const u = new BasisFunctionInput(2, 1);
const v = new BasisFunctionInput(2, 1);
const controls = [
  new Vector3(0, 0, 0), new Vector3(2, 0, 0),
  new Vector3(0, 2, 0), new Vector3(2, 2, 1),
];
const surface = new BSplineSurface(u, v, controls);
const output = { value: null };
surface.Evaluate(0.5, 0.5, 2, output);
const position = output.value.get_Item(0); // (1, 1, 0.25)
const derivativeU = output.value.get_Item(1);
const derivativeV = output.value.get_Item(2);

const rational = new NURBSSurface(u, v, controls, [1, 2, 3, 4]);
const weightedPosition = rational.GetPosition(0.5, 0.5);
```

`BasisFunction.js` also exports the original `UniqueKnot` and
`BasisFunctionInput` value models. The basis evaluates local support and
its first three derivatives, preserving source evaluation order. Periodic
inputs wrap before evaluation; nonperiodic inputs clamp to the cached domain.
Repeated calls retain the source's derivative-storage behavior. Evaluation
writes minimum/maximum active indexes through objects with a `.value` property.

Both surfaces provide position, normalized U/V tangents, and a six-vector jet:
position, first U derivative, first V derivative, second U derivative, mixed
UV derivative, and second V derivative. Polynomial and rational accumulation
orders follow the unchanged production code. High derivative-order and
invalid control-index behavior is preserved, not replaced with new policy.
`NURBSSurface.Compute` is also implemented with its original output parameters.

The native fixed-array adapter models addressable element storage. Direct
`array[i]` access exposes a value location; `get_Item(i)` returns a value copy.
Assignments copy vector values. Array length cannot be changed. Surface
constructors own copies of input controls; partial arrays leave remaining
controls at zero, while oversized arrays reject with the original exception
parameter name. Null rational weights deliberately leave zero weights: the
source does not invent unit weights, and a zero denominator may produce NaNs.

`BSplineSurface.Controls()` is a method; `NURBSSurface.Controls` and `Weights`
are properties, matching the C# distinction. An out-of-range grid pair returns
the control/weight at index zero; its corresponding setter is a no-op, as in the source.
Basis dimensions outside 0 and 1 reject. Mutating exposed knot arrays does not
rebuild the separately cached domain or unique-knot lookup keys. Callers must
not assume that such edits reconstruct a new basis.

The reference GTE code uses `Debug.Assert` to describe structural preconditions.
Browser alert dialogs and process-aborting assertion hosts are not emulated.
The exact audit qualifies the recorded valid layouts and explicit API rejection
paths; it does not promise useful behavior for every malformed knot structure.
The absence of a throwing stub is not a claim of every possible runtime profile.

## Polygon mesh authoring

```js
import { PolygonMesh, PolylineSmoothType, Vector3 } from './javascript/index.js';

const controls = Array.from({ length: 16 }, (_, index) => {
  const u = index % 4, v = Math.floor(index / 4);
  return new Vector3(u, v, (u + v) % 2);
});
const grid = new PolygonMesh(4, 4, controls);
grid.SmoothType = PolylineSmoothType.Cubic;
grid.DensityU = 7;
grid.DensityV = 9;
const samples = grid.MeshVertexes(7, 9); // 63 value points
const mesh = grid.ToMesh(7, 9);         // 48 quadrilateral faces
const faces = grid.Explode();          // explicit densities agree with the samples
```

Controls are U-major: `uIndex + U * vIndex`. Construction preserves the
2..256 dimension limits and requires exactly U times V controls. Values are
copied, but the exposed fixed-array object remains stable over transforms.
`GetVertex` returns a value copy; `SetVertex` uses the source's invalid-pair
no-op behavior. Smoothing accepts only the original none/quadratic/cubic modes.
Finite-coordinate and degree/cardinality checks are performed on the same
paths as the reference implementation.

Quadratic and cubic sampling duplicate the required leading controls along
closed axes and invoke the same source-translated B-spline surface evaluator.
U and V closure remain independent. Knot edits, traversal order and repeated
parameter additions are preserved exactly; replacing them with a different
spline algorithm or multiplication-based stepping would change some results.

Source-specific behaviors are explicit:

- Parameterless `MeshVertexes()` clamps default sample counts to at least three;
  parameterless `ToMesh()` passes through its defaults and can reject lower counts.
- Smoothed `Explode()` uses the stored density fields for face construction.
  Unedited zero density fields can therefore produce no faces even when sampling
  returned points. Set explicit densities for a matching authored mesh workflow.
- The original seam loop omits the final corner when both axes are closed.
  This port preserves that topology rather than silently adding a face.
- Geometry conversion uses default new-entity appearance. Clone retains the
  broader appearance, proxy and XData contract and independent resources.

These are compatibility choices, not claims that each source behavior is ideal.
The test corpus exercises them independently against the pinned C# assembly.

## Retained child records and ownership boundary

`PolygonMesh.StoredRecords.js` and `PolygonMeshRecord.js` preserve stable
read-only record views, retained VERTEX/SEQEND metadata, resource lookup,
opaque-handle enumeration, topology accounting, independent clone resources,
and source-document/registration validation hooks. Clones rebind child owners
to the cloned mesh but do not fabricate a registered document. Private or external
dependencies, lossy smoothing changes and cross-document adoption reject where
required by the source.

These hooks are not a complete typed DXF reader/writer, registered document
collection or automatic topology importer. Supplemental host-adapter tests are
reported separately from actual C# document workflows; original typed document,
wire and native application cases remain unported unless explicitly counted.

## License and reproduction

The GTE translations preserve the MIT notices for netDxf and the Geometric
Tools attribution to David Eberly. The original Geometric Tools portions retain
Boost Software License 1.0, included in `netDxf/GTE/LICENSE.BSL-1.0`. Existing
reference-math code retains LGPL-2.1-or-later. The aggregate private package is
labeled `MIT AND LGPL-2.1-or-later AND BSL-1.0`; no mathematical files were
relicensed as MIT-only.

```sh
export DOTNET_ROOT=/path/to/pinned-dotnet
export CONFIGURATION=Release
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
node tools/dotnet.mjs native-port --check
npm test
npm run test:unit
npm run test:surfaces
npm run test:entities
npm run test:differential
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-check.py
python tools/browser-inline-check.py
npm run verify
```

Repeat configuration-specific steps with Debug. Complete compilation before
executing readers of the generated oracle assemblies. Run browser configurations
sequentially because they share a corpus path. Every failed or missing check
remains visible in the aggregate report. Platform CI now runs surfaces and
exp/log as separate steps, retaining their full results even after an earlier
entity-stage failure.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/POLYGON_MESH_SURFACES.md). Current published scope is maintained in the [README](../README.md).
