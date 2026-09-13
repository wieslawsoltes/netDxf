# HATCH and DXF conformance checkpoint — 13 September 2026

Production snapshot after PR #50: `f4c8234494a3088677a366f9575feb05b1a88a0b`; exact tree `365eec7a7c69da87838a2f8e7cfc286b7dc84a27`. Branch: `netstandard`. The generated version comparison and coverage.json are pinned to this implementation snapshot, not to an unverified work branch.

## Integrated implementation increments

| PR | Scope | Additional registered C# cases | Final-head CI |
|---|---|---:|---|
| [#45](https://github.com/wieslawsoltes/netDxf/pull/45) | HATCH polyline closure retained in reader and clone | 50 | [34777603495](https://github.com/wieslawsoltes/netDxf/actions/runs/34777603495) |
| [#46](https://github.com/wieslawsoltes/netDxf/pull/46) | Optional bulges, checked polyline lists and incremental count handling | 450 | [34778287986](https://github.com/wieslawsoltes/netDxf/actions/runs/34778287986) |
| [#47](https://github.com/wieslawsoltes/netDxf/pull/47) | Counted pattern lines and dash packets, local field ordering and comments | 450 | [34778652397](https://github.com/wieslawsoltes/netDxf/actions/runs/34778652397) |
| [#48](https://github.com/wieslawsoltes/netDxf/pull/48) | Exact boundary classification; clone/transform/associative-update consistency | 424 | [34779221271](https://github.com/wieslawsoltes/netDxf/actions/runs/34779221271) |
| [#49](https://github.com/wieslawsoltes/netDxf/pull/49) | Final angle/scale/name/fill metadata resolved before pattern-coordinate conversion | 552 | [34780099625](https://github.com/wieslawsoltes/netDxf/actions/runs/34780099625) |
| [#50](https://github.com/wieslawsoltes/netDxf/pull/50) | Non-stalling edge dispatch, scalar/flag/count validation and incremental spline/reference storage | 618 | [34780590182](https://github.com/wieslawsoltes/netDxf/actions/runs/34780590182) |

PRs #47 and #49 were developed independently in concurrent repository work. #47 was reviewed and merged before integration into #48; #49 was already merged and was integrated into #50 before final testing. Shared reader files and test registrations were reconciled without discarding either implementation. Dependent changes were merged only after final-head Linux/Windows Debug/Release jobs, netstandard2.0 builds, generated-ledger checks and source audit passed.

The starting merged PR #44 snapshot had 6,596 C# cases. These increments add **2,544**, yielding **9,140 passed / 0 failed** in local signed-library Debug and Release and the retained final Linux Debug CI report. The separate **15 Python tests** validate ledger integrity, not CAD semantics. The corpus is not a completion percentage or evidence of every target runtime.

## Evidence retained and independently repeated

For PR #50, all **455** files in the downloaded final CI source archive match the locally tested source. Its 9,140-case report has no failures. Six independent ezdxf 1.4.4 verifiers were rerun on those CI fixtures: closure, sparse bulges, boundary flags, pattern lists, pattern ordering and edge packets.

The first five verifiers check their stated semantic fields before audit and report no audit errors or repairs. The last verifier checks 48 individual primitive-edge fixtures, including open line/spline paths; it deliberately does not certify valid closed fill areas or perform a whole-drawing qualification claim. No native AutoCAD process was executed.

Old-code evidence is preserved in each feature note. In particular, the edge-type stall was reproduced only in bounded child processes, not by running a potentially nonterminating full old suite. Huge-count probes were run only after removing producer-controlled preallocation.

## Current comparison and known losses

The refreshed comparison has **167 scoped feature rows** across **nine raw-preservation families** and **six partial typed families**. It now includes pixel size (#43), ACAD XData preservation (#44), and every narrow HATCH increment above. Broad HATCH support remains partial. R11/R12, R13 and R14 are still raw-only; typed historical read/write is not implied.

The spline fit-data row is corrected to **known lossy** even for 2010+: validating and consuming a packet is not retaining it. New source-audit rows expose fractional gradient-shift loss and overwritten gradient rotation. These are unresolved defects, not features delivered by this checkpoint. See [remaining HATCH audit](hatch-remaining-audit.md).

## Remaining implementation work

The nearest HATCH work is authored spline fit/tangent storage, gradient shift/rotation/color metadata, outer path-list grammar, geometric validity and general affine/reference fidelity. Other substantial scopes remain: MTEXT columns; MESH subentity overrides and writer validation; full UCS/VIEW/VPORT contexts; missing typed entities/objects; context-aware ownership/dependency-safe imports; integration of preservation with typed edits; historical dialects and explicit downgrade policies; ACIS/ASM/application semantics; dynamic/annotative/associative behavior; fuzzing/resource qualification; and native AutoCAD open/AUDIT/save/reopen interoperability.

**Full AutoCAD DXF capability is not yet achieved.** The repository now contains tested increments and a current evidence-linked comparison; raw preservation, typed editing, geometric evaluation and native CAD qualification remain distinct contracts.
