# Primitive entities and recovered qualification

## Status and recovery

The primitive implementation, 521 complete original stored-transparency cases, independent entity comparison, and expanded browser runner were reconciled and published in upstream commit `8ee09bb68dd7cc52db8fb07f631deffcef72cedc` on PR #98. This document restores the retained contract material from the earlier local continuation, without overwriting the newer upstream entity code or scalar LIN offset fix. Both sets of supplemental tests remain present.

The present numerical continuation advances upstream `6bedd1b`, preserving the primitive code from `8ee09bb` and the newer reference-math backend. Its new code is committed locally as `a14fc00`, not pushed to GitHub. See [numerical implementation, measurements and retained failures](NUMERICS.md). All original C# and shared inputs remain pinned and unchanged; this is native JavaScript, not a CLR/WASM wrapper.

## Implemented models

Ten source-file mirrors provide the `EntityObject` base and its `CommonData` partial, seven concrete primitive entities (`Point`, `Line`, `Ray`, `XLine`, `Face3D`, `Solid`, `Trace`), and the standalone `Polyline2DVertex` model. These build on the previously committed style, layer, linetype, vector, matrix, XData and DxfObject implementations.

The entity base supplies original type/default-display state, layer and linetype change events, normal handling, visibility, transparency, common color-name/shadow metadata, guarded proxy-graphics byte storage, and the entity reactor view. Proxy bytes are copied on input and output and retain the original 16 MiB limit. Entity reactor references remain distinct from `DxfObject` persistent reactor metadata. Clones retain the original per-class field-copy order and deep-copy mutable display objects, XData and common proxy data without importing ownership or handles.

Each primitive exposes its original constructors, scalar/vector properties, clone methods and transforms. `Line.Reverse` and `Direction` retain their source behavior. Ray and XLine directions use the original normalization and zero-transform fallback rules. Face, solid and trace point order, normal/elevation handling and geometric mutations follow the pinned source. `Polyline2DVertex` adds bulge, copy construction, optional Int32 identifiers and independently present/absent start/end widths; this is **not** an implementation of the enclosing `Polyline2D` entity.

### Usage

```js
import {
  Line, Layer, Vector3, Matrix3, AciColor, EntityShadowMode,
  Polyline2DVertex, Vector2,
} from './javascript/index.js';

const line = new Line(new Vector3(0, 0, 0), new Vector3(10, 2, 0));
line.Layer = new Layer('CUT');
line.Color = AciColor.Red;
line.ColorName = 'Author-defined color';
line.ShadowMode = EntityShadowMode.CastAndReceive;
line.ProxyGraphics = Uint8Array.of(1, 2, 3);
line.TransformBy(Matrix3.Identity, new Vector3(5, -2, 0));
const independent = line.Clone();
console.log(independent.EndPoint.X, independent.EndPoint.Y); // 15, 0

const vertex = new Polyline2DVertex(new Vector2(4, 5), 0.25);
vertex.StartWidthOverride = 0;     // Explicit zero, distinct from absence.
vertex.EndWidthOverride = null;    // Absent override.
vertex.VertexIdentifier = 42;
const vertexCopy = vertex.Clone();
```

This authors **detached typed objects**. There is not yet a typed JavaScript `DxfDocument` to own or serialize these objects. The existing `DxfRawDocument` API remains a separate raw-tag engine; a passing raw roundtrip does not prove typed entity serialization.

### JavaScript adapters

`TransformBy` accepts either `Matrix4` or `Matrix3` plus `Vector3`, with dispatch isolated in the entity runtime adapters. Geometry still comes from the original-path vector/matrix implementations. A custom subclass implementing the two-argument transform can delegate its one-argument overload to the base implementation. Vector-valued public properties return copies rather than aliases. Reactors are exposed through a live read-only facade with `Count`, iteration and `get_Item`.

Events use the existing `EventHook.Add` / `Remove` adapter. A callback can replace `NewValue`; a callback exception preserves the original assignment order rather than committing a replacement prematurely. The .NET assembly supplies expected event traces in the independent comparison corpus.

The port deliberately retains source behaviors that might otherwise look like defects: zero normal/direction inputs normalize to the source's NaN vector before its post-normalization check; `Solid.Clone` and `Trace.Clone` omit elevation; `LinetypeScale` keeps its nonpositive-only guard. These are not claims that those behaviors are ideal. Changing them only in JavaScript would violate this pinned compatibility contract.


## Parsing, tests and remaining boundaries

PAT and LIN share invariant numeric parsing. The exercised .NET finite-number rule permits trailing ASCII whitespace followed by terminal NULs, while rejecting whitespace after NUL and NUL-terminated NaN/infinity symbols. The full current corpora retain 13 additional termination cases each: 376 hatch/PAT scenarios and 541 style/LIN/SHX scenarios, with exact output and error comparisons. Parsing NaNs reads binary storage rather than depending on a potentially canonicalized imported Number constant.

The original `TransparencyStoredTests` contribution has 521 complete model cases: 512 packed-alpha inputs, eight unknown packed values, and one authored-default case. It does not register shortened versions of the missing typed wire tests. Combined with the other original cases already on the branch, the ledger is 2,543/35,309, separate from 199 supplemental tests at the new numerical checkpoint.

`npm run test:entities` compares 479 scenarios and 2,598 operations against the actual pinned .NET assembly. It includes constructor overloads, common/display state, rejection semantics, nonfinite normals, event replacement/exception order, clone isolation, reactor references, seeded transforms and vertex-width presence. The reconciled numerical snapshot retains the exact entity corpus; its measurements are recorded with the other results in NUMERICS.md. They are also retained in the full browser corpus, which now contains 100,444 comparisons after the direct math audit was added.

This is **detached typed model** coverage. `DxfDocument`, registered ownership and typed entity serialization remain unfinished. The raw document layer is separate and must not be counted as a typed document implementation. The original multi-entity clone/reflection, polyline/document, version downgrade and typed IO cases remain missing until their complete bodies can run.

The prior local continuation's complete result sets and native CI results belong to their own commits. Current positive and negative evidence, browser-mode distinctions, numerical failures, platform limits and performance costs are recorded in [NUMERICS.md](NUMERICS.md), not inferred from old workflow statuses.
