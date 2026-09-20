# Completed hosted HATCH checkpoint — b8b1931

This supplements [HATCH entity and boundary contracts](HATCH_ENTITIES.md) with completed executable evidence for code commit `b8b1931734ae6320055cfc324b389ab60450d544`, tree `920770e57b4537aa4a4a84fb337d1a39781fa99d`. The evidence belongs to [Actions run 35500670795](https://github.com/wieslawsoltes/netDxf/actions/runs/35500670795), not to a later documentation-only workflow. **Overall implemented-scope and full-port qualification remain failing.**

The Release, Debug and Windows artifact ZIPs were downloaded, SHA-256 checked and inspected. Runtime and platform-specific verifier fingerprints were reproduced from the exact committed source bytes. The differing POSIX/Windows verifier digest reflects native path sorting in the existing fingerprint function, not different test sources.

## Observed results

| Check | Linux Release | Linux Debug | Windows Release |
| --- | --- | --- | --- |
| Complete original C# suite | 35,309 passed | 35,309 passed | Not this lane's scope |
| Mirrored original JavaScript cases | 2,782 passed | 2,782 passed | 82/82 raw atomic-save cases |
| Supplemental JavaScript cases | 479 passed | 479 passed | 479 passed |
| New HATCH entity coverage | 1,075 scenarios / 20,990 exact operations, zero differences | Same, zero differences | Same, zero differences |
| Full entity differential | 7,391 scenarios / 57,725 operations, zero differences | Same, zero differences | All execute; 395 existing differing outputs |
| Surface differential | 1,066 scenarios / 17,258 operations, zero differences | Same, zero differences | Same, zero differences |
| Ordinal casing | 3,045 comparisons pass | 3,045 comparisons pass | Not this lane's scope |
| Offline installed package | 352 files; passed | 352 files; passed | 354 files including native host; passed |
| Chromium 152.0.7977.0 HTTP | All 124,216 comparisons execute; one existing UCS scenario fails | All 124,216 comparisons execute; one existing Bézier scenario fails | Not this lane's scope |
| Chromium 152 inline native ESM | Same UCS result, all comparisons execute | Same Bézier result, all comparisons execute | Not this lane's scope |

Every hosted browser report has an empty page-error list. All 1,075 HATCH scenarios are included and none is a reported mismatch in either browser mode or configuration. `completed: false` remains in the failing browser reports; successful execution of every input does not turn a result mismatch into a pass.

Windows additionally passes all five native-host integration cases, 1,782 real filesystem comparisons, 541 style scenarios / 3,585 operations and the new installed-package HATCH checks. Its combined styles/entities step still fails because the full entity stage fails. The 395 differing entity outputs remain in the existing categories: 284 HELIX, 77 curves, 32 ellipse and two seeded display outputs. The separate exp/log stage retains 269 differences across all 13,512 comparisons. The new HATCH categories have zero differences; the platform as a whole is not qualified.

The Linux aggregate reports identify only the retained Release coordinate failure or Debug geometry failure, plus their HTTP and inline browser manifestations. Other required comparison categories pass. Hosted casing passes on its documented reference platform; the rejected local ICU profile and local HTTP navigation restriction remain accurately recorded as local limitations in the contract report, not as hosted failures.

## Remaining exact failures

Release: three differing observations of `coordinates/normal/115`, represented by browser scenario `coordinates/coordinates/normal/115` in both modes.

Debug: `BezierCurveCubic/CalculateTangent/double/5`, also failing both browser modes.

Windows: the existing 395 entity outputs and 269 exp/log outputs described above. No numerical tolerance, allowlist, fixture rewrite, expected-output correction or removed scenario was introduced to hide any of these results.

## Artifact identities

| Artifact | ID | Downloaded ZIP SHA-256 |
| --- | --- | --- |
| Release | 10602856072 | `aa9e6e3774e06bd68a24ba6d2eafd16123cdd3fc9abd92c6492cd8435b963930` |
| Debug | 10602975086 | `7d2fe86e5335c69374d02d6aa919663f0280b27a5ab313630691ea61e7870535` |
| Windows | 10602022357 | `49932274645fd212ba16536ee19858b48527fe19959828a38d0be74e47b3fc36` |

Runtime fingerprint: `4131bb6f6406d13e3eb4476a82bef791574f1379646146ad4115c73f5cba1e4b`.

POSIX verifier: `e2c22371209834c5723ac6cb9ff9fae551cfa54e1134da7a4f7702faa0e92cef`.

Windows verifier: `d05d299b2b36c81144be33a962c12239ba60a7401ff1ae5ca0acee8c4c5afc68`.

## Full-port status

The current ledger is **246/510 library mirrors, 50/193 conformance-file mirrors and 2,782/35,309 original cases**. There are **264 library paths, 143 conformance paths and 32,527 original cases missing**. File presence is not complete API or behavioral equivalence. Typed document/registration/transport work, remaining APIs and original tests/examples, exact platform results and performance acceptance remain unfinished. PR #98 stays a draft and the package remains private; no merge or npm publication occurred.
