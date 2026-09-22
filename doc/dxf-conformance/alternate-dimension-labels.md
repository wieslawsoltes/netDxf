# Inline alternate-unit dimension labels

The shared DimensionBlock formatter now uses enabled alternate-unit settings
when generating measured labels for linear, aligned, radial, diametric,
ordinate and arc-length dimensions. Angular dimensions retain their angular
labels. The alternate value is appended in brackets, with its own prefix and
suffix. Explicit literal text and the one-space suppression marker remain
literal/suppressed; each `<>` replacement includes the full measured label.

The resolved DIMLFAC owner policy applies once, before the primary and alternate
rounding branches diverge. Alternate multiplication and DIMALTRND rounding do
not reuse the already rounded primary number. All six existing linear formats
are supported through the existing unit formatters: Scientific, Decimal,
Engineering, Architectural, Fractional and WindowsDesktop. Alternate precision,
zero suppression, feet/inches and stacking settings are honored. WindowsDesktop
uses the current culture's decimal precision/separator without changing it.
Stacked MTEXT instructions are scoped to the alternate value.

The effective-style copy retains the base alternate-unit value object through a
clone and applies all twelve supported alternate-unit overrides. A color-only
or other unrelated override therefore no longer loses enabled alternate units.
The same copy now preserves base DimScaleOverall instead of defaulting it to 1.
Neither change modifies the original style or its sparse override dictionary.

Nonfinite multipliers, rounding settings, converted values and rounded values,
and undefined alternate-unit formats reject during numeric label generation.
Public property admission and the existing formatter/rounding algorithms are
not changed. Disabled, angular and literal paths do not invoke this numeric
alternate formatter. This is not transaction-wide adoption/Update rollback.

## Verification

The final 345-case harness covers all eight dimension builders, enabled and
disabled overrides, inherited values with unrelated overrides, all six numeric
formats, stacked fractions, two explicit cultures, independent rounding,
negative scale owner contexts, repeated placeholders, literal/suppressed text,
clones, overall scale inheritance, and invalid/overflowing numeric inputs.
Its 282 wire cases span six supported typed profiles, text/binary source and
both output formats, modelspace, paper space and referenced blocks. Arc-length
dimensions retain their existing R2004+ eligibility. Each drawing holds four
variants and is checked across loading and repeated block regeneration.

The required independent corpus contains 846 drawings and 3,384 dimensions.
Expected label values follow fixed source geometry and precision rather than
being copied from netDxf output. The checker compares actual MTEXT values,
base/override settings, ownership and graph integrity. It rejects altered,
missing, duplicated and mistyped packets/labels and incomplete inventories.
The first checker revision decoded modern UTF-8 as cp1252; selecting encoding
by DXF version corrected that test reader, without changing expected labels or
production output. The final unchanged test assembly is also executed against
the actual preceding library, separately from the changed library. Executed
results and source/assembly hashes are recorded in the PR evidence.

## Limits

This implements inline alternate **numeric strings**, not full alternate or
tolerance/limit typography, native font metrics, fit/leader routing, automatic
alternate placement on a second line, private-cache regeneration or viewport
and instance-dependent rendering. It retains existing fraction and rounding
algorithms; their exhaustive floating-point/native-format equivalence is not
newly qualified. Synthetic independent-reader checks are not native AutoCAD
open/AUDIT/save/reopen or visual acceptance. Historical typed dialects, pre-R11,
dependency-complete imports, general version conversion and full AutoCAD parity
remain separate work. No public signature, version gate or DXF codec changes.

Primary references:
- [Autodesk DIMALT](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-D3B737AB-53B7-431E-A794-746DD2EB6209.htm)
- [Autodesk DIMAPOST example](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Core/files/GUID-AEA9448D-CC01-404B-AFA0-055B9C2F21EE.htm)
- [Autodesk DIMALTF](https://help.autodesk.com/cloudhelp/2018/ENU/AutoCAD-Core/files/GUID-4BB25870-8BFF-478D-8C04-88457311AAF2.htm)
- [Autodesk DIMALTRND](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-Core/files/GUID-B67093DA-6B0D-4E32-8A33-6298A770CAAF.htm)
