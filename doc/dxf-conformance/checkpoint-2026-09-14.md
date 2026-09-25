# DXF conformance checkpoint — 14 September 2026

> Historical PR #58 snapshot. For current merged support and PR #69–#75 verification, use the [later merged checkpoint](checkpoint-merged-2026-09-14.md). The original results below are retained, not replaced by later suite totals.

Production snapshot: [`82361078d1b84f2345d150fa4142ea315c04723f`](https://github.com/wieslawsoltes/netDxf/commit/82361078d1b84f2345d150fa4142ea315c04723f), after merged PR #58. Exact tree: `7e6507c4fa27b8db7a09f4a51352b961f6c8daac`. Target branch: `netstandard`.

The [173-row, nine-profile comparison](version-feature-matrix.md) and [machine-readable ledger](coverage.json) distinguish 161 typed rows from 12 separate raw-preservation rows. A row can represent a narrow field or a broad partial model; rows are not equally weighted standard requirements. **No completion percentage follows from the row or test counts.**

## Completed in this continuation

| PR | Implemented scope | Additional registered C# cases | Verified final-head CI |
|---|---|---:|---|
| [#57](https://github.com/wieslawsoltes/netDxf/pull/57) | HATCH spline fit points and optional tangent vectors; read/write, finite edits, deep clones, conversion, selected transforms/INSERT and safe older-profile rejection | 336 | [34816134157](https://github.com/wieslawsoltes/netDxf/actions/runs/34816134157) |
| [#58](https://github.com/wieslawsoltes/netDxf/pull/58) | Counted outer HATCH boundary packets; per-path headers, count/record boundaries and preservation of valid mixed paths | 408 | [34817070669](https://github.com/wieslawsoltes/netDxf/actions/runs/34817070669) |

PR #57 was merged before starting #58. Both final-head workflows passed Linux/Windows Debug/Release, the actual netstandard2.0 build, the generated comparison/ledger checks and the Linux source audit. Neither merge used only a preparation job or stale previous-head status.

The starting PR #56 suite had **12,163** cases. These two changes add **744**, yielding **12,907 passed / zero failed** in local signed-library Debug and Release runs and the downloaded final Linux Debug CI report. Fifteen Python documentation-integrity tests are a separate count. A netstandard2.0 build is not evidence of execution on every older consuming runtime.

For PR #57, all 478 source files in the CI archive match the complete locally tested tree `739f3d7cffec64b26f5a3a83964b5a8b29d8fc5f`. For PR #58, all 481 files match `7e6507c4fa27b8db7a09f4a51352b961f6c8daac`. Temporary publication helpers are absent from both final diffs and were removed before final-head validation.

## Reproduced failures, not just self-round-trips

| Test set | Unchanged production: Debug | Unchanged production: Release | Corrected complete suite |
|---|---:|---:|---:|
| 177 independent-byte fit/grammar cases, before the 159 extra API cases | 12,208 pass / 132 fail | 12,208 pass / 132 fail | 12,499 pass / 0 fail, both configurations |
| 408 outer boundary-count cases | 12,715 pass / 192 fail | 12,751 pass / 156 fail | 12,907 pass / 0 fail, both configurations |

The fit tests initially had invalid comments in binary input. This fixture error was corrected before both final unchanged-production comparisons; binary-comment rejection was not weakened. The boundary-count red difference follows the library's precise Debug exceptions versus Release null-return convention. Valid, empty-policy and physical-EOF controls have no baseline failures.

Both new independent ezdxf 1.4.4 verifiers were repeated on the final CI fixtures: six fit drawings / twelve spline packets, and twelve mixed-boundary drawings / 24 paths. They check actual imported values, neighboring data and audit outcomes; **zero audit errors and zero repairs** were reported. All sixteen `verify_hatch*.py` scripts also passed against the complete local final Release exports. These are independent-reader results, not native AutoCAD execution or rendered-appearance certification.

## Version and feature comparison

| Format family | Typed `DxfDocument` | Raw `DxfRawDocument` | New spline-fit output | Checked outer HATCH count |
|---|---|---|---|---|
| AC1009 / R11–R12 | Rejected | Ordered preservation and scoped edits; legacy binary framing | Not admitted | Typed not admitted |
| AC1012 / R13 | Rejected | Ordered preservation and scoped edits | Not admitted | Typed not admitted |
| AC1014 / R14 | Rejected | Ordered preservation and scoped edits | Not admitted | Typed not admitted |
| AC1015 / 2000 | Partial typed models | Separate raw pipeline | Reject populated metadata; empty control-only export unchanged | Tested text/binary |
| AC1018 / 2004 | Partial typed models | Same | Same | Tested text/binary |
| AC1021 / 2007 | Partial typed models | Same | Same | Tested text/binary |
| AC1024 / 2010 | Partial typed models | Same | Retained | Tested text/binary |
| AC1027 / 2013 | Partial typed models | Same | Retained | Tested text/binary |
| AC1032 / 2018 | Partial typed models | Same | Retained | Tested text/binary |

The new fit exporter preserves the existing conservative 2010+ packet profile; that boundary is **not** a claim that every individual field first appeared in 2010. Older populated exports reject before destination-stream writes, handle allocation or document preprocessing, including paper space, nested and unreferenced registered blocks. The file-path `Save` wrapper may already have truncated its destination; it is not transactional.

### Reconciled since the old PR #50 comparison

The main matrix no longer calls these completed increments missing: [gradient rotation, PR #51](hatch-gradient-angle.md), [continuous shift, PR #53](hatch-gradient-shift.md), [authored RGB/tint/mode, PR #54](hatch-gradient-color-state.md), [gradient packet dispatch, PR #55](hatch-gradient-packets.md), [standalone tangent transforms, PR #56](spline-tangent-transforms.md), fit metadata #57 and outer counts #58. Their broader SPLINE/HATCH rows remain partial. Existing AC1015 gradient downgrade remains lossy.

Optional ACI *input acceptance* is still not exact ACI identity/presence retention: exported indices are derived from the RGB model. Empty/absent-boundary HATCH entities also retain the existing typed discard behavior. Both are explicit lossy rows rather than being hidden by the new tested rows. The [PR #50 source audit](hatch-remaining-audit.md) is retained as historical evidence, with its own immutable source pin.

## API consequences

`HatchBoundaryPath.Spline.FitPoints` is an editable finite-valued `IList<Vector2>`. `StartTangent` and `EndTangent` are independently nullable OCS vectors. Absence differs from an explicit zero vector; duplicate/reordered fit positions are retained. Changing fit metadata does not rebuild control points or knots. Converting to a standalone spline preserves both representations rather than fitting a replacement curve.

For a transformation `p' = M p + b`, fit positions receive both `M` and `b`, while tangents receive `M` only. The regression scope includes arbitrary starting normals with rigid/uniform/reflection transforms and coplanar nonuniform INSERT scaling. It does not certify arbitrary-affine normal/plane treatment throughout HATCH.

Outer boundary parsing now consumes exactly the declared number of complete packets. Group 92 is required for each path; non-polyline flags are followed by edge count 93. Extra/duplicate/missing count framing is rejected instead of skipping unrelated entities. Whole packets can move relative to metadata; unrelated fields cannot be interleaved arbitrarily inside the packet. Existing zero/absent-count empty-HATCH discard is unchanged.

## Remaining implementation sequence

**Preservation and graph integrity.** Context-aware handle indexing, ownership/reactor/reference diagnostics, dependency-closed clone/import/remapping, integration of unknown records with typed editing, and transactional file saves remain separate implementation scopes. A raw record surviving does not prove that the typed model edits or evaluates it.

**Existing typed records.** Next HATCH work includes exact optional ACI metadata, empty-HATCH persistence, scalar ordering inside edge packets, knot/degree/periodicity relations, full affine geometry and associative source-reference closure. MESH subentity overrides and writer topology checks, MTEXT column contexts/linked records, and extended UCS/VIEW/VPORT references remain incomplete. Each independent increment needs version-aware regression evidence and a separate PR.

**Missing typed families.** The matrix still marks HELIX, MULTILEADER, structured TABLE, LIGHT/SECTION and inert OLE/proxy/ACIS/surface payload models as missing where applicable to this library. Object work includes generic XRECORD/dictionary variants, draw order/spatial filters, MLEADERSTYLE/TABLESTYLE, FIELD/DIMASSOC/GEODATA and rendering/material families. A missing cell does not assert that a feature existed in every historical format.

**Historical dialects and qualification.** R12/R13/R14 typed schemas, earlier raw families, headerless inference, per-property down-save reports, broader independent corpora, total-resource budgets and fuzzing remain open. Native AutoCAD open/AUDIT/save/reopen has **not** been executed. Full AutoCAD DXF capability is **not yet achieved**; the ledger explicitly records that status.

## Reproduction

```sh
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_hatch_spline_fit.py artifacts/conformance
python tools/verify_hatch_path_count.py artifacts/conformance
```

The feature notes link the inspected primary Autodesk tables and exact test contracts. See the current generated comparison for all 173 feature/pipeline rows rather than treating this checkpoint's selected rows as the full inventory.
