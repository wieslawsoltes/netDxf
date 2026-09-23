# Preview, metadata and mesh transport helpers

Runtime checkpoint: `38e48f152c0ebf33221f456de2c74066db0b8e24`.
Executable verification checkpoint: `688b6d0e50d9c71e82ee209ac8042746302408bd`.
Executable tree: `53ce231b513b9367dae34dcc22d221ed26f09227`.
Unchanged C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This adds five original-path IO mirrors and an explicit browser-safe
`DxfTransport` namespace. They are working section codecs and output validators,
**not a complete typed DxfDocument.Load/Save/SaveAtomic pipeline**. They do not
render previews, interpret image formats, or evaluate private CAD schemas.

## Implemented contracts

**DxfThumbnailImage.** Read starts at the section name, checks the single
nonnegative group-90 count against the bytes actually received, tolerates the
source's comment/empty/128-byte input cases, and leaves ENDSEC current. Missing,
duplicate, excessive and truncated counts retain the original failure order and
positions. Buffer growth follows received data, never an untrusted declared
allocation. Write emits nothing for an empty image; otherwise it emits the full
section, byte count and independent packets of at most 127 bytes.

**DxfEntityCommonData.** The explicit reader state retains color name, shadow mode,
proxy byte count, accumulated bytes and completion state. Null proxy data differs
from a declared empty proxy. Count-after-data, duplicate fields, 128-byte input
packets, the 16 MiB bound, long-count range checks and version restrictions match
the source. Rejected values preserve prior state and callback timing; completion
preserves native disposal/repeat behavior. Output uses group 92 before the 2013
profile and group 160 thereafter, with copied 127-byte packets. Color-name output
preserves literal backslashes and escapes line controls. Whole-document version
validation visits every block, attribute definition and INSERT attribute.

**DxfMTextBackground.** Only background-specific group codes allocate the ref-cell
model. The initialized object remains visible after an invalid first value;
ArgumentException is wrapped with the source position and inner error. Repeated
recognized fields update the same object. Background output requires the 2007
profile, and frame output requires 2018. Required active-background defaults are
written without mutating absent scale or color-index properties on the model.

**DxfMeshWriteValidation / DxfMeshVersion.** Preflight counts the serialized face
list before walking vertex indices, including repeated references to one large
face array. It checks null/short faces, finite coordinates and creases, valid
vertex indices and edges. Document traversal also validates POLYLINE polygon
meshes. MESH output is rejected before the 2010 profile, including entities in
unused definitions and inactive layouts; no automatic lossy conversion is used.

Private C# partial methods are adapted as explicit chunk/version/document
functions rather than represented by a second incomplete DxfReader class.
The string adapter preserves single-pass DXF Unicode decoding, pre-2007 UTF-16
escapes, and literal database-string escaping. Arbitrary encodings and full CLR
stream/localized-exception behavior are outside this helper checkpoint.

## Buffer ownership correction

A regression test caught Node Buffer.slice returning shared storage. Preview and
proxy writers now allocate a fresh Uint8Array for every packet and copy the
relevant subarray into it. Retaining or mutating a supplied Buffer cannot rewrite
an already emitted packet. Tests also preserve the completed prefix when a
synchronous caller write throws. This correction is included in `688b6d0`.

## Original and independent tests

The input-only corpus has **1,165 scenarios / 3,473 operations**, using text,
modern binary and legacy binary codecs, six writer profiles, all relevant block
placements and malformed/count boundary cases. Independent controllers invoke
the unchanged C# private methods and the actual production JavaScript helpers.
They observe exact emitted bytes, reader state and position, metadata state,
inner errors and nullable background fields. Strict envelope/count checks reject
missing or malformed observations. No expected output tables or normalization
are used in production code.

**32 complete original ThumbnailImageTests cases** are now mirrored. The twelve
original document-roundtrip cases still requiring typed Load/Save are absent,
not shortened, skipped or counted as passing. **29 supplemental tests** are
counted separately. The category is mandatory in aggregate verification, appended
after all existing browser inputs, and exercised from the offline-installed
package. Existing reader/writer corpora and the Windows replacement-host build
remain enabled in the hosted workflow.

## Completed evidence at 688b6d0

September 23, 2026: SDK 8.0.425, .NET 8.0.31, Node 22.16.0, Debian 13 x64,
Chromium 144.0.7559.96. Original sources, tests and fixtures are unchanged.

| Check | Observed result |
| --- | --- |
| Section comparisons, Debug and Release | Each: 1,165 scenarios / 3,473 operations; zero mismatches or unavailable observations |
| Full unchanged C# suite | 35,309 passed in each configuration |
| Full mirrored original JavaScript suite | 3,619 passed; no duplicate or unexpected original identities |
| Supplemental JavaScript suite | 1,153 passed; zero failures, skips or TODOs |
| Offline-installed package | Passed; 564 files |
| Foundation, dimension and GTE regeneration | All three exact checks passed |
| Complete differential refresh | All 51 stages completed per configuration; Debug 45 pass / 6 fail, Release 44 pass / 7 fail |
| Release inline Chromium | 145,884 checks executed; zero new section-codec mismatches or page errors; 119 failures elsewhere |
| Full-parity gates | Both configurations fail; incomplete coverage and failed/unavailable categories retained |

The first aggregate attempts lacked the source inventory required by four
fixture-dependent stages. After generating that unchanged inventory, **all 51
stages were rerun in both configurations**. Initial logs remain separate; their
missing-input failures are not relabeled as successful executions. The final
full-parity reports were regenerated after both complete reruns.

Debug/HTTP-origin browser modes, MPFR and performance were not rerun in this
checkpoint. Their missing current evidence remains blocking; historical reports
are not silently reused as current results.

[Hosted run 35921643226](https://github.com/wieslawsoltes/netDxf/actions/runs/35921643226)
at `688b6d0` passed all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs.
Each ran the new section corpus, both existing codec corpora, **69 focused tests**
and **all 3,619 mirrored original cases**. Combined coverage is 2,587 scenarios
and 36,375 executed commands/operations, with the existing 40 constructor
rejections separately recorded. Rejected constructors do not fabricate command
results. The mirrored subset is not the full 35,309-case original suite.

All four downloaded archives matched GitHub SHA-256 digests. The
[committed receipt](transport-sections-hosted-688b6d0.json) retains all twelve
actual comparison reports and original-suite metadata/counts/hashes. Original
result identities match the local suite and unchanged C# names without duplicates.
Original arrays remain in the identified archives and downloadable validation
bundle. Passing this focused matrix does not qualify the broad failing port.

Runtime fingerprint:
`e361b74aba74801f7fb2ef6355a3c933ff1a2e7b1b239e393ae749e27c93f3d2`.
POSIX verifier:
`8fa110df4ebc10dcc253bd3534586d620bcc90cb584c7f52b5c83f31c012d1ee`.
Windows verifier:
`738d6abb5ca67c61545531de4ed9404ebc0173454fde2a5e6f95c024b423916a`.
Both verifier hashes were independently reproduced from identical source bytes;
the existing host-path sort explains their difference. Actual reports are not
rewritten. Documentation changes do not alter these executable fingerprints.

## Remaining work

**416/510 library mirrors (94 missing), 66/193 original conformance-file mirrors
(127 missing), and 3,619/35,309 original cases (31,690 missing).** Presence is not
exhaustive public member/signature or behavioral qualification.

Release retains the existing 26 concrete-dimension, six LEADER, eight block,
three coordinate and 128 entity operation differences. GTE retains twelve
polynomial operation differences and 32 unavailable native scenarios in Release,
or 38 unavailable scenarios in Debug. Debug also retains the original six
dimension, twelve tolerance and four layout assertions and one cubic Bezier
NaN-sign difference. Both casing stages reject the local globalization profile
before pairwise comparisons. None of those failures is waived.

The Release browser's 119 failures are 87 prior-model digest differences and 32
unavailable GTE native observations. All 1,165 new section scenarios match. The
browser count is not a claim that Node's polynomial differences were fixed.

Complete typed reader/writer dispatch, DxfDocument Load/Save/SaveAtomic,
resource/reference reconstruction, all private-schema IO, general version
conversion, remaining original tests/examples and broad host/numerical/performance
acceptance remain unfinished. No AutoCAD open/AUDIT/save/reopen fidelity is claimed.
No original C# or fixture change, merge, force push, expected-failure waiver or
npm publication occurred. PR #98 remains a draft.
