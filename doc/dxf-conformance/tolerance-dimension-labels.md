# Tolerance and limit labels with coherent native settings

## Rendering

The existing dimension-block builders now use a shared tolerance formatter.
Symmetric tolerances append a plus/minus value; deviations append upper/lower
signed values in an MTEXT stack; limits replace the nominal measurement with
upper and lower values. Alignment and text-height changes are scoped to the
expression so repeated placeholders and surrounding literal text do not inherit
them. All eight implemented dimension families use the same path, including
angular dimensions and the existing R2004+ arc-length family.

Primary and alternate tolerances use their own precision and zero-suppression
settings. Linear formats reuse the existing unit formatters; angular tolerances
use the angular formatter. WindowsDesktop tolerances use the current decimal
separator with explicit tolerance precision. Fractions inside a deviation or
limit stack stay inline instead of nesting an MTEXT stack. Default R/diameter
prefixes and explicit affixes are retained; limit affixes surround the complete
limit expression. Alternate expressions remain in the existing inline brackets.

DIMLFAC resolves the nominal linear measurement, not DIMTP or DIMTM. Limits use
that unrounded measurement plus the upper allowance and minus the lower
allowance. DIMRND is not reused to round either allowance. Alternate conversion
applies its multiplier to the measurement and the allowances independently of
the primary rounded label. The sign of DIMTM is reversed for the lower displayed
deviation. Equal native allowances render as a symmetric tolerance. Disabled
settings leave the existing nominal path unchanged; literal text and exactly one
space retain their literal/suppression behavior.

The effective-style copy now clones the tolerance value object and applies all
fourteen tolerance overrides. Base settings survive unrelated overrides. Neither
formatting nor serialization changes the authored style or its sparse override
dictionary. Invalid/nonfinite generated values and invalid active display or
alignment settings reject rather than emitting nonfinite labels. Existing public
property admission rules remain; this is not transaction-wide rollback for Build,
Update, adoption or Save.

## Native settings and intentional materialization

DIMTOL, DIMLIM, DIMTP and DIMTM jointly describe the public display method and
allowances. Header/table writing uses UpperLimit for both native allowances in
Symmetrical mode, since a distinct inactive LowerLimit cannot describe a native
symmetric pair. The authored inactive property is not mutated. Explicit signed
zero and subnormal lower allowances are retained in deviation mode; the header
writer no longer substitutes MathHelper.Epsilon for zero. Mode decoding uses
exact scalar equality rather than geometric tolerance, and enabled DIMTOL takes
precedence over DIMLIM.

If any of the three public components is overridden, the DSTYLE writer emits the
complete effective four-field group, deriving unselected components from the
base style. If any native component is present on input, the reader combines it
with inherited native settings and materializes the complete method/upper/lower
triple. Absent groups remain absent. This intentionally changes loaded override
counts and future inheritance: a stored complete group is no longer partly
inherited. Equal-value Deviation and Symmetrical are indistinguishable in these
native fields and load as Symmetrical. A symmetric authored inactive LowerLimit
loads as the effective UpperLimit, not its unused authored value.

This compatibility behavior is explicit rather than silently introducing epsilon
or relying on a same-library incomplete-packet round trip. Existing public
signatures, version gates, other composite codecs and geometry algorithms remain.

## Verification

The focused suite has 1,084 cases: 192 all-family direct formatter/clone tests,
282 version/transport/placement cases, 23 scalar/alignment/rounding/culture/invalid
cases, 576 independently authored sparse-packet cases, and 11 format/eligibility
cases. The SurveyorUnits case asserts the existing public angular rejection; an
initial fixture incorrectly tried to assign that disallowed value, and was
corrected without changing production admission or any baseline assertion.

Every wire case produces a source and both output transports, totaling 846
required drawings and 10,152 independently loaded dimensions. They cover all six
existing typed profiles, modelspace, paper space, referenced blocks and twelve
base/override/mode combinations. The 576 raw-packet cases separately cover both
DIMENSION and LEADER with six sparse packet variants and four base methods; they
are not included in the exported positive drawing count. Tests check signed-zero
bits, inherited components, clone isolation, repeated Update, literal/placeholder
behavior, handles, following geometry and object validation.

The independent checker derives its expected nominal/limit numbers from source
geometry and declared precision, not captured netDxf output. It verifies physical
native fields, complete override presence, MTEXT expression content/ownership,
independent ezdxf reads, database audit, and the entire required file inventory.
Actual-packet mutations and missing/extra inventories must reject. The installed
package consumer also regenerates and round-trips a symmetric tolerance against
an inactive different lower allowance. Final execution counts, old/new results,
source hashes and hosted qualification belong to the PR, not this test definition.

## Boundaries and primary references

These changes produce numeric strings and valid scoped MTEXT structure, not a
native font/visual/automatic fitting qualification. Exact native tolerance spacing,
text-box sizing, leader/collision decisions, alternate-line placement and every
fraction/unit combination in native applications are not certified. No new
historical typed dialect or pre-R11 support, private FIELD/TABLE/cache regeneration,
dependency-complete imports, general version conversion, transaction-wide rollback,
or AutoCAD open/AUDIT/save/reopen evidence is introduced.

- [Autodesk DIMLFAC](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-B6063785-B199-4A9A-8BD7-2108EB0AB7ED.htm): linear measurement scale does not apply to DIMRND, DIMTM or DIMTP.
- [Autodesk DIMTM](https://help.autodesk.com/cloudhelp/2017/ENU/AutoCAD-LT/files/GUID-E2AAD7FB-C563-42A8-B7B4-3A3EC8AA8C68.htm): signed lower allowance and symmetric/deviation behavior.
- [Autodesk DIMTFAC](https://help.autodesk.com/cloudhelp/2019/ENU/AutoCAD-LT/files/GUID-0D3CBEEB-CF89-4979-BE42-A123357188DE.htm): relative fraction/tolerance height.
- [Independent ezdxf tolerance renderer](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/render/dim_base.py): independent MTEXT convention reference, not native AutoCAD proof.
