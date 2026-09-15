# Explicit SECTION_MANAGER storage lifecycle

`document.Objects.CreateSectionManager(sections, requiresFullUpdate)` creates one
canonical `SECTION_MANAGER` object and the `ACAD_SECTION_MANAGER` named-object
root entry. `document.Objects.EraseSectionManager(manager)` explicitly erases that
exact registered manager and its qualified typed owned metadata. The caller
controls both operations. Creating, copying, editing or deleting a SECTION does
not automatically create a manager or maintain its membership.

The API stores public DXF records; it does not generate section geometry, evaluate
live sectioning, initialize an AutoCAD application, or promise that another CAD
application will leave an absent manager absent when it opens remaining SECTION
entities. Manager cloning and source-profile conversion remain unsupported.

## Evidence and canonical packet

The [Autodesk DXF reference](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-C1C9B840-F291-4CB2-8EBA-A94BC27DC46D.htm)
documents the `AcDbSectionManager` subclass, group 70 update flag, group 90 count,
and repeated soft group 330 SECTION pointers. Its name is `SECTIONMANAGER`;
the pinned native file uses `SECTION_MANAGER`. The existing loader accepts both
spellings. The factory always creates the native canonical `SECTION_MANAGER`.
The [ObjectARX class reference](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbSectionManager.html)
places creation under database management rather than a public constructor. The
netDxf factory accordingly belongs to `DxfObjectDatabase`; no detached public
manager constructor is introduced.

The unchanged AutoCAD R2018 producer file
`tests/fixtures/section/LiveSection1.dxf.gz`, pinned by its manifest, contains
manager `229`, root `C`, SECTION `228` and owned SECTIONSETTINGS `22A`. Its complete
manager packet is:

```text
0 SECTION_MANAGER
5 229
102 {ACAD_REACTORS
330 C
102 }
330 C
100 AcDbSectionManager
70 0
90 1
330 228
```

The root has `3 ACAD_SECTION_MANAGER`, `350 229`. The manager CLASS has C++ name
`AcDbSectionManager`, application `ObjectDBX Classes`, proxy flags 1024, present
instance count 1, and false proxy/entity flags. The root's original group 280 is
absent and group 281 is 1; this is evidence for retaining the caller's existing
root flags, not for imposing new flags. Fresh synthetic roots in the gate use
explicitly different flags to verify that boundary.

## Creation and atomic validation

The factory accepts actual registered same-document SECTION identities in stored
order. Repeats are retained, null members are rejected, and zero through 65,536
entries are supported. No name or numeric-handle match adopts a foreign Section.
Caller enumeration, including `GetEnumerator`, `MoveNext` and `Dispose`, finishes
before document validation or allocation. An attempted recursive creation poisons
the outer request even when the caller catches the nested exception. This is the
new creation contract; the separately qualified loaded-membership editing
contract is unchanged.

After enumeration the factory validates the actual root, an unoccupied anchor,
absence of any existing manager object (including orphaned or opaque managers),
compatible canonical CLASS metadata, current R2007/R2010/R2013/R2018 profile,
actual registered members, and handle availability. It also validates each distinct
requested Section with its existing metadata/ownership rules and checks the required
root's common extension, reactor and semantic XData references. These checks run after
disposal; they do not scan unrelated invalid entities. Handle reservation includes
retained ATTRIB/ENDBLK identities outside ordinary object lookup. Only the manager
receives a new handle. The commit installs its root owner, matching root reactor,
and soft dictionary entry without caller callbacks. Validation failures introduce
no partial manager and do not advance the allocator. Changes made by caller code
during enumeration remain caller-owned changes and are not rolled back.

The current profile becomes the new manager's immutable source profile. A later
profile conversion rejects before any output bytes or new handles. Explicit
creation does not synthesize a CLASS declaration; the existing writer supplies a
canonical declaration when needed for output. If a compatible declaration already
exists, all its metadata survives and a present instance count becomes one. An
absent group 91 remains absent in memory. Compatibility analysis changes neither
declarations nor counts.

## Explicit erasure and snapshots

Erasure validates the exact current manager registration, original profile,
original root entry and pointer strength, original reactor sequence, and actual
member registration. It then uses the established typed ownership traversal with
only the selected manager admitted through the otherwise categorical manager
erasure guard. Generic `EraseOwnedTree` continues to reject managers.

All aliases in the owning root are removed. Qualified typed owned dictionaries
and XRECORD metadata are erased; opaque or otherwise unqualified owned records
reject before mutation. Incoming references from outside the erased tree,
including references to owned metadata, block erasure. These checks include
semantic XRECORD pointers, dictionary defaults, reactors, XData 1005, retained
ATTRIB metadata, SECTIONSETTINGS references and semantic custom-header handles.
Arbitrary XRECORD groups 320–329 do not create an incoming dependency.

SECTION members stay registered and their outgoing membership guards are released.
Other incoming consumers can still protect them. An erased manager has a terminal
identity: its handle, packet and membership remain inspectable, while its database
and root owner are cleared; it cannot be reattached. Explicit recreation allocates
a fresh identity. `Sections` and `Tags` use the existing snapshot contract: prior
views remain unchanged after `ReplaceSections`, and erasure does not change the
last stored packet or previously returned snapshots.

Erasure retains an existing CLASS declaration and every metadata field. If group
91 was present, its value becomes the actual remaining count for that spelling;
if absent it remains absent. No unrelated class is created or edited, and no
absent manager declaration is invented.

## Qualification

`SectionManagerLifecycleTests.cs` is registered by
`RegisterSectionManagerLifecycleTests`. Its `manager-lifecycle/` cases cover all
four profiles and both transports, native input/output transports, creation and
recreation, empty and maximum lists, foreign identities, caller failures and
mutations, caught reentry, handle exhaustion/reservations, CLASS absence and stale
counts, incoming-reference release, owned metadata and root aliases, and terminal
inspection snapshots. Artifacts use `manager-lifecycle-*`, outside every existing
`section-*` exact-inventory gate.

`tools/verify_section_manager_lifecycle.py` requires all 40 emitted drawings. It
checks raw records through ezdxf's low-level tag parser, independently checks
canonical metadata and ordered counts/pointers, and audits each drawing. The authored creation packet baseline is taken after one ordinary save/reload,
before explicit membership editing. Native before/after comparisons require
surviving records to remain identical; only
the exact manager and its root anchor disappear, with only the present manager
CLASS count changing. The pre-existing writer creates a fresh temporary empty
`ACAD_LAYERSTATES` dictionary on every save. The verifier requires its exact empty
packet and reciprocal LAYER-table ancestry, then normalizes only that temporary
identity and its immediate dictionary entry. It does not claim preservation of
this generated handle. Authored recreation preserves surviving physical identities
and uses a fresh manager identity. The 276 deliberate mutations of actual parsed
output records test CLASS counts, member inventory, root flags and anchor strength,
reactors, update flags/counts, resynthesis after erasure, and unexpected payload, changed ownership, or extra entries in the narrowly
normalized temporary layer-state dictionary. This is storage
qualification, without native CAD execution.

The final production candidate is `5464b0385b44273733ae2701a2fa292465ccced0`.
Focused qualification contains 128 new lifecycle cases and 496 neighboring cases
(84 membership, 110 stored manager, 215 SECTION, and 87 typed erasure) per
configuration. Net8 Debug and Release builds preserve the ordinary 561 legacy
CS1591 warnings without suppressing diagnostics. The accompanying qualification
receipt records final assembly hashes, exact test results, raw-gate totals and
independent review evidence. Earlier `0442c91` review files remain historical;
final qualification reruns the required-source metadata fix rather than carrying
forward results from that earlier candidate.
