# Remaining major DXF / AutoCAD parity gaps

**Review date:** 25 September 2026. **Reviewed default-branch baseline:**
`9e4eb348b607f3fe3d50f5f469a382750befeba7`, source tree
`84b11cc47e0adbccae86ea13219be871effc89e4` (merged PR #212).

This is the maintained major-gap report, not a new conformance certificate.
It reviews the baseline, recent PR boundaries, selected production paths and
source-bound contracts. It is not a claim that every line or every native
AutoCAD feature was independently audited. A capability can contain substantial
implemented and tested subsets while remaining incomplete as a whole.

## Executive assessment

The library has progressed well beyond a basic entity reader/writer. It has
ordered raw preservation, typed object graphs, guarded identity operations,
retained child records, bounded FIELD and TABLE operations, extensive geometry
work and independent output verification. The remaining gap is not simply a
list of unrecognized entity names. Much of it lies between **storing data,
editing it safely, evaluating dependencies, regenerating all affected state,
and proving native application interoperability**.

Full AutoCAD parity is **not established**. The highest-value next work is:

1. Close current source/CI discrepancies and establish native acceptance evidence.
2. Complete dependency-aware import/version-conversion and coordinated regeneration.
3. Broaden semantic evaluation, historical typed dialects and rendering fidelity.

These are priorities, not assertions that all their subfeatures are missing.
No percentage is computed from test totals, file counts or coverage rows.

### Evidence that must not be conflated

| Evidence | What it establishes | What it does not establish |
|---|---|---|
| Source implementation and unit tests | The tested API behavior and failure policy | Universal DXF legality or native appearance |
| A same-library save/load cycle | Internal consistency for the selected input | Agreement with another producer |
| Physical packet comparison and independent-reader audit | The stated wire, ownership and interpretation checks | Native AutoCAD evaluation or rendering |
| Loaded-package/runtime receipt | The tested assembly and scenarios on that runtime | Full conformance on every compatible CLR |
| Native open, AUDIT, regeneration, save and reopen | The observed application/build/profile behavior | All other releases, fonts, object enablers or files |

The historical [coverage ledger](coverage.json) is pinned to 15 September 2026,
PR #95, rather than the current default branch. Its [generated matrix](version-feature-matrix.md)
remains useful evidence for that snapshot. Do not silently change its source pin
or promote its broad rows solely because later PRs added passing tests. Later
contracts and PR receipts carry their own source identities.

## Recent work: closed versus still pending

Merged #209 preserves signed/tiny/zero/Z UNDERLAY scales. Merged #210 adds
portable decimal-to-binary64 conversion. Merged #211 adds INSERT affine staging,
attribute atomicity and registered sequence terminators. Merged #212 adds explicit
3D-polyline coordinate editing and parent-graphics invalidation. Those are real
increments, not completion of their entire entity or dependency families.

At this review, #213 is the separate legacy-mesh editing/density continuation;
#214 is the separate 2D-polyline and position-batch continuation. Their draft
source must not be described as part of the baseline above. Qualification must
use each current head and its actual exported evidence, not a stale PR body,
job summary, earlier local result or mismatched log. No assertions should be
relaxed merely to turn a pending run green.

The accompanying modern-MESH header fix is separate again: changing
`BlendCrease` or `SubdivisionLevel` invalidates stale common graphics; exact
no-ops preserve them. Its source and tests are in PR #215. This does not add
subdivision evaluation, native mesh rendering or observation of direct edits
to vertex/face/edge collections. The new conformance tests and independent
checker must be executed before that candidate is considered qualified.

## Priority map

P0 means an evidence or data-integrity prerequisite. P1 means a major
interoperability capability. P2 means a broader engine/product capability.
These labels order work; they are not severity scores for every member of a family.

| ID | Priority | Remaining scope | Classification |
|---|---|---|---|
| G01 | P0 | Native AutoCAD acceptance with reproducible producer/build/profile evidence | Qualification gap |
| G02 | P1 | Typed R11/R12, R13, R14 and earlier dialect handling | Missing typed scope; raw subsets exist |
| G03 | P1 | General, explicit, dependency-aware version conversion | Partial diagnostics and selected conversions |
| G04 | P0/P1 | Dependency-complete clone/import and graph-wide atomic publication | Guarded subsets; conservative refusals remain |
| G05 | P1 | Coordinated TABLE value/style/layout/display/private-cache regeneration | Substantial components; incomplete orchestration |
| G06 | P1 | Broader native FIELD providers, expressions and dependency contexts | Bounded explicit evaluator subsets |
| G07 | P1/P2 | Dynamic blocks, annotation contexts and associative regeneration | Storage and narrow operations are not full engines |
| G08 | P1 | Native text, dimensions, leaders, viewports and plot appearance | Stored settings and helpers; incomplete visual qualification |
| G09 | P1/P2 | Geometry reconstruction, subdivision and arbitrary application payload semantics | Selected algorithms/preservation; broader engines incomplete |
| G10 | P1 | Comprehensive mutation notification and cache/dependency coherence | Explicit setters cover subsets; mutable escape hatches remain |
| G11 | P0/P1 | Fuzz/resource/platform/performance qualification at production scale | Existing bounds and tests are not exhaustive qualification |

## G01 — Native application acceptance

The existing C# and independent-reader suites are valuable, but they do not run
AutoCAD's evaluators, regeneration machinery, modeler or font/layout engine.
A native-authored fixture is also not evidence that an edited output was opened,
audited and saved successfully by the native application.

**Next implementation:** build an external, explicitly provisioned acceptance
harness for available licensed installations. Record product/build, host OS,
DXF profile, loaded enablers, fonts, locale, source hash, command sequence and
output hashes. Keep this optional native infrastructure separate from the two
ordinary repository workflows; do not pretend an unavailable native runner ran.

**Acceptance:** open the untouched producer input and the edited output; run
AUDIT with repair behavior recorded; force relevant regeneration/evaluation;
save and reopen; compare required geometry, ownership, fields, optional values
and appearance. Record repairs as failures or explicitly reviewed compatibility
outcomes, not invisible normalization. Test a second save to expose unstable
identities or regenerated data. A clean independent-reader audit alone cannot
close this item.

## G02 — Historical typed dialects

`DxfDocument` supports the six 2000–2018 format families. `DxfRawDocument`
additionally admits R11/R12, R13 and R14 for ordered preservation and selected
edits. Those paths are not interchangeable. Raw retention does not imply that
all historical entities can be constructed, edited and written through the
modern typed model. Earlier/headerless dialects also need explicit policies.

**Next implementation:** introduce or extend profile-specific codecs with
explicit header, encoding, binary framing, section, subclass, entity and object
rules. Start with a bounded historical typed subset rather than merely widening
a version-enum check. Preserve unknown packets where promised and reject
unsupported typed edits before publishing partial output.

**Acceptance:** independent native inputs and newly authored outputs for each
claimed profile, in both applicable transports, including missing optional
fields, legacy code pages, child sequences and negative version-legality cases.
Test raw and typed paths separately. Autodesk's `$ACADVER` values identify format
families; accepting a version code is not an all-feature certificate for a
particular AutoCAD product year. [Source: ledger](coverage.json);
[primary version reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm).

## G03 — General version conversion

Per-feature admission checks, selected geometric conversions and bounded target
compatibility diagnostics already exist. They do not constitute a general
converter that transforms an arbitrary source document, every dependency and
all private caches into another valid format family.

**Next implementation:** a conversion plan that inventories source features and
their dependency closure, classifies each as preserve/convert/drop/reject, names
all intentional losses, prepares target objects and only then publishes. The
policy must distinguish a lossless request from an explicitly approved flattening
request. Changing `$ACADVER`, deleting unsupported tags or approximating geometry
without a report is not an adequate conversion.

**Acceptance:** source remains unchanged on rejection; unsupported data cannot
silently disappear; target references resolve; converted geometry and optional
metadata match the declared policy; a complete target-native open/save cycle
passes. Test interactions, not only isolated entities: TABLE/FIELD/display
blocks, named objects, attributes, retained sequences and external references.

## G04 — Dependency-complete import, cloning and atomicity

Named-object graphs, source identities, raw object transactions, explicit mappings
and protected removals are implemented in documented subsets. Retained objects
and referenced private records may deliberately refuse cloning or cross-document
adoption. Such refusal can be the correct safety behavior; replacing it with a
shallow copy is not progress toward safe parity.

**Next implementation:** a two-phase dependency-closure planner for supported
schemas. Classify hard ownership, hard/soft pointers, reactors, dictionary slots,
resource names and embedded references by context. Resolve name collisions and
handle allocation before publication. Preserve aliases, shared resources and
cycles where legal. Unknown pointer-bearing payloads need an explicit strategy,
not a search-and-replace of all handle-looking strings.

**Acceptance:** exact shared-versus-independent identity expectations, retained
child/terminator metadata, source immutability, deterministic collision behavior,
no dangling pointers, reference-safe erasure and failure before publication.
Fault injection must reach the last dependency and callback. Entity-local
staging is not a promise to undo caller callbacks, concurrent mutation, process
termination or an entire filesystem transaction. See [named-object database](named-object-database.md),
[raw transactions](raw-object-transactions.md), [retained 2D records](polyline2d-records.md)
and [INSERT sequences](insert-sequences.md).

## G05 — TABLE orchestration and complete regeneration

This is a large remaining capability, but it is inaccurate to call TABLE support
absent. Implemented pieces include addressed grids, bounded formulas, stored
scalar updates, explicit style cascades, measured layouts, generated display
blocks and source display selection. Their publication boundaries are deliberately
separate. `ApplyFormulaResults`, `BuildDisplayBlock` and `ReplaceDisplayBlock`
are not synonyms for regenerating every native TABLE representation.

**Remaining work:** complete interpretation of all claimed inline/private schemas;
automatic native style precedence and duplicated-style coherence; synchronized
inline/backing values, field results, TABLEGEOMETRY and display data; block and
rotated content; advanced flow, breaks and layout modes; and native font-dependent
measurement. A native application may regenerate an explicitly selected display
from other retained table data, exposing inconsistencies that a static viewer misses.

**Next implementation:** a regeneration plan that resolves the addressed content,
styles, FIELD dependencies and measurement inputs, builds every supported
candidate representation, checks reference/resource budgets and publishes them
together. Unknown cache layouts must cause explicit refusal or an accurately
scoped result, not fabricated compatible-looking bytes.

**Acceptance:** edits to a cell, formula, merged region, style and linked content
remain consistent across all relevant representations after two saves and native
regeneration. Failed late calculations or missing fonts/resources cannot leave
half-updated state. See [calculation and layout](table-calculation-layout.md),
[display binding](table-display-binding.md), [stored content editing](table-content-editing.md)
and [cell formatting](cell-style-format-editing.md).

## G06 — FIELD semantics beyond the bounded evaluator

Persistent FIELD results, ownership forests, failure outcomes, selected text-host
updates and an explicit standard evaluator already exist. The documented evaluator
supports `_text`, `AcVar` and `AcExpr`, explicit variable bindings, owned child
slots, numeric expressions, date masks and angular formatting. It is therefore
wrong to list all FIELD evaluation, date formatting or angle formatting as missing.

**Remaining work:** broader native evaluator/provider coverage, object-property
lookups and dependencies, system/context bindings, expression options, external
sources, native error/fallback semantics and coordinated propagation to additional
hosts and display/layout caches. Explicit supplied time/variables are different
from automatic access to a drawing, filesystem, sheet set or environment.
The current host operation replaces selected complete literal values; it is not
arbitrary substring FIELD expansion or MTEXT column reflow.

**Next implementation:** provider interfaces with explicit dependency discovery,
context and side-effect policies; extend the supported expression grammar with
independent native examples; integrate invalidation/re-evaluation with existing
forest transactions. Never execute arbitrary text as a provider side effect.

**Acceptance:** native input/output examples per provider and context; cycle,
missing-property and stale-snapshot cases; exact failure-cache and host-update
policies; unchanged unrelated trees and resources. Existing date/unit/culture
contracts stay explicit until native equivalence is demonstrated.
See [standard evaluation](standard-field-evaluation.md), [persistent results](field-results.md)
and [text hosts](field-text-hosts.md). Autodesk separately documents evaluator
contexts and cached failure results in its
[field evaluator API](https://help.autodesk.com/cloudhelp/2027/ENU/OARX-RefGuide/files/OARX-RefGuide-AcFdFieldEvaluator__evaluate_AcDbField__int_AcDbDatabase__AcFdFieldResult_.html).

## G07 — Dynamic, annotative and associative behavior

Retaining blocks, contexts, DIMASSOC/section data or association pointers is not
an implementation of the corresponding native evaluation system. Current INSERT
geometry and atomic attribute transforms are useful foundations, but do not by
themselves evaluate dynamic-block actions, annotation-scale representations,
constraints, recursive display dependencies or live section generation.

**Next implementation:** choose one dependency family and model its state changes,
required contexts and invalidation rules explicitly. Do not silently flatten a
dynamic or annotated object into one current visual representation unless the
caller requests and receives a loss report.

**Acceptance:** parameter/context changes, nested references and dependent labels
regenerate consistently; unsupported actions are identified before mutation;
cloning/importing retains the relevant dependencies; native regeneration does
not unexpectedly replace the edited result. See [INSERT geometry](insert-geometry.md),
[stored containers](typed-containers.md), [MULTILEADER contexts](multileader-contexts.md)
and [section membership](section-manager-membership.md).

## G08 — Text, dimensions, leaders, viewports and plotting

Stored style/settings fidelity, formatting and selected geometry/display helpers
must be distinguished from native visual equivalence. Remaining qualification
includes font substitution and SHX/TrueType metrics, shaping and wrapping, MTEXT
column reflow, fit/alignment, dimension/leader placement, annotation contexts,
viewport clipping and plot-style effects. External IMAGE/PDF/DWF/DGN payloads and
clipping metadata also need real content interpretation to claim display parity.

**Next implementation:** explicit measurement/rendering providers with deterministic
inputs and a native comparison corpus. Keep literal text, native control sequences,
FIELD expansion and formatting distinct. Define missing-font/resource behavior
rather than guessing from string length or silently dropping external content.

**Acceptance:** compare positions, extents, line breaks and visual outputs under
specified fonts, units, locales, scales and plot settings. Database audits cannot
prove those properties. Include native producer fixtures, not only helper-built
examples. See [text style](text-style-fidelity.md), [MTEXT columns](mtext-columns.md),
[output settings](output-settings.md) and [UNDERLAY geometry](underlay-affine.md).

## G09 — Geometry and application-specific engines

Geometry work already covers many primitive, conic, spline, hatch, INSERT and
legacy-mesh operations. Remaining scopes include complete native reconstruction
and rendering, subdivision/crease evaluation, manifold/degeneracy interpretation,
all fitted/smoothed record families, robust application-specific modeler data and
arbitrary private entity semantics. Existing numerical rank/tolerance and
representability checks are meaningful contracts, not necessarily bugs to remove.

Modern MESH export preflight validates finite values and index/count consistency;
it expressly does not prove manifoldness, winding, self-intersection or subdivision.
An inert ACIS/SAT envelope is not a modeling kernel. An opaque proxy can preserve
a custom application's payload without knowing its edit or evaluation semantics.

**Next implementation:** identify each proposed operation's representation and
numerical domain, then implement and test that domain without weakening existing
refusal or preservation contracts. Separate improved intermediate arithmetic from
claims about all geometric degeneracies. Application payload support needs an
actual schema/modeler/provider, not invented decoded geometry.

**Acceptance:** independent analytic or native geometry oracles, adversarial
numerical inputs, exact unaffected metadata, explicit approximation bounds,
repeatable transforms and downstream native acceptance. See [MESH preflight](mesh-write-validation.md),
[ACIS envelope](acis-sat.md), [opaque entities](opaque-entities.md) and Autodesk's
[proxy-object definition](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-DevGuide/files/GUID-11AEF76F-9638-4322-A7D0-0D628BBB456F.htm).

## G10 — Mutation notification and cache coherence

Recent PRs close specific stale-graphics defects and add validated editing APIs.
They do not make every exposed mutable list, array, vertex, edge, brush, pattern
or referenced resource observable. A cache can also depend on another object,
not only the entity whose setter was called. Common graphics, child-record
payloads, display blocks and private evaluation caches are different stores.

**Next implementation:** maintain a dependency/invalidation inventory; prefer
validated operations that stage candidates, preserve identity and publish only
after validation. Provide a deliberate migration strategy before changing public
collection types. Where compatibility retains raw mutation, document the caller's
responsibility and provide an explicit refresh/invalidation path.

**Acceptance:** changed/no-op/refused operations with absent, empty and nonempty
caches; alias and shared-resource cases; clone independence; reader hydration;
late failures; cross-format saves and unchanged unselected records. Do not seed
a fixture's proxy before a setup edit and then mistake intentional invalidation
for an operation-under-test regression. Preserve every original operation assertion
when correcting such setup. The modern-MESH header patch addresses two setters,
not this entire inventory. See [3D-polyline edits](polyline3d-edits.md) and
[common entity data](common-entity-data.md).

## G11 — Qualification, resource behavior and platform coverage

The consolidated pipeline builds five library targets and exercises selected
installed assets/runtimes. Full conformance and installed-package smoke tests
have different scopes. A successful build of a legacy target is not execution
on that CLR; an updated Windows Framework runner is not every historical CLR
installation. Cross-platform numerical, encoding, filesystem and culture behavior
needs evidence tied to the actual assembly and environment.

Bounds and malformed-input tests exist, but should not be described as exhaustive
fuzzing, concurrency, denial-of-service or production-scale performance certification.
Large aliased arrays, deeply nested dependencies, long tokens, composite payload
budgets and interrupted file operations need operation-specific measurements and
failure checks. `SaveAtomic` and entity-local staging do not automatically turn
all save/import/callback operations into database transactions.

**Next implementation:** deterministic mutation/fuzz corpora, allocation/time
measurements, fault injection and broadened environment matrices. Keep complete
prior test identities, exact fixture inventories, corruption controls and package
source binding. Do not increase a timeout or normalize an identity solely to hide
a regression. Report unknown/unexecuted outcomes honestly.

**Acceptance:** reproducible source/build/toolchain/fixture hashes, actual result
arrays, unchanged baseline coverage, no unreviewed repairs or waivers, and explicit
runtime/performance scope. See [CI and release contract](../CI-RELEASE.md),
[pipeline implementation](../../tools/ci/pipeline.py) and [atomic save](atomic-file-save.md).

## Implementation sequence and completion criteria

First qualify the pending narrow PRs against their actual heads. Next create the
native acceptance harness and use its first failures to prioritize graph closure,
TABLE orchestration and FIELD/provider work. Expand historical typed profiles one
claimed subset at a time. Broaden rendering/modeler/annotation engines only with
appropriate providers and acceptance fixtures. Continue isolated cache/geometry
fixes in parallel, but do not let them replace the major workstreams above.

A gap closes only when the API and schema contract are implemented, applicable
versions/transports and dependency interactions are tested, preservation and
refusal behavior are checked, independent output tests pass, relevant package
runtimes are exercised, and native evidence exists where native equivalence is
the claim. Release readiness also requires the final reviewed tree and actual
artifacts to agree. No aggregate row or case count substitutes for that chain.

The JavaScript port in #98 remains a separate project with its own fixed-reference,
original-test and differential gates. Neither this review nor a green C# PR
waives those requirements.

## Documentation and workflow cleanup policy

The root README should explain installation, usage, support boundaries and where
to find evidence. The conformance README should be an index, not an accumulating
copy of every PR narrative. This report is the maintained major-gap entry point;
feature contracts, fixtures, qualification data, upstream notes and licenses remain.

Three superseded narrative checkpoints (PR #41, #50 and #58) are retired from the
current tree because their nearest-work lists describe many features since
implemented. Their exact original text and evidence remain available at the
reviewed baseline:

- [13 September / PR #41](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-2026-09-13.md).
- [13 September HATCH / PR #50](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-hatch-2026-09-13.md).
- [14 September / PR #58](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-2026-09-14.md).

This is targeted cleanup, not an assertion that every remaining document is
redundant or has been exhaustively audited. Source-pinned ledger/qualification
records are not rewritten as current evidence. Live references to retired files
must be migrated to their immutable archive URLs, or the file must remain.

The reviewed default branch already contains only `ci-build.yml` and
`release.yml` in `.github/workflows`. Neither should be deleted or weakened.
No active workflow-file deletion is needed. The source-only hygiene checker runs
through the existing verifier discovery and guards that two-workflow policy,
front-door size/link integrity and retired-document references. It is not an
additional CAD conformance case. No new publication workflow, credentials,
release tags or registry publication are part of this cleanup.
