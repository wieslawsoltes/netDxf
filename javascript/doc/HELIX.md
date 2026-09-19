# HELIX and recovered exp/log checkpoint

This checkpoint advances PR #98 on `codex/javascript-port` from
`ff2db075585980f938502bd7b3688bbc2096ba0c`. The original C# reference remains
`3496ab91893a1e4ec9261b4833479f1799149cdc`, using SDK 8.0.425, runtime 8.0.31
and Node 22.16.0. Original C# sources, shared DXF fixtures and support assets
are unchanged. The package remains private; no merge or npm publication occurred.

## Published changes

| Commit | Change |
| --- | --- |
| `bbe6dfadea08905c4c6028e47e0d678780bfd53f` | Recover the unfinished exp/log sources, activate the runtime functions and require the exact differential |
| `6067bd76348b6a669ea272b7eaa08fbc6a14f4fc` | Add `Helix` and its `Helix.Geometry` partial at original relative paths |
| `4c1a10c47ccf07c7d640bd128f6e464bf4909adf` | Publish complete HELIX tests, C#/JS serialization, package commands and required browser/report integration |
| `6fed62c2398fd2e24afcd7e4f6607e3681aac23e` | Validate the complete installed mathematical sources and transfer the browser corpus as JSON strings |

The implementation/verifier tree at the last code commit is
`c3623928756a06b1b3b8b673315db711d3001b61`. Each GitHub tree was independently
reproduced from the local staged bytes before the branch was fast-forwarded.

The prior unfinished exp/log object tree
`f85bfa0b164b521e0562305498f956a9e77bf63b` was recovered in full and reproduced
exactly before integration. This is recovery of known uploaded Git objects,
not a claim that unavailable files from other sessions were reconstructed.
The previously local-only coordinate verification was already published by
`62a88b4d2e10cb7f6b1aee97671aa590252cc1aa`; it is preserved here, together with
all subsequent circle, arc, 2D/3D polyline, ellipse and spline work.

## HELIX usage

```js
import { Helix, Vector3, Matrix3 } from './javascript/index.js';

// The initial radius comes from start - axisBase. The supplied radius is terminal.
const coil = Helix.Create(
  Vector3.Zero, new Vector3(4, 0, 0), new Vector3(0, 0, 2),
  2,       // terminal radius: this example tapers from 4 to 2
  3,       // turns
  1.5,     // signed height per turn
  true,    // right-handed
  1e-5,    // conservative cubic approximation tolerance
  65536,   // maximum cubic segments
);
const point = coil.EvaluateDefinition(0.5);
const derivative = coil.EvaluateDefinitionDerivative(0.5);
const generatedBound = coil.GetApproximationErrorBound(coil.ControlPoints.length / 4);

coil.Radius = 3; // changes parameters, not the independently stored spline
const regenerated = coil.WithRegeneratedSpline(1e-5, 65536);
const storedSplineCopy = coil.ToSpline(); // preserves the old stored spline
regenerated.TransformBy(Matrix3.Scale(2), new Vector3(10, 0, 0));
```

`new Helix(existingSpline)` copies the stored spline without inferring HELIX
parameters. `Clone()` preserves both independent representations; `ToSpline()`
returns a detached spline without refitting. Control points, knots, weights, fit
points, tangents and inherited appearance follow the existing spline copy path.
An error bound describes the analytic definition's cubic construction; it does
not certify an arbitrary edited stored spline.

The model preserves axis-vector magnitude and value-copy semantics, signed pitch,
handedness, release metadata and constraint values. Parameter scalars are finite;
radius is nonnegative and turns are strictly positive. Binary64 storage retains
signed zero where accepted. Axis normalization uses scaled direct divisions to
avoid avoidable underflow or overflow for very small or large finite axes.

Analytic evaluation supports tapered, planar, ascending, descending and zero-radius
cases. Zero-initial-radius phase can use the retained start tangent, including
after reflection and regeneration. `Create` constructs cubic Hermite spans with
source-ordered endpoint/derivative arithmetic and a conservative logarithmic error
bound. Segment doubling respects an explicit budget; a tolerance that cannot be
met rejects rather than silently returning a coarser approximation.

Transforms stage finite checks for all analytic parameters and inherited control,
fit and tangent data before mutation. Nonsingular similarity transforms are
accepted and reflections invert handedness. Nonuniform scale and shear require
an explicit conversion to the stored spline instead. Regeneration builds a new
entity, retaining the source's documented appearance/XData fields while omitting
database identity and stale proxy/color-name payloads. It never modifies its source.

These are detached model and analytic authoring APIs. Registered document ownership,
complete HELIX DXF read/write workflows, native CAD interoperability and every
original HELIX document test remain separate work.

## Exponential and logarithm runtime

`DotNetMath.Exp` and `DotNetMath.Log` now use the recovered native JavaScript
adaptations of glibc 2.35's x86-64 FMA round-to-nearest implementation instead of
host `Math.exp`/`Math.log`. The seven preferred C source files are hash-pinned in
`tools/ReferenceMath/exp-log-manifest.json`; data generation uses those source
constants, not recorded .NET answers. The adaptation template and all generated
outputs are retained, readable and independently reproducible.

The existing LGPL-2.1-or-later notices and preferred sources remain intact. The
package check requires the new generation files, verifies every installed source
hash, compares runtime/source manifests, and reproduces outputs inside an offline
installed tarball. The explicit development-file filter still rejects unrelated
tools, tests and artifacts. The private package has 334 files on this Linux host.

The additional corpus has 13,512 exact `System.Math` comparisons: seeded inputs,
reduction boundaries, subnormal/overflow regions, near-one logarithms, negative
domains, signed zeros, and signaling/quiet NaN payloads. It is required separately
in `test:differential` and the aggregate report, and is also included in the full
browser corpus. This qualifies the tested reference profile, not every native
libm, architecture or rounding mode. The unrelated development-only MPFR audit
continues to be reported separately from production .NET compatibility.

## Local verification and accounting

The complete pinned C# suite passes **35,309 cases in each Debug and Release
configuration**. The JavaScript original suite passes **2,634 cases**, with zero
unexpected identities, and **431 supplemental tests** pass without failures,
skips or TODOs. The increment adds **37 complete original HELIX cases** and seven
HELIX supplemental tests; the recovered exp/log work adds three supplemental tests.
Typed wire and registered-document tests are not registered under shortened bodies.

Both configurations pass the enlarged entity corpus: **6,058 scenarios / 34,745
exact operations**, including **296 HELIX scenarios / 2,241 operations**. All
13,512 exp/log comparisons, 413 NURBS evaluations, 849 database-model scenarios,
61,876 existing direct reference-math comparisons, 30,904 additional mathematical
comparisons, 49,421 fixed-foundation operations and 2,000 exact randomized geometry
comparisons also pass locally. Raw, handle, OBJECTS, detached lifecycle, styles,
hatch, collections, Linux filesystem and unit-conversion regressions remain intact.

The independent development reference agrees with MPFR for 28,296 finite-bit and
2,608 NaN-class results at both 512 and 1,024 bits. This is not a change to the
production expected values. Package installation, preferred-source generation,
native-mirror regeneration checks and descriptive benchmarks are separate checks.

The required complete browser corpus is **121,817 comparisons**. The inline runner
now sends JSON strings instead of asking Playwright to recursively serialize the
large object graph; the actual Chromium renderer parses the same descriptors and
digests before running native ESM. No comparison or module is replaced by Node
execution. HTTP-origin execution remains a distinct required mode.

Local Chromium 144.0.7559.96 executed all **121,817 comparisons in each build
configuration**, with no page errors. Release retained only the UCS scenario;
Debug retained only the existing Bézier scenario. Both runs retain nonzero exit
status and complete counterexample data. Local HTTP-origin navigation is still
blocked with `ERR_BLOCKED_BY_ADMINISTRATOR`; the successful execution of inline
modules does not relabel that host restriction as an HTTP pass.

The first inline setup attempt was interrupted before returning a comparison
report. A concurrent-build Release corpus attempt ended with oracle SIGBUS and
was rejected; it was rerun from an isolated copy of the exact committed code
after compilation. The results above are the subsequent complete executions,
not estimates or partial comparison counts.

## Completed hosted evidence at code commit 6fed62c

Run `35434446493` executed the final code tree. Release artifact `10581843347`,
Debug artifact `10582300352`, and Windows artifact `10581392544` were downloaded,
SHA-256 verified, and inspected. Their runtime and native-path-ordered verifier fingerprints were
independently reproduced from the local committed bytes.

On hosted Linux Release, all 35,309 C# cases, 2,634 original JavaScript cases and
431 supplemental tests pass. The entity stage passes all 6,058 scenarios / 34,745
operations; the 13,512-case exp/log audit and all other report categories pass
except the retained coordinate mismatch and its two browser observations.
Chromium 152.0.7977.0 executes all 121,817 comparisons in **each HTTP-origin and
inline mode**, with no page errors. Both modes retain only
`coordinates/coordinates/normal/115`. The implemented-scope job is therefore
**not passing**, despite the new HELIX and exp/log comparisons matching.

Hosted Linux Debug likewise passes all 35,309 original C# cases, 2,634 original
JavaScript cases, 431 supplemental tests, the complete entity corpus and all
13,512 exp/log comparisons. Its report retains only the Bézier NaN-sign discrepancy
in baseline geometry and each browser mode. Chromium 152 executes all 121,817
comparisons in each mode with no page errors. Thus both configurations retain
exactly their documented numerical blockers rather than silently passing them.

Windows executes all 6,058 entity scenarios / 34,745 operations and retains
**395 differing operation outputs**: 284 in HELIX, 77 in the circle/arc/2D-polyline
category, 32 in ellipse and two in seeded display transforms. These exact
counterexamples remain failures; Linux compatibility is not Windows bit parity.
The independent Windows exp/log function corpus is not separately qualified by
that job. All 431 supplemental tests, five native-host tests, 82 original raw
atomic-save cases, 1,782 filesystem comparisons, and the 541-scenario style stage
pass. Offline package installation passes with 336 files, including the Windows
native host and metadata. The composite Windows job remains **failed**.

Downloaded artifact SHA-256 hashes:

- Release: `609d6cb16ee60a967ddb327efc1d604e0b3701738bf4bf3fee8c5defc97d79ff`.
- Debug: `aa2df9f26f407cee7f4f13487e36f17ed749473bafbb52c00e428d72e6c61b03`.
- Windows: `638956a5d1f20e0b1b6e19f8e8bfa6cf427479c049ff002f7a965113a3459a92`.

Windows verifier fingerprint:
`05135c2c0d781bb72d36d042742568d42bbb447407f6ea205670c0e386ff21f8`.
The runtime fingerprint is the same as the POSIX result below. A later
documentation-only commit does not substitute its workflow status for this
source-bound executable evidence.

## Remaining failures and full-port ledger

Release retains three differing observations of `coordinates/normal/115`, involving
UCS infinite/NaN inputs whose NaN signs differ between the original Debug and
Release builds. Debug retains the existing
`BezierCurveCubic/CalculateTangent/double/5` NaN-sign discrepancy. The local ICU
profile is rejected by the reference globalization contract. These are failing
gates, not accepted approximations or ignored failures.

**232/510 library mirrors**, **46/193 conformance-file mirrors**, and
**2,634/35,309 original cases** are present. That leaves **278 library mirrors,
147 conformance files and 32,675 original cases** missing. Source-file presence is
not exhaustive member/signature/behavioral qualification. Complete typed
`DxfDocument`, registered collections/ownership, the remaining entities/APIs,
typed reading/writing, original examples and broad platform/performance acceptance
remain unfinished. The full-port completion gate continues to fail.

Runtime fingerprint:
`9bcbe81d1a5fb70ca0a16d9ec27b2797db4ac5bb33875811afbae64b3b20fa07`.
POSIX verifier fingerprint at the final code tree:
`8e00647427cd14b9a6aaf2a6f94ea574d8017ffecc10dd0f618275f9ce02982e`.
Documentation-only updates do not change these fingerprints. Hosted checks are
reported only for their actual commit and platform; local tests do not qualify
Windows or replace the blocked local HTTP-origin lane.

## Reproduction

```sh
export DOTNET_ROOT=/path/to/pinned-dotnet
export CONFIGURATION=Release
# NETDXF_SOURCE_ROOT may select an unchanged checkout of the pinned C# reference.
node tools/dotnet.mjs inventory
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
node tools/dotnet.mjs native-port --check
node tools/dotnet.mjs conformance
# Complete builds before running readers of the resulting oracle assemblies.
npm test
npm run test:unit
npm run test:exp-log
npm run verify:exp-log
npm run test:differential
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-check.py
python tools/browser-inline-check.py
npm run verify
```

Repeat configuration-specific checks for Debug and run independent browser modes
without overwriting another active mode's shared corpus. Failed gates stay failed;
continue other checks to collect complete evidence. `verify:complete` remains a
failure until all missing APIs, original tests and platform qualifications exist.
