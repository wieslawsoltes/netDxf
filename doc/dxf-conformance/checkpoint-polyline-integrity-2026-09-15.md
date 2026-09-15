# POLYFACE and PolygonMesh integrity checkpoint

The published implementation for [PR #91](https://github.com/wieslawsoltes/netDxf/pull/91)
is `b77d731ef874fc21b594ffc208d30b92e7f48db6`, with tree
`27b6fa0145322aa5af26e486e6232486ce08d540`. Local implementation
`f4dd7268df1ed13427073fb73ead8ffe2415351c` has exactly the same complete Git tree.
The [qualification receipt](polyline-integrity-qualification.json) records the
completed local runs, runtime dependency hashes, independent reviews and source
checks. All four implementation CI jobs passed.

This increment follows the merged
[SECTION_MANAGER, HATCH and source identity checkpoint](checkpoint-section-hatch-2026-09-15.md).
PR #90 merged as `311504fe9c9220bdca69344c85d2b8d44794a7ae` after all four
[final CI jobs](https://github.com/wieslawsoltes/netDxf/actions/runs/34964658658)
passed 27,555 cases; Linux Release also passed all 98 independent verifiers.

## Resulting behavior

[POLYFACE](polyface-grammar.md) resolves signed, one-based face references by
fixed group-code slot. The first omitted or zero slot ends the active prefix;
negative values retain invisible-edge meaning. Faces may precede coordinates,
and header counts remain advisory. Invalid active indices and duplicate public
slots reject normally. Construction, explosion and export use the same active
prefix, with export checks covering all registered blocks before output mutation.
Face and mesh clones copy indices and face resources while retaining null
inheritance and keeping source event subscriptions independent.

[PolygonMesh](POLYGONMESH_CARDINALITY.md) checks the physical grid before
allocation: ordinary meshes require exactly M×N ordinary vertices, and supported
quadratic/cubic surfaces require exactly M×N spline controls. Generated samples
are classified separately and cannot fill missing control slots. Mixed flags,
unsupported surface types and missing or surplus controls reject. Export checks
mutable coordinates for nonfinite values; sampling and export also check the
implementation's degree and closure requirements.

Both PolygonMesh constructors and typed input use the existing **2 through 256**
range for each grid dimension, so the selected control product is at most 65,536.
This is a shared library admission bound, not an Autodesk DXF wire-format limit.
The change repairs physical cardinality and allocation checks; it does not claim
an Int32 overflow defect for signed 16-bit dimension fields. A complete grid may
be staged or loaded before its current degree/closure settings permit sampling.

The two mesh feature rows remain partial in the 272-row ledger. POLYFACE child
handles and arbitrary child metadata are outside this geometry increment.
PolygonMesh retains the admitted control grid while regenerating output samples
and child identities; source sample streams and arbitrary child metadata are
not newly preserved.

## Independent evidence

The [merged focused receipt](receipts/polyline-integration/README.md) records
2,239 passing cases in each configuration, including the prior source-identity,
HATCH, SECTION_MANAGER and ordinary Polyline3D controls. Its separate frozen
reviews retain 279 POLYFACE cases, 160 PolygonMesh cases, sixteen HATCH cases and
eight source-metadata cases per configuration. These are separate review and
integration inventories, not additional unique cases to add to the full suite.

The POLYFACE output gate checks 96 schema/API drawings and 576 decoded corruption
controls in each focused configuration. Its topology, clone and large-coordinate
cases add no native CAD qualification. The PolygonMesh gate checks 116 outputs,
twelve actual ASCII/binary corruptions and two native fourteen-packet chains.
The native chains are ordinary 3×4 grids from pinned TS1 R2000 and R2018 inputs:
POLYLINE `20F`, twelve VERTEX records and SEQEND. Only the parent's common owner
changes from source `1F` to carrier `17`. No native smoothed polygon mesh was
found in the recorded scan; smooth-grid evidence is explicitly synthetic.

## Final qualification

| Check | Result |
| --- | --- |
| Full .NET 8 Debug | 28,627 unique cases; zero failures |
| Full .NET 8 Release | 28,627 unique cases; zero failures |
| Additional cases over PR #90 | 1,072: 345 POLYFACE and 727 PolygonMesh |
| Independent verifiers | All 100 in each configuration |
| Generated output per configuration | 3,803 DXFs and 96 LAS files |
| Release compilation | netstandard2.0, net471, net48, net6.0 and net8.0 |
| Additional Debug compilation | netstandard2.0 |
| Build errors | Zero; 561 existing XML-documentation warnings per target |
| Python ledger/runner tests | 18 passed |
| Field audit | Self-test passed; 100 IO files, 703 methods, 250 model/header files |
| Coverage ledger | 272 scoped rows across nine version columns |

Both complete configurations have result SHA-256
`534ff8c17990ac1fe693051f0731cdef879523a1b7c2c36cebc11dd05c7a45cf`.
The conformance executables and their runtime dependencies were compiled from
`93f37735db35aae25bec6fcaef15cc0406e8368d`; the production and test source trees
are byte-identical to the qualified implementation. The additional library
builds identify `f4dd7268df1ed13427073fb73ead8ffe2415351c`. Their hashes are recorded
separately from the libraries used by the executed tests. All 350 source-file
hashes in the FieldAudit inventory match the qualified source. The receipt also
pins the separate Python source inventory and names both generators.

[Implementation CI](https://github.com/wieslawsoltes/netDxf/actions/runs/34965166782)
passed all four Windows/Linux Debug/Release jobs with 28,627 cases each and zero
failures. Linux Release also passed all 100 independent verifiers. These results
were checked against each job's decoded log. Final documentation-head CI is
required before merge. Compilation, executed regression cases, independent
packet checks and source inventories remain distinct evidence.

## Recovery accounting

The recorded recovery audit at **2026-09-15 11:37 UTC** accounted for 45 worktrees
and checkouts. All 35 original worktrees were clean; the single dirty current
worktree contained the active PR90 documentation and ledger edits. It recorded
no completed agent implementation left uncommitted. These counts describe that
captured audit. The qualification receipt pins the captured audit by SHA-256; its recovered-patch
accounting is separate from the current mesh qualification.

## Remaining scope

Full DXF support remains incomplete. Arbitrary POLYFACE/PolygonMesh VERTEX and
SEQEND metadata, original child identities, native smooth-surface regeneration,
rendering and native AutoCAD execution are outside this increment. General
association updates, editable TABLE and association graphs, modern modeler data,
historical typed dialects and the other limits in the current ledger remain
separate work. Passing tests and source audits do not establish complete-format
or production-scale interoperability.
