# Retained records and section record transport — local checkpoint

Runtime commit: `cb33807c2880d3dfa76031dbf24b03ea0d77f445`.
Executable verification commit: `88a5944a5ea172ebbbde4f99321c23c75709a1fd`.
Executable tree: `fe3fba305b267efa344e4e13b5c066adda727c85`.
Verified remote base: `e494c189c1c601d34e1cf7f534804c189eeb169a`.
Unchanged native reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

**This checkpoint is committed locally, not pushed.** The GitHub connection
exposed only read actions in this session; commit/tree/ref updates were not
available. Direct Git also failed to resolve github.com. No remote branch,
pull-request text, workflow or release was modified. The change archive contains
all three local commits as patches and an independently checked combined diff.
Full typed document transport and full parity remain unfinished.

## New production modules

Four dedicated original paths are implemented:

| Module | Public JavaScript adapters for native internal methods |
| --- | --- |
| `IO/DxfReader.SectionSettings.js` | `ReadSectionSettingsRecord`, `ResolveSectionSettingsReferences` |
| `IO/DxfWriter.SectionSettings.js` | `WriteSectionSettingsPayload` |
| `IO/DxfReader.SectionManager.js` | `ReadSectionManagerRecord`, `ResolveSectionManagerReferences` |
| `IO/DxfWriter.SectionManager.js` | `WriteSectionManagerPayload`, `PrepareSectionManagerClasses` |

These are exported through the existing browser-safe DatabasePayloadIO and
DxfTransport modules. DatabaseIOContext holds their real pending-reference state.
They are record adapters, **not complete DxfDocument.Load/Save/SaveAtomic**.

### Section settings

Both SECTIONSETTINGS and SECTION_SETTINGS retain the known public grammar,
ordered type/geometry bundles, required fields, reference counts and aggregate
budgets. Geometry marker modes remain distinct, including zero and one-bundle
cases. Mixed marker grammars, missing public fields and invalid recognized
values remain errors. Unknown/private variants are preserved whole as opaque
payloads instead of partially translated. Simultaneous group-62/group-63 color
variants remain opaque according to the source's admission rule.

Geometry values preserve layer/linetype/plot-style/hatch text, color and lineweight
codes, signed-zero numeric fields and the source assignment/error order. XData
uses the actual document registry; registration performed before a later parse
failure remains observable. The output path validates before emitting bytes,
retains version restrictions and writes in source order. Callback failure leaves
only the already completed output prefix.

Deferred binding uses accepted physical-source identities, not matching generated
defaults or arbitrary same-handle replacements. Repeated source references and
null slots remain ordered. A destination must be an actual source BlockRecord.
Each settings object's full replacement list is applied only after its pending
types bind successfully, retaining the native partial-state boundary between
objects rather than inventing a whole-document transaction.

### Section managers

Both SECTION_MANAGER and SECTIONMANAGER retain bounded membership, ordered
subclass/update/count/pointer fields and numeric reactor-identity checks. The
65,536-entry boundary is covered without treating null pointers as resolved
sections. Unsupported profiles and private flags remain opaque.

Reference resolution preserves exact source section identity, repeated membership
and the existing reciprocal ownership rules. Output retains the stored tags.
CLASS preparation preserves compatible declaration identity and application
metadata, updates actual stored counts and rejects live typed conflicts. A name
used only by opaque private instances is not silently rewritten.

## Reconstructed retained-record qualification

The fresh corpus covers the preceding FIELD, DIMASSOC, SUNSTUDY, TABLESTYLE,
TABLECONTENT, TABLEGEOMETRY and CELLSTYLEMAP record readers/writers, private
XRECORD admission, SPATIAL_INDEX/VBA_PROJECT envelopes and the new section codecs.
It includes successful read/register/resolve/write/validate paths as well as
malformed/public/private variants, all six target profiles, exact text and binary
output, source-identity invalidation, callbacks, CLASS conflicts, partial FIELD
header outputs, and forty deterministic randomized section inputs.

It contains **2,593 unique scenarios / 5,909 operations**. Input descriptors do
not contain expected results. Independent native and JavaScript controllers
invoke the unchanged C# methods and actual production adapters. They compare
models, deferred lists, metadata, errors, CLASS state, registry effects and exact
emitted bytes. Existing exception observation conventions are retained; no new
output normalization or expected-failure rule is introduced.

Fixtures explicitly create registered support objects in a 2018 document and
then select the target profile before parsing. This permits exercising old-profile
admission without claiming those support objects were themselves loaded from an
old-profile file. Accepted-source state is explicit fixture input; physical-stream
source tracking has its separate unchanged regression corpus.

**46 supplemental regressions** are added. They include successful typed lifecycles
for all nine central record families, maximum manager membership, opaque/public
classification, failed binding retries, metadata side effects, snapshots and
malformed-observation rejection. Original tests requiring complete document I/O
were not shortened; original-case coverage remains 3,619.

The new category is required by aggregate verification and the offline package.
All new browser inputs are appended after existing categories. The separate real
Chromium report is marked `scope: retained-record-io-only`, `fullSuite: false`;
it does not satisfy the complete browser gates.

## Remaining scope

Local presence is **462/510 library mirrors (48 missing)** and **66/193 original
conformance-file mirrors (127 missing)**. Original case coverage is
**3,619/35,309 (31,690 missing)**. The apparent increase from the older README's
434 mirrors includes 24 paths already present in the newer remote commits; only
four original library paths are added by this continuation. File presence is not
exhaustive member/signature or behavioral qualification.

Complete reader/writer dispatch, entity envelopes, document Load/Save/SaveAtomic,
resource/reference reconstruction, remaining private/TABLE/evaluator I/O, version
conversion, missing original tests/examples and broad numerical/host/performance
acceptance remain incomplete. The full 57-stage aggregate and complete 159,250-check
browser suites were not rerun, nor were Windows/hosted CI, HTTP-origin, MPFR or
performance qualification. Prior numeric, recursion/assertion and globalization
failures are not waived or claimed fixed. No native AutoCAD qualification is claimed.

Original C# sources, original tests and shared fixtures are unchanged. No workflow
was restored, no comparison removed, no failure waived, and no force push, merge
or npm publication occurred. The new commits remain local until a write-capable
GitHub route is available.

Historical execution results and recovery details are available in [the pre-cleanup record](https://github.com/wieslawsoltes/netDxf/blob/2593c82490af9bf7f00208e75162ff74707dec04/javascript/doc/RETAINED_RECORD_IO.md). Current published scope is maintained in the [README](../README.md).
