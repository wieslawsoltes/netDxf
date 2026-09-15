# Stored SECTIONSETTINGS

`DxfSectionSettings` retains public stored generation settings and object
references without generating section geometry. New objects use the documented
`SECTIONSETTINGS` name; imported native `SECTION_SETTINGS` objects retain that
spelling. Typed input and output support R2007 and later. Earlier profiles retain
input objects opaquely; authoring typed settings in those profiles fails before
output is written.

The settings object stores its group-90 integer and an ordered `TypeSettings`
list. Each `DxfSectionTypeSettings` bundle contains a stored type integer,
integer `GenerationOptions`, source references, a destination `BlockRecord`, an
inert destination file string, geometry settings, and marker layout. Source
references retain order, duplicates, and explicit null handles. Real references
must resolve to identities physically supplied by the file; the destination
must resolve to a BLOCK_RECORD. Registering or replacing live references requires
targets in the same document. Layer, linetype, plot-style and hatch-pattern names
are stored strings; no resources are created or renamed on their behalf.

`SetTypeSettings` snapshots and validates before replacing state. Reference-bearing
type bundles are immutable. Their geometry appearance values remain editable
through validated scalar setters and are copied independently when a bundle is
adopted or cloned. Cross-document graph cloning requires explicit mappings for
external source objects and destination blocks, while references inside the
cloned graph remap automatically. Generic erasure preflight observes incoming
settings references. The separate SECTION entity integration owns the reciprocal
entity/settings attachment and its lifecycle.

Only structurally established counts are derived: outer group 91 counts type
bundles, type group 92 counts source slots, and type group 93 counts geometry
bundles. Every geometry group's 90 and 91 values remain explicit stored integers.
The documentation labels those as section type and geometry count; the native
packet uses 91 values 1, 2, 4 and 8, and LibreDWG gives the fields different names.
No list count, flag meaning or evaluated geometry is inferred from them. Likewise,
groups 42 and 43 preserve their raw values under the published names hatch scale
and hatch spacing even though LibreDWG's source labels are reversed.

The reader preserves both physical marker layouts: one marker before the whole
geometry sequence, including an empty sequence, and one marker before each
geometry record. Mixed layouts, duplicate public scalar groups, missing public
markers, incomplete bundles and mismatched counts reject. Literal stored hatch
names equal to marker text remain valid in their scalar position. Documented
indexed color 63 and native indexed color 62 retain their spelling; simultaneous
color fields, other unqualified color forms, private common headers, unknown
subclasses and private payload extensions preserve the complete object opaquely.
Opaque settings cannot be cloned through an invented schema.

Public enumeration and input count admission is bounded to 1,024 type bundles,
1,048,576 aggregate source-reference slots and 65,536 aggregate geometry bundles
per settings object. Counts are checked against remaining input before loops,
and infinite public enumerables reject at their admission boundary. Geometry
numeric values must be finite; indexed colors admit 0 through 256, and stored
face/edge transparency admits zero through 100. Other stored integers and finite
scalars retain their values without evaluation or normalization.

The primary schema is Autodesk's [2009 DXF reference](https://damassets.autodesk.net/content/dam/autodesk/www/developer-network/platform-technologies/autocad-dxf-archive/acad_dxf_2009.pdf),
printed pages 223–225. Independent implementation evidence is pinned to
[IxMilia's settings source](https://github.com/ixmilia/dxf/blob/3ab0f9d6d3f14a6f6fa924e111e8e3af1065c567/src/IxMilia.Dxf/Objects/DxfSectionSettings.cs)
and [LibreDWG's stored schema](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec).
IxMilia's Boolean generation-option API is explicitly insufficient for native
option 17. Native spelling, repeated markers, color 62 and reciprocal ownership
come from the unchanged source drawing, not from CLASS declarations alone.

The focused Debug and Release qualifications each pass 497 cases: 161 settings, 109
SECTION entity, 140 LAYER_INDEX and 87 object-erasure cases. The mandatory
`tools/verify_section_settings.py` checks all 56 settings outputs, eight pinned
producer originals and exact mapped extraction packets, complete native packet
equality, source/destination identities, mutable-value and binary independence,
clone and erased graphs, exact opaque variants, zero independent audit errors or
repairs, and four corrupted-output controls in each configuration. All five Release
library targets and netstandard2.0 Debug compile with the baseline documentation
warnings only. Final integrated entity lifecycle and the strengthened shared
source-identity helper still require qualification on the combined SECTION branch.
