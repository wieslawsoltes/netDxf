# MESH terminal override declaration

After a public zero subentity-override declaration, MESH core parsing is complete.
The reader now rejects a subsequent public version, blend flag, subdivision,
vertex/face/edge/crease count or item, or a second override declaration. The
affected groups are 71, 72, 90–95, 10/20/30 and 140. The diagnostic identifies
MESH, the unexpected group, its position and the terminal declaration phase.
The reader rejects before constructing an admitted mesh or allocating from the
trailing count, and leaves the caller's stream open.

Previously, an otherwise complete mesh with subdivision 3 followed by `90=0`
and `91=7` loaded successfully with subdivision 7. The trailing subentity marker
had been reused as a core subdivision field. Repeated vertex, face and edge
packets could similarly replace already parsed topology. Other trailing core
tags were silently ignored. The terminal declaration now closes that parsing
phase even when the input later re-enters the public AcDbSubDMesh subclass.

The guard runs only after the existing context checks. Values inside nested
group-102 packets or a later unrelated subclass cannot become public fields.
Once XData starts, later values cannot restart public mesh parsing. Existing
acceptance of non-XData trailers beyond that boundary remains unchanged; the
tests establish parser scoping, not validity or preservation of such a trailer.
Comments remain ignored, and following XData and entities remain aligned.
Private and unknown subclass payloads retain the existing writer behavior;
this fix does not introduce a storage API for those values.

The declaration is also terminal when the optional edge/crease packets are
absent. An absent declaration keeps existing behavior. A first negative count
remains malformed, and a first nonzero count remains explicitly unsupported.
There is no new decoding of override property values or guessed marker grammar.
The [Autodesk MESH reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
documents the declaration, subentity marker, property count and four property
kinds; it does not provide their complete value-packet grammar. See the
[override assessment](mesh-override-assessment.md) and
[existing declaration boundary](mesh-override-declaration.md).

`MeshTerminalDeclarationTests.cs` adds 240 cases across R2010/R2013/R2018 and
both ASCII and binary transports. Its 168 rejection cases cover every affected
group, a duplicate zero and public subclass re-entry, each with present or
absent optional edge data. Its 72 accepted controls exercise nested private
packets, private subclass markers, later subclasses, XData and comments while
checking exact geometry, subdivision, XData and the following LINE before and
after save/reload. The existing declaration suite retains its exact native
zero-override bodies and unsupported/malformed declaration controls.

`tools/verify_mesh_terminal_declaration.py <artifact-directory>` independently
checks all 72 accepted output drawings, their complete ordered public MESH
packets, actual mesh/following-entity identities, source profile, transport and
external audit. Twelve corruption controls append each forbidden field to an
actual output packet and require the wire comparison to fail. The existing
`verify_mesh_override_declaration.py` continues to qualify 64 zero/absent outputs,
including four exact native subclass outputs and six corruption controls.
