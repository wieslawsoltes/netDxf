# Stored TABLESTYLE and CELLSTYLEMAP

Runtime checkpoint: `14d8727902303f4dcb1fc6a5e8368ce6b3aedb35`.
Executable verification checkpoint: `7b2249b72796d81089ce12a84dc855884eb22f43`.
Executable tree: `4070b9cc352edf323accaaaee2abebcfe73e7b9a`.
C# reference: `3496ab91893a1e4ec9261b4833479f1799149cdc`.

This increment implements retained table-style models and their qualified edits.
It does **not** implement the typed DXF reader/writer, complete ACAD_TABLE,
inherited effective formatting, native table regeneration or full AutoCAD parity.
The existing raw transport API is separate and does not fill those typed gaps.

## Recovery and source contract

No surviving unpublished project checkout was found in the available recovery
files. The published source archive plus the preceding section-manager changes
were restored, including ignored tracked fixtures. The reconstructed initial
file tree exactly matched `8086f7d` / `a2cb8a5bd4e837e076bd3d24cd721378af41b4fb`.
All subsequent uploads were checked against the local committed file trees.
Local reconstructed commit IDs differ from GitHub; complete tree equality is
what is verified. Original C# sources, original tests and shared fixtures remain
unchanged.

The behavioral specification is the pinned C# implementation, including its
retained/private-schema restrictions, validation ordering and snapshot behavior.
The public group-code background is documented in Autodesk's
[TABLESTYLE reference](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-DXF/files/GUID-0DBCA057-9F6C-4DEB-A66F-8A9B3C62FB1A.htm).
No claim of undocumented native behavior is inferred from a finite test corpus.

## Original paths and exposed models

The seven mirrored files are DxfTableStyle, DxfTableStyle.Projection,
DxfTableStyle.Borders, DxfTableStyle.DataTypes, DxfTableStyle.Edit,
DxfStoredCellStyleMap and DxfStoredCellStyleMap.Edit under `netDxf/Objects/`.
The package barrel and standalone paths export ten modeled types:
DxfTableStyle; DxfTableStyleHeader; DxfTableStyleRow; DxfTableStyleRowEdit;
DxfTableStyleRowValues; DxfTableStyleBorderValues; DxfTableStyleRowBorders;
DxfTableStyleRowDataTypes; DxfStoredCellStyleMap; DxfStoredCellStyleMapEntry.

The two retained model constructors are internal in C#. JavaScript exposes the
source document, tag sequence and decode callback explicitly for the retained
loader adapter. That language adaptation is not a new typed file parser. Loader
admission constraints that live in the unported DxfReader are not claimed by
these internal constructors. In particular, an internal synthetic construction
is not evidence that every source version accepts that object in a DXF file.

## TABLESTYLE projections

The model retains the complete tag packet. Public projections omit nested 102
application-control groups without dropping their stored tags. Malformed control
groups fail; duplicate AcDbTableStyle subclasses leave public projection
unqualified rather than choosing a conflicting packet.

Header projection requires the source's exact ordered fields. The optional
leading stored-version field is recognized only in the source profiles that
qualify it. Public header requests validate description length/text, flow and
finite nonnegative margins. Internal projection does not silently add edit-only
validation that the original loader constructor did not perform.

Rows are projected only when exactly three public row starts are present. Their
order is retained without inventing a semantic row-role mapping. Scalar values,
all six border slots, and the data/unit pair are independently qualified from
unambiguous fields. Missing or duplicate fields remain unprojected. Signed
stored color, lineweight, data-type and unit codes are retained as values, not
normalized to a guessed native interpretation.

Headers, value objects, row-edit requests, row/tag collections and border
containers are immutable snapshots. STYLE bindings refer to real registered
objects, so a resource rename remains visible while the old stored-name snapshot
is unchanged. Read-only collection adapters preserve indexing, Contains, IndexOf,
CopyTo and enumerator behavior; attempted IList mutation is rejected.

```js
import {
  DxfTableStyleBorderValues, DxfTableStyleRowBorders,
  DxfTableStyleRowDataTypes,
} from './javascript/index.js';

const borders = new DxfTableStyleRowBorders(Array.from(
  { length: 6 }, () => new DxfTableStyleBorderValues(-2, true, 256),
));
const revised = borders.WithBorder(3,
  new DxfTableStyleBorderValues(-32768, false, 32767));
console.assert(borders.Values.get_Item(3).StoredLineweight === -2);
console.assert(revised.Values.get_Item(3).StoredLineweight === -32768);
const data = new DxfTableStyleRowDataTypes(-2147483648, 2147483647);
console.assert(data.StoredUnitType === 2147483647);
```

## Qualified style replacement

ReplaceStyle consumes bounded row-edit requests before committing a replacement.
Each request must identify a distinct row from the current snapshot. Stale and
duplicate requests cannot modify the packet. WithValues, WithBorders,
WithDataTypes and WithTextStyle compose immutable requests while retaining their
original row identity.

A no-op preserves the same header, row and tag containers. Accepted changes
replace only selected tags, reuse untouched tag identities, preserve opaque
payloads and leave previously returned snapshots intact. Exact binary64 equality
keeps negative-zero changes observable. Replacement allocates no document handles.

STYLE reassignment requires the exact already-registered resource from the same
document. Same-named foreign records are not imported implicitly. References
retain order and multiplicity, combining exposed handles with named STYLE
bindings. Rebinding updates current usage without erasing unrelated exposed
handle dependencies. Old References containers preserve their old membership.

The source object, document profile, resource identities and database validity
are checked after caller enumeration and disposal. GetEnumerator, MoveNext,
Current and Dispose failures propagate with source ordering. Even caught
recursive style/map edits invalidate the outer edit. Caller mutations are not
rolled back; the operation must revalidate the state they leave behind.

The shared synchronous managed-enumerable adapter now gives a native-style
NullReferenceException for a null managed enumerator. Native JavaScript
iterables use their return() cleanup hook; this is the explicit language
adaptation, not a claim that JavaScript has CLR interfaces or reflection.

## CELLSTYLEMAP preservation and name edits

DxfStoredCellStyleMap retains the counted entry grammar, ordered/repeated IDs,
signed stored types, nesting checks and uninterpreted format packets. It binds
the exact source owner, ancestry and handle dependencies. Format payloads are
not interpreted as complete private cell-format semantics.

ReplaceEntryNames requires exactly the current entry count and validates every
string, source identity and database state before replacing containers. Duplicate
names and empty names retain the source's behavior. No-op names preserve exact
container identity; edits retain old entry/payload snapshots and share untouched
format tags. Generic cloning and unsupported schema erasure keep the original
explicit rejection rather than discarding the stored payload.

Text validation operates on UTF-16 code units, rejects NUL/unpaired surrogates,
and applies the source's 1,048,576-unit edit limit. Encoded output is bounded too.
Literal backslashes remain literal through DXF escaping; earlier source profiles
encode non-ASCII UTF-16 units separately. The complete retained-record tag budget
includes common metadata, reactors, extension attachment and XData binary chunks.
The shared helper is derived from the original CheckEditableText implementation;
it is not a throwing placeholder counted as a TABLECONTENT source mirror.

## Independent verification

The input-only corpus has **279 scenarios / 5,829 operations**: valid and
ambiguous projections; six-slot borders and stored codes; source-version and
resource guards; no-op/old snapshot identity; stale requests; framing and depth
limits; Unicode and encoded-length limits; callback/disposal/reentry failures;
null enumerators; and 24 deterministic randomized edit sequences.

Test-only C# and JavaScript adapters independently invoke the existing internal
retained constructors. Production APIs perform all mutations. The native decoder
is obtained from the actual unchanged C# implementation, not a reimplemented
expected-result routine. Negative zero is explicitly encoded in input descriptors
because JSON otherwise loses its sign. String observations carry UTF-16 units so
unpaired surrogates are not silently replaced by a JSON serializer. Neither
change normalizes compared outputs or relaxes bit/identity checks.

Two **complete original cases**, `table-style-edit/constructors` and
`table-style-borders/constructors`, are ported with their original identities.
The border case retains readonly IList and endless/disposal-failing enumeration
assertions. Original file-loading, serialization and independent-fixture cases
are not shortened, skipped or counted as complete. The 37 additional regression
tests are supplemental, not original-case coverage.

Both corpus modes are required by aggregate verification. All 279 browser inputs
are appended after every preceding input; both browser modes require at least
141,465 comparisons. Offline package checks import only installed modules.
The existing four-profile ownership workflow now runs all five lifecycle corpora,
144 focused tests and the complete mirrored original JavaScript suite, including
the Windows replacement-host prerequisite.

### Completed local evidence at 7b2249b

SDK 8.0.425 / .NET 8.0.31, Node 22.16.0, Debian 13 x64; September 23, 2026.

| Check | Result |
| --- | --- |
| TABLESTYLE/CELLSTYLEMAP, Debug and Release | Each: 279 scenarios / 5,829 operations; zero mismatches or unavailable observations |
| SECTION_MANAGER, both configurations | Each: 174 scenarios / 5,406 operations; zero mismatches |
| Retained polylines, both configurations | Each: 181 scenarios / 5,419 operations; zero mismatches |
| Document ownership, both configurations | Each: 188 scenarios / 8,130 operations; zero mismatches |
| Registered annotations, both configurations | Each: 112 scenarios / 6,633 operations; zero mismatches |
| Full unchanged original C# suite | 35,309 passed in each configuration; zero failures |
| Full mirrored original JavaScript suite | 2,907 passed; zero failures or unexpected original identities |
| Supplemental suite | 970 passed; zero failures, skips or TODOs |
| Offline-installed package | Passed; 509 files |
| Source-derived foundations and dimensions | Both exact regeneration checks passed |
| Release inline Chromium 144.0.7559.96 | All 141,465 comparisons executed; no new table mismatches, page errors or unavailable native observations; 83 other failures remain |
| HTTP-origin Chromium | Failed: local navigation returned ERR_BLOCKED_BY_ADMINISTRATOR |
| Full parity gates | Failed; missing work and failed/unavailable evidence remain blocking |

Runtime fingerprint:
`14ff4a7247c484834afe6b3433c470ac6580481b9a14cb4bb67bdc28779c69b7`.
POSIX verifier:
`de653b0a045c0078b7b4d97dfb817f2ccdeabac52d010d9183b90d083b34d5c9`.

### Complete differential-stage refresh

All **43 configured differential stages** were executed in each configuration
against these executable fingerprints. Release passed **37/43**; the final Debug
run passed **38/43**. All commands returned normally with status 0 or 1; no
command timeout or excluded stage was treated as a pass.

The first Debug invocation had no built general `Oracle.dll` and recorded
transport errors in stages using it. Its command ledger and logs are retained
separately. After confirming no active oracle remained, the unchanged Debug
oracle was built and the full 43-stage run repeated. The missing-prerequisite
attempt is not used as final passing evidence or relabeled as a code mismatch.

Release still reports 26 concrete-dimension, six classic-LEADER, eight block,
three coordinate and 128 entity operation mismatches. These correspond to
12, two, four, one and 64 failing scenarios, respectively. Debug retains one
BezierCurveCubic tangent NaN-sign comparison difference. It also has six
concrete-dimension, 12 tolerance and four layout/viewport scenarios without a
native result because the original Debug assertions terminate those processes.
Those unavailable observations remain blocking, not expected successes.

Both casing stages reject the local native globalization profile before
performing the pairwise comparison: Debian's observed 1,453 mappings and digest
`59c5a0ca1a3ad925c36fc1e8b9e69f6daa6e8e94e95b3020accb6eb147d9e011`
do not match the pinned Ubuntu profile. The precomputed pair count is not a
count of completed comparisons. No casing tables were regenerated or changed.

Other stages, including raw transport, handles/objects, filesystem, foundations
and randomized geometry, passed in both final runs. This does not qualify every
filesystem platform or the unported typed transport. Full gates were rerun after
the stage refresh and remain failed. Independent high-precision math evidence,
performance acceptance and Debug-browser evidence remain unavailable.

### Completed hosted matrix

[Run 35826932807](https://github.com/wieslawsoltes/netDxf/actions/runs/35826932807)
at `7b2249b` passed all four Ubuntu 22.04 / Windows 2022, Debug / Release jobs.
Each executed all five complete lifecycle corpora (**934 scenarios / 31,417
operations**), 144 focused tests, and all **2,907 mirrored original cases**.
The Windows bridge prerequisite remained enabled and no affected test was skipped.

All four artifact archives were downloaded, SHA-256 checked against GitHub and
inspected. The [retained receipt](table-styles-hosted-7b2249b.json) includes all
20 actual differential result documents plus original-suite metadata, counts
and file/archive hashes. Full original-result arrays remain in the identified
archives and the downloadable validation bundle. This is the whole mirrored
suite, not the still-unported complete 35,309-case C# suite.

Windows verifier:
`695bfc54fb71c4fb238e27998d5a031a4849282a8274dca616654b585ca8a001`.
The existing verifier sorts host paths before normalizing separators. Its POSIX
and Windows fingerprints were independently reproduced from identical source
bytes; actual reports were not rewritten. These finite focused successes do not
waive unrelated failed or missing full-port evidence.

From `javascript/`, with the pinned source checkout and toolchain selected:

```sh
export CONFIGURATION=Release # Repeat with Debug.
node tools/dotnet.mjs oracle
node tools/dotnet.mjs geometry
npm run test:table-styles
npm test
npm run test:unit
npm run test:differential
npm run test:package
node tools/browser-corpus.mjs
python tools/browser-inline-check.py
npm run verify:complete
```

## Remaining parity work

The ledger is **375/510 library mirrors (135 missing), 58/193 conformance-file
mirrors (135 missing), and 2,907/35,309 original cases (32,402 missing)**. File
presence is not an exhaustive member/signature or behavioral compatibility audit.

Typed DXF reading/writing and Load/Save/SaveAtomic integration, complete profile
conversion, ACAD_TABLE/TABLECONTENT/TABLEGEOMETRY and other private schemas,
remaining APIs/original tests/examples, broad numerical/platform/filesystem
qualification and performance acceptance remain unfinished. These retained
models do not generate complete native TABLE layout or prove AutoCAD
open/AUDIT/save/reopen fidelity.

The inline browser's 83 failures consist of 64 entity, 12 concrete-dimension,
four block, two classic-LEADER and one coordinate scenario. The preceding
checkpoint reported 87. No production numerical change was made in this
increment, and the changed count is not claimed as a numerical fix.
Debug browser and other unavailable qualification categories remain blocking.
No original C# or fixture changes, removed comparisons, tolerance, expected-
failure waiver, merge, force push or npm publication occurred. PR #98 stays draft.
