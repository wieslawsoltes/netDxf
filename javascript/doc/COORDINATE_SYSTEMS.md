# Coordinate systems, named views and viewport configurations

This increment mirrors `UCS`, `UcsOrthographicBase`, `UcsRelationships`, `View`,
`ViewUcs`, `View.LiveSection` and `VPort` at the original C# paths. It builds on
`994928bf120ea2a590124179c43f60b7dd4b78e8` without changing original C# sources,
fixtures, the generated geometry implementation or the reference-math backend.
These are detached models and explicit internal host adapters, not a complete
registered `DxfDocument`, VPORT collection, viewport entity or typed DXF codec.

```js
import {
  UCS, View, ViewUcs, VPort, Vector3, CoordinateSystem,
} from './javascript/index.js';

const frame = new UCS('Machine', new Vector3(10, 20, 0),
  Vector3.UnitY, Vector3.Negate(Vector3.UnitX));
frame.Elevation = 2;
const world = frame.Transform(new Vector3(1, 0, 0),
  CoordinateSystem.Object, CoordinateSystem.World); // (10, 21, 0)

const view = new View('Inspection');
view.Target = world;
view.Width = 40;
view.Height = 25;
view.Ucs = Object.assign(new ViewUcs(), { NamedUcs: frame });
const copy = view.Clone('Inspection copy');
// copy.Ucs is independent, but copy.Ucs.NamedUcs still references frame.

const configuration = VPort.Active; // A fresh reserved *Active record each time.
configuration.ViewHeight = 25;
configuration.ViewAspectRatio = 1.6;
configuration.NamedUcs = frame;
```

## Publication and delivery status

The production models and original/supplemental tests are published on PR #98,
branch `codex/javascript-port`, as two non-forced fast-forward commits:

| Commit | Published increment |
| --- | --- |
| `4b582712fa97c7f5f2aff59957c6858f9476ea67` | UCS model, orthographic state, internal reference adapter, four original cases and eight supplemental tests |
| `a4eb7e117a36fb752adc12e153c71bc73b13c6b3` | View, ViewUcs, VPort, live-section metadata, six original cases and fourteen supplemental tests |

Their complete model/test tree is
`f25da3266c0a3215bab59e0ba41dadfc9d91f415`. A clean export of that exact tree
passed all **2,569 original JavaScript cases** and **314 supplemental tests**, with
no failures, skips or TODOs. No original C# source, fixture, generated geometry,
reference-math implementation or license material was changed.

**The new coordinate differential and browser/gate integration is saved locally,
not published on the branch.** The GitHub tool rejected the shared C# verifier
file upload twice. Rather than activate an incomplete verifier or try another
route around the block, the publication excludes that entire integration. Existing
remote qualification gates remain unchanged. Local independent results below
therefore must not be described as hosted coordinate-corpus qualification.

The complete local continuation consists of eleven files: the coordinate and view
request corpora, JS/C# coordinate serializers, strict differential runner, shared
JS/C# verifier integration, browser-corpus integration, qualification and completion
gates, and the package test command. A patch and source-bound evidence are retained
with the conversation handoff. A successful current remote model/test workflow
would still not execute this new corpus until that separate integration is applied.

## UCS state and transformations

Origins and axes have value-copy semantics. Factories preserve the source's
normalization and evaluation order, including raw point-on-plane semantics and
the **radians** argument of `FromNormal` with rotation. Elevation is independent
of origin. An enumerable transform snapshots its transformation matrix and origin
before enumerating user input. Unrecognized coordinate-system combinations retain
the source's value-copy behavior rather than inventing another transform.

`OrthographicOrigins` is a stable live read-only dictionary adapter. It returns
copied vectors and supports `Count`, `ContainsKey`, `get_Item`, `TryGetValue`,
keys, values and iteration as `{ Key, Value }` pairs. Mutating IDictionary methods
reject with `NotSupportedException`. Use `SetOrthographicOrigin` and
`RemoveOrthographicOrigin` to author values. Internal `$set`/`$remove` hooks are
not supported authoring APIs. Deleted dictionary slots are reused in the pinned
runtime's order. Insertion invalidates enumerators; value replacement and removal
retain the .NET 8 version behavior.

`TryGetOrthographicOrigin(type, output)` writes to `output.value`, including a
fresh `Vector3.Zero` on a missing key. Base-UCS metadata preserves omitted versus
explicit-null group-346 state. A clone owns axes, overrides and XData but retains
the original base-UCS reference, including self/cyclic references; it does not
silently import or recursively clone that dependency.

## VIEW and VPORT behavior

Named views expose target/direction, center, height/width, lens length, clipping
planes, rotation, view mode, flags, render mode and camera-plottable state. Legacy
`Camera`, `Fov` and `Viewmode` aliases address the same underlying properties.
`IsPaperSpace` changes only its flag bit. Direction values must be finite and
exactly nonzero, but are **not** normalized: even a finite subnormal vector is
retained. Scalar guards, parameter names and signed-zero state follow the source;
view rotation is not silently normalized.

Viewport configurations include grid/snap settings, screen corners, view target
and direction, aspect ratio, lens/clipping, twist, render and display modes, UCS
vectors, elevation and named/base references. The configuration is not the
separate `Entities.Viewport` object. A viewport's equality and hash identity are
reference-based, unlike ordinary table-name equality. Hashes remain stable over
rename; their numeric values are intentionally process-specific, not claimed to
match a different .NET process. `*Active` recognition ignores case and surrounding
.NET whitespace, creates a reserved record, and does not make separate active
instances equal.

A `ViewUcs` bundle can belong to only one view. Assigning it twice to the same
view is a no-op; assigning it elsewhere fails before detaching the current bundle.
Detachment clears its internal owner so it can be reused. Clone creates a distinct
bundle attached to the cloned view, while retaining named/base UCS object identity.
Base-reference/orthographic consistency is checked by the original explicit
validation path, not by an invented earlier setter guard.

Null SUN with a retained presence bit survives cloning. Non-null owned SUN data
rejects the shallow table clone before clone-name validation, because the required
ownership-subtree operation is not implemented here. Live-section metadata retains
omitted versus explicitly null state, clear, clone and validation order. The
non-null `Section` entity and registered live-section graph are still unported;
structural host checks are supplemental tests, not native document qualification.

## Ownership adapters and API boundary

The internal UCS adapter checks existing registered targets and maintains the
host's reference counts. View/viewport name callbacks preserve validation and
assignment order and re-read ownership after user callbacks. These helpers do
not construct document collections, assign handles, canonicalize registered
names, import dependencies, register SUN/live-section data, or read/write DXF.
Properties and methods corresponding to C# internal hooks remain implementation
adapters, not a claim that the missing registered workflows are implemented.

## Executable evidence and retained failure

Ten complete original detached test cases were added: three UCS elevation cases,
one orthographic-origin case, five named-view cases and one VPORT validation case.
They bring original JavaScript coverage to **2,569**; **314** supplemental tests
pass. The new 22 supplemental tests do not inflate the original-case ledger.
Original registered-document and transport cases remain unregistered, not shortened.

The locally retained coordinate differential contains **1,020 scenarios / 5,365
operations**. **775 named-view/VPORT scenarios / 4,460 operations match in both
Debug and Release.** All **245 UCS scenarios / 905 operations** match Debug.
Release retains **three observations of one factory result**:
`coordinates/normal/115`, combining an infinite normal component with a NaN
rotation. The unchanged C# Debug and Release builds themselves choose different
NaN signs for the underlying Matrix3 product; JavaScript matches Debug. Direct
calls of both `Matrix3.Multiply` and `op_Multiply` reproduced that distinction.
Finite coordinate scenarios and all new view/validation/reference cases match.

The Release coordinate stage stays failing. It is a newly exposed numerical
boundary, not an accepted approximation or allowlist. Expected values, every
previous comparison and full counterexample records remain intact. The earlier
Bézier Debug NaN-sign and Windows Shape last-bit boundaries remain separate.

The saved integration includes all 1,020 scenarios in both native-browser modes,
raising its required corpus from 103,199 to **104,219 comparisons**. Source-bound local results
and hosted results must be reported separately; a queued workflow or a previous
passing Release checkpoint does not qualify this enlarged corpus. Full C# API,
registered ownership, platform/performance and native application qualification
remain unfinished.

## Local checkpoint and completion ledger

The unmodified pinned .NET source passed **35,309 original cases in each Debug and
Release configuration** in this session. Both native-lowering reproduction and
source-fingerprint validation passed. Original JS case accounting reports zero
unexpected identities: **2,569/35,309**, leaving **32,740 original cases** missing.
The source ledger is **200/510 library mirrors** and **34/193 conformance-file
mirrors**, leaving **310 library files** and **159 conformance files** missing.
File presence does not establish exhaustive member or behavioral qualification.

The published production files and the saved integration share runtime fingerprint
`2a2a21df1deb27866b2d35d0712b617c9ef5c3187606f63a967ff61f642def99`.
The complete **local integration** has POSIX verifier fingerprint
`c017f367311809ca6a89a590732ca3be8750e32debb0c6f9a1ac6bdc0dc1bcf5`.
The published model/test subset has a different verifier fingerprint because it
deliberately omits the new verifier files; do not mix evidence between those trees.
Documentation changes do not change either executable fingerprint.

Starting archive `javascript-source-checkpoint` from CI run `35315851036`, artifact
`10535596250`, was SHA-256 verified as
`0f6916643feb44e55de3a4878940e9feb987ed603c9332735baa80fec1a71679`
and reproduced original tree `a877b60d83f4a56279f5842282727fa49dedd5fc`.
Earlier local checkpoint archives contained no additional uncommitted project
changes to merge. This is recovery of committed source, not a claim to recover
unavailable edits from a failed session.

All pre-existing local entity, database-model, style, hatch, raw, handle, OBJECTS,
lifecycle, filesystem, fixed-foundations, randomized-geometry and direct mathematical
comparison stages still pass in both configurations, apart from the already
recorded Debug baseline Bézier result. The independent development-only MPFR audit
passes at both precisions. Offline package installation passes with **285 files**.
The local globalization profile remains rejected and HTTP-origin navigation returns
`ERR_BLOCKED_BY_ADMINISTRATOR`; neither result is treated as passing. These host
limitations do not replace required hosted evidence or permit weakened comparisons.

The local inline Chromium 144 Release run executed **104,219 comparisons** with
no page errors and retained only `coordinates/coordinates/normal/115`. A failed
digest is an error, not an accepted approximation. The complete result is retained
with the independent .NET counterexample. Full typed document construction,
registration, ownership, typed DXF IO, original examples, runtime profiles and
performance acceptance remain unfinished. This source publication is not a merge,
an npm release, or full C# parity.
