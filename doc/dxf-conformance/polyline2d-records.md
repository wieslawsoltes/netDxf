# Retained ordinary legacy 2D POLYLINE records

A loaded ordinary legacy `POLYLINE` keeps its `AcDb2dPolyline` parent subclass,
physical `VERTEX` identities and `SEQEND` on output. Existing
`Polyline2DVertex` objects remain the geometry API. A newly authored ordinary
`Polyline2D`, or one loaded from `LWPOLYLINE`, keeps the existing lightweight
representation. The existing fitted reader/writer path remains separate and
has no claim to retained child metadata.

| API | Meaning |
| --- | --- |
| `Polyline2D.VertexRecords` | Read-only physical records aligned with the existing `Vertexes` list. |
| `Polyline2DRecord.Vertex` | The same existing `Polyline2DVertex` object at the corresponding index; null for SEQEND. |
| `Polyline2D.EndSequenceRecord` | Retained terminator, or null for authored/lightweight/fitted paths. |
| `LegacyDefaultStartWidth`, `LegacyDefaultEndWidth` | Read-only nullable legacy parent groups 40/41; distinct from lightweight group 43. |
| `GetEffectiveStartWidth`, `GetEffectiveEndWidth` | Resolve an explicit vertex override, including zero, before an inherited legacy default. |
| `Polyline2DRecord.SourceVersion` | Required source DXF profile for output and clean-clone adoption. |
| Record `Layer`, `Linetype` | Resolved common resources, or null when their original fields were absent. |
| Record `StoredOwner`, `UsesBlockRecordOwner` | Actual ordinary group 330 target and the retained producer ownership form. |

`Polyline2DRecord` derives directly from `DxfObject`. Common XData, persistent
reactors and extension dictionaries belong to that actual record identity.
Records cannot be independently inserted as entities.

## Ordinary geometry and optional fields

The retained slice accepts ordinary 2D flags zero, closure bit 1 and continuous
linetype bit 128. Omitted parent and vertex flags preserve their absence.
Ordinary child vertex flags are zero or omitted. Curve fitting, spline fitting,
tangent vertices, 3D polylines and mesh roles do not enter this retained schema.

Empty and one-vertex source chains retain their parent and actual SEQEND,
including optional defaults and metadata. They represent an empty or point
sequence without a further curve geometry claim. Reverse is a validated no-op
for those chains. Transform preserves the count and identities and transforms
the parent plane; a singleton point follows that plane transform.

The vertex model stores OCS X/Y. This bounded path requires each physical child
Z coordinate to equal zero. World positions use the parent `Elevation` and
`Normal`. A nonzero child Z is explicitly rejected; the implementation does not
flatten or reinterpret it. Non-world normals and finite nonzero elevations are
supported. Parent dummy X/Y values must be zero. The dummy point may be wholly omitted;
an unchanged omitted point stays omitted. An elevation edit then inserts a
complete contiguous 10/20/30 triple before any later private subclass. Partial
source dummy vectors are rejected, since a standalone Z field does not provide
the qualified point interpreted by independent DXF readers.

Vertex position, bulge, nullable start/end width overrides and optional vertex
identifier edits update only their qualified public fields. Absence stays
absent until an edit needs a field. Clearing an override removes that field and
restores parent-default inheritance. An explicit zero override remains present
and suppresses the corresponding inherited width. Legacy header defaults are
not materialized into each vertex. `ConstantWidth` remains a lightweight field;
assigning a non-null value to a retained legacy polyline is rejected.
`SetConstantWidth` keeps its existing behavior of writing per-vertex overrides.

`Reverse()` keeps each point object, record identity, vertex identifier and
metadata together. It reverses their order, transfers the preceding segment's
outgoing bulge and width data to the reversed segment, negates bulges, exchanges
start/end widths, and swaps nullable parent defaults. SEQEND remains the same
object. Double reversal restores point/record alignment and segment geometry.
Direct list replacement, reordering or count changes require a separate
topology mapping and are rejected before save, clone or adoption.

`TransformBy` computes all retained geometry before mutating it. Uniform scaling
scales inherited defaults and explicit width overrides while preserving absent
fields and explicit zeros. OCS point edits follow the transformed world plane;
physical thickness scales along the transformed normal. Wide segments and arcs
require a nonsingular uniform scale within their plane. Transforms that cannot
preserve that representation, nonfinite results and unsupported arc reflections
are rejected before changing coordinates, widths or identities.

## Packet boundaries

The complete qualified `AcDb2dPolyline` subclass packet is stored. Its public
flags, elevation, thickness, defaults and normal are updated only through the
typed model. Private 102 groups and later private subclasses can contain
lookalike groups 70, 10/20/30, 40/41 and 210/220/230 without becoming geometry.
New public fields are inserted before a later private subclass. Common XData is
handled once as common metadata, after the retained subclass packet.

Native complete-header comparisons permit exactly the existing common writer
normalizations observed in the two source chains: it adds group 67=0,
370=-1, 48=1.0 and 60=0, and writes existing group 62=0 before existing group
6="ByBlock" instead of the native 6-before-62 order. Parent identity/owner,
remaining common values, the complete retained subclass and all child packets
are compared without other allowances. This slice does not add universal
common-header retention.

## Source identity and graph lifecycle

VERTEX ordinary owner group 330 may name its actual containing POLYLINE or its
actual containing BLOCK_RECORD. SEQEND must name the actual POLYLINE. Structural
`Owner` always refers to the parent entity. The reader resolves these forms
against accepted physical source identities; private handle-like fields do not
create a source identity.

Clean removal unregisters child objects but keeps their structural ownership by
the detached parent. Re-adoption into the source document preserves child and
SEQEND handles. The parent can receive a new handle; actual object references
and ordinary stored owners follow it. Textual child-to-parent references that
would require a handle map conservatively block removal.

Incoming semantic references to either child role prevent removal. Current
record XData group 1005 and persistent reactors also prevent removal of an
ordinary target entity or its containing block until released. Arbitrary
320–329 values remain exempt. Child resources and opaque semantic references
participate in entity, block, resource and owned-object erasure checks. Private
packets and owned child extension graphs block ordinary parent removal.
An explicit `Objects.EraseOwnedTree(child.ExtensionDictionary)` can release an
owned attachment when its actual incoming references permit that operation.

An original retained chain is source-document and source-profile bound. A clean
clone gets independent vertex models and common metadata; its child handles
remain null until adoption. Clone adoption still requires the source profile.
Private packets, owned extensions and external dependencies need a complete
graph mapping and therefore reject clone before callbacks or allocation. Block
clones and partial owned-graph clones use the same boundary. Retained VERTEX
identifiers use their qualified legacy packet profile; the existing older-profile
identifier rejection for authored/lightweight output remains intact.

`AnalyzeVersionCompatibility` reports `STORED_SOURCE_PROFILE` on each actual
registered legacy VERTEX and SEQEND when a different profile is requested.
The lightweight-only `LWPOLYLINE_VERTEX_ID_PROFILE` rule does not apply to these
retained legacy records. The six-source by six-target profile matrix checks
the report against both text and binary saves and verifies that rejected saves
leave the stream and physical record registrations intact.

## Admission and qualification

The parent subclass permits 4,096 tags. Each child permits 4,096 physical tags
after its opening record marker. The shared retained child budget across legacy
2D, Polyline3D, PolygonMesh and PolyfaceMesh is 1,048,576 tags; the legacy chain
has an upper bound of 65,536 VERTEX records plus SEQEND. Writer preflight includes
metadata growth and newly required typed fields before output or allocation.
Clone and adoption also validate the retained chain's geometry and packet
budgets. XData coordinate components are separate scalar records in this API;
each explicit X/Y/Z component contributes one physical tag.

Malformed physical identities, duplicate qualified fields, invalid owner or
subclass grammar, unsupported child roles, nonzero child Z and missing SEQEND
are rejected without admitting a document graph. Source and edited geometry
must be finite, with a finite nonzero normal. Unsupported typed state is rejected
without silent geometry repair.

The native evidence is exactly **two R2000 chains** from the pinned LibreDWG
corpus file `test/test-data/2000/PolyLine2D.dxf`, commit
`34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`. Chain 1EF has two straight vertices,
omitted parent/child flags and inherited width 0.15. Closed chain 1FF has two
bulged vertices and inherited width 0.5. The eight complete native packets are
extracted into a declared neutral carrier; only the two parent block-owner
identities are remapped. The original compressed source, hashes and extraction
script are retained beside the fixtures. No native CAD application was executed.

Separate producer evidence consists of twelve unchanged ezdxf 1.4.4 outputs,
six R2000–R2018 profiles across ASCII/binary. These preserve actual generated
BLOCK_RECORD owner forms and rich child metadata. The producer omits zero-width
attributes on output; separate explicit raw zero-width cases prove the
absent-versus-zero contract. These raw variants are not represented as unchanged
producer outputs. The width-presence rule agrees with the independent producer's
[2D trace construction](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/render/trace.py).

The schema references are Autodesk's [POLYLINE group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
and [VERTEX group codes](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm).
Qualification uses `legacy2d-records/*` conformance cases and
`tools/verify_polyline2d_records.py`, which checks raw packets, actual owner
identities, geometry and metadata independently of the typed model and ezdxf's
normalized child properties. The final receipt records exact source, assembly,
test and gate hashes.
