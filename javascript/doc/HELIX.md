# HELIX and recovered exp/log checkpoint

This checkpoint advances PR #98 on `codex/javascript-port` from
`ff2db075585980f938502bd7b3688bbc2096ba0c`. The original C# reference remains
`3496ab91893a1e4ec9261b4833479f1799149cdc`, using SDK 8.0.425, runtime 8.0.31
and Node 22.16.0. Original C# sources, shared DXF fixtures and support assets
are unchanged. The package remains private; no merge or npm publication occurred.

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

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/HELIX.md). Current published scope is maintained in the [README](../README.md).
