# Ordinary 3D POLYLINE child records

Loaded ordinary unsmoothed `Polyline3D` entities retain physical `VERTEX` and
`SEQEND` objects. Repeated saves preserve their identities, order, optional
fields, common metadata and source relationships. The implementation restores
the native `DIMASSOC 42F -> VERTEX 41E` reference described in the
[initial assessment](polyline3d-records-assessment.md); the repeated DIMASSOC path
remains opaque and association evaluation remains outside this module.

## Public API and admitted input

`Polyline3D.VertexRecords` exposes a read-only collection of owned
`Polyline3DRecord` objects. `EndSequenceRecord` exposes the terminating record.
Each object has its own registered `Handle`, `Owner`, `XData`,
`PersistentReactors`, `ExtensionDictionary`, `Layer`, `Linetype` and
`SourceVersion`. These are owned records, not independently insertable entities.
Newly authored polylines keep the existing geometry writer; their children become
retained records after a save and reload.

The admitted input is an ordinary 3D POLYLINE with no smoothing and ordinary
VERTEX flags 32. All six supported R2000–R2018 profiles are covered in ASCII and
binary. Required physical identity, owner, subclass sequence, flags and three
coordinates must be present and unambiguous. The parser rejects missing SEQEND at
the next physical record boundary. Admission is bounded to 65,536 vertices per
polyline, 4,096 tags per record and 1,048,576 retained child tags per document;
private control-group nesting is bounded to 32.

The structural `Owner` always names the containing `Polyline3D`. Native fixtures
use that same owner in ordinary group 330. Unchanged ezdxf 1.4.4 producer fixtures
instead use the containing BLOCK_RECORD for VERTEX group 330 and the POLYLINE for
SEQEND. `UsesBlockRecordOwner` and `StoredOwner` expose this distinction and follow
an explicit move to a different containing block. An unrelated POLYLINE, unrelated
BLOCK_RECORD, null owner or unresolved owner is rejected. Private group 5 and 330
values do not become physical source identities or ordinary owners.

## Editing, cloning and removal

Geometry remains in the existing mutable `List<Vector3> Vertexes` API. An
identity belongs to its index slot: equal-count coordinate replacement keeps that
identity; `Reverse()` reverses both points and child records. Duplicate coordinates
are deliberately not used to infer identity. Count changes, smoothing changes,
nonfinite coordinates and DXF profile changes reject before stream writes or
handle allocation. The stored optional and private packets have no cross-profile
regenerator, so cloned retained records also require their original profile.

Common named resources follow actual table objects when renamed, and their
references prevent removal. Child APPID references use the document's normal
XData registration and rename bookkeeping. Known reactors and extension
attachments resolve actual source objects. Empty metadata packets and explicit
null handles retain their stored presence. Private groups and unknown subclass
packets retain their data without projecting private coordinates, flags or
handles onto public geometry.

Complete geometry-only clones copy optional fields and safe XData, including
independent binary arrays, and allocate new child identities. Parent or child
nonzero XData handle references, child reactors, owned extensions, private packets
or external resource dependencies require a graph mapping and conservatively
reject cloning. The same checks cover containing block/INSERT graphs and generic
owned-metadata cloning before destination mutation. Explicit null XData handles
remain cloneable. Existing retained record sets cannot be adopted into a different
document; supported transfer uses a qualified clone.

Removing a parent unregisters its owned child identities. Incoming object,
XRECORD, XData, opaque, dictionary and custom-header references block removal.
Arbitrary header handles do not become semantic references. Owned child extension
graphs and private packets also conservatively block ordinary parent removal;
explicit owned-tree erasure remains available and clears extension attachments.
A same-document move retains child identities even though the existing collection
API assigns a new parent handle. Live reactor references are rewritten to that new
parent handle. Parent or child XData text pointing at the parent blocks removal,
because that text requires an explicit handle map before a move.

## Evidence and independent checks

The fixtures include six pinned native LibreDWG originals and their existing
explicitly mapped DIMASSOC extractions, plus 12 unchanged independent ezdxf 1.4.4
producer files. The producer manifest pins every SHA-256 and labels the
BLOCK_RECORD owner form. Extension dictionaries, XData and optional common
metadata in those producer files are independent authored evidence; the native
corpus did not contain positive child XData or extension examples. Private and
malformed variants are explicit test mutations and are not claimed as native
application output.

The focused conformance group has 382 cases. The independent verifier requires
exactly 90 output files: 24 native, 24 producer, 12 clones, 12 moves, 12 private
variants, two coordinate edits, two metadata edits and two parent-reactor moves.
It checks exact ordered child packets, native repeated paths, both owner forms,
owned XRECORD payloads, clone identity remapping and binary XData, and mutable
coordinate/metadata behavior. It rejects six actual-output corruption controls
covering identity, coordinates, layer, reactors, optional width and SEQEND
ownership. The independent object reader audits every output with no errors or
repairs. Reactor multiplicity is checked in its original tag stream because
ezdxf's object projection deduplicates reactor handles during input.

Implementation commit `e424679` passes all 382 cases in net8.0 Debug and Release.
Both independent runs pass all 90 outputs and all six corruption controls. The
[qualification receipt](receipts/polyline3d-records/qualification.json) pins the
source hashes, complete result lists and the unchanged independent reviewer
probe. That probe reproduces eight ASCII/binary failures before the final fixes:
parent XData captured an unrelated destination LINE during clone, a header
reference allowed child deletion, a moved reactor retained its old parent handle,
and child XData silently retained a stale parent handle. All eight then pass with
the same harness. Integrated full-suite and cross-platform CI results are tracked
by the repository's combined checkpoint rather than inferred from this focused run.

Run the focused group in both configurations, then verify its outputs:

```sh
DXF_TEST_FILTER=polyline-records/ DXF_TEST_ARTIFACTS=artifacts/vertex-debug \
  dotnet run --project tests/netDxf.Conformance -c Debug
DXF_TEST_FILTER=polyline-records/ DXF_TEST_ARTIFACTS=artifacts/vertex-release \
  dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_polyline3d_records.py artifacts/vertex-debug
python tools/verify_polyline3d_records.py artifacts/vertex-release
```

The optional test filter matches ordinal name prefixes and fails when no cases
match. Without it, the complete suite still runs. CI discovers the independent
verifier through the normal `tools/verify_*.py` entry point.

Legacy 2D POLYLINE conversion, fitted or smoothed vertices, polyface/polygon mesh
child records, arbitrary topology editing, full metadata-graph cloning and native
CAD execution remain outside this retained-record slice. Compile/runtime and
independent-reader evidence do not establish rendering or native-application
certification.
