# MESH Blend Crease field retention and editing

Baseline: `e248236bee2b84ca3179649d79532fd1866e130a`, after merged PR #36.

## Delivered field

`Mesh.BlendCrease` is the Boolean value of MESH group 72. The reader previously ignored it, the writer always emitted zero, and the model had no editable field. It is now read, edited, cloned and written independently of per-edge crease values and the subdivision level. The default is false; absent input normalizes to an explicit zero on typed output. Use the raw preservation API when original field absence or lexical byte identity is required.

Group 72 accepts only the documented 16-bit values zero and one. Other values throw InvalidDataException internally, preserving public Debug exception / Release null behavior. A value of two does not become true. Tests check the group-code diagnostic and caller stream ownership. This correction does not change the group-code primitive classifier.

```csharp
var mesh = new Mesh(
    new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY },
    new[] { new[] { 0, 1, 2 } },
    new[] { new MeshEdge(0, 1, 1.5) })
{
    SubdivisionLevel = 2,
    BlendCrease = true
};
var document = new DxfDocument(DxfVersion.AutoCad2010);
document.Entities.Add(mesh);
var copy = (Mesh)mesh.Clone();
copy.BlendCrease = false; // does not change the source
document.Save("mesh.dxf");
```

## Version/pipeline matrix

| Capability | AC1015 / 2000 | AC1018 / 2004 | AC1021 / 2007 | AC1024 / 2010 | AC1027 / 2013 | AC1032 / 2018 |
|---|---|---|---|---|---|---|
| Typed MESH export | Rejected by PR #36 | Rejected | Rejected | Tested | Tested | Tested |
| Blend Crease read/write, clone and explosion | No export profile | No export profile | No export profile | Text/binary tests | Text/binary tests | Text/binary tests |
| Invalid 72 values | Same parser when encountered | Same | Same | Tested rejection | Tested rejection | Tested rejection |

No new historical profile or implicit downgrade is added. Out-of-profile input retention is not a statement of historical legality. The typed pre-2000 admission boundary and raw preservation behavior remain unchanged.

## Executed evidence

The first 48 independently encoded behavioral fixtures compiled against unchanged production and yielded **5,748 passed / 24 failed**, Debug and Release. Twelve enabled-flag cases lost data; twelve malformed-flag cases were accepted. The remaining absent/zero cases passed.

After implementation, thirteen additional direct API/default/clone/explosion cases bring the addition to **61 registered cases**. The complete signed-library suite reports **5,785 passed / 0 failed**, Debug and Release on the local .NET 8 workbench. No new API is claimed to have existed in the old-implementation run.

Fixtures cover all three supported MESH format families and both transports, absent/zero/one values, early and late tag positions, comments, following XData, clone output and independence, block clone and INSERT explosion. Exact output group-72 values are inspected through the raw pipeline; new-field tests do not depend solely on the newly added property accessor.

The independent ezdxf 1.4.4 verifier loads six exported drawings containing original and cloned meshes, agrees on enabled blend flags, subdivision level, face/crease data and XData, and reports **zero audit errors or repairs**. Final-head Linux/Windows Debug/Release, netstandard2.0 compilation and source audit remain merge gates.

```sh
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_mesh_blend.py artifacts/conformance
```

This implements subdivision metadata, not a subdivision renderer/evaluator. Subentity overrides, arbitrary tag-order robustness inside topology arrays, malformed topology quotas/validation, general schema downgrade and AutoCAD-process verification remain separate work.

## Primary source

Autodesk MESH group 72 (Blend Crease), distinct from edge crease values and subdivision level:
https://help.autodesk.com/cloudhelp/2015/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm
