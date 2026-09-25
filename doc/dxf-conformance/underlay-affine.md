# UNDERLAY affine geometry and editing coherence

PDF, DWF and DGN references share the UNDERLAY geometry model: an OCS
insertion point on the wire, a WCS normal, rotation, and two page scales.
`Position` remains WCS in the public API. This increment changes the entity
transform and geometry/appearance setters; it does not replace the reader or
writer, decode external files, or introduce a new DXF dialect.

## Transformation contract

The complete candidate is calculated before publication. Nonfinite matrix,
translation, source geometry or result values reject. Matrix4 also checks its
entire last row: projective input is refused rather than silently truncated,
and refusal occurs before dispatch to a subclass's Matrix3 override.

The target normal is derived from transformed plane axes, not `A * Normal`.
Nonidentity linear transforms use positive scale magnitudes and an oriented
normal/rotation that reproduce the two signed source page axes. In particular,
off-diagonal reflections no longer rely on the product of three diagonal
matrix entries. Signed source scales are supported by this API transformation,
including save/reload of the resulting positive-scale representation.

The format has no independent skew axes. Transforms whose normalized page
axes have an absolute dot product above 1e-12 reject rather than silently
orthogonalizing the content. The shared planar helper also rejects numerical
rank loss at its existing 1e-14 cross-component threshold. A singular 3D map
can still succeed if it preserves a nondegenerate, representable page plane.
The result must satisfy the existing Scale model's zero-tolerance admission;
a collapsed/too-small scale is never replaced with MathHelper.Epsilon.
The existing angle normalization must reproduce the unit axis within 1e-12.
These are explicit binary64 representability policies, not native application
rendering guarantees or configurable physical drawing tolerances.

Exact identity preserves stored geometry bits and null, empty or populated
common graphics. Pure translation preserves the stored scale, rotation and
normal representation, including signed scales. Large translations are never
subtracted from transformed page corners to infer scale. Changed geometry
invalidates stale common graphics. Rejected operations preserve geometry,
clipping/definition references, ownership, handles, XData and graphics.
No virtual Normal callback participates in candidate publication.

## Public edits and hydration

Position, Scale and Rotation changes invalidate common graphics when their
stored binary64 values change. Contrast, Fade and DisplayOptions invalidate
on stored-value changes; ClippingBoundary invalidates on reference changes.
The previous setter admission rules remain in force. Refused scalar/zero-scale
assignments preserve state. No-op assignments preserve all three proxy states.
ClippingBoundary remains nullable; a null-to-null assignment is a no-op.

Loading is not a user edit: the existing late common-data attachment retains
stored graphics, and the unchanged final clone common-data copy preserves an
independently editable clone. The tests distinguish this hydration behavior
from subsequent public edits. Definition replacement/callbacks, mutations
inside a shared definition or a cast-mutated clipping list, and common entity
appearance properties are not changed by this increment.

## Verification design

The focused harness covers all three definition types, three source planes,
eight representable maps and both transform overloads; signed-source maps;
nonfinite/projective/refused transforms; three proxy states; editing/no-op and
range refusal; subclass callback isolation; large offsets/scales; and a
plane-preserving singular map. Forty-eight document matrices cover all six
supported typed profiles, both transports, modelspace, paper layout,
referenced blocks and unreferenced blocks. Each matrix emits source plus two
resaves: 144 files and 10,368 UNDERLAY records. Source stream lifetime,
handles, metadata, clipping, graph consistency, clones and following LINE
geometry are checked on load and reload.

The independent checker derives expected WCS geometry from specified input
bases and maps, then checks physical OCS fields and ezdxf's independent model.
It checks exact ordered clipping/proxy packets, positive scales, appearance,
definition resolution, ownership, placement, inventory and database audit.
Field mutations and stale graphics must be rejected. It fails closed on a
missing or extra fixture. Executed totals belong in the PR evidence, not an
assumption inferred from the number of test definitions.

The installed-package shared body exercises reflected page geometry,
projective/shear refusal, no-op/cache hydration and a post-load appearance
edit within its existing twelve version/transport scenarios. The ordinary
consumer and eight exact-asset/runtime profiles reuse the same body. The two
consolidated workflows remain unchanged; the new independent checker is
discovered by the existing CI runner.

## Known boundaries and next work

The existing ReadUnderlay method still applies Math.Abs to incoming X/Y scales
and repairs near-zero values to 1; its Z scale is ignored. Therefore unchanged
signed-scale source files (including identity/translation of signed API
objects) do **not** gain signed scalar wire fidelity from this increment.
Nonidentity transforms canonicalize to equivalent positive scales, but this is
not a substitute for fixing and qualifying the signed-scale reader. Arbitrary
malformed input, source lexical preservation, signed zero/tiny-scale wire
fidelity and general version conversion remain outside the qualified scope.

Native PDF/DWF/DGN rendering, file availability, clipping visual acceptance,
application open/AUDIT/save/reopen, historical typed dialects, complete private
FIELD/TABLE/cache regeneration and dependency-complete imports are not claimed.
Full AutoCAD parity and the separate JavaScript port remain unestablished.

Primary references:
- Autodesk UNDERLAY DXF fields: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3EC8FBCC-A85A-4B0B-93CD-C6C785959077.htm
- ezdxf underlay model and OCS clipping: https://ezdxf.readthedocs.io/en/stable/dxfentities/underlay.html
