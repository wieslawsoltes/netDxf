# Stored references, explicit topology and affine HATCH editing

PR #92 extends the merged `c108513f3e13486c049d4d07fac79bf3d0bb7217`
baseline with five modules and a combined graph regression. The implementation
commit is `1ed8719e2b1a2dcbde9556f52e9951485b7c2f6e`, with tree
`c1e7c729aaccb05dea5b4980ecba45663c4bd5e9`. Subsequent qualification documents
and one missing XML parameter comment do not change its runtime behavior.
Exact source, executable, result and archive hashes are recorded in the
[integration receipt](stored-editing-qualification.json).

| Increment | Qualified behavior | New cases |
|---|---|---:|
| [SUNSTUDY](stored-sunstudy.md) | Immutable internal-version-0 empty-date packets and four actual source dependencies, with disclosed R2013/R2018 carrier adaptations | 60 |
| [TABLEGEOMETRY](table-geometry.md) | Immutable declared cells, ordered geometry packets and source identities for qualified R2004+ shapes | 142 |
| [VIEW live section](view-live-section.md) | Stored R2007+ reference, explicit-null presence, shared target protection and mapped clone adoption | 124 |
| [Polyline3D topology](polyline3d-topology.md) | Explicit insertion, removal and movement preserving retained VERTEX/SEQEND identities | 169 |
| [HATCH affine transforms](hatch-affine-transforms.md) | Direct stored spline and straight-boundary transforms with correct plane geometry and atomic refusal | 163 |
| [Mixed graph](eighth-mixed-modules.md) | Cross-family reference protection during topology and HATCH edits | 8 |

Debug and Release each passed **29,293 unique cases with zero failures**. The
result JSON files are byte-identical. Each configuration passed **all 106
independent verifiers** against its own 4,147 generated DXF artifacts. Independent
controls check actual packets and deliberate mutations; the corrected mixed gate
adds explicit HATCH plane, seed and count assertions that a clean ezdxf audit
alone did not establish.

All four Linux/Windows implementation CI jobs passed in
[run 34982636218](https://github.com/wieslawsoltes/netDxf/actions/runs/34982636218),
with 29,293 cases per job and all 106 independent gates in Linux Release. The
downloaded Linux Debug archive matches all **1,659 source files** in the pinned
implementation tree. Both downloaded Linux archives match their published SHA-256
digests and contain the expected complete passing result sets.

All five Release library targets and Debug netstandard2.0 compiled with zero
errors. The original integrated build had the existing 561 CS1591 warnings plus
one missing XML parameter comment warning. The integration supplies that comment;
the receipt distinguishes the original run from the final diagnostic check.
The field-audit self-test passes and indexes 105 IO files, 712 methods and 255
model/header files. All 18 coverage-ledger tests pass. The comparison now contains
**278 scoped rows across nine profiles**; these counts do not measure a percentage
of DXF completeness.

SUNSTUDY date arrays and solar evaluation, editable table layout, general conic
and pattern affine transforms, additional POLYLINE child families, automatic
section generation, historical typed dialects and native application execution
remain separate work. Unknown stored shapes remain opaque under their documented
admission rules. No native AutoCAD qualification or full-standard completion is
claimed.
