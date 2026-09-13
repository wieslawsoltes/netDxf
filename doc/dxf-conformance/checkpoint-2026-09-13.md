# DXF conformance execution checkpoint — 13 September 2026

Production snapshot: [`0fe160164428b6e5628bd7d91897451f274cdd66`](https://github.com/wieslawsoltes/netDxf/commit/0fe160164428b6e5628bd7d91897451f274cdd66), after PR #41. Exact source tree: `7e73d5ec5e270a0775615b846342c6138466422d`. Target branch: `netstandard`.

## Merged changes in this continuation

| PR | Scope | New registered C# cases | Final-head CI |
|---|---|---:|---|
| [#38](https://github.com/wieslawsoltes/netDxf/pull/38) | Counted MESH input: component codes, list bounds, topology references, crease counts, comment handling and allocation from consumed input | 99 | [34759129520](https://github.com/wieslawsoltes/netDxf/actions/runs/34759129520) |
| [#39](https://github.com/wieslawsoltes/netDxf/pull/39) | HATCH group 77 through `HatchPattern.IsDouble`, clone/INSERT/transform and serialization | 217 | [34760068596](https://github.com/wieslawsoltes/netDxf/actions/runs/34760068596) |
| [#40](https://github.com/wieslawsoltes/netDxf/pull/40) | 158-row machine-readable coverage ledger, nine-profile generated comparison, original audit archive and 15 separate Python integrity tests | 0 | [34761206654](https://github.com/wieslawsoltes/netDxf/actions/runs/34761206654) |
| [#41](https://github.com/wieslawsoltes/netDxf/pull/41) | HATCH group 98 and ordered OCS 10/20 seed points, counted input validation, finite edits, independent cloning and selected OCS transforms | 254 | [34762014287](https://github.com/wieslawsoltes/netDxf/actions/runs/34762014287) |

Each PR was merged after its final-head Linux/Windows × Debug/Release jobs passed, including netstandard2.0 builds and the applicable source/documentation checks. Changes were sequential: #38, #39, #40, then #41. No failing build was merged.

The starting PR #37 snapshot had 5,785 C# cases. These changes add 570, yielding **6,355 passing C# cases, zero failures**, in local signed-library Debug and Release execution and the retained final Linux Debug CI report. The **15 Python tests are documentation-integrity tests**, not additional CAD conformance cases. Compilation for netstandard2.0 does not establish runtime behavior on every consuming platform or older .NET runtime.

## Failure reproduction and independent checks

MESH's original parser failed 63 Release / 81 Debug cases in the initial 87-case regression set. Twelve extra empty-list/huge-declared-count probes were executed only after removing count-based preallocation. HATCH's original double-flag path failed 108 of 180 independently encoded cases in each configuration; another 37 API/control cases complete that increment. HATCH seed handling failed all 204 independently encoded cases against the preceding production snapshot; another 50 API/transform/boundary cases complete the increment. Detailed fixtures and policies are in [MESH validation](mesh-read-validation.md), [HATCH double](hatch-double-pattern.md), and [HATCH seeds](hatch-seed-points.md).

The optional **ezdxf 1.4.4** verifiers checked six MESH, twelve double-pattern HATCH and twelve seed-point HATCH output fixtures: all tested values matched, with zero audit errors and zero repairs. For PR #41, the downloaded CI artifact supplied a 6,355/0 report; all 428 archived source files matched the locally tested files, and its twelve seed fixtures were independently rechecked. These are fixture-specific results, not all-feature or native AutoCAD certification.

## What the comparison means

There are **six partial typed format families**: AC1015, AC1018, AC1021, AC1024, AC1027 and AC1032. The separate raw API also admits AC1009, AC1012 and AC1014, for **nine raw-preservation families**. See the [generated matrix](version-feature-matrix.md). A tested seed-point row does not make the whole HATCH row complete; raw retention does not confer typed editing or historical legality.

## Remaining implementation scopes

Full AutoCAD DXF capability is **not yet achieved**. The next field-level gaps include HATCH pixel size (group 47), unrelated ACAD XData retention during pattern-origin serialization, broader boundary-list grammar and transform fidelity; MESH subentity overrides and writer validation; and MTEXT columns. Those are separate implementation PRs, not implied by the increments above.

Broader work remains: typed historical R12/R13/R14 grammars, earlier/headerless raw profiles, missing typed entity/object families, preservation integrated with typed editing, context-aware identity/ownership/reference graphs and dependency-safe imports, explicit downgrade diagnostics, ACIS/ASM and application payload semantics, dynamic/annotative/associative behavior, resource/fuzzing qualification and actual native AutoCAD open/AUDIT/save/reopen runs. The ledger keeps these missing or partial; no completion percentage is inferred from row or test counts.
