# Stored HATCH source boundary relationships

This module retains the ordered source-entity references attached to HATCH
boundary paths and requires those references to resolve to retained entities in
the same block as the HATCH. It also maintains the source-use bookkeeping when
paths or hatches are added, removed, cloned or explicitly unlinked. The focused qualification passed **405 cases in Debug and 405 in Release**.
Both independent gates passed all 48 mandatory outputs with zero audit errors
or repairs. The isolated source and test commit is `f6f2c1a4b6c4d7f2fc70f73e317e888c36138e1c`;
the detailed records are in
[`qualification.json`](receipts/hatch-source-relations/qualification.json).

Autodesk defines HATCH group `71` as the associativity flag, with `0` for
non-associative and `1` for associative. Group `91` counts its boundary paths.
[Autodesk HATCH DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-C6C71CED-CE0F-4184-82A5-07AD6241F15B.htm).
Within each boundary path, group `97` counts the source objects and repeated
group `330` values identify those objects. The same reference defines group
`92` path flags, edge packets and polyline packets. Group `97` also has a
separate meaning inside spline-edge fit data; that field must not be mistaken
for the containing path's source count.
[Autodesk Boundary Path Data](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-DC5215D6-E73F-4DFF-8BE9-01CA9610FAEE.htm).

The reader consumes the declared source list exactly. Short, long or otherwise
malformed counted lists reject through the existing edge/polyline packet
grammar. The source resolver then requires each nonzero numeric handle to
identify the exact retained entity produced by an eligible physical source
record, still registered under that identity, and owned by the HATCH's block.
A generated object that happens to reuse a discarded record's handle cannot
satisfy the reference. Neither metadata-only objects nor a handle found solely
inside private group-`102` data can supply the physical identity. Hexadecimal
case and leading zeroes do not change identity. Nested private groups remain
private and do not override an eligible common-header handle.

Referenced identity must also be unambiguous. The reader conservatively rejects
a repeated common-header identity declaration in one source record, including
equal repeated values, and a duplicate physical declaration of the referenced
numeric handle across records. It rejects missing or zero targets, discarded or
unretained targets, absent eligible identities, self-reference, and targets
owned by another block. These checks close the typed source graph; they do not
claim to accept every possible DXF extension or resolve ambiguous producer
records by guessing.

The following distinction is part of this typed model's contract. Autodesk's
field tables establish the stored flag and list meanings; the model's supported
combinations and conservative rejection rules are stated here separately.

| Stored combination | Typed behavior |
| --- | --- |
| Associative HATCH, one or more retained same-block source references | Preserve every list occurrence, order and entity identity. |
| Associative HATCH, zero source references | Supported; keep the associative flag and the empty source list. |
| Non-associative HATCH, zero source references | Supported under the existing HATCH model. |
| Non-associative HATCH with source references on any path | Reject as an unsupported typed combination; do not silently clear the input references. |

Duplicate source references are valid stored occurrences within this contract.
One entity may appear repeatedly in a path, in several paths, or in several
hatches in the same block. Each occurrence contributes one live source use.
This permission does not extend to sharing a `HatchBoundaryPath` instance:
each path object may occur only once in one HATCH. Reusing a path within a
collection, across hatches, or through a constructor rejects before changing
the original association. Null paths also reject. Clone the path to obtain a
separate path object.

Adding a HATCH or adding a source-bearing path checks all incoming sources
before collection insertion and reactor changes. An already owned source must
belong to the actual destination block. An unowned source is adopted into that
block, including a nested definition; the active layout is not used to choose
its owner. Existing source uses in the destination block are retained without
trying to insert the same entity again. When a destination document is known,
source adoption also invokes the existing retained-record/adoption validators.
The focused cases exercise a detached retained `Polyline3D` from another
document through direct HATCH adoption, path insertion, and containing-block
adoption. Unsupported adoption rejects before allocating destination handles,
adding earlier plain sources, or changing destination membership. This is a
conservative rejection boundary, not a general cross-document source import
or dependency-remapping engine.

Removal and unlinking have different established effects:

| Operation | Source relationship and entity effect |
| --- | --- |
| Remove an entity still used by a HATCH | Refuse removal while live source reactors remain. |
| Remove one boundary path | Release that path's source occurrences, preserving remaining uses by this or another HATCH. Attempt source removal through the HATCH's actual owning block; a source still in use remains. |
| Remove the final path use | Remove an otherwise removable source from its owning block. Other existing removal guards still apply. |
| `UnLinkBoundary()` | Set the HATCH non-associative, release its source uses and clear its source lists, while retaining source entities in their document. |
| Remove a HATCH | Use the existing unlink lifecycle, retaining its former source entities. |
| Clone a HATCH | Create a non-associative copy with detached source lists and copied stored boundary geometry; leave the original source uses intact. |

Loaded persistent reactor backlinks are removed only after the last live use by
that HATCH ends. A remaining path use keeps that backlink, and backlinks for
other hatches remain. Explicit unlink, loaded HATCH removal and loaded path
removal are included in the focused registration. The source lists are exposed
as read-only collections; storing source references does not automatically
recompute the boundary geometry from those entities.

Output preflight visits HATCHes in every registered block, including modelspace,
paperspace and unreferenced definitions. Every stored source must belong to an
associative HATCH, be distinct from that HATCH, have the same block owner and
resolve to the same registered entity instance by handle. Invalid output state
rejects before writer preprocessing, handle allocation and destination-stream
writes. Debug retains exception behavior; Release retains the usual load-null
and save-false conventions. In this isolated source, the filename overload
calls `File.Create` before writer preflight and can truncate an existing file;
stream preflight does not make that wrapper transactional.

The independent input evidence contains 24 drawings: two evidence kinds, six
versions (R2000, R2004, R2007, R2010, R2013 and R2018), and ASCII/binary
transports. The pinned manifest is
[`tests/fixtures/hatch-source-relations/manifest.json`](../../tests/fixtures/hatch-source-relations/manifest.json).
It records gzip and decompressed-original hashes, git blob identities,
extracted/adapted packet hashes, generator and verifier hashes, producer-module
hashes, fixture hashes, and exact stored inventories.

Each of the six native originals contains `HATCH 24F` pointing to
`LWPOLYLINE 8E`, with original owner `1F`, associative flag `1`, one source
reference, and six stored LINE edges. The source carries a persistent reactor
to `24F`. The originals are the version-matched
`tests/fixtures/dimassoc/originals-gzip/example_YEAR.dxf.gz` files pinned to
LibreDWG/libredwg commit `34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`.
These are native entity packets transplanted into minimal independent carriers;
the entire original drawings are not claimed to have been parsed or audited.
The carrier defines the original `Tavolo 3` layer. Only the entity-level owner
`330` changes from `1F` to modelspace BLOCK_RECORD `17`; all other native ASCII
entity text pairs remain unchanged. Binary carriers encode the corresponding
typed tags. The verifier independently compares both fixture transports with
the original packets, allowing only the declared owner adaptation.

The producer drawings are actual ezdxf 1.4.4 API output. Both producer HATCHes
have an external edge path containing four LINE edges and an outermost closed
polyline path with stored bulges. Source LWPOLYLINE identities are `3A0` and
`3A1`; both HATCHes have associative flag `1`.

| HATCH | Edge-path sources, flags `1` | Polyline-path sources, flags `18` |
| --- | --- | --- |
| `3A2` | `3A0`, `3A0`, `3A1` | `3A1`, `3A0` |
| `3A3` | `3A1`, `3A0`, `3A1` | `3A0`, `3A0` |

Every fixture has a following modelspace LINE from `(1, 2, 3)` to `(4, 5, 6)`:
handle `300` for native carriers and `3A4` for producer drawings. An unreferenced
`OTHER_SOURCE_BLOCK` contains LINE `403` for wrong-owner rejection cases.
The fixture generator pins ezdxf 1.4.4, fixed metadata and Python hash seed `0`
to make regeneration deterministic. Independent input loads and audits are
recorded in the fixture manifest; input validation is separate from qualification
of netDxf's outputs.

The following inventory passed in each configuration:

| Focused group | Passed cases per configuration |
| --- | ---: |
| Retained native/producer sources, six versions, both input and output transports | 48 |
| Numeric identity normalization and nested private-header handling | 24 |
| Thirteen rejection variants across both evidence kinds, six versions and both transports | 312 |
| Shared-use lifecycle in modelspace, paperspace and block definitions, both transports | 6 |
| API, adoption, unique-path and loaded-backlink lifecycle cases | 15 |
| Total | **405** |

Each retained case performs three save/reload cycles with the middle transport
alternated and checks every actual transport signature. The final outputs are
named `hatch-source-{native|producer}-AutoCadYEAR-{inputBinary}-{outputBinary}.dxf`,
using `False`/`True` Boolean tokens. The independent gate
[`tools/verify_hatch_source_relations.py`](../../tools/verify_hatch_source_relations.py)
requires all 48 files. Both completed gates observed 72 HATCHes, 120 paths
and 264 ordered source occurrences. It compares source identity, order, count,
duplicates, associativity, path flags, stored edge/polyline geometry, source
geometry, actual block ownership, following geometry and the control block. It
also loads the documents through ezdxf and requires zero audit errors or
repairs. Missing a required output is a hard failure. Three negative controls
corrupt serialized ASCII output by dropping a duplicate with its matching count
adjustment, changing associativity, or substituting another valid source handle;
each must be rejected by the positive-output verifier.

Debug/Release result lists, library/test hashes, output hashes, run/build logs,
independent output receipts, a 47-output missing-file control and the successful
.NET Standard 2.0 Release build are committed in the receipt directory.
The standalone unchanged 96-input probe retains all 24 positive relationships
and now rejects the 72 relationships previously lost silently. The independent
reviewer's unchanged 16-case probe passes every corrected identity, adoption,
path-alias and loaded-backlink case. Full adjacent-suite qualification belongs
to the coordinated integration branch; the isolated duplicate run was stopped
when that integration run began. The source evidence and completed focused
qualification are recorded in
[`receipts/hatch-source-relations/source-evidence.json`](receipts/hatch-source-relations/source-evidence.json).

The bounded claim is stored source-reference closure and the specified lifecycle
behavior. Native CAD execution, graphical rendering, automatic associative
geometry updates, curve evaluation, geometric agreement between source entities
and edge packets, arbitrary affine transformations, and a general dependency
import engine remain outside this module's evidence. Existing HATCH pattern,
gradient, seed, polyline and spline contracts retain their separate scopes.
