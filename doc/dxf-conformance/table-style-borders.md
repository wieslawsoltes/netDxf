# Stored TABLESTYLE borders and version-zero headers

This increment extends the explicit stored-style editing contract merged in
PR #95. It does not create or regenerate TABLE layout, resolve shared cell
edges, synchronize CELLSTYLEMAP formats, or qualify rendered output.

## Source-backed projections

The [Autodesk TABLESTYLE group-code reference](https://help.autodesk.com/cloudhelp/2016/ENU/AutoCAD-DXF/files/GUID-0DBCA057-9F6C-4DEB-A66F-8A9B3C62FB1A.htm)
documents six lineweights (274–279), six visibility flags (284–289), and six
color indices (64–69) in each classic row. These fields are populated in the
five already-pinned native fixtures used by the stored TABLESTYLE suite.
`DxfTableStyleRow.Borders` exposes them as six immutable triples: slot `i`
contains groups `274+i`, `284+i`, and `64+i`. No row-role or shared-border
interpretation is inferred from the slot index.

All eighteen fields must occur exactly once in the public row packet, and
visibility must be zero or one. Missing, duplicate, or invalid fields leave
`Borders` null without destroying the stored packet or disabling otherwise
qualified scalar edits. Public field order is preserved. Private 102 groups and
other subclasses neither satisfy missing fields nor conflict with public ones.
Lineweight and color values remain signed stored codes, without normalization
or a promise that every numeric code has rendering semantics.

A header in an R2010–R2018 source profile may additionally have the exact
leading `(280,0)` format-version tag before the existing
`3,70,71,40,41,280,281` sequence. `Header.StoredVersion` exposes zero in that
case and null for classic headers. Header editing preserves the actual leading
tag and changes only the later group-280 title-suppression flag. Other format
versions, duplicate or reordered prefixes, and pre-R2010 version-prefixed
packets remain unprojected. This is source-profile recognition, not conversion.

## Atomic editing

```csharp
DxfTableStyle style = document.Objects.Items.OfType<DxfTableStyle>().Single();
DxfTableStyleRow row = style.Rows[0];
if (row.Borders == null)
    throw new NotSupportedException("The stored border packet is not qualified.");

DxfTableStyleRowBorders borders = row.Borders.WithBorder(
    0, new DxfTableStyleBorderValues(
        storedLineweight: 25, isVisible: true, storedColor: 3));
style.ReplaceStyle(header: null, rows: new[] { row.WithBorders(borders) });
```

A border-only edit leaves scalar values untouched, even when they are not
projected. To replace scalars and borders atomically, use
`row.WithValues(values).WithBorders(borders)` in the same `ReplaceStyle` call.
Each request binds to an actual current row snapshot. Duplicate, stale, and
foreign rows reject before publication. Earlier immutable snapshots remain
unchanged; equivalent edits retain the existing snapshots. STYLE tag identities,
resource bindings, dependency order, ownership, and source profile are unchanged.
No handles are allocated by editing. Existing writer handle allocation during
save is a separate operation.

`DxfTableStyleRowBorders` copies exactly six non-null immutable triples. Input
iteration is bounded and completes, including disposal, before publication.
The existing ReplaceStyle graph validation, caught-reentry rejection, rollback
boundary, and resource-removal guards also apply to border-only and combined
edits. Independent changes that caller callbacks make to other document state
are not rolled back. These APIs do not provide concurrent-thread mutation.

## Verification

`table-style-borders/` registers 257 conformance cases across the five typed
R2004–R2018 profiles and text/binary output. Coverage includes all eighteen
missing/duplicate/private-only field cases, invalid visibility flags, native
carriers and complete native drawings, unknown/scoped/versioned headers,
immutable input snapshots, stale and foreign edits, disposal failures, caught
reentry, resource renaming, and opposite-transport reloads. Existing stored
style and scalar-edit regression suites are retained; their expectations now
recognize the documented native AC1024 prefix instead of treating it as unknown.
An actual unknown-version fixture continues to assert conservative rejection.

```sh
# Focused .NET cases; the regular CI executes the complete unfiltered suite.
DXF_TEST_FILTER=table-style-borders/ dotnet run \
  --project tests/netDxf.Conformance --configuration Debug
python tools/verify_table_style_borders.py --self-test
python tools/verify_table_style_borders.py artifacts/conformance
```

The independent ezdxf gate requires 34 before/after pairs: twenty synthetic
profile/transport/combined-edit pairs and fourteen native pairs. It compares
complete ordered physical records, checks fixed format-version tags, and rejects
corruptions of every requested field plus untouched rows and unrelated records.
Only the existing writer's verified empty ACAD_LAYERSTATES dictionary identity
is normalized; this is the same narrow normalization used by the scalar gate.
The oracle computes expected edits itself rather than trusting emitted
expected-value manifests. Its self-test has four pairs and 197 corruption
controls. CI discovers the new gate automatically; pass/fail evidence is the
actual workflow result, not the declared test inventory.

The exact PR95 source was recovered from final CI artifact 10421002666 and
matched Git tree `20d2f858459cc03b879ad234896bd03dc067beda` across all 3,038
archive files. Its 34,836 unique cases passed before this increment; those
historical results do not stand in for running the new source. PR #95 was merged
at `267b921e38740ff8ed04fc8048a84bd8008be98c` after all four final CI jobs passed.
The existing 297-row comparison remains the prior scoped ledger, not a claim
that these additions establish universal TABLESTYLE or DXF completeness.
