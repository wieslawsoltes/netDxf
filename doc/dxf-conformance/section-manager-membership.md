# Explicit section-manager membership editing

`DxfStoredSectionManager.ReplaceSections(IEnumerable<Section> sections,
bool requiresFullUpdate)` replaces the public ordered membership and stored update
flag of an already loaded, typed manager. It enables callers to choose the
manager's exact section identities without generating section geometry or
automatically maintaining membership when sections are created, copied or edited.

The [Autodesk Section Manager DXF reference](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-C1C9B840-F291-4CB2-8EBA-A94BC27DC46D.htm)
defines a group-70 update flag, group-90 count, and a corresponding list of
group-330 section pointers in `AcDbSectionManager`. The existing typed reader
qualifies that complete public envelope for R2007, R2010, R2013 and R2018, with
both `SECTION_MANAGER` and documented `SECTIONMANAGER` spellings. Replacement
uses those same constraints: zero through 65,536 entries, actual non-null section
entities, retained order, and retained repetitions. The Boolean update flag is
independent of list size and has no execution side effect.

```csharp
if (document.Objects.Root.TryGetValue("ACAD_SECTION_MANAGER", out var stored)
    && stored is DxfStoredSectionManager manager)
{
    // Each supplied Section is already registered in this document.
    manager.ReplaceSections(new[] { secondSection, firstSection, secondSection },
        requiresFullUpdate: true);
}
```

Caller enumeration finishes before document and target validation. Exceptions
from enumeration, more than 65,536 entries, recursive replacement, an invalid
source manager, and null, detached, erased or foreign targets leave the manager's
membership, tags and update flag unchanged. Replacement assigns no handles.
Changes made independently by caller enumeration are not rolled back; the method
validates the resulting document state before committing its own change. The
usual document requirement to avoid concurrent mutation still applies.

`Sections` and `Tags` are read-only snapshots. A collection obtained before a
successful replacement retains the old membership or packet; reading either
property again returns the new state. Section objects themselves retain their
usual mutable entity APIs. Replacement changes only the public group-70/90/330
packet, leaving the manager's identity, source profile, root entry, persistent
reactors, extension graph and XData intact.

Incoming dependency checks read the current section list. Removing the last
membership occurrence releases that dependency; retained or repeated members
remain protected from ordinary entity removal and owned SECTION erasure.
Independent references such as XData continue to protect a released section.
An empty membership list is allowed, including with the update flag set.

Foreign sections must be mapped explicitly to actual destination section objects
before passing them to a different already loaded destination manager. Coincident
handles and names do not authorize adoption. The manager itself still cannot be
created, cloned, erased or converted to another DXF profile. Its original root
entry spelling, ownership strength and persistent-reactor sequence remain
required. The [ObjectARX class description](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbSectionManager.html)
describes a database-managed object that cannot be instantiated directly. The
current native evidence does not qualify constructing that complete lifecycle,
so this API is limited to explicit public-list editing on qualified loaded
managers. Opaque managers remain opaque and have no membership-editing API.

Qualification includes the pinned, unchanged native R2018 source drawing
`tests/fixtures/section/LiveSection1.dxf.gz`. Its manager `229` is owned by root
dictionary `C`, has reactor `C`, and references section `228` with update flag
zero. Four edit cases load its original ASCII or field-preserving binary
conversion and save each output transport after explicitly selecting section
`228` twice and setting the flag. The independent verifier compares the complete
resulting manager record to the source common packet plus that exact requested
public edit. This proves a bounded edit against a native input; it does not claim
that a CAD producer independently emitted the new repeated list.

Synthetic cases cover both spellings and transports across all four supported
profiles, mapping, clear operations, metadata, atomic rejection and list bounds.
Both Debug and Release pass all 84 dedicated cases and 325 existing manager and
SECTION regressions, for 409 distinct cases per configuration. Disposal failures,
uncaught recursive edits, caller-caught recursive edits and changes to the root
or candidate target during enumeration have separate coverage. The
[qualification receipt](section-manager-membership-qualification.json) records
the frozen source, libraries, test results, native input and all new output hashes.
An additional [independent runtime review](receipts/section-manager-membership-independent/README.md)
passes 44 cases and audits 12 output drawings in each configuration. Its unchanged
harness, results, separate reviewed assembly hashes and exact compressed outputs
are preserved with the review receipt.
The verifier requires exactly 52 output drawings, validates the physical targets,
ordered references, independent flag, root anchor and exact manager CLASS fields,
and rejects 260 actual parsed-output corruptions. Every output must audit with
zero errors and zero repairs using ezdxf 1.4.4.

```sh
DXF_TEST_FILTER=section-membership DXF_TEST_ARTIFACTS=/path/to/artifacts \
  dotnet tests/netDxf.Conformance/bin/Debug/net8.0/netDxf.Conformance.dll
python tools/verify_section_membership.py /path/to/artifacts
```

The earlier [stored-manager qualification](section-manager.md) remains the
evidence for reader recognition, source identity, opaque boundaries and unchanged
native round-trips. This increment does not qualify automatic manager membership,
live sectioning, section generation, complete manager creation/clone/erase
lifecycles, native AutoCAD execution or complete-drawing byte identity.
