# Immutable MTEXT paragraph composition

Paragraph interpretation lives in `netDxf.Shadow.Rendering`; captured source values
remain in `netDxf.Shadow`. The layout neither reads live netDxf objects nor retains
native fonts. Skia and SVG consume the same positioned runs and rules.
`ShxEntityProjector` uses the common layout before converting runs to stroke geometry.

## Run the sample

```sh
dotnet run --project shadow/samples/netDxf.Shadow.Studio/netDxf.Shadow.Studio.csproj -c Release -p:TargetFrameworks=net8.0
```

Open the local Studio and select **Paragraphs**. Six panels demonstrate hanging
indents, alignment, justification, left/right tabs, exact baseline spacing, rich runs
and synthetic SHX strokes. Switch Skia/SVG, retain a snapshot, edit text or spacing
in the inspector, undo, and save/reload the DXF. Stroke examples are generated
instructions, not redistributed font files.

## Detached contracts

`RichTextStyle.Paragraph` carries an immutable `RichTextParagraphStyle`: first/left/right
indents, before/after spacing, alignment, baseline policy and an immutable ordered tab
table. Equality and hashing compare tab values, not the identity of an array allocated
by a particular parse. Invalid default arrays remain distinct from initialized empty
arrays and are rejected by validation.

`RichTextInline.IsParagraphControl` identifies an empty source-associated formatting
token. Tokens preserve controls on empty paragraphs without sending empty strings to
a shaping engine. Paragraph controls affect the current paragraph; font/color changes
do not introduce extra word-breaking opportunities. A paragraph beginning after a
formatting scope closes obtains the restored context.

`RichTextLayoutResult.Bounds` encloses ink/decorations. `LayoutBounds` encloses the
logical column and vertical layout extent. Both use the attachment origin. `Lines`
contains paragraph/row indices, left/right positions, baselines and paragraph-end
flags. Overlong words can extend beyond `LayoutBounds`; rendering/picking use ink
bounds rather than an artificially clipped logical column.

## Supported paragraph controls

`MTextParagraphControl.Parse(argument, inherited)` processes the argument of `\p...;`.
The source parser recognizes multiline `^I` tabs, `^J` breaks and `^M` carriage returns,
without interpreting those caret controls in ordinary TEXT.

| Control | Meaning |
| --- | --- |
| `i`, `l`, `r` | First-line, left and right indentation |
| `ql`, `qc`, `qr`, `qj`, `qd` | Left, center, right, justified and word-distributed alignment |
| `b`, `a` | Additional spacing before and after a paragraph |
| `s*`, `sa`, `se`, `sm` | Entity/default, minimum, exact and multiple baseline spacing |
| `t` / `t*` | Replace the explicit tab table with an empty table |
| `t4,c8,r12` | Left tab at 4, center at 8, right at 12 |
| `i*`, `l*`, `r*`, `q*` | Reset the named setting, retaining other settings |
| `*` | Reset the complete paragraph style |

A leading exporter `x` marker is accepted. Numeric parsing is invariant-culture and
finite-only. Malformed controls remain visible with diagnostics and do not partially
modify inherited settings. Tabs are sorted once; duplicate positions, invalid
alignments and out-of-range values are rejected.

Indents, tabs and additional paragraph spacing are multiples of the initial entity
height, not the current inline `\H` height. Hanging first lines are clamped at the
column origin. Exact/minimum paragraph baseline values are drawing units; multiple
spacing observes the largest run height of adjacent rows. Before/after gaps add only
at paragraph boundaries. Blank paragraphs retain metrics and inherited controls.

Justification stretches breakable spaces on wrapped rows, excluding the last row.
Word distribution also stretches the last row. Glyphs/shaping clusters are not
stretched. Tabs suppress justification so authored stops remain stable. Center/right
fields are atomic: an oversized field overflows rather than wrapping only its suffix.
Default tabs use a four-height grid after the last explicit stop. Authored terminal
spaces participate in alignment; separators consumed by soft wrapping are removed.
Nonbreaking spaces are never discarded.

## Entity line spacing and compatibility

`CadText` retains its existing positional constructor and adds optional immutable
`CadLineSpacing(Factor, Exact)`. `StandardEntityProjector` reads MTEXT's source factor
and style through the observed immutable reader. Source edits invalidate text
projection while unrelated items remain reusable. Affine transforms transport text
axes, not local spacing metadata. SHX uses the same standard text projection.

An entity factor multiplies `5 / 3 * entity height`. Exact mode uses that distance
with mixed inline heights; at-least expands for taller runs. Paragraph spacing
controls override it; `s*` restores it. Factors must be finite and within 0.25 to 4.
A portable `CadText` without optional spacing metadata retains the legacy default:
previous descent + next ascent + 0.2 entity height. This preserves custom callers'
existing default behavior while projected MTEXT uses its source settings.

```csharp
using netDxf.Shadow;
using netDxf.Shadow.Geometry;
using netDxf.Shadow.Rendering;

var text = new CadText(
    @"\pl1,r1;First paragraph\PSecond paragraph",
    Vec3.Zero, Vec3.UnitX, Vec3.UnitY, 10,
    "sans-serif", "TopLeft", true, 100)
{
    LineSpacing = new CadLineSpacing(Factor: 1.5, Exact: true)
};
var layout = RichTextLayout.Layout(text, RichTextLayout.ApproximateMetrics);
// Supply ITextLayoutEngine/native shaping for production font metrics.
```

## Work bounds and verification

Words/spaces are measured once; styled fragments remain one wrapping token. Tab
field widths are accumulated once in a reverse pass. Three sorted indexes select
left/center/right stops with binary search instead of rescanning every stop or
remeasuring a growing field. Index construction still visits the inherited tab table
for each paragraph; no constant-cost or globally linear guarantee is implied.

Character, inline, control, tab, measurement, token, paragraph and row budgets reject
excessive work. Nonfinite metrics/arithmetic overflow fail explicitly. Tables contain
at most 4096 stops; control arguments at most 65536 characters. Old source snapshots
and prepared layouts remain immutable after failures or later edits.

Run both full executable suites. `ParagraphControlVerification` checks grammar,
resets, culture, scope, source authoring and budgets. `ParagraphLayoutVerification`
checks geometry, all nine attachments, wrapping, blank paragraphs, tabs, spacing,
justified decoration and equality. Its 128 randomized tab cases use an independent
linear oracle. Integration tests cover source invalidation, undo/redo, retained data,
DXF roundtrips, native pixel containment, SVG, SHX placement and the Studio gallery.
CI retains PNG/SVG/DXF artifacts; an artifact alone does not prove a passing suite.

## Explicit remaining boundaries

Not implemented here: paragraph-level Unicode bidi itemization, arbitrary font
fallback, mixed native/SHX runs, decimal tabs, glyph-level distributed spacing,
automatic MTEXT column balancing, BIGFONT/vertical flow or application FIELD evaluation.
Fields remain literal and cannot execute code. This is not a pixel-identity claim
against proprietary CAD renderers. Specialized geometry/ACIS, linetype, viewport and
factory/edit extensions remain separate workstreams.

## Primary references

- [Autodesk Paragraph dialog](https://help.autodesk.com/cloudhelp/2021/ENU/AutoCAD-Core/files/GUID-90F5A530-BB66-4264-86F0-D5F670EC8519.htm).
- [Autodesk MTEXT command](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-Core/files/GUID-F94BE932-DA31-437E-9610-27F46ACD5711.htm).
- [Autodesk LineSpacingStyle](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-ActiveX-Reference/files/GUID-F2313E05-2429-4B28-8D6B-8D474F1DA746.htm).

netDxf's `MText.StartParagraph` and `MTextParagraphOptions` supply independent
source-authoring cases; tests do not use the renderer as their own text writer.
