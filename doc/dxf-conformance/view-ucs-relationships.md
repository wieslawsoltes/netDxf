# Associated UCS data in VIEW and VPORT

Named VIEW records previously wrote group 72 as zero and discarded their associated UCS coordinates and references. VPORT retained its stored UCS coordinates but omitted the named/base UCS references. UCS table flags were also replaced with zero. This module adds editable stored relationships and retains the original registered UCS identities through text and binary persistence.

## Fields and admitted versions

| Model | Groups | Contract |
| --- | --- | --- |
| `View.Ucs` | 72 | Null means no associated UCS; otherwise writes 1 and the bundle below. |
| `ViewUcs.Origin`, `XAxis`, `YAxis` | 110/120/130, 111/121/131, 112/122/132 | Finite stored coordinates; axes must be nonzero and retain their magnitude. |
| `ViewUcs.OrthographicType`, `Elevation` | 79, 146 | Type 0–6 and finite elevation. |
| `ViewUcs.NamedUcs`, `BaseUcs` | 345, 346 | References to registered UCS table records. Null means unnamed or the default WORLD base. |
| `VPort.NamedUcs`, `BaseUcs` | 345, 346 | References complement the existing stored VPORT UCS coordinates and orthographic type. |
| `UCS.Flags` | 70 | Preserves table flags, including externally dependent, resolved-xref and referenced bits. |

All six admitted typed profiles—R2000, R2004, R2007, R2010, R2013 and R2018—support this bundle in both transports. The R2000 association gate is corroborated by the explicit version metadata in ezdxf 1.4.4's VIEW/VPORT implementation. Typed R12/R13/R14 admission is unchanged. These APIs store data; they do not restore application views, activate coordinate systems, transform geometry, render a scene, or evaluate orthographic frames.

`BaseUcs` on VIEW or VPORT requires a nonzero orthographic type when saving. A missing base uses WORLD; the writer does not invent a table record or a handle for WORLD. Input zero handles normalize to absent references. A wholly absent coordinate vector uses the typed model's default origin or unit axis; a partially present vector rejects. The reader rejects duplicate associated fields, invalid Boolean/type values, zero axes, nonfinite values, missing targets and references to objects that are not UCS records. UCS references resolve after all tables and entities have been registered, so forward references do not depend on table order. UCS group 79 remains reserved zero as specified by Autodesk.

## Authoring and ownership

```csharp
var document = new DxfDocument(DxfVersion.AutoCad2018);
UCS frame = document.UCSs.Add(new UCS("SurveyFrame")
{
    Origin = new Vector3(100, 200, 0),
    Flags = UcsFlags.Referenced
});
var view = document.Views.Add(new View("Survey")
{
    Ucs = new ViewUcs
    {
        Origin = frame.Origin,
        XAxis = frame.XAxis,
        YAxis = frame.YAxis,
        NamedUcs = frame
    }
});
document.Viewport.NamedUcs = frame;
```

A `ViewUcs` bundle belongs to one view. Assign a clone to share its values with another view. Register referenced UCS entries first, then add the referring VIEW/VPORT. A registered view or viewport rejects a foreign or detached UCS reference before changing the current reference. Adding a detached referring record under a new name also validates all targets before assigning its handle. UCS and VIEW additions under new names reject XData that carries an application registry owned by another document; cloning supplies an independent registry and preserves the original owner's handle. As with other existing name-keyed tables, `Views.Add` and `UCSs.Add` return the already registered record when the name exists, without adopting or validating the supplied record. Use the returned object when relying on that canonicalization behavior.

UCS reference counts include both named and base slots and every physical VPORT tile. Duplicate VPORT configuration names do not collapse their references. Removing a referring record or detaching its bundle releases its uses; a referenced UCS cannot be removed. Rename indexes commit after all observers return successfully, and recheck the current owner and name availability after callbacks. Rejected renames preserve the original name/index relationship. Explicit additions, removals or moves performed by a callback retain their ordinary effects even if the callback later throws.

## Cloning and explicit mapping

`View.Clone` copies the UCS value bundle independently. VIEW and VPORT clones retain their referenced UCS objects while remaining detached, allowing copies within the same document. Registering such a copy under a new name in another document rejects until the caller maps the references to UCS objects registered there:

```csharp
View copy = (View)view.Clone("SurveyCopy");
var destination = new DxfDocument(DxfVersion.AutoCad2018);
UCS destinationFrame = destination.UCSs.Add((UCS)frame.Clone());
copy.Ucs.NamedUcs = destinationFrame;
destination.Views.Add(copy);
```

Map `BaseUcs` too when present. This explicit policy avoids transferring source records or silently replacing a referenced frame with a same-name destination frame. Generic extension dictionaries, persistent reactors and attached application-object subtrees continue to use the separate named-object database clone API; this module does not add an automatic application database import to table cloning.

## Evidence and remaining boundaries

`ViewUcsTests.cs` covers six profiles and both transports, independently generated input, reordered scalar/vector components, three alternating persistence cycles, exact handles and values, original UCS flags, following LINE/XData, authoring, absent/WORLD references, malformed fields and unresolved/wrong-type targets. API tests check slot counts, repeated-name tiles, clone isolation, foreign ownership, before-output validation, failed observers, and attachment, detachment, moves and callback-created collisions during rename.

Six committed inputs under `tests/fixtures/view-ucs` were generated with ezdxf 1.4.4. Their manifest pins SHA-256 values and source provenance; normal conformance runs verify those hashes before loading them. Named-UCS and orthographic-base cases are separate, matching the documented ObjectARX overloads. `tools/verify_view_ucs.py` requires exactly 12 output profiles, checks the actual header/transport, exact values, resolved handle targets and absent counterpart references, and requires zero ezdxf audit errors or repairs. The existing independent-verifier runner discovers this script automatically. No licensed native AutoCAD open/AUDIT/save/reopen was run.

On the module's `3197705` baseline, the final Debug and Release conformance runs each passed all 20,303 cases, including 355 added here. The .NET Standard 2.0 build passed. All 52 independent verifier scripts passed against the Release artifacts, including the 12 VIEW/UCS profile/transport files; the VIEW/UCS verifier also passed against the Debug files. The coverage validator and its 18 Python tests passed. A separate lifecycle review reproduced the pre-fix failed-observer index corruption and verified the repaired name/index relationship and reference removal. A second review passed ten external lifecycle probes, including callback-driven moves and reference changes, same-name foreign records, clone/bundle isolation, and independent-input binary persistence followed by removal of all referencing records and UCS entries.

Typed UCS-table group 346 retention and authoring remain deferred: Autodesk describes its orthographic base relationship while describing UCS group 79 as always zero. The public `AcDbUCSTableRecord` API exposes per-orthographic origin points but no parent-UCS assignment. Existing orthographic origin overrides and elevation support remain intact; an unverified parent relationship is not inferred. VPORT frozen-layer variants, background/live-section/visual-style/sun graphs, modern rendering values and private metadata remain outside this module. In particular, Autodesk's published VPORT “331 or 441” frozen-layer wording conflicts with group 441's integer type; no corrected handle code is guessed.

References: [Autodesk VIEW schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-CF3094AB-ECA9-43C1-8075-7791AC84F97C.htm), [VPORT schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-8CE7CC87-27BD-4490-89DA-C21F516415A9.htm), [UCS schema](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-1906E8A7-3393-4BF9-BD27-F9AE4352FB8B.htm), [ObjectARX VIEW/VPORT UCS assignment overloads](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__OVERLOADED_setUcs_AcDbAbstractViewTableRecord.html), [UCS table methods](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__MEMBERTYPE_Methods_AcDbUCSTableRecord.html), and the pinned [ezdxf VIEW implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/view.py) and [VPORT implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/vport.py).
