# INSERT/MINSERT affine, scalar and graphics fidelity

This increment follows the qualified portable decimal reader in PR #210 and
supersedes the singleton/dormant-spacing and epsilon-scale limitations recorded
in `insert-arrays.md`. It does not add dynamic block evaluation, clipping,
native font rendering, historical typed dialects or native AutoCAD acceptance.

## Representable affine frames

Singleton INSERTs and rectangular MINSERTs now share one staged transform path.
The three unscaled source frame axes are transformed independently of position
and of the block's signed scales. Orthogonal target axes are decomposed into a
normal, rotation and signed component scales. Reflections retain handedness;
conjugated nonuniform scaling is supported when the resulting frame remains
orthogonal. All three axis pairs must satisfy the existing array admission
threshold: absolute dot product of normalized axes at most `1e-10`. The rebuilt
in-plane axes are also checked against that threshold. This is a numerical
admission tolerance, not exact symbolic orthogonality.

A general sheared frame cannot be encoded by normal/rotation/diagonal scale.
Such transforms now throw instead of changing the shape through an implicit
projection. Callers requiring shear must explicitly choose an exploded or other
representable entity model. Nonfinite matrix entries, nonfinite translations,
projective Matrix4 bottom rows, collapsed axes, nonfinite stored geometry,
overflow and nonzero scale/spacing underflow also reject. Matrix4 validation
precedes virtual Matrix3 dispatch. Publishing prepared normals bypasses virtual
Normal setters, like the other staged entity transform implementations.

Insertion and direction components use the existing exact dyadic transform
helper to avoid intermediate multiply/add overflow and cancellation. Axis
normalization uses component-wise division by the largest absolute component,
not an overflowing reciprocal of a subnormal length. Scale/spacing products
remain binary64 products: an underflowing intermediate may reject even if a more
elaborate product algorithm could represent the final result. Unrepresentable
transformed direction components can also reject. This is not universal exact
arithmetic or overflow-proof affine geometry.

Grid distances are measured in the containing coordinate system. Their two
frame vectors transform independently of block component scales and block unit
conversion. Nonzero spacing is transformed even while its row/column count is
one, so activating a dormant singleton grid after transformation gives the
correct locations. Counts and logical zero-spacing multiplicity do not change.

## Stored scales and authoring

The public Scale setter accepts finite, exactly nonzero components, including
negative, tiny and subnormal values, independent of MathHelper.Epsilon. Zero
components and nonfinite components reject. Position and Rotation assignments
also require finite values. Constructors and unrelated entity setters are not
redefined by this change.

The reader restores every finite source X/Y/Z scale directly instead of
replacing near-zero values with one. Omitted components still default separately
to one. Reader-only restoration and direct stored-scalar cloning additionally
retain positive and negative zeros. The two late drawing/block unit-conversion
sites use the restoration path, so hydration does not invoke public authoring
validation or invalidate a stored graphics cache. Unit conversion mathematics
and the writer's existing scale conversion are otherwise unchanged.

Identity returns without changing stored scale/normal/rotation, attribute bits,
object identities or graphics. Translation preserves scale/normal/rotation and
text shape parameters exactly while translating positions. A nonidentity linear
transform of a stored collapsed INSERT rejects; no epsilon scale is invented.
The writer continues to materialize all three scale fields. Original decimal
spelling and optional-field absence are not preserved by typed serialization.

Finite-zero retention is a file-fidelity contract, **not a claim that zero-scale
INSERTs are valid native AutoCAD geometry**. The independent ezdxf model has its
own near-zero scale validation/repair policy. Exact scalar bits, including tiny
and zero scales, are therefore checked from the physical DXF packets; no repaired
independent value is substituted for the source observation.

## Attribute preparation and graphics

The parent frame and every attached attribute candidate are prepared before any
live entity is changed. Numeric-only attribute carriers cannot share proxy data
or copy/replace live resources. They preserve the document MIRRTEXT context when
attached to a document; detached inserts/blocks use the existing default context.
Candidates reuse the existing standalone attribute transform mathematics. All
numeric outputs are checked finite before non-throwing field copies publish the
result. A late attribute failure therefore leaves earlier attributes and the
parent unchanged. Attribute instances, owners, handles, definitions, styles,
values and unrelated metadata retain their identity.

This staging does **not** replace the standalone attribute text metric, normal,
fit, oblique-angle or mirroring algorithm. Its existing clamps and native font
limitations remain. Nor does it provide thread safety, whole-document rollback,
resource callback transactionality or observation of deep block mutations.

Changed position, scale, rotation, counts or spacing clear the parent common
proxy graphics. Identical/refused assignments preserve absent, empty and
nonempty caches. Committed transforms invalidate changed parent/attribute
caches; identity and reader hydration retain them. Clones retain independent
common data through the existing final clone-copy step. Replacement graphics
are not generated automatically.

## Qualification design (not an execution claim)

The shared corpus defines 341 cases, registered in .NET8 conformance and run
unchanged in the ordinary installed NuGet consumer and all eight selected
asset/runtime profiles. It covers three planes, seven maps, two affine entry
points, singleton/array shapes, eight sign combinations, every Matrix4 entry
with nonfinite values, projective refusal, shear/collapse refusal, attribute
failure atomicity, detached ownership, virtual dispatch, finite direct edits,
no-op cache states and extreme finite cancellation.

Forty-eight geometry matrices combine six typed profiles, both source/output
transports and model/paper/referenced/unreferenced block placement. Each has
42 INSERTs and one attribute per insert; source, output and opposite-transport
resave produce 144 drawings /6,048 INSERT records. Forty-eight scalar matrices
independently inject seven scale/default profiles into valid text sources and
produce another 144 drawings /1,008 INSERT records. They include minimum
subnormals, both zero signs, maximum finite scales and omitted components.

The auto-discovered independent verifier requires exactly 288 files and 7,056
INSERT/ATTRIB/SEQEND sequences. Expected source bases and maps are specified
independently, not derived from recorded netDxf output. It checks physical
coordinate/scale/grid packets, ordered version-specific proxy data, XData,
sequence framing, cross-save handles/owners, block origins and following LINE
geometry. Independent ezdxf INSERT matrices check the nondegenerate geometry
corpus; that corpus additionally requires zero independent audit errors/repairs.
Scalar packet fidelity does not make an independent zero-scale geometry claim.
Altered scalar bits, geometry, attributes, duplicate fields, proxy data and file
inventories must reject. Tests of constructed checker packets are not C#
execution or native-producer evidence. Actual run results belong in PR evidence.

All earlier assertions and the two consolidated workflows remain. Complete
conformance is not claimed on every runtime merely because shared package
assertions execute there. The JavaScript port remains separate.

## Primary references

- Autodesk INSERT fields, coordinate system, scales, rotation and array spacing:
  https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-28FA4CFB-9D5E-4880-9F11-36C97578252F.htm
- Independent ezdxf INSERT API, frame representability and transformation limits:
  https://ezdxf.readthedocs.io/en/stable/blocks/insert.html
- Independent near-zero model admission and reader implementation:
  https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/insert.py

Native AutoCAD open/AUDIT/save/reopen, historical typed DXF, private FIELD/TABLE
cache regeneration, dependency-complete imports, full version conversion,
dynamic blocks, clipped/nested rendering and exhaustive text equivalence remain
outside this increment. Full AutoCAD parity is not established.

## Attribute-sequence qualification correction

The initial independent run found missing SEQEND owners and unstable terminator
handles. See [INSERT sequence identity and ownership](insert-sequences.md) for
the retained-record correction, 132 additional shared cases, metadata/lifecycle
coverage and the distinction between diagnostic replay and final-head evidence.
