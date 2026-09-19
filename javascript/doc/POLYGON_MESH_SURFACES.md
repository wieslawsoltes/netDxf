# Polygon meshes and parametric surfaces

This checkpoint extends the native JavaScript port on PR #98 from
`c87ab47daf7ea65557fa02618fb8712fa07a6a03`. The behavioral reference remains
C# commit `3496ab91893a1e4ec9261b4833479f1799149cdc`, SDK 8.0.425 and runtime
8.0.31. Original C# sources, fixtures, support assets and the existing reference
math implementation are unchanged. No merge or npm publication was performed.

## Published implementation

| Commit | Increment |
| --- | --- |
| `fd0f014fc7bbce18b40d0f640c8395fc32d80cff` | GTE basis functions, polynomial/rational surfaces, shared storage and Boost license |
| `3b8ccdd6c84f4aadfe4ed2ca0669c09f738bbcda` | PolygonMesh, original stored-record partial and PolygonMeshRecord |
| `21678ded75bb6c2fcbcafd66c39255887c8ec8a7` | Complete test/oracle/browser integration, required platform stages and package metadata |

The complete code tree is `948dabf56cc058dc275938de555383f8eda95ed7`.
It was reproduced from the local staged files before publication. The known
starting source was restored from CI artifact `10581318896` of run `35435531664`,
ZIP SHA-256 `e9bbd9795e5c1d54aacd517060a8c3ba4a4b584c89bfceeffd9c053b8c38b5f6`.
Its restored tree exactly matched the published checkpoint. No additional
unpublished project edits were found in the available archives. This is not a
claim to recover unavailable files from another session.

Seven original source paths are added: four GTE source-file mirrors and three
polygon-mesh mirrors. Shared runtime storage and the full BSL-1.0 license are
additional support files, not extra C# source-file coverage.

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

## Local verification

Both full pinned C# configurations pass **35,309 original cases**. JavaScript
passes **2,635 original cases** with zero unexpected identities, and **459
supplemental tests** with no failures, skips or TODOs. This increment adds only
one complete original identity, `polygonmesh/model/invalid-smooth-type`, plus
28 supplemental tests. No shortened document tests are counted as completed.

| Stage | Recorded local result in each Debug and Release configuration |
| --- | --- |
| New surface differential | 1,066 scenarios / 17,258 exact operations; zero mismatches |
| New polygon-mesh category | 258 scenarios / 1,990 exact operations; zero mismatches |
| Entire entity differential | 6,316 scenarios / 36,735 exact operations; zero mismatches |
| Existing numerical stages | 13,512 exp/log, 61,876 reference-math, 30,904 added math, 413 NURBS and 2,000 randomized-geometry comparisons pass |
| Fixed foundations | 5,185 scenarios / 49,421 operations pass |
| Raw, handles, OBJECTS, lifecycle, database models, styles, hatch, collections and Linux filesystem | Existing exact stages pass |
| Native source reproduction and bundled preferred-source checks | Pass; original C# and fixture fingerprint unchanged |
| Offline installed package | Pass; 343 files, with required Boost and LGPL license/source material |

A separate offline-install smoke check also executed both surface types and polygon-mesh
sampling/conversion from the installed tarball. This does not add original test identities.

The independent development-only MPFR audit passes at 512 and 1,024 bits. It
remains separate from production .NET compatibility. Descriptive benchmarks
were rerun; they do not establish performance acceptance or a general .NET speedup.

Real local Chromium **144.0.7559.96** executes all **123,141 comparisons** in each
inline-native-ESM build configuration without page errors. Release retains only
`coordinates/coordinates/normal/115`; Debug retains only
`BezierCurveCubic/CalculateTangent/double/5`. All new surface and polygon-mesh
browser scenarios match. The nonzero exit statuses and full counterexamples
remain intact. HTTP-origin execution was attempted independently in both
configurations and rejected locally with `ERR_BLOCKED_BY_ADMINISTRATOR`; inline
execution does not turn that host restriction into an HTTP pass.

Local verification also retains the pre-existing ICU-profile rejection. The
first Debug all-category attempt found that the general Oracle assembly had
not yet been built and was rejected as invalid JSONL protocol evidence. The
assembly was then compiled and the entire Debug qualification rerun; only the
known numerical/globalization failures remain. The initial long C# Release
execution was interrupted by the command host and rerun to completion from an
isolated copy of the pinned assembly. Interrupted attempts are not counted as
successful comparisons.

## Completed hosted Linux evidence at code commit 21678de

Run `35467438004` also produced Release artifact `10591842247` and Debug
artifact `10591634021`. Both ZIPs were downloaded, SHA-256 verified and inspected.
Their runtime and POSIX verifier fingerprints match those independently reproduced
from the local committed bytes. Documentation-only follow-ups do not replace
this executable evidence with a different workflow's status.

Both configurations pass all **35,309 original C# cases**, **2,635 original
JavaScript cases** and **459 supplemental tests**, with no skipped or TODO tests.
The full **1,066-scenario / 17,258-operation surface stage** and
**6,316-scenario / 36,735-operation entity stage** pass without mismatches.
All new polygon-mesh comparisons pass. The existing numerical, raw, object,
collection, style, hatch, filesystem, packaging and independent development-reference
checks also pass, except for the retained configuration-specific geometry result
listed below. The hosted reference globalization profile passes; it is not the
rejected local ICU environment.

Chromium **152.0.7977.0** executes all **123,141 comparisons in each HTTP-origin
and inline-native-ESM run**, in both configurations, with no page errors.
Release retains only `coordinates/coordinates/normal/115`, corresponding to the
three differing operations in the standalone coordinate stage. Debug retains
only `BezierCurveCubic/CalculateTangent/double/5`, also visible in its standalone
geometry result. Thus all four browser runs execute the entire expanded corpus,
but **neither complete implemented-scope job passes**. The full-port completion
gate remains failing. No expected values, tolerances or allowlists were changed.

Downloaded ZIP hashes:

- Release `10591842247`: `8ed77fc326d142d6b1abf959a778929fe55945ccf493509d1ec1c2ba60bc92a6`.
- Debug `10591634021`: `b58a01c61ebe327ba86273b242be63c676206386dd08b5e32aceed13d56f8bd1`.

The separately required randomized-geometry job and Linux filesystem/platform
job completed successfully. Those successes do not override failures elsewhere.

## Hosted Windows evidence at code commit 21678de

Run `35467438004`, artifact `10591728122`, was downloaded and SHA-256
verified as `bab7412ded40ec73787de78296122dcead0a6dde328774d1f02b0a8d36abc861`.
The runtime and Windows-native path ordering of the verifier fingerprint were
independently reproduced from the local committed bytes.

Windows passes all **1,066 surface scenarios / 17,258 operations** and all
**258 polygon-mesh scenarios / 1,990 operations**. All 459 supplemental tests,
five native Windows-host tests, 82 original raw atomic-save cases, 1,782
filesystem comparisons and 541 style scenarios also pass. The offline installed
Windows package passes with 345 files, including the native host and metadata.

The full entity stage executes all 6,316 scenarios / 36,735 operations and retains
**395 differing outputs**: 284 HELIX, 77 circle/arc/2D-polyline, 32 ellipse and two
seeded display outputs. The complete retained counterexample records are identical
to the prior `6fed62c` Windows artifact, not merely equal in count. No new mesh
category mismatch is observed, but the composite Windows job remains failing.

The new separate Windows exp/log stage executes all 13,512 comparisons and exposes
**269 differences in the existing math implementation: 173 Log and 96 Exp**.
They are newly measured platform differences, not accepted approximations. The
production math adapter still targets the documented Linux x86-64 FMA reference
profile; no Windows-specific numerical corrections or ignored failures were added.
The standalone result is retained separately from the entity mismatches.

Windows verifier fingerprint:
`1ac2d587919fc5718e637bcc2453f3ba7af745b8efb95481bfaf0afe29b49ae1`.

## Completion ledger and evidence identity

**239/510 library mirrors, 47/193 conformance-file mirrors and 2,635/35,309
original cases** are present. Remaining: **271 library mirrors, 146 conformance
files and 32,674 original cases**. File presence is not an exhaustive public
member/signature/behavioral audit. The typed `DxfDocument`, registered ownership
and collections, remaining entity APIs, typed IO, original examples and broad
runtime/platform/performance qualification remain unfinished. Full parity is
not verified and its publication gate continues to fail.

Runtime fingerprint:
`7687e35d410ec490e61271839e3129f9e637fd09bab3556d06ee5888abdd40f8`.
POSIX verifier fingerprint:
`05fd6b09bb5c15da8898a61611b1ac24f72f0b0b788a35c774caa84f23f34132`.
The documentation follow-up does not change those executable bytes. Hosted
results must be tied to their actual code commit and platform, not inferred
from local execution or a previous passing workflow.

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
