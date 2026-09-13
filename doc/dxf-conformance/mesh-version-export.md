# Version-safe MESH export

Baseline: `828115ca6547daf80241ba1a64c497636c1ce1e4`, after merged PR #35. Audit date: 13 September 2026.

## Corrected defect

The typed writer emitted `MESH` (`AcDbSubDMesh`) in AC1015, AC1018 and AC1021 documents even though that entity belongs to the 2010+ mesh model. Modern subdivision MESH is not the legacy polygon/polyface mesh encoded using POLYLINE/VERTEX/SEQEND. The old writer did not convert the geometry; it merely produced an inconsistent target-version file.

Preflight every registered block before layout creation, handle allocation, application registrations or destination writes. Reject modern MESH below AC1024 with a `NotSupportedException` identifying the feature and minimum version. Model space, inactive paper space, nested blocks and unreferenced definitions are covered because all can enter the serialized database. The normal public convention is retained: Debug throws, Release Save returns false. Caller-owned streams remain open.

No silent tessellation, topology loss, entity deletion or version upgrade occurs. Explicitly select AutoCAD2010 or later, remove the mesh, or perform an application-controlled conversion before saving. Legacy PolygonMesh and PolyfaceMesh remain permitted in every currently admitted typed document profile.

## Version comparison

| Pipeline / representation | 2000 AC1015 | 2004 AC1018 | 2007 AC1021 | 2010 AC1024 | 2013 AC1027 | 2018 AC1032 |
|---|---|---|---|---|---|---|
| Typed MESH output | Rejected before writes | Rejected before writes | Rejected before writes | Tested subset | Tested subset | Tested subset |
| PolygonMesh / PolyfaceMesh as POLYLINE | Tested control | Tested control | Tested control | Tested control | Tested control | Tested control |
| Mesh-free typed document | Tested | Tested | Tested | Tested | Tested | Tested |
| Explicit upgrade to 2010 | Tested | Tested | Tested | Not needed | Not needed | Not needed |
| Raw retention of encountered MESH tags | Unchanged, not legality validation | Same | Same | Same | Same | Same |

A loader retaining an encountered out-of-profile record is not a certificate that the record is legal in that version. Raw preservation and typed read-side retention remain unchanged; callers can deliberately promote a retained file before typed export. The default new document is AutoCAD2000, so applications creating MESH entities must now choose an appropriate output version explicitly.

## Executed evidence

126 additional registered cases over the 5,598-case baseline. With unchanged production, **5,676 passed / 48 failed**, Debug and Release. With preflight, **5,724 passed / 0 failed**, Debug and Release, using the local signed-library .NET 8 workbench.

The matrix crosses all six typed version profiles, text/binary, four block placements and subdivision levels 0/2. Supported outputs verify vertices, exact small-coordinate bits, faces, edges, crease values, subdivision level and XData over two transports. Rejections check a nonempty destination at an interior position, unchanged bytes/position, handle seed, object identities, registrations, layouts and active layout. Twelve legacy mesh controls and twelve empty-document controls prevent overbroad rejection. Six explicit-upgrade cases check raw retention and subsequent typed promotion.

Independent ezdxf 1.4.4 verification loads all **48 supported MESH drawings**, compares topology, coordinate bits, subdivision/crease data and XData, and reports **zero errors and zero repairs** from its audit. This is agreement with one independent implementation on these fixtures, not an AutoCAD execution or full MESH schema certificate.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_mesh_profiles.py artifacts/conformance
```

Final-head Linux/Windows Debug/Release CI, netstandard2.0 builds and source audit remain merge gates. A rejected file-path Save still follows its existing File.Create truncation contract; this preflight protects writer destination bytes, not transactional replacement of an existing file. Other validation failures and final-copy IO failures are not made transactional by this change.

## Remaining MESH work

Blend-crease flag preservation, per-subentity color/material/transparency/mapper overrides, topology validation, true subdivision evaluation, schema-driven downgrade and historical fixtures remain separate. No general target-version capability system is introduced by this isolated correction.

## Primary references

- Autodesk distinction between pre-2010 polygon/polyface meshes and the newer mesh model: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-Core/files/GUID-C4325DCB-3648-4463-8135-629EA7F72AB0.htm
- Autodesk MESH group codes, including blend crease and subentity overrides: https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm
- Autodesk database-family identifiers: https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm
