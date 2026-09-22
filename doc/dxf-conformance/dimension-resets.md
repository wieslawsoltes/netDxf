# Explicit arrow and text-fill resets

The existing nullable DimArrow1, DimArrow2 and LeaderArrow overrides now emit a
complete DSTYLE identifier plus database-handle pair. A null value writes handle
`0`, the default closed-filled arrow, rather than omitting the value. Null is not
the same as an absent override and does not mean an invisible arrowhead.

The typed reader retains explicit zero handles for separate first/second arrows
and for the shared DIMBLK field when DIMSAH is off. A shared field materializes
both arrow overrides, as it already did for a custom block. Numeric zero handles
with leading zeroes are accepted and subsequently written canonically as `0`.
Unresolved nonzero references retain their existing admission behavior. Leader
arrow decoding already supported the null handle and is unchanged.

A null TextFillColor override writes DIMTFILL=0, clearing the inherited fill,
instead of silently omitting the override. Nonnull colors retain the existing
DIMTFILL=2 and indexed DIMTFILLCLR representation. The public object model's
absence/no-fill distinction is preserved; this does not add a separate API for
drawing-background fill mode or full true-color fill semantics.

## DIMSTYLE table flags versus fill color

The DIMSTYLE table writer now emits its standard flags group70 immediately after
the style name. For an explicit fill, the later second group70 stores the fill
color. Previously the only group70 contained that color, which an independent
reader interpreted as flags; same-library round trips masked the loss. An
unfilled style still has one group70 with zero flags. The existing typed reader
uses the later color and requires no new table decoding policy for these files.
Unrepresented source flag semantics are not newly preserved by this correction.

## Verification

The focused suite defines 496 cases: 424 source/output cases across all eight
concrete dimension families and LEADER, six typed versions and four containers,
plus 72 independently authored raw-input cases. ARC_DIMENSION keeps its R2004+
gate. Each ordinary fixture contains seven variants: absence, each individual
reset, all resets and explicit nonnull replacements. Both output transports are
reloaded, regenerated, cloned and saved after clearing overrides. Tests retain
source XData identities and contents, opaque neighboring records and unknown
pairs, canonical base settings, handles, following geometry and graph validity.
Aligned regeneration also checks custom-arrow insert counts.

Raw tests start from nonnull overrides and patch their physical packets,
independently of the new reset encoder. They cover separate and shared defaults,
leading-zero handles, shared custom blocks, and no-fill with a retained color
packet. Common-arrow spelling may normalize to two separate fields on output;
byte-identical spelling is not claimed.

The independent verifier requires all 1,272 ordinary source/output drawings,
checks full DSTYLE packets and real handle targets, and loads all 8,904 hosts with
ezdxf. It distinguishes absent fields from explicit reset values and rejects
packet changes and incomplete/extra inventories. The installed-package consumer
also exercises all four resets alongside previous six-version regressions.
Executed results and source-bound evidence are recorded in the PR.

The initial reset-only implementation passed every C# case, but independent
inspection found the table flags/color collision. The writer was corrected and
a physical two-group70 assertion was added rather than weakening the reader
check. Interrupted build/checker invocations are not counted as passing runs.

## Boundaries and references

No new public API, version eligibility, fit/font/leader routing algorithm or
whole-save transaction guarantee is added. Fixed labels and stored settings are
not native AutoCAD visual evidence. Historical typed dialects, pre-R11, full
private FIELD/TABLE/cache regeneration, dependency-complete import, general
version conversion and native open/AUDIT/save/reopen remain separate work.

- Autodesk DIMSTYLE DXF schema: https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-DXF/files/GUID-F2FAD36F-0CE3-4943-9DAD-A9BCD2AE81DA.htm
- Autodesk DIMTFILL semantics: https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-4E38E29F-DE85-4791-A2E7-4DC22842B1B4.htm
- Independent default-arrow handle conversion: https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/entities/dimension.py
