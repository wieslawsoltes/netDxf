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

The library has progressed beyond a basic entity reader/writer. It has ordered
raw preservation, typed object graphs, guarded identity operations, retained
child records, bounded FIELD and TABLE operations, extensive geometry work and
independent output verification. The remaining gap is not simply a list of
unrecognized entity names. Much of it lies between **storing data, editing it
safely, evaluating dependencies, regenerating all affected state, and proving
native application interoperability**.

Full AutoCAD parity is **not established**. The highest-value next work is to
close current source/CI discrepancies and establish native acceptance evidence;
complete dependency-aware import/version-conversion and coordinated regeneration;
and broaden semantic evaluation, historical typed dialects and rendering fidelity.
These are priorities, not assertions that all their subfeatures are missing.
No percentage is computed from test totals, file counts or coverage rows.

### Evidence that must not be conflated

| Evidence | What it establishes | What it does not establish |
|---|---|---|
| Source implementation and unit tests | The tested API behavior and failure policy | Universal DXF legality or native appearance |
| A same-library save/load cycle | Internal consistency for the selected input | Agreement with another producer |
| Physical packets and independent-reader audit | The stated wire, ownership and interpretation checks | Native evaluation or rendering |
| Loaded-package/runtime receipt | The tested assembly and scenarios on that runtime | Full conformance on every compatible CLR |
| Native open, AUDIT, regeneration, save and reopen | The observed application/build/profile behavior | All other releases, fonts, enablers or files |

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

The modern-MESH header fix in #215 is separate again: changing `BlendCrease`
or `SubdivisionLevel` invalidates stale common graphics; exact no-ops preserve
them. This does not add subdivision evaluation, native mesh rendering or
observation of direct edits to vertex/face/edge collections. Its conformance
tests and independent checker require execution before qualification is claimed.

## Priority map

P0 means an evidence or data-integrity prerequisite. P1 means a major
interoperability capability. P2 means a broader engine/product capability.
These labels order work; they are not severity scores for every family member.

| ID | Priority | Remaining scope | Classification |
|---|---|---|---|
| G01 | P0 | Native AutoCAD acceptance with reproducible build/profile evidence | Qualification gap |
| G02 | P1 | Typed R11/R12, R13, R14 and earlier dialect handling | Missing typed scope; raw subsets exist |
| G03 | P1 | General, explicit, dependency-aware version conversion | Partial diagnostics and selected conversions |
| G04 | P0/P1 | Dependency-complete clone/import and graph-wide publication | Guarded subsets; conservative refusals remain |
| G05 | P1 | Coordinated TABLE value/style/layout/display/cache regeneration | Components exist; orchestration is incomplete |
| G06 | P1 | Broader native FIELD providers, expressions and contexts | Bounded explicit evaluator subsets |
| G07 | P1/P2 | Dynamic blocks, annotation contexts and associative regeneration | Storage is not a full evaluation engine |
| G08 | P1 | Native text, dimensions, leaders, viewports and plot appearance | Helpers exist; visual qualification is incomplete |
| G09 | P1/P2 | Reconstruction, subdivision and application payload semantics | Selected algorithms and preservation |
| G10 | P1 | Mutation notification and cache/dependency coherence | Explicit setters cover subsets; mutable access remains |
| G11 | P0/P1 | Fuzz/resource/platform/performance qualification at scale | Existing bounds are not exhaustive qualification |

## G01 — Native application acceptance

The C# and independent-reader suites do not run AutoCAD's evaluators,
regeneration machinery, modeler or font/layout engine. A native-authored input
fixture is also not evidence that an edited output was opened, audited and saved
successfully by the native application.

**Next implementation:** an external, explicitly provisioned acceptance harness
for available licensed installations. Record product/build, host OS, DXF profile,
loaded enablers, fonts, locale, source hash, command sequence and output hashes.
Keep optional native infrastructure separate from the two ordinary repository
workflows; do not claim an unavailable native runner executed.

**Acceptance:** open untouched producer input and edited output; run AUDIT with
repair behavior recorded; force relevant regeneration/evaluation; save and reopen;
compare required geometry, ownership, fields, optional values and appearance.
Record repairs as failures or explicitly reviewed compatibility outcomes, not
invisible normalization. A second save must expose unstable identities or
regenerated data. A clean independent-reader audit alone cannot close this item.

## G02 — Historical typed dialects

`DxfDocument` supports six 2000–2018 format families. `DxfRawDocument` additionally
admits R11/R12, R13 and R14 for ordered preservation and selected edits. These
paths are not interchangeable. Raw retention does not imply that every historical
entity can be constructed, edited and written through the modern typed model.
Earlier/headerless dialects also need explicit policies.

**Next implementation:** profile-specific codecs with explicit header, encoding,
binary framing, section, subclass, entity and object rules. Start with a bounded
historical typed subset rather than merely widening a version-enum check.
Preserve unknown packets where promised and reject unsupported typed edits before
publishing partial output.

**Acceptance:** independent native inputs and newly authored outputs for each
claimed profile, in both applicable transports, including missing optional
fields, legacy code pages, child sequences and negative version-legality cases.
Test raw and typed paths separately. `$ACADVER` values identify format families;
accepting a code is not an all-feature certificate for a particular product year.
See the [ledger](coverage.json) and [Autodesk HEADER reference](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-A85E8E67-27CD-4C59-BE61-4DC9FADBE74A.htm).

## G03 — General version conversion

Per-feature admission checks, selected geometric conversions and bounded target
compatibility diagnostics exist. They do not constitute a general converter that
transforms an arbitrary document, every dependency and all private caches into
another valid format family.

**Next implementation:** a conversion plan that inventories source features and
their dependency closure, classifies each as preserve/convert/drop/reject, names
all intentional losses, prepares target objects and only then publishes. The
policy must distinguish a lossless request from explicitly approved flattening.
Changing `$ACADVER`, deleting unsupported tags or approximating geometry without
a loss report is not sufficient.

**Acceptance:** source remains unchanged on rejection; unsupported data cannot
silently disappear; target references resolve; geometry and optional metadata
match the declared conversion policy; target-native open/save passes. Include
interactions between TABLE/FIELD/display blocks, named objects, attributes,
retained sequences and external references, not only isolated entities.

## G04 — Dependency-complete import, cloning and atomicity

Named-object graphs, source identities, raw object transactions, explicit mappings
and protected removals are implemented in documented subsets. Retained objects
and referenced private records may deliberately refuse cloning or cross-document
adoption. Refusal can be the correct safety behavior; replacing it with a shallow
copy is not progress toward safe parity.

**Next implementation:** a two-phase dependency-closure planner. Classify hard
ownership, hard/soft pointers, reactors, dictionary slots, resource names and
embedded references by context. Resolve name collisions and handle allocation
before publication. Preserve aliases, shared resources and cycles where legal.
Unknown pointer-bearing payloads need an explicit strategy, not replacement of
all handle-looking strings.

**Acceptance:** exact shared-versus-independent identities, retained child and
terminator metadata, source immutability, deterministic collision behavior,
no dangling pointers, reference-safe erasure and failure before publication.
Fault injection must reach the last dependency and callback. Entity-local staging
does not undo caller callbacks, concurrent mutation, process termination or an
entire filesystem transaction. See [named objects](named-object-database.md),
[raw transactions](raw-object-transactions.md), [2D records](polyline2d-records.md)
and [INSERT sequences](insert-sequences.md).

## G05 — TABLE orchestration and complete regeneration

It is inaccurate to call TABLE support absent. Existing pieces include addressed
grids, bounded formulas, stored scalar updates, explicit style cascades, measured
layouts, generated display blocks and source display selection. Their publication
boundaries are deliberately separate. `ApplyFormulaResults`, `BuildDisplayBlock`
and `ReplaceDisplayBlock` are not synonyms for regenerating every representation.

**Remaining work:** complete interpretation of all claimed inline/private schemas;
automatic native style precedence and duplicated-style coherence; synchronized
inline/backing values, FIELD results, TABLEGEOMETRY and display data; block and
rotated content; advanced flow, breaks and layout modes; native font-dependent
measurement. A native application may regenerate a selected display from other
retained data, exposing inconsistencies a static viewer misses.

**Next implementation:** resolve addressed content, styles, FIELD dependencies
and measurement inputs; build every supported candidate representation; check
reference/resource budgets; publish them together. Unknown cache layouts must
cause explicit refusal or a scoped result, not fabricated compatible-looking data.

**Acceptance:** cell, formula, merged-region, style and linked-content edits stay
consistent across relevant representations after two saves and native regeneration.
Late failures or missing fonts/resources cannot leave half-updated state. See
[calculation/layout](table-calculation-layout.md), [display binding](table-display-binding.md),
[content editing](table-content-editing.md) and [cell formatting](cell-style-format-editing.md).

## G06 — FIELD semantics beyond the bounded evaluator

Persistent FIELD results, ownership forests, failure outcomes, selected text-host
updates and an explicit standard evaluator already exist. `_text`, `AcVar` and
`AcExpr`, supplied variables, owned child slots, numeric expressions, date masks
and angular formatting have documented implementations. Listing all FIELD/date/
angle evaluation as missing would be incorrect.

**Remaining work:** broader native providers, object-property lookups and
dependencies, system/context bindings, expression options, external sources,
native error/fallback semantics and propagation to additional hosts and layout
caches. Explicit time/variables are not automatic drawing, filesystem, sheet-set
or environment access. Whole literal-host replacement is not arbitrary substring
FIELD expansion or MTEXT column reflow.

**Next implementation:** provider interfaces with explicit dependency discovery,
context and side-effect policies; broaden grammar against native examples;
integrate invalidation and re-evaluation with existing forest transactions.
Do not execute arbitrary text as a provider side effect.

**Acceptance:** native examples per provider/context; cycles, missing properties
and stale snapshots; exact failure-cache/host policies; unchanged unrelated trees.
Existing date/unit/culture contracts remain explicit until native equivalence is
demonstrated. See [standard evaluation](standard-field-evaluation.md),
[results](field-results.md), [text hosts](field-text-hosts.md) and Autodesk's
[field evaluator API](https://help.autodesk.com/cloudhelp/2027/ENU/OARX-RefGuide/files/OARX-RefGuide-AcFdFieldEvaluator__evaluate_AcDbField__int_AcDbDatabase__AcFdFieldResult_.html).

## G07 — Dynamic, annotative and associative behavior

Retaining blocks, contexts, DIMASSOC/section data or association pointers is not
an implementation of the corresponding native evaluation system. INSERT geometry
and atomic attribute transforms are foundations, not dynamic-block action,
annotation-scale, constraint or live-section engines.

**Next implementation:** select one dependency family and model its state changes,
required contexts and invalidation rules explicitly. Do not flatten a dynamic or
annotated object into one visual representation unless the caller requests and
receives a loss report.

**Acceptance:** parameter/context changes, nested references and labels regenerate
consistently; unsupported actions are identified before mutation; clone/import
preserves relevant dependencies; native regeneration does not replace the intended
result unexpectedly. See [INSERT geometry](insert-geometry.md),
[containers](typed-containers.md), [MULTILEADER contexts](multileader-contexts.md)
and [section membership](section-manager-membership.md).

## G08 — Text, dimensions, leaders, viewports and plotting

Stored style/settings fidelity, formatting and selected display helpers are not
native visual equivalence. Remaining qualification includes font substitution,
SHX/TrueType metrics, shaping/wrapping, MTEXT column reflow, fit/alignment,
dimension/leader placement, annotation contexts, viewport clipping and plot-style
effects. External IMAGE/PDF/DWF/DGN references also need real content interpretation.

**Next implementation:** explicit measurement/rendering providers with deterministic
inputs and a native comparison corpus. Keep literal text, native controls, FIELD
expansion and formatting distinct. Define missing-font/resource behavior rather
than estimating it from string length or dropping external content.

**Acceptance:** positions, extents, line breaks and visual output under specified
fonts, units, locales, scales and plot settings. Database audits cannot prove
these properties. Include native producer fixtures, not only helper-built examples.
See [text styles](text-style-fidelity.md), [MTEXT columns](mtext-columns.md),
[output settings](output-settings.md) and [UNDERLAY geometry](underlay-affine.md).

## G09 — Geometry and application-specific engines

Geometry work covers many primitive, conic, spline, hatch, INSERT and legacy-mesh
operations. Remaining scopes include native reconstruction/rendering,
subdivision/crease evaluation, manifold/degeneracy interpretation, all fitted or
smoothed record families, modeler data and arbitrary private entity semantics.
Existing rank/tolerance and representability checks are contracts, not necessarily
bugs to remove.

Modern MESH preflight checks finite values and index/count consistency, not
manifoldness, winding, self-intersection or subdivision. An inert ACIS/SAT envelope
is not a modeling kernel. A proxy can preserve a custom application's payload
without knowing its edit or evaluation semantics.

**Next implementation:** define the representation and numerical domain of each
operation, then implement it without weakening refusal or preservation behavior.
Separate improved intermediate arithmetic from claims about all degeneracies.
Application payload semantics need an actual schema/modeler/provider.

**Acceptance:** analytic or native geometry oracles, adversarial numeric inputs,
exact unaffected metadata, explicit approximation bounds, repeatable transforms
and relevant native acceptance. See [MESH preflight](mesh-write-validation.md),
[ACIS envelopes](acis-sat.md), [opaque entities](opaque-entities.md) and
[Autodesk proxy definitions](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-DevGuide/files/GUID-11AEF76F-9638-4322-A7D0-0D628BBB456F.htm).

## G10 — Mutation notification and cache coherence

Recent PRs close stale-graphics defects and add validated editing APIs. They do
not make every exposed list, array, vertex, edge, pattern or shared resource
observable. Caches can depend on other objects. Common graphics, child payloads,
display blocks and private evaluation caches are separate stores.

**Next implementation:** maintain a dependency/invalidation inventory; prefer
operations that stage candidates, preserve identity and publish after validation.
Plan a compatibility migration before changing public collection types. Where raw
mutation remains, document the caller's responsibility and expose a deliberate
refresh/invalidation path.

**Acceptance:** changed/no-op/refused operations with absent, empty and nonempty
caches; aliases/shared resources; clones; reader hydration; late failures;
cross-format saves; unchanged unselected records. A fixture must seed its proxy
after setup edits when testing a later operation's source-preservation contract.
Fix such setup without removing the original operation assertions. The MESH patch
covers two setters, not the entire inventory. See [3D-polyline edits](polyline3d-edits.md)
and [common entity data](common-entity-data.md).

## G11 — Resource, platform and performance qualification

The consolidated pipeline builds five library targets and exercises selected
installed assets/runtimes. Full conformance and package smoke tests have different
scope. Compiling a legacy target is not execution on that CLR; an updated Windows
Framework runner is not every historical CLR installation. Numerical, encoding,
filesystem and culture claims need the actual assembly/environment evidence.

Bounds and malformed-input cases exist, but are not exhaustive fuzzing,
concurrency, denial-of-service or production-scale performance qualification.
Large aliased arrays, nested dependencies, long tokens, composite payload budgets
and interrupted file operations need measured, operation-specific checks.
`SaveAtomic` does not turn all save/import/callback operations into transactions.

**Next implementation:** deterministic mutation/fuzz corpora, allocation/time
measurement, fault injection and broader environment matrices. Preserve prior
test identities, exact fixture inventories, corruption controls and source binding.
Do not increase timeouts or normalize identities merely to hide regressions.

**Acceptance:** reproducible source/build/toolchain/fixture hashes, actual result
arrays, unchanged baseline coverage, no unreviewed repairs/waivers, and explicit
runtime/performance scope. See [CI/release](../CI-RELEASE.md),
[the pipeline](../../tools/ci/pipeline.py) and [atomic saves](atomic-file-save.md).

## Implementation sequence and completion criteria

First qualify pending narrow PRs against their actual heads. Next create native
acceptance infrastructure and use its failures to prioritize graph closure,
TABLE orchestration and FIELD/provider work. Expand historical typed profiles
one claimed subset at a time. Broaden rendering/modeler/annotation engines with
appropriate providers and fixtures. Isolated cache/geometry fixes remain useful,
but do not replace the major workstreams above.

A gap closes when its API/schema contract is implemented; applicable versions,
transports and dependency interactions are tested; preservation/refusal behavior
is checked; independent output tests pass; relevant runtimes are exercised; and
native evidence exists where native equivalence is the claim. Final reviewed
source and actual artifacts must agree. No row or case count substitutes for
this evidence chain.

JavaScript PR #98 is a separate project with fixed-reference, original-test and
differential gates. Neither this report nor a green C# PR waives them.

## Documentation and workflow cleanup

The root README now focuses on installation, usage and support boundaries.
The conformance README is a contract index rather than an accumulating PR log.
This report is the maintained major-gap entry point. Feature contracts, fixtures,
qualification data, upstream notes and licenses remain.

Two superseded narrative checkpoints are retired from the current tree because
their nearest-work lists describe many features since implemented. Their exact
original text remains available at the reviewed baseline:

- [13 September / PR #41](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-2026-09-13.md).
- [13 September HATCH / PR #50](https://github.com/wieslawsoltes/netDxf/blob/9e4eb348b607f3fe3d50f5f469a382750befeba7/doc/dxf-conformance/checkpoint-hatch-2026-09-13.md).

The [14 September / PR #58 checkpoint](checkpoint-2026-09-14.md) is deliberately
**retained unchanged**: the source-pinned coverage ledger and HATCH audit still
reference it. An initial cleanup candidate proposed removing it; the dependency
review caught that mistake before qualification. The correction restores its
original blob rather than changing the ledger or weakening evidence validation.
Its old missing-feature list remains historical, not the current project status.

This is targeted cleanup, not an exhaustive declaration that every remaining
document is necessary or redundant. Source-pinned evidence is not relabeled.
Live references to retired files must become immutable archive URLs; referenced
evidence documents stay in the tree when required.

The default branch already has only `ci-build.yml` and `release.yml` under
`.github/workflows`. Both remain unchanged: no active workflow-file deletion is
needed. The source-only hygiene checker uses existing verifier discovery to guard
that inventory, entry-point/link integrity and retired-document references.
It is not a CAD case or a new workflow. No publication workflow, credentials,
release tags or registry publication are part of this cleanup.
