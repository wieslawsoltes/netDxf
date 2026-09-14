# MESH export preflight for mutable topology

## Defect and scope

The typed `Mesh` exposes mutable vertex, face and edge lists. Constructor/read checks do not protect a later save after those lists or their elements have been edited. The previous writer could emit invalid vertex indices, null/short faces, missing edges and nonfinite data. It also accumulated group-93 face-list size in an unchecked `int`, and some failures happened only after destination bytes and document preprocessing had already changed.

The writer now preflights every modern MESH in every registered block before preprocessing or stream writes. This includes model space, paper space, nested definitions and unreferenced definitions. It checks:

- nonnull faces with at least three indices, the existing 16-million-face model limit, and a serialized group-93 list size representable by `Int32`;
- finite vertex coordinates, nonnegative/in-range face indices and edge endpoints, nonnull edges and finite crease values.

Face-list size is accumulated in `long` before walking individual indices. A caller can share one large face array many times with modest memory consumption but still exceed the wire count; this is rejected before a multi-billion-entry write. The check does not copy the mesh or change any stored value.

This is intentionally **not** manifoldness, face winding, degeneracy, closure, self-intersection, subdivision evaluation or subentity overrides. Duplicate indices, unused vertices and the existing empty-MESH behavior are not newly rejected. Negative crease inputs continue to follow `MeshEdge`'s existing normalization to -1; this change does not add a competing crease setter policy. Concurrent caller mutation during save remains unsupported; this is not a snapshot or universal resource budget.

## Version and failure contract

| Format family | Typed modern MESH output |
|---|---|
| AC1009 / AC1012 / AC1014 | Typed historical profiles remain unadmitted; raw preservation is separate |
| AC1015 / 2000 | Existing pre-2010 MESH rejection runs first |
| AC1018 / 2004 | Same |
| AC1021 / 2007 | Same |
| AC1024 / 2010 | Validated text/binary export |
| AC1027 / 2013 | Same |
| AC1032 / 2018 | Same |

Legacy POLYLINE-based `PolygonMesh` and `PolyfaceMesh` behavior is unchanged. For a bad modern mesh, Debug throws an `InvalidOperationException` identifying the block and group-code context; Release preserves the public `Save` false-return convention. The supplied stream's bytes, position and ownership, document handle seed, entity identities, APPID registrations, layouts and active layout remain unchanged on preflight failure. The file-path wrapper can have truncated its destination before reaching this check and is still not transactional.

## Executed tests

The independent development baseline was merged PR #59, commit `77e7cdbd86fa0c8d2d31bf1d0a1eaaab1e006767`, tree `a4168bbd3663e539e264c594d80bebd6903bed56`.

The 337 initial tests against unchanged production report **12,932 passed / 312 failed**, in Debug and Release. These include 13 invalid mutable states across three modern profiles, both transports and four block placements, plus valid/empty controls. The shared-array overflow test was intentionally executed only after the preflight correction: the old writer would attempt over two billion serialized indices. With that additional case, corrected production reports **13,245 passed / zero failed**, both local signed-library configurations.

The retained independent-reader corpus has six text/binary drawings, two mixed triangle/quad faces per MESH, a deliberately unused vertex, full crease metadata, subdivision level 255, blend-crease state and XData. `verify_mesh_write_validation.py` checks all coordinates including exact binary64 precision, indices and metadata with ezdxf 1.4.4; **zero audit errors and zero repairs** on both configurations. Degenerate-face acceptance is a separate local compatibility control, not hidden inside the independent corpus.

```sh
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_mesh_write_validation.py artifacts/conformance
```

The independent patch was integrated after merged PR #60 at `4a60d52c430b346a695c8cdd5ad73f4e00a92800` (tree `dda8d8e44fa78c3834ede6982b098fbe967533ce`). The final combined signed-library suite passes **14,212 tests / zero failures**, in both local Debug and Release; the same independent MESH corpus passes again on these integrated exports.

Final-head CI must validate the integrated branch on Linux/Windows Debug/Release, build the actual netstandard2.0 target, check the generated ledger and retain the source audit. Test counts are scoped regression evidence, not standard-completion percentages or native AutoCAD qualification.

## Primary reference

[Autodesk MESH DXF reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-4B9ADA67-87C8-4673-A579-6E4C76FF7025.htm) defines vertex count/data, group-93 face-list sizing, edge endpoints and crease lists. Minimum face size and the 16-million-face ceiling are existing typed-library policies, not claimed independent requirements inferred from that table. Native AutoCAD has not been executed.
