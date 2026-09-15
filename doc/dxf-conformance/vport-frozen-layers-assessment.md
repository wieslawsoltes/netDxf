# VPORT frozen-layer evidence assessment

The VPORT symbol-table frozen-layer gap remains open. No typed field, reference API, clone policy, profile admission or writer behavior is added by this assessment. The public documentation has an unresolved code discrepancy, and the inspected producer implementations and source corpus provide no populated VPORT packet that resolves it. Absence in this bounded corpus does not prove that such packets cannot exist.

## Schema and implementation evidence

Autodesk's [VPORT DXF page](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-8CE7CC87-27BD-4490-89DA-C21F516415A9.htm) lists repeated `331 or 441` references to frozen layers. Its description does not establish a version boundary or distinguish an alternate encoding. The [2005 DXF reference](https://images.autodesk.com/adsk/files/acad_dxf.pdf), printed pages 52–55, does not list a VPORT frozen-layer field. That reference assigns groups 440–449 an integer representation, so the later page's description of 441 as a handle conflicts with the global value type. The later page also differs from the older reference and established packets on view-height/aspect-ratio fields. Substituting 341 for 441 would be an inference, not a verified correction.

The [ViewportTableRecord class](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_ViewportTableRecord.html) represents model-space tiled viewport arrangements. Its [managed methods](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Methods_Autodesk_AutoCAD_DatabaseServices_ViewportTableRecord.html), [properties](https://help.autodesk.com/cloudhelp/2024/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-__MEMBERTYPE_Properties_Autodesk_AutoCAD_DatabaseServices_ViewportTableRecord.html), and [native methods](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__MEMBERTYPE_Methods_AcDbViewportTableRecord.html) expose no frozen-layer member. The inspected AbstractViewTableRecord base methods/properties likewise expose none. This is an API observation, not proof of an absent serialized field.

The separate [VIEWPORT entity schema](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-2602B0FB-02E4-4B9A-B03C-B1D904753D34.htm) lists optional repeated 331 frozen-layer handles. The entity's [AcDbViewport methods](https://help.autodesk.com/cloudhelp/2018/ENU/OARX-RefGuide/files/OREF-__MEMBERTYPE_Methods_AcDbViewport.html) include freezeLayersInViewport and getFrozenLayerList. netDxf already exposes this entity relationship through `Viewport.FrozenLayers`; it is distinct from `Tables.VPort`.

Independent source inspection found:

- [LibreDWG dwg.spec at 34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg.spec): `DWG_TABLE(VPORT)` has no frozen-layer vector. The 331/341 frozen-layer vectors occur in `DWG_ENTITY(VIEWPORT)`.
- [IxMilia TableSpec at 3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf.Generator/Specs/TableSpec.xml): DxfViewPort has no frozen-layer property.
- [ezdxf VPort at v1.4.4](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/vport.py): no frozen-layer field or export.

The local ACadSharp VPort source also lacks an implemented frozen-layer property and repeats the questionable 331/441 documentation as comments. Its DWG frozen-layer reader belongs to the VIEWPORT entity. This additional observation is not used as a pinned producer qualification claim.

At netDxf baseline `763da9a`, `Tables/VPort.cs` has no such relationship, and `IO/DxfVPort.cs` treats 331/341/441 as unknown fields under its existing typed fallback. Unknown-tag preservation belongs to the raw API. No flag interpretation or viewport evaluation follows from this assessment.

## Bounded corpus result

The scan reads raw text/binary tags, including decompressed pinned sources, rather than relying on an independent reader's typed VPORT model. It records candidate codes 331, 341 and 441 with the physical record, section, subclass, application-group depth and XData position. Candidate presence would require further interpretation; it is not automatically classified as a frozen-layer relationship.

| Corpus | Files attempted / parsed | VPORT records | VIEWPORT entities | Candidate tags in either type |
|---|---:|---:|---:|---:|
| Repository fixtures at 763da9a | 179 / 179 | 185 | 18 | 0 |
| All DXF paths in pinned LibreDWG tree | 67 / 66 | 61 | 32 | 0 in parsed files |

The corpora may overlap and are not claimed as unique producer drawings. The one failed LibreDWG file is `test/test-data/r1.4/entities.dxf`; it begins with `EXTENTS,1`, which the modern pair-tag parser rejects. The failure remains explicit. All 67 downloaded files were checked against their Git blob identities before inventory; hashes and per-file counts, including the failed file's identity, are retained in [the corpus receipt](evidence/vport-frozen-layer-corpus.json).

Reproduce the structural scan with ezdxf 1.4.4:

```sh
python tools/assess_vport_frozen_layers.py tests/fixtures --output repository-scan.json
python tools/assess_vport_frozen_layers.py /path/to/pinned/libredwg/test/test-data --output libredwg-scan.json
```

The second command exits unsuccessfully if the R1.4 source is included and retains the partial inventory plus its parse failure. Six synthetic scanner controls check positive detection, duplicate order, subclass/application scope and separation of VPORT from VIEWPORT, using LF, CRLF and binary input. These controls validate the inventory tool only. They provide no interoperability evidence for a new relationship. Text labels in this structural inventory use a byte-preserving Latin-1 decode; it is not a Unicode label-fidelity test.

## Evidence required before implementation

A future implementation needs a pinned, independently produced populated VPORT record with its referenced LAYER records and source profile, plus a schema explanation consistent with the actual wire type. Additional examples should establish admitted profiles, repeated-reference ordering, duplicate/null behavior and whether soft/hard variants occur. The eventual API should retain exact source object identities, track incoming references for layer removal, and define explicit clone/adoption behavior. Those choices remain undecided until evidence establishes the relationship. A synthetic packet authored from the disputed documentation cannot fill this gap.
