# DXF contracts and qualification

Start with the [remaining major parity gaps](remaining-major-gaps.md) for the
reviewed baseline, priorities and acceptance criteria. This page indexes the
implementation contracts; it is not a running copy of every PR description.

**Full AutoCAD parity is not established.** Ordered preservation, typed editing,
geometric evaluation, coordinated regeneration and native application acceptance
are different claims. A stored object or a passing round trip does not prove
that its native behavior is implemented.

## How to read the evidence

The [coverage ledger](coverage.json) and its [generated comparison](version-feature-matrix.md)
are explicitly source-pinned historical evidence: their recorded audit is
15 September 2026, PR #95. They are not a current count of every implemented
feature. Later feature notes and PR receipts retain their own source identities,
fixtures, execution scope and limitations. Do not promote broad support rows
using only a larger suite count or a newer commit label.

Feature documents may also record earlier implementations. Read their source
pins and subsequent PR corrections before treating a historical limitation or
normalization policy as current. The new major-gap report separates confirmed
merged work from pending draft branches; it does not rewrite old test evidence.

| Claim | Required evidence |
|---|---|
| API behavior | Tests of the actual library, including no-op and rejected edits |
| Wire preservation | Applicable versions/transports, exact records and reference identities |
| Independent interoperability | Actual exported drawings checked by an independent implementation |
| Installed-package execution | The loaded assembly hash and the tested runtime/scenarios |
| Native AutoCAD equivalence | Reproducible native open/AUDIT/regenerate/save/reopen and relevant visual checks |

## Transport, profiles and safe IO

`DxfDocument` admits the six 2000–2018 DXF format families, subject to per-feature
restrictions. `DxfRawDocument` additionally admits R11/R12, R13 and R14 for ordered
preservation and selected edits. Historical raw support is not historical typed
editing or universal version conversion.

Read the [raw handle index](raw-handle-index.md), [guarded handle operations](raw-handle-operations.md),
[embedded-handle context](raw-embedded-handle-context.md), [atomic-save contract](atomic-file-save.md)
and [portable numeric parsing](portable-double-parsing.md). File replacement,
stream ownership, graph publication and caller callbacks have different failure
boundaries. Unknown data preservation does not authorize guessing its semantics.

## Object graphs, resources and retained records

| Area | Contracts |
|---|---|
| Objects and references | [Named-object database](named-object-database.md), [raw OBJECTS transactions](raw-object-transactions.md), [common entity data](common-entity-data.md), [APPID/XData lifecycle](appid-xdata-lifecycle.md) |
| Containers and contexts | [Typed containers](typed-containers.md), [VIEW/UCS relationships](view-ucs-relationships.md), [VPORT records](vport-records.md), [GEODATA](geodata.md) |
| Legacy polylines | [2D retained records](polyline2d-records.md), [3D retained records](polyline3d-records.md), [3D topology](polyline3d-topology.md), [3D explicit editing](polyline3d-edits.md) |
| Legacy meshes | [POLYFACE grammar](polyface-grammar.md), [retained POLYFACE records](polyface-records.md), [polygon cardinality](POLYGONMESH_CARDINALITY.md) |
| INSERT | [Affine geometry and atomic attributes](insert-geometry.md), [registered sequence terminators](insert-sequences.md) |

Source-bound metadata and protected-removal contracts are deliberate. A refused
clone or import is not equivalent to lost data; dependency-complete operations
must preserve aliases, ownership and references rather than bypass those guards.

## FIELD, TABLE and text

| Area | Contracts |
|---|---|
| FIELD evaluation | [Persistent results](field-results.md), [bounded standard evaluator](standard-field-evaluation.md), [atomic literal text hosts](field-text-hosts.md) |
| TABLE operations | [Calculation, styles and measured layout](table-calculation-layout.md), [display selection](table-display-binding.md), [content editing](table-content-editing.md), [geometry editing](table-geometry-editing.md) |
| Stored styles | [TABLESTYLE edits](table-style-editing.md), [CELLSTYLEMAP](cell-style-map.md), [map editing](cell-style-map-editing.md), [format editing](cell-style-format-editing.md) |
| Text and dimensions | [Font/style fidelity](text-style-fidelity.md), [MTEXT columns](mtext-columns.md), [stored DIMSTYLE settings](dimstyle-stored-settings.md), [MULTILEADER contexts](multileader-contexts.md) |

The standard FIELD evaluator already includes explicit variables, arithmetic,
child slots, date masks and angular formatting. Broader providers and native
contexts remain separate. TABLE calculation, layout construction and display
selection are implemented components, not a transaction that regenerates every
inline, backing and private representation. Native font metrics, column reflow
and all visual placement policies require their own providers and evidence.

## Geometry, display payloads and sections

| Area | Contracts |
|---|---|
| Curves | [SPLINE reversal](spline-knot-reversal.md), [periodic input](spline-periodic-input.md), [Bezier domains](bezier-knot-parameterization.md), [HELIX](helix.md), [HELIX authoring](helix-authoring.md) |
| HATCH | [Empty retention](empty-hatch-retention.md), [pattern affine transforms](hatch-pattern-affine.md), [periodic conversion](hatch-periodic-conversion.md), [graphics coherence](hatch-graphics.md) |
| Raster and underlays | [IMAGE affine geometry](image-affine.md), [IMAGE appearance](image-appearance.md), [UNDERLAY affine geometry](underlay-affine.md), [UNDERLAY scales](underlay-scales.md), [WIPEOUT geometry](wipeout-affine.md) |
| Modern MESH and private data | [MESH preflight](mesh-write-validation.md), [field framing](mesh-field-framing.md), [inert ACIS envelopes](acis-sat.md), [opaque entities](opaque-entities.md) |
| Sections and output | [Stored section manager](section-manager.md), [membership](section-manager-membership.md), [lifecycle](section-manager-lifecycle.md), [output settings](output-settings.md) |

Finite/rank/tolerance admission and approximation limits are part of each
geometry contract. Cache invalidation does not generate replacement graphics.
Preserving an external file reference or a proxy payload does not decode or render
that content. Direct mutable-list/array edits can bypass notifications; use the
validated operations where available and follow the documented caller obligations.

## Reproduce checks

The [CI and release guide](../CI-RELEASE.md) defines the build, full-conformance,
independent-output and installed-package matrices. Keep the two core workflows
and their source-binding/publication guards intact. Feature verifiers are
discovered by the existing runner, not by adding one workflow per feature.

```sh
python -m pip install -r tools/requirements-independent.txt
dotnet run --project tests/netDxf.Conformance -c Debug
dotnet run --project tests/netDxf.Conformance -c Release
python tools/run_independent_verifiers.py artifacts/conformance
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
```

The new `tools/verify_repository_hygiene.py` is a source-maintenance check, not
an additional CAD behavior test. It checks the two-workflow policy, compact
entry-point documentation, existing inline links and retired-checkpoint
references. Its execution status belongs in the cleanup PR's actual evidence.

Build results, previous local runs, constructed checker self-tests and current
hosted results must be labeled separately. Qualify the final reviewed tree.
Keep failed results and corruption controls; do not normalize an unstable
identity merely to make an output comparison pass.

## Historical narratives

The [previous expanded index](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/README.md)
retains the original chronological navigation. Two superseded narrative
checkpoints are retired from the current tree and remain unchanged in Git history:

- [PR #41 / 13 September](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-2026-09-13.md).
- [PR #50 / HATCH](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-hatch-2026-09-13.md).

The [PR #58 / 14 September checkpoint](checkpoint-2026-09-14.md) remains
unchanged in the current tree because the source-pinned coverage ledger and
HATCH audit reference it. Its historical missing-feature list is not the current
project status; retaining it preserves the evidence chain rather than duplicating
the maintained major-gap report.

Feature contracts, native fixtures, qualification records, the historical
ledger, upstream notes and licensing material remain. The JavaScript port in
PR #98 has separate fixed-reference and differential gates; C# results do not
waive them.
