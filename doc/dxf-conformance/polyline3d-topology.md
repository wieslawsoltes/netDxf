# Explicit ordinary 3D polyline topology edits

`Polyline3D` now exposes three operations that update its points and retained
VERTEX records together:

| Operation | Result |
| --- | --- |
| `InsertVertex(index, position)` | Insert at an index from zero through the current count. A retained sequence receives one new ordinary VERTEX identity. |
| `RemoveVertexAt(index)` | Remove that point and record after checking incoming dependencies. No referenced object is cascaded. |
| `MoveVertex(fromIndex, toIndex)` | Move the exact point and record pair to its **final** zero-based index. Both indices identify existing points. |

Equal coordinates remain distinct identity slots. `Reverse()` still reverses
both collections. The existing mutable `Vertexes` list remains available;
changing its count directly still invalidates a retained sequence and rejects
output. These methods supply the explicit topology operation that the earlier
[retained-record slice](polyline3d-records.md) deliberately withheld.

For retained records, the parent must already be registered in its bound source
document, with its source profile unchanged. A detached geometry-only clone
must be adopted into a document before these operations can run. A detached
original must return to its same document; existing foreign-adoption guards
remain applicable. Newly authored polylines without retained records use the
methods as bounded, finite-coordinate list edits and allocate no child handles
until the ordinary writer does so.

Insertion creates a canonical flags-32 VERTEX with the parent's current layer
and ordinary POLYLINE group-330 ownership. It copies no optional widths, private
subclass data, XData, extension dictionary or reactor list from a neighboring
record. Existing VERTEX and SEQEND identities remain unchanged. A source using
BLOCK_RECORD ownership can therefore retain its original records alongside a
new record using the equally supported POLYLINE owner form. The new record is
authored data; it never enters the reader's map of accepted physical source
identities. Unicode layer names follow the normal output encoding.

The allocator checks both registered objects and retained owner-held metadata
identities, including ATTRIB and ENDBLK, before choosing the new handle. It also
checks the existing 65,536-vertex admission limit, 4,096-tag record limit and
1,048,576-tag retained-child document limit. The latter counts the currently
emitted metadata and XData, including edits, rather than only the original raw
packet length. These are library admission limits, not general DXF limits.

Removal checks actual object identity and semantic handle references from
retained metadata carriers, XRECORDs, dictionaries, typed object graphs, opaque
standard fields and custom header values. Arbitrary handles in groups 320–329
do not become semantic dependencies. The native opaque DIMASSOC repeated path
to a VERTEX prevents deleting that record; it remains opaque and is not
evaluated or converted to an index. Private child payloads or owned extension
graphs also prevent direct deletion. A caller can explicitly erase a supported
extension subtree first using the existing object lifecycle API. Outgoing
reactors alone do not cascade to their target or prevent deleting an otherwise
unreferenced record.

A successfully removed `Polyline3DRecord` has `IsRemoved == true`, has no owner
and is no longer registered. Its retired handle and remaining payload stay
available to callers that held the record for inspection. There is no record
resurrection or insertion-by-record API. APPID references and internal metadata
subscriptions are released. Movement and removal consume no handles, and the
SEQEND identity survives an empty sequence and subsequent insertion.

All argument, current geometry, profile, registration, dependency and admission
checks occur before point/record mutation or handle allocation. Equal-index
movement still validates the current retained sequence. Operations use no
caller clone callbacks. Resource copying and graph mutation remain subject to
the existing retained-record clone boundaries.

The regression group is `polyline-topology/`. It exercises the six supported
R2000–R2018 profiles in ASCII and binary, unchanged native DIMASSOC packets,
unchanged independent ezdxf 1.4.4 child and owned metadata packets, identity
permutations, repeated saves, clone/adoption boundaries, Unicode layers,
allocation limits, low advertised source seeds and deletion dependencies.
The deliberate internal allocator-counter mutation is labeled separately from
native and independent-producer input. The independent verifier requires 98
outputs and checks eight actual-output corruption controls. Qualification
results are recorded separately after execution.

This adds explicit ordinary Polyline3D topology editing. It does not add
POLYFACE or PolygonMesh child metadata, fitted or smoothed retained sequences,
association updates, arbitrary private graph remapping or native CAD execution.
