# Nested CELLSTYLEMAP formatting and atomic editing

`DxfStoredCellStyleMapEntry.Format` now exposes supported nested formatting
packets. `DxfStoredCellStyleMap.ReplaceEntries` can edit selected names, stored
format values and exact resource references in one transaction. The existing
`ReplaceEntryNames` API remains supported and preserves formatting projections.
This is existing-packet editing: it does not regenerate TABLE geometry, evaluate
format expressions, synchronize duplicated formats, or author a new map schema.

## Source-backed field contracts

The five original drawings pinned in [the table corpus](../../tools/table_oracle/fixtures.json)
contain three map entries with complete TABLEFORMAT, CONTENTFORMAT, CELLMARGIN
and six GRIDFORMAT frames. They cover R2004, R2007, R2010, R2013 and R2018.
The independently maintained [LibreDWG wire specification](https://github.com/LibreDWG/libredwg/blob/34f02f54b9aacb5708c1d3d2070efb3e4b2d8c43/src/dwg2.spec)
provides the field labels used here. The parser and editing code are original;
no external implementation code is incorporated. The
[existing map evidence](cell-style-map.md) identifies the native source records,
common ownership and version qualifications.

| Public model | Editable stored fields | Fields kept structurally fixed |
|---|---|---|
| `DxfCellStyleFormatValues` | Property overrides (91), merge flags (92), background index (62), content layout (93) | Type (90), data-presence flags (170), all frame boundaries |
| `DxfCellContentFormatValues` | Overrides (90), property flags (91), data/unit codes (92/93), decoded format expression (300), rotation (40), block scale (140), alignment (94), color index (62), text height (144) | CONTENTFORMAT frame presence and order |
| `DxfCellMargins` | Six ordered doubles (40): vertical, horizontal, bottom, right, horizontal spacing, vertical spacing | Margin-presence flags (171), margin frame presence |
| `DxfCellGridFormatValues` | Overrides (90), border type (91), color index (62), lineweight code (92), visibility code (93), double-line spacing (40) | Border count, order and stored index masks (95) |
| Explicit resource selection | Content STYLE handle (340), grid LTYPE handles (340) | Source-document resource registration; no implicit import |

Numeric flags, alignment, visibility, lineweight and color codes retain their
stored signed types. In particular, nested grid visibility is a raw integer;
this API does not assign the classic TABLESTYLE border-boolean interpretation
to it. Floating-point constructors require finite values but preserve signed
values and negative zero. Acceptance as stored data is not a promise that every
numeric combination has meaningful native rendering behavior.

A projection is available only for the complete, exact ordered packet shapes
implemented by this parser. Data-presence flag zero admits an empty format;
margin flag zero omits margins; zero borders is allowed. Present margin frames
require six values and the grid count is bounded by six. Nonzero duplicate masks
are retained without assigning unique edge roles. A zero-mask compact variant,
extra true-color fields, unsupported extensions, missing/repeated fields,
reordered fields or incompatible decoded text leave `Format` null. The complete
`FormatPayload` remains stored, and names can still be edited. Malformed outer
frame envelopes retain their existing rejection rules; private map variants
retain their opaque-object contract.

Decoded format strings are stored, not executed or interpreted. Literal marker
strings, literal `\U+...` sequences and Unicode remain text. Escaping occurs
once; R2004 non-ASCII characters use UTF-16 DXF escapes. NUL, unpaired surrogates
and oversized text reject. Both decoded and escaped strings are bounded by the
existing 1,048,576-code-unit edit limit. Binary retains CR/LF; ASCII save rejects
these values before writing bytes. Existing record/common-metadata tag limits
also apply.

## Usage and atomic publication

```csharp
DxfStoredCellStyleMap map = style.StoredCellStyleMap;
if (map != null)
{
    DxfStoredCellStyleMapEntry entry = map.Entries[0];
    DxfCellStyleFormat format = entry.Format;
    if (format != null && format.Margins != null)
    {
        var edit = format.Edit().WithMargins(
            new DxfCellMargins(0.25, 0.5, 0.75, 1.0, 1.25, 1.5));
        map.ReplaceEntries(new[] { entry.WithFormat(edit).WithName("Custom") });
    }
}
```

`format.Edit()` composes `WithValues`, `WithContent`, `WithMargins`,
`WithTextStyle` and `WithBorders`. A border request comes from `border.Edit()`
and composes `WithValues` and `WithLinetype`. Each builder returns a new immutable
request. `WithBorders` replaces that request's selected border-edit list, copies
a bounded enumeration, completes disposal, and requires distinct actual border
snapshots from the same format. Null value objects reject. A null resource in
`WithTextStyle(null)` or `WithLinetype(null)` explicitly clears the selected
handle; omitting the method preserves the original handle.

`ReplaceEntries` requires distinct actual current entry snapshots, not a lookup
by potentially duplicate entry IDs. Every request is materialized and disposed
before validating source registration, ownership, dependencies and profile.
Both replacement APIs share a reentry guard, including nested calls whose
exceptions the caller catches. Foreign/stale snapshots, invalid resources,
callback exceptions or escaped-string overflow reject before publication.
The guard resets after failure.

Values, new packet tags, candidate projections and dependency memberships are
prepared before the final state swap. No caller code, resource registration,
handle allocation or fallible binding occurs during publication. Old payloads,
entry/format snapshots and reference-membership lists remain unchanged.
Referenced resource objects are still mutable: snapshot membership does not
freeze their future names or lifetimes. Equivalent edits preserve current
snapshots and original stored spelling. Thread-concurrent mutation is not
supported. Independent changes caller callbacks make to other objects are not
rolled back.

A new name snapshot makes an old entry edit stale. The name-only API can retain
an unchanged format snapshot; reusing it is allowed only when the request is
attached to the actual current entry. An edit that changes the format creates
new border snapshots, so old grid requests cannot be silently retargeted.

## Exact resource identities and lifecycle

A non-null resource must already be the actual registered STYLE or LTYPE object
in the source document, with matching collection owner, collection name entry
and handle identity. Validation occurs after enumeration, permitting explicit
caller registration beforehand but never implicitly importing a same-name
foreign resource. Later renames preserve handle-based identity on save/reload.
A null or differently typed existing target projects a null typed convenience
property. Its stored handle is still retained, and any nonzero semantic target
still participates in the map's full dependency list. An explicit selection can
repair a mismatched target; explicit null can clear it.

The ordered reference list is rebuilt from the resulting packet, including
repeated semantic handles. Reassignment releases only the selected uses; other
entries, other grid slots, common metadata and independent consumers keep their
existing protection. New targets become protected immediately. Clear requests
write null handles without generating defaults. Snapshot rebuilding does not
resolve new coincidental targets for unchanged fields. No changes to cloning,
erasure, cross-document import or source-profile conversion are introduced.

## Verification and profile scope

All five R2004–R2018 profiles have synthetic text/binary cases. The native path
uses the same five corpus families as the earlier module: two whole unchanged
original drawings and three explicitly documented complete carriers. The
[carrier manifest](../../tests/fixtures/table-content/manifest.json) identifies
selected source records and permitted ownership adaptations; these carriers
are not represented as whole unmodified originals.

The new regression suite includes values, resources, name composition, null
clearing, repeated-reference counts, independent resource consumers, malformed
packet admission, unsupported extensions, immutable request construction,
unknown format preservation, enumeration/disposal failures, cross-API reentry,
stale/foreign snapshots, Unicode and signed values. Existing map-name, stored
map, TABLESTYLE and mixed-graph tests remain enabled.

`verify_cell_format_editing.py` requires exactly 120 before/after pairs (60
synthetic and 60 native-family pairs). Its frame-scoped parser locates requested
fields independently of production offsets. It calculates expected changes
from the test operation, not an emitted expected-value manifest. Complete
ordered physical records must match except for requested map fields and the
existing writer's narrowly verified empty ACAD_LAYERSTATES dictionary identity.
The shared normalizer validates that exact empty shape before normalizing its
identity and incoming dictionary link. HEADER time/seed fields and CLASS
records are outside this physical-record comparison.

Corruption controls alter actual parsed values after that narrow normalization.
The same exact complete-record comparator must reject every requested-field
corruption, each untouched middle-entry field, structural markers/IDs/counts/
masks, selected unrelated record changes, and map deletion. The independent
oracle's challenge self-test uses modeled edits of the pinned native packets;
it tests rejection behavior and is separate from checking actual library output.

```sh
DXF_TEST_FILTER=cell-format/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_cell_format_editing.py --self-test
python tools/verify_cell_format_editing.py artifacts/conformance
```

The [implementation-stage receipt](cell-style-format-qualification.json) records
35,876 passing Linux Debug cases at `a55a38e7520bbb80637f291c1b71c98be0ba6992`,
including 567 initial additions. The new independent gate, executed separately
against that downloaded artifact, checks 120 pairs and rejects 27,000 actual
output corruptions. Its challenge self-test checks 30 modeled native pairs and
6,690 corruptions. Fourteen additional request-boundary cases are included in
the subsequent checkpoint. Final-head execution is recorded in PR #99; the
initial receipt is not substituted for qualifying that final source.

The [aggregate comparison](version-feature-matrix.md) remains the pinned PR95
297-row snapshot. This note supplements the post-PR95 TABLESTYLE operations:
qualified nested CELLSTYLEMAP editing is tested for R2004–R2018 source packets;
R2000 map variants remain opaque, and R12/R13/R14 remain raw-only profiles.
No source-version conversion is introduced. Full DXF capability, native AutoCAD
execution, arbitrary private schemas, complete color/format semantics,
structural map authoring, cross-object formatting synchronization and automatic
TABLE layout/regeneration remain unfinished.
