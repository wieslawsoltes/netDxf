# DXF conformance work

Start with the [current version and feature comparison](version-feature-matrix.md), with typed editing distinguished from raw preservation and each format family shown separately. The machine-readable source is [coverage.json](coverage.json). It includes evidence links, a pinned production commit/tree, explicit missing/lossy/rejected states, and the remaining implementation sequence.

`DxfDocument` admits the six 2000–2018 format families. `DxfRawDocument` additionally admits R11/R12, R13 and R14 for ordered preservation and scoped immutable edits. Unknown raw records surviving is not evidence that the typed model can edit or evaluate them. No completeness percentage is inferred from test count.

The [12 September source-pinned 113-row snapshot](version-feature-matrix-2026-09-12.md) is retained as history; its missing raw/VIEW/CLASSES/UCS entries are no longer the current state. Subsequent feature notes record their own baselines, red/green tests, version contracts and remaining boundaries.

Recent implementation evidence: [spline fit metadata](hatch-spline-fit-data.md), [outer boundary counts](hatch-path-count-validation.md), [gradient packets](hatch-gradient-packets.md), [gradient color state](hatch-gradient-color-state.md), [fractional shift](hatch-gradient-shift.md), [gradient rotation](hatch-gradient-angle.md), [SPLINE tangent transforms](spline-tangent-transforms.md), [HATCH edge packets](hatch-edge-packets.md), [pattern ordering](hatch-pattern-order.md), [boundary classification](hatch-boundary-flags.md), [pattern lists](hatch-pattern-lists.md), [sparse polyline bulges](hatch-polyline-input.md), [closure](hatch-polyline-closure.md), [ACAD XData](hatch-xdata-preservation.md), [pixel size](hatch-pixel-size.md), [seed points](hatch-seed-points.md), and [MESH input validation](mesh-read-validation.md). The matrix also links raw preservation, legacy profiles and all earlier increments.

The [14 September checkpoint](checkpoint-2026-09-14.md) records merged PRs #57–#58, 744 added cases, the 12,907-case production suite and exact CI/source checks. The current 173-row comparison also reconciles PRs #51 and #53–#56. The [13 September HATCH checkpoint](checkpoint-hatch-2026-09-13.md), [earlier checkpoint](checkpoint-2026-09-13.md) and [PR #50 HATCH audit](hatch-remaining-audit.md) remain historical, not current missing-feature lists.

## Updating the comparison

Edit `coverage.json`, then regenerate. Python 3.10+ is required; CI selects Python 3.12 and checks the ledger and its generated output on Linux and Windows.

```sh
python tools/generate_dxf_coverage.py
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
```

The validator checks schema/version columns, unique IDs, evidence paths, typed admission, and generated-output freshness. It does **not** prove implementation correctness or historical schema legality. Changes to support status must be backed by production changes and regression evidence, not just ledger edits.

## Source and regression evidence

The [Roslyn field-audit tool](../../tools/netDxf.FieldAudit/README.md) regenerates syntax evidence in CI. Linux Debug artifacts contain `field-audit/field-inventory.json`, `field-audit/field-inventory.md`, regression reports, generated DXF fixtures and an exact source archive. The Python `tools/verify_*.py` scripts provide optional independent ezdxf checks; that dependency is development-only and is not loaded by the library.

Every feature or defect has its own scoped PR, applicable version/transport fixtures, explicit downgrade/preservation semantics, and green final-head CI before merge. Opaque preservation, semantic editing, geometric/rendering evaluation, and native AutoCAD validation remain separate claims. Full standard capability is still an open goal at the pinned snapshot.
