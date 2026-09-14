# LWPOLYLINE vertex packets and tapered reversal

## Scope and correction

The typed reader previously discarded the group 90 count and created a vertex only when it reached group 20. Optional start width, end width and bulge fields between groups 10 and 20 were attached to the preceding vertex or discarded. It also admitted mismatched counts, repeated or orphan coordinates and missing vertex components.

The bounded reader now starts each vertex at group 10, requires one corresponding group 20, and attaches groups 40, 41 and 42 to that vertex on either side of its group 20. One nonnegative group 90 must equal the number of complete vertices actually encountered. Counts never determine speculative list capacity. Invalid framing is rejected within its entity, so the coordinates of a following entity cannot complete an unfinished vertex. Text comments and following XData remain supported.

Polyline2D.Reverse now exchanges the start and end widths of every reversed outgoing edge. Vertex positions, bulge direction and width endpoints therefore describe the same tapered straight or circular segment when traversal is reversed. Reversing twice restores all stored vertex values, including the inactive final outgoing-edge data of an open polyline. The tests also check clone isolation and persistence.

The existing group 43 constant-width precedence is unchanged. Group 91 vertex identifiers remain outside the typed model pending a verified first-version policy. This change does not establish complete polyline schema, geometric validity, affine width transformation, or native AutoCAD qualification.

## Version and operation comparison

| DXF profile | Typed vertex import | Typed persistence and reversal |
|---|---|---|
| R11/R12, R13, R14 | Typed document remains unsupported | Separate raw preservation path unchanged |
| AC1015 / 2000 | Counted, complete vertex packets | Existing text/binary writer; corrected taper reversal |
| AC1018 / 2004 | Same | Same |
| AC1021 / 2007 | Same | Same |
| AC1024 / 2010 | Same | Same |
| AC1027 / 2013 | Same | Same |
| AC1032 / 2018 | Same | Same |

## Regression design

`LwPolylineIntegrityTests.cs` registers 312 cases across six admitted versions and both transports:

- 96 positive cases cover every placement of the three optional width/bulge fields before or after group 20, exact coordinates and scalar values, text comments, following XData and LINE, two persistence cycles, and clone isolation.
- 168 malformed cases cover missing, negative, duplicate and mismatched group 90; missing, repeated and orphan coordinates; and optional vertex fields before any group 10. Every fixture includes a following entity to test entity boundaries.
- 12 allocation checks reject an `Int32.MaxValue` declared count with three actual vertices and require less than 2 MB allocated on the current thread after loader warm-up.
- 12 empty-count cases check the existing acceptance of an exact zero-vertex entity and preservation of its following LINE. These are reader boundary tests and are not exported as valid drawing examples.
- 24 reversal cases check open and closed polylines with asymmetric tapers and positive, negative and zero bulges. Nine samples per edge compare the original and reversed locus and interpolated width. Direct clones, double reversal and reloaded output are checked.

`verify_lwpolyline_integrity.py` independently reads 120 exported drawings using ezdxf: 96 packet fixtures and 24 original/reversed pairs, totaling 144 polylines. It checks coordinates, widths, bulges, entity metadata, XData and following LINE. Reversal comparisons use ezdxf's bulge-to-arc conversion and 17 samples per edge. Every drawing must have zero ezdxf audit errors and repairs.

## Executed evidence

The unchanged 44c9f70 production baseline passed all 18,739 existing cases in both configurations before this change. An isolated scoped harness then ran the exact 312 new cases plus the 13 existing document/transport smoke cases against the unchanged library: Both Debug and Release reported 37 passes and 288 failures. With the corrected reader and reversal code, both configurations reported 325 passes and zero failures. The controlled red comparison leaves the production baseline unchanged and changes only test selection in the scratch harness; committed test registration adds these cases to the complete suite.

The 288 baseline failures consist of 84 reordered optional-field cases, 168 malformed packet/count cases, 12 count-rejection/allocation cases and 24 tapered-reversal cases. Existing canonical input and exact empty-count behavior passed. This distinguishes the newly demonstrated defects from previously working behavior.

The independent ezdxf 1.4.4 verifier passed all 120 fixture drawings in each configuration: 240 independently loaded drawings and 2,856 reversed-locus and taper comparisons in total, with zero audit errors or repairs.

## Validation commands

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_lwpolyline_integrity.py artifacts/conformance
```

In the local managed execution environment, compiler servers and parallel MSBuild nodes must be disabled: build with `--disable-build-servers -m:1 -p:UseSharedCompilation=false`, then run the produced conformance DLL. This is an environment workaround and does not change the library or test target.

## Primary reference

[Autodesk LWPOLYLINE group-code specification](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-DXF/files/GUID-748FC305-F3F2-4F74-825A-61F04D757A50.htm) identifies the vertex count, repeated OCS coordinate components, per-vertex width and bulge fields, and entity scalar data. Native AutoCAD open/AUDIT/save/reopen has not been executed.
