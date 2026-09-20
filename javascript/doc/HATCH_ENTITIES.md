# HATCH entity and boundary qualification

## Published checkpoint

Code commit `b8b1931734ae6320055cfc324b389ab60450d544`, tree `920770e57b4537aa4a4a84fb337d1a39781fa99d`, follows the identity-test correction `b304fc6de0c6bd575156c96fb20aa67ebf97bc88`. Both were non-force-pushed to `codex/javascript-port` in draft PR #98. The original C# pin remains `3496ab91893a1e4ec9261b4833479f1799149cdc`, using SDK 8.0.425 / runtime 8.0.31 and Node 22.16.0. No merge or npm publication occurred.

This is a verification and test-port increment, not a claim that all HATCH functionality was newly implemented here. The boundary/spline models were already published in `dc12f59`, and the entity/affine models in `826b9b6`; `e51fe09` exported them and added eight supplemental tests. The unpublished full verification integration mentioned by those older model commits was not recovered. This checkpoint rebuilds independent verification rather than reusing their unverified coverage claims.

The source ZIP from Actions run `35493763373`, artifact `10600112378`, was restored and reproduced the exact `e51fe09` tree `9535836c01a7761db492a7a6c81aca043793b3d3`. Its SHA-256 is `5f3449d49b89e7f3b1af99ceb16360005be9544aa9017739303ad520dcc56650`. The failing supplemental test was reproduced locally before editing: seven passed and the identity test failed. Original C# sources, fixtures, support inputs and production JavaScript are unchanged in this continuation.

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

## Completed local verification

| Check | Observed result |
| --- | --- |
| Complete unchanged C# suite | **35,309 passed**, zero failures in each Debug and Release configuration |
| Mirrored JavaScript originals | **2,782 passed**, zero unexpected identities against either original inventory |
| Supplemental JavaScript suite | **479 passed**, no failures, skips or TODOs |
| New HATCH subset | **1,075 scenarios / 20,990 operations**, zero differences in Debug and Release |
| Complete entity differential | **7,391 scenarios / 57,725 operations**, zero differences in Debug and Release |
| Surface differential | **1,066 scenarios / 17,258 operations**, zero differences in Debug and Release |
| Fixed foundations and randomized geometry | **5,185 / 49,421** and **2,000** respectively, zero differences in both configurations |
| Existing raw, handles, OBJECTS, lifecycle, styles, patterns, database models, collections, NURBS and filesystem stages | Pass in both configurations |
| Direct reference math, added math and exp/log | **61,876**, **30,904** and **13,512** comparisons pass in both configurations |
| Independent development-reference MPFR audit | **30,904 comparisons**, zero differences at 512 and 1,024 bits, both configurations |
| Offline installed package | **352 files**, passed HATCH identity unlink, circle-to-ellipse transform, seed/clone isolation and boundary reconstruction, alongside existing checks |
| Chromium 144.0.7559.96 inline native ESM | All **124,216 comparisons** executed per configuration, no page errors; only the existing non-HATCH UCS/Bézier failures remain |
| Aggregate verification | **Fails in both configurations**, preserving the failures below and the full missing-work ledger |

The package is private and was installed offline with lifecycle scripts disabled; no npm publication occurred. The benchmark run is descriptive evidence, not broad performance acceptance or a .NET speedup claim.

### Remaining exact failures and environment boundaries

Release retains three differing observations in `coordinates/normal/115`; the corresponding browser scenario `coordinates/coordinates/normal/115` fails. Debug retains `BezierCurveCubic/CalculateTangent/double/5`, including its browser result. These involve existing NaN-sign differences and remain failures, without an allowlist or comparator adjustment.

The local ICU profile is rejected rather than silently replacing the pinned ordinal tables. HTTP-origin navigation returns `ERR_BLOCKED_BY_ADMINISTRATOR` in both local browser attempts; inline results do not substitute for HTTP qualification. The hosted workflow for code commit `b8b1931` is run `35500670795`. Its pending or later results are separate from the completed local evidence above. Windows qualification cannot be inferred from Linux success or from older hosted results.

## Completion ledger

**246/510 library source mirrors; 50/193 conformance-file mirrors; 2,782/35,309 original cases.** Missing: **264 library paths, 143 conformance paths and 32,527 original cases**. The seven HATCH library mirrors were present before this continuation; it adds three original test-file mirrors, not seven new production files. File presence is not complete member/signature/behavioral qualification.

Typed `DxfDocument`, registered collections and source/reactor graphs, remaining entities and APIs, typed DXF reading/writing, original examples and transport tests, exact runtime/platform profiles and performance acceptance remain unfinished. Neither implemented-scope nor full-port qualification is green, and this is not an AutoCAD interoperability certificate.

## Source-bound fingerprints

Runtime: `4131bb6f6406d13e3eb4476a82bef791574f1379646146ad4115c73f5cba1e4b`.

Verifier: `e2c22371209834c5723ac6cb9ff9fae551cfa54e1134da7a4f7702faa0e92cef`.

The complete code tree was independently staged locally and matched the uploaded GitHub tree before committing. Documentation-only changes do not replace executable evidence.

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
