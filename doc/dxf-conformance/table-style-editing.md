# Explicit stored TABLESTYLE editing

`DxfTableStyle.ReplaceStyle` atomically updates the recognized classic header
and selected row scalars or [complete border sets](table-style-borders.md) of a loaded TABLESTYLE. It keeps the source document,
profile, ownership, physical row count and order, exact STYLE resource identities,
and every untouched stored tag. Existing native TABLESTYLE and CELLSTYLEMAP
packets supply the field framing. This is explicit stored-data editing: TABLE
geometry and CELLSTYLEMAP formats are not synchronized or regenerated.

```csharp
DxfTableStyle style = document.Objects.Items.OfType<DxfTableStyle>().Single();
var values = new DxfTableStyleRowValues(
    textHeight: 4.125, cellAlignment: 6,
    storedTextColor: 3, storedFillColor: 257, backgroundColorEnabled: true);
style.ReplaceStyle(
    header: null, // Keep the original header, including unknown header variants.
    rows: new[] { style.Rows[0].WithValues(values) });
```

The header constructor accepts decoded description text (up to 255 UTF-16 code
units), flow direction 0 or 1, signed stored flags, two finite nonnegative
margins, and title/heading suppression flags. The row-value constructor accepts
a finite nonnegative height and stored signed alignment/color codes. The latter
remain stored values; this API does not assign new meanings or normalize special
values such as fill color 257. Negative zero is preserved as a distinct value.

The classic header sequence is `3,70,71,40,41,280,281`. R2010 and later
source profiles also admit exactly one leading `(280,0)` format-version tag;
`Header.StoredVersion` reports it, and edits retain that original tag unchanged.
This is distinct from the later group-280 title-suppression flag. Other header
variants remain unprojected. A non-null replacement rejects when `Header` is
unavailable; passing null preserves any such header while qualified rows can
be edited. A row exposes `WithValues` only when its
five scalar groups `140,170,62,63,283` are unique and valid. Private application
groups and unknown subclasses are never mistaken for these fields.

Row edits must refer to distinct objects from the current `Rows` snapshot. All
enumeration and disposal finish before source registration and the complete
database graph are validated. Reentry rejects the outer edit even if caller
code catches the inner exception. Null entries, foreign or stale snapshots,
changed source profiles, erased identities and invalid graphs reject before
the style changes. Caller callbacks' independent document changes are not
rolled back. No handles are assigned and no dependency is silently rebound.

Equivalent requests retain the original `Tags`, `Header` and `Rows` objects.
Successful changes publish new immutable snapshots; earlier snapshots remain
unchanged. Group-7 tag objects and exact STYLE bindings are retained, so later
STYLE resource renames still write the actual resource's new name. Renaming a
resource does not require replacing a row's scalar values.

Edited descriptions escape literal backslashes exactly once. Legacy profiles
also escape non-ASCII UTF-16 code units; Unicode including surrogate pairs and
literal `\U+....` text round-trips without double decoding. NUL and unpaired
surrogates reject. Binary DXF retains CR/LF while text output rejects them before
writing bytes, following the existing database string policy.

`table-style-edit/` tests exercise native carrier and complete-drawing edits,
all five R2004–R2018 typed profiles and both transports, source graph and
snapshot invariants, enumeration failures, private field boundaries, Unicode,
negative zero, repeated edits and resource renames. Existing TABLESTYLE tests
remain the unchanged-storage regression suite. Run the independent gate with:

```sh
python tools/verify_editable_table_styles.py ARTIFACT_DIRECTORY
```

The gate parses fourteen native before/after pairs using ezdxf's independent
tag readers. It checks every physical record: only the explicit header and
first/third row scalar values can differ. Entire TABLE, CELLSTYLEMAP and STYLE
records remain unchanged. The existing writer regenerates the empty
`ACAD_LAYERSTATES` dictionary on every save, so the gate verifies that exact
empty shape and normalizes only its generated identity and incoming owner slot.
HEADER timestamps and HANDSEED are excluded from the physical object inventory.
Every other record's order, identity and values remain exact. Corruption controls alter each
edited field and untouched row/style/map values in the actual parsed output.
These tests qualify stored packet edits, not native AutoCAD execution, rendered
formatting, table evaluation, or complete TABLESTYLE/CELLSTYLEMAP editing.

The [source-pinned qualification receipt](receipts/table-style-editing/qualification.json)
records the earlier PR95 scalar-only Debug and Release executions: 154 cases in each configuration,
including 61 new editing cases; fourteen edited native pairs with 322 corruption
controls; and the unchanged-style gate's 70 outputs with 118 controls. It includes
the implementation and oracle commits, source/assembly/result digests, compressed
result and gate logs, and complete output archives with per-file hashes.

The separate [VPORT frozen-layer assessment](vport-frozen-layers-assessment.md)
remains unresolved; this work adds no guessed VPORT layer encoding.
