# MESH override declaration boundary

The typed MESH reader now rejects a nonzero public group-90 subentity override
count after the counted crease list, or after the completed face list when
optional edge data is absent. It does so before any following group 91
or 92 can be reused as the mesh subdivision level or vertex count. Negative
declarations are malformed; positive declarations explicitly report that
subentity property overrides are unsupported. Rejection leaves the caller's
stream open and does not allocate in proportion to the declared count.

The [Autodesk MESH schema](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm)
identifies this suffix count, the following subentity marker and property count,
and four property kinds. It does not supply their complete value-packet grammar.
This change does not implement property overrides or interpret those values.
The new positive-count controls deliberately test unsupported declarations;
they are not claimed to be valid native override packets.

Absent and zero declarations remain accepted. The existing writer emits a
canonical zero declaration. Public fields are limited to the AcDbSubDMesh
subclass outside nested group-102 control packets and before XData starts.
Private packets, unknown later subclasses and trailing values after group 1001
cannot become public override declarations or overwrite the core topology.
Their application-specific payloads are not exposed as a new preservation API.
Following XData and following entities retain their existing behavior.

`MeshOverrideDeclarationTests.cs` registers 116 cases: 42 unsupported/malformed
declaration cases, 48 zero/absent/context cases, 24 optional-edge cases, and two
native carrier cases.
The context cases cover both transports in R2010, R2013 and R2018, nested private
controls, later subclasses, the XData boundary and ordinary counted topology.
Existing MESH counted-list, blend/crease, write-validation and profile tests
remain part of qualification.

The optional-edge tests first save and reload an actual netDxf drawing with an
empty edge list. Public constructors canonicalize null edges to an empty list,
so that output includes groups 94 and 95 with zero counts. The fixture then
explicitly omits those two packets to exercise the already accepted optional
edge representation. Zero and absent suffixes retain all vertices, faces,
subdivision, XData and the following entity; nonzero and negative suffixes reject.

The native controls reuse the exact AcDbSubDMesh bodies of MESH 343 and 380 from
the already pinned `table-oracle/sample_AC1024_ascii.dxf.gz`. The decompressed
source SHA-256 is
`c97e857047ad4cecd84754ea1a8638e47508e339fd489f1e895ee7332127b372`.
Both original bodies end in zero overrides. Minimal R2010 carriers replace the
common owner/proxy context and append a disclosed XData/following-LINE control;
this qualifies the exact subclass bodies, not the original complete drawing
or its common proxy data. Both transports preserve every ordered subclass
code and decoded value, including native floating-point values.

`tools/verify_mesh_override_declaration.py <artifact-directory>` independently
requires all 64 output drawings, compares their complete MESH bodies, verifies
the XData and following entity, checks all four native output bodies against
the pinned originals, and audits every drawing. Six corruption controls alter
the actual parsed output records to prove that a nonzero suffix, changed
subdivision, missing vertex, damaged following entity, missing MESH or changed
native suffix is detected.
