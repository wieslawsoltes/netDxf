# Dimension tolerance and limit labels

The shared dimension-block formatter now generates symmetrical plus/minus,
stacked signed deviation and stacked upper/lower limit labels. It covers linear,
aligned, radial, diametric, ordinate, arc-length and both angular dimensions.
Primary and alternate-unit tolerances use their independent precision and zero
suppression. All fourteen tolerance overrides are applied to an independent copy
of the inherited tolerance settings; unrelated overrides no longer discard them.

Upper deviation uses its stored sign; lower deviation uses the opposite sign of
DIMTM. Exact zero has no extra sign. Equal deviation bounds use the symmetrical
form. The public Symmetrical choice uses the absolute upper bound, as documented
by that property. A deliberately inconsistent Symmetrical definition with unequal
bounds still has the existing DXF enum/flag ambiguity described in the fidelity
contract; this formatter does not change its serializer.

Limits use the scaled, unrounded measurement plus the upper bound and minus the
lower bound. Bounds themselves do not use DIMLFAC or DIMRND. Alternate limits use
the alternate measurement, and alternate bounds use DIMALTF. Neither is computed
from the already rounded nominal string. Numeric products and sums are binary64;
number formatting reuses the qualified unit-format engine. Selected nonfinite
values/results, invalid modes/alignment/height, and precision outside 0–8 reject.
Inactive tolerances and literal or suppressed labels do not evaluate unused
bounds. There is no new whole-document rollback guarantee.

Angular bounds are in the selected DIMAUNIT: degrees for decimal/DMS, gradians
for gradian display and radians for radian display. The existing angular-surveyor
fallback to decimal degrees is retained. Angular dimensions do not gain alternate
units. Precision and zero suppression use tolerance settings, not nominal angle
settings. WindowsDesktop uses the current culture's decimal separator while
DIMTDEC/DIMALTTD continue to determine tolerance precision.

## MTEXT and integration

Tolerance alignment maps Bottom/Middle/Top to scoped `\\A0/1/2` controls. Relative
height uses a scoped `\\H` control and the exact stored DIMTFAC spelling. Limits
and unequal deviations use `\\Supper^ lower;` without a dividing rule. Fractions
inside tolerance rows are linear fractions rather than nested stacks; delimiter
characters are escaped so a slash inside a fractional number is not mistaken
for the upper/lower separator. An independent MTEXT parser checks these rows.

Primary affixes surround the complete numeric/tolerance expression; alternate
affixes surround the corresponding expression inside brackets. User placeholders
include the complete expression on every replacement, and literals/suppression
retain their existing behavior. Dimension and style objects are not mutated by
formatting. Numeric labels now carry the tolerance information, but native font
metrics, arrow/text fit, collision-free layout, annotative scales, native line
spacing and pixel/print equivalence are **not** established by these strings.

## Tests and boundaries

Focused tests cover all dimension families, modes, inherited and explicit
settings, every tolerance override, signed bounds, alignment, all six linear and
five angular formats, primary/alternate precision, zero suppression, independent
rounding, invalid inputs and source isolation. The wire matrix covers six existing
typed profiles (with ARC_DIMENSION's existing gate), both source/output transports,
model/paper space and referenced/unreferenced blocks. Actual executed counts are
recorded in the PR, not inferred from source inspection.

```sh
DXF_TEST_FILTER=tolerance-label/ dotnet run --project tests/netDxf.Conformance -c Release
python tools/verify_tolerance_labels.py artifacts/conformance
```

Autodesk's [DIMTP description](https://help.autodesk.com/view/ACDLT/2026/ENU/?caas=caas%2Fdocumentation%2FACDLT%2F2014%2FENU%2Ffiles%2FGUID-635300D6-9738-44C4-A0B6-176F194533B3-htm.html)
and [tolerance settings](https://help.autodesk.com/view/ACDLT/2026/ENU/?caas=caas%2Fdocumentation%2FACDLT%2F2014%2FENU%2Ffiles%2FGUID-BE52ACEE-A046-412D-AEC8-593B9DEB674D-htm.html)
describe bounds, precision, height and alignment. The generated MTEXT structure
and unit-format rounding remain explicit library contracts, not native rendering
certification. No new historical typed dialect/pre-R11 format, complete private
FIELD/TABLE/cache regeneration, dependency-complete import, general version
conversion or actual AutoCAD open/AUDIT/save/reopen is supplied by this task.
