# Association, child-record and stored-data recovery checkpoint

This checkpoint completes the recovered implementation following PR #88. The
published implementation is `ff2233122991a4454d42cc850b748dd9746a84a6`, with tree
`da7c6b2d62a2c593d7d24564290646d6664c39f9`. Its local tested equivalent is
`a1c83db871518b8905d0f2ebf68a55a550dafe1f`; the GitHub Git tree matches exactly.
The [machine-readable receipt](recovery-qualification.json) records both source
identities, configuration hashes and validation evidence.

## Implemented behavior

- [DIMASSOC](stored-dimassoc.md) retains qualified immutable native point packets,
  exact geometry references and dimension ownership. Private paths remain opaque;
  unsupported cloning, erasure and conversion fail before mutation.
- [UCS base references](ucs-record-base.md) retain group 79/346 state, explicit
  numeric-null presence and exact registered target identities. Atomic edits and
  explicit cross-document mapping maintain reference counts.
- [TABLECONTENT](table-content.md) exposes immutable four-subclass packets,
  conservative outer counts and exact source dependencies. Native composite
  [TABLE ownership](composite-table-ownership.md) and [private XRECORD
  bodies](private-xrecords.md) retain their qualified structures.
- [Polyline3D child records](polyline3d-records.md) retain ordinary unsmoothed
  VERTEX/SEQEND identities, common metadata and optional fields. Coordinate edits,
  synchronized reversal, safe clones and qualified moves retain coherent links.
  Independent before/after checks cover stale parent handles, clone capture and
  incoming-header reference removal.
- [HATCH spline relations](hatch-spline-relations.md) validate nonperiodic degree,
  knot and control relationships and preserve accepted indexed weights without
  changing the stored rational flag.
- [MESH declarations](mesh-override-declaration.md) reject unsupported nonzero
  override counts before following fields can overwrite topology. Exact native
  zero-count bodies remain covered.
- [SUNSTUDY producer evidence](sunstudy-assessment.md) pins original successful
  and failed attempts, disclosed carrier adaptations, and scoped raw preservation.
  It does not introduce a typed SUNSTUDY model.

The [seventh mixed suite](seventh-mixed-modules.md) verifies these association,
UCS and TABLE dependencies together. Eight recovered source-identity cases also
prevent generated collection objects from acquiring legitimacy from discarded
records that happen to carry the same handle.

## Qualification

| Check | Result |
| --- | --- |
| Full local .NET 8 Debug | 26,980 unique cases; zero failures |
| Full local .NET 8 Release | 26,980 unique cases; zero failures |
| Additional cases over PR #88 | 1,572 |
| Result files | Identical SHA-256 in both configurations |
| Independent verifier suite | All 96 scripts pass in each configuration |
| Generated fixtures | 3,491 DXFs and 96 LAS files per configuration |
| Release library compilation | netstandard2.0, net471, net48, net6.0 and net8.0 |
| Additional Debug library compilation | netstandard2.0 |
| Compilation errors | Zero; 561 existing XML-documentation warnings per target |
| Python ledger/runner tests | 18 passed |
| Field-audit self-test | Passed |
| Source inventory | 96 IO files, 695 methods, 248 model/header files |
| Coverage ledger | 270 scoped rows across nine version columns |

Both result files have SHA-256
`5f96fd348195f0ed99030915f34a1f43b0abacb305a6af36ab60d35cb230973c`.
[Implementation CI 34959884293](https://github.com/wieslawsoltes/netDxf/actions/runs/34959884293)
passed Linux and Windows in both configurations, each with 26,980 successful
cases. Linux Release also passed all 96 independent verifiers. Final
documentation head `fbe2a96a6c77398a95a03d1884ec7b90f2b59f06` passed all four
jobs in [CI 34960829144](https://github.com/wieslawsoltes/netDxf/actions/runs/34960829144).
PR #89 merged into `netstandard` at `5592edade61a9fe22215b8f9e098fa8beda46b76`.

## Recovery and remaining scope

All identified uncommitted VERTEX, TABLECONTENT, source-reference and SUNSTUDY
work is represented in committed source. The original source-reference patch is
preserved by local commit `e98866c` in its previous worktree; its tested equivalent
is committed here.
Separate implementation branches and qualification receipts were integrated
without replacing newer TABLECONTENT/UCS fixes with older checkpoints.

These results qualify the documented stored schemas, reference behavior and
transport boundaries. Full DXF support remains incomplete. Editable TABLE and
association schemas, other POLYLINE child families, arbitrary topology and
metadata-graph mappings, HATCH associative source closure and periodic evaluation,
MESH override values, typed SUNSTUDY, modern SAB/ACDSDATA and historical typed
dialects remain separate work. Native AutoCAD open/AUDIT/save/reopen, rendering,
regeneration and production-scale qualification have not been performed.
