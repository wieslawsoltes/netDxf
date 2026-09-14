# Merged DXF conformance checkpoint — 14 September 2026

Production baseline: **PR #75**, commit [`6420dd213b018d6f8698167aca4473bc3d3a499f`](https://github.com/wieslawsoltes/netDxf/commit/6420dd213b018d6f8698167aca4473bc3d3a499f), tree `280e1b80946fc2e4133e69ead3bc8651825b13bd`. Target branch: `netstandard`.

The [189-row, nine-profile comparison](version-feature-matrix.md) and [coverage ledger](coverage.json) distinguish **172 typed rows and 17 raw-pipeline rows**. Rows describe unequal scopes, from narrow fields to broad partial families; neither row counts nor test counts produce a percentage of standard coverage. **Full AutoCAD DXF capability remains incomplete.**

## Actual publication in this continuation

The previously delivered local implementation batch is now published and merged, rather than still awaiting authenticated publication. A newly reproduced embedded-object safety defect was then corrected in its own PR. Shared-source changes were merged sequentially; local Debug/Release runs, independent-reader processes and the four CI matrix jobs executed concurrently where independent. No sub-agent execution is claimed.

| PR | Scope | Added C# cases | Accumulated passing cases | Final-head CI | Merge commit |
|---|---|---:|---:|---|---|
| [#69](https://github.com/wieslawsoltes/netDxf/pull/69) | Knot-aware SPLINE reversal and periodic control/weight phase | 327 | 15,919 | [34859831437](https://github.com/wieslawsoltes/netDxf/actions/runs/34859831437) | `095843520c22c4154fecb4300a2fb42b7cd3f7c5` |
| [#70](https://github.com/wieslawsoltes/netDxf/pull/70) | Explicit typed/raw atomic file saves | 156 | 16,075 | [34861184112](https://github.com/wieslawsoltes/netDxf/actions/runs/34861184112) | `b04a720351572c8c90e21cf56f34d872759ff155` |
| [#71](https://github.com/wieslawsoltes/netDxf/pull/71) | Contextual raw identities, references and owner diagnostics | 160 | 16,235 | [34862523340](https://github.com/wieslawsoltes/netDxf/actions/runs/34862523340) | `2115ad288c59abb0822255c97b64720f9c5880bb` |
| [#72](https://github.com/wieslawsoltes/netDxf/pull/72) | Selected outgoing traversal and simultaneous handle remapping | 80 | 16,315 | [34863629586](https://github.com/wieslawsoltes/netDxf/actions/runs/34863629586) | `ba60aca65bb92b796d300560ce2878e52423013f` |
| [#73](https://github.com/wieslawsoltes/netDxf/pull/73) | Empty HATCH input retention and safe typed export rejection | 192 | 16,507 | [34865164429](https://github.com/wieslawsoltes/netDxf/actions/runs/34865164429) | `7d0b4d3b362527a54d7aa12d30603019d7309b3b` |
| [#74](https://github.com/wieslawsoltes/netDxf/pull/74) | Combined persistence tests and corrected prior Bezier clone oracle | 24 | 16,531 | [34866391607](https://github.com/wieslawsoltes/netDxf/actions/runs/34866391607) | `fab7bb8d3afbbfc467da6ba05563e00752b625a9` |
| [#75](https://github.com/wieslawsoltes/netDxf/pull/75) | Embedded-object handles remain opaque to generic remapping | 234 | 16,765 | [34867250706](https://github.com/wieslawsoltes/netDxf/actions/runs/34867250706) | `6420dd213b018d6f8698167aca4473bc3d3a499f` |

The starting merged PR #68 suite had **15,592 cases**. These increments add **1,173**, yielding **16,765 passed / zero failed**. PR #74 changes tests/verifiers, not production; its oracle correction reflects the equal Bezier spans already implemented by PR #64. The unchanged PR #68 CI fixture reproduced the stale oracle failure, and no tolerance was widened.

Every listed PR was merged only after its final head passed Linux/Windows Debug/Release, actual `netstandard2.0` builds, ledger integrity and the Linux Debug source audit. Preparation workflows were not treated as test results. Temporary transfer helpers checked the original patch hash and complete expected tree and were absent from each final PR diff. Transfer encoding errors were corrected only under those exact-hash gates, not by modifying tested production semantics.

## Retained source evidence

Downloaded Linux Debug artifacts were independently reconstructed as Git trees before each merge. Complete source equality was checked, not only selected production files.

| PR | Files in retained source | Exact verified source tree |
|---|---:|---|
| #69 | 518 | `1ebfbc69794ae35894a58620eab6756e8f33c775` |
| #70 | 524 | `c06289913f34733ce0bfa767c7e1b76e5fa32ae8` |
| #71 | 528 | `ba101e2757ad665127a1870fb8e705f3613667bd` |
| #72 | 532 | `393df6ce7999f6326a35f101d8cbba495806f029` |
| #73 | 535 | `30f1f35f9f79674b9754bdaf5ed90c4041f1c2de` |
| #74 | 536 | `3efc84005abc0759c535677ea67912961bd65bda` |
| #75 | 539 | `280e1b80946fc2e4133e69ead3bc8651825b13bd` |

The recovered combined implementation was re-executed in this continuation: **16,531 / zero failures**, Debug and Release against a separately compiled strong-named production assembly. After adding the embedded-object fix, both configurations pass **16,765 / zero failures** on .NET 8.0.31. The 386 production/test source files in that final local run exactly match PR #75. Python documentation-integrity tests remain a separate count of 15.

## Newly found embedded-object defect

A group-101 `Embedded Object` separator was ignored by the contextual handle index. Private ordinary-code data could consequently become enclosing pointers, reactors or XData links. The remapper could then rewrite a private handle without its class schema.

The fix treats the recognized embedded tail as opaque through the lexical record boundary, including 100/102/1001-looking private values. The existing affected-opaque guard rejects unsafe changes; unrelated renames and numeric no-ops remain valid. Nonmatching markers, nested application controls and established XRECORD payloads preserve their existing rules.

Identical final new tests on unchanged production report **16,603 passed / 162 failed** in each configuration. All 18 direct unsafe-remap probes demonstrate actual private-slot rewriting before the correction. Corrected results are **16,765 / zero failures**. No historical embedding legality is inferred from raw fixtures in older declared profiles.

See [the feature note](raw-embedded-handle-context.md) for the exact recognition policy, primary-source observations and the limitation concerning class-specific XData after embedded objects. This is not an implementation of MTEXT columns or a complete private-payload schema.

## Independent-reader checks

All **39 `verify_*.py` scripts passed against both final local configurations: 78 successful verification processes**. Some compare raw ordered tags; others load semantic objects and run ezdxf AUDIT. These are not 78 native AutoCAD tests, nor 78 universal semantic-validation certificates.

Selected checks were also repeated on downloaded final-head CI fixtures before merging: SPLINE reversal (36 drawings / 72 splines / 1,188 curve comparisons), atomic output (24 drawings), raw remapping (12 drawings), boundary-count compatibility (12 drawings), and the corrected clone oracle (36 drawings / 72 splines). Their semantic verifiers report zero audit errors and zero repairs.

The new embedded verifier uses ezdxf 1.4.4's independent text/binary tag decoders to check **four drawings / 48 exact ordered embedded tags**, enclosing references and isolated following-POINT renaming. These private-payload fixtures are intentionally **tag-level** evidence; no semantic AUDIT or AutoCAD-valid entity claim is made. An initial CRLF/StringIO verifier harness mistake was corrected using universal-newline input, not altered production output or weakened value assertions.

## Version and operation contract

| Format | Typed `DxfDocument` | Raw `DxfRawDocument` | Scoped additions |
|---|---|---|---|
| AC1009 / R11–R12 | Still rejected | Admitted, legacy binary framing | Raw atomic saving, contextual index, selected traversal/remapping and opaque-tail isolation |
| AC1012 / R13 | Still rejected | Admitted | Same raw operation contract |
| AC1014 / R14 | Still rejected | Admitted | Same raw operation contract |
| AC1015 / 2000 | Partial typed schemas | Admitted | All new typed and raw operations; existing gradient downgrade remains lossy |
| AC1018 / 2004 | Partial typed schemas | Admitted | All new typed and raw operations |
| AC1021 / 2007 | Partial typed schemas | Admitted | Same; existing HELIX starts at the conservative 2007+ writer profile |
| AC1024 / 2010 | Partial typed schemas | Admitted | Same; existing MESH and HATCH fit-output profiles also admitted |
| AC1027 / 2013 | Partial typed schemas | Admitted | Same scoped contracts |
| AC1032 / 2018 | Partial typed schemas | Admitted | Same scoped contracts |

`SaveAtomic` is opt-in destination-byte protection, not a transaction over every document mutation or a power-loss/hostile-filesystem guarantee. Original `Save` remains nontransactional. Typed empty-HATCH input is retained, but zero-boundary output rejects before preprocessing or caller-stream writes. Raw traversal follows selected exposed outgoing links; it does not synthesize BLOCK/POLYLINE aggregates or discover dependencies hidden in private strings or binary values.

The comparison also reconciles already merged PRs #60–#68: optional gradient ACI state, mutable MESH output validation, stored SPLINE cloning, typed comments, Bezier domains, flags, periodic input, typed HELIX and explicit analytic authoring. Their wider entity families remain partial, and no old local-only pending matrix is presented as current merged support.

## Remaining work and qualification

Missing typed families still include MULTILEADER, structured TABLE, LIGHT/SECTION and inert OLE/proxy/ACIS/surface payload models. Generic object families, complete historical typed R12/R13/R14 dialects, MTEXT columns/linked contexts, deeper HATCH geometry and association rules, MESH overrides and context-specific private dependency closure remain unimplemented scopes in the ledger. HELIX, optional ACI retention, the opt-in atomic API and the exposed raw handle operations must no longer be listed as wholly absent.

Native AutoCAD open/AUDIT/save/reopen has **not** been executed. Successful netstandard2.0 compilation is not proof of execution on every older consuming runtime. Broader independent corpora, resource accounting, fuzzing, per-property down-save reporting and native interoperability remain necessary qualification work. No completion percentage is asserted.

```sh
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
dotnet build netDxf/netDxf.csproj -f netstandard2.0 -c Release
python tools/verify_raw_embedded_handles.py artifacts/conformance
```
