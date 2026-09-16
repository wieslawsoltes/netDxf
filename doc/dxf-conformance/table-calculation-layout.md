# Addressed table calculations, explicit style cascades and measured display blocks

This increment adds reusable calculation and layout APIs above the stored TABLE
and TABLECONTENT contracts. It does **not** turn every preserved TABLE packet
into a fully regenerated native AutoCAD table. In particular, editing a backing
scalar, building a display block and selecting it for a source TABLE are distinct, explicit operations.

## Architecture and data flow

| Layer | API | Input and publication boundary |
|---|---|---|
| Addressing | `DxfTableCellAddress` | Immutable zero-based coordinates; checked invariant A1 parsing. |
| Compilation | `DxfTableFormula.Parse` | Immutable bounded expression tree; no document dependency. |
| Calculation | `DxfTableCalculation.Evaluate` | Explicit formula map and scalar resolver; detached read-only results. |
| Stored projection | `DxfStoredTableContent.GetGrid` | Complete qualified linked frames; immutable addressed grid associated with one payload snapshot. |
| Result application | `DxfStoredTableContent.ApplyFormulaResults` | All calculations and target validations inside the existing guarded content transaction; one publication. |
| Formatting | `DxfCellStyleResolver.Resolve` | Complete base and explicitly ordered override definitions; detached flattened definition and per-property provenance. |
| Layout | `DxfTableLayout.Create` | Addressed grid plus explicit style/text/measurement providers; immutable physical rectangles. |
| Display | `DxfTableLayout.BuildDisplayBlock` | Fresh unregistered block with LINE, SOLID and MTEXT entities; no source TABLE cache mutation. |
| Display selection | `StoredTable.ReplaceDisplayBlock` | Atomically selects an already registered flat display block and clears stale proxy graphics; inline/backing caches remain fixed. |

The production APIs add no third-party runtime dependency. Independent Python
verification remains development-only. Source-bound STYLE/LTYPE objects retain
identity rather than being silently cloned or imported.

## Addresses and formula evaluation

```csharp
using netDxf.Tables;

var a1 = DxfTableCellAddress.Parse("$a$1");
// a1.Row == 0; a1.Column == 0; a1.ToString() == "A1"

var expression = DxfTableFormula.Parse("=SUM(A1:A3)/2");
double result = expression.Evaluate(3, 1, address =>
    address.Row == 0 ? (object)10 : address.Row == 1 ? 20 : 30);
// result == 30
```

Addresses accept ASCII letters case-insensitively and optional dollar markers.
Standalone addresses store coordinates, not copy/paste relativity. The compiled
formula retains its original expression, but automatic translation when copying
formulas has not been implemented. Rows have no leading zeros. Both coordinates
are checked against the specific grid when evaluated.

Expressions start with `=` and use invariant finite numeric literals, cell
references, `+ - * / ^`, unary signs, parentheses and comma-separated aggregate
arguments. Exponentiation is right-associative and binds before unary minus:
`=-2^2` yields -4, while `=(-2)^2` yields 4. Ranges are valid only as aggregate
arguments. Reversed rectangle endpoints are normalized as an explicit API policy.

`SUM`, `AVERAGE`, `COUNT`, `MIN` and `MAX` ignore empty or text-valued references.
`COUNT` counts numeric results, **not every addressed cell**. This follows the
explicit rule in Autodesk's [formula overview](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-505EF5BB-5347-43DA-91A6-03141A9134C8.htm).
The shorter [formula entry examples](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-LT/files/GUID-15E0C775-6E60-456D-B5C7-4726E0C586B7.htm)
call COUNT a cell count but omit that filtering detail. The initial draft used
unfiltered address counting; qualification corrected it before merge. COUNT now
resolves dependent formulas and cannot bypass circular-reference checks.

An empty numeric SUM or COUNT is zero. Empty AVERAGE/MIN/MAX rejects. Arithmetic
requires `int` or finite `double`; strings are never coerced to numbers or
executed as formulas. Unknown functions, cross-table references, native FIELD
expressions and other scalar types reject. MIN/MAX, reversed ranges, precedence,
error types and exact rounding are explicit implementation contracts, not a
claim that every native evaluator option has been qualified.

```csharp
var formulas = new Dictionary<DxfTableCellAddress, string>
{
    [DxfTableCellAddress.Parse("A1")] = "=7",
    [DxfTableCellAddress.Parse("A2")] = "=A1*3",
    [DxfTableCellAddress.Parse("A3")] = "=SUM(A1:A2)"
};
var calculated = DxfTableCalculation.Evaluate(3, 1, _ => 0, formulas);
// A1 = 7; A2 = 21; A3 = 28. Formula addresses override the resolver.
```

All request enumeration, parsing and disposal complete before scalar callbacks.
Repeated references are memoized during one evaluation. Cycles reject even when
they pass through COUNT. There are limits of 4,096 characters per expression,
128 syntax/dependency depth, 100,000 formula targets, 1,048,576 combined formula
characters, one million grid cells and one million evaluation operations.
SUM/AVERAGE use compensated accumulation; arithmetic and intermediate sums must
remain finite. This is IEEE-double calculation, not arbitrary-precision native
formula equivalence. Errors are exceptions, not silently substituted zero or
native `#` display strings.

## Addressed TABLECONTENT and atomic updates

`GetGrid()` associates scalars by their actual CELLCONTENT frame locations rather
than assuming scalar number N belongs to cell N. Empty and unsupported-content
cells therefore do not shift subsequent values into the wrong addresses.

The projection checks complete column/row/cell order, declared content counts,
positive finite band sizes, source-bound local formatting, and in-bounds,
nonoverlapping merged rectangles. It exposes each cell's `DeclaredContentCount`,
`Contents` and `HasCompleteScalarContent`. Incomplete cells remain explicitly
incomplete. Formula operands require an empty cell or exactly one qualified
scalar. Merged continuation addresses remain distinct stored cells; the layout
uses only the top-left anchor, and calculation does not invent continuation
aliases or overwrite their stored contents.

```csharp
// content is a loaded, qualified DxfStoredTableContent.
var grid = content.GetGrid();
var preview = grid.EvaluateFormulas(formulas); // Does not mutate the document.
content.ApplyFormulaResults(formulas);       // Explicit guarded publication.
var currentGrid = content.GetGrid();          // Refresh after a committed edit.
```

Result targets must already hold one integer or real scalar. The stored kind is
retained: fractional and overflowing integer results reject rather than truncate.
Existing supported display expressions are evaluated through `DxfValueFormat`.
One bad formula, target, format, enumerator disposal or caught reentrant edit
aborts the whole content change. No handles are allocated by this operation.
Saving a document may separately allocate writer defaults, so handle tests take
their baseline after the preceding save.

Old grids and value snapshots remain immutable; scalar edits built from replaced
snapshots reject as stale. Caller callbacks' independent changes and concurrent
thread mutations are not rollback guarantees. Formula requests are not persisted
FIELD definitions. Inline TABLE values, linked geometry and display caches are
not changed by `ApplyFormulaResults`; the whole-record verifier checks this
boundary explicitly rather than calling unchanged geometry regenerated.

## Explicit formatting inheritance

The resolver understands the 19 documented [cell-property bits](https://help.autodesk.com/cloudhelp/2022/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_CellProperties.html)
and six [grid-property bits](https://help.autodesk.com/cloudhelp/2019/ENU/OARX-ManagedRefGuide/files/OREFNET-Autodesk_AutoCAD_DatabaseServices_GridProperties.html).
It flattens the last selected override for each property and records its source
layer. Zero means the complete base; positive indices identify caller overrides.

```csharp
// These are qualified immutable definitions chosen by the application.
var resolved = DxfCellStyleResolver.Resolve(baseDefinition,
    new[] { tableOverride, columnOverride, rowOverride, cellOverride });
var complete = resolved.Format;
int textHeightSource = resolved.PropertySources[DxfCellProperty.TextHeight];
```

This example's column-before-row order is an **application policy**, not inferred
native precedence. The API does not silently assign roles to duplicate map IDs,
interpret a zero style ID as a particular map entry, or synchronize classic
TABLESTYLE row settings with duplicated CELLSTYLEMAP definitions. Callers select
the complete base and ordered scopes explicitly.

Combined edge masks expand to six separate edges. Partial border overrides retain
unselected properties and resource identities. Unknown bits, overlapping edge
masks within a layer, absent selected margins and partial grids without a base
reject. Empty format frames select nothing. A complete base must provide content,
table values and margins. Result override masks are zero because the result is a
flattened definition, not another sparse override packet. Private color packets
are not newly interpreted by this resolver.

## Measured layout and display generation

```csharp
// currentGrid is an immutable grid; providers belong to the host application.
// MeasureTextHeight must account for the selected font and usable width.
var layout = DxfTableLayout.Create(currentGrid,
    ResolveCellStyle, GetLiteralCellText, MeasureTextHeight, growRows: true);
var block = layout.BuildDisplayBlock("CALCULATED_TABLE_DISPLAY");
// block is detached. Adding an Insert/block to a document is a separate operation.
```

Column widths constrain layout. Row heights either remain fixed, with overflow
rejection, or grow to satisfy supplied text-height measurements plus margins.
Merged-cell deficits are distributed equally across their rows in deterministic
row-major order. Prefix sums reject overflow or loss of a positive extent.
The implementation does not inspect installed fonts, infer line wrapping from
string length, or claim native AutoCAD font metrics.

Layout supports horizontal text, all nine attachment alignments, explicit
merged rectangles, positive font height, nonnegative margins, indexed/ByLayer/
ByBlock colors, optional indexed fills, and single/double/hidden borders.
Rotation, autoscale, bottom-to-top flow, style-driven merge-all, unsupported
content layout, true-color/private color packets and unknown border data reject.
At most 100,000 logical cells and 1,048,576 literal characters are admitted.

Generated blocks use a top-left XY origin with rows progressing in negative Y.
Shared border segments are deduplicated; unequal styles reject by default, or
`DxfTableBorderConflictPolicy.LastCell` explicitly selects the later row-major
cell. Double borders offset their segments by half the supplied spacing. Fills,
borders and text are emitted in that order. This does not qualify native corner
join decoration, pagination, clipping, break tables or rotated/block content.

Backslashes and braces are escaped and line breaks converted to MTEXT paragraph
markers. Native `%%` symbol sequences and `%<` / `>%` field delimiters reject
instead of entering the literal path. Autodesk documents [percent control
sequences](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-968CBC1D-BA99-4519-ABDD-88419EB2BF92.htm);
passing them through unchanged would contradict a literal-text contract.
Ordinary percent characters and actual Unicode symbols remain text.

## Verification and reproducibility

The C# harness covers arithmetic and culture independence; memoization, cycles
and budgets; all 19 property bits and all 64 grid-property-mask combinations;
all nine alignments; native-family addressed projections; atomic numeric edits;
malformed counts, sizes and merges; callback failures; literal control rejection;
and detached fixed/growing/filled/double/hidden/literal displays.

`verify_table_calculation.py` independently derives exactly three changed cells
in each of two text/binary drawing pairs. It compares every ordered physical
record and challenges every backing-content tag plus removal of every physical
record. Only the prior strict exact-empty ACAD_LAYERSTATES normalization applies;
HEADER timestamps/seed and CLASS records are outside this record comparator.

`verify_table_layout.py` compares 12 text/binary displays against an explicit
456-unit, three-column blueprint with a merged first row. Its oracle is written
independently of production layout code and checks entity counts, coordinates,
colors, lineweights, linetypes, fonts, text, width and alignment. Coordinate
comparison rounds to ten decimal places for this blueprint. Parsed entity fields
are individually corrupted and extra entities injected as negative controls.

Every new before/after/display file must pass ezdxf audit without errors or
repairs. Exact fixture inventories, transports and profile checks are mandatory.
The numeric inputs deliberately replace native text values with zeros; display
styles and height metrics are synthetic explicit inputs. Neither is represented
as native producer evidence. The original source hash remains pinned.

```sh
DXF_TEST_FILTER=table-calculation/ dotnet run --project tests/netDxf.Conformance -c Debug
DXF_TEST_FILTER=table-engine/ dotnet run --project tests/netDxf.Conformance -c Debug
DXF_TEST_FILTER=table-layout/ dotnet run --project tests/netDxf.Conformance -c Debug
python tools/verify_table_calculation.py artifacts/conformance
python tools/verify_table_layout.py artifacts/conformance
# Full suite: preserves all existing independent gates as well.
python tools/run_independent_verifiers.py artifacts/conformance
```

Final execution counts, exact commit/tree, all four CI job results and artifact
digests are recorded in PR #103. Declared test inventories are not substituted
for executed results. No native AutoCAD process or font-comparison run has been
performed for this increment.

## Combined calculation depth

Expression and dependency limits alone do not bound their combined recursive
call stack. The evaluator also limits the sum of full expression depths across
active dependencies to `DxfTableFormula.MaximumEvaluationDepth` (1,024). It
reserves that conservative depth before evaluating each formula and releases it
in a finally block. An expression can therefore be independently valid while a
chain containing it is rejected. Eight additional tests check accepted and
rejected combinations at four expression depths, separately from operation and
formula-count limits. This budget does not govern arbitrary recursion inside
a caller-provided callback.

## Source TABLE display selection

The continuation adds [explicit display-block selection](table-display-binding.md).
It updates the qualified TABLE block name and BLOCK_RECORD pointer together and
clears stale proxy graphics, while leaving all inline and backing data unchanged.
This closes the detached-block usability gap, not native all-cache regeneration.

## Remaining requested scope

| Requested area | Result of this increment | Still not implemented/qualified |
|---|---|---|
| Inherited/duplicated formatting | Explicit property/grid cascade and provenance. | Native automatic scope selection/precedence and duplicated classic-style synchronization. |
| Formulas | Bounded numeric formulas, dependency graph and atomic same-kind result writes. | Persistent native FIELD graphs, cross-table formulas, formula copy translation, full functions/options, date/angle FIELD formatting. |
| Automatic layout/display blocks | Measured literal-cell layout, fresh LINE/SOLID/MTEXT blocks, and explicit source TABLE display selection. | Transactional replacement of a source TABLE and all inline/backing/display caches; native text metrics and every layout mode. |
| Modern/private TABLE schemas | Existing unsupported packets remain guarded and preserved. | Full interpretation, editing and regeneration of every inline/private schema. |
| Private identifiers/colors | Existing qualification gates remain. | Complete private reference/color interpretation, including arbitrary hidden references. |
| Recursive dependency import | Existing explicit resource-mapping API remains. | General dependency-complete typed graph import. |
| General version conversion | Independent generated displays use a qualified portable output profile. | General document/version migration or schema conversion of source-bound TABLE objects. |
| Native AutoCAD qualification | None claimed. | Actual native open/AUDIT/save/reopen and visual/font qualification. |
