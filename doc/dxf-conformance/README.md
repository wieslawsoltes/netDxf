# DXF conformance work

Start with the [current version and feature comparison](version-feature-matrix.md), with typed editing distinguished from raw preservation and each format family shown separately. The machine-readable source is [coverage.json](coverage.json). It includes evidence links, a pinned production commit/tree, explicit missing/lossy/rejected states, and the remaining implementation sequence.

The [editing, retained-record and opaque-entity checkpoint](checkpoint-editing-opaque-2026-09-15.md) records PR #94: 33,296 passing cases and 121 independent gates in each local configuration, all four Linux/Windows implementation CI jobs, all five Release library targets, and 291 scoped rows. It adds [TABLEGEOMETRY replacement](table-geometry-editing.md), [SECTION_MANAGER creation and erasure](section-manager-lifecycle.md), [retained Polyface records](polyface-records.md), [explicit HATCH pattern affine transforms](hatch-pattern-affine.md), [TABLECONTENT edits](table-content-editing.md), [standalone opaque entities](opaque-entities.md), and the [tenth mixed graph](tenth-mixed.md). Qualified opaque HATCH backlink release, collection insertion and retained child removal guards have preserved before/after evidence. The integration receipt pins source and CI artifacts; module notes distinguish native packets from declared schema carriers and keep evaluation, regeneration and unknown private dependencies outside their claims.

The preceding [map, mesh and conic checkpoint](checkpoint-map-mesh-conic-2026-09-15.md) records PR #93: 30,772 passing cases and 112 independent gates in each local configuration, all four platform CI jobs, all five Release library targets, and 284 scoped rows. It adds stored CELLSTYLEMAP access, explicit SECTION_MANAGER membership editing, retained PolygonMesh records, affine HATCH conics and bounded target-version diagnostics. The integration receipt pins exact source and verified CI artifacts. The CELLSTYLEMAP getter return-type change requires consumers to recompile; its migration is documented.

The [stored reference and editing checkpoint](checkpoint-stored-editing-2026-09-15.md) records PR #92: 29,293 passing cases and 106 independent gates in each local configuration, all four platform CI jobs, all five Release library targets, and 278 scoped rows. SUNSTUDY/TABLEGEOMETRY storage, VIEW live-section references, explicit retained Polyline3D topology and direct HATCH transforms have separate source and lifecycle contracts. The integration receipt pins the verified source archive and distinguishes storage from evaluation.

The [POLYFACE and PolygonMesh integrity checkpoint](checkpoint-polyline-integrity-2026-09-15.md) records PR #91: 28,627 passing cases and 100 independent gates in each local configuration, all five Release library targets, and 272 scoped rows. All four implementation CI jobs passed; Linux Release also passed all 100 independent gates. Signed face topology, independent clones and exact polygon-grid cardinality have explicit evidence; arbitrary child metadata and native smooth-surface regeneration remain outside this increment.

The merged [SECTION_MANAGER, HATCH and source identity checkpoint](checkpoint-section-hatch-2026-09-15.md) records PR #90: 27,555 passing cases, 98 independent gates per configuration and 272 scoped rows. The [association and child-record recovery checkpoint](checkpoint-recovery-2026-09-15.md) records PR #89: 26,980 passing cases, 96 independent gates in each local configuration, all five Release library targets, and a 270-row scoped comparison. It follows the [SECTION, style, field and sun checkpoint](checkpoint-section-storage-2026-09-15.md) for PR #88 and the [TABLE, DATATABLE and index checkpoint](checkpoint-table-storage-2026-09-15.md). Stored schemas, typed lifecycle operations, evaluation and native application qualification remain separate claims.

`DxfDocument` admits the six 2000–2018 format families. `DxfRawDocument` additionally admits R11/R12, R13 and R14 for ordered preservation and scoped immutable edits. Unknown raw records surviving is not evidence that the typed model can edit or evaluate them. No completeness percentage is inferred from test count.

The [12 September source-pinned 113-row snapshot](version-feature-matrix-2026-09-12.md) is retained as history; its missing raw/VIEW/CLASSES/UCS entries are no longer the current state. Subsequent feature notes record their own baselines, red/green tests, version contracts and remaining boundaries.

The current major modules cover [typed named-object graphs](named-object-database.md), [raw OBJECTS transactions](raw-object-transactions.md), [MTEXT columns](mtext-columns.md), [VPORT configurations](vport-records.md), and [LWPOLYLINE packet/reversal integrity](lwpolyline-integrity.md). Each note defines its admitted storage profiles, mutation rules, and qualification limits. Mixed database tests exercise references among duplicate-name VPORT records, MTEXT groups, extension dictionaries, XRECORDs, XDATA, and reactors.

Additional integrated modules add [optional LWPOLYLINE widths and vertex identifiers](lwpolyline-fidelity.md), [common graphical metadata and opaque proxy caches](common-entity-data.md), [typed pointer buffers, draw order and spatial filters](typed-containers.md), [VIEW/VPORT UCS relationships](view-ucs-relationships.md), and [public GEODATA coordinate metadata](geodata.md). Version gates and independent producer fixtures are documented per module. These schemas store and edit data; geographic transformations, clipping evaluation and proxy rendering are separate capabilities.

The next integrated batch adds [stored MULTILEADER contexts and styles](multileader-contexts.md), [inert ACIS SAT envelopes](acis-sat.md), [STYLE font fidelity](text-style-fidelity.md), [DIMSTYLE settings and overrides](dimstyle-stored-settings.md), [standalone and embedded output settings](output-settings.md), and [attribute empty-value invariants](attribute-default-values.md). Eighteen mixed scenarios exercise shared resources and explicit cross-document mappings across the qualified profiles.

The [parallel module checkpoint](checkpoint-modules-2026-09-14.md) records the PR #83, #84 and #85 increments, exact implementation source, the combined 22,047-case suite, independent verification and remaining qualification limits.

Recent implementation evidence: [SPLINE reversal](spline-knot-reversal.md), [atomic saves](atomic-file-save.md), [raw handle indexing](raw-handle-index.md), [guarded remapping](raw-handle-operations.md), [empty HATCH retention](empty-hatch-retention.md), and [embedded-object safety](raw-embedded-handle-context.md). The comparison also reconciles [ACI metadata](hatch-gradient-aci.md), [MESH output validation](mesh-write-validation.md), [stored SPLINE clones](spline-clone-state.md), [Bezier domains](bezier-knot-parameterization.md), [periodic input](spline-periodic-input.md), [typed HELIX](helix.md) and [analytic authoring](helix-authoring.md).

The [merged 14 September checkpoint](checkpoint-merged-2026-09-14.md) records PRs #69–#75, 1,173 additional cases since PR #68, the 16,765-case production suite and exact final-head CI/source checks. The current comparison is regenerated from the ledger; this older checkpoint is not the current feature count. The [earlier 14 September checkpoint](checkpoint-2026-09-14.md), [13 September HATCH checkpoint](checkpoint-hatch-2026-09-13.md), [earlier checkpoint](checkpoint-2026-09-13.md) and [PR #50 HATCH audit](hatch-remaining-audit.md) remain historical, not current missing-feature lists.

## Updating the comparison

Edit `coverage.json`, then regenerate. Python 3.10+ is required; CI selects Python 3.12 and checks the ledger and its generated output on Linux and Windows.

```sh
python tools/generate_dxf_coverage.py
python tools/generate_dxf_coverage.py --check
python -m unittest discover -s tests/dxf_coverage -p 'test_*.py'
```

The validator checks schema/version columns, unique IDs, evidence paths, typed admission, and generated-output freshness. It does **not** prove implementation correctness or historical schema legality. Changes to support status must be backed by production changes and regression evidence, not just ledger edits.

## Source and regression evidence

The [Roslyn field-audit tool](../../tools/netDxf.FieldAudit/README.md) regenerates syntax evidence in CI. Linux Debug artifacts contain `field-audit/field-inventory.json`, `field-audit/field-inventory.md`, regression reports, generated DXF fixtures and an exact source archive. The Python `tools/verify_*.py` scripts provide independent ezdxf checks. Install `tools/requirements-independent.txt`, then run `python tools/run_independent_verifiers.py artifacts/conformance`. Linux Release CI runs every verifier and retains individual logs plus `artifacts/independent/results.json`; failed checks, timeouts and missing fixtures fail the job. The dependency is development-only and is not loaded by the library.

Feature changes are grouped into reviewable PRs with applicable version/transport fixtures, explicit downgrade/preservation semantics, and verified final-head CI before merge. Opaque preservation, semantic editing, geometric/rendering evaluation, and native AutoCAD validation remain separate claims. Full standard capability is still an open goal at the pinned snapshot.

Stored [UCS-record orthographic base references](ucs-record-base.md) preserve the distinct79/346 relationship, actual target identities and explicit-null presence. Independent producer packets qualify storage and lifecycle behavior; the published schema contradiction and lack of positive native packets remain documented.

Loaded [ordinary 3D POLYLINE child records](polyline3d-records.md) retain VERTEX and SEQEND identities, optional packets, common metadata and exact native association references. The focused module has 382 Debug/Release cases and 90 independently checked outputs, including clone/removal guards and parent-reactor moves. It remains source/profile bound; fitted and legacy 2D records and full metadata-graph cloning remain separate work. Explicit [retained topology operations](polyline3d-topology.md) now support insertion, removal and movement under their documented guards.

The [stored section manager](section-manager.md) retains the stored update flag,
ordered section pointers and exact source identities with root-anchor and lifecycle
guards. One unchanged R2018 native drawing and explicit R2007+ schema cases qualify
storage. [Explicit membership replacement](section-manager-membership.md) edits its
ordered list and flag atomically. [Database creation and guarded erasure](section-manager-lifecycle.md)
now manage explicit manager lifecycles while preserving member identities; live sectioning,
section generation and automatic membership maintenance remain separate.

[POLYFACE face grammar and clone isolation](polyface-grammar.md) qualify signed indices,
zero termination, advisory counts and independent face lifecycles across both
transports. The newer [retained-record subset](polyface-records.md) adds source identities,
physical record order and qualified common metadata with fixed-topology edits. Private
metadata graphs and unrestricted topology changes remain outside that scope.
