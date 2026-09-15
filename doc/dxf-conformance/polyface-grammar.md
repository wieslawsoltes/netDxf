# POLYFACE face grammar and independent clones

The typed POLYFACE path reads and writes signed, one-based face indices without
turning padding into geometry. Index groups 71 through 74 identify fixed slots;
their physical order in a record does not reorder the face. An omitted slot has
the same terminating effect as zero. The first zero ends the active prefix, and
subsequent slots do not participate in range checks or geometry.

The [Autodesk VERTEX reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm)
defines the slot codes, one-based numbering, negative invisible-edge indices and
zero termination. The [Polyface Meshes reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-96B6288E-F413-46C0-968A-A314171C0AAE.htm)
states that header counts need not be correct and readers should accept unusual
coordinate/face ordering. Output emits coordinate records before face records.
One- and two-index faces retain the existing Point/Line explosion behavior;
the existing whole-mesh minimum of three coordinates and one face is unchanged.

The reader resolves each active index against the complete coordinate sequence,
including when a face precedes its coordinates. Empty faces, duplicate public
index-slot codes and out-of-range active indices reject through the ordinary
load failure path. The signed magnitude calculation uses an integer wide enough
to handle -32768: it is valid when coordinate 32768 exists. Additional unreferenced
coordinates are allowed; this change does not impose a signed-header-count cap.

The public face constructor accepts a one-to-four-slot array with a nonempty
active prefix. Thus `[1, -2, 3, 0]` remains a valid padded triangle. The default
constructor still creates four mutable placeholder slots, which must be populated
before adding the face to a mesh. Both mesh constructors validate references
against the supplied coordinates. Since callers can later mutate index arrays,
save preflight repeats validation across model space, paper space, nested blocks
and unused registered blocks before writing bytes or allocating handles. `Explode`
uses the same active prefix and rejects invalid mutated indices.

`PolyfaceMeshFace.Clone` preserves null Layer and Color values, which mean
inheritance from the parent mesh. Explicit face resources and index arrays are
copied. `PolyfaceMesh.Clone` now clones each face as well as coordinates and entity
metadata. Changes to cloned face indices, resources or layers do not modify the
source mesh or notify its event subscribers. Adding the clone to another document
registers its resources with that document.

Private child control groups and unrecognized subclass bodies cannot contribute
public geometry fields, and XData marks the end of public interpretation. A known
public subclass may resume after a private subclass. Unclosed private control
groups reject. This is a context guard for the existing typed geometry path;
arbitrary POLYFACE VERTEX/SEQEND metadata, original child handles and opaque child
packets are not newly retained or qualified. The separate stored POLYLINE3D child
record implementation is unchanged.

The mandatory output matrix contains 96 explicit schema cases: six typed versions,
both transports and eight face shapes. It covers one through four active indices,
negative indices, explicit and omitted terminators, repeated references, shuffled
slot tags, advisory counts and faces preceding coordinates. The independent gate
uses ezdxf to verify the physical coordinate/face/SEQEND sequence, exact signed
topology, coordinate order, actual child owners and following LINE. It requires
zero external audit errors and repairs and detects 576 independent mutations of
signs, zeroes, references, coordinates, owners and sequence boundaries.

```sh
python tools/verify_polyface_grammar.py /path/to/conformance-artifacts
```

The [qualification receipt](polyface-grammar-qualification.json) pins the tested
source and libraries, output hashes and test results. Focused Debug and Release
runs each pass 471 cases: 345 POLYFACE cases and 126 existing mesh-version
controls. The separate [independent review](receipts/polyface-independent/review-report.md)
passes 193 grammar/clone cases and 86 context/magnitude cases in each configuration.
The same frozen probes exposed 143 and 73 failures in their respective completed
original-code baselines; the supplemental original Debug process abort is also
retained. Its independent decoder checks another 204 outputs per configuration.
The receipt distinguishes compiled candidate identities from the final source's
subsequent XML-comment and line-ending cleanup; integrated build qualification is
reported separately.

All new drawing and
large-coordinate controls are explicitly authored schema/API evidence. This
increment adds no native AutoCAD qualification, arbitrary private child metadata
support, or complete drawing/child packet identity guarantee.
