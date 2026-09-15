# Recovered retained geometry and explicit stored-style editing

PR #95 extends merged PR #94 at
`bb8c73cefdeecaee3a65d3abf3fc12b19d2283e4`. The
[conversation and repository recovery note](continuation-recovery-2026-09-15.md)
traces the earlier work and distinguishes unpublished component changes from
code already integrated by earlier aggregate commits.

| Qualification item | Verified result |
|---|---|
| Implementation commit | [`ec085f403eb56f361f8ba05574b2bb2f94a24e1f`](https://github.com/wieslawsoltes/netDxf/tree/ec085f403eb56f361f8ba05574b2bb2f94a24e1f) |
| Implementation tree | `cde35e08b2dbce2d4f7ff0c665e20301f9c9a52a` |
| Debug conformance | 34,836 passed; 0 failed |
| Release conformance | 34,836 passed; 0 failed |
| Independent output verifiers | 127 passed; 0 failed in each local configuration |
| Generated DXF inventories | 7,363 DXF files in each configuration; 7,796 total files in each hashed inventory |
| Release library targets | `netstandard2.0`, `net471`, `net48`, `net6.0`, `net8.0`: all passed |
| CI run and platform jobs | [Run 35029062734](https://github.com/wieslawsoltes/netDxf/actions/runs/35029062734): all four Linux/Windows Debug/Release jobs passed; 34,836 cases and 0 failures each |
| Exact source/output receipt | [Source, result, output and build/audit evidence](pr95-integration-qualification.json) |

Both local configurations have identical result JSON bytes, with 34,836 unique
case names and 1,540 additional cases over the PR #94 checkpoint. The 127 gates
are the scripts discovered by the CI runner in `tools/verify_*.py`; nested
producer-corpus tools and historical report-only challenges are separate.

The five Release library targets and Debug `netstandard2.0` target compile with
zero errors and the existing 561 missing-XML-comment warnings per target.
FieldAudit self-tests pass, and its final inventory covers 115 IO files, 756
methods and 274 model/header files; all 389 inventory source hashes match. This
is syntax inventory evidence, not semantic DXF qualification. The
[build/audit receipt](receipts/pr95-integration/build-audit-qualification.json)
preserves the original locally tested commit, and the integration receipt maps
it to the GitHub commit with an identical complete tree.

The downloaded Linux Debug CI archive contains all 2,986 source files, whose
Git blob hashes match the pinned implementation tree. Both Linux CI result
inventories and outcomes match the local suite by unique case name; their
execution order differs. The Linux Release archive also confirms all 127
independent verifiers. Windows Debug and Release job logs each explicitly
report 34,836 passed and zero failed cases. The
[CI archive verification receipts](pr95-integration-qualification.json) preserve
the artifact IDs and verified SHA-256 digests.

Generated inventories include intentional rejection inputs and intermediate
artifacts. Each verifier specifies its valid-output and corruption-control
inventory; the total file count does not certify every generated file as valid.

| Increment | Implemented contract |
|---|---|
| [Ordinary legacy 2D POLYLINE retention](polyline2d-records.md) | Actual parent/VERTEX/SEQEND packets and identities, optional/inherited width distinctions, qualified reversal and transforms, exact source references and guarded clone/adoption |
| [Legacy profile and target removal integration](receipts/polyline2d-integration-20260915/README.md) | Actual child source-profile diagnostics, separate lightweight identifier rules, and removal protection from current retained child references |
| [MESH declaration framing](mesh-field-framing.md) | Public singleton/count uniqueness and orphan-item rejection without treating a zero override count as an end marker; unique reordered core fields remain accepted |
| [Periodic HATCH conversion/evaluation](hatch-periodic-conversion.md) | Qualified expanded/compact adapters, stored knot preservation, active-domain sampling, exponent-scaled local evaluation and bounded exact-rational fallback |
| [TABLESTYLE scalar replacement](table-style-editing.md) | Explicit classic header and snapshot-bound row scalar edits, with exact STYLE references, immutable snapshots and unchanged private/map packets |
| [CELLSTYLEMAP entry names](cell-style-map-editing.md) | Ordered name replacement with fixed IDs/types/counts/formats, exact dependencies, bounded strings and atomic snapshot updates |
| [Eleventh mixed graph](eleventh-mixed.md) | Native table backing with explicit style/name edits, retained legacy children, periodic boundaries, resource renaming and ordered reference release |

The recovered legacy module retains its original native and independent
producer evidence and was replayed against the newer PR94 baseline. Clean
clones retain the source-profile requirement. Private metadata graphs,
unsupported fitted variants and unqualified topology changes still reject.

MESH framing follows the public subclass and complete counted-list boundaries.
Zero override count is not a terminator, and positive override value packets
remain unsupported. The discarded terminal-declaration interpretation was not
used to remove already-qualified field ordering.

Periodic conversion/evaluation remains a bounded operation over supported
cyclic layouts, positive finite weights with a nonzero minimum/maximum weight
ratio, and resolvable parameter grids. The
numerical path preserves stored controls and weights while handling local
weight scale, cancellation and underflowed basis contributions. The exact
fallback has an explicit arithmetic budget; expensive cases can reject instead
of promising universal numerical evaluation. Repeated knots, other compact
conventions, adaptive error bounds and native curve rendering remain separate.

Style and map editing operates on the stored packets supplied by the caller.
It neither regenerates TABLE geometry nor synchronizes duplicated format
representations. Stale/foreign row snapshots, invalid name counts/strings,
reentry and invalid source graphs reject before publishing new snapshots.
Independent caller changes made during enumeration are not rolled back.

The version comparison retains **297 scoped rows across nine profiles**: six
typed modern families and nine raw-preservation families. Broad family rows
remain partial. The VPORT frozen-layer encoding is still unresolved after its
primary-reference and producer-corpus assessment; no pointer encoding was
invented to close that row.

Full DXF standard capability remains unfinished. Major boundaries include
complete TABLE/style formatting and regeneration, arbitrary modeler and proxy
schemas, private dependency import, automatic associative geometry regeneration,
historical typed dialects and schema-aware conversion, remaining MESH override
values and unqualified periodic/fitted geometry. Compilation of a framework
target is not execution on every consumer runtime. Native AutoCAD
open/AUDIT/save/reopen is not claimed, and regression counts are not a
percentage of standard completeness.
