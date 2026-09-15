# Immutable stored section manager

`DxfStoredSectionManager` exposes a loaded section manager's stored update flag
and exact ordered section identities. It preserves the original `SECTION_MANAGER`
or `SECTIONMANAGER` spelling and the complete subclass packet through ASCII and
binary saves. There is no public constructor or editable section collection.

The [Autodesk Section Manager DXF reference](https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-C1C9B840-F291-4CB2-8EBA-A94BC27DC46D.htm)
identifies the `AcDbSectionManager` subclass, its group-70 update flag,
group-90 count, and ordered group-330 section pointers. Its spelling omits the
underscore. The [ObjectARX manager description](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-AcDbSectionManager.html)
describes a database-managed object that callers obtain from the database.
The unchanged, pinned R2018 drawing in `tests/fixtures/section/LiveSection1.dxf.gz`
provides the native underscored spelling: object `229` is owned by root dictionary
`C`, appears under `ACAD_SECTION_MANAGER`, and references section `228`.
Its update flag is zero and its native CLASS proxy flags are 1024.

The typed scope is deliberately limited to the public ordered envelope in R2007,
R2010, R2013 and R2018 profiles. The update flag must be zero or one; other stored
values remain opaque. Counts from zero through 65,536 must agree with the actual
pointer list. Every pointer must resolve through the reader's source-record
identity proof to an actual `Section`; null, missing, or wrong-type targets reject.
Repeated targets remain repeated. Padded or lowercase handles resolve by numeric
identity; the shared typed reader normalizes their spelling.

The manager must belong to the actual source root and its
`ACAD_SECTION_MANAGER` entry. Root-entry lookup follows dictionary case rules;
the original name spelling and ownership strength are retained. Private common
fields, private subclasses, application control groups, unknown payload fields,
and earlier profiles retain whole opaque objects. Malformed recognized public
packets reject, including absent or duplicate scalar fields, inconsistent counts,
and bare extended-data codes outside a real XData application group. Extended
codes inside a private subclass or private application group remain opaque data.

`Tags` excludes common metadata and real XData. Common metadata keeps its ordinary
APIs, but changing the source root entry or persistent reactors causes schema
validation and save preflight to fail. Source-profile conversion also fails before
output. The manager cannot be cloned or erased, including through an enclosing
ownership graph. Referenced sections remain protected by the existing entity
removal and section erasure checks. The section list is not automatically rebuilt
when other sections are added or copied, and the update flag is not recomputed
after section edits.

For example, loaded references can be inspected without interpreting generation
or live-sectioning behavior:

```csharp
if (document.Objects.Root.TryGetValue("ACAD_SECTION_MANAGER", out var stored)
    && stored is DxfStoredSectionManager manager)
{
    bool requiresUpdate = manager.RequiresFullUpdate;
    foreach (Section section in manager.Sections)
        Console.WriteLine($"{section.Handle}: {section.Name}");
}
```

The focused tests distinguish evidence sources. Four native cases load the
unchanged original or its field-preserving binary transport conversion and emit
both transports. Thirty-two explicitly synthetic schema cases cover the four
supported profiles, both object spellings, empty lists and repeated references.
Sixteen opaque output cases cover private boundaries and an older profile.
Additional malformed, lifecycle and numeric-identity controls exercise source
resolution, read-only collections, CLASS handling, mutation refusal and preflight.
Real XData has separate positive coverage. In each of Debug and Release the final
focused run passes 339 cases: 110 manager cases, 30 entity-identity regressions,
and 199 existing section and lifecycle cases. An additional Debug source-reference
run passes 114 cases. The [qualification receipt](section-manager-qualification.json)
records source, library, fixture, output and result identities.

Independent review also exposed a generic retained-entity identity defect before
manager resolution: a missing common handle terminated Debug processes through an
assertion, and a repeated same-value handle was accepted. The separate reader fix
rejects missing, zero or duplicate common identities through the ordinary load
error path. It checks retained entities only; dedicated legacy sequence readers
remain unchanged and valid POLYLINE/VERTEX and INSERT/ATTRIB/SEQEND sequences are
tested across all six typed profiles.

A separate [independent review receipt](receipts/section-manager-independent/receipt.json)
preserves its frozen 52-case harness and before/after results. The initial manager
draft passed 46 cases; the six failures covered root-name casing, missing source
identity and duplicate source identity in both transports. The corrected Debug
and Release libraries each pass all 52 cases. The public loader throws for these
malformed records in Debug and returns null in Release under its existing
behavior. The probe accepts both and isolates missing-identity inputs in child
processes so the earlier process termination is recorded. The frozen harness
contains the local .NET runtime path used for those subprocesses.

The independent gate requires all 52 output drawings, verifies the complete native
manager packet against the pinned original, checks physical section targets and
root ownership, and validates exact CLASS fields. It checks the synthetic fields
against explicit schema expectations and the private suffixes against their
declared input variants. Its 160 corruption controls alter parsed output counts,
root entries, target types, flags, class metadata and private packets. Every
output must audit with zero errors and zero repairs using ezdxf 1.4.4.

```sh
python tools/verify_section_manager.py /path/to/conformance-artifacts
```

Native evidence currently covers one R2018 manager with one section and update
flag zero. Other profiles, the documented spelling, flag one, empty lists and
multiple pointers are qualified by schema cases, not by additional native
drawings. This increment does not qualify live sectioning, automatic manager
membership maintenance, section generation, editable manager lifecycles, native
AutoCAD execution, or complete-drawing byte identity.
