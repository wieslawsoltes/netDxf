# Display-model checkpoint — 5b63748

This checkpoint advances `5de8db8a8b804fcba294eccfb4fac38a68ca59a7` on PR #98's
`codex/javascript-port` branch. The original C# baseline remains
`3496ab91893a1e4ec9261b4833479f1799149cdc`; no original C# file, shared DXF fixture,
or support asset was changed. The package is private and the PR remains a draft.

## Published implementation commits

| Commit | Change |
| --- | --- |
| `3423fab327d549d1c0f768c5a3ed1ff361b57c90` | Binary64 angle storage for Text and Shape, with cold/warmed regressions |
| `be318845182fd391c9cafe0b2b6173c78da71804` | Underlay, abstract definition and three PDF/DGN/DWF definition mirrors |
| `5b63748682877ea0ddaee493547d3cd41515a059` | ImageDefinition, ImageDefinitionReactor, Image and Wipeout mirrors |

The implementation tree is `461ab96ccd9649be3f2f606e7c918f3d8d352144` and matches
the committed local bytes. All three commits were published as non-forced
fast-forwards; no merge or npm publication was performed. Documentation-only
follow-ups do not change the executable or verifier fingerprints below.

The starting tree was restored from CI artifact `10518130689` (run `35267676912`),
ZIP SHA-256 `eda78f766eb350aabf3677ce3da02e4e1b745c7b07101548202e089b8dbc3195`,
and reproduced GitHub tree `b09ba6c4712d367fad277cb45f3818994271cf82` exactly.
That restores committed work; it is not a claim that unavailable uncommitted
files from another session were recovered.

## Added behavior and accounting

Nine original-path source mirrors cover detached underlay and raster definitions,
entity placement, clipping, reference-change events, validation, transforms and
deep clones. No referenced PDF/DGN/DWF/image file is opened, decoded or rendered.
See [underlay contracts](UNDERLAYS.md) and [raster contracts](RASTER_IMAGES.md) for
source-specific distinctions, host paths, ambiguous overloads and remaining
registered-document boundaries.

Two hosted Chromium 152 failures at the starting checkpoint involved a NaN sign
changing in mutable numeric Text/Shape angle fields. Private binary64 storage
preserves both raw constructor state and normalized setter/clone state. The
unchanged strict browser comparisons now pass in Release. No expected result,
comparison tolerance, original case or source fixture was changed to hide them.

The new underlay corpus contributes **510 scenarios / 2,403 operations** and the
raster corpus **354 scenarios / 1,881 operations**: **864 scenarios / 4,284
operations** added to the existing required entity and browser corpora. Their
expected values come from the actual pinned production C# assembly. Twenty-five
supplemental tests were added (two angle, eight underlay, fifteen raster tests).
They do not increase original-test coverage. The 2,551 original case identities
are unchanged from the starting checkpoint; no shortened original test body is
registered as a completed port.

## Completed hosted verification

[Run 35277523978](https://github.com/wieslawsoltes/netDxf/actions/runs/35277523978)
executed code commit `5b63748`. Release, Debug and Windows artifacts were downloaded,
SHA-256 checked and inspected. Their runtime and platform-specific verifier
fingerprints were reproduced independently from the committed file bytes.

**The Linux Release implemented-scope job passes**, with no verification problems.
This is not full-port completion. The Debug, Windows composite and full-port jobs
still fail for the explicit reasons below.

| Check | Actual result |
| --- | --- |
| Complete pinned .NET suite | 35,309 passed in each Debug and Release configuration |
| Mirrored original JavaScript tests | 2,551 passed in each configuration; no unexpected identities |
| Supplemental tests | 252 passed on Linux and Windows; no failing, skipped or TODO cases |
| Entity differential / Linux | 2,429 scenarios / 16,241 operations; zero mismatches in both configurations |
| New underlay and raster categories / Windows | All 864 scenarios / 4,284 operations pass |
| Entire entity differential / Windows | All 2,429 scenarios / 16,241 operations executed; two existing Shape result mismatches remain |
| Style / LIN / SHX | 541 scenarios / 3,585 operations / 112 exact text comparisons; zero mismatches in Linux Debug/Release and Windows Release |
| Hatch / PAT | 376 scenarios / 2,559 operations / 174 exact text comparisons; zero mismatches in both Linux configurations |
| Fixed foundations | 5,185 scenarios / 49,421 operations; zero mismatches in both Linux configurations |
| Exact randomized geometry | 2,000 comparisons; zero mismatches in both Linux configurations |
| Reference / additional math | 61,876 / 30,904 exact comparisons; zero mismatches per Linux configuration |
| Independent development reference | 28,296 finite-bit and 2,608 NaN-class results agree with MPFR at both 512 and 1,024 bits |
| Raw / handle / OBJECTS bytes | 8,263 / 1,140 / 1,068 exact emitted-byte comparisons; zero mismatches per Linux configuration |
| Detached lifecycle | 523 scenarios / 15,563 operations / 256 byte comparisons; zero mismatches per Linux configuration |
| Filesystem / Linux and Windows | 1,782 comparisons / 1,686 exact byte comparisons / 399 fixtures; zero mismatches |
| Native Windows host | 5/5 integration tests passed; all 82 original raw/helper atomic-save cases passed |
| Offline package installation | Passed: 260 files on Linux, 262 including built host/metadata on Windows |
| Real Chromium 152.0.7977.0 / Release | 102,394 comparisons passed in each HTTP-origin and inline-ESM run; no page errors |
| Real Chromium 152 / Debug | All 102,394 comparisons executed in each mode; the single pre-existing Bézier NaN-sign result remains |
| Globalization / pinned Linux CI | 3,045 comparisons pass in both configurations |

### Remaining exact failures

Debug `BezierCurveCubic/CalculateTangent/double/5` returns positive quiet-NaN
components in JavaScript where the Debug oracle returns negative quiet-NaN
components. It remains visible in the 4,254-case baseline geometry runner and
both browser modes. It is not an allowed failure and still fails those gates.

Windows `display/seeded/7` retains two differing operations: the Shape snapshot
and subsequent Clone result. In the snapshot, size is expected
`400D34C77C0646A0` but actual is `400D34C77C06469F`; width factor is expected
`BFF1821F0CBD79EA` but actual is `BFF1821F0CBD79EB`. Clone retains the size
difference; its source-defined width-factor omission still matches. These are
one-bit last-place differences, not accepted approximations. The full
counterexample records are **identical** to those in starting-commit Windows
artifact `10517720281` (SHA-256
`302e8a49c2329e63d1566f0f1eac06c1472d3e467af049f20ed2b93f1ca7ec11`). Thus the
new underlay/raster categories introduce no observed Windows mismatch, but the
whole Windows job is not passing. The underlying platform-sensitive arithmetic
cause is not yet independently localized; no test-specific numerical adjustment
was introduced.

## Local execution, separate from hosted qualification

The exact pinned .NET 8.0.425 SDK and 8.0.31 runtime were available locally.
All 35,309 original C# cases were rerun in both configurations, as were the exact
entity, style, hatch, raw, handle, OBJECTS, lifecycle, filesystem and numerical
regressions. All 2,551 original JavaScript tests, all 252 supplemental tests, and
the 260-file offline package check passed locally. Source fingerprints confirmed
that original inputs had not drifted.

The local ICU profile is correctly rejected by the globalization contract; it
was not relabeled as the pinned CI environment. Local HTTP navigation reports
`ERR_BLOCKED_BY_ADMINISTRATOR`, so the HTTP-browser qualification above is from
actual hosted CI, not a local mock or substituted transport. Independent local inline ESM execution in Chromium 144.0.7559.96 ran all 102,394
comparisons in each configuration: Release passed with no page errors; Debug
retained only the existing Bézier NaN-sign result. This separate mode does not
erase the HTTP-host restriction.

## Completion ledger and boundaries

**182/510 library mirrors** are present (328 missing), **28/193 conformance-file
mirrors** are present (165 missing), and **2,551/35,309 original runtime cases**
are implemented (32,758 missing). Presence is not a complete member, signature,
semantic or interoperability audit. The new underlay/raster cases are deliberately
kept separate from this original-test ledger.

Complete typed `DxfDocument`, registered ownership/collections, automatic reactor
linking, the remaining entities/APIs, typed DXF reading/writing, original examples
and tests, platform profiles, and performance acceptance remain unfinished.
Native AutoCAD validation and arbitrary external PDF/DGN/DWF/raster rendering are
not established. The existing reference-math implementation, license notices and
preferred sources were not changed. The independent whole-source translation
probe is still unsuccessful and is not counted as port coverage.

## Reproducible proof

Runtime fingerprint (both native path-order recipes):
`487a7669af41f0e0bfa2121078a0c612a6cba6c9016dcf18f2f8b5625962f507`.

Verifier fingerprints (native paths are sorted before separator normalization):

- POSIX: `442066d4f17cc7502bb0df371b76bb6250e92aaf1e6d5ec71de5e617ba8af06b`.
- Windows: `5613e3a91468641965c174ec898378584bae6a2c4ef5a0715e23f265d37e2802`.

Downloaded code-commit artifact ZIP hashes:

- Release `10521527734`: `347ccf59c3652b00e41d6dbd1d383c689714a52ca9b9e9a0e77dc3d6bdd8e9a0`.
- Debug `10520754991`: `01791980baa704298ad046c82ec9f6aea010b7f904f26b20d030503793ffc6c0`.
- Windows `10521042516`: `b3ff7402368091d77231dea0e169ab5302a48a727f0f9755e62f9c71a753a507`.

The documentation-only follow-up preserves these executable and verifier bytes;
a new documentation-triggered workflow is not substituted for this completed run.
