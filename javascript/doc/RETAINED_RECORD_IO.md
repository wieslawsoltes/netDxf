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

## Recovery and preserved published work

The actual PR head was ahead of the stale PR description: `e494c18` already
contained eighteen retained-record runtime partials, after `c896447`'s output,
GEODATA and SUN verification. That newer work was preserved rather than reset
to the older described checkpoint.

No unpublished checkout survived. The source artifact for run 36024087740 was
downloaded and checked against GitHub's SHA-256 digest:
`38fbc62537622d1599db5311bd3a835185a6753f839ff73fa9de20b64fa0cf12`.
Its PR merge archive has newer native sources, so only its current JavaScript
files were used. Restoring the pinned non-JavaScript tree and existing workflow
cleanup reproduced the complete published tree
`4f7368491219193e8bed5a416573e3f503ebc4b4` exactly. No newer merge source was used
as the behavioral reference.

The prior in-progress 1,619-scenario verifier was not available in that published
runtime checkpoint. The corpus and observation adapters in this continuation are
**new reconstruction, not recovered bytes or relabeled earlier results**.
The synthetic local base commit has the exact remote file tree but a different
commit identity. Patches apply to the real remote base without those synthetic
history identities. All eighteen earlier workflow deletions remain preserved.

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

## Completed local results

September 24, 2026: Debian 13 x64, SDK 8.0.425 / .NET 8.0.31, Node 22.16.0,
Chromium 144.0.7559.96. Results below belong to executable commit `88a5944`.
The [local receipt](retained-record-io-local-88a5944.json) retains fourteen actual
reports plus original-suite hashes and metadata. Full arrays and logs are in the
recovery archive.

| Check | Observed result |
| --- | --- |
| New retained/section records, Debug and Release | Each: 2,593 scenarios / 5,909 operations; zero differences or unavailable native observations |
| Focused Chromium, both profiles | Each: 2,593 matching scenario digests; 619 native ESM modules; zero page errors |
| Existing SECTION_MANAGER lifecycle, both profiles | Each: 174 scenarios / 5,406 operations; zero differences |
| Existing database-payload regression, both profiles | Each: 1,070 scenarios / 4,701 operations; zero differences |
| Existing physical-source metadata, both profiles | Each: 420 scenarios / 12,322 operations; zero differences |
| Existing stored-dependency regression, both profiles | Each: 738 scenarios / 5,566 operations; zero differences |
| Full unchanged C# suite | 35,309 passed in each configuration |
| Complete mirrored original JavaScript subset | 3,619 passed; no duplicate or unexpected identities |
| Full supplemental suite | 1,370 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; 616 files |
| Source generation | Foundation, dimension and GTE exact regeneration checks pass |
| Full-parity verification | Both profiles fail; missing/stale evidence and incomplete coverage remain blocking |

An initial combined JavaScript test command timed out before completion and was
rerun fully. Initial Release regression and generator commands overlapped an
active native build lease and were correctly refused. They were rerun completely
after the build ended; no lock was bypassed or removed. Preliminary fixtures that
omitted required record identities were corrected, not used to weaken the reader.
Only final, completed, fingerprint-matched results supply the passing evidence.

Runtime fingerprint:
`14dcfa8a8788def0b076176e976089d414f9d1f099474a85e06d221f74a32695`.
Verifier fingerprint:
`a7eacea5ed8ed2a672aefe80e85869290b98524d52d4d8eefc1aa735b52ea78e`.
Native source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

From javascript/, with the pinned native source and toolchain configured:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:retained-record-io
npm run test:retained-record-browser
npm test
npm run test:unit
npm run test:package
npm run verify:complete
```

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
