# Tolerance labels with sparse-override compatibility

The shared dimension-block formatter generates symmetric tolerances, signed
upper/lower deviations, and upper/lower limits. All eight implemented dimension
families use the same path. Primary and enabled alternate values use their own
precision and zero suppression. Alternate conversion applies to the measurement
and allowances, not an already rounded primary label. DIMLFAC scales the nominal
linear measurement, not DIMTP/DIMTM. Limits use the unrounded scaled measurement;
DIMRND and DIMALTRND remain nominal-number settings.

Angular allowances are already expressed in the selected DIMAUNIT. For gradian
and radian limits, the measured degrees are converted first, then the allowances
are added/subtracted in those units. Angular values do not use DIMLFAC or alternate
linear units. Existing public rejection of SurveyorUnits is unchanged. The six
linear formatters are reused, including the current-culture decimal separator
with explicit tolerance precision for WindowsDesktop.

The effective style clones tolerance settings and applies all fourteen existing
overrides. Unrelated overrides retain inherited settings. Generation does not
mutate source styles or dictionaries. Explicit literal text and single-space
suppression bypass tolerance generation; each measurement placeholder receives
the complete scoped expression. Invalid/nonfinite active calculations reject;
Build, Update and document adoption are not made transactional by this change.

## MTEXT grammar

Deviation/limit stacks use `\Supper^ lower;`, including the space after the caret
required to prevent caret decoding. Alignment and relative height are scoped
with groups. Fractional rows use unstacked numbers and escape their slash so it
is not mistaken for the outer tolerance separator. An independent MTEXT parser
checks the actual decoded numerator, denominator, separator and relative height;
matching string literals alone is not the acceptance criterion.

## Symmetry and minimal native projection

The existing public Symmetrical mode uses UpperLimit, even when an inactive
LowerLimit differs. Header/table output therefore writes the effective lower
allowance. When the two numbers are already equal, their original lower bit
pattern is retained, including negative zero. Source properties are not changed.

DSTYLE output preserves sparse groups whenever the inherited native component
already expresses the effective public setting. A missing lower component is
added only when needed, for example an upper-only change of a symmetric value,
or switching from a symmetric base to deviations with an inactive different
base lower value. Explicit lower entries remain explicit, but an inactive lower
in Symmetrical mode writes the effective symmetric allowance. This preserves the
merged #185 sparse-deviation contract instead of unconditionally materializing
all method/upper/lower fields. Loaded presence can change for the cases that
require an extra native component; the source dictionary never changes.

Foreign unequal bounds on a symmetric base, with no method flags, imply a native
deviation. The reader adds that inferred method but does not fabricate either
absent numeric component. General partial/malformed DIMTOL/DIMLIM combinations
and complete native flag precedence are outside this increment. Equal-bound
Deviation and Symmetrical cannot be distinguished in the native fields.

## Verification and integration

The final focused harness defines 464 cases: 380 label cases and 84 symmetric
projection/foreign-input cases. The 282 main wire cases each contain eight
families/variants across six typed versions, both transports and three placements.
They generate 846 drawings. Another 72 symmetric projection cases generate 216
drawings; twelve independently patched foreign-input cases do not add exported
fixtures. ARC_DIMENSION retains the existing R2004+ gate.

The independent checker requires all 1,062 drawings and 6,984 dimension records.
It checks complete physical style and override packets, source bit values,
independent DXF loads, MTEXT tokens, owners, labels, anchors and database audits.
Changed/missing/duplicate/mistyped packets, erased caret spaces and missing/extra
inventories reject. The installed-package consumer exercises generation and
regeneration in its existing twelve version/transport scenarios.

The first candidate passed its C# string assertions but failed an independent
MTEXT-token probe because the caret space was missing. Production output and the
new expected strings were corrected to the documented grammar; token-level
assertions now prevent that defect from returning. An initial new test assigned
SurveyorUnits, which the existing public API rejects; its final case verifies
that existing rejection. These were new-fixture corrections, not changes to any
preceding baseline assertion. Executed counts and incomplete attempts are
recorded separately in the PR evidence, not inferred from these definitions.

A concurrent draft, PR #187, proposes unconditional complete tolerance-group
materialization from an older baseline. Its source is retained separately. This
increment is based on merged #185/#186 and retains every preceding test and
verifier. The two public presence contracts must not be treated as equivalent.

## Boundaries and references

These tests qualify numeric text and MTEXT grammar, not native AutoCAD font
metrics, bounding boxes, automatic fitting, leader/collision choices, alternate
line placement or every nested-fraction presentation. No historical typed
dialect/pre-R11 support, private FIELD/TABLE/cache regeneration, dependency-complete
import, general version conversion or AutoCAD open/AUDIT/save/reopen qualification
is added. Full all-version parity is not established.

- [Autodesk DIMLFAC](https://help.autodesk.com/cloudhelp/2023/ENU/AutoCAD-LT/files/GUID-B6063785-B199-4A9A-8BD7-2108EB0AB7ED.htm): nominal scaling excludes tolerance allowances.
- [Autodesk tolerance settings](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-LT/files/GUID-BE52ACEE-A046-412D-AEC8-593B9DEB674D.htm): methods and selected settings.
- [ezdxf MTEXT internals](https://ezdxf.readthedocs.io/en/stable/dxfinternals/entities/mtext.html): mandatory caret space and stack grammar.
- [Independent angular tolerance implementation](https://github.com/mozman/ezdxf/blob/v1.4.4/src/ezdxf/render/dim_curved.py): allowance units, not native renderer proof.
