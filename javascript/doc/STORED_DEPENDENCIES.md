# Retained FIELD, DIMASSOC, SUNSTUDY and binary diagnostics

Runtime checkpoint: `a7f4559822ad6e63cfb10ea01ad814fd25929f7c`.
Executable verification checkpoint: `887557749595490b0e75977686eb3f40c1b4620d`.
Executable tree: `36b8b13f725d85d67a55c93a0409b4129f60aa1e`.
Unchanged C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This checkpoint adds three original-path source mirrors and fixes an existing
binary-reader diagnostic defect. It does **not** implement a FIELD evaluator,
dimension regeneration, SUNSTUDY execution or complete typed DXF transport.

## Recovery and preserved work

The remote branch had advanced to `bb7b117` before this continuation. Its
TABLECONTENT and registered ACAD_TABLE work, including the preceding `ced929a`
implementation, was already pushed and is preserved. The PR description still
named the older TABLEGEOMETRY checkpoint; the actual branch head was used.

No surviving unpublished checkout or patch was found. GitHub source artifact
`10741656127` was downloaded and its SHA-256 verified:
`649687ef557b836a2624690440f8fe92e45c80dbfe9630f3a8c8960fa08eba14`.
Restoring all tracked paths, including ignored tracked fixtures, reproduced
`bb7b117` tree `7ef2c8584b2f92f87fe74021c33f30ece728885b` exactly.
Local reconstructed commit IDs differ from GitHub, but the complete file trees
were checked before each non-forced branch update. Original C# sources, original
tests and shared fixtures remain unchanged.

## FIELD source and ownership semantics

`netDxf/Objects/DxfStoredField.js` retains the full payload, evaluator identifier
and field-code string without executing those strings. The internal constructor
and Resolve method are explicit retained-loader adapters, not substitutes for
the missing typed reader's admission checks.

Semantic handle tags bind to exact source objects. Identity and arbitrary-handle
fields remain stored but do not become fabricated dependencies. Numeric handle
canonicalization preserves UInt64 parsing, including leading zeroes, overflow and
format errors. The source's leading object slots retain their separate null and
lookup-spelling behavior. Repeated references remain repeated; dependency use
counts therefore do not collapse to a set.

Leading FIELD children must have unique FIELD identities, reciprocal ownership
and no ancestor cycle. Validation rejects undeclared owned children while
exempting the actual extension dictionary. Payload/evaluator data is immutable;
Children, ReferencedObjects and References are live, read-only list views with
source-compatible enumerator invalidation.

Resolution is deliberately not an invented transaction: a failing callback can
leave already observed dependencies or children in the backing lists. Repeat
resolution and callback mutation follow the unchanged C# behavior. Source/profile,
registration and identity checks continue to reject stale or moved graphs.

## DIMASSOC identities and reactor backlinks

`netDxf/Objects/DxfStoredDimAssoc.js` includes the immutable
DxfStoredDimAssocPoint value model. Points copy Vector3 values and retain exact
binary64 parameters, including negative zero. Geometry binding is an explicit
internal-loader step rather than a mutable public point property.

DIMASSOC resolves an actual registered Dimension and graphical geometry objects,
retaining ordered/repeated references and partial state on failure. Its source
owner must be the reciprocal dimension extension dictionary with the exact
case-sensitive `ACAD_DIMASSOC` hard-owner entry.

The model separately snapshots the persistent-reactor sequence and whether each
source graphical entity did or did not contain the association backlink.
Validation detects changed ownership, source profile, target registration,
reactor ordering and backlink presence. This does not compute osnap positions,
re-evaluate associations or regenerate dimensions.

## SUNSTUDY retained projections

`netDxf/Objects/DxfStoredSunStudy.js` preserves its retained packet and exposes
qualified scalar values, decoded text, raw hour flags and role references.
Signed output/date/time numbers remain source values, not inferred application
enumerations or evaluated date ranges. Ordered and repeated references, null
slots and lexical handle spelling are preserved.

Resolution requires an actual source dictionary owner and records the registered
ancestry through the source document. Later ownership or reference-identity
changes are reported independently. Resolver failure and repeated resolution
retain the native partial-mutation behavior. Generic cloning and erasure of all
three source-bound models remain explicitly rejected by their existing schema
boundaries instead of inventing a private application lifecycle.

## Binary-reader diagnostic correction

Porting all original SunStudyProducerRawTests exposed a real existing defect:
the JavaScript binary reader rejected invalid data but omitted the native group
code and value byte address from the error. Four binary producer-rejection cases
initially failed while the other 22 cases passed.

BinaryCodeValueReader now captures its actual absolute stream position after
reading the group code and reports it for invalid hexadecimal handles, booleans
and nonfinite doubles. Modern and legacy binary group encodings and nonzero
stream origins are tested. The precise native diagnostic is retained, for example:

```text
Invalid hexadecimal handle (1 to 16 digits) value for group code 340 at byte address 24.
```

The fix neither accepts malformed input nor closes the caller's stream. It does
not claim all BinaryReader/System.IO behavior or nonseekable-stream parity.

## Independent tests and required integration

The new input-only corpus contains **738 scenarios / 5,566 operations**:
**218 retained-model scenarios / 5,046 operations** and **520 binary diagnostic
inputs / operations**. Separate C# and JavaScript adapters invoke the unchanged
native models and actual production port. Observations include exact scalar
bits, UTF-16 text, identities, ordered views, source errors, callback traces and
partial state. Native diagnostic messages are compared directly, not normalized.

The binary inputs span handle, double and Boolean group codes, invalid values,
legacy/modern group encoding, stream origins and a preceding valid record.
Retained-model inputs cover source profiles, leading FIELD children, DIMASSOC
masks/geometry/backlinks, SUNSTUDY ancestry, failures, repeated resolution,
metadata/removal guards and 32 deterministic randomized sequences.

The **26 complete original SunStudyProducerRawTests cases** retain every source
identity and assertion: producer manifest classifications, compressed and
uncompressed hashes, exact source bytes, disclosed carrier transformations,
raw transport normalization, error detail and caller stream ownership. These
are raw producer-fixture tests, **not typed SUNSTUDY Load/Save qualification**.
No original typed-reader/evaluator test was shortened to increase coverage.

The **32 added supplemental tests** are counted separately. The new category is
mandatory in run-qualification.mjs and verify.mjs, appended after all earlier
browser inputs, and exercised through the installed package. The hosted ownership
workflow retains its Windows replacement-host prerequisite and every previous
comparison and original test.

## Completed local results

Executable checkpoint `8875577`, September 23, 2026. SDK 8.0.425, runtime 8.0.31,
Node 22.16.0, Debian 13 x64 and Chromium 144.0.7559.96:

| Check | Actual result |
| --- | --- |
| New dependencies/diagnostics, Debug and Release | Each: 738 scenarios / 5,566 operations; zero mismatches or unavailable native observations |
| Nine lifecycle corpora, both configurations | Each: 2,365 scenarios / 47,478 operations; all pass |
| Full unchanged C# suite | 35,309 passed in each configuration |
| Full mirrored original JavaScript suite | 2,946 passed; no duplicate or unexpected original identities |
| Supplemental JavaScript tests | 1,062 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; 526 files |
| Source-derived foundations and dimensions | Both exact regeneration checks passed |
| Full differential refresh | All 47 stages completed per configuration; Debug 42 pass / 5 fail, Release 41 pass / 6 fail; no stage timeout |
| Release inline Chromium | All 142,896 checks executed; 83 prior-category failures; no new dependency failures, page errors or unavailable native observations |
| Debug inline Chromium | All 142,896 checks executed; 27 failures including 22 unavailable native observations; no new dependency failures or page errors |
| HTTP-origin Chromium | Failed before comparison execution: ERR_BLOCKED_BY_ADMINISTRATOR |
| Independent mathematical reference, both configurations | MPFR 4.2.2: 30,904 comparisons each, zero failures or disagreements between 512- and 1,024-bit references |
| Existing descriptive benchmark | Completed for 2,000 objects, 3 warmups and 12 samples per operation |
| Full-port gates, both configurations | Failed; all remaining failures and incomplete source/test coverage retained |

The independent audit concerns the development-only HighPrecisionMath reference,
not replacement of production .NET compatibility checks. It compares finite bits
and signed zero, and nonfinite classes; exact native NaN sign/payload differences
remain separate blocking evidence. MPFR is not a production dependency.
The benchmark covers raw operations and selected geometry/collections, not all
registered workloads, a C# speed comparison or release performance acceptance.

The earlier progress message and executable commit message repeated the prior
Debug browser count of 31. The final actual report has **27**, including the 22
unavailable native scenarios, four classic-LEADER digest differences and one cubic
Bezier difference. The report is retained unchanged. No numerical implementation
changed, and the count difference is not claimed as a numerical correction.

An initial combined local test command was interrupted before completing the
original suite. A complete successful rerun supplies that evidence; interrupted
attempts and the initial producer diagnostic failures are not relabeled as passes.

Runtime fingerprint:
`3d73396e798302921c87e9141b1058910bd49e0df8a675e61de3c4756805611b`.
POSIX verification fingerprint:
`57fb5e6fec0fc016e68ebfdeea5436b157754c0293f4c95dc0bc3dd47a4d43b9`.
Source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

From javascript/, with the pinned source checkout and toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:stored-dependencies
npm test
npm run test:unit
npm run test:package
npm run test:differential
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
python tools/math-independent.py
npm run benchmark
npm run verify:complete
```

## Hosted four-profile verification

[Workflow run 35853472716](https://github.com/wieslawsoltes/netDxf/actions/runs/35853472716)
at `8875577` completed successfully in Ubuntu 22.04 and Windows 2022, each in
Debug and Release. Every job ran all nine retained/ownership corpora: **2,365
scenarios / 47,478 operations**, all **236 focused supplemental tests** and every
one of the **2,946 mirrored original cases**. Each new dependency report contains
738 observed scenarios / 5,566 operations and zero failures or unavailable native
observations. Focused successes do not qualify the broad failing port workflow.

All four archives were downloaded, checked against GitHub's SHA-256 digests and
inspected. The [committed receipt](stored-dependencies-hosted-8875577.json)
retains the four actual new comparison documents, hashes and statistics for the
32 preceding comparison documents, and full mirrored-suite metadata/counts/hashes.
All 36 complete comparison documents and original-result arrays remain in the
identified archives and validation bundle. Original identities were independently
checked against the unchanged C# suite. Supplemental JSON outputs from original
hatch/raw tests remain archived; they are not extra original-case identities.

The existing verification fingerprint sorts host paths before separator
normalization. The Windows value is
`0d729e979399585c6b4aa42a863876ba1c8ebba6c502946fe7629abbc4c2b8fc`.
Both platform hashes were reproduced from identical checked-out file bytes;
actual reports were not rewritten. All four runtime fingerprints match. This
final documentation/receipt update does not change executable fingerprints.

## Remaining parity work

Current ledger: **388/510 library mirrors (122 missing)**,
**60/193 original conformance-file mirrors (133 missing)** and
**2,946/35,309 original cases (32,363 missing)**. File presence is not exhaustive
member/signature or behavioral qualification.

Release retains 26 concrete-dimension, six LEADER, eight block, three coordinate
and 128 entity operation differences. Debug retains one cubic Bezier tangent
NaN-sign comparison and 22 unavailable native scenarios from original assertions:
six dimensions, twelve tolerances and four layout/viewports. Both casing stages
reject the local Debian globalization profile before pairwise comparison; their
precomputed pair counts are not executed-comparison counts. No casing table,
expectation, tolerance or failed-result allowlist was changed.

Typed DXF reading/writing and Load/Save/SaveAtomic integration, full version
conversion, remaining TABLE and private-schema APIs, field/association evaluation,
original tests/examples and broad platform/filesystem/performance acceptance are
still incomplete. Retained constructor/Resolve adapters do not qualify the typed
loader's schema admission. The raw transport API is not a typed-IO substitute.
No native AutoCAD open/AUDIT/save/reopen fidelity is claimed.

PR #98 remains draft. No original C#/fixture changes, merge, force push,
comparison removal, failure waiver or npm publication occurred.
