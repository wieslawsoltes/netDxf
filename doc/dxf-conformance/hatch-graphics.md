# HATCH graphics invalidation and affine-matrix validation

The shared HATCH transform now rejects Matrix4 values with a nonfinite element
or a bottom row other than exactly `(0, 0, 0, 1)` before dispatching to the
existing virtual Matrix3 affine method. Previously the inherited adapter silently
discarded the bottom row. Valid inputs reuse the existing boundary, pattern and
seed transformation algorithms; there is no alternative hatch renderer.

After successful preparation, a nonidentity transform clears common proxy
bytes before publishing geometry. An in-plane translation or scale can leave
Normal unchanged and therefore cannot depend on the normal setter to invalidate
its cache. Failure during preparation retains geometry, associations, path/edge
identities, seeds and proxy state. Existing numerical limits and unsupported
pattern-transform rules remain unchanged. Existing identity transforms retain
boundary/pattern identities and cache bytes, and still explicitly unlink source
associations as specified by the previous transform contract.

## Direct edits and topology

Elevation assignment compares binary64 bits, using the existing primitive
assignment helper, before invalidation. It retains the public property's previous
admission policy: it does not introduce setter validation for NaN or infinities.
Identical bits, including a repeated NaN payload, preserve the attached cache.
Pattern replacement clears graphics only when the pattern reference changes;
null still rejects before mutation, and reassigning the same instance is a no-op.

Committed additions, removals, replacements and clears of BoundaryPaths clear
common graphics before public path observers run. Rejected/cancelled changes do
not invalidate the cache. Existing collection behavior is preserved: a cancelled
Add throws while a cancelled RemoveAt returns without removal. Once topology is
committed, a caller's later exception does not restore its stale cache. This does
not make collection callbacks or entire document operations transactional.

The reader attaches HATCH paths after resolving their source handles. This is
hydration of the geometry stored beside the input cache, not an edit. It retains
the already-validated private proxy byte-array reference across that phase while
reusing every existing source-validation, ownership and association handler.
No second public cache setter or globally suppressible event mode is introduced.
Subsequent public changes on the loaded hatch invalidate normally. Explicit empty
payloads remain distinct from absent payloads.

Direct edits inside a mutable path edge or HatchPattern instance are not observed
by these outer setters and collection events. SeedPoints/PixelSize remain stored
computation hints. Their mutation, arbitrary application XData, private caches,
proxy rendering and automatic graphic regeneration are outside this increment;
callers must explicitly clear or replace graphics for those changes.

## Verification

The focused C# matrix covers modeled edits with absent/empty/nonempty graphics,
polyline and line-edge paths, detached/owned associations, source geometry and
ownership, clone isolation, all Matrix4 positions with nonfinite inputs, each
projective-row position, normalized no-ops, cancelled mutations and exceptions
in public path callbacks. A virtual-dispatch probe checks refusal before the
Matrix3 override is called. Additional tests cover conic/spline transformation
and associative hydration of all three proxy states.

The wire matrix covers six existing typed profiles, both input/output transports,
modelspace, a paper layout, referenced blocks and unreferenced blocks. Its 288
source/output drawings contain 4,032 HATCH records. The independent checker derives
expected geometry from fixture inputs, compares complete hatch-specific packets
(including seeds and XData), verifies proxy fields only in AcDbEntity, loads each
drawing with ezdxf and checks graph integrity without repair. Count/presence,
coordinates, boundaries, independent owners, chunk bytes and inventory mutations
must fail; no comparison is allowed to confuse hatch path group 92 with the older
common graphics-length group 92. The existing 2013+ common group-160 output policy
is unchanged.

The package consumer adds refusal, no-op, translation, cache-hydration, topology,
pattern and elevation assertions before its existing per-version checks. The two
consolidated workflows and the eight packaged-target/runtime profiles remain.
Actual before/after, complete local and hosted results belong to the PR receipt,
not counts inferred from these definitions.

## References and scope

- Autodesk [HATCH group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm)
  describe the OCS plane, boundary list, pattern and seeds.
- Autodesk [common entity fields](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm)
  distinguish proxy graphics from entity-specific geometry.

Cache invalidation is a library editing policy, not native AutoCAD rendering
acceptance. No historical typed version is added, no private cache is regenerated,
and no native AutoCAD open/AUDIT/save/reopen, font/fit/leader, general conversion,
transaction-wide rollback or full all-version equivalence is claimed.
