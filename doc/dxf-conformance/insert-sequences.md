# INSERT attribute-sequence identity and ownership

## Defect and correction

PR #211's first independent INSERT verifier stopped on a real serialization
omission: SEQEND lacked its group-330 owner. The source, output and resave also
assigned three different identities to each terminator. The writer created a
fresh temporary object on every save, while the reader explicitly discarded it.
Adding group 330 alone would therefore still fail the cross-save identity gate.

INSERT now retains its terminator as an owned DxfObject. The read-only
`Insert.EndSequenceRecord` property exposes it when an attribute sequence exists;
an ordinary attribute-free insert returns null. Document adoption allocates and
registers the terminator, and the writer reuses that object and handle. Reading
retains the physical common-header identity. The common group-330 owner always
names the actual INSERT, not its containing BLOCK_RECORD. Both singleton INSERT
and MINSERT use this path in every currently typed version and placement.

Old netDxf files without the owner remain readable: a missing owner is
materialized from physical sequence membership. An explicit zero, duplicate,
foreign, or containing-block owner is not silently substituted. Common-header
identities participate in the existing physical-source ambiguity validation.
The parser stops the attribute loop at the first non-ATTRIB record and requires
SEQEND when attributes or the follows flag are present, rather than consuming
unrelated following entities. An explicit empty sequence survives reload and
attribute synchronization. Inserts without attributes and without a retained
sequence do not acquire a spurious terminator.

## Lifecycle and common metadata

The terminator is available through GetObjectByHandle; document registration,
APPID reference binding and removal include it. Source layer references are
canonicalized and retained. XData, extension dictionaries, persistent reactors
and database references to the terminator use the normal registered-object
mechanisms. The writer separates common owner 330 from reactor 330 entries in
control groups. Removing a parent cannot strand a referenced terminator or its
extension dictionary. Removing/re-adding a plain sequence releases its old
registration and obtains a new identity.

A plain clone receives an independent terminator without the source handle.
Non-reference XData and source layer are copied. Cloning terminators with
extension dictionaries, reactors or nonzero XData handles requires an explicit
metadata graph mapping and is refused before cloning the parent/attributes.
This is conservative refusal, not dependency-complete cloning.

This increment covers terminator identity, membership, source layer and common
database metadata. It does not introduce a full SEQEND graphical-appearance or
application-private payload model, change POLYLINE record handling, certify
native font/rendering behavior, or qualify arbitrary version conversion. Empty
sequence retention and legacy-owner repair are stated library policies, not
claims that every AutoCAD producer emits the same optional fields.

## Regression definitions

132 new shared cases run through the existing INSERT suite in conformance,
ordinary installed-package consumption and all eight selected runtime profiles:
12 six-version/two-transport matrices, 96 malformed sequence cases, 12 legacy
missing-owner cases and 12 lifecycle cases. The matrix includes singleton and
array inserts in model space, paper space, a referenced block and an unreferenced
block, each with two ordered attributes and registered terminator metadata.

The matrices export 36 drawings /288 SEQEND /576 ATTRIB records. Checks cover
same-object repeated saving, cross-format reload/resave, exact handles/owners,
metadata graph resolution, following geometry, source bytes/stream lifetime,
attribute synchronization, empty sequences, plain clone/removal/re-add,
referenced-removal refusal, and source-identity/owner/framing errors. A new
independent verifier checks physical records and ezdxf, verifies the exact file
inventory, rejects altered/missing/duplicate owner/identity and attribute/XData
corruptions, and requires zero independent audit errors/repairs.
The physical common owner is checked before loading: ezdxf 1.4.4 rewrites
SEQEND ownership to the containing layout in `link_seqend`. Its parsed object
is used for identity/link and metadata checks, not to certify the original
owner field after that normalization.

The prior 341 shared INSERT cases, 288 geometry/scalar drawings and their
independent checker remain unchanged. Expected complete inventories become
95,029 conformance cases and 244 independent scripts. These counts describe
registered tests, not successful execution. Hosted evidence for the final source
head is required before merging.

## Diagnostic evidence and boundaries

Downloaded first-head Linux Release artifact 10861463518 from run 36129370737
has SHA-256 `a84641ac66ecfca146a992d215adeb9edca40d29a019acfd84dd3e30a9e0a813`.
Its actual results contain 94,897 C# passes and 242 passing independent scripts;
`verify_insert_geometry.py` fails on the missing group330. Inspection additionally
shows changing SEQEND identities across the three save stages.

A local diagnostic copy of those 288 drawings, with only terminator identities
and owner fields repaired before independent serialization, passes the unchanged
INSERT verifier: 7,056 sequences and 210,914 rejected corruptions, with zero
independent geometry audit errors/repairs. This isolates the two defects. Those
rewritten diagnostic files are **not C# output from the correction**, are not
committed as fixtures, and cannot replace final-head qualification.

The new verifier also passes a separately constructed 36-file checker self-test
and rejects 2,306 corruptions/inventory changes. These constructed inputs are
not production C# execution evidence. All 98 existing local Python unit tests
and the generated 297-row/nine-version coverage ledger check pass.

No local .NET SDK/runtime is available. Native AutoCAD open/AUDIT/save/reopen,
historical typed dialects, dynamic blocks, private FIELD/TABLE caches,
dependency-complete imports and general DXF version conversion remain outside
this correction. Full AutoCAD parity is not established.

## FIELD-host checker integration

The first corrected-head Linux Release artifact (run 36136669637,
artifact 10864592964) contains 95,029 passing C# cases and 243/244 passing
independent scripts. Both INSERT verifiers pass on actual emitted drawings.
The remaining FIELD/text-host checker still expected the former ownerless
SEQEND packet and canonicalized newly generated terminator identities. That
old workaround is now removed: the checker requires the actual INSERT owner
and leaves every terminator identity untouched for the whole-record
before/after comparison. All existing field-result, host-text, reference and
unselected-record expectations remain. Additional actual-output mutations
challenge every SEQEND field and a changed record identity; unit tests reject
an otherwise well-formed before/after identity change as well as missing,
duplicate, zero and foreign owner fields. This strengthens the identity gate
rather than allowing both old and new packets or ignoring ownership.

Primary references:
- Autodesk INSERT sequence flag and fields:
  https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-28FA4CFB-9D5E-4880-9F11-36C97578252F.htm
- Independent ezdxf handle/ownership model (INSERT owns ATTRIB and SEQEND):
  https://ezdxf.readthedocs.io/en/stable/dxfinternals/handles.html
