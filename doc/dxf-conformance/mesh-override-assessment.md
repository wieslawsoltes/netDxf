# MESH subentity override assessment

The current typed MESH reader and writer do not implement subentity overrides.
This assessment records the boundary before implementation. It does not qualify
an editable property model or infer private property value encodings.

## Primary schema and existing behavior

The [Autodesk MESH reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
describes a separate suffix after crease data: group90 declares the number of
subentities, group91 identifies each subentity, group92 declares its property
count, and group90 identifies a property kind. The listed kinds are color,
material, transparency and material mapping (values0 through3). The page does
not specify each property's value-packet grammar, marker numbering or private
flags. These omitted meanings must not be inferred from the shared numeric codes.

`DxfReader.ReadMesh` currently treats later group91 and group92 values as core
subdivision and vertex fields. `DxfWriter.WriteMesh` always writes a final
`90=0`. A proper implementation therefore needs a distinct parsing context after
core topology; simply adding properties to the existing switch would be unsafe.

## Evidence inspected

The shared pinned ACadSharp corpus contains six MESH records: handles343 and380
in each AC1018, AC1021 and AC1024 sample. All six end in override-count zero.
These are zero-override controls, not positive override evidence. Earlier-profile
records do not establish historical format legality.

The source of pinned ACadSharp commit
`f6a7f1e7e502c6fe9d1e840e576b6124ec0ded11` leaves override processing as a TODO
in `readMesh`, and `writeMesh` emits zero. The independent
[ezdxf MESH internals reference](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mesh.html)
also states that it lacks the property-override protocol. Its ordinary topology
reader is not an oracle for these fields.

A bounded scan of sixteen LibreDWG R2010/R2013/R2018 DXF candidates at commit
`34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43` found no MESH entity records. The
[research inventory](../../tools/mesh_oracle/override-assessment.json) pins their
repository paths, exact SHA-256 values and sizes. This is a scoped negative
finding, not a claim that no public positive fixture exists.

## Proposed next boundary

Before implementing a typed override property, obtain an exact positive native
or independently produced packet and identify the value grammar using primary
schema evidence. Start with one observed property kind, retaining its subentity
marker and ordered raw packet; keep other kinds opaque. Qualify nonzero counts,
multiple markers/properties, absent and unknown variants, exact reference
identity and resource rename/removal behavior, plus real corruption controls.

Any retained opaque suffix needs explicit lifecycle rules. Topology edits can
invalidate subentity markers; material-mapper data can depend on transforms;
material references can require document remapping. Until those relationships
are proven, unsupported edits, clones and transfers must reject before mutation
or output rather than rewriting the suffix as an empty override list.

No production source has been changed by this assessment. With the currently
inspected evidence, a claim of completed typed override semantics would exceed
what the available documentation and positive packets establish.
