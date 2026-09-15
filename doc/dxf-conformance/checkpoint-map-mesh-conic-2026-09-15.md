# Stored map access, retained mesh records and affine HATCH conics

PR #93 extends the merged `6128f0533becdb8b63b0755054ecc37eac7266cf`
baseline. Its qualified implementation is
`358af5f72e1af4eaaed0c9293b7dc5b841bad391`, with tree
`0b190ff8de849b3af99d04ad470c2e7d473b4e31`. Exact source, executable, result
and downloaded archive hashes are recorded in the
[integration receipt](map-mesh-conic-qualification.json).

| Increment | Qualified behavior | Net new cases |
|---|---|---:|
| [CELLSTYLEMAP](cell-style-map.md) | Immutable ordered entries and formatting packets, exact native source dependencies and TABLESTYLE access | 160 |
| [SECTION_MANAGER membership](section-manager-membership.md) | Explicit atomic replacement of a loaded manager's ordered section list and update flag | 84 |
| [PolygonMesh retained records](polygonmesh-records.md) | Ordinary VERTEX/SEQEND identities, metadata, physical order and native associations | 387 |
| [HATCH conics](hatch-conic-affine.md) | Direct arc, ellipse and bulge transforms with interval, direction and plane preservation | 202 |
| [Target-version report](version-compatibility.md) | Read-only diagnostics for documented writer rejections and omissions | 638 |
| [Mixed graph](ninth-mixed.md) | Actual map, table, section, mesh-child and HATCH relationships during editing and removal | 8 |

The HATCH increment adds 205 cases and replaces three previous tests that required
rejection of the newly supported transforms. The total increases by **1,479**.
Debug and Release each passed **30,772 unique cases with zero failures**; their
result JSON files are byte-identical. Each configuration passed **all 112 independent
verifiers** against its own **4,825 generated DXF files**. Independent controls
include actual output corruption, complete source-identity checks and separate
world-space conic evaluation. Clean ezdxf audits alone are not treated as proof of
the edit semantics.

All four Linux/Windows implementation CI jobs passed in
[run 34989160308](https://github.com/wieslawsoltes/netDxf/actions/runs/34989160308),
with 30,772 passing cases per job and all 112 independent gates in Linux Release.
The downloaded Linux Debug archive matches all **1,844 source files** in the
pinned tree. Both downloaded Linux archives match their published SHA-256 digests
and contain the expected complete passing result sets.

The first combined verifier run exposed a filename collision: the older SECTION
gate counted the new `section-membership-*` fixtures as its own outputs. The fixed
gate retains its exact 70-file inventory, and the membership gate separately
requires all 52 of its outputs. Both complete verifier sets were rerun successfully;
the correction changes no C# production or conformance source.

All five Release library targets and Debug netstandard2.0 compiled with zero
errors and the existing 561 CS1591 warnings per library target. Conformance builds
have zero warnings. The unchanged field-audit tool passes its self-test and indexes
109 IO files, 724 methods and 259 model/header files. All 18 coverage-ledger tests
pass. The current comparison contains **284 scoped rows across nine profiles**;
these counts do not measure a percentage of DXF completeness.

`DxfTableStyle.CellStyleMap` changes its return type from `DxfOpaqueObject` to
`DxfDatabaseObject`. This is a **source and binary compatibility break**. Consumers
must recompile and use `StoredCellStyleMap.Payload` for qualified typed maps, or
pattern-match `CellStyleMap` as `DxfOpaqueObject` for opaque `Tags` access. The
[module migration example](cell-style-map.md#api-migration) distinguishes
the two cases.

The retained-record work also closes a pre-existing Polyline3D save/load mismatch:
ordinary XData growth could produce a child packet exceeding the reader's tag
budget. Both retained mesh families now check per-packet and aggregate physical
tag budgets before output or handle allocation. Archived before/after cases cover
both transports and distinguish this bug from PR #92's topology operations.

CELLSTYLEMAP formatting evaluation and editable table backing, SECTION_MANAGER
creation/erasure and automatic section generation, fitted mesh retention and
additional POLYLINE families, HATCH pattern/gradient affine transforms, historical
typed dialects and complete version conversion remain separate work. The version
report lists known failures and losses; an empty report is not a compatibility
certificate. Native AutoCAD execution and full-standard completion are not claimed.
