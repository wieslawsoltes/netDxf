# HATCH entity and boundary qualification

## Contract correction

The pinned `netDxf/Entities/Hatch.Transform.cs` stages and validates the complete geometry, then unlinks associative source entities before returning for an exact identity transform. Identity therefore preserves stored edge/path/pattern identities but does **not** retain the source association. The previous supplemental test incorrectly required an association to survive.

The corrected test checks preserved geometry identities, released contour references and source reactors, retained path ownership, and unchanged source geometry. A separate rejection test proves that invalid identity geometry fails before unlinking. No production behavior was changed to satisfy the incorrect expectation.

## Model coverage

The already-published native models expose `Hatch`, `HatchBoundaryPath` and its nested `Line`, `Arc`, `Ellipse`, `Polyline` and `Spline` edge classes. Qualified scenarios cover detached geometry snapshots, explicit contour refresh, conversions, independent clones, source-occurrence reactors, duplicate/foreign path rejection, before/after events, cancellation, nullable pixel hints, finite seeds, boundary reconstruction, periodic spline encodings and staged affine transformations.

Solid boundaries support the tested plane-preserving affine changes, including promotion of circles and bulged edges to conics. Explicit pattern families are re-encoded through the existing pattern transform. Nonuniform gradient, user-defined and doubled-pattern cases retain the source's rejection rules. Spline knots, weights, periodicity, fit points and tangents remain stored data rather than a refitted curve. These are detached APIs and do not imply registered document ownership or typed DXF transport.

Test-only reflection now resolves nested CLR type names and explicit enumerable signatures. Ordinary array indexing copies value elements and rejects invalid indices; the existing fixed-array adapters are preserved. The C# and JavaScript serializers observe their respective production models independently. No expected model values are stored in the input corpus.

| New HATCH category | Scenarios | Exact operations |
| --- | ---: | ---: |
| Boundary models and conversions | 80 | 740 |
| Associations and ownership | 72 | 1,058 |
| Collection events | 16 | 320 |
| Pixel hints and seed editing | 45 | 216 |
| Affine geometry and patterns | 840 | 18,216 |
| Periodic spline packets and metadata | 22 | 440 |
| **Total** | **1,075** | **20,990** |

All scenarios are part of the mandatory entity differential and both generated browser corpora. The entity requirement is now **7,391 scenarios / 57,725 operations**; the browser requirement is **124,216 comparisons**. Test coverage cannot pass by executing only the earlier corpus.

## Original test accounting

Three matching original-path test files add **147 complete original identities**: 145 detached cases from `HatchPeriodicConversionTests.cs`, `hatch/pixel-size/model`, and `hatch/seeds/api-validation`. The periodic cases retain degree/encoding tests, independent de Boor expectations, extreme/subnormal coordinates and weights, cancellation fixtures, reversal assertions and diagnostic JSON output. The original test-specific numeric tolerances are retained in those original methods; independent differential comparisons still require exact bits.

Original document-loading, registered ownership, INSERT/block, round-trip and transport bodies are not shortened or registered. The non-generic `IList.Add` seed operation maps to the same checked JavaScript collection method. Twelve new supplemental tests are accounted separately, not as original C# cases.

## Reproduction and detached example

From `javascript/`, with the pinned toolchain and an exact C# source checkout selected by `NETDXF_SOURCE_ROOT` when needed:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs conformance
npm test
npm run test:unit
npm run test:differential
npm run test:math:independent
node tools/browser-corpus.mjs
python tools/browser-check.py
python tools/browser-inline-check.py
npm run test:package
npm run benchmark
npm run verify # Retains negative evidence and returns nonzero for the blockers above.
```

The full entity runner includes every HATCH scenario. Its per-category counts and counterexamples are stored in `artifacts/entity-differential/<configuration>/results.json`; aggregate reports are in `artifacts/verification/<configuration>/report.json`.

```js
import { Hatch, HatchBoundaryPath, HatchPattern, Circle, Vector2, Vector3, Matrix3 }
  from './javascript/index.js';

const source = new Circle(Vector3.Zero, 2);
const path = new HatchBoundaryPath([source]);
const hatch = new Hatch(HatchPattern.Solid, [path], true);
hatch.SeedPoints.Add(new Vector2(3, 4));
hatch.PixelSize = null;
hatch.TransformBy(Matrix3.Scale(2, 3, 1), new Vector3(5, 7, 0));
console.assert(!hatch.Associative && source.Reactors.Count === 0);
console.assert(hatch.BoundaryPaths.get_Item(0).Edges.get_Item(0)
  instanceof HatchBoundaryPath.Ellipse);
const geometry = hatch.CreateBoundary(false); // Detached entities, not typed DXF output.
console.assert(geometry.Count === 1);
```

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/HATCH_ENTITIES.md). Current published scope is maintained in the [README](../README.md).
