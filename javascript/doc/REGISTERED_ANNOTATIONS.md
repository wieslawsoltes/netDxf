# Registered MULTILEADER and SECTION lifecycles

Executable checkpoint: `361b89536bc640ea6efd2e7c84cf5020342612e9`.
Tree: `d3bafe8015b60bd6af532c42f45314b8f39ede7f`.
CI prerequisite correction: `545107527fa336c8702084e6bb367a2d275cd806`.
C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.
SDK 8.0.425 / .NET 8.0.31; Node 22.16.0.

This extends the typed in-memory document introduced at `6665f8c`. It does not
provide typed DXF transport or establish full port parity. The previous source
archive was restored with its checksum and complete file tree verified; no
unpublished local changes were found. Original C# sources, tests and shared DXF
fixtures remain unchanged.

## Registered MULTILEADER

The document now admits MULTILEADER after its existing incoming validation,
including source version restrictions and same-document reference checks. The
stored values, text/block components and exact transform restrictions continue
to come from the pre-existing detached models; this is not an alternate model.

`AddMLeaderStyle(name, style)` validates a detached style and its references,
uses the ACAD_MLEADERSTYLE dictionary, rejects occupied/incompatible entries and
preserves detached ownership when the new dictionary cannot be adopted.

`DxfDocument.MultiLeader.js` implements the source's live reference scan. Uses
are counted by object identity and multiplicity, including combined graphics and
component references. Removing a referenced style, text style, linetype or block
cannot be enabled by passing a same-name object from another document. The
existing callback-aware table rename logic is now exercised with real registered
MULTILEADER references.

The `MLeaderData.RegisteredDocument` lookup no longer accesses the lazy Objects
property while identifying an owner. Merely inspecting registration must not
consume a named-object handle. Registered child mutations continue to reject
foreign references before assigning a component's parent.

## Registered SECTION and SECTIONOBJECT

Both source spellings are admitted under their qualified profiles. Validation
runs before registration, including nested blocks reached through INSERT and
explicit dimension blocks. Existing named blocks preserve the source's adoption
short-circuit. Stored/private entity families outside this checkpoint remain
rejected rather than losing backing records.

`SetSectionSettings` attaches detached settings with reciprocal ownership.
`SectionReferencesRemoval` prevents ordinary entity/block/layout removal from
invalidating owned settings, settings-source geometry, VIEW live-section slots,
reactors, XData handles, XRECORDs, exposed opaque handles or custom headers.
The source's lazy database initialization during the removal scan is retained.

`CloneSection` plans an ownership graph with detached copies before registration.
It preserves dictionary aliases, repeated/null/self-referential sources,
reciprocal owners, XData, reactors and exposed handle remapping. Same-document
resources remain shared by identity. Cross-document resources and external
geometry require explicit mappings. Mapping conflicts, wrong target types,
invalid source graphs and numeric handle exhaustion are rejected. It does not
invoke public Layer/Linetype Clone overrides to manufacture destination resources.
Caller-provided mapping enumeration precedes the live-source recheck, matching
the C# callback behavior.

`EraseSection` checks retained incoming references and the owned database subtree
before changing registrations. Successful erasure releases APPID bookkeeping,
removes the section from its owner, and preserves erased handles as tombstone
identifiers. Internal erased ownership remains inspectable; external geometry
and caller event subscriptions remain intact. Erased sections cannot be adopted
again. Unsupported opaque/private owned graphs stay rejected.

```js
import {
  DxfDocument, DxfVersion, DxfMLeaderStyle, MultiLeader,
  Section, DxfSectionSettings,
} from './javascript/index.js';

const document = new DxfDocument(DxfVersion.AutoCad2018);
const style = new DxfMLeaderStyle();
style.Properties.TextStyle = document.TextStyles.get_Item('Standard');
document.Objects.AddMLeaderStyle('Annotations', style);

const leader = new MultiLeader();
leader.Properties.Style = style;
leader.Properties.TextStyle = style.Properties.TextStyle;
leader.Properties.LeaderLinetype = document.Linetypes.get_Item('Continuous');
leader.Properties.ContentType = 0;
document.Entities.Add(leader);
leader.Validate();

const section = new Section();
document.Entities.Add(section);
document.Objects.SetSectionSettings(section, new DxfSectionSettings());
const copy = document.Objects.CloneSection(section, section.Owner);
document.Objects.EraseSection(copy);
console.assert(copy.IsErased && document.GetObjectByHandle(copy.Handle) === null);
```

## Independent verification and original cases

The new input-only corpus contains **112 scenarios / 6,633 operations**. It
covers registration profiles, three leader content variants, dynamic and foreign
references, style adoption/cloning, nested-section preflight, owned/unowned section
cloning and erasure, cross-document maps, metadata channels, failure ordering and
24 deterministic randomized leader-reference sequences. The unchanged native
assembly and production JavaScript are observed independently. Native process or
transport failures remain failures, not invented expected results.

The observation harness adds explicit internal-fixture adapters for opaque
objects and loaded XRECORD tags, plus source-compatible null-target observations.
These adapters do not implement document behavior. No comparison output is
normalized, no tolerance is added, and no previous corpus is removed/reordered.

**24 complete original cases** are newly ported: 20 MULTILEADER cases, three
SECTION cases and one erasure case for a leader in an otherwise unused block.
The original identities are checked against the full unchanged native suite.
Cases requiring typed IO or producer fixtures are not shortened to an in-memory
prefix. **19 additional focused tests** are supplemental, not original identities.

The new category is mandatory in aggregate verification. Browser inputs are
appended after all earlier corpora, raising the required minimum to **140,831**.
The installed-package smoke test covers both annotation families. The existing
ownership workflow now runs both ownership corpora, 60 focused tests and the
entire mirrored original JavaScript suite on Ubuntu/Windows, Debug/Release.

## Completed local results at 361b895

Linux x64, September 22, 2026. These results describe the executable tree above,
not a claim that every unrelated category or subsequent hosted run passed.

| Check | Observed result |
| --- | --- |
| Registered annotations, Debug and Release | Each: 112 scenarios / 6,633 operations; zero mismatches or unavailable native observations |
| Existing document ownership, both configurations | Each: 188 scenarios / 8,130 operations; zero mismatches |
| Existing detached multileaders, both configurations | Each: 1,030 scenarios / 8,104 operations; zero mismatches |
| Full unchanged native C# suite | Each configuration: 35,309 passed, zero failed |
| Additional Release classic LEADER check | 639 scenarios / 7,221 operations executed; six mismatching operations in two scenarios, no unavailable observations |
| Full mirrored original JavaScript suite | 2,905 passed; no unexpected original identities |
| Supplemental JavaScript suite | 886 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; 490 files |
| Release inline Chromium 144.0.7559.96 | All 140,831 comparisons executed; no page errors, unavailable source observations or new annotation mismatches; 87 failures in other categories |
| Full-port verification, both configurations | Failed; missing, stale and failed evidence remains blocking |

Runtime fingerprint:
`26fe9895139fc627b2198ce31705d7237e35b61a008bdc5a2b1bab609319ced3`.
POSIX verifier:
`f0d4f5026fe7ac774733fb396afea1f712bb932e2a0e71c27b22fe5c1c6e68c6`.
Original-source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

The six standalone classic-LEADER operation differences are exact binary64 NaN
sign differences in the transformed Text annotation height. They are retained
as failures, not normalized. This observation does not by itself establish the
cause of the additional browser failures.

The HTTP-origin and Debug browser modes, other standalone differential categories,
source-generator execution and performance acceptance were not rerun in this
continuation. Previous source-bound successes do not satisfy fresh evidence for
this tree. The verifier still checks the pinned source and committed generated
file hashes, but those checks are not a new generator execution.

The browser failures comprise 64 entity, 12 concrete-dimension, six classic-LEADER,
four block and one coordinate scenario. This is four more classic-LEADER failures
than the prior 83-failure checkpoint; those outcomes remain in the report, not
waived or attributed to a cause without separate investigation.

## Completed hosted matrix and corrected CI prerequisite

[Run 35787377633](https://github.com/wieslawsoltes/netDxf/actions/runs/35787377633)
at `5451075` passed all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs.
Each completed both independent corpora (188 / 8,130 ownership and 112 / 6,633
annotation scenarios/operations), all 60 focused tests, and all **2,905 mirrored
original JavaScript cases**. This is the full mirrored suite, not the earlier
61-case erasure filter. It remains a subset of the 35,309-case original C# suite.

The first expanded run at `361b895` passed both differential stages and focused
tests on Windows, but its full-suite step rejected 37 raw atomic-file operations
because the workflow had not built the existing optional Windows replacement
bridge. The safe runtime rejected replacement rather than using a destructive
fallback. Commit `5451075` adds the same bridge-build prerequisite already used
by the primary filesystem workflow. No runtime, verifier or test changed; no
filesystem case was filtered out. All four complete jobs then passed.

All four successful artifact archives and the initial Windows Debug failure
archive were downloaded, checked against GitHub SHA-256 digests and inspected.
The [hosted receipt](registered-annotations-hosted-5451075.json) retains all eight
actual differential reports, full-suite original-test metadata/counts/hashes,
archive identifiers and the corrected-attempt record. The full original-result
arrays remain in the hash-identified archives and downloadable validation bundle.

Runtime fingerprints match across the four hosts. The existing verifier's
host-path sorting produces Windows fingerprint
`feb7f030011e162c92e7bb2f7ccc248c9b6cfe3793b40293a283a9931c05ecf0`;
it was independently reproduced from the same source bytes. The actual hosted
reports are retained without rewriting that platform-specific value. The CI-only
correction and this documentation do not change executable fingerprints.
These focused successes do not qualify the failing aggregate or all platforms.

## Remaining parity work

**361/510 library mirrors (149 missing); 56/193 conformance-file mirrors (137
missing); 2,905/35,309 original cases (32,404 missing).** File presence is not a
complete member/signature or behavioral audit.

Typed DXF reader/writer, Load/Save/SaveAtomic integration, general version
conversion, ACAD_TABLE and retained polyline-record adoption, specialized private
schemas (including SECTIONMANAGER), missing public APIs, original tests/examples
and cross-platform numeric/filesystem/performance qualification remain unfinished.
The original SECTION settings model does not itself generate native CAD section
geometry. A finite passing annotation corpus is not exhaustive AutoCAD parity.
PR #98 stays draft; no merge, force push or npm publication was performed.
