# Typed document ownership and database lifecycle

This is the historical `6665f8c` checkpoint. The later
[registered annotation continuation](REGISTERED_ANNOTATIONS.md) admits MULTILEADER
and SECTION and supersedes the corresponding rejection and coverage statements
below; the source-bound results here remain historical.

Initial core: `027e61974ea683321beff527b840b826dcc21b4f`.
Expanded executable checkpoint: `6665f8cbca3ccf54f8f23a1d15db7fa4aa050978`.
Executable tree: `c44fb7671a30ad8fe6d59f0d15e61c35a3f30a36`.
C# baseline: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This is a real in-memory typed authoring and registration engine, **not complete
DxfDocument parity or typed DXF transport**. Original C# sources, original tests
and shared fixtures are unchanged. No previous uncommitted typed-document patch
survived in the available files; the implementation was reconstructed from the
pinned sources, not described as recovery of unavailable bytes. Uploaded file
trees were checked against the local committed trees before each fast-forward.

## Document and table ownership

DxfDocument initializes the source's default tables, active viewport, layer-state
manager, model-space block/layout, classes and raster variables. Document and
resource handles are allocated by the production registry, not precomputed by
test adapters. A default document has handle `0` and next handle seed `22`.

The registered table classes retain their original relative paths and exported
names. They canonicalize resources, count references, track owners, reject
referenced removals and handle rename/dependency events. The public collection
properties that have no setters remain read-only. Some internal/protected C#
hooks are explicit JavaScript methods; this is not CLR access-control emulation.

DrawingEntities uses the current layout's block. Admitted typed entities can be
added, resolved by handle and explicitly removed. Blocks and INSERT attributes,
groups, paper-space layouts, dimension blocks, layer/linetype/text/style resources
and XData registries share the pre-existing models. Removal releases registration
and references without substituting a raw-document object graph.

The named-object database is lazy. Its initialization during ordinary removal is
also retained because the C# dependency scan makes that allocation observable.
The initial core's 53 scenarios / 450 operations remain in the expanded corpus.

```js
import {
  DxfDocument, DxfVersion, Line, Vector3, Layer,
  DxfDictionary, DxfPlaceholder,
} from './javascript/index.js';

const document = new DxfDocument(DxfVersion.AutoCad2018);
const line = new Line(Vector3.Zero, new Vector3(10, 5, 0));
line.Layer = new Layer('Design');
document.Entities.Add(line);
console.assert(document.GetObjectByHandle(line.Handle) === line);
console.assert(line.Layer === document.Layers.get_Item('Design'));

const extension = new DxfDictionary();
extension.Add('Metadata', new DxfPlaceholder());
document.Objects.SetExtensionDictionary(line, extension);
document.Objects.EraseOwnedTree(extension);
console.assert(line.ExtensionDictionary === null);
document.Entities.Remove(line);
```

## Database registration, cloning and erasure

DxfObjectDatabase registers dictionary ownership graphs, extension dictionaries,
known schema references and XData. It reserves exposed unresolved handles before
allocating an adopted graph and validates reciprocal ownership/registration.
Items returns a read-only snapshot rather than a second mutable registry.

Clone, CloneObject, CloneExtensionDictionary and CloneSun use explicit
reference-identity maps. Internal descendants and handles are remapped; external
cross-document references require destination mappings. Source-owner mapping
conflicts, occupied slots and invalid source schemas are rejected. Enumerable
mapping callbacks are evaluated before the clone's live-source check, preserving
the source's re-entrant failure behavior.

EraseOwnedTree preflights the complete ownership subtree and retained incoming
reference carriers before mutation. Dictionary aliases, extension attachments,
SUN owner slots, XRECORD pointers, typed database references, persistent/entity
reactors, XData 1005, custom header handles and non-registry attribute/layout
viewport carriers are checked. Numeric sort keys remain values, not pointers.

Successful erasure removes registrations, releases APPID bookkeeping and marks
objects erased while preserving their handles as tombstone identifiers. Reusing
erased objects is rejected. The owning root is detached; relationships inside
the erased graph remain inspectable. Traversal handles the original 2,048-level
ownership test without recursive stack traversal. Opaque or specialized graphs
that need an unimplemented schema lifecycle are rejected rather than erased
unsafely.

## Specific object lifecycles

SORTENTSTABLE authoring includes DxfSortOrderEntry, unique entity membership,
block ownership checks, source version restrictions, ACAD_SORTENTS placement and
optional regeneration flags. The `$SORTENTS` header uses its boxed Int16 value;
a malformed existing header is rejected before graph attachment.

Spatial filters create ACAD_FILTER/SPATIAL ownership and reciprocal reactors.
Named plot settings clone the input settings and validate their schema. Wipeout
variables update the existing object instead of reallocating it. GEODATA attaches
to a registered block record's extension dictionary. SUN attachment and cloning
preserve reciprocal host ownership and source version/profile restrictions.

BlockRecord, Insert, VPort, View, Viewport and DxfDocument register their actual
class identities with the schema validators. This fixes previously unbound type
checks rather than relaxing them.

## Layer states and explicit file hosts

LayerState, LayerStateProperties and LayerStateManager implement snapshots,
selective restore/update, rename/remove and independent clones. The source's
CopyFrom flag-reset behavior and clone PaperSpace reset are preserved. Portable
ToLasString/LoadText are explicit JavaScript conveniences; file Save/Load use
Node adapters rather than filesystem access from the browser entry.

SupportFolders and LAS/LIN host APIs are implemented with explicit platform
adapters. The LAS corpus compares actual C# file output with JavaScript text using
the host newline supplied as an input, not by rewriting observed outputs. The
finite LAS cases do not establish exhaustive malformed-input, file-locking,
Windows device-path or arbitrary System.IO compatibility.

## Tests and evidence

The input-only ownership corpus contains **188 scenarios / 8,130 operations**,
including the original 53 scenarios and 32 deterministic randomized registration
sequences. Separate C# and JavaScript controllers observe real APIs, registry
order, handles, ownership, reference counts, errors, model snapshots and LAS
output. No expected native result is embedded in production code. Crashed or
malformed oracle responses remain failures; the transport restarts for subsequent
scenarios instead of fabricating results.

**61 complete original TypedObjectErasureTests cases** were added to the mirrored
suite. Their identities are checked against the unchanged original suite. Cases
requiring unfinished typed IO, independent fixtures or MULTILEADER adoption are
not counted, shortened or marked as successful. The separate 41 new focused
tests are supplemental and do not inflate original-case coverage.

The new stage is required by run-qualification.mjs and verify.mjs. All 188 browser
inputs are appended after the previous comparisons; both browser modes require
at least **140,719 comparisons**. The offline package smoke test uses only the
installed barrel and standalone imports. A read-only GitHub workflow tests Ubuntu
22.04 and Windows 2022, each in Debug and Release, including the exact corpus,
41 focused tests and 61 original erasure cases.

### Completed local results for 6665f8c

SDK 8.0.425, .NET 8.0.31, Node 22.16.0, Debian 13 x64, September 22, 2026:

| Check | Observed result |
| --- | --- |
| Ownership differential, Debug and Release | Each: 188 scenarios / 8,130 operations; zero mismatches or unavailable native observations |
| Full unchanged C# suite | Each configuration: 35,309 passed, zero failed |
| Full original JavaScript suite | 2,881 passed; no unexpected original identities |
| Supplemental JavaScript tests | 867 passed; no failures, skips or TODOs |
| Offline-installed package | Passed; **487 files** |
| Source-derived foundations/dimensions | Exact regeneration passed |
| Release inline Chromium 144.0.7559.96 | All 140,719 comparisons executed; no page errors, unavailable source observations or new ownership mismatches; 83 previous-category failures remain |
| HTTP-origin Chromium | Failed: ERR_BLOCKED_BY_ADMINISTRATOR during local-origin navigation |
| Full-port verification, both configurations | Failed; missing, stale and failing categories retained |

The package report records 487 files. The executable commit message's 488-file
figure was a transcription error, not a different successful package run.

Runtime fingerprint:
`0ef1e9301168259bc0beead91465220a7c19c932680c676cf782bb7037e6d369`.
POSIX verifier:
`40f0048ac869afcbe2c05e4323a64e9f0d1a1e1cd34e50f3a043727846f9d639`.
Source fingerprint:
`97bf956b156b644901333ca312556f389cf02b8cbabba3ac16198d7c1b46fb9d`.

From `javascript/`, select the pinned source and toolchain, then run:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs geometry
npm run test:document-ownership
npm test
npm run test:unit
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

### Completed hosted ownership matrix

[Run 35780494490](https://github.com/wieslawsoltes/netDxf/actions/runs/35780494490)
at `6665f8c` passed all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs.
Each ran 188 scenarios / 8,130 exact operations, 41 focused supplemental tests,
and 61 filtered original erasure cases. All four artifact ZIPs were downloaded,
hash-checked against GitHub's digests and inspected.

The [hosted receipt](document-ownership-hosted-6665f8c.json) retains the actual
ownership result documents, original-test metadata, counts and file hashes.
The full original-test result documents remain in the hash-identified archives;
the filtered run is not relabeled as the full original suite. Runtime fingerprints
match across all four profiles. The existing host-path sort produces Windows
verifier `fb44c0934073d8311e0a8b417d70b9b7877c614fd81e05051568e681f597e071`;
the actual reports are retained without rewriting that platform-specific value.
Passing focused jobs do not qualify unrelated full-port categories.

## Remaining scope and ledger

**358/510 library mirrors (152 missing); 54/193 conformance-file mirrors (139
missing); 2,881/35,309 original cases (32,428 missing).** Source-file presence is
not exhaustive member/signature or behavioral qualification.

Typed DXF reading/writing, Load/Save/SaveAtomic transport and complete version
conversion are not supplied by this document layer. DxfRawDocument remains a
separate unchanged transport API, not a replacement for those typed operations.
MULTILEADER, SECTION, ACAD_TABLE and retained polyline-record adoption still need
complete registered lifecycles and are explicitly rejected by this checkpoint.
Other missing database partials, APIs, examples and original tests remain work.

The 83 inline-browser failures comprise 64 entity, 12 concrete-dimension, four
block, two leader and one coordinate scenario. Other standalone categories not
rerun against this exact executable tree remain unavailable or stale, not green.
Debug browser, broad numeric/platform/filesystem guarantees and performance
acceptance are also incomplete. No source/fixture change, tolerance, expected-
failure waiver, merge, force push or npm publication was used. PR #98 stays draft.
