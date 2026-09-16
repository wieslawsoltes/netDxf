# MESH public declaration uniqueness and counted-list framing

The typed MESH reader now rejects repeated public singleton declarations and
counted-list packets in AcDbSubDMesh. Version 71, blend flag 72, subdivision 91,
vertex count 92, face-list size 93, edge count 94, crease count 95 and the outer
override count 90 can each be declared once. Both equal and conflicting repeated
values reject. Empty counted lists are declarations too. Vertex-coordinate
10/20/30 and crease-value 140 tags must be consumed inside their corresponding
counted list; an orphan item in the outer public parser rejects.

This fixes inputs that silently changed subdivision 3 to 7 through a second
group91, or replaced earlier vertices/faces/edges with a second complete packet.
The diagnostic identifies MESH, the responsible group and input position.
Rejection happens before the reader returns an admitted entity and does not
reserve memory from the duplicate count. Caller streams remain open.

The first occurrence of a scalar or complete packet retains existing ordering
behavior. A unique blend flag after `90=0` is still accepted; the original
`MeshBlendCreaseTests.cs` fixture is unchanged. Unique version, subdivision,
vertex/face lists, edge/crease lists and an early zero override declaration are
also qualified. Existing dependencies within counted packets remain: coordinates
follow the declared vertex count, index items follow their list count, and a
crease list matches an existing edge list. Items consumed inside those readers
do not become repeated outer declarations.

The [Autodesk DXF Reference, Object and Entity Codes, printed page 2](https://images.autodesk.com/adsk/files/autocad_2012_pdf_dxf-reference_enu.pdf)
warns against assuming the table's group-code order and identifies the next
group0 as the entity boundary. The
[MESH reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
lists core counts separately from the override declaration and property fields.
A zero override count is not an end-of-subclass marker. The outer reader treats
an outer group90 as the override declaration because face/edge group90 items
are consumed inside the counted readers. Negative declarations remain malformed;
nonzero declarations remain explicitly unsupported. No property-value grammar
or marker interpretation is added.

Uniqueness is scoped to the actual public MESH data. Nested group102 payload,
an unrelated later subclass and data beyond the existing XData boundary do not
declare public fields. Re-entering AcDbSubDMesh does not reset declarations
already seen in that entity. Existing tolerance of a non-XData trailer after
XData starts remains a scoping behavior; it is not a claim of valid tail grammar
or retention of those values. The writer's existing treatment of private and
unknown subclass payloads is unchanged. Following XData and entities retain
their identities and geometry.

`MeshFieldFramingTests.cs` adds 534 cases across R2010/R2013/R2018 and both
transports: 180 scalar/count/override duplicate cases, 48 empty-list duplicate
cases, 72 orphan-item cases, 12 public re-entry cases, 72 scoped positive controls
and 48 unique reordered controls, plus 54 early negative/nonzero override
declarations and 48 duplicate maximum-sized list declarations. Early override
declarations reject before version, vertex or crease fields. Maximum-sized
duplicates reject before reading a payload or allocating from the count.
Duplicate packets occur before and after zero;
the singleton/count cases include unchanged and changed values. The existing
native zero-override cases compare the complete original subclass packets.

`tools/verify_mesh_field_framing.py <artifacts>` independently compares all 120
new positive outputs, complete ordered public mesh packets, actual mesh and
following-LINE identities, profiles/transports, and external audit results.
Twelve controls corrupt actual output packets and require the wire oracle to
fail. The existing override gate still checks 64 outputs, including four exact
native bodies, and six corruption controls. No new positive native override
packet or CAD-process execution is claimed.

The recovered implementation and additional early/large-declaration controls
passed all 1,274 MESH conformance cases in both Debug and Release on .NET 8,
including the 534 framing cases. All six independent MESH gates passed with
ezdxf 1.4.4 in both configurations. The
[qualification record](mesh-field-framing-qualification.json) records matching
test inventories, source and assembly hashes, gate results and the preserved
test receipts. The original late-blend tests are unchanged.

An earlier candidate (`852e94b`, tests `f8e5fba`) rejected every public core tag
after zero. It was withdrawn after the unchanged late-blend tests and primary
ordering guidance showed that this was too broad. Its provisional run and
source hashes are historical evidence only. The final behavior is declaration
uniqueness and list framing, with scalar reordering preserved.
