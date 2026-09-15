# Retained POLYFACE records

Loaded ordinary `PolyfaceMesh` entities retain their physical coordinate VERTEX,
face VERTEX and SEQEND records. Handles, observed owner forms, common metadata,
optional fields and private packets survive a save in the source DXF profile.
The existing coordinate and face models remain the geometry API.

| API | Meaning |
| --- | --- |
| `PolyfaceMesh.VertexRecords` | Read-only records aligned with `Vertexes`; ordinal zero means signed DXF coordinate index 1. |
| `PolyfaceMesh.FaceRecords` | Read-only records aligned with `Faces`; each record's `Face` is the same existing face object. |
| `PolyfaceMesh.RecordSequence` | Original physical coordinate/face interleaving, followed by SEQEND. |
| `PolyfaceMesh.EndSequenceRecord` | Retained SEQEND, or null for a newly authored mesh. |
| `DeclaredVertexCount`, `DeclaredFaceCount` | Optional source header groups 71/72, preserving presence and advisory values. |
| `PolyfaceMeshRecord.SourceVersion` | Required DXF profile for output and clone adoption. |
| `Layer`, `Linetype` | Actual resolved resources, including null when absent. Face `Layer` follows the current face model. |
| `StoredOwner`, `UsesBlockRecordOwner` | Current ordinary group 330 identity and observed source ownership form. |

`PolyfaceMeshRecord` derives directly from `DxfObject` and exposes its common
XData, persistent reactors and extension dictionary. It is not independently
insertable as an entity. A newly authored mesh has empty retained record views;
its first load establishes physical record identities.

## Geometry and exact record ordering

Ordinary headers use POLYFACE flag 64, optionally with flag 128. Coordinate
records require flag 192 and `AcDbPolyFaceMeshVertex`; face records require flag
128 and `AcDbFaceRecord`. The reader validates physical identities and complete
subclass packets before admitting the graph. Fitted variants remain unsupported.

Face indices use the complete coordinate-record sequence, independently of the
physical position of a face packet. A face may precede all its coordinates.
Signed values preserve invisible edges. The magnitude calculation widens the
16-bit signed value before taking its absolute value, so -32768 can address
coordinate 32768 when it exists. Header counts are advisory and do not determine
allocation, role assignment or the actual sequence length.

Groups 71–74 identify fixed face slots even when physically out of order. The
first zero or omitted slot terminates the active face. Inactive later values,
including out-of-range values, remain stored and uninterpreted. The public face
model contains the active source indices. Editing its existing array patches
only corresponding qualified face slots and leaves other raw fields intact.
Editing a coordinate updates the qualified 10/20/30 values of its existing
coordinate record. Child identities, SEQEND and physical packet order remain
stable. Replacing a retained face object or changing record counts is rejected;
this slice does not introduce explicit mesh topology editing.

## Header and face property boundaries

The complete `AcDbPolyFaceMesh` subclass packet is retained. Groups 70, 71, 72
and 210/220/230 are interpreted only in their qualified public context. Private
102 groups and later private subclasses may contain identical group codes
without changing flags, advisory counts or the public normal. A typed normal
edit updates qualified source coordinates, or inserts a complete normal before
a later private subclass if the source omitted it. Common XData remains common
metadata and is emitted once after the stored subclass packet.

Common entity-header writing retains the pre-existing library normalization:
when absent in the native fixture it adds group 67=0, group 62=256,
group 6="ByLayer", group 370=-1, group 48=1.0 and group 60=0. The native gate
compares the **complete** parent header allowing exactly these named defaults,
and compares the complete stored subclass and all child packets without such
allowances. This is not a universal common entity-header retention API.

A typed `Face.Layer` change updates qualified common group 8 and the actual
resource dependency. The old layer can be removed once its remaining references
are released. Null omits group 8 and retains parent-layer inheritance. A typed
`Face.Color` change replaces qualified 62/420/430 color fields with the new ACI
and optional truecolor values; null omits them and retains parent-color
inheritance. Other metadata, including transparency, remains unchanged. Private
lookalike layer/color/index fields are preserved and cannot override typed edits.

## Ownership, removal and cloning

A coordinate or face record may name either its actual containing POLYLINE or
its actual containing BLOCK_RECORD in ordinary group 330. SEQEND must name the
actual parent POLYLINE. Structural `Owner` always names the mesh. Retained
records resolve references against accepted physical source identities;
private handle-like fields cannot manufacture an accepted source identity.

A clean mesh can be removed and re-adopted in its source document. Child handles
and their structural ownership by the detached mesh remain stable, while
registration is removed and restored. The parent can receive a new handle;
SEQEND and object reactor references follow its actual new identity. Moving
into another block updates the observed BLOCK_RECORD owner form. Textual
child-to-parent references requiring handle remapping conservatively block
removal. External semantic references to any child also block removal until
released; arbitrary 320–329 values do not act as dependencies.

Owned child extension graphs and private packets require a complete graph
operation and therefore prevent ordinary mesh removal. An explicit
`Objects.EraseOwnedTree(child.ExtensionDictionary)` clears that attachment when
its own incoming references permit erasure. Common metadata dependencies
participate in object erasure, resource removal and entity/block removal checks.
Faces with null inherited layers support removal and re-adoption.

Original retained meshes remain bound to their source document. A clean clone
has independent geometry, face objects, child metadata and binary XData, with
fresh child handles allocated only when adopted. Clone adoption retains the
source profile requirement. Private packets, owned extensions, external
references and nonzero XData handles require graph remapping and are rejected
before clone callbacks or destination adoption mutate the graph. Block clones
and partial owned-graph clones use the same boundaries.

## Admission and malformed input

The retained POLYFACE child sequence allows at most 65,536 VERTEX packets plus
SEQEND. Each child packet permits at most 4,096 physical tags after the opening
record marker; the shared Polyline3D/PolygonMesh/PolyfaceMesh child budget is
1,048,576 tags. The retained subclass header independently permits 4,096 tags.
Authored and retained Polyface normals must be finite and nonzero before save,
clone or adoption. Invalid values are rejected without repairing geometry.

Writer preflight counts public XData, reactors, extensions and typed face/header
edits before output or handle allocation. XData coordinates are scalar records
in this API: explicit X/Y/Z values each contribute one physical tag.

Malformed child identities, source owners, duplicate public fields, invalid
subclass sequences, missing terminators and active dangling signed indices are
rejected without an admitted document graph. The older geometry-only reader
ignored a non-XData group 71 after an XData 1001/1000 tail. Retained metadata
admission now rejects that malformed tail. Its original ASCII and binary bytes
are pinned in `tests/fixtures/polyface-records/negative-manifest.json`. A private
subclass before the actual required face subclass remains supported: public
geometry parsing resumes only at the required `AcDbFaceRecord` marker.

## Reproducible qualification

`tests/fixtures/polyface-records/native-manifest.json` pins six native LibreDWG
source files, compressed/uncompressed hashes, Git blob hashes and the full
11-packet extracted component. The extraction changes only the declared parent
owner to the carrier block record. Its carrier layers are synthetic; the slice
does not claim qualification of the original source layer tables.

`manifest.json` pins twelve unchanged ezdxf 1.4.4 producer files, across six DXF
profiles and both transports. Asymmetric coordinates, signed faces, separate
child layers, colors, XData, reactor cycles and owned XRECORDs exercise complete
physical identities and metadata. The independent raw gate checks packets
before ezdxf's object projection can normalize child owners or layers.

Run the conformance executable with `DXF_TEST_FILTER=polyface-records/` and
`DXF_TEST_ARTIFACTS` set to an empty output directory. Then run:

```sh
python tools/verify_polyface_records.py /absolute/output/directory
```

Run the existing `polyface/` suite and `tools/verify_polyface_grammar.py` as well.
Its 96 explicit schema drawings preserve advisory counts, face-first odd
variants, missing slots, inactive extrema and unusual slot order. The gate
retains independent active-geometry checks and 576 actual corruption controls.
