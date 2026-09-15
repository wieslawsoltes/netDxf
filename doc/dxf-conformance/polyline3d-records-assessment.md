# Internal POLYLINE records: evidence and first stored slice

This assessment identifies and reproduces a VERTEX identity loss. It records the original unqualified implementation checkpoint. The completed [ordinary 3D child-record module](polyline3d-records.md) and [focused qualification receipt](receipts/polyline3d-records/qualification.json) now supersede that checkpoint's implementation status; DIMASSOC evaluation remains separate.

## Primary schema

Autodesk documents VERTEX as its own entity record, with common entity data and the AcDbVertex / AcDb3dPolylineVertex subclass markers. Group 70 value 32 identifies an ordinary 3D polyline vertex. The point is in world coordinates. Optional width, bulge, tangent and vertex-identifier fields are distinct stored groups. SEQEND terminates the physical sequence. These rules do not imply that the library should evaluate fitted polylines or reconstruct missing application relationships.

- [VERTEX, Autodesk DXF 2018](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-0741E831-599E-4CBF-91E1-8ADBCFD6556D.htm)
- [Common entity data, Autodesk DXF 2018](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-3610039E-27D1-4E23-B6D3-7E60B22BB5BD.htm)
- [SEQEND, Autodesk DXF 2018](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-FD4FAA74-1F6D-45F6-B132-BF0C4BE6CC3B.htm)
- [POLYLINE, Autodesk DXF 2018](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-ABF6B778-BE20-4B49-9B58-A94E64CEFFF3.htm)
- [Extension dictionaries and persistent reactors, Autodesk DXF 2018](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A55D4A3D-67CF-417E-B63F-3124CD8027FD.htm)

## Native evidence

The inventory scans 67 Git-blob-verified LibreDWG DXF files pinned to commit `34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43`. The modern pair-tag loader reads 66; the R1.4 file beginning `EXTENTS,1` remains an explicit parser boundary. Across the parsed files there are 8,379 VERTEX records and 91 SEQEND records. Most vertices are in legacy versions outside the modern typed-reader scope. The six modern profiles contain 112 VERTEX records. Every inventoried modern VERTEX and SEQEND has its own group 5 and reciprocal ordinary group 330 owner.

The six `example_2000/2004/2007/2010/2013/2018.dxf` originals each contain the same positive relationship:

| Record | Physical relationship |
| --- | --- |
| POLYLINE `41A` | Four ordinary 3D vertices; group 70 is 9, including the closed bit; reactor to DIMASSOC `42F` |
| VERTEX `41E` | Common owner `330:41A`; reactor `330:42F` inside ACAD_REACTORS; flags 32 |
| DIMASSOC `42F` | Repeated geometry path `331:41A`, then `331:41E` |

R13/R14 examples contain the same vertex reactor, yielding eight positive reactor packets overall. The modern R2000 `PolyLine2D.dxf` additionally supplies four VERTEX and two SEQEND packets with explicit ByBlock linetype and ACI color 0. That is positive common-field evidence but does not qualify legacy 2D storage in the first implementation slice. No VERTEX XData or extension-dictionary packet was found in this corpus; such tests must be identified as independent authored or malformed fixtures, not native qualification.

## Reproduced behavior

Before this increment, ReadVertex constructs an internal DxfObject with a handle. ReadPolyline3D then copies only Vector3 values and discards the object. SEQEND fields are skipped. PreProcessPolyline3D subsequently creates fresh VERTEX and SEQEND objects and allocates fresh handles on each save. Common child XData is not read, and child reactors cannot resolve because the physical source objects never reach document registration.

An unchanged 12-case probe uses the already pinned DIMASSOC extraction (six source profiles, ASCII and binary output). All cases retain four geometry points but lose mapped vertex `F1009` (source `41E`) and its reactor. The first implementation build retains that identity and backlink in all 12 cases. This probe is an early defect control, not complete qualification.

## Approved compatible boundary

The initial slice retains actual owned Polyline3DRecord DxfObjects for loaded ordinary unsmoothed 3D polylines and their SEQEND. VertexRecords and EndSequenceRecord expose identity and common metadata. The existing mutable List&lt;Vector3&gt; remains the geometry API. Each record belongs to an index slot; equal-count coordinate replacement keeps the slot identity. Polyline3D.Reverse reverses both the point list and the record order. Unsupported count or smoothing changes fail before output or handle allocation. No coordinate-value matching is attempted, because duplicate coordinates make identity inference ambiguous.

The physical record token must belong to the exact accepted record. Common group 5 and group 330 must be nonzero and unambiguous; payload or private-control handles cannot replace them. Child registration, reciprocal ownership, APPID bookkeeping, named resource identities, incoming references, extension dictionaries and erasure carriers need explicit lifecycle integration. Optional fields retain their presence and stored values. Private packets are retained without projecting their fields as public semantics.

Complete geometry-only child clones should retain optional fields and safe XData with fresh child identities. Reactors, owned extensions, private packets or external dependencies require an explicit graph mapping and conservatively reject in this initial API. Foreign adoption of an existing source record set rejects. The same restrictions must be checked through containing blocks and generic owned-metadata clone paths before callbacks or destination mutation.

The repeated-path DIMASSOC remains opaque. This increment restores a referenced physical VERTEX identity; it does not qualify the DIMASSOC path schema or implement association evaluation. Legacy 2D POLYLINE conversion to LWPOLYLINE, smoothed/fitted vertices, polyface and polygon meshes remain separate gaps. The malformed missing-SEQEND framing failure receives its own bounded regression.
