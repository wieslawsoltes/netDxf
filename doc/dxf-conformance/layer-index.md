# Stored LAYER_INDEX graphs

The bounded public model stores the finite group-40 Julian-date timestamp and
ordered layer-name entries, each owning one distinct IDBUFFER through group 360.
Layer names are strings independent of the document's layer table. Duplicate,
case-distinct and unresolved names remain distinct entries.

Group 90 is checked against the complete group-330 sequence in its resolved
IDBUFFER, including duplicate and null references. A mismatched input count is
rejected. After explicit edits to the buffer's reference list, output derives the
count from that list. No stale stored count is silently repaired during import.

`SetEntries` snapshots and validates entries before changing ownership. Detached
indexes can adopt unowned detached buffers; registered indexes can rename and
reorder their existing entries while preserving the entire owned buffer set.
The ordinary IDBUFFER reference API remains editable. Generic object-graph
cloning remaps the owned buffers and requires explicit mappings for cross-document
external references. Terminal erasure follows reciprocal ownership and rejects
incoming references from outside the erased graph.

The reader accepts the public fields as ordered parallel sequences, allowing
both grouped arrays and interleaved name/handle/count tuples. Unknown private
fields or subclass extensions preserve the entire object as opaque. LibreDWG's
undocumented extra leading group 90 is also opaque. Opaque graphs cannot be
cloned without a schema. The writer emits interleaved public tuples.

The model stores an index graph; it does not calculate index contents, evaluate
layer membership, update timestamps, or track later layer renames. Native index
execution and historical native interoperability are not qualified.

## Schema evidence

- [Autodesk LAYER_INDEX schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-17560B05-31B9-44A5-BA92-E92C799398C0.htm) describes timestamp 40, repeated layer names 8, hard-owned IDBUFFER handles 360, and IDBUFFER entry counts 90.
- The [Autodesk 2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf), printed pages 194–195, agrees. Its class table gives AcDbLayerIndex with nongraphical flags zero.
- [IxMilia source at 3ab0f9d6](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf.Generator/Specs/ObjectsSpec.xml) declares LAYER_INDEX from R14 and writes three grouped arrays in ordinal correspondence. This supports admitting the six current export profiles without an invented later-version restriction; it is not native-version qualification.
- [LibreDWG source at 34f02f54](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg.spec) writes name/handle/count tuples plus an undocumented leading 90=0. That extra field is not assigned invented semantics.

Fixture generation, focused tests, and independent verification are in progress
for this implementation checkpoint; this document does not claim their completion.
