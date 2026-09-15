# Named VIEW live-section references

`Tables.View.LiveSection` stores the public VIEW group-334 soft reference to an
existing `Entities.Section`. R2007, R2010, R2013 and R2018 are admitted in text
and binary. A named view refers to the section without owning it, activating it
or generating section geometry.

```csharp
var section = new netDxf.Entities.Section();
document.Entities.Add(section);
var view = new netDxf.Tables.View("Section view") { LiveSection = section };
document.Views.Add(view);

view.LiveSection = null;             // Emit an explicit null group 334.
view.ClearLiveSectionReference();    // Omit group 334.
```

`HasStoredLiveSection` distinguishes absence from an explicit null. Input keeps
that distinction. Nonzero references bind after entity and SECTION ownership
resolution to the exact registered object from the physical input record.
Padded lowercase hexadecimal references resolve numerically; private handles,
ambiguous identities, unknown handles and non-SECTION targets cannot substitute
for an accepted source section. Repeated public group 334 is rejected, including
a duplicate null. Group 334 in nested application data or another subclass does
not become the public relationship.

A registered view accepts only a section already registered in the same document.
An erased or detached section cannot become its target. Each current named view
participates in the incoming-reference checks for ordinary entity removal,
containing-block removal and `Objects.EraseSection`. Clearing or removing one
view leaves another view's target protected. These checks use object identity;
names do not select replacement sections.

`View.Clone(newName)` retains the original target object and field presence.
Adding that detached clone to the same document therefore shares the section.
For a different document, set `copy.LiveSection` to the actual registered
destination section before calling `destination.Views.Add(copy)`. An unresolved
foreign adoption rejects before assigning handles or inserting the view. Cloning
or removing a view never clones or removes its referenced section.

An older output profile rejects a present group 334, including explicit null,
before writing to the caller's stream or assigning handles. Clearing the field
restores ordinary profile behavior. This storage support does not implement
automatic section regeneration or native application acceptance.

## Evidence and verification

[Autodesk's VIEW schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm)
identifies group 334 as an optional live-section soft reference.
[The ezdxf 1.4.4 VIEW codec](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/view.py)
exports the field from R2007. Its API spells the property `live_selection_handle`;
the fixture generator assigns that API property and uses the actual producer's
save and reload paths in all four profiles and both transports.

The eight pinned files in `tests/fixtures/view-live-section` include live, null
and absent VIEW references. Their SECTIONOBJECT carrier is an explicitly
authored packet stored through ezdxf's opaque `DXFTagStorage`: these fixtures
qualify the independent VIEW writer and its references, not an independent
SECTION geometry codec. The manifest identifies the source hashes, section and
view handles, transport, profile and zero-error/zero-repair producer audits.
No AutoCAD-produced populated VIEW-334 packet or native AutoCAD execution is
claimed. Existing native SECTION tests remain separate.

`RegisterViewLiveSectionTests` covers actual producer input, stored null/absence,
multiple consumers, cloning, explicit foreign replacement, containing-block and
owned erasure, malformed references, private contexts, physical source identity
and refusal before stream/handle mutation. The mandatory gate is:

```sh
python tools/verify_view_live_section.py artifacts/conformance
```

The gate requires all 24 producer, authored and foreign-map outputs. It verifies
public field presence, physical section identity and selected section payload
values, checks all eight producer hashes, audits the outputs and rejects 120
mutations of actual parsed output with the same validators.

The [qualification receipt](view-live-section-qualification.json) records
124 dedicated passing cases and 636 existing VIEW/UCS/SECTION regression cases
in each of net8.0 Debug and Release. Both configurations pass the new independent
gate and the existing VIEW-UCS and SECTION gates. A separate probe uses only
preexisting APIs against the same eight producer inputs: the prior library loses
both live and null group-334 fields and permits removing or erasing the referenced
section; the updated library retains both fields and rejects both removal paths.
The receipt pins the executable, fixture, source and result hashes. Full-suite
and additional target-framework qualification belongs to the integration receipt.

The separate [VPORT frozen-layer assessment](vport-frozen-layers-assessment.md)
remains unresolved: the later public table lists `331 or 441`, while 441 has an
integer wire type, the older schema and inspected producer APIs omit the field,
and the pinned corpus supplies no positive VPORT packet. This VIEW-334 support
does not infer a frozen-layer API from that disputed entry.
